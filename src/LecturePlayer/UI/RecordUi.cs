namespace LecturePlayer.UI;

/// <summary>شريط التسجيل الصغير (فوق كل حاجة، ومابيظهرش في الفيديو).</summary>
internal sealed class RecordBar : Form
{
    private readonly Label _time = new() { AutoSize = false, Size = new Size(86, 32), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
    private readonly Label _dot = new() { AutoSize = false, Size = new Size(18, 32), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(240, 60, 60), Text = "●", Font = new Font("Segoe UI", 12f) };
    private readonly Button _pause, _cam, _scene;
    private readonly FlowLayoutPanel _p = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4), WrapContents = false, BackColor = Color.FromArgb(32, 33, 36) };
    private readonly System.Windows.Forms.Timer _blink = new() { Interval = 600 };
    public event Action<string>? Command;

    public RecordBar(bool camera, bool mic = false)
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; BackColor = Color.FromArgb(32, 33, 36);
        Controls.Add(_p);
        // السحب بالطريقة بتاعة ويندوز نفسها (مابيرعشش ولا يتنطط زي حساب الإزاحة يدوي)
        void Drag(object? _, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { LayeredWindow.ReleaseCapture(); LayeredWindow.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } }
        _dot.MouseDown += Drag; _time.MouseDown += Drag; _p.MouseDown += Drag;
        _dot.Cursor = _time.Cursor = Cursors.SizeAll;
        _p.Controls.Add(_dot);
        _p.Controls.Add(_time);
        _p.Controls.Add(_mic);
        new ToolTip().SetToolTip(_mic, "مستوى المايك");
        _pause = Btn("⏸", "إيقاف مؤقت / كمّل (Ctrl+Shift+F10)", "pause");
        Btn("⏹", "إنهاء التسجيل (Ctrl+Shift+F9)", "stop").BackColor = Color.FromArgb(200, 50, 50);
        Btn("🔖", "علامة هنا", "mark");
        Btn("🖊", "أقلام على الشاشة (Ctrl+Shift+F8)", "ink");
        _cam = Btn("📷", "إظهار/إخفاء الكاميرا (Ctrl+Shift+F7)", "cam");
        _cam.Visible = camera;
        _scene = Btn("🎬", "المشهد", "scene");
        _scene.Visible = camera;
        Btn("✕", "إلغاء التسجيل (من غير حفظ)", "cancel");
        // النقطة بتنور وتطفي باللون بس — لو اختفت (Visible) الشريط كله بيتحرك من مكانه
        _mic.Visible = mic;
        bool lit = true;
        _blink.Tick += (_, _) => { lit = !lit || _paused; _dot.ForeColor = _paused ? Color.Orange : lit ? Color.FromArgb(240, 60, 60) : Color.FromArgb(90, 40, 40); };
        _blink.Start();
    }

    private readonly Label _mic = new() { AutoSize = false, Size = new Size(30, 32), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gray, Text = "🎙", Font = new Font("Segoe UI Emoji", 11f), Visible = false };
    private float _shown;

    /// <summary>مستوى المايك: أخضر بيتحرك مع الصوت، وأحمر لو المايك بيبعت سكوت تام.</summary>
    public void SetMic(float? level, bool silent)
    {
        if (level is not { } l) return;
        _shown = Math.Max(l, _shown * 0.7f);
        _mic.BackColor = silent ? Color.FromArgb(150, 40, 40)
            : Color.FromArgb(32 + (int)(Math.Min(1, _shown * 4) * 30), 33 + (int)(Math.Min(1, _shown * 4) * 120), 36 + (int)(Math.Min(1, _shown * 4) * 30));
    }

    private bool _paused;
    public void SetTime(TimeSpan t, bool paused)
    {
        _time.Text = (t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss"));
        if (paused != _paused) { _paused = paused; _pause.Text = paused ? "⏺" : "⏸"; _dot.ForeColor = paused ? Color.Orange : Color.FromArgb(240, 60, 60); }
    }

    private Button Btn(string text, string tip, string cmd)
    {
        var b = new Button { Text = text, Size = new Size(36, 32), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(54, 57, 63), Margin = new Padding(2), Font = new Font("Segoe UI Emoji", 11f) };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) => Command?.Invoke(cmd);
        new ToolTip().SetToolTip(b, tip);
        _p.Controls.Add(b);
        return b;
    }

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000080; return cp; } }
    protected override bool ShowWithoutActivation => true;
    protected override void Dispose(bool disposing) { if (disposing) _blink.Dispose(); base.Dispose(disposing); }
}

/// <summary>تحديد جزء من الشاشة بالسحب (Esc يلغي).</summary>
internal sealed class RegionPicker : Form
{
    private Point _a;
    private Rectangle _r;
    private bool _down;
    public Rectangle? Result { get; private set; }

    public RegionPicker()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        BackColor = Color.Black; Opacity = 0.35; Cursor = Cursors.Cross; DoubleBuffered = true; KeyPreview = true;
        MouseDown += (_, e) => { _down = true; _a = e.Location; _r = Rectangle.Empty; };
        MouseMove += (_, e) => { if (!_down) return; _r = Rectangle.FromLTRB(Math.Min(_a.X, e.X), Math.Min(_a.Y, e.Y), Math.Max(_a.X, e.X), Math.Max(_a.Y, e.Y)); Invalidate(); };
        MouseUp += (_, _) =>
        {
            _down = false;
            if (_r.Width > 40 && _r.Height > 40) { Result = new Rectangle(_r.X + Left, _r.Y + Top, _r.Width, _r.Height); DialogResult = DialogResult.OK; }
            Close();
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, "اسحب مستطيل حوالين الجزء اللي هتسجّله — Esc للإلغاء", new Font("Segoe UI", 16f), new Point(40, 40), Color.White);
        if (_r.Width > 0) { using var p = new Pen(Color.FromArgb(79, 195, 247), 3); e.Graphics.DrawRectangle(p, _r); using var b = new SolidBrush(Color.FromArgb(60, 255, 255, 255)); e.Graphics.FillRectangle(b, _r); TextRenderer.DrawText(e.Graphics, $"{_r.Width}×{_r.Height}", Font, new Point(_r.X + 6, _r.Y + 6), Color.White); }
    }
}

/// <summary>عد تنازلي 3-2-1 قبل التسجيل (مابيتسجلش).</summary>
internal sealed class Countdown : LayeredWindow
{
    public Countdown(Rectangle area) : base(clickThrough: true, noActivate: true)
    {
        Size = new Size(220, 220);
        Location = new Point(area.X + (area.Width - 220) / 2, area.Y + (area.Height - 220) / 2);
    }

    public async Task RunAsync(int seconds)
    {
        Show();
        ExcludeFromCapture(this);
        for (int i = seconds; i > 0; i--)
        {
            using var bmp = new Bitmap(220, 220, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var b = new SolidBrush(Color.FromArgb(200, 20, 20, 24)); g.FillEllipse(b, 10, 10, 200, 200);
                using var f = new Font("Segoe UI", 72f, FontStyle.Bold);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; // TextRenderer بيكتب ألفا 0 على الشباك الشفاف
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(i.ToString(), f, Brushes.White, new RectangleF(10, 10, 200, 200), sf);
            }
            Present(bmp);
            await Task.Delay(1000);
        }
        Close();
    }
}
