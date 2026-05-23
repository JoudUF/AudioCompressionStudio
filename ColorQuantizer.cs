using System;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.Structure;

namespace Multimedia
{
    /// <summary>
    /// Reduces an image to K palette colors (k-means in BGR), similar to an 8-bit indexed image with a small palette.
    /// </summary>
    public static class ColorQuantizer
    {
        public sealed class QuantizeResult : IDisposable
        {
            public Mat Image { get; }
            public Color[] Palette { get; }

            public QuantizeResult(Mat image, Color[] palette)
            {
                Image = image;
                Palette = palette;
            }

            public void Dispose() => Image?.Dispose();
        }

        /// <param name="bgrSource">BGR 8-bit image.</param>
        /// <param name="colorCount">Palette size (e.g. 4 unique colors for the whole image).</param>
        public static QuantizeResult QuantizePalette(Mat bgrSource, int colorCount)
        {
            if (bgrSource == null || bgrSource.IsEmpty)
                throw new ArgumentException("Image is empty.", nameof(bgrSource));

            colorCount = Math.Clamp(colorCount, 2, 256);

            using (var src = bgrSource.ToImage<Bgr, byte>())
            {
                int width = src.Width;
                int height = src.Height;
                int sampleStep = Math.Max(1, (int)Math.Sqrt((width * height) / 10000.0));

                var samples = new System.Collections.Generic.List<(float b, float g, float r)>();
                for (int y = 0; y < height; y += sampleStep)
                {
                    for (int x = 0; x < width; x += sampleStep)
                    {
                        var p = src[y, x];
                        samples.Add(((float)p.Blue, (float)p.Green, (float)p.Red));
                    }
                }

                int k = Math.Min(colorCount, samples.Count);
                var centers = KMeansBgr(samples, k, iterations: 20);

                var palette = new Color[k];
                var centerBgr = new Bgr[k];
                for (int i = 0; i < k; i++)
                {
                    byte bb = (byte)Math.Clamp((int)Math.Round(centers[i].b), 0, 255);
                    byte gg = (byte)Math.Clamp((int)Math.Round(centers[i].g), 0, 255);
                    byte rr = (byte)Math.Clamp((int)Math.Round(centers[i].r), 0, 255);
                    centerBgr[i] = new Bgr(bb, gg, rr);
                    palette[i] = Color.FromArgb(rr, gg, bb);
                }

                var output = new Image<Bgr, byte>(width, height);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var p = src[y, x];
                        int best = 0;
                        double bestDist = double.MaxValue;
                        for (int i = 0; i < k; i++)
                        {
                            double db = p.Blue - centerBgr[i].Blue;
                            double dg = p.Green - centerBgr[i].Green;
                            double dr = p.Red - centerBgr[i].Red;
                            double dist = db * db + dg * dg + dr * dr;
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                best = i;
                            }
                        }
                        output[y, x] = centerBgr[best];
                    }
                }

                return new QuantizeResult(output.Mat.Clone(), palette);
            }
        }

        private static (float b, float g, float r)[] KMeansBgr(
            System.Collections.Generic.List<(float b, float g, float r)> samples,
            int k,
            int iterations)
        {
            var rng = new Random(42);
            var centers = new (float b, float g, float r)[k];
            for (int i = 0; i < k; i++)
                centers[i] = samples[rng.Next(samples.Count)];

            var counts = new int[k];
            var sumB = new float[k];
            var sumG = new float[k];
            var sumR = new float[k];

            for (int iter = 0; iter < iterations; iter++)
            {
                Array.Clear(counts, 0, k);
                Array.Clear(sumB, 0, k);
                Array.Clear(sumG, 0, k);
                Array.Clear(sumR, 0, k);

                foreach (var s in samples)
                {
                    int best = 0;
                    double bestDist = double.MaxValue;
                    for (int i = 0; i < k; i++)
                    {
                        double db = s.b - centers[i].b;
                        double dg = s.g - centers[i].g;
                        double dr = s.r - centers[i].r;
                        double dist = db * db + dg * dg + dr * dr;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = i;
                        }
                    }
                    counts[best]++;
                    sumB[best] += s.b;
                    sumG[best] += s.g;
                    sumR[best] += s.r;
                }

                for (int i = 0; i < k; i++)
                {
                    if (counts[i] == 0)
                    {
                        centers[i] = samples[rng.Next(samples.Count)];
                        continue;
                    }
                    centers[i] = (sumB[i] / counts[i], sumG[i] / counts[i], sumR[i] / counts[i]);
                }
            }

            return centers;
        }
    }
}
