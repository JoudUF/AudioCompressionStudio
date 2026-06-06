using System;
using System.IO;
using NAudio.Wave;

namespace AudioCompressionApp
{
    public class CompressionMetrics
    {
        public long OriginalFileBytes { get; set; }
        public long UncompressedPcmBytes { get; set; }
        public long CompressedBytes { get; set; }
        public long ReconstructedBytes { get; set; }

        public double SavingsVsOriginalPercent =>
            OriginalFileBytes > 0
                ? (1.0 - (double)CompressedBytes / OriginalFileBytes) * 100.0
                : 0;

        public double SavingsVsPcmPercent =>
            UncompressedPcmBytes > 0
                ? (1.0 - (double)CompressedBytes / UncompressedPcmBytes) * 100.0
                : 0;

        public static CompressionMetrics Create(
            string sourcePath,
            string compressedPath,
            CompressionSettings settings = null,
            string reconstructedPath = null)
        {
            long originalBytes = new FileInfo(sourcePath).Length;
            long pcmBytes = GetUncompressedPcmBytes(sourcePath, settings);
            long compressedBytes = File.Exists(compressedPath)
                ? new FileInfo(compressedPath).Length
                : 0;

            long reconstructedBytes = 0;
            if (!string.IsNullOrEmpty(reconstructedPath) && File.Exists(reconstructedPath))
                reconstructedBytes = new FileInfo(reconstructedPath).Length;

            return new CompressionMetrics
            {
                OriginalFileBytes = originalBytes,
                UncompressedPcmBytes = pcmBytes,
                CompressedBytes = compressedBytes,
                ReconstructedBytes = reconstructedBytes
            };
        }

        public static long GetUncompressedPcmBytes(
            string path,
            CompressionSettings settings = null)
        {
            using var reader = new AudioFileReader(path);
            long bytes = reader.Length;

            int targetRate = settings?.TargetSampleRate ?? 0;
            if (targetRate > 0 && targetRate != reader.WaveFormat.SampleRate)
            {
                bytes = (long)(bytes * (double)targetRate / reader.WaveFormat.SampleRate);
            }

            return bytes;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F2} MB";

            return $"{bytes / 1024.0:F2} KB";
        }

        public static string FormatChangePercent(double percent)
        {
            if (percent >= 0)
                return $"{percent:F1}% saved";

            return $"{Math.Abs(percent):F1}% larger (expansion)";
        }

        public static int GetCompressedBitDepth(CompressionAlgorithm algorithm) =>
            algorithm is CompressionAlgorithm.DeltaModulation
                or CompressionAlgorithm.AdaptiveDeltaModulation
                ? 1
                : 8;

        public string BuildCompressionReport(
            string algorithmName,
            int sampleRate,
            int bitDepth,
            int quantizationLevels,
            long timeMs)
        {
            return
                "Compression Complete!\n\n" +
                $"Algorithm:    {algorithmName}\n" +
                $"Sample Rate:  {sampleRate} Hz\n" +
                $"Bit Depth:    {bitDepth}-bit\n" +
                $"Q-Levels:     {quantizationLevels}\n\n" +
                $"Original Size:  {FormatBytes(OriginalFileBytes)}\n" +
                $"Compressed Size: {FormatBytes(CompressedBytes)}\n" +
                $"vs Original:    {FormatChangePercent(SavingsVsOriginalPercent)}\n\n" +
                $"Time Taken: {timeMs} ms";
        }

        public string BuildDecompressionReport(long timeMs)
        {
            return
                "Decompression Complete!\n\n" +
                $"Compressed Size:        {FormatBytes(CompressedBytes)}\n" +
                $"Reconstructed WAV Size: {FormatBytes(ReconstructedBytes)}\n" +
                $"Time Taken: {timeMs} ms";
        }
    }
}
