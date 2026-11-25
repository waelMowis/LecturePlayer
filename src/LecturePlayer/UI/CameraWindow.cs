using System.Drawing.Drawing2D;

namespace LecturePlayer.UI;

/// <summary>
/// شباك الكاميرا العايم (زي كامتازيا): دايرة/مستطيل مدوّر/مستطيل، إطار وظل، 4 مقاسات، يتسحب ويلزق في الأركان،
/// ومرآة. بيظهر في التسجيل لأنه على الشاشة (مشهد «الشاشة + كاميرا صغيرة»).
/// </summary>
internal sealed class CameraWindow : LayeredWindow
{
    public enum Shape { Circle, Rounded, Rect }
    public static readonly int[] Sizes = { 160, 220, 300, 400 };
    private Bitmap? _frame;
    private readonly object _lock = new();
    private const int Pad = 14; // مساحة الظل
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Shape CamShape { get; set; } = Shape.Circle;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public int SizeIndex { get; set; } = 1;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Color BorderColor { get; set; } = Color.White;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public int BorderWidth { get; set; } = 4;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool Shadow { get; set; } = true;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool Mirror { get; set; } = true;
    public event Action? SettingsChanged;
    public event Action? HideRequested;
    public int FramesShown { get; private set; }

    public CameraWindow() : base(clickThrough: false, noActivate: true)
    {
        Resize2();
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { DragMove(); SnapToCorner(); SettingsChanged?.Invoke(); } };
        MouseDoubleClick += (_, _) => { SizeIndex = (SizeIndex + 1) % Sizes.Length; Resize2(); Render(); SettingsChanged?.Invoke(); };
        MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowMenu(e.Location); };
    }

    private int Side => Sizes[Math.Clamp(SizeIndex, 0, Sizes.Length - 1)];
    private Size ContentSize => CamShape == Shape.Circle ? new Size(Side, Side) : new Size(Side * 16 / 9, Side);

    private void Resize2()
    {
        var c = ContentSize;
        var center = new Point(Left + Width / 2, Top + Height / 2);
        Size = new Size(c.Width + Pad * 2, c.Height + Pad * 2);
        if (Visible) Location = new Point(center.X - Width / 2, center.Y - Height / 2);
    }

    /// <summary>أول مرة: الركن تحت (عكس شريط المهام) على الشاشة اللي فيها «مسار».</summary>
    public void PlaceDefault(Screen screen, bool right)
    {
        var wa = screen.WorkingArea;
        Location = new Point(right ? wa.Right - Width - 16 : wa.Left + 16, wa.Bottom - Height - 16);
    }

    private void SnapToCorner()
    {
        var wa = Screen.FromControl(this).WorkingArea;
        const int snap = 60;
        int x = Left, y = Top;
        if (Math.Abs(Left - wa.Left) < snap) x = wa.Left + 8;
        if (Math.Abs(wa.Right - Right) < snap) x = wa.Right - Width - 8;
        if (Math.Abs(Top - wa.Top) < snap) y = wa.Top + 8;
        if (Math.Abs(wa.Bottom - Bottom) < snap) y = wa.Bottom - Height - 8;
        Location = new Point(x, y);
        Render();
    }

    /// <summary>صورة جديدة من الكاميرا (من خيط تاني).</summary>
    public void SetFrame(byte[] bgra)
    {
        var bmp = FromBgra(bgra, Data.CameraFeed.PreviewW, Data.CameraFeed.PreviewH);
        lock (_lock) { _frame?.Dispose(); _frame = bmp; }
        if (IsHandleCreated && !IsDisposed) try { BeginInvoke(Render); } catch { }
    }

    public void Render()
    {
        if (!IsHandleCreated || IsDisposed) return;
        var c = ContentSize;
        using var bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            var rect = new Rectangle(Pad, Pad, c.Width, c.Height);
            using var path = ShapePath(rect);
            if (Shadow)
                for (int i = Pad; i > 0; i -= 2)
                {
                    using var sp = ShapePath(Rectangle.Inflate(rect, i, i));
                    using var sb = new SolidBrush(Color.FromArgb((int)(28 * (1 - (double)i / Pad)) + 4, 0, 0, 0));
                    g.FillPath(sb, sp);
                }
            g.SetClip(path);
            lock (_lock)
            {
                if (_frame != null)
                {
                    // الصورة 16:9: الدايرة بتاخد النص
                    var src = CamShape == Shape.Circle ? new Rectangle((_frame.Width - _frame.Height) / 2, 0, _frame.Height, _frame.Height) : new Rectangle(0, 0, _frame.Width, _frame.Height);
                    g.DrawImage(_frame, rect, src, GraphicsUnit.Pixel);
                    FramesShown++;
                }
                else
                {
                    g.Clear(Color.FromArgb(255, 40, 42, 46));
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.DirectionRightToLeft };
                    g.DrawString("جارٍ فتح الكاميرا…", Font, Brushes.Gainsboro, rect, sf);
                }
            }
            g.ResetClip();
            if (BorderWidth > 0) { using var pen = new Pen(BorderColor, BorderWidth); g.DrawPath(pen, path); }
        }
        Present(bmp);
    }

    private GraphicsPath ShapePath(Rectangle r)
    {
        var p = new GraphicsPath();
        if (CamShape == Shape.Circle) p.AddEllipse(r);
        else if (CamShape == Shape.Rect) p.AddRectangle(r);
        else
        {
            int d = Math.Max(2, r.Height / 5);
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        }
        return p;
    }

    private void ShowMenu(Point at)
    {
        var m = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
        void Item(string text, bool check, Action a) { var i = new ToolStripMenuItem(text) { Checked = check }; i.Click += (_, _) => { a(); Resize2(); Render(); SettingsChanged?.Invoke(); }; m.Items.Add(i); }
        Item("⭕ دائرة", CamShape == Shape.Circle, () => CamShape = Shape.Circle);
        Item("▢ مستطيل مستدير الزوايا", CamShape == Shape.Rounded, () => CamShape = Shape.Rounded);
        Item("▭ مستطيل", CamShape == Shape.Rect, () => CamShape = Shape.Rect);
        m.Items.Add(new ToolStripSeparator());
        string[] names = { "صغير", "متوسط", "كبير", "كبير جدًا" };
        for (int i = 0; i < Sizes.Length; i++) { int k = i; Item("المقاس: " + names[i], SizeIndex == i, () => SizeIndex = k); }
        m.Items.Add(new ToolStripSeparator());
        Item("إطار", BorderWidth > 0, () => BorderWidth = BorderWidth > 0 ? 0 : 4);
        Item("ظل", Shadow, () => Shadow = !Shadow);
        Item("مرآة (كما ترى نفسك)", Mirror, () => Mirror = !Mirror);
        m.Items.Add(new ToolStripSeparator());
        var hide = new ToolStripMenuItem("إخفاء الكاميرا (Ctrl+Shift+F7)"); hide.Click += (_, _) => HideRequested?.Invoke(); m.Items.Add(hide);
        m.Show(this, at);
    }

    protected override void Dispose(bool disposing) { if (disposing) lock (_lock) { _frame?.Dispose(); _frame = null; } base.Dispose(disposing); }
}
