using System;
using System.Collections.Generic; // ✅ FIX for Dictionary error
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;


namespace Multimedia
{
    public static class BitmapExtension
    {
        public static Mat ToMat(this Bitmap bitmap)
        {
            if (bitmap == null) return null;
            var img = new Image<Bgr, byte>(bitmap.Width, bitmap.Height);
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color c = bitmap.GetPixel(x, y);
                    img[y, x] = new Bgr(c.B, c.G, c.R);
                }
            }
            Mat clone = img.Mat.Clone();
            img.Dispose();
            return clone;
        }

        /// <summary>
        /// Converts an Emgu CV Mat to a System.Drawing.Bitmap safely.
        /// Handles Bgr, Gray, and other common formats by converting to Bgr first.
        /// </summary>
        public static Bitmap ToBitmap(this Mat mat)
        {
            if (mat == null || mat.IsEmpty)
                return null;

            // Ensure the Mat is not empty and has data
            if (mat.NumberOfChannels == 0)
                return null;

            try
            {
                // Convert to Bgr<byte> for standard 24-bit RGB compatibility with WinForms
                using (var bgrImg = mat.ToImage<Bgr, byte>())
                {
                    // ✅ FIX: Use .ToBitmap() METHOD, not .Bitmap property
                    return bgrImg.ToBitmap();
                }
            }
            catch
            {
                // Fallback for grayscale or unusual formats
                using (var grayImg = mat.ToImage<Gray, byte>())
                {
                    return grayImg.ToBitmap();
                }
            }
        }

        /// <summary>
        /// Converts an Emgu CV Image<Bgr, byte> to a System.Drawing.Bitmap.
        /// </summary>
        public static Bitmap ToBitmap(this Image<Bgr, byte> img)
        {
            if (img == null)
                return null;

            // ✅ FIX: Add parentheses () to invoke the method
            return img.ToBitmap();
        }
    }
}

    public class LabForm : Form
    {
        private string loadedImagePath = string.Empty;
        private Mat currentMat = null;
        private Mat originalMat = null;
        /// <summary>Base image after color-picker tint/fill; sliders always apply on top of a fresh clone.</summary>
        private Mat pickerEditedMat = null;
        private System.Windows.Forms.Timer quantizeDebounceTimer;
        private bool syncingChannelControls;

        // UI Controls
        private Button btnBrowse;
        private Panel dragDropPanel;
        private Label lblPixelInfo;
        private Panel panelColorPreview;
        private Label lblMetadata;
        private PictureBox picOriginal;
        private PictureBox picProcessed;

        // Color Space Controls
        private ComboBox comboColorSpaces;
        private TrackBar sliderCh1, sliderCh2, sliderCh3;
        private Label lblCh1, lblCh2, lblCh3;
        private NumericUpDown numCh1, numCh2, numCh3;
        private Button btnCh1Minus, btnCh1Plus, btnCh2Minus, btnCh2Plus, btnCh3Minus, btnCh3Plus;
        private CheckBox chkDisableCh1, chkDisableCh2, chkDisableCh3;

        // Quantization Controls
        private CheckBox chkEnableQuantize;
        private TrackBar sliderQuantize;
        private Label lblQuantize;
        private Panel panelPalette;

        public LabForm()
        {
            InitializeLaboratoryLayout();
        }

        private void InitializeLaboratoryLayout()
        {
            this.Font = new Font("Segoe UI", 9f);
            this.Text = "PixelLab - Multimedia Color Space Laboratory";
            this.Size = new Size(1280, 750);
            this.MinimumSize = new Size(1100, 650);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(245, 245, 247);

            // --- Left Panel Controls ---
            
            // Browse Button
            btnBrowse = new Button();
            btnBrowse.Text = "📁 Browse Image File...";
            btnBrowse.Location = new Point(15, 15);
            btnBrowse.Size = new Size(270, 38);
            btnBrowse.BackColor = Color.FromArgb(0, 120, 215);
            btnBrowse.ForeColor = Color.White;
            btnBrowse.FlatStyle = FlatStyle.Flat;
            btnBrowse.Click += BtnBrowse_Click;
            this.Controls.Add(btnBrowse);

            // Drag & Drop Panel
            dragDropPanel = new Panel();
            dragDropPanel.Location = new Point(15, 60);
            dragDropPanel.Size = new Size(270, 70);
            dragDropPanel.BackColor = Color.FromArgb(200, 200, 200);
            dragDropPanel.AllowDrop = true;
            dragDropPanel.DragEnter += DragDropPanel_DragEnter;
            dragDropPanel.DragDrop += DragDropPanel_DragDrop;
            dragDropPanel.Paint += (s, e) => {
                e.Graphics.DrawString("📤 Drop Image Here", new Font("Segoe UI", 10), Brushes.DimGray, new PointF(70, 25));
            };
            this.Controls.Add(dragDropPanel);

            // Save Button
            Button btnSave = new Button();
            btnSave.Text = "💾 Save Processed Image";
            btnSave.Location = new Point(15, 140);
            btnSave.Size = new Size(270, 38);
            btnSave.BackColor = Color.FromArgb(16, 124, 16);
            btnSave.ForeColor = Color.White;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);

            // Reset Button
            Button btnReset = new Button();
            btnReset.Text = "🔄 Reset to Original";
            btnReset.Location = new Point(15, 185);
            btnReset.Size = new Size(270, 35);
            btnReset.BackColor = Color.FromArgb(200, 60, 60);
            btnReset.ForeColor = Color.White;
            btnReset.FlatStyle = FlatStyle.Flat;
            btnReset.Click += BtnReset_Click;
            this.Controls.Add(btnReset);

            // Metadata Display
            lblMetadata = new Label();
            lblMetadata.Text = "Image File Metadata:\n\nFile Name: -\nFormat: -\nDimensions: -\nSize: -";
            lblMetadata.Location = new Point(15, 230);
            lblMetadata.Size = new Size(270, 100);
            lblMetadata.BackColor = Color.White;
            lblMetadata.BorderStyle = BorderStyle.FixedSingle;
            lblMetadata.Padding = new Padding(5);
            this.Controls.Add(lblMetadata);

            // Color Space Selector
            Label lblSelectSpace = new Label() { 
                Text = "🎨 Color Space System:", 
                Location = new Point(15, 340), 
                Size = new Size(270, 25), 
                Font = new Font("Segoe UI", 9, FontStyle.Bold) 
            };
            this.Controls.Add(lblSelectSpace);

            comboColorSpaces = new ComboBox();
            comboColorSpaces.Location = new Point(15, 368);
            comboColorSpaces.Size = new Size(270, 32);
            comboColorSpaces.DropDownStyle = ComboBoxStyle.DropDownList;
            comboColorSpaces.Items.AddRange(new string[] { "RGB", "CMYK", "HSV", "YUV", "LAB", "YCbCr" });
            comboColorSpaces.SelectedIndex = 0;
            comboColorSpaces.SelectedIndexChanged += ColorSpaceOrSliderChanged;
            this.Controls.Add(comboColorSpaces);

            // Channel controls (− / value / + next to each slider)
            lblCh1 = new Label() { Text = "Channel 1", Location = new Point(15, 408), Size = new Size(250, 18) };
            btnCh1Minus = CreateStepButton("−", 15, 428, () => StepChannel(1, -1));
            numCh1 = CreateChannelNumeric(48, 426);
            btnCh1Plus = CreateStepButton("+", 103, 428, () => StepChannel(1, 1));
            sliderCh1 = new TrackBar() { Location = new Point(138, 424), Size = new Size(100, 45), Minimum = -100, Maximum = 100, Value = 0, TickFrequency = 25 };
            chkDisableCh1 = new CheckBox() { Text = "Mute", Location = new Point(242, 428), AutoSize = true };

            lblCh2 = new Label() { Text = "Channel 2", Location = new Point(15, 468), Size = new Size(250, 18) };
            btnCh2Minus = CreateStepButton("−", 15, 488, () => StepChannel(2, -1));
            numCh2 = CreateChannelNumeric(48, 486);
            btnCh2Plus = CreateStepButton("+", 103, 488, () => StepChannel(2, 1));
            sliderCh2 = new TrackBar() { Location = new Point(138, 484), Size = new Size(100, 45), Minimum = -100, Maximum = 100, Value = 0, TickFrequency = 25 };
            chkDisableCh2 = new CheckBox() { Text = "Mute", Location = new Point(242, 488), AutoSize = true };

            lblCh3 = new Label() { Text = "Channel 3", Location = new Point(15, 528), Size = new Size(250, 18) };
            btnCh3Minus = CreateStepButton("−", 15, 548, () => StepChannel(3, -1));
            numCh3 = CreateChannelNumeric(48, 546);
            btnCh3Plus = CreateStepButton("+", 103, 548, () => StepChannel(3, 1));
            sliderCh3 = new TrackBar() { Location = new Point(138, 544), Size = new Size(100, 45), Minimum = -100, Maximum = 100, Value = 0, TickFrequency = 25 };
            chkDisableCh3 = new CheckBox() { Text = "Mute", Location = new Point(242, 548), AutoSize = true };

            sliderCh1.ValueChanged += ChannelSlider_ValueChanged;
            sliderCh2.ValueChanged += ChannelSlider_ValueChanged;
            sliderCh3.ValueChanged += ChannelSlider_ValueChanged;
            numCh1.ValueChanged += ChannelNumeric_ValueChanged;
            numCh2.ValueChanged += ChannelNumeric_ValueChanged;
            numCh3.ValueChanged += ChannelNumeric_ValueChanged;
            chkDisableCh1.CheckedChanged += ColorSpaceOrSliderChanged;
            chkDisableCh2.CheckedChanged += ColorSpaceOrSliderChanged;
            chkDisableCh3.CheckedChanged += ColorSpaceOrSliderChanged;

            this.Controls.AddRange(new Control[] {
                lblCh1, btnCh1Minus, numCh1, btnCh1Plus, sliderCh1, chkDisableCh1,
                lblCh2, btnCh2Minus, numCh2, btnCh2Plus, sliderCh2, chkDisableCh2,
                lblCh3, btnCh3Minus, numCh3, btnCh3Plus, sliderCh3, chkDisableCh3
            });

            // Quantization Controls
            Label lblQuantTitle = new Label() {
                Text = "🎯 Color Quantization:",
                Location = new Point(15, 598),
                Size = new Size(270, 20), 
                Font = new Font("Segoe UI", 9, FontStyle.Bold) 
            };
            this.Controls.Add(lblQuantTitle);

            chkEnableQuantize = new CheckBox();
            chkEnableQuantize.Text = "Enable palette quantization";
            chkEnableQuantize.Location = new Point(15, 623);
            chkEnableQuantize.AutoSize = true;
            chkEnableQuantize.CheckedChanged += ColorSpaceOrSliderChanged;
            this.Controls.Add(chkEnableQuantize);

            lblQuantize = new Label();
            lblQuantize.Text = "Palette size: 4 colors (whole image)";
            lblQuantize.Location = new Point(15, 648);
            lblQuantize.Size = new Size(270, 20);
            lblQuantize.Font = new Font("Segoe UI", 8);
            this.Controls.Add(lblQuantize);

            sliderQuantize = new TrackBar();
            sliderQuantize.Location = new Point(15, 668);
            sliderQuantize.Size = new Size(270, 40);
            sliderQuantize.Minimum = 2;
            sliderQuantize.Maximum = 32;
            sliderQuantize.Value = 4;
            sliderQuantize.TickFrequency = 2;
            sliderQuantize.ValueChanged += QuantizeSlider_ValueChanged;
            this.Controls.Add(sliderQuantize);

            quantizeDebounceTimer = new System.Windows.Forms.Timer { Interval = 120 };
            quantizeDebounceTimer.Tick += (_, _) =>
            {
                quantizeDebounceTimer.Stop();
                ProcessColorSpaceManipulation();
            };

            panelPalette = new Panel();
            panelPalette.Location = new Point(15, 708);
            panelPalette.Size = new Size(270, 22);
            panelPalette.BorderStyle = BorderStyle.FixedSingle;
            panelPalette.Visible = false;
            this.Controls.Add(panelPalette);

            // Visualize Button
            Button btnVisualize = new Button();
            btnVisualize.Text = "🎨 Pick Color from Gamut";
            btnVisualize.Location = new Point(15, 735);
            btnVisualize.Size = new Size(270, 38);
            btnVisualize.BackColor = Color.FromArgb(100, 100, 200);
            btnVisualize.ForeColor = Color.White;
            btnVisualize.FlatStyle = FlatStyle.Flat;
            btnVisualize.Click += BtnVisualizeColorSpace_Click;
            this.Controls.Add(btnVisualize);

            // --- Right Panel Images ---
            Label lblOriginal = new Label() { 
                Text = "Original Image", 
                Location = new Point(310, 15), 
                Font = new Font("Segoe UI", 10, FontStyle.Bold) 
            };
            this.Controls.Add(lblOriginal);

            picOriginal = new PictureBox() { 
                Location = new Point(310, 40), 
                Size = new Size(460, 620), 
                BorderStyle = BorderStyle.FixedSingle, 
                BackColor = Color.White, 
                SizeMode = PictureBoxSizeMode.Zoom 
            };
            this.Controls.Add(picOriginal);

            Label lblProcessed = new Label() { 
                Text = "Processed Image", 
                Location = new Point(790, 15), 
                Font = new Font("Segoe UI", 10, FontStyle.Bold) 
            };
            this.Controls.Add(lblProcessed);

            picProcessed = new PictureBox() { 
                Location = new Point(790, 40), 
                Size = new Size(460, 620), 
                BorderStyle = BorderStyle.FixedSingle, 
                BackColor = Color.White, 
                SizeMode = PictureBoxSizeMode.Zoom 
            };
            picProcessed.MouseMove += PicProcessed_MouseMove;
            this.Controls.Add(picProcessed);

            // Pixel Info Panel
            Panel infoPanel = new Panel() {
                Location = new Point(310, 670),
                Size = new Size(940, 35),
                BackColor = Color.FromArgb(240, 240, 240),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(infoPanel);

            lblPixelInfo = new Label();
            lblPixelInfo.Text = "📍 Hover over processed image to see pixel values...";
            lblPixelInfo.Location = new Point(10, 8);
            lblPixelInfo.Size = new Size(920, 20);
            lblPixelInfo.Font = new Font("Consolas", 9);
            infoPanel.Controls.Add(lblPixelInfo);

            // Color Preview Panel
            panelColorPreview = new Panel();
            panelColorPreview.Location = new Point(15, 795);
            panelColorPreview.Size = new Size(270, 40);
            panelColorPreview.BackColor = Color.Black;
            panelColorPreview.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(panelColorPreview);

            Label lblColorPreview = new Label() {
                Text = "Selected Color Preview",
                Location = new Point(15, 775),
                Font = new Font("Segoe UI", 8)
            };
            this.Controls.Add(lblColorPreview);
        }

        private static Button CreateStepButton(string text, int x, int y, Action onClick)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(28, 26),
                FlatStyle = FlatStyle.Flat
            };
            btn.Click += (_, _) => onClick();
            return btn;
        }

        private static NumericUpDown CreateChannelNumeric(int x, int y)
        {
            return new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(52, 26),
                Minimum = -100,
                Maximum = 100,
                Value = 0
            };
        }

        private void StepChannel(int channel, int delta)
        {
            SetChannelOffset(channel, GetChannelOffset(channel) + delta);
            ProcessColorSpaceManipulation();
        }

        private int GetChannelOffset(int channel)
        {
            return channel switch
            {
                1 => sliderCh1.Value,
                2 => sliderCh2.Value,
                3 => sliderCh3.Value,
                _ => 0
            };
        }

        private void SetChannelOffset(int channel, int value)
        {
            value = Math.Clamp(value, -100, 100);
            syncingChannelControls = true;
            switch (channel)
            {
                case 1:
                    sliderCh1.Value = value;
                    numCh1.Value = value;
                    break;
                case 2:
                    sliderCh2.Value = value;
                    numCh2.Value = value;
                    break;
                case 3:
                    sliderCh3.Value = value;
                    numCh3.Value = value;
                    break;
            }
            syncingChannelControls = false;
            UpdateChannelLabelsForColorSpace();
        }

        private void ChannelSlider_ValueChanged(object sender, EventArgs e)
        {
            if (syncingChannelControls) return;
            syncingChannelControls = true;
            if (sender == sliderCh1) numCh1.Value = sliderCh1.Value;
            else if (sender == sliderCh2) numCh2.Value = sliderCh2.Value;
            else if (sender == sliderCh3) numCh3.Value = sliderCh3.Value;
            syncingChannelControls = false;
            ColorSpaceOrSliderChanged(sender, e);
        }

        private void ChannelNumeric_ValueChanged(object sender, EventArgs e)
        {
            if (syncingChannelControls) return;
            syncingChannelControls = true;
            if (sender == numCh1) sliderCh1.Value = (int)numCh1.Value;
            else if (sender == numCh2) sliderCh2.Value = (int)numCh2.Value;
            else if (sender == numCh3) sliderCh3.Value = (int)numCh3.Value;
            syncingChannelControls = false;
            ColorSpaceOrSliderChanged(sender, e);
        }

        private void ClearPickerEdits()
        {
            pickerEditedMat?.Dispose();
            pickerEditedMat = null;
        }

        /// <summary>Non-destructive pipeline input: picker edit or original (never the last displayed frame).</summary>
        private Mat CreatePipelineSource()
        {
            if (pickerEditedMat != null && !pickerEditedMat.IsEmpty)
                return pickerEditedMat.Clone();
            if (originalMat != null && !originalMat.IsEmpty)
                return originalMat.Clone();
            return currentMat?.Clone();
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Image Files|*.bmp;*.jpg;*.jpeg;*.png";
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                LoadImageIntoLaboratory(openFileDialog.FileName);
            }
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            if (originalMat != null)
            {
                ClearPickerEdits();
                currentMat.Dispose();
                currentMat = originalMat.Clone();

                syncingChannelControls = true;
                sliderCh1.Value = 0;
                sliderCh2.Value = 0;
                sliderCh3.Value = 0;
                numCh1.Value = 0;
                numCh2.Value = 0;
                numCh3.Value = 0;
                syncingChannelControls = false;

                chkDisableCh1.Checked = false;
                chkDisableCh2.Checked = false;
                chkDisableCh3.Checked = false;
                comboColorSpaces.SelectedIndex = 0;
                chkEnableQuantize.Checked = false;
                sliderQuantize.Value = 4;
                UpdatePalettePreview(null);
                UpdateChannelLabelsForColorSpace();

                ProcessColorSpaceManipulation();
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (picProcessed.Image == null)
            {
                MessageBox.Show("There is no processed image to save!", "Export Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Title = "Save Processed Image";
                sfd.Filter = "JPEG Image|*.jpg|PNG Image|*.png|Bitmap Image|*.bmp";
                sfd.DefaultExt = "jpg";
                
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        picProcessed.Image.Save(sfd.FileName);
                        MessageBox.Show("Image exported successfully!", "Success", 
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to save image: {ex.Message}", "File Error", 
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void DragDropPanel_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) 
                e.Effect = DragDropEffects.Copy;
        }

        private void DragDropPanel_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0) 
                LoadImageIntoLaboratory(files[0]);
        }

        private void LoadImageIntoLaboratory(string filePath)
        {
            try
            {
                loadedImagePath = filePath;

                if (currentMat != null) currentMat.Dispose();
                if (originalMat != null) originalMat.Dispose();
                ClearPickerEdits();

                currentMat = CvInvoke.Imread(filePath, ImreadModes.AnyColor);
                originalMat = currentMat.Clone();

                if (picOriginal.Image != null) picOriginal.Image.Dispose();
                picOriginal.Image = BitmapExtension.ToBitmap(currentMat);

                FileInfo fileInfo = new FileInfo(filePath);
                double fileSizeKb = fileInfo.Length / 1024.0;
                lblMetadata.Text = $"Image File Metadata:\n\n" +
                    $"File Name: {fileInfo.Name}\n" +
                    $"Format: {fileInfo.Extension.ToUpper()}\n" +
                    $"Dimensions: {currentMat.Width} x {currentMat.Height} px\n" +
                    $"Size: {fileSizeKb:F2} KB";

                ProcessColorSpaceManipulation();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not process image file: {ex.Message}");
            }
        }

        private void ColorSpaceOrSliderChanged(object sender, EventArgs e)
        {
            UpdateChannelLabelsForColorSpace();
            lblQuantize.Text = $"Palette size: {sliderQuantize.Value} colors (entire image)";
            panelPalette.Visible = chkEnableQuantize.Checked && panelPalette.Controls.Count > 0;

            if (currentMat != null)
                ProcessColorSpaceManipulation();
        }

        private void QuantizeSlider_ValueChanged(object sender, EventArgs e)
        {
            lblQuantize.Text = $"Palette size: {sliderQuantize.Value} colors (entire image)";
            panelPalette.Visible = chkEnableQuantize.Checked && panelPalette.Controls.Count > 0;

            if (currentMat == null || !chkEnableQuantize.Checked)
                return;

            quantizeDebounceTimer.Stop();
            quantizeDebounceTimer.Start();
        }

        private void UpdateChannelLabelsForColorSpace()
        {
            string space = comboColorSpaces.SelectedItem?.ToString() ?? "RGB";
            switch (space)
            {
                case "HSV":
                    lblCh1.Text = $"Hue offset: {sliderCh1.Value}";
                    lblCh2.Text = $"Saturation offset: {sliderCh2.Value}";
                    lblCh3.Text = $"Brightness / Value (V): {sliderCh3.Value}";
                    break;
                case "YUV":
                case "YCbCr":
                    lblCh1.Text = $"Luma / brightness (Y): {sliderCh1.Value}";
                    lblCh2.Text = $"Chroma 1 offset: {sliderCh2.Value}";
                    lblCh3.Text = $"Chroma 2 offset: {sliderCh3.Value}";
                    break;
                case "LAB":
                    lblCh1.Text = $"Lightness (L*): {sliderCh1.Value}";
                    lblCh2.Text = $"Green–red (a*) offset: {sliderCh2.Value}";
                    lblCh3.Text = $"Blue–yellow (b*) offset: {sliderCh3.Value}";
                    break;
                case "CMYK":
                    lblCh1.Text = $"Cyan offset: {sliderCh1.Value}";
                    lblCh2.Text = $"Magenta offset: {sliderCh2.Value}";
                    lblCh3.Text = $"Yellow offset: {sliderCh3.Value}";
                    break;
                default:
                    lblCh1.Text = $"Red offset: {sliderCh1.Value}";
                    lblCh2.Text = $"Green offset: {sliderCh2.Value}";
                    lblCh3.Text = $"Blue offset: {sliderCh3.Value}";
                    break;
            }
        }

        private void BtnVisualizeColorSpace_Click(object sender, EventArgs e)
        {
            if (comboColorSpaces.SelectedItem == null)
            {
                MessageBox.Show("Please select a color space from the dropdown.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            byte r = 128, g = 128, b = 128;
            if (currentMat != null && !currentMat.IsEmpty)
            {
                using (var img = currentMat.ToImage<Bgr, byte>())
                {
                    int cx = currentMat.Width / 2;
                    int cy = currentMat.Height / 2;
                    b = img.Data[cy, cx, 0];
                    g = img.Data[cy, cx, 1];
                    r = img.Data[cy, cx, 2];
                }
            }

            string selectedSpace = comboColorSpaces.SelectedItem.ToString();
            var viewer = new Multimedia.ColorSpaceViewer(selectedSpace, r, g, b);
            viewer.ColorPicked += OnColorPickedFromViewer;
            viewer.Show();
        }

        private void OnColorPickedFromViewer(byte b, byte g, byte r,
            Dictionary<string, double[]> allValues, Multimedia.ColorPickAction action)
        {
            panelColorPreview.BackColor = Color.FromArgb(r, g, b);
            lblPixelInfo.Text = Multimedia.ColorConversionUtility.FormatAllValues(allValues);

            if (action == Multimedia.ColorPickAction.Preview ||
                currentMat == null || currentMat.IsEmpty)
                return;

            if (action == Multimedia.ColorPickAction.ApplyTint)
                ApplyTintToImage(b, g, r);
            else if (action == Multimedia.ColorPickAction.ApplySolid)
                ApplySolidColorToImage(b, g, r);
        }

        /// <summary>
        /// Shifts image toward the picked color in HSV while preserving each pixel's brightness (V).
        /// Keeps edges and shading unlike a flat fill.
        /// </summary>
        private void ApplyTintToImage(byte b, byte g, byte r)
        {
            ClearPickerEdits();
            pickerEditedMat = originalMat.Clone();
            using (Mat hsvMat = new Mat())
            {
                CvInvoke.CvtColor(pickerEditedMat, hsvMat, ColorConversion.Bgr2Hsv);
                using (var hsvImg = hsvMat.ToImage<Hsv, byte>())
                {
                    using (Mat pickMat = new Mat())
                    {
                        pickMat.Create(1, 1, DepthType.Cv8U, 3);
                        pickMat.SetTo(new MCvScalar(b, g, r));
                        using (Mat pickHsvMat = new Mat())
                        {
                            CvInvoke.CvtColor(pickMat, pickHsvMat, ColorConversion.Bgr2Hsv);
                            using (var pickHsv = pickHsvMat.ToImage<Hsv, byte>())
                            {
                                byte pickH = (byte)pickHsv[0, 0].Hue;
                                byte pickS = (byte)pickHsv[0, 0].Satuation;

                                int h = hsvImg.Height;
                                int w = hsvImg.Width;
                                for (int y = 0; y < h; y++)
                                {
                                    for (int x = 0; x < w; x++)
                                    {
                                        hsvImg.Data[y, x, 0] = pickH;
                                        hsvImg.Data[y, x, 1] = pickS;
                                    }
                                }
                            }
                        }
                    }
                }
                CvInvoke.CvtColor(hsvMat, pickerEditedMat, ColorConversion.Hsv2Bgr);
            }
            ProcessColorSpaceManipulation();
        }

        private void ApplySolidColorToImage(byte b, byte g, byte r)
        {
            ClearPickerEdits();
            pickerEditedMat = originalMat.Clone();
            pickerEditedMat.SetTo(new MCvScalar(b, g, r));
            ProcessColorSpaceManipulation();
        }

        private Dictionary<string, double[]> GetSelectedPixelValues()
        {
            int x = currentMat.Width / 2;
            int y = currentMat.Height / 2;
            using (var img = currentMat.ToImage<Bgr, byte>())
            {
                byte b = img.Data[y, x, 0];
                byte g = img.Data[y, x, 1];
                byte r = img.Data[y, x, 2];
                return Multimedia.ColorConversionUtility.ComputeAllFromBgr(b, g, r);
            }
        }

        private void ProcessColorSpaceManipulation()
        {
            if (originalMat == null || originalMat.IsEmpty) return;

            string selectedSpace = comboColorSpaces.SelectedItem.ToString();
            Mat pipelineSource = CreatePipelineSource();
            if (pipelineSource == null || pipelineSource.IsEmpty) return;

            Mat transformedMat = new Mat();
            Mat resultMat = null;

            try
            {
                switch (selectedSpace)
                {
                    case "RGB":
                        transformedMat = pipelineSource.Clone();
                        break;
                    case "HSV":
                        CvInvoke.CvtColor(pipelineSource, transformedMat, ColorConversion.Bgr2Hsv);
                        break;
                    case "YUV":
                        CvInvoke.CvtColor(pipelineSource, transformedMat, ColorConversion.Bgr2Yuv);
                        break;
                    case "LAB":
                        CvInvoke.CvtColor(pipelineSource, transformedMat, ColorConversion.Bgr2Lab);
                        break;
                    case "YCbCr":
                        CvInvoke.CvtColor(pipelineSource, transformedMat, ColorConversion.Bgr2YCrCb);
                        break;
                    case "CMYK":
                        Bitmap cmykBitmap = ApplyCmykSimulation(pipelineSource);
                        if (chkEnableQuantize.Checked)
                        {
                            using (Mat cmykMat = BitmapExtension.ToMat(cmykBitmap))
                            using (var q = Multimedia.ColorQuantizer.QuantizePalette(cmykMat, sliderQuantize.Value))
                            {
                                UpdatePalettePreview(q.Palette);
                                if (picProcessed.Image != null) picProcessed.Image.Dispose();
                                picProcessed.Image = BitmapExtension.ToBitmap(q.Image);
                            }
                            cmykBitmap.Dispose();
                        }
                        else
                        {
                            UpdatePalettePreview(null);
                            if (picProcessed.Image != null) picProcessed.Image.Dispose();
                            picProcessed.Image = cmykBitmap;
                        }
                        transformedMat.Dispose();
                        return;
                }

                Mat[] channels = transformedMat.Split();

                ModifyChannelData(channels[0], sliderCh1.Value, chkDisableCh1.Checked);
                ModifyChannelData(channels[1], sliderCh2.Value, chkDisableCh2.Checked);
                ModifyChannelData(channels[2], sliderCh3.Value, chkDisableCh3.Checked);

                using (VectorOfMat vm = new VectorOfMat())
                {
                    vm.Push(channels);
                    resultMat = new Mat();
                    CvInvoke.Merge(vm, resultMat);
                }

                Mat displayMat = new Mat();
                switch (selectedSpace)
                {
                    case "RGB":
                        resultMat.CopyTo(displayMat);
                        break;
                    case "HSV":
                        CvInvoke.CvtColor(resultMat, displayMat, ColorConversion.Hsv2Bgr);
                        break;
                    case "YUV":
                        CvInvoke.CvtColor(resultMat, displayMat, ColorConversion.Yuv2Bgr);
                        break;
                    case "LAB":
                        CvInvoke.CvtColor(resultMat, displayMat, ColorConversion.Lab2Bgr);
                        break;
                    case "YCbCr":
                        CvInvoke.CvtColor(resultMat, displayMat, ColorConversion.YCrCb2Bgr);
                        break;
                }

                if (chkEnableQuantize.Checked)
                {
                    using (var q = Multimedia.ColorQuantizer.QuantizePalette(displayMat, sliderQuantize.Value))
                    {
                        UpdatePalettePreview(q.Palette);
                        if (picProcessed.Image != null) picProcessed.Image.Dispose();
                        picProcessed.Image = BitmapExtension.ToBitmap(q.Image);
                    }
                }
                else
                {
                    UpdatePalettePreview(null);
                    if (picProcessed.Image != null) picProcessed.Image.Dispose();
                    picProcessed.Image = BitmapExtension.ToBitmap(displayMat);
                }

                displayMat.Dispose();
                resultMat.Dispose();
                foreach (var m in channels) m.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pipeline Error: {ex.Message}");
                if (resultMat != null) resultMat.Dispose();
            }
            finally
            {
                pipelineSource.Dispose();
                transformedMat.Dispose();
            }
        }

        private void ModifyChannelData(Mat channel, int offsetValue, bool isMuted)
        {
            if (isMuted)
            {
                channel.SetTo(new MCvScalar(0));
            }
            else if (offsetValue != 0)
            {
                using (ScalarArray sa = new ScalarArray(new MCvScalar(offsetValue)))
                using (ScalarArray maxV = new ScalarArray(new MCvScalar(255)))
                using (ScalarArray minV = new ScalarArray(new MCvScalar(0)))
                {
                    CvInvoke.Add(channel, sa, channel);
                    CvInvoke.Min(channel, maxV, channel);
                    CvInvoke.Max(channel, minV, channel);
                }
            }
        }

        private Bitmap ApplyCmykSimulation(Mat inputBgr)
        {
            double s1 = sliderCh1.Value / 100.0;
            double s2 = sliderCh2.Value / 100.0;
            double s3 = sliderCh3.Value / 100.0;
            bool mute1 = chkDisableCh1.Checked;
            bool mute2 = chkDisableCh2.Checked;
            bool mute3 = chkDisableCh3.Checked;

            Image<Bgr, byte> inputImg = inputBgr.ToImage<Bgr, byte>();
            Image<Bgr, byte> outputImg = new Image<Bgr, byte>(inputImg.Width, inputImg.Height);

            int width = inputImg.Width;
            int height = inputImg.Height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double b = inputImg.Data[y, x, 0] / 255.0;
                    double g = inputImg.Data[y, x, 1] / 255.0;
                    double r = inputImg.Data[y, x, 2] / 255.0;

                    double k = 1.0 - Math.Max(r, Math.Max(g, b));
                    
                    double cyan = (k == 1.0) ? 0 : (1.0 - r - k) / (1.0 - k);
                    double magenta = (k == 1.0) ? 0 : (1.0 - g - k) / (1.0 - k);
                    double yellow = (k == 1.0) ? 0 : (1.0 - b - k) / (1.0 - k);

                    cyan = Math.Min(1.0, Math.Max(0.0, cyan + s1));
                    magenta = Math.Min(1.0, Math.Max(0.0, magenta + s2));
                    yellow = Math.Min(1.0, Math.Max(0.0, yellow + s3));

                    if (mute1) cyan = 0;
                    if (mute2) magenta = 0;
                    if (mute3) yellow = 0;

                    int rNew = (int)Math.Round((1.0 - cyan) * (1.0 - k) * 255.0);
                    int gNew = (int)Math.Round((1.0 - magenta) * (1.0 - k) * 255.0);
                    int bNew = (int)Math.Round((1.0 - yellow) * (1.0 - k) * 255.0);

                    outputImg.Data[y, x, 0] = (byte)Math.Min(255, Math.Max(0, bNew));
                    outputImg.Data[y, x, 1] = (byte)Math.Min(255, Math.Max(0, gNew));
                    outputImg.Data[y, x, 2] = (byte)Math.Min(255, Math.Max(0, rNew));
                }
            }
            
            Bitmap finalBmp = BitmapExtension.ToBitmap(outputImg.Mat);
            inputImg.Dispose();
            outputImg.Dispose();
            return finalBmp;
        }

        private void UpdatePalettePreview(Color[] palette)
        {
            panelPalette.Controls.Clear();
            if (palette == null || palette.Length == 0 || !chkEnableQuantize.Checked)
            {
                panelPalette.Visible = false;
                return;
            }

            panelPalette.Visible = true;
            int swatchW = Math.Max(6, panelPalette.Width / palette.Length);
            for (int i = 0; i < palette.Length; i++)
            {
                panelPalette.Controls.Add(new Panel
                {
                    BackColor = palette[i],
                    Location = new Point(i * swatchW, 0),
                    Size = new Size(Math.Max(4, swatchW - 1), panelPalette.Height - 2),
                    BorderStyle = BorderStyle.FixedSingle
                });
            }
        }

        private void PicProcessed_MouseMove(object sender, MouseEventArgs e)
        {
            if (picProcessed.Image == null) return;

            try
            {
                Bitmap bmp = (Bitmap)picProcessed.Image;
                int imgX = e.X * bmp.Width / picProcessed.Width;
                int imgY = e.Y * bmp.Height / picProcessed.Height;

                if (imgX < 0 || imgY < 0 || imgX >= bmp.Width || imgY >= bmp.Height)
                    return;

                Color pixel = bmp.GetPixel(imgX, imgY);
                int r = pixel.R, g = pixel.G, b = pixel.B;

                double hue = pixel.GetHue();
                double sat = pixel.GetSaturation() * 100.0;
                double val = pixel.GetBrightness() * 100.0;

                double yVal = 0.299 * r + 0.587 * g + 0.114 * b;
                double cb = 128 - 0.168736 * r - 0.331264 * g + 0.5 * b;
                double cr = 128 + 0.5 * r - 0.418688 * g - 0.081312 * b;

                lblPixelInfo.Text = $"Pos: ({imgX}, {imgY}) | " +
                    $"RGB: ({r,3}, {g,3}, {b,3}) | " +
                    $"HSV: ({hue,5:F1}°, {sat,5:F1}%, {val,5:F1}%) | " +
                    $"YCbCr: ({yVal,5:F1}, {cb,5:F1}, {cr,5:F1})";

                panelColorPreview.BackColor = pixel;
            }
            catch { }
        }
    }
