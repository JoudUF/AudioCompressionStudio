using System;

namespace AudioCompressionApp
{
    public class CompressionProgress
    {
        public int PercentageComplete { get; set; }
        public double CurrentCompressionRatio { get; set; }
        public double ProcessingSpeed { get; set; }
    }

    public enum CompressionAlgorithm
    {
        NonlinearQuantization = 0,
        DPCM = 1,
        DeltaModulation = 2,
        AdaptiveDeltaModulation = 3,
        PredictiveDifferentialCoding = 4
    }

    public class CompressionSettings
    {
        public int QuantizationLevels { get; set; } = 256;
        public int TargetSampleRate { get; set; } = 44100;
    }

    public static class CompressionProgressHelper
    {
        public static int ClampPercent(int percent) =>
            Math.Clamp(percent, 0, 100);
    }
}
