using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AudioCompressionApp
{
    public class CompressionHistoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string SourceFileName { get; set; } = "";

        public string SourceFilePath { get; set; } = "";

        public string Algorithm { get; set; } = "";

        public long OriginalFileBytes { get; set; }

        public long UncompressedPcmBytes { get; set; }

        public long CompressedBytes { get; set; }

        public long ReconstructedBytes { get; set; }

        public string CompressedPath { get; set; } = "";

        public string ReconstructedPath { get; set; } = "";

        public long TimeMs { get; set; }

        public string Status { get; set; } = "OK";

        public double SavingsVsOriginalPercent =>
            OriginalFileBytes > 0
                ? (1.0 - (double)CompressedBytes / OriginalFileBytes) * 100.0
                : 0;

        public double SavingsVsPcmPercent =>
            UncompressedPcmBytes > 0
                ? (1.0 - (double)CompressedBytes / UncompressedPcmBytes) * 100.0
                : 0;

        public static CompressionHistoryEntry FromMetrics(
            CompressionMetrics metrics,
            string sourcePath,
            string algorithm,
            string compressedPath,
            string reconstructedPath,
            long timeMs,
            string status = "OK")
        {
            return new CompressionHistoryEntry
            {
                Timestamp = DateTime.Now,
                SourceFileName = Path.GetFileName(sourcePath),
                SourceFilePath = sourcePath,
                Algorithm = algorithm,
                OriginalFileBytes = metrics.OriginalFileBytes,
                UncompressedPcmBytes = metrics.UncompressedPcmBytes,
                CompressedBytes = metrics.CompressedBytes,
                ReconstructedBytes = metrics.ReconstructedBytes,
                CompressedPath = compressedPath,
                ReconstructedPath = reconstructedPath ?? "",
                TimeMs = timeMs,
                Status = status
            };
        }
    }

    public static class CompressionHistory
    {
        private static readonly string HistoryPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "compression_history.json");

        public static List<CompressionHistoryEntry> Load()
        {
            try
            {
                if (!File.Exists(HistoryPath))
                    return new List<CompressionHistoryEntry>();

                string json = File.ReadAllText(HistoryPath);

                return JsonSerializer.Deserialize<List<CompressionHistoryEntry>>(json)
                    ?? new List<CompressionHistoryEntry>();
            }
            catch
            {
                return new List<CompressionHistoryEntry>();
            }
        }

        public static void Save(List<CompressionHistoryEntry> entries)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(entries, options);
            File.WriteAllText(HistoryPath, json);
        }

        public static void Add(CompressionHistoryEntry entry)
        {
            var entries = Load();
            entries.Insert(0, entry);
            Save(entries);
        }

        public static void UpdateReconstruction(
            string compressedPath,
            string reconstructedPath,
            long reconstructedBytes,
            long additionalTimeMs = 0)
        {
            var entries = Load();
            var match = entries.FirstOrDefault(
                e => string.Equals(
                    e.CompressedPath,
                    compressedPath,
                    StringComparison.OrdinalIgnoreCase));

            if (match == null)
                return;

            match.ReconstructedPath = reconstructedPath;
            match.ReconstructedBytes = reconstructedBytes;
            match.TimeMs += additionalTimeMs;
            match.Status = "Complete";

            Save(entries);
        }

        public static void Clear()
        {
            if (File.Exists(HistoryPath))
                File.Delete(HistoryPath);
        }
    }
}
