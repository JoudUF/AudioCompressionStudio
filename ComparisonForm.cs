using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;

namespace AudioCompressionApp
{
    public class ComparisonForm : Form
    {
        private readonly CompressionEngine engine = new CompressionEngine();

        private TextBox txtSourceFile;
        private Button btnBrowse;
        private Button btnRunAll;
        private Button btnCancel;
        private Button btnClearHistory;
        private Button btnRefresh;
        private NumericUpDown numQuantization;
        private NumericUpDown numSampleRate;
        private ProgressBar progressBar;
        private Label lblStatus;
        private DataGridView gridHistory;

        private WaveOutEvent outputDevice;
        private AudioFileReader audioFile;
        private CancellationTokenSource cts;

        public ComparisonForm()
        {
            InitializeComponent();
            LoadHistoryGrid();
        }

        private void InitializeComponent()
        {
            Text = "Compression History";
            Size = new Size(1150, 650);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(950, 520);

            Label lblSource = new Label
            {
                Text = "Source Audio:",
                Location = new Point(20, 20),
                Size = new Size(100, 25)
            };

            txtSourceFile = new TextBox
            {
                Location = new Point(125, 18),
                Size = new Size(560, 25),
                ReadOnly = true
            };

            btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(695, 16),
                Size = new Size(90, 30)
            };
            btnBrowse.Click += BtnBrowse_Click;

            Label lblQ = new Label
            {
                Text = "Q-Levels:",
                Location = new Point(20, 58),
                Size = new Size(70, 25)
            };

            numQuantization = new NumericUpDown
            {
                Location = new Point(95, 56),
                Size = new Size(100, 25),
                Minimum = 2,
                Maximum = 65536,
                Value = 256
            };

            Label lblSR = new Label
            {
                Text = "Sample Rate:",
                Location = new Point(210, 58),
                Size = new Size(90, 25)
            };

            numSampleRate = new NumericUpDown
            {
                Location = new Point(305, 56),
                Size = new Size(120, 25),
                Minimum = 8000,
                Maximum = 48000,
                Value = 44100,
                Increment = 1000
            };

            btnRunAll = new Button
            {
                Text = "Test All Algorithms",
                Location = new Point(450, 52),
                Size = new Size(150, 32)
            };
            btnRunAll.Click += BtnRunAll_Click;

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(610, 52),
                Size = new Size(80, 32),
                Enabled = false
            };
            btnCancel.Click += BtnCancel_Click;

            btnRefresh = new Button
            {
                Text = "Refresh",
                Location = new Point(800, 16),
                Size = new Size(80, 30)
            };
            btnRefresh.Click += (s, e) => LoadHistoryGrid();

            btnClearHistory = new Button
            {
                Text = "Clear History",
                Location = new Point(890, 16),
                Size = new Size(110, 30)
            };
            btnClearHistory.Click += BtnClearHistory_Click;

            progressBar = new ProgressBar
            {
                Location = new Point(20, 95),
                Size = new Size(1100, 22),
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            lblStatus = new Label
            {
                Location = new Point(20, 125),
                Size = new Size(1100, 22),
                Text = "History of all compressed and reconstructed audio files.",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            gridHistory = new DataGridView
            {
                Location = new Point(20, 155),
                Size = new Size(1100, 440),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                    | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                MultiSelect = false
            };

            gridHistory.Columns.Add("Index", "#");
            gridHistory.Columns.Add("SourceFile", "Source File");
            gridHistory.Columns.Add("Algorithm", "Algorithm");
            gridHistory.Columns.Add("OriginalKB", "Original (KB)");
            gridHistory.Columns.Add("CompressedKB", "Compressed (KB)");
            gridHistory.Columns.Add("ReconstructedKB", "Reconstructed (KB)");
            gridHistory.Columns.Add("VsOriginal", "vs Original");

            var playColumn = new DataGridViewButtonColumn
            {
                Name = "Play",
                HeaderText = "Play",
                Text = "Play",
                UseColumnTextForButtonValue = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 60
            };
            gridHistory.Columns.Add(playColumn);

            gridHistory.Columns["Index"].FillWeight = 30;
            gridHistory.Columns["Index"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            gridHistory.Columns["Index"].Width = 40;
            gridHistory.Columns["SourceFile"].FillWeight = 110;
            gridHistory.Columns["Algorithm"].FillWeight = 100;
            gridHistory.Columns["VsOriginal"].FillWeight = 70;

            gridHistory.CellClick += GridHistory_CellClick;

            Controls.Add(lblSource);
            Controls.Add(txtSourceFile);
            Controls.Add(btnBrowse);
            Controls.Add(btnRefresh);
            Controls.Add(btnClearHistory);
            Controls.Add(lblQ);
            Controls.Add(numQuantization);
            Controls.Add(lblSR);
            Controls.Add(numSampleRate);
            Controls.Add(btnRunAll);
            Controls.Add(btnCancel);
            Controls.Add(progressBar);
            Controls.Add(lblStatus);
            Controls.Add(gridHistory);
        }

        private void LoadHistoryGrid()
        {
            gridHistory.Rows.Clear();
            List<CompressionHistoryEntry> entries = CompressionHistory.Load();

            for (int i = 0; i < entries.Count; i++)
            {
                AddEntryRow(i + 1, entries[i]);
            }

            lblStatus.Text = entries.Count == 0
                ? "No history yet. Compress from the main window or test all algorithms here."
                : $"{entries.Count} record(s) in history.";
        }

        private void AddEntryRow(int index, CompressionHistoryEntry entry)
        {
            int rowIndex = gridHistory.Rows.Add(
                index.ToString(),
                entry.SourceFileName,
                entry.Algorithm,
                FormatKb(entry.OriginalFileBytes),
                FormatKb(entry.CompressedBytes),
                entry.ReconstructedBytes > 0
                    ? FormatKb(entry.ReconstructedBytes)
                    : "-",
                FormatSavings(entry.SavingsVsOriginalPercent),
                "Play");

            gridHistory.Rows[rowIndex].Tag = entry;
        }

        private static string FormatKb(long bytes) =>
            bytes > 0 ? $"{bytes / 1024.0:F1}" : "-";

        private static string FormatSavings(double percent)
        {
            if (percent >= 0)
                return $"{percent:F1}% saved";

            return $"{Math.Abs(percent):F1}% larger";
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Audio Files|*.wav;*.mp3;*.aiff;*.aiff"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
                txtSourceFile.Text = ofd.FileName;
        }

        private async void BtnRunAll_Click(object sender, EventArgs e)
        {
            string sourcePath = txtSourceFile.Text;

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                MessageBox.Show(
                    "Please select a valid source audio file.",
                    "Missing File",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            StopPlayback();

            btnRunAll.Enabled = false;
            btnBrowse.Enabled = false;
            btnClearHistory.Enabled = false;
            btnCancel.Enabled = true;
            progressBar.Value = 0;

            cts = new CancellationTokenSource();

            string outputDir = Path.Combine(
                Path.GetDirectoryName(sourcePath) ?? Path.GetTempPath(),
                "AudioLab_Comparison");

            Directory.CreateDirectory(outputDir);

            var algorithms = (CompressionAlgorithm[])Enum.GetValues(
                typeof(CompressionAlgorithm));

            var settings = new CompressionSettings
            {
                QuantizationLevels = (int)numQuantization.Value,
                TargetSampleRate = (int)numSampleRate.Value
            };

            try
            {
                for (int i = 0; i < algorithms.Length; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    CompressionAlgorithm algo = algorithms[i];
                    string tag = GetAlgorithmTag(algo);
                    string baseName = Path.GetFileNameWithoutExtension(sourcePath);

                    string compressedPath = Path.Combine(
                        outputDir,
                        $"{baseName}_{tag}.pcomp");

                    string reconstructedPath = Path.Combine(
                        outputDir,
                        $"{baseName}_{tag}_reconstructed.wav");

                    lblStatus.Text = $"Running {GetAlgorithmName(algo)}...";

                    int overallPercent = (int)(i * 100.0 / algorithms.Length);
                    SetProgress(overallPercent);

                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    try
                    {
                        var progress = new Progress<CompressionProgress>(p =>
                        {
                            int algoSlice = 100 / algorithms.Length;
                            int combined = overallPercent
                                + (p.PercentageComplete * algoSlice / 100);
                            SetProgress(combined);
                        });

                        await engine.CompressAudioAsync(
                            sourcePath,
                            compressedPath,
                            algo,
                            settings,
                            progress,
                            cts.Token);

                        await engine.DecompressAudioAsync(
                            compressedPath,
                            reconstructedPath,
                            progress,
                            cts.Token);

                        sw.Stop();

                        var metrics = CompressionMetrics.Create(
                            sourcePath,
                            compressedPath,
                            settings,
                            reconstructedPath);

                        var entry = CompressionHistoryEntry.FromMetrics(
                            metrics,
                            sourcePath,
                            GetAlgorithmName(algo),
                            compressedPath,
                            reconstructedPath,
                            sw.ElapsedMilliseconds,
                            metrics.SavingsVsOriginalPercent < 0
                                ? "Expanded vs original"
                                : "Complete");

                        CompressionHistory.Add(entry);
                        LoadHistoryGrid();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();

                        var entry = new CompressionHistoryEntry
                        {
                            SourceFileName = Path.GetFileName(sourcePath),
                            SourceFilePath = sourcePath,
                            Algorithm = GetAlgorithmName(algo),
                            OriginalFileBytes = new FileInfo(sourcePath).Length,
                            CompressedPath = compressedPath,
                            ReconstructedPath = reconstructedPath,
                            TimeMs = sw.ElapsedMilliseconds,
                            Status = $"Error: {ex.Message}"
                        };

                        CompressionHistory.Add(entry);
                        LoadHistoryGrid();
                    }
                }

                SetProgress(100);
                lblStatus.Text = "Batch test complete. Results added to history.";
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Batch test cancelled.";
            }
            finally
            {
                btnRunAll.Enabled = true;
                btnBrowse.Enabled = true;
                btnClearHistory.Enabled = true;
                btnCancel.Enabled = false;
                cts?.Dispose();
                cts = null;
            }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            cts?.Cancel();
            btnCancel.Enabled = false;
        }

        private void BtnClearHistory_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Clear all compression history records?",
                "Clear History",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            CompressionHistory.Clear();
            LoadHistoryGrid();
        }

        private void GridHistory_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            if (gridHistory.Columns[e.ColumnIndex].Name != "Play")
                return;

            var entry = gridHistory.Rows[e.RowIndex].Tag as CompressionHistoryEntry;

            if (entry == null)
                return;

            string path = entry.ReconstructedPath;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show(
                    "No reconstructed audio available for this record.",
                    "Playback",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            PlayAudio(path);
        }

        private void PlayAudio(string path)
        {
            StopPlayback();

            outputDevice = new WaveOutEvent();
            audioFile = new AudioFileReader(path);
            outputDevice.Init(audioFile);
            outputDevice.PlaybackStopped += (s, e) => StopPlayback();
            outputDevice.Play();

            lblStatus.Text = $"Playing: {Path.GetFileName(path)}";
        }

        private void StopPlayback()
        {
            outputDevice?.Stop();
            outputDevice?.Dispose();
            outputDevice = null;

            audioFile?.Dispose();
            audioFile = null;
        }

        private void SetProgress(int value)
        {
            progressBar.Value = CompressionProgressHelper.ClampPercent(value);
        }

        private static string GetAlgorithmTag(CompressionAlgorithm algo) =>
            algo switch
            {
                CompressionAlgorithm.NonlinearQuantization => "NLQ",
                CompressionAlgorithm.DPCM => "DPCM",
                CompressionAlgorithm.DeltaModulation => "DM",
                CompressionAlgorithm.AdaptiveDeltaModulation => "ADM",
                CompressionAlgorithm.PredictiveDifferentialCoding => "PDC",
                _ => "COMP"
            };

        private static string GetAlgorithmName(CompressionAlgorithm algo) =>
            algo switch
            {
                CompressionAlgorithm.NonlinearQuantization =>
                    "Nonlinear Quantization",
                CompressionAlgorithm.DPCM => "DPCM",
                CompressionAlgorithm.DeltaModulation => "Delta Modulation",
                CompressionAlgorithm.AdaptiveDeltaModulation =>
                    "Adaptive Delta Modulation",
                CompressionAlgorithm.PredictiveDifferentialCoding =>
                    "Predictive Differential Coding",
                _ => algo.ToString()
            };

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopPlayback();
            cts?.Cancel();
            cts?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
