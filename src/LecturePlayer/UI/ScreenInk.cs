using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LecturePlayer.UI;

/// <summary>
/// أقلام على الشاشة كلها (فوق أي برنامج): قلم، تظليل، خط، سهم، مستطيل، دايرة، ممحاة، ليزر.
/// «الماوس» = الكليك بيعدّي للبرنامج اللي تحت (والرسم بيفضل ظاهر). الشريط بتاعها مابيتسجلش.
/// </summary>
internal sealed class ScreenInk : LayeredWindow
{
    public enum Tool { Mouse, Pen, Highlighter, Line, Arrow, Rect, Ellipse, Eraser, Laser }
    private sealed class Item
    {
        public Tool Tool; public Color Color; public float Width;
        public List<PointF> Pts = new();
        public RectangleF Bounds() { if (Pts.Count == 0) return RectangleF.Empty; float x0 = Pts.Min(p => p.X), y0 = Pts.Min(p => p.Y), x1 = Pts.Max(p => p.X), y1 = Pts.Max(p => p.Y); return RectangleF.FromLTRB(x0, y0, x1, y1); }
    }
    private readonly List<Item> _items = new();
    private readonly Stack<List<Item>> _undo = new();
    private Item? _cur;
    private readonly List<(PointF P, DateTime T)> _laser = new();
    private readonly System.Windows.Forms.Timer _laserTimer = new() { Interval = 30 };
    public Tool Current { get; private set; } = Tool.Pen;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Color Ink { get; set; } = Color.FromArgb(224, 49, 49);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public float PenWidth { get; set; } = 4;
    public int Count => _items.Count;
    public event Action? Changed;
    private ScreenInkBar? _bar;

    public ScreenInk(Screen screen) : base(clickThrough: false, noActivate: true)
    {
        Bounds = screen.Bounds;
        Cursor = Cursors.Cross;
        MouseDown += OnDown;
        MouseMove += OnMove;
        MouseUp += OnUp;
        _laserTimer.Tick += (_, _) => { _laser.RemoveAll(x => (DateTime.Now - x.T).TotalMilliseconds > 700); Render(); if (_laser.Count == 0 && Current != Tool.Laser) _laserTimer.Stop(); };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();
        // الشريط مملوك لشباك الرسم = دايمًا فوقه، وتحت في نص الشاشة (بعيد عن شريط التسجيل اللي فوق)
        _bar = new ScreenInkBar(this) { Owner = this };
        _bar.Show();
        var wa = Screen.FromRectangle(Bounds).WorkingArea;
        _bar.Location = new Point(wa.Left + (wa.Width - _bar.Width) / 2, wa.Bottom - _bar.Height - 16);
        ExcludeFromCapture(_bar);
        SetTool(Current);
    }

    public void SetTool(Tool t)
    {
        Current = t;
        ClickThrough = t == Tool.Mouse;
        Cursor = t == Tool.Eraser ? Cursors.Hand : t == Tool.Laser ? Cursors.Default : Cursors.Cross;
        if (t == Tool.Laser) _laserTimer.Start();
        // Esc من أي مكان بيرجّع الماوس وانت بترسم (الشباك ده مابياخدش الكيبورد عشان مايسرقش الفوكس)
        if (t == Tool.Mouse) _esc.Off(); else _esc.On(() => BeginInvoke(() => SetTool(Tool.Mouse)));
        _bar?.Sync();
        Render();
    }

    /// <summary>Esc عام (RegisterHotKey) — شغال بس وقت الرسم، وبيتشال أول ما ترجع للماوس.</summary>
    private sealed class EscKey : NativeWindow
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        private Action? _a;
        private bool _on;
        public void On(Action a)
        {
            _a = a;
            if (_on) return;
            if (Handle == IntPtr.Zero) CreateHandle(new CreateParams());
            _on = RegisterHotKey(Handle, 77, 0x4000, (uint)Keys.Escape);
        }
        public void Off() { if (_on) { UnregisterHotKey(Handle, 77); _on = false; } }
        public void Destroy() { Off(); if (Handle != IntPtr.Zero) DestroyHandle(); }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x0312 && m.WParam.ToInt32() == 77) _a?.Invoke(); base.WndProc(ref m); }
    }
    private readonly EscKey _esc = new();

    private void Snapshot() { _undo.Push(_items.Select(i => new Item { Tool = i.Tool, Color = i.Color, Width = i.Width, Pts = i.Pts.ToList() }).ToList()); if (_undo.Count > 100) { var keep = _undo.Take(100).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); } }
    public void Undo() { if (_undo.Count == 0) return; _items.Clear(); _items.AddRange(_undo.Pop()); Render(); Changed?.Invoke(); }
    public void ClearAll() { if (_items.Count == 0) return; Snapshot(); _items.Clear(); Render(); Changed?.Invoke(); }

    private void OnDown(object? s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { SetTool(Tool.Mouse); return; }   // كليك يمين = ارجع للماوس
        if (e.Button != MouseButtons.Left) return;
        var p = new PointF(e.X, e.Y);
        if (Current == Tool.Laser) { _laser.Add((p, DateTime.Now)); return; }
        if (Current == Tool.Eraser) { Snapshot(); EraseAt(p); return; }
        if (Current == Tool.Mouse) return;
        Snapshot();
        _cur = new Item { Tool = Current, Color = Current == Tool.Highlighter ? Color.FromArgb(90, Ink) : Ink, Width = Current == Tool.Highlighter ? PenWidth * 4 : PenWidth };
        _cur.Pts.Add(p);
        if (Current is Tool.Line or Tool.Arrow or Tool.Rect or Tool.Ellipse) _cur.Pts.Add(p);
        _items.Add(_cur);
    }

    private void OnMove(object? s, MouseEventArgs e)
    {
        var p = new PointF(e.X, e.Y);
        if (Current == Tool.Laser) { _laser.Add((p, DateTime.Now)); if (!_laserTimer.Enabled) _laserTimer.Start(); return; }
        if (e.Button != MouseButtons.Left) return;
        if (Current == Tool.Eraser) { EraseAt(p); return; }
        if (_cur == null) return;
        if (_cur.Tool is Tool.Pen or Tool.Highlighter) { if (Distance(_cur.Pts[^1], p) >= 1.5f) _cur.Pts.Add(p); }
        else _cur.Pts[1] = (Control.ModifierKeys & Keys.Shift) != 0 ? Constrain(_cur.Pts[0], p, _cur.Tool) : p;
        Render();
    }

    private void OnUp(object? s, MouseEventArgs e)
    {
        if (_cur != null) { if (_cur.Pts.Count == 1) _cur.Pts.Add(new PointF(_cur.Pts[0].X + 0.5f, _cur.Pts[0].Y + 0.5f)); _cur = null; Render(); Changed?.Invoke(); }
    }

    /// <summary>للفحص وللبرنامج: خط جاهز.</summary>
    public void AddStroke(Tool tool, Color c, float w, params PointF[] pts)
    {
        Snapshot();
        _items.Add(new Item { Tool = tool, Color = tool == Tool.Highlighter ? Color.FromArgb(90, c) : c, Width = w, Pts = pts.ToList() });
        Render(); Changed?.Invoke();
    }

    private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static PointF Constrain(PointF a, PointF b, Tool t)
    {
        if (t is Tool.Rect or Tool.Ellipse) { float m = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)); return new PointF(a.X + Math.Sign(b.X - a.X) * m, a.Y + Math.Sign(b.Y - a.Y) * m); }
        double ang = Math.Round(Math.Atan2(b.Y - a.Y, b.X - a.X) / (Math.PI / 12)) * (Math.PI / 12), len = Distance(a, b);
        return new PointF(a.X + (float)(Math.Cos(ang) * len), a.Y + (float)(Math.Sin(ang) * len));
    }

    private void EraseAt(PointF p)
    {
        int n = _items.RemoveAll(i => Hit(i, p, 10));
        if (n > 0) { Render(); Changed?.Invoke(); }
    }
    private static bool Hit(Item i, PointF p, float tol)
    {
        if (i.Tool is Tool.Rect or Tool.Ellipse) { var r = i.Bounds(); r.Inflate(tol, tol); return r.Contains(p) && !RectangleF.Inflate(i.Bounds(), -tol, -tol).Contains(p); }
        for (int k = 1; k < i.Pts.Count; k++) if (SegDist(p, i.Pts[k - 1], i.Pts[k]) <= tol + i.Width / 2) return true;
        return i.Pts.Count == 1 && Distance(p, i.Pts[0]) <= tol;
    }
    private static float SegDist(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, l = dx * dx + dy * dy;
        if (l == 0) return Distance(p, a);
        float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l, 0, 1);
        return Distance(p, new PointF(a.X + t * dx, a.Y + t * dy));
    }

    public void Render()
    {
        if (!IsHandleCreated || IsDisposed) return;
        using var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // شبه شفاف تمامًا (ألفا 1): الماوس بيتمسك في الشباك كله وانت بترسم، ومابيبانش
            if (!ClickThrough) using (var b = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(b, 0, 0, Width, Height);
            foreach (var it in _items) Draw(g, it);
            if (_laser.Count > 1)
                for (int k = 1; k < _laser.Count; k++)
                {
                    double life = 1 - (DateTime.Now - _laser[k].T).TotalMilliseconds / 700;
                    if (life <= 0) continue;
                    using var lp = new Pen(Color.FromArgb((int)(200 * life), 255, 40, 40), 2 + (float)(6 * life)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawLine(lp, _laser[k - 1].P, _laser[k].P);
                }
            if (_laser.Count > 0) { var h = _laser[^1].P; using var b = new SolidBrush(Color.FromArgb(230, 255, 40, 40)); g.FillEllipse(b, h.X - 7, h.Y - 7, 14, 14); }
        }
        Present(bmp);
    }

    private static void Draw(Graphics g, Item i)
    {
        using var pen = new Pen(i.Color, i.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (i.Pts.Count < 2) return;
        var a = i.Pts[0]; var b = i.Pts[^1];
        switch (i.Tool)
        {
            case Tool.Pen: case Tool.Highlighter:
                if (i.Pts.Count == 2) g.DrawLine(pen, a, b); else g.DrawCurve(pen, i.Pts.ToArray(), 0.4f);
                break;
            case Tool.Line: g.DrawLine(pen, a, b); break;
            case Tool.Arrow:
                using (var cap = new AdjustableArrowCap(4, 4)) { pen.CustomEndCap = cap; g.DrawLine(pen, a, b); }
                break;
            case Tool.Rect: g.DrawRectangle(pen, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)); break;
            case Tool.Ellipse: g.DrawEllipse(pen, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)); break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { if (Current != Tool.Mouse) SetTool(Tool.Mouse); else Close(); }
        else if (e.Control && e.KeyCode == Keys.Z) Undo();
        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _laserTimer.Stop();
        _esc.Destroy();
        _bar?.Close();
        base.OnFormClosed(e);
    }
}

/// <summary>شريط أقلام الشاشة (مابيتسجلش في الفيديو).</summary>
internal sealed class ScreenInkBar : Form
{
    private readonly ScreenInk _ink;
    private readonly FlowLayoutPanel _p = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4), BackColor = Color.FromArgb(43, 45, 49), WrapContents = false };
    private readonly List<(Button B, ScreenInk.Tool T)> _tools = new();
    public ScreenInkBar(ScreenInk ink)
    {
        _ink = ink;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; BackColor = Color.FromArgb(43, 45, 49);
        Controls.Add(_p);
        var grip = new Label { Text = "⠿", ForeColor = Color.Gray, AutoSize = false, Size = new Size(18, 32), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.SizeAll };
        grip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { LayeredWindow.ReleaseCapture(); LayeredWindow.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } };
        _p.Controls.Add(grip);
        void T(string text, string tip, ScreenInk.Tool t) { var b = Btn(text, tip, () => _ink.SetTool(t)); _tools.Add((b, t)); }
        // أهم زرارين أول حاجة وبكلام واضح: ترجع للماوس، أو تقفل الأقلام خالص
        var close = Btn("✕  قفل الأقلام", "قفل أقلام الشاشة (Ctrl+Shift+F8)", () => _ink.Close());
        close.Width = 118; close.BackColor = Color.FromArgb(200, 50, 50); close.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        var mouse = Btn("🖱  ماوس", "ارجع للماوس وتشتغل على البرامج والرسم ظاهر — Esc أو كليك يمين", () => _ink.SetTool(ScreenInk.Tool.Mouse));
        mouse.Width = 92; mouse.Font = new Font("Segoe UI Emoji", 10f);
        _tools.Add((mouse, ScreenInk.Tool.Mouse));
        T("🖊", "قلم", ScreenInk.Tool.Pen);
        T("🖍", "تظليل", ScreenInk.Tool.Highlighter);
        T("╱", "خط (Shift = زاوية مظبوطة)", ScreenInk.Tool.Line);
        T("➜", "سهم", ScreenInk.Tool.Arrow);
        T("▭", "مستطيل", ScreenInk.Tool.Rect);
        T("◯", "دايرة", ScreenInk.Tool.Ellipse);
        T("🧽", "ممحاة", ScreenInk.Tool.Eraser);
        T("🔴", "ليزر", ScreenInk.Tool.Laser);
        foreach (var c in new[] { Color.FromArgb(224, 49, 49), Color.FromArgb(25, 113, 194), Color.FromArgb(47, 158, 68), Color.FromArgb(245, 159, 0), Color.Black, Color.White })
        {
            var cc = c;
            var b = Btn("", "اللون", () => { _ink.Ink = cc; if (_ink.Current is ScreenInk.Tool.Mouse or ScreenInk.Tool.Eraser or ScreenInk.Tool.Laser) _ink.SetTool(ScreenInk.Tool.Pen); });
            b.BackColor = c; b.Width = 26;
        }
        foreach (var w in new[] { 2f, 4f, 8f }) { var ww = w; Btn(w switch { 2 => "·", 4 => "•", _ => "●" }, "المقاس", () => _ink.PenWidth = ww); }
        Btn("↶", "تراجع (Ctrl+Z)", _ink.Undo);
        Btn("🗑", "امسح الرسم", _ink.ClearAll);
        _p.Controls.Add(new Label { Text = "Esc / كليك يمين = ماوس", ForeColor = Color.Gray, AutoSize = false, Size = new Size(150, 32), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8.5f) });
    }

    private Button Btn(string text, string tip, Action a)
    {
        var b = new Button { Text = text, Size = new Size(38, 36), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(54, 57, 63), Margin = new Padding(2), Font = new Font("Segoe UI Emoji", 11f) };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) => a();
        new ToolTip().SetToolTip(b, tip);
        _p.Controls.Add(b);
        return b;
    }

    public void Sync() { foreach (var (b, t) in _tools) b.BackColor = t == _ink.Current ? Color.FromArgb(25, 113, 194) : Color.FromArgb(54, 57, 63); }

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000080; return cp; } } // NOACTIVATE | TOOLWINDOW
    protected override bool ShowWithoutActivation => true;
}

/// <summary>لمعة حوالين الماوس ودايرة عند كل كليك (بتظهر في التسجيل).</summary>
internal sealed class CursorFx : LayeredWindow
{
    private readonly System.Windows.Forms.Timer _t = new() { Interval = 16 };
    private readonly List<(Point P, DateTime T, bool Right)> _clicks = new();
    private bool _wasL, _wasR;
    private const int R = 60;
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    public CursorFx() : base(clickThrough: true, noActivate: true)
    {
        Size = new Size(R * 2, R * 2);
        _t.Tick += (_, _) => Tick();
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); _t.Start(); }

    private void Tick()
    {
        var c = Cursor.Position;
        bool l = (GetAsyncKeyState(0x01) & 0x8000) != 0, r = (GetAsyncKeyState(0x02) & 0x8000) != 0;
        if (l && !_wasL) _clicks.Add((c, DateTime.Now, false));
        if (r && !_wasR) _clicks.Add((c, DateTime.Now, true));
        _wasL = l; _wasR = r;
        _clicks.RemoveAll(x => (DateTime.Now - x.T).TotalMilliseconds > 500);
        Location = new Point(c.X - R, c.Y - R);
        using var bmp = new Bitmap(R * 2, R * 2, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var glow = new SolidBrush(Color.FromArgb(70, 255, 212, 59))) g.FillEllipse(glow, R - 22, R - 22, 44, 44);
            foreach (var (p, t, right) in _clicks)
            {
                double k = (DateTime.Now - t).TotalMilliseconds / 500;
                float rad = 10 + (float)(k * 40);
                using var pen = new Pen(Color.FromArgb((int)(220 * (1 - k)), right ? Color.FromArgb(25, 113, 194) : Color.FromArgb(224, 49, 49)), 3);
                g.DrawEllipse(pen, p.X - c.X + R - rad, p.Y - c.Y + R - rad, rad * 2, rad * 2);
            }
        }
        Present(bmp);
    }

    protected override void Dispose(bool disposing) { if (disposing) _t.Dispose(); base.Dispose(disposing); }
}
