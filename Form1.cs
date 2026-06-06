using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;
using OxyPlot.WindowsForms;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;

namespace AudioCompressionApp
{
    public class Form1 : Form
    {
        // =====================================================
        // UI CONTROLS
        // =====================================================

        private Panel pnlDrop;

        private Label lblDropText;

        private Button btnBrowse,
                       btnPlay,
                       btnStop,
                       btnCompress,
                       btnCancel,
                       btnDecompress,
                       btnReset,
                       btnComparison;

        private Label lblProperties;

        private ComboBox cmbAlgorithm;

        private NumericUpDown numQuantization,
                              numSampleRate;

        private ProgressBar progressBar;

        private PlotView plotRatio,
                         plotSpeed;

        // =====================================================
        // AUDIO ENGINE VARIABLES
        // =====================================================

        private string currentFilePath;

        private WaveOutEvent outputDevice;

        private AudioFileReader audioFile;

        private CancellationTokenSource cts;

        private CompressionEngine engine =
            new CompressionEngine();

        private int plotXIndex = 0;

        private long originalFileSizeBytes = 0;

        // =====================================================
        // CONSTRUCTOR
        // =====================================================

        public Form1()
        {
            InitializeComponent();

            SetupPlots();
        }

        // =====================================================
        // INITIALIZE UI
        // =====================================================

        private void InitializeComponent()
        {
            this.Text = "Audio Compression Studio";

            this.Size = new Size(1000, 750);

            this.StartPosition =
                FormStartPosition.CenterScreen;

            // =================================================
            // DRAG & DROP PANEL
            // =================================================

            pnlDrop = new Panel
            {
                Location = new Point(20, 20),

                Size = new Size(400, 150),

                BorderStyle = BorderStyle.FixedSingle,

                BackColor = Color.LightGray,

                AllowDrop = true
            };

            lblDropText = new Label
            {
                Text =
                    "Drag & Drop Audio File Here\nor Click Browse",

                TextAlign =
                    ContentAlignment.MiddleCenter,

                Dock = DockStyle.Fill,

                Font = new Font("Segoe UI", 11F)
            };

            pnlDrop.Controls.Add(lblDropText);

            pnlDrop.DragEnter += PnlDrop_DragEnter;

            pnlDrop.DragDrop += PnlDrop_DragDrop;

            this.Controls.Add(pnlDrop);

            // =================================================
            // MEDIA CONTROLS
            // =================================================

            btnBrowse = new Button
            {
                Text = "Browse",

                Location = new Point(20, 180),

                Size = new Size(100, 35)
            };

            btnBrowse.Click += BtnBrowse_Click;

            btnPlay = new Button
            {
                Text = "Play Preview",

                Location = new Point(130, 180),

                Size = new Size(110, 35),

                Enabled = false
            };

            btnPlay.Click += BtnPlay_Click;

            btnStop = new Button
            {
                Text = "Stop",

                Location = new Point(250, 180),

                Size = new Size(90, 35),

                Enabled = false
            };

            btnStop.Click += BtnStop_Click;

            this.Controls.Add(btnBrowse);
            this.Controls.Add(btnPlay);
            this.Controls.Add(btnStop);

            // =================================================
            // AUDIO PROPERTIES
            // =================================================

            lblProperties = new Label
            {
                Location = new Point(440, 20),

                Size = new Size(520, 150),

                BorderStyle = BorderStyle.Fixed3D,

                Font = new Font("Consolas", 10),

                Text =
                    "Audio Properties:\n- Waiting for file..."
            };

            this.Controls.Add(lblProperties);

            // =================================================
            // ALGORITHM SELECTION
            // =================================================

            Label lblAlgo = new Label
            {
                Text = "Compression Algorithm:",

                Location = new Point(20, 230),

                Size = new Size(170, 25),

                TextAlign = ContentAlignment.MiddleLeft
            };

            cmbAlgorithm = new ComboBox
            {
                Location = new Point(200, 228),

                Size = new Size(320, 30),

                DropDownStyle =
                    ComboBoxStyle.DropDownList,

                Font = new Font("Segoe UI", 10F)
            };

            cmbAlgorithm.Items.AddRange(new string[]
            {
                "Nonlinear Quantization",
                "Differential Pulse Code Modulation (DPCM)",
                "Delta Modulation",
                "Adaptive Delta Modulation",
                "Predictive Differential Coding"
            });

            cmbAlgorithm.SelectedIndex = 0;

            this.Controls.Add(lblAlgo);
            this.Controls.Add(cmbAlgorithm);

            // =================================================
            // SETTINGS
            // =================================================

            Label lblQ = new Label
            {
                Text = "Q-Levels:",

                Location = new Point(20, 275),

                Size = new Size(80, 25),

                TextAlign = ContentAlignment.MiddleLeft
            };

            numQuantization = new NumericUpDown
            {
                Location = new Point(105, 273),

                Size = new Size(110, 30),

                Minimum = 2,

                Maximum = 65536,

                Value = 256,

                Font = new Font("Segoe UI", 10F)
            };

            Label lblSR = new Label
            {
                Text = "Sample Rate:",

                Location = new Point(240, 275),

                Size = new Size(100, 25),

                TextAlign = ContentAlignment.MiddleLeft
            };

            numSampleRate = new NumericUpDown
            {
                Location = new Point(345, 273),

                Size = new Size(140, 30),

                Minimum = 8000,

                Maximum = 48000,

                Value = 44100,

                Increment = 1000,

                Font = new Font("Segoe UI", 10F)
            };

            this.Controls.Add(lblQ);
            this.Controls.Add(numQuantization);
            this.Controls.Add(lblSR);
            this.Controls.Add(numSampleRate);

            // =================================================
            // ACTION BUTTONS
            // =================================================

            btnCompress = new Button
            {
                Text = "Start Compression",

                Location = new Point(540, 225),

                Size = new Size(150, 70),

                Enabled = false
            };

            btnCompress.Click += BtnCompress_Click;

            btnDecompress = new Button
            {
                Text = "Decompress",

                Location = new Point(700, 225),

                Size = new Size(110, 70),

                Enabled = false
            };

            btnDecompress.Click += BtnDecompress_Click;

            btnCancel = new Button
            {
                Text = "Cancel",

                Location = new Point(820, 225),

                Size = new Size(70, 70),

                Enabled = false
            };

            btnCancel.Click += BtnCancel_Click;

            btnReset = new Button
            {
                Text = "Reset",

                Location = new Point(900, 225),

                Size = new Size(70, 70),

                Enabled = false
            };

            btnReset.Click += BtnReset_Click;

            btnComparison = new Button
            {
                Text = "Compression History",
                Location = new Point(20, 350),
                Size = new Size(150, 30)
            };

            btnComparison.Click += BtnComparison_Click;

            this.Controls.Add(btnCompress);
            this.Controls.Add(btnDecompress);
            this.Controls.Add(btnCancel);
            this.Controls.Add(btnReset);
            this.Controls.Add(btnComparison);

            // =================================================
            // PROGRESS BAR
            // =================================================

            progressBar = new ProgressBar
            {
                Location = new Point(20, 320),

                Size = new Size(940, 25),

                Minimum = 0,

                Maximum = 100,

                Style = ProgressBarStyle.Continuous
            };

            this.Controls.Add(progressBar);

            // =================================================
            // REAL-TIME PLOTS
            // =================================================

            plotRatio = new PlotView
            {
                Location = new Point(20, 390),

                Size = new Size(460, 300)
            };

            plotSpeed = new PlotView
            {
                Location = new Point(500, 390),

                Size = new Size(460, 300)
            };

            this.Controls.Add(plotRatio);
            this.Controls.Add(plotSpeed);
        }

        // =====================================================
        // PLOTS
        // =====================================================

        private void SetupPlots()
        {
            var ratioModel =
                new PlotModel
                {
                    Title = "Real-time Compression Ratio"
                };

            ratioModel.Axes.Add(
                new LinearAxis
                {
                    Position = AxisPosition.Bottom,
                    Title = "Data Blocks"
                });

            ratioModel.Axes.Add(
                new LinearAxis
                {
                    Position = AxisPosition.Left,
                    Title = "Ratio (%)"
                });

            ratioModel.Series.Add(
                new LineSeries
                {
                    Title = "Ratio",
                    Color = OxyColors.Blue
                });

            plotRatio.Model = ratioModel;

            var speedModel =
                new PlotModel
                {
                    Title = "Processing Speed"
                };

            speedModel.Axes.Add(
                new LinearAxis
                {
                    Position = AxisPosition.Bottom,
                    Title = "Time"
                });

            speedModel.Axes.Add(
                new LinearAxis
                {
                    Position = AxisPosition.Left,
                    Title = "Samples/ms"
                });

            speedModel.Series.Add(
                new LineSeries
                {
                    Title = "Speed",
                    Color = OxyColors.Red
                });

            plotSpeed.Model = speedModel;
        }

        // =====================================================
        // DRAG & DROP
        // =====================================================

        private void PnlDrop_DragEnter(
            object sender,
            DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect =
                    DragDropEffects.Copy;
            }
        }

        private void PnlDrop_DragDrop(
            object sender,
            DragEventArgs e)
        {
            string[] files =
                (string[])e.Data.GetData(
                    DataFormats.FileDrop);

            if (files.Length > 0)
            {
                LoadAudioFile(files[0]);
            }
        }

        // =====================================================
        // BROWSE FILE
        // =====================================================

        private void BtnBrowse_Click(
            object sender,
            EventArgs e)
        {
            using (OpenFileDialog ofd =
                   new OpenFileDialog())
            {
                ofd.Filter =
                    "Audio Files|*.wav;*.mp3;*.aiff;*.pcomp";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    LoadAudioFile(ofd.FileName);
                }
            }
        }

        // =====================================================
        // LOAD AUDIO FILE
        // =====================================================

        private void LoadAudioFile(string path)
        {
            try
            {
                currentFilePath = path;

                var fileInfo =
                    new FileInfo(path);

                bool isCompressedFile =
                    Path.GetExtension(path)
                    .Equals(
                        ".pcomp",
                        StringComparison.OrdinalIgnoreCase);

                if (!isCompressedFile)
                {
                    using (var reader =
                           new AudioFileReader(path))
                    {
                        double fileSizeMB =
                            fileInfo.Length
                            /
                            (1024.0 * 1024.0);

                        lblProperties.Text =
                            $"Audio Properties:\n" +
                            $"File Size: {fileSizeMB:F2} MB\n" +
                            $"Duration: {reader.TotalTime:mm\\:ss\\.fff}\n" +
                            $"Sample Rate: {reader.WaveFormat.SampleRate} Hz\n" +
                            $"Channels: {reader.WaveFormat.Channels}\n" +
                            $"Bit Rate: {(reader.WaveFormat.AverageBytesPerSecond * 8) / 1000} kbps\n" +
                            $"Encoding: {reader.WaveFormat.Encoding}";
                    }
                }
                else
                {
                    double fileSizeMB =
                        fileInfo.Length
                        /
                        (1024.0 * 1024.0);

                    lblProperties.Text =
                        $"Compressed Audio File\n\n" +
                        $"File Size: {fileSizeMB:F2} MB\n" +
                        $"Format: PCOMP\n" +
                        $"Status: Ready for decompression";
                }

                btnPlay.Enabled = !isCompressedFile;

                btnCompress.Enabled = !isCompressedFile;

                btnDecompress.Enabled = isCompressedFile;

                btnReset.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error loading file: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // =====================================================
        // PLAYBACK
        // =====================================================

        private void BtnPlay_Click(
            object sender,
            EventArgs e)
        {
            if (string.IsNullOrEmpty(currentFilePath))
                return;

            if (outputDevice == null)
            {
                outputDevice =
                    new WaveOutEvent();

                audioFile =
                    new AudioFileReader(currentFilePath);

                outputDevice.Init(audioFile);

                outputDevice.PlaybackStopped +=
                    (s, args) =>
                    {
                        CleanUpAudio();
                    };
            }

            outputDevice.Play();

            btnPlay.Enabled = false;

            btnStop.Enabled = true;
        }

        private void BtnStop_Click(
            object sender,
            EventArgs e)
        {
            outputDevice?.Stop();
        }

        private void CleanUpAudio()
        {
            if (outputDevice != null)
            {
                outputDevice.Dispose();

                outputDevice = null;
            }

            if (audioFile != null)
            {
                audioFile.Dispose();

                audioFile = null;
            }

            if (this.IsHandleCreated)
            {
                this.Invoke(
                    (MethodInvoker)delegate
                    {
                        btnPlay.Enabled = true;
                        btnStop.Enabled = false;
                    });
            }
        }

        // =====================================================
        // COMPRESS
        // =====================================================

        private async void BtnCompress_Click(
            object sender,
            EventArgs e)
        {
            if (string.IsNullOrEmpty(currentFilePath))
                return;

            btnCompress.Enabled = false;
            btnPlay.Enabled = false;
            btnDecompress.Enabled = false;
            btnCancel.Enabled = true;
            cmbAlgorithm.Enabled = false;

            progressBar.Value = 0;

            ((LineSeries)plotRatio.Model.Series[0])
                .Points.Clear();

            ((LineSeries)plotSpeed.Model.Series[0])
                .Points.Clear();

            plotXIndex = 0;

            CompressionAlgorithm selectedAlgo =
                (CompressionAlgorithm)cmbAlgorithm.SelectedIndex;

            string algorithmTag = selectedAlgo switch
            {
                CompressionAlgorithm.NonlinearQuantization => "NLQ",

                CompressionAlgorithm.DPCM => "DPCM",

                CompressionAlgorithm.DeltaModulation => "DM",

                CompressionAlgorithm.AdaptiveDeltaModulation => "ADM",

                CompressionAlgorithm.PredictiveDifferentialCoding => "PDC",

                _ => "COMP"
            };

            string directory =
                Path.GetDirectoryName(currentFilePath);

            string fileName =
                Path.GetFileNameWithoutExtension(currentFilePath);

            string outputPath =
                Path.Combine(
                    directory,
                    $"{fileName}_{algorithmTag}.pcomp");

            originalFileSizeBytes =
                new FileInfo(currentFilePath).Length;

            cts = new CancellationTokenSource();

            Stopwatch fullTimer =
                Stopwatch.StartNew();

            var settings = new CompressionSettings
            {
                QuantizationLevels = (int)numQuantization.Value,
                TargetSampleRate = (int)numSampleRate.Value
            };

            var progress =
                new Progress<CompressionProgress>(p =>
                {
                    SetProgressBarValue(p.PercentageComplete);

                    ((LineSeries)plotRatio.Model.Series[0])
                        .Points.Add(
                            new DataPoint(
                                plotXIndex,
                                p.CurrentCompressionRatio));

                    ((LineSeries)plotSpeed.Model.Series[0])
                        .Points.Add(
                            new DataPoint(
                                plotXIndex,
                                p.ProcessingSpeed));

                    plotXIndex++;

                    plotRatio.InvalidatePlot(true);

                    plotSpeed.InvalidatePlot(true);
                });

            try
            {
                await engine.CompressAudioAsync(
                    currentFilePath,
                    outputPath,
                    selectedAlgo,
                    settings,
                    progress,
                    cts.Token);

                fullTimer.Stop();

                SetProgressBarValue(100);

                string sourcePath = currentFilePath;

                var metrics = CompressionMetrics.Create(
                    sourcePath,
                    outputPath,
                    settings);

                string report = metrics.BuildCompressionReport(
                    cmbAlgorithm.Text,
                    (int)numSampleRate.Value,
                    CompressionMetrics.GetCompressedBitDepth(selectedAlgo),
                    (int)numQuantization.Value,
                    fullTimer.ElapsedMilliseconds);

                MessageBox.Show(
                    report,
                    "Compression Report",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                var historyEntry = CompressionHistoryEntry.FromMetrics(
                    metrics,
                    sourcePath,
                    cmbAlgorithm.Text,
                    outputPath,
                    null,
                    fullTimer.ElapsedMilliseconds,
                    metrics.SavingsVsOriginalPercent < 0
                        ? "Compressed (not decompressed)"
                        : "Compressed (not decompressed)");

                CompressionHistory.Add(historyEntry);

                currentFilePath = outputPath;

                btnDecompress.Enabled = true;
            }
            catch (OperationCanceledException)
            {
                fullTimer.Stop();

                MessageBox.Show(
                    "Compression was cancelled.",
                    "Cancelled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            catch (Exception ex)
            {
                fullTimer.Stop();

                MessageBox.Show(
                    $"Compression failed: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                btnCompress.Enabled = false;

                btnPlay.Enabled = false;

                btnCancel.Enabled = false;

                cmbAlgorithm.Enabled = true;

                cts?.Dispose();
            }
        }

        // =====================================================
        // DECOMPRESS
        // =====================================================

        private async void BtnDecompress_Click(
            object sender,
            EventArgs e)
        {
            if (string.IsNullOrEmpty(currentFilePath))
                return;

            string compPath =
                currentFilePath;

            string reconstructedPath =
                Path.Combine(
                    Path.GetDirectoryName(currentFilePath),
                    $"{Path.GetFileNameWithoutExtension(currentFilePath)}_reconstructed.wav");

            cts = new CancellationTokenSource();

            btnDecompress.Enabled = false;

            btnCompress.Enabled = false;

            progressBar.Value = 0;

            var progress =
                new Progress<CompressionProgress>(p =>
                {
                    SetProgressBarValue(p.PercentageComplete);
                });

            var decompressTimer = Stopwatch.StartNew();

            try
            {
                await engine.DecompressAudioAsync(
                    compPath,
                    reconstructedPath,
                    progress,
                    cts.Token);

                decompressTimer.Stop();
                SetProgressBarValue(100);

                long reconstructedBytes = new FileInfo(reconstructedPath).Length;

                CompressionHistory.UpdateReconstruction(
                    compPath,
                    reconstructedPath,
                    reconstructedBytes,
                    decompressTimer.ElapsedMilliseconds);

                var metrics = new CompressionMetrics
                {
                    CompressedBytes = new FileInfo(compPath).Length,
                    ReconstructedBytes = reconstructedBytes
                };

                MessageBox.Show(
                    metrics.BuildDecompressionReport(
                        decompressTimer.ElapsedMilliseconds),
                    "Decompression Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                LoadAudioFile(reconstructedPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Decompression failed: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                btnCompress.Enabled = true;

                cts?.Dispose();
            }
        }

        // =====================================================
        // CANCEL
        // =====================================================

        private void BtnCancel_Click(
            object sender,
            EventArgs e)
        {
            if (cts != null &&
                !cts.IsCancellationRequested)
            {
                cts.Cancel();

                btnCancel.Enabled = false;
            }
        }

        // =====================================================
        // COMPARISON LAB
        // =====================================================

        private void BtnComparison_Click(
            object sender,
            EventArgs e)
        {
            using var comparisonForm = new ComparisonForm();
            comparisonForm.ShowDialog(this);
        }

        private void SetProgressBarValue(int value)
        {
            progressBar.Value = CompressionProgressHelper.ClampPercent(value);
        }

        // =====================================================
        // RESET
        // =====================================================

        private void BtnReset_Click(
            object sender,
            EventArgs e)
        {
            CleanUpAudio();

            currentFilePath = null;

            lblProperties.Text =
                "Audio Properties:\n- Waiting for file...";

            progressBar.Value = 0;

            ((LineSeries)plotRatio.Model.Series[0])
                .Points.Clear();

            ((LineSeries)plotSpeed.Model.Series[0])
                .Points.Clear();

            plotRatio.InvalidatePlot(true);

            plotSpeed.InvalidatePlot(true);

            btnPlay.Enabled = false;

            btnStop.Enabled = false;

            btnCompress.Enabled = false;

            btnDecompress.Enabled = false;

            btnReset.Enabled = false;
        }

        // =====================================================
        // FORM CLOSED
        // =====================================================

        protected override void OnFormClosed(
            FormClosedEventArgs e)
        {
            CleanUpAudio();

            base.OnFormClosed(e);
        }
    }
}

