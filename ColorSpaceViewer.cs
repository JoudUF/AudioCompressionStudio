using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Multimedia
{
    public enum ColorPickAction
    {
        /// <summary>Update swatch and value readouts only — image unchanged.</summary>
        Preview,
        /// <summary>Recolor image using picked hue/saturation while keeping each pixel's brightness.</summary>
        ApplyTint,
        /// <summary>Replace every pixel with the picked color (flat fill).</summary>
        ApplySolid
    }

    /// <summary>
    /// Interactive full gamut color-space picker (rotate/zoom on RGB cube, click to preview values).
    /// </summary>
    public class ColorSpaceViewer : Form
    {
        public event Action<byte, byte, byte, Dictionary<string, double[]>, ColorPickAction> ColorPicked;

        private readonly string colorSpace;
        private byte selB, selG, selR;
        private Dictionary<string, double[]> allValues = new();

        private Panel previewSwatch;
        private Label lblValues;
        private Label lblHint;
        private TrackBar sliderThird;
        private Label lblThird;
        private ChromaPlanePanel chromaPanel;
        private RgbCubePanel rgbCubePanel;

        public ColorSpaceViewer(string space, byte initialR, byte initialG, byte initialB)
        {
            colorSpace = space ?? "RGB";
            selR = initialR;
            selG = initialG;
            selB = initialB;
            allValues = ColorConversionUtility.ComputeAllFromBgr(selB, selG, selR);
            BuildUi();
            SelectColor(selB, selG, selR, fireEvent: false);
        }

        private void BuildUi()
        {
            Text = $"Color Space Picker — {colorSpace}";
            Size = new Size(960, 780);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;

            var lblTitle = new Label
            {
                Text = colorSpace == "RGB"
                    ? "RGB cube — drag to rotate, mouse wheel to zoom, click to preview"
                    : $"{colorSpace} plane — click to preview (use slider for 3rd axis)",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(20, 12),
                Size = new Size(900, 28)
            };
            Controls.Add(lblTitle);

            lblThird = new Label
            {
                Location = new Point(20, 48),
                Size = new Size(900, 20),
                Font = new Font("Segoe UI", 9),
                Visible = colorSpace != "RGB"
            };
            Controls.Add(lblThird);

            sliderThird = new TrackBar
            {
                Location = new Point(20, 68),
                Size = new Size(900, 45),
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                TickFrequency = 10,
                Visible = colorSpace != "RGB"
            };
            sliderThird.ValueChanged += (_, _) => { chromaPanel?.InvalidateGamut(); chromaPanel?.Invalidate(); };
            Controls.Add(sliderThird);

            int topY = colorSpace == "RGB" ? 50 : 115;

            chromaPanel = new ChromaPlanePanel
            {
                Location = new Point(20, topY),
                Size = new Size(620, 520),
                Visible = colorSpace != "RGB"
            };
            chromaPanel.ColorSelected += OnPlaneColorSelected;
            Controls.Add(chromaPanel);

            rgbCubePanel = new RgbCubePanel
            {
                Location = new Point(20, topY),
                Size = new Size(620, 520),
                Visible = colorSpace == "RGB"
            };
            rgbCubePanel.ColorSelected += (b, g, r) => SelectColor(b, g, r, fireEvent: true);
            Controls.Add(rgbCubePanel);

            previewSwatch = new Panel
            {
                Location = new Point(660, topY),
                Size = new Size(120, 120),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(selR, selG, selB)
            };
            Controls.Add(previewSwatch);

            var lblPreview = new Label
            {
                Text = "Selected color",
                Location = new Point(660, topY + 125),
                Size = new Size(120, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(lblPreview);

            var btnTint = new Button
            {
                Text = "Apply tint (keep shading)",
                Location = new Point(660, topY + 155),
                Size = new Size(260, 36),
                BackColor = Color.FromArgb(100, 100, 200),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnTint.Click += (_, _) => FireColorPicked(ColorPickAction.ApplyTint);
            Controls.Add(btnTint);

            var btnSolid = new Button
            {
                Text = "Solid fill (flat color)",
                Location = new Point(660, topY + 197),
                Size = new Size(260, 32),
                FlatStyle = FlatStyle.Flat
            };
            btnSolid.Click += (_, _) => FireColorPicked(ColorPickAction.ApplySolid);
            Controls.Add(btnSolid);

            lblHint = new Label
            {
                Text = "Click = preview values in all systems.\n" +
                       "Tint shifts image color but keeps light/dark detail.\n" +
                       "Solid fill replaces every pixel (demo only).",
                Location = new Point(660, topY + 235),
                Size = new Size(260, 58),
                Font = new Font("Segoe UI", 8.5f)
            };
            Controls.Add(lblHint);

            lblValues = new Label
            {
                Location = new Point(20, topY + 530),
                Size = new Size(900, 90),
                Font = new Font("Consolas", 8.5f),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(248, 248, 252),
                Padding = new Padding(6)
            };
            Controls.Add(lblValues);

            ConfigureSpaceUi();
            UpdateValueLabel();
        }

        private void ConfigureSpaceUi()
        {
            switch (colorSpace)
            {
                case "HSV":
                    chromaPanel.Setup(
                        "Hue (0–360°)", "Saturation (%)",
                        (x, y, w, h) =>
                        {
                            double hue = x / (double)w * 360.0;
                            double sat = (1.0 - y / (double)h) * 100.0;
                            double val = sliderThird.Value;
                            return ColorConversionUtility.BgrFromHsv(hue, sat, val);
                        });
                    lblThird.Text = $"Value / brightness: {sliderThird.Value}%";
                    sliderThird.ValueChanged += (_, _) =>
                    {
                        lblThird.Text = $"Value / brightness: {sliderThird.Value}%";
                        chromaPanel.InvalidateGamut();
                        chromaPanel.Invalidate();
                    };
                    break;

                case "YUV":
                    chromaPanel.Setup(
                        "U (0–255)", "V (0–255)",
                        (x, y, w, h) =>
                        {
                            double u = x / (double)w * 255.0;
                            double v = (1.0 - y / (double)h) * 255.0;
                            double yVal = sliderThird.Value / 100.0 * 255.0;
                            return ColorConversionUtility.BgrFromYuv(yVal, u, v);
                        });
                    lblThird.Text = $"Y (luma): {sliderThird.Value * 255 / 100}";
                    sliderThird.ValueChanged += (_, _) =>
                    {
                        lblThird.Text = $"Y (luma): {sliderThird.Value * 255 / 100}";
                        chromaPanel.InvalidateGamut();
                        chromaPanel.Invalidate();
                    };
                    break;

                case "LAB":
                    chromaPanel.Setup(
                        "a* (−128…+128)", "b* (−128…+128)",
                        (x, y, w, h) =>
                        {
                            double a = x / (double)w * 256.0 - 128.0;
                            double bStar = (1.0 - y / (double)h) * 256.0 - 128.0;
                            double l = sliderThird.Value;
                            return ColorConversionUtility.BgrFromLab(l, a, bStar);
                        });
                    lblThird.Text = $"L* (lightness): {sliderThird.Value}";
                    sliderThird.ValueChanged += (_, _) =>
                    {
                        lblThird.Text = $"L* (lightness): {sliderThird.Value}";
                        chromaPanel.InvalidateGamut();
                        chromaPanel.Invalidate();
                    };
                    break;

                case "YCbCr":
                    chromaPanel.Setup(
                        "Cb (0–255)", "Cr (0–255)",
                        (x, y, w, h) =>
                        {
                            double cb = x / (double)w * 255.0;
                            double cr = (1.0 - y / (double)h) * 255.0;
                            double yVal = sliderThird.Value / 100.0 * 255.0;
                            return ColorConversionUtility.BgrFromYCbCr(yVal, cb, cr);
                        });
                    lblThird.Text = $"Y: {sliderThird.Value * 255 / 100}";
                    sliderThird.ValueChanged += (_, _) =>
                    {
                        lblThird.Text = $"Y: {sliderThird.Value * 255 / 100}";
                        chromaPanel.InvalidateGamut();
                        chromaPanel.Invalidate();
                    };
                    break;

                case "CMYK":
                    sliderThird.Minimum = 0;
                    sliderThird.Maximum = 100;
                    sliderThird.Value = 0;
                    var sliderK = new TrackBar
                    {
                        Location = new Point(20, 108),
                        Size = new Size(900, 45),
                        Minimum = 0,
                        Maximum = 100,
                        Value = 0,
                        TickFrequency = 10
                    };
                    var lblK = new Label
                    {
                        Text = "Black (K): 0%",
                        Location = new Point(20, 88),
                        Size = new Size(900, 20),
                        Font = new Font("Segoe UI", 9)
                    };
                    Controls.Add(lblK);
                    Controls.Add(sliderK);
                    chromaPanel.Location = new Point(20, 155);
                    chromaPanel.Size = new Size(620, 485);
                    rgbCubePanel.Location = chromaPanel.Location;
                    previewSwatch.Location = new Point(660, 155);
                    lblValues.Location = new Point(20, 650);

                    chromaPanel.Setup(
                        "Cyan (%)", "Magenta (%)",
                        (x, y, w, h) =>
                        {
                            double c = x / (double)w * 100.0;
                            double m = (1.0 - y / (double)h) * 100.0;
                            double yPct = sliderThird.Value;
                            double k = sliderK.Value;
                            return ColorConversionUtility.BgrFromCmyk(c, m, yPct, k);
                        });
                    lblThird.Text = $"Yellow (Y): {sliderThird.Value}%";
                    lblK.Text = $"Black (K): {sliderK.Value}%";
                    sliderThird.ValueChanged += (_, _) =>
                    {
                        lblThird.Text = $"Yellow (Y): {sliderThird.Value}%";
                        chromaPanel.InvalidateGamut();
                        chromaPanel.Invalidate();
                    };
                    sliderK.ValueChanged += (_, _) =>
                    {
                        lblK.Text = $"Black (K): {sliderK.Value}%";
                        chromaPanel.InvalidateGamut();
                        chromaPanel.Invalidate();
                    };
                    break;
            }
        }

        private void OnPlaneColorSelected(byte b, byte g, byte r)
        {
            SelectColor(b, g, r, fireEvent: true);
        }

        private void SelectColor(byte b, byte g, byte r, bool fireEvent)
        {
            selB = b;
            selG = g;
            selR = r;
            allValues = ColorConversionUtility.ComputeAllFromBgr(b, g, r);
            previewSwatch.BackColor = Color.FromArgb(r, g, b);
            rgbCubePanel.SetSelection(r, g, b);
            UpdateValueLabel();
            if (fireEvent)
                FireColorPicked(ColorPickAction.Preview);
        }

        private void UpdateValueLabel()
        {
            lblValues.Text = ColorConversionUtility.FormatAllValues(allValues);
        }

        private void FireColorPicked(ColorPickAction action)
        {
            ColorPicked?.Invoke(selB, selG, selR, allValues, action);
        }

        /// <summary>2D chroma / hue plane filled with the full gamut slice.</summary>
        private sealed class ChromaPlanePanel : Panel
        {
            public event Action<byte, byte, byte> ColorSelected;

            private string axisX = "X";
            private string axisY = "Y";
            private Func<int, int, int, int, (byte b, byte g, byte r)> colorAt;

            public ChromaPlanePanel()
            {
                DoubleBuffered = true;
                Cursor = Cursors.Cross;
            }

            public void Setup(
                string xTitle,
                string yTitle,
                Func<int, int, int, int, (byte b, byte g, byte r)> getBgrAt)
            {
                axisX = xTitle;
                axisY = yTitle;
                colorAt = getBgrAt;
                Invalidate();
            }

            private Bitmap gamutBitmap;

            public void InvalidateGamut() => gamutBitmap = null;

            protected override void OnPaint(PaintEventArgs e)
            {
                if (colorAt == null) return;

                int w = Math.Max(1, ClientSize.Width - 50);
                int h = Math.Max(1, ClientSize.Height - 40);
                int ox = 40, oy = 10;

                if (gamutBitmap == null || gamutBitmap.Width != w || gamutBitmap.Height != h)
                {
                    gamutBitmap?.Dispose();
                    gamutBitmap = new Bitmap(w, h);
                    for (int py = 0; py < h; py += 2)
                    {
                        for (int px = 0; px < w; px += 2)
                        {
                            var (b, g, r) = colorAt(px, py, w, h);
                            var c = Color.FromArgb(r, g, b);
                            gamutBitmap.SetPixel(px, py, c);
                            if (px + 1 < w) gamutBitmap.SetPixel(px + 1, py, c);
                            if (py + 1 < h) gamutBitmap.SetPixel(px, py + 1, c);
                            if (px + 1 < w && py + 1 < h) gamutBitmap.SetPixel(px + 1, py + 1, c);
                        }
                    }
                }

                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                e.Graphics.DrawImage(gamutBitmap, ox, oy);

                using (var font = new Font("Segoe UI", 8f))
                {
                    e.Graphics.DrawString(axisX, font, Brushes.Black, ox + w / 2f - 30, oy + h + 4);
                    var state = e.Graphics.Save();
                    e.Graphics.TranslateTransform(8, oy + h / 2f);
                    e.Graphics.RotateTransform(-90);
                    e.Graphics.DrawString(axisY, font, Brushes.Black, 0, 0);
                    e.Graphics.Restore(state);
                }
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                if (colorAt == null) return;
                int w = Math.Max(1, ClientSize.Width - 50);
                int h = Math.Max(1, ClientSize.Height - 40);
                int ox = 40, oy = 10;
                int px = e.X - ox;
                int py = e.Y - oy;
                if (px < 0 || py < 0 || px >= w || py >= h) return;

                var (b, g, r) = colorAt(px, py, w, h);
                ColorSelected?.Invoke(b, g, r);
                base.OnMouseClick(e);
            }
        }

        /// <summary>Rotatable RGB cube with colored faces; click picks a color on the surface.</summary>
        private sealed class RgbCubePanel : Panel
        {
            public event Action<byte, byte, byte> ColorSelected;

            private float yaw = 0.55f;
            private float pitch = 0.35f;
            private float zoom = 1f;
            private Point dragStart;
            private bool dragging;
            private byte? selR, selG, selB;

            public RgbCubePanel()
            {
                DoubleBuffered = true;
                BackColor = Color.White;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                zoom *= e.Delta > 0 ? 1.12f : 0.89f;
                zoom = Math.Clamp(zoom, 0.35f, 3.5f);
                Invalidate();
                base.OnMouseWheel(e);
            }

            public void SetSelection(byte r, byte g, byte b)
            {
                selR = r;
                selG = g;
                selB = b;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                dragStart = e.Location;
                dragging = false;
                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (!dragging && e.Button == MouseButtons.Left)
                {
                    if (TryPickColor(e.Location, out byte r, out byte g, out byte b))
                        ColorSelected?.Invoke(b, g, r);
                }
                dragging = false;
                base.OnMouseUp(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    int dx = e.X - dragStart.X;
                    int dy = e.Y - dragStart.Y;
                    if (Math.Abs(dx) > 4 || Math.Abs(dy) > 4)
                        dragging = true;
                    if (dragging)
                    {
                        yaw += dx * 0.01f;
                        pitch += dy * 0.01f;
                        pitch = Math.Clamp(pitch, -1.2f, 1.2f);
                        dragStart = e.Location;
                        Invalidate();
                    }
                }
                base.OnMouseMove(e);
            }

            private bool TryPickColor(Point screen, out byte r, out byte g, out byte b)
            {
                r = g = b = 0;
                if (!ScreenToRay(screen, out float ox, out float oy, out float oz,
                        out float dx, out float dy, out float dz))
                    return false;

                if (!RayBoxIntersect(ox, oy, oz, dx, dy, dz, 0f, 1f, out float t))
                    return false;

                float x = ox + dx * t;
                float y = oy + dy * t;
                float z = oz + dz * t;

                r = (byte)Math.Clamp((int)Math.Round(x * 255f), 0, 255);
                g = (byte)Math.Clamp((int)Math.Round(y * 255f), 0, 255);
                b = (byte)Math.Clamp((int)Math.Round(z * 255f), 0, 255);
                return true;
            }

            private bool ScreenToRay(Point screen, out float ox, out float oy, out float oz,
                out float dx, out float dy, out float dz)
            {
                ox = oy = oz = dx = dy = dz = 0;
                var bounds = ClientRectangle;
                bounds.Inflate(-40, -40);
                float cx = bounds.Left + bounds.Width / 2f;
                float cy = bounds.Top + bounds.Height / 2f;
                float nx = (screen.X - cx) / (bounds.Width * 0.5f);
                float ny = -(screen.Y - cy) / (bounds.Height * 0.5f);

                float lx = nx, ly = ny, lz = -2f;
                float len = (float)Math.Sqrt(lx * lx + ly * ly + lz * lz);
                lx /= len; ly /= len; lz /= len;

                RotateInverse(lx, ly, lz, out dx, out dy, out dz);
                ox = 0.5f; oy = 0.5f; oz = 2.5f;
                RotateInverse(ox - 0.5f, oy - 0.5f, oz - 0.5f, out float rx, out float ry, out float rz);
                ox = rx + 0.5f; oy = ry + 0.5f; oz = rz + 0.5f;
                return true;
            }

            private void RotateInverse(float x, float y, float z, out float ox, out float oy, out float oz)
            {
                float cosP = (float)Math.Cos(-pitch);
                float sinP = (float)Math.Sin(-pitch);
                float y1 = y * cosP - z * sinP;
                float z1 = y * sinP + z * cosP;

                float cosY = (float)Math.Cos(-yaw);
                float sinY = (float)Math.Sin(-yaw);
                ox = x * cosY - z1 * sinY;
                oy = y1;
                oz = x * sinY + z1 * cosY;
            }

            private static bool RayBoxIntersect(float ox, float oy, float oz,
                float dx, float dy, float dz, float min, float max, out float tHit)
            {
                tHit = float.MaxValue;
                float tMin = 0f, tMax = float.MaxValue;

                if (!Slab(ox, dx, min, max, ref tMin, ref tMax)) return false;
                if (!Slab(oy, dy, min, max, ref tMin, ref tMax)) return false;
                if (!Slab(oz, dz, min, max, ref tMin, ref tMax)) return false;

                if (tMax < tMin || tMax < 0) return false;
                tHit = tMin >= 0 ? tMin : tMax;
                return tHit >= 0 && tHit <= 10f;
            }

            private static bool Slab(float o, float d, float min, float max, ref float tMin, ref float tMax)
            {
                if (Math.Abs(d) < 1e-6f)
                    return o >= min && o <= max;
                float t1 = (min - o) / d;
                float t2 = (max - o) / d;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = Math.Max(tMin, t1);
                tMax = Math.Min(tMax, t2);
                return tMin <= tMax;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var bounds = ClientRectangle;
                bounds.Inflate(-40, -40);
                float scale = Math.Min(bounds.Width, bounds.Height) / 320f * zoom;
                float cx = bounds.Left + bounds.Width / 2f;
                float cy = bounds.Top + bounds.Height / 2f;

                PointF Project(float r, float g, float b)
                {
                    float x = r / 255f - 0.5f;
                    float y = g / 255f - 0.5f;
                    float z = b / 255f - 0.5f;

                    float cosY = (float)Math.Cos(yaw);
                    float sinY = (float)Math.Sin(yaw);
                    float xr = x * cosY - z * sinY;
                    float zr = x * sinY + z * cosY;

                    float cosP = (float)Math.Cos(pitch);
                    float sinP = (float)Math.Sin(pitch);
                    float yr = y * cosP - zr * sinP;

                    return new PointF(cx + xr * scale * 255f, cy - yr * scale * 255f);
                }

                DrawFace(e.Graphics, Project, (i, j, n) => (255f, i / (float)n * 255f, j / (float)n * 255f));
                DrawFace(e.Graphics, Project, (i, j, n) => (i / (float)n * 255f, 255f, j / (float)n * 255f));
                DrawFace(e.Graphics, Project, (i, j, n) => (i / (float)n * 255f, j / (float)n * 255f, 255f));

                using (var pen = new Pen(Color.FromArgb(60, 60, 60), 2f))
                    DrawCubeWireframe(e.Graphics, pen, Project);

                if (selR.HasValue)
                {
                    var hp = Project(selR.Value, selG.Value, selB.Value);
                    using (var pen = new Pen(Color.Red, 2.5f))
                    {
                        e.Graphics.DrawEllipse(pen, hp.X - 10, hp.Y - 10, 20, 20);
                    }
                }

                e.Graphics.DrawString("R", Font, Brushes.DarkRed, Project(280, 0, 0));
                e.Graphics.DrawString("G", Font, Brushes.DarkGreen, Project(0, 280, 0));
                e.Graphics.DrawString("B", Font, Brushes.DarkBlue, Project(0, 0, 280));
                e.Graphics.DrawString("Wheel = zoom  |  Drag = rotate", Font, Brushes.Gray,
                    bounds.Left, bounds.Bottom + 4);
            }

            private static void DrawFace(
                Graphics g,
                Func<float, float, float, PointF> project,
                Func<int, int, int, (float r, float g, float b)> rgbAt)
            {
                int steps = 10;
                var poly = new PointF[(steps + 1) * (steps + 1)];
                var colors = new Color[poly.Length];

                for (int i = 0; i <= steps; i++)
                {
                    for (int j = 0; j <= steps; j++)
                    {
                        int idx = i * (steps + 1) + j;
                        var (r, gr, b) = rgbAt(i, j, steps);
                        poly[idx] = project(r, gr, b);
                        colors[idx] = Color.FromArgb((int)r, (int)gr, (int)b);
                    }
                }

                for (int i = 0; i < steps; i++)
                {
                    for (int j = 0; j < steps; j++)
                    {
                        int i00 = i * (steps + 1) + j;
                        int i10 = (i + 1) * (steps + 1) + j;
                        int i01 = i * (steps + 1) + j + 1;
                        int i11 = (i + 1) * (steps + 1) + j + 1;
                        using (var brush = new SolidBrush(colors[i00]))
                            g.FillPolygon(brush, new[] { poly[i00], poly[i10], poly[i11], poly[i01] });
                    }
                }
            }

            private static void DrawCubeWireframe(Graphics g, Pen pen, Func<float, float, float, PointF> project)
            {
                var corners = new (float r, float g, float b)[]
                {
                    (0, 0, 0), (255, 0, 0), (0, 255, 0), (0, 0, 255),
                    (255, 255, 0), (255, 0, 255), (0, 255, 255), (255, 255, 255)
                };
                int[][] edges =
                {
                    new[] { 0, 1 }, new[] { 0, 2 }, new[] { 0, 3 },
                    new[] { 1, 4 }, new[] { 1, 5 }, new[] { 2, 4 },
                    new[] { 2, 6 }, new[] { 3, 5 }, new[] { 3, 6 },
                    new[] { 4, 7 }, new[] { 5, 7 }, new[] { 6, 7 }
                };
                foreach (var edge in edges)
                {
                    var a = corners[edge[0]];
                    var b = corners[edge[1]];
                    g.DrawLine(pen, project(a.r, a.g, a.b), project(b.r, b.g, b.b));
                }
            }
        }
    }
}
