using System;
using System.Collections.Generic;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace Multimedia
{
    /// <summary>RGB ↔ all color spaces used in PixelLab (matches LabForm conversions).</summary>
    public static class ColorConversionUtility
    {
        public static Dictionary<string, double[]> ComputeAllFromBgr(byte b, byte g, byte r)
        {
            var values = new Dictionary<string, double[]>();
            double rN = r / 255.0, gN = g / 255.0, bN = b / 255.0;
            double max = Math.Max(rN, Math.Max(gN, bN));
            double min = Math.Min(rN, Math.Min(gN, bN));
            double delta = max - min;

            values["RGB"] = new[] { (double)r, g, b };

            double h = 0, s = 0, v = max * 100.0;
            if (delta > 0)
            {
                s = (delta / max) * 100.0;
                if (max == rN) h = 60 * (((gN - bN) / delta) % 6);
                else if (max == gN) h = 60 * (((bN - rN) / delta) + 2);
                else h = 60 * (((rN - gN) / delta) + 4);
                if (h < 0) h += 360;
            }
            values["HSV"] = new[] { h, s, v };

            double yVal = 0.299 * r + 0.587 * g + 0.114 * b;
            double u = 128 - 0.168736 * r - 0.331264 * g + 0.5 * b;
            double vComp = 128 + 0.5 * r - 0.418688 * g - 0.081312 * b;
            values["YUV"] = new[] { yVal, u, vComp };

            double fx = rN > 0.04045 ? Math.Pow((rN + 0.055) / 1.055, 2.4) : rN / 12.92;
            double fy = gN > 0.04045 ? Math.Pow((gN + 0.055) / 1.055, 2.4) : gN / 12.92;
            double fz = bN > 0.04045 ? Math.Pow((bN + 0.055) / 1.055, 2.4) : bN / 12.92;
            double X = (fx * 0.4124 + fy * 0.3576 + fz * 0.1805) / 0.9505;
            double Y = (fx * 0.2126 + fy * 0.7152 + fz * 0.0722) / 1.0000;
            double Z = (fx * 0.0193 + fy * 0.1192 + fz * 0.9505) / 1.0890;
            double fX = X > 0.008856 ? Math.Pow(X, 1.0 / 3.0) : 7.787 * X + 16.0 / 116.0;
            double fY = Y > 0.008856 ? Math.Pow(Y, 1.0 / 3.0) : 7.787 * Y + 16.0 / 116.0;
            double fZ = Z > 0.008856 ? Math.Pow(Z, 1.0 / 3.0) : 7.787 * Z + 16.0 / 116.0;
            values["LAB"] = new[] { 116 * fY - 16, 500 * (fX - fY), 200 * (fY - fZ) };

            double cb = 128 - 0.168736 * r - 0.331264 * g + 0.5 * b;
            double cr = 128 + 0.5 * r - 0.418688 * g - 0.081312 * b;
            values["YCbCr"] = new[] { yVal, cb, cr };

            double k = 1.0 - max;
            double c = k == 1.0 ? 0 : (1.0 - rN - k) / (1.0 - k) * 100;
            double m = k == 1.0 ? 0 : (1.0 - gN - k) / (1.0 - k) * 100;
            double yC = k == 1.0 ? 0 : (1.0 - bN - k) / (1.0 - k) * 100;
            values["CMYK"] = new[] { c, m, yC, k * 100 };

            return values;
        }

        public static (byte b, byte g, byte r) BgrFromHsv(double hueDeg, double satPct, double valPct)
        {
            byte h = (byte)Math.Clamp(hueDeg / 2.0, 0, 180);
            byte s = (byte)Math.Clamp(satPct / 100.0 * 255, 0, 255);
            byte v = (byte)Math.Clamp(valPct / 100.0 * 255, 0, 255);
            using (var src = new Image<Hsv, byte>(1, 1))
            using (var dst = new Image<Bgr, byte>(1, 1))
            {
                src[0, 0] = new Hsv(h, s, v);
                CvInvoke.CvtColor(src, dst, ColorConversion.Hsv2Bgr);
                var p = dst[0, 0];
                return ((byte)p.Blue, (byte)p.Green, (byte)p.Red);
            }
        }

        public static (byte b, byte g, byte r) BgrFromYuv(double y, double u, double v)
        {
            using (var src = new Image<Bgr, byte>(1, 1))
            using (var dst = new Image<Bgr, byte>(1, 1))
            {
                src[0, 0] = new Bgr(
                    (byte)Math.Clamp(y, 0, 255),
                    (byte)Math.Clamp(u, 0, 255),
                    (byte)Math.Clamp(v, 0, 255));
                CvInvoke.CvtColor(src, dst, ColorConversion.Yuv2Bgr);
                var p = dst[0, 0];
                return ((byte)p.Blue, (byte)p.Green, (byte)p.Red);
            }
        }

        public static (byte b, byte g, byte r) BgrFromLab(double lStar, double aStar, double bStar)
        {
            byte l = (byte)Math.Clamp(lStar * 255.0 / 100.0, 0, 255);
            byte a = (byte)Math.Clamp(aStar + 128, 0, 255);
            byte bCh = (byte)Math.Clamp(bStar + 128, 0, 255);
            using (var src = new Image<Lab, byte>(1, 1))
            using (var dst = new Image<Bgr, byte>(1, 1))
            {
                src[0, 0] = new Lab(l, a, bCh);
                CvInvoke.CvtColor(src, dst, ColorConversion.Lab2Bgr);
                var p = dst[0, 0];
                return ((byte)p.Blue, (byte)p.Green, (byte)p.Red);
            }
        }

        public static (byte b, byte g, byte r) BgrFromYCbCr(double y, double cb, double cr)
        {
            using (var src = new Image<Ycc, byte>(1, 1))
            using (var dst = new Image<Bgr, byte>(1, 1))
            {
                src[0, 0] = new Ycc(
                    (byte)Math.Clamp(y, 0, 255),
                    (byte)Math.Clamp(cb, 0, 255),
                    (byte)Math.Clamp(cr, 0, 255));
                CvInvoke.CvtColor(src, dst, ColorConversion.YCrCb2Bgr);
                var p = dst[0, 0];
                return ((byte)p.Blue, (byte)p.Green, (byte)p.Red);
            }
        }

        public static (byte b, byte g, byte r) BgrFromCmyk(double cPct, double mPct, double yPct, double kPct)
        {
            double c = cPct / 100.0, m = mPct / 100.0, y = yPct / 100.0, k = kPct / 100.0;
            int r = (int)Math.Round((1.0 - c) * (1.0 - k) * 255.0);
            int g = (int)Math.Round((1.0 - m) * (1.0 - k) * 255.0);
            int b = (int)Math.Round((1.0 - y) * (1.0 - k) * 255.0);
            return ((byte)Math.Clamp(b, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(r, 0, 255));
        }

        public static string FormatAllValues(Dictionary<string, double[]> v)
        {
            if (v == null) return "";
            return
                $"RGB ({v["RGB"][0]:F0}, {v["RGB"][1]:F0}, {v["RGB"][2]:F0})  |  " +
                $"HSV ({v["HSV"][0]:F1}°, {v["HSV"][1]:F1}%, {v["HSV"][2]:F1}%)  |  " +
                $"YUV ({v["YUV"][0]:F1}, {v["YUV"][1]:F1}, {v["YUV"][2]:F1})  |  " +
                $"LAB ({v["LAB"][0]:F1}, {v["LAB"][1]:F1}, {v["LAB"][2]:F1})  |  " +
                $"YCbCr ({v["YCbCr"][0]:F1}, {v["YCbCr"][1]:F1}, {v["YCbCr"][2]:F1})  |  " +
                $"CMYK ({v["CMYK"][0]:F1}%, {v["CMYK"][1]:F1}%, {v["CMYK"][2]:F1}%, {v["CMYK"][3]:F1}%)";
        }

    }
}
