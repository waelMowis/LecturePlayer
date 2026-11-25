using System.ComponentModel;
using System.Drawing.Drawing2D;
using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>
/// «التنزيلات»: كل تنزيل في صف — الاسم، المرحلة والتقدم والسرعة والوقت الفاضل، وأزرار
/// (إلغاء / حاول تاني / كمّل / شغّل / الفولدر / امسح). شباك عادي جنب البرنامج (مش بيقفل الشغل).
/// </summary>
internal sealed class DownloadsPanel : Form
{
    private readonly MainForm _main;
    private readonly AppSettings _settings;
    private readonly Panel _list = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Window, Padding = new Padding(10, 6, 10, 6) };
    private readonly Label _empty = new()
    {
        Text = "لا توجد تنزيلات.\nاضغط «＋ تنزيل جديد» أو Ctrl+D، أو انقر بالزر الأيمن على فيديو يوتيوب في المكتبة.",
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 10f)
    };

    public DownloadsPanel(MainForm main, AppSettings settings)
    {
        _main = main;
        _settings = settings;
        Text = "التنزيلات";
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 460);
        MinimumSize = new Size(480, 300);
        ShowInTaskbar = false;
        ShowIcon = false;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        HandleCreated += (_, _) => NativeMethods.UseDarkTitleBar(this);

        // ---------- الشريط اللي فوق ----------
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, BackColor = Theme.Panel, Padding = new Padding(10, 9, 10, 0), WrapContents = false, RightToLeft = RightToLeft.Yes };
        var add = new PillButton("＋  تنزيل جديد", PillStyle.Primary) { Height = 32 };
        add.Click += (_, _) => _main.OpenDownloadDialog(null);
        var clear = new PillButton("امسح المكتمل", PillStyle.Ghost) { Height = 32 };
        clear.Click += (_, _) => _main.ClearFinishedDownloads();
        var folder = new PillButton("📂  مجلد التنزيلات", PillStyle.Ghost) { Height = 32 };
        folder.Click += (_, _) =>
        {
            string f = string.IsNullOrWhiteSpace(_settings.DownloadFolder) ? DownloadForm.DefaultFolder : _settings.DownloadFolder;
            Directory.CreateDirectory(f);
            _main.OpenFolder(f);
        };
        var parallel = new SelectBox { Width = 150, Height = 32 };
        parallel.Items.AddRange(new[] { "واحدًا تلو الآخر", "اثنان معًا" });
        parallel.SelectedIndex = Math.Clamp(_settings.DownloadParallel, 1, 2) - 1;
        parallel.SelectedIndexChanged += (_, _) => { _settings.DownloadParallel = parallel.SelectedIndex + 1; Storage.SaveSettings(_settings); };
        foreach (var c in new Control[] { add, clear, folder, parallel }) { c.Margin = new Padding(0, 0, 8, 0); top.Controls.Add(c); }

        Controls.Add(_list);
        Controls.Add(top);
        _main.DownloadListChanged += Rebuild;
        FormClosed += (_, _) => _main.DownloadListChanged -= Rebuild;
        Rebuild();
    }

    /// <summary>للفحص: عدد الصفوف الظاهرة.</summary>
    internal int RowCount => _list.Controls.OfType<Row>().Count();

    private void Rebuild()
    {
        if (IsDisposed) return;
        _list.SuspendLayout();
        foreach (Control c in _list.Controls.Cast<Control>().ToList()) { _list.Controls.Remove(c); c.Dispose(); }
        var jobs = _main.DownloadJobs;
        if (jobs.Count == 0) _list.Controls.Add(_empty);
        // Dock=Top: اللي بيتضاف آخر بيبقى فوق، فبنضيف من الآخر عشان الأقدم يبقى فوق
        for (int i = jobs.Count - 1; i >= 0; i--)
            _list.Controls.Add(new Row(_main, jobs[i]) { Dock = DockStyle.Top });
        _list.ResumeLayout(true);
    }

    /// <summary>صف تنزيل واحد.</summary>
    private sealed class Row : Panel
    {
        private readonly MainForm _main;
        private readonly DownloadJob _job;
        private readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Left, Width = 250, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 22, 0, 0) };
        private DownloadStatus _shownStatus = (DownloadStatus)(-1);

        public Row(MainForm main, DownloadJob job)
        {
            _main = main;
            _job = job;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 82;
            Margin = new Padding(0, 0, 0, 8);
            Padding = new Padding(0, 0, 0, 8);
            BackColor = Theme.Window;
            RightToLeft = RightToLeft.No;
            Controls.Add(_buttons);
            _job.Changed += OnChanged;
            Disposed += (_, _) => _job.Changed -= OnChanged;
            BuildButtons();
        }

        private void OnChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(OnChanged); return; }
            if (_job.Status != _shownStatus) BuildButtons();
            Invalidate();
        }

        private void BuildButtons()
        {
            _shownStatus = _job.Status;
            _buttons.SuspendLayout();
            foreach (Control c in _buttons.Controls.Cast<Control>().ToList()) { _buttons.Controls.Remove(c); c.Dispose(); }
            void Btn(string text, Action click, PillStyle style = PillStyle.Ghost)
            {
                var b = new PillButton(text, style) { Height = 28, Margin = new Padding(0, 0, 6, 0) };
                b.Font = new Font("Segoe UI", 9f);
                b.AutoSizeToText();
                b.Click += (_, _) => click();
                _buttons.Controls.Add(b);
            }
            switch (_job.Status)
            {
                case DownloadStatus.Queued:
                case DownloadStatus.Running:
                    Btn("إلغاء", () => _main.CancelDownload(_job));
                    break;
                case DownloadStatus.Paused:
                    Btn("▶ أكمل", () => _main.RetryDownload(_job), PillStyle.Primary);
                    Btn("مسح", () => _main.RemoveDownload(_job));
                    break;
                case DownloadStatus.Failed:
                case DownloadStatus.Canceled:
                    Btn("↻ حاول مرة أخرى", () => _main.RetryDownload(_job), PillStyle.Primary);
                    Btn("مسح", () => _main.RemoveDownload(_job));
                    break;
                case DownloadStatus.Done:
                    Btn("▶ شغّل", () => _main.PlayDownloaded(_job), PillStyle.Primary);
                    if (_job.File != null) Btn("📂", () => _main.RevealFile(_job.File));
                    Btn("مسح", () => _main.RemoveDownload(_job));
                    break;
            }
            _buttons.ResumeLayout(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = new RectangleF(0.5f, 0.5f, Width - 1, Height - Padding.Bottom - 1);
            Modern.FillRound(g, Theme.Panel, card, 8);

            int right = Width - 14, left = _buttons.Right + 8, w = Math.Max(40, right - left);
            var flags = TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

            // العنوان + نوع الملف
            string kind = _job.Spec.Kind switch { DownloadKind.Mp3 => $"MP3 {_job.Spec.Mp3Kbps}", DownloadKind.M4a => "M4A", _ => _job.Spec.MaxHeight > 0 ? $"MP4 {_job.Spec.MaxHeight}p" : "MP4" };
            using var bold = new Font("Segoe UI", 10f, FontStyle.Bold);
            using var small = new Font("Segoe UI", 8.8f);
            TextRenderer.DrawText(g, _job.Spec.Title, bold, new Rectangle(left, 8, w - 80, 22), Theme.Text, flags);
            TextRenderer.DrawText(g, kind, small, new Rectangle(left, 10, 76, 20), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // سطر الحالة
            var p = _job.Progress;
            string line; Color color = Theme.Muted;
            switch (_job.Status)
            {
                case DownloadStatus.Running:
                    var parts = new List<string> { _job.Stage ?? "جارٍ التنزيل…" };
                    if (p != null && p.Percent > 0 && p.Percent < 100) parts.Add($"{p.Percent:0}%");
                    if (p?.TotalBytes > 0) parts.Add(YtDlp.FormatSize(p.TotalBytes));
                    if (p?.BytesPerSecond > 0) parts.Add(YtDlp.FormatSize((long)p.BytesPerSecond) + "/ث");
                    if (p?.EtaSeconds > 0) parts.Add("متبقٍ " + (p.EtaSeconds >= 3600 ? TimeSpan.FromSeconds(p.EtaSeconds).ToString(@"h\:mm\:ss") : TimeSpan.FromSeconds(p.EtaSeconds).ToString(@"m\:ss")));
                    line = string.Join("  •  ", parts);
                    color = Theme.Accent;
                    break;
                case DownloadStatus.Done:
                    line = "✓ اكتمل — " + (_job.Spec.LibraryUrl != null ? "داخل المكتبة (يعمل دون إنترنت)" : Path.GetFileName(_job.File ?? ""));
                    color = Theme.Success;
                    break;
                case DownloadStatus.Failed:
                    line = "✗ " + (_job.Error ?? "فشل").Split('\n')[0];
                    color = Theme.Danger;
                    break;
                case DownloadStatus.Canceled: line = "أُلغي"; break;
                case DownloadStatus.Paused: line = "متوقف — سيكمل من حيث توقف"; break;
                default: line = "في الانتظار — نحو " + YtDlp.FormatSize(_job.Spec.EstimatedBytes); break;
            }
            TextRenderer.DrawText(g, line, small, new Rectangle(left, 32, w, 18), color, flags);

            // شريط التقدم
            if (_job.Status is DownloadStatus.Running or DownloadStatus.Done or DownloadStatus.Paused)
            {
                var bar = new RectangleF(left, 56, w, 5);
                Modern.FillRound(g, Theme.PanelLight, bar, 2.5f);
                double pct = _job.Status == DownloadStatus.Done ? 100 : p?.Percent ?? 0;
                if (pct > 0)
                {
                    float fw = (float)(bar.Width * pct / 100);
                    Modern.FillRound(g, _job.Status == DownloadStatus.Done ? Theme.Success : Theme.Accent, new RectangleF(bar.Right - fw, bar.Y, fw, bar.Height), 2.5f);
                }
            }
            // الخطأ كامل في التلميح
            string? tip = _job.Status == DownloadStatus.Failed ? _job.Error : _job.Status == DownloadStatus.Done ? _job.File : null;
            if (tip != null && tip != _tipText) { _tip ??= new ToolTip(); _tip.SetToolTip(this, tip); _tipText = tip; }
        }

        private ToolTip? _tip;
        private string? _tipText;
        protected override void Dispose(bool disposing) { if (disposing) _tip?.Dispose(); base.Dispose(disposing); }
    }
}
