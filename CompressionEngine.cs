using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioCompressionApp
{
    public class CompressionEngine
    {
        private const float MU = 255f;
        private const int MuLawMax = 0x1FFF;
        private const int MuLawBias = 0x84;
        private const int LossySourceSampleRateCap = 22050;

        private const string MagicLegacy = "PCOMP";
        private const string MagicV2 = "PCMP2";
        private const string MagicV3 = "PCMP3";
        private const string MagicV4 = "PCMP4";
        private const string MagicCurrent = "PCMP5";

        public async Task<long> CompressAudioAsync(
            string inputFilePath,
            string outputFilePath,
            CompressionAlgorithm algorithm,
            CompressionSettings settings,
            IProgress<CompressionProgress> progressReporter,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                using var reader = new AudioFileReader(inputFilePath);

                int sourceRate = reader.WaveFormat.SampleRate;
                int sourceChannels = reader.WaveFormat.Channels;
                int requestedRate = settings?.TargetSampleRate > 0
                    ? settings.TargetSampleRate
                    : sourceRate;

                var path = ResolveCompressionPath(
                    inputFilePath,
                    sourceRate,
                    requestedRate,
                    sourceChannels);

                ISampleProvider sampleProvider = reader;

                if (path.TargetRate != sourceRate)
                {
                    sampleProvider = new WdlResamplingSampleProvider(
                        reader,
                        path.TargetRate);
                }

                int qLevels = Math.Clamp(settings?.QuantizationLevels ?? 256, 2, 65536);

                using var writer = new BinaryWriter(
                    File.Open(outputFilePath, FileMode.Create));

                var headerState = new CompressionHeader
                {
                    Algorithm = algorithm,
                    SampleRate = path.TargetRate,
                    Channels = path.OutputChannels,
                    QuantizationLevels = qLevels,
                    InitialStepSize = GetDefaultStepSize(path.TargetRate, qLevels)
                };

                long sampleCountPosition = WriteHeader(writer, headerState);

                long totalSamples = EstimateTotalSamples(
                    reader,
                    sourceRate,
                    path.TargetRate,
                    sourceChannels,
                    path.OutputChannels);

                long samplesProcessed = 0;

                float[] readBuffer = new float[path.TargetRate * sourceChannels];
                float[] monoBuffer = path.MixToMono
                    ? new float[path.TargetRate]
                    : null;

                var previousSamples = new float[path.OutputChannels];
                var olderSamples = new float[path.OutputChannels];
                var stepSizes = new float[path.OutputChannels];

                float minStep = GetMinStepSize(path.TargetRate, qLevels);
                float maxStep = GetMaxStepSize(path.TargetRate, qLevels);

                for (int ch = 0; ch < path.OutputChannels; ch++)
                    stepSizes[ch] = headerState.InitialStepSize;

                Stopwatch sw = Stopwatch.StartNew();

                ReportProgress(
                    progressReporter,
                    0,
                    sw,
                    samplesProcessed,
                    writer.BaseStream.Length,
                    4);

                bool stepCalibrated = false;

                while (true)
                {
                    int samplesRead = sampleProvider.Read(readBuffer, 0, readBuffer.Length);
                    if (samplesRead <= 0)
                        break;

                    cancellationToken.ThrowIfCancellationRequested();

                    float[] processBuffer;
                    int processCount;
                    int processChannels = path.OutputChannels;

                    if (path.MixToMono)
                    {
                        processCount = MixToMono(
                            readBuffer,
                            samplesRead,
                            sourceChannels,
                            monoBuffer);

                        processBuffer = monoBuffer;
                    }
                    else
                    {
                        processBuffer = readBuffer;
                        processCount = samplesRead;
                    }

                    if (!stepCalibrated && algorithm is CompressionAlgorithm.DeltaModulation
                        or CompressionAlgorithm.AdaptiveDeltaModulation)
                    {
                        CalibrateStepSizes(
                            processBuffer,
                            processCount,
                            processChannels,
                            path.TargetRate,
                            stepSizes,
                            minStep,
                            maxStep);

                        headerState.InitialStepSize = AverageStepSize(
                            stepSizes,
                            processChannels);

                        stepCalibrated = true;
                    }

                    switch (algorithm)
                    {
                        case CompressionAlgorithm.DeltaModulation:
                            samplesProcessed += CompressDeltaModulation(
                                processBuffer,
                                processCount,
                                processChannels,
                                writer,
                                previousSamples,
                                stepSizes,
                                path.TargetRate,
                                qLevels,
                                adaptive: false);
                            break;

                        case CompressionAlgorithm.AdaptiveDeltaModulation:
                            samplesProcessed += CompressDeltaModulation(
                                processBuffer,
                                processCount,
                                processChannels,
                                writer,
                                previousSamples,
                                stepSizes,
                                path.TargetRate,
                                qLevels,
                                adaptive: true);
                            break;

                        default:
                            samplesProcessed += CompressSampleAlgorithms(
                                processBuffer,
                                processCount,
                                processChannels,
                                writer,
                                algorithm,
                                qLevels,
                                previousSamples,
                                olderSamples);
                            break;
                    }

                    int percent = totalSamples > 0
                        ? (int)(samplesProcessed * 100 / totalSamples)
                        : 0;

                    ReportProgress(
                        progressReporter,
                        percent,
                        sw,
                        samplesProcessed,
                        writer.BaseStream.Length,
                        4);
                }

                headerState.TotalSamples = samplesProcessed;
                PatchHeader(writer, sampleCountPosition, headerState);

                ReportProgress(
                    progressReporter,
                    100,
                    sw,
                    samplesProcessed,
                    writer.BaseStream.Length,
                    4);

                sw.Stop();
                return samplesProcessed;
            }, cancellationToken);
        }

        public async Task DecompressAudioAsync(
            string inputFilePath,
            string outputFilePath,
            IProgress<CompressionProgress> progressReporter,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                using var reader = new BinaryReader(
                    File.Open(inputFilePath, FileMode.Open));

                var header = ReadHeader(reader);
                bool useMuLaw = header.UseMuLawEncoding;

                long totalSamples = header.TotalSamples > 0
                    ? header.TotalSamples
                    : EstimateDecompressedSamples(
                        reader.BaseStream.Length - reader.BaseStream.Position,
                        header.Algorithm);

                long samplesProcessed = 0;
                var previousSamples = new float[header.Channels];
                var olderSamples = new float[header.Channels];
                var stepSizes = new float[header.Channels];

                float minStep = GetMinStepSize(header.SampleRate, header.QuantizationLevels);
                float maxStep = GetMaxStepSize(header.SampleRate, header.QuantizationLevels);

                for (int ch = 0; ch < header.Channels; ch++)
                    stepSizes[ch] = header.InitialStepSize;

                var dmState = new DeltaModulationState[header.Channels];
                for (int ch = 0; ch < header.Channels; ch++)
                    dmState[ch] = new DeltaModulationState();

                Stopwatch sw = Stopwatch.StartNew();

                WaveFormat waveFormat = new WaveFormat(
                    header.SampleRate,
                    16,
                    header.Channels);

                using var writer = new WaveFileWriter(outputFilePath, waveFormat);

                ReportDecompressProgress(progressReporter, 0, sw, samplesProcessed);

                while (reader.BaseStream.Position < reader.BaseStream.Length
                    && samplesProcessed < totalSamples)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    switch (header.Algorithm)
                    {
                        case CompressionAlgorithm.NonlinearQuantization:
                        {
                            int ch = (int)(samplesProcessed % header.Channels);
                            previousSamples[ch] = useMuLaw
                                ? DecompressMuLaw(reader, writer)
                                : DecompressNonlinearLegacy(
                                    reader,
                                    writer,
                                    header.QuantizationLevels);
                            samplesProcessed++;
                            break;
                        }

                        case CompressionAlgorithm.DPCM:
                        {
                            int ch = (int)(samplesProcessed % header.Channels);
                            previousSamples[ch] = DecompressDpcm(
                                reader,
                                writer,
                                previousSamples[ch],
                                header.QuantizationLevels);
                            samplesProcessed++;
                            break;
                        }

                        case CompressionAlgorithm.PredictiveDifferentialCoding:
                        {
                            int ch = (int)(samplesProcessed % header.Channels);
                            DecompressPdc(
                                reader,
                                writer,
                                ref previousSamples[ch],
                                ref olderSamples[ch],
                                header.QuantizationLevels);
                            samplesProcessed++;
                            break;
                        }

                        case CompressionAlgorithm.DeltaModulation:
                            samplesProcessed += DecompressDeltaModulation(
                                reader,
                                writer,
                                header.Channels,
                                previousSamples,
                                stepSizes,
                                dmState,
                                header.SampleRate,
                                header.QuantizationLevels,
                                totalSamples,
                                samplesProcessed,
                                adaptive: false);
                            break;

                        case CompressionAlgorithm.AdaptiveDeltaModulation:
                            samplesProcessed += DecompressDeltaModulation(
                                reader,
                                writer,
                                header.Channels,
                                previousSamples,
                                stepSizes,
                                dmState,
                                header.SampleRate,
                                header.QuantizationLevels,
                                totalSamples,
                                samplesProcessed,
                                adaptive: true);
                            break;
                    }

                    if (samplesProcessed % 4096 == 0 || samplesProcessed >= totalSamples)
                    {
                        int percent = totalSamples > 0
                            ? (int)(samplesProcessed * 100 / totalSamples)
                            : 0;

                        ReportDecompressProgress(
                            progressReporter,
                            percent,
                            sw,
                            samplesProcessed);
                    }
                }

                ReportDecompressProgress(progressReporter, 100, sw, samplesProcessed);
                sw.Stop();
            }, cancellationToken);
        }

        private static CompressionPath ResolveCompressionPath(
            string inputPath,
            int sourceRate,
            int requestedRate,
            int sourceChannels)
        {
            if (requestedRate <= 0)
                requestedRate = sourceRate;

            if (!IsLossyContainer(inputPath))
            {
                return new CompressionPath
                {
                    TargetRate = requestedRate,
                    OutputChannels = sourceChannels,
                    MixToMono = false
                };
            }

            int targetRate = Math.Min(requestedRate, LossySourceSampleRateCap);
            bool mixToMono = sourceChannels > 1;

            return new CompressionPath
            {
                TargetRate = targetRate,
                OutputChannels = mixToMono ? 1 : sourceChannels,
                MixToMono = mixToMono
            };
        }

        private static bool IsLossyContainer(string path)
        {
            string ext = Path.GetExtension(path);

            return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".aac", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".wma", StringComparison.OrdinalIgnoreCase);
        }

        private static int MixToMono(
            float[] stereoBuffer,
            int samplesRead,
            int sourceChannels,
            float[] monoBuffer)
        {
            int frameCount = samplesRead / sourceChannels;

            for (int i = 0; i < frameCount; i++)
            {
                float sum = 0f;

                for (int ch = 0; ch < sourceChannels; ch++)
                    sum += stereoBuffer[i * sourceChannels + ch];

                monoBuffer[i] = sum / sourceChannels;
            }

            return frameCount;
        }

        private static float GetMinStepSize(int sampleRate, int qLevels) =>
            Math.Max(1f / sampleRate * 8f, 1f / (qLevels * 8f));

        private static float GetMaxStepSize(int sampleRate, int qLevels) =>
            Math.Max(1f / sampleRate * 120f, 1f / Math.Max(8, qLevels / 4));

        private static float GetDefaultStepSize(int sampleRate, int qLevels) =>
            Math.Max(1f / sampleRate * 40f, 1f / qLevels);

        private static float AverageStepSize(float[] stepSizes, int channels)
        {
            if (channels <= 0)
                return GetDefaultStepSize(44100, 256);

            float sum = 0f;
            for (int ch = 0; ch < channels; ch++)
                sum += stepSizes[ch];

            return sum / channels;
        }

        private static float GetQuantStep(int qLevels) =>
            2f / Math.Max(2, qLevels);

        private static void CalibrateStepSizes(
            float[] buffer,
            int samplesRead,
            int channels,
            int sampleRate,
            float[] stepSizes,
            float minStep,
            float maxStep)
        {
            var peaks = new float[channels];
            var sumSquares = new float[channels];
            var counts = new int[channels];

            for (int i = 0; i < samplesRead; i++)
            {
                int ch = i % channels;
                float sample = buffer[i];
                float abs = Math.Abs(sample);

                peaks[ch] = Math.Max(peaks[ch], abs);
                sumSquares[ch] += sample * sample;
                counts[ch]++;
            }

            for (int ch = 0; ch < channels; ch++)
            {
                float peak = Math.Max(peaks[ch], 0.01f);
                float rms = counts[ch] > 0
                    ? (float)Math.Sqrt(sumSquares[ch] / counts[ch])
                    : peak;

                float stepFromPeak = peak / 128f;
                float stepFromRms = rms * 2f / sampleRate;
                float step = Math.Max(stepFromPeak, stepFromRms);

                stepSizes[ch] = Math.Clamp(step, minStep, maxStep);
            }
        }

        private static long WriteHeader(
            BinaryWriter writer,
            CompressionHeader header)
        {
            writer.Write(MagicCurrent.ToCharArray());
            writer.Write((int)header.Algorithm);
            writer.Write(header.SampleRate);
            writer.Write(header.Channels);
            writer.Write(header.QuantizationLevels);

            long sampleCountPosition = writer.BaseStream.Position;
            writer.Write(0L);
            writer.Write(header.InitialStepSize);

            return sampleCountPosition;
        }

        private static void PatchHeader(
            BinaryWriter writer,
            long sampleCountPosition,
            CompressionHeader header)
        {
            long endPosition = writer.BaseStream.Position;

            writer.Seek((int)sampleCountPosition, SeekOrigin.Begin);
            writer.Write(header.TotalSamples);

            writer.Seek((int)(sampleCountPosition + sizeof(long)), SeekOrigin.Begin);
            writer.Write(header.InitialStepSize);

            writer.Seek((int)endPosition, SeekOrigin.Begin);
        }

        private static CompressionHeader ReadHeader(BinaryReader reader)
        {
            string magic = new string(reader.ReadChars(5));

            if (magic != MagicCurrent
                && magic != MagicV4
                && magic != MagicV3
                && magic != MagicV2
                && magic != MagicLegacy)
            {
                throw new InvalidDataException(
                    "Invalid compression file format.");
            }

            var header = new CompressionHeader
            {
                Algorithm = (CompressionAlgorithm)reader.ReadInt32(),
                SampleRate = reader.ReadInt32(),
                Channels = reader.ReadInt32(),
                QuantizationLevels = 256,
                TotalSamples = 0,
                InitialStepSize = GetDefaultStepSize(44100, 256),
                UseMuLawEncoding = magic == MagicCurrent
            };

            if (magic == MagicLegacy)
                return header;

            header.QuantizationLevels = Math.Clamp(reader.ReadInt32(), 2, 65536);
            header.InitialStepSize = GetDefaultStepSize(
                header.SampleRate,
                header.QuantizationLevels);

            if (magic == MagicV2)
                return header;

            header.TotalSamples = reader.ReadInt64();

            if (magic == MagicV3)
                return header;

            if (magic == MagicV4)
            {
                reader.ReadSingle();
                header.InitialStepSize = reader.ReadSingle();
            }
            else
            {
                header.InitialStepSize = reader.ReadSingle();
            }

            if (header.InitialStepSize <= 0f)
            {
                header.InitialStepSize = GetDefaultStepSize(
                    header.SampleRate,
                    header.QuantizationLevels);
            }

            return header;
        }

        private static long EstimateTotalSamples(
            AudioFileReader reader,
            int sourceRate,
            int targetRate,
            int sourceChannels,
            int outputChannels)
        {
            long interleavedSamples = Math.Max(1, reader.Length / 4);
            long frames = interleavedSamples / Math.Max(1, sourceChannels);

            if (sourceRate <= 0 || targetRate <= 0)
                return Math.Max(1, frames * outputChannels);

            long outputFrames = (long)(frames * (double)targetRate / sourceRate);
            return Math.Max(1, outputFrames * outputChannels);
        }

        private static long EstimateDecompressedSamples(
            long payloadBytes,
            CompressionAlgorithm algorithm)
        {
            return algorithm switch
            {
                CompressionAlgorithm.DeltaModulation =>
                    Math.Max(1, payloadBytes * 8),

                CompressionAlgorithm.AdaptiveDeltaModulation =>
                    Math.Max(1, payloadBytes * 8),

                _ => Math.Max(1, payloadBytes)
            };
        }

        private static int CompressSampleAlgorithms(
            float[] buffer,
            int samplesRead,
            int channels,
            BinaryWriter writer,
            CompressionAlgorithm algorithm,
            int qLevels,
            float[] previousSamples,
            float[] olderSamples)
        {
            float quantStep = GetQuantStep(qLevels);

            for (int i = 0; i < samplesRead; i++)
            {
                int ch = i % channels;
                float currentSample = Math.Clamp(buffer[i], -1f, 1f);

                switch (algorithm)
                {
                    case CompressionAlgorithm.NonlinearQuantization:
                    {
                        short linear = FloatToLinear16(currentSample);
                        writer.Write(LinearToMuLaw(linear));
                        break;
                    }

                    case CompressionAlgorithm.DPCM:
                    {
                        float delta = currentSample - previousSamples[ch];

                        int quantizedDelta = (int)Math.Round(delta / quantStep);
                        quantizedDelta = Math.Clamp(quantizedDelta, -128, 127);
                        writer.Write((sbyte)quantizedDelta);

                        float reconstructed = Math.Clamp(
                            previousSamples[ch] + quantizedDelta * quantStep,
                            -1f,
                            1f);

                        previousSamples[ch] = reconstructed;
                        break;
                    }

                    case CompressionAlgorithm.PredictiveDifferentialCoding:
                    {
                        float predicted = previousSamples[ch];
                        float residual = currentSample - predicted;

                        int quantizedResidual = (int)Math.Round(residual / quantStep);
                        quantizedResidual = Math.Clamp(
                            quantizedResidual,
                            -128,
                            127);

                        writer.Write((sbyte)quantizedResidual);

                        float reconstructed = Math.Clamp(
                            predicted + quantizedResidual * quantStep,
                            -1f,
                            1f);

                        olderSamples[ch] = previousSamples[ch];
                        previousSamples[ch] = reconstructed;
                        break;
                    }
                }
            }

            return samplesRead;
        }

        private static int CompressDeltaModulation(
            float[] buffer,
            int samplesRead,
            int channels,
            BinaryWriter writer,
            float[] previousSamples,
            float[] stepSizes,
            int sampleRate,
            int qLevels,
            bool adaptive)
        {
            int bitIndex = 0;
            byte packedByte = 0;
            var dmState = new DeltaModulationState[channels];

            float minStep = GetMinStepSize(sampleRate, qLevels);
            float maxStep = GetMaxStepSize(sampleRate, qLevels);

            for (int i = 0; i < samplesRead; i++)
            {
                int ch = i % channels;
                float currentSample = Math.Clamp(buffer[i], -1f, 1f);
                byte bit = (byte)(currentSample >= previousSamples[ch] ? 1 : 0);

                if (adaptive)
                    AdaptCvsdStep(
                        ref stepSizes[ch],
                        ref dmState[ch],
                        bit,
                        minStep,
                        maxStep);

                previousSamples[ch] += bit == 1 ? stepSizes[ch] : -stepSizes[ch];

                packedByte |= (byte)(bit << bitIndex);
                bitIndex++;

                if (bitIndex == 8)
                {
                    writer.Write(packedByte);
                    packedByte = 0;
                    bitIndex = 0;
                }
            }

            if (bitIndex > 0)
                writer.Write(packedByte);

            return samplesRead;
        }

        private static void AdaptCvsdStep(
            ref float stepSize,
            ref DeltaModulationState state,
            byte bit,
            float minStep,
            float maxStep)
        {
            if (bit == state.PreviousBit)
            {
                state.SameDirectionCount++;

                if (state.SameDirectionCount >= 4)
                {
                    stepSize = Math.Min(stepSize * 2f, maxStep);
                    state.SameDirectionCount = 0;
                }
            }
            else
            {
                stepSize = Math.Max(stepSize * 0.5f, minStep);
                state.SameDirectionCount = 0;
            }

            state.PreviousBit = bit;
            stepSize = Math.Clamp(stepSize, minStep, maxStep);
        }

        private static float DecompressMuLaw(BinaryReader reader, WaveFileWriter writer)
        {
            byte muLawByte = reader.ReadByte();
            short linear = MuLawToLinear(muLawByte);
            float reconstructed = Linear16ToFloat(linear);

            WritePcm16(writer, reconstructed);
            return reconstructed;
        }

        private static float DecompressNonlinearLegacy(
            BinaryReader reader,
            WaveFileWriter writer,
            int qLevels)
        {
            int maxQuant = Math.Min(127, Math.Max(1, qLevels / 2 - 1));
            sbyte quantized = reader.ReadSByte();

            float normalized = Math.Clamp(
                quantized / (float)maxQuant,
                -1f,
                1f);

            float reconstructed =
                Math.Sign(normalized)
                * (float)(
                    (Math.Pow(1 + MU, Math.Abs(normalized)) - 1)
                    / MU);

            WritePcm16(writer, reconstructed);
            return reconstructed;
        }

        private static float DecompressDpcm(
            BinaryReader reader,
            WaveFileWriter writer,
            float previousSample,
            int qLevels)
        {
            sbyte quantizedDelta = reader.ReadSByte();
            float quantStep = GetQuantStep(qLevels);

            float reconstructed = Math.Clamp(
                previousSample + quantizedDelta * quantStep,
                -1f,
                1f);

            WritePcm16(writer, reconstructed);
            return reconstructed;
        }

        private static void DecompressPdc(
            BinaryReader reader,
            WaveFileWriter writer,
            ref float previousSample,
            ref float olderSample,
            int qLevels)
        {
            sbyte residualQuantized = reader.ReadSByte();
            float quantStep = GetQuantStep(qLevels);

            float prediction = previousSample;
            float reconstructed = Math.Clamp(
                prediction + residualQuantized * quantStep,
                -1f,
                1f);

            olderSample = previousSample;
            previousSample = reconstructed;

            WritePcm16(writer, reconstructed);
        }

        private static int DecompressDeltaModulation(
            BinaryReader reader,
            WaveFileWriter writer,
            int channels,
            float[] previousSamples,
            float[] stepSizes,
            DeltaModulationState[] dmState,
            int sampleRate,
            int qLevels,
            long totalSamples,
            long samplesProcessed,
            bool adaptive)
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length)
                return 0;

            byte packedByte = reader.ReadByte();
            float minStep = GetMinStepSize(sampleRate, qLevels);
            float maxStep = GetMaxStepSize(sampleRate, qLevels);
            int samplesWritten = 0;

            for (int b = 0; b < 8; b++)
            {
                if (samplesProcessed + samplesWritten >= totalSamples)
                    break;

                int ch = (int)((samplesProcessed + samplesWritten) % channels);
                byte bit = (byte)((packedByte >> b) & 1);

                if (adaptive)
                {
                    AdaptCvsdStep(
                        ref stepSizes[ch],
                        ref dmState[ch],
                        bit,
                        minStep,
                        maxStep);
                }

                previousSamples[ch] += bit == 1 ? stepSizes[ch] : -stepSizes[ch];

                WritePcm16(writer, previousSamples[ch]);
                samplesWritten++;
            }

            return samplesWritten;
        }

        private static short FloatToLinear16(float sample)
        {
            sample = Math.Clamp(sample, -1f, 1f);
            return (short)Math.Round(sample * short.MaxValue);
        }

        private static float Linear16ToFloat(short sample) =>
            Math.Clamp(sample / (float)short.MaxValue, -1f, 1f);

        private static byte LinearToMuLaw(short sample)
        {
            int sign = (sample >> 8) & 0x80;

            if (sign != 0)
                sample = (short)-sample;

            if (sample > MuLawMax)
                sample = (short)MuLawMax;

            sample = (short)(sample + MuLawBias);

            int exponent = 7;
            for (int expMask = 0x4000; (sample & expMask) == 0 && exponent > 0; exponent--, expMask >>= 1)
            {
            }

            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        private static short MuLawToLinear(byte muLawByte)
        {
            muLawByte = (byte)~muLawByte;

            int sign = muLawByte & 0x80;
            int exponent = (muLawByte >> 4) & 0x07;
            int mantissa = muLawByte & 0x0F;

            int sample = ((mantissa << 3) + MuLawBias) << exponent;
            sample -= MuLawBias;

            return (short)(sign != 0 ? -sample : sample);
        }

        private static void ReportProgress(
            IProgress<CompressionProgress> progressReporter,
            int percent,
            Stopwatch sw,
            long samplesProcessed,
            long compressedBytes,
            int bytesPerSourceSample)
        {
            if (progressReporter == null)
                return;

            double elapsedMs = Math.Max(1, sw.ElapsedMilliseconds);
            double speed = samplesProcessed / elapsedMs;

            double ratio = compressedBytes > 0
                ? (double)(samplesProcessed * bytesPerSourceSample)
                  / compressedBytes * 100.0
                : 100.0;

            progressReporter.Report(new CompressionProgress
            {
                PercentageComplete = CompressionProgressHelper.ClampPercent(percent),
                ProcessingSpeed = speed,
                CurrentCompressionRatio = ratio
            });
        }

        private static void ReportDecompressProgress(
            IProgress<CompressionProgress> progressReporter,
            int percent,
            Stopwatch sw,
            long samplesProcessed)
        {
            if (progressReporter == null)
                return;

            double elapsedMs = Math.Max(1, sw.ElapsedMilliseconds);

            progressReporter.Report(new CompressionProgress
            {
                PercentageComplete = CompressionProgressHelper.ClampPercent(percent),
                ProcessingSpeed = samplesProcessed / elapsedMs
            });
        }

        private static void WritePcm16(WaveFileWriter writer, float sample)
        {
            sample = Math.Clamp(sample, -1f, 1f);
            short pcm = (short)Math.Round(sample * short.MaxValue);
            writer.WriteSample(pcm);
        }

        private struct CompressionPath
        {
            public int TargetRate;
            public int OutputChannels;
            public bool MixToMono;
        }

        private struct DeltaModulationState
        {
            public int PreviousBit;
            public int SameDirectionCount;
        }

        private class CompressionHeader
        {
            public CompressionAlgorithm Algorithm { get; set; }
            public int SampleRate { get; set; }
            public int Channels { get; set; }
            public int QuantizationLevels { get; set; }
            public long TotalSamples { get; set; }
            public float InitialStepSize { get; set; }
            public bool UseMuLawEncoding { get; set; }
        }
    }
}
