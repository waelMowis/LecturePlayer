namespace LecturePlayer.UI;

/// <summary>
/// مساحة العرض التقديمي جوه «مسار»: شباك PowerPoint (بالأنيميشن) + شريط تحكم تحت (التنقل، أقلام PowerPoint، الممحاة، روح لشريحة).
/// </summary>
internal sealed class PresentationPane : Panel
{
    private readonly Panel _host = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private readonly FlowLayoutPanel _bar = new() { Dock = DockStyle.Bottom, Height = 44, BackColor = Color.FromArgb(43, 45, 49), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6, 6, 6, 0), WrapContents = false };
    private readonly Label _pos = new() { AutoSize = false, Width = 120, Height = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gainsboro };
    private readonly ComboBox _slides = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Height = 28, FlatStyle = FlatStyle.Flat };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 300 };
    private PowerPointShow? _show;
    private Form? _owner;
    private int _last;
    private readonly List<(PillButton Btn, int Type, Color? Color)> _penButtons = new();

    public event Action<int>? SlideChanged;
    public event Action? Ended;
    public event Action<string>? Command;
    public PowerPointShow? Show => _show;
    public bool Running => _show != null;
    public int Total { get; private set; }
    public bool Embedded => _show?.Embedded ?? false;

    public PresentationPane()
    {
        BackColor = Color.Black;
        Visible = false;
        Controls.Add(_host);
        Controls.Add(_bar);
        PillButton B(string text, string tip, Action click, PillStyle style = PillStyle.Ghost)
        {
            var b = new PillButton(text, style) { Height = 30, Margin = new Padding(3, 0, 3, 0) };
            b.Width = Math.Max(36, TextRenderer.MeasureText(text, Font).Width + 22);
            b.Click += (_, _) => { click(); _show?.Focus(); };
            new ToolTip().SetToolTip(b, tip);
            _bar.Controls.Add(b);
            return b;
        }
        B("⏹ إنهاء", "إنهاء العرض (Esc)", () => Command?.Invoke("end"), PillStyle.Subtle);
        B("⏮", "أول شريحة (Home)", () => _show?.GoTo(1));
        B("›", "الشريحة اللي قبلها", () => _show?.Previous());
        _bar.Controls.Add(_pos);
        B("‹", "اللي بعدها (المسافة / السهم)", () => _show?.Next());
        _slides.Margin = new Padding(6, 1, 6, 0);
        _slides.SelectedIndexChanged += (_, _) => { if (_slides.Focused && _slides.SelectedIndex >= 0) { _show?.GoTo(_slides.SelectedIndex + 1); _show?.Focus(); } };
        _bar.Controls.Add(_slides);
        void Pen(string text, string tip, int type, Color? c) { var b = B(text, tip, () => SetPointer(type, c)); _penButtons.Add((b, type, c)); }
        Pen("➤", "الماوس العادي", PowerPointShow.PointerAutoArrow, null);
        Pen("🔴", "قلم أحمر", PowerPointShow.PointerPen, Color.FromArgb(224, 49, 49));
        Pen("🔵", "قلم أزرق", PowerPointShow.PointerPen, Color.FromArgb(25, 113, 194));
        Pen("🟢", "قلم أخضر", PowerPointShow.PointerPen, Color.FromArgb(47, 158, 68));
        Pen("🟡", "قلم أصفر", PowerPointShow.PointerPen, Color.FromArgb(245, 159, 0));
        Pen("🧽", "ممحاة", PowerPointShow.PointerEraser, null);
        B("🗑", "امسح الكتابة من الشريحة", () => _show?.EraseDrawing());
        B("🎬", "حوّل العرض لفيديو", () => Command?.Invoke("video"));
        _host.Resize += (_, _) => Fit();
        _poll.Tick += (_, _) => Poll();
    }

    private void SetPointer(int type, Color? c)
    {
        _show?.Pointer(type, c);
        foreach (var (btn, t, col) in _penButtons) btn.Style = t == type && col == c ? PillStyle.Primary : PillStyle.Ghost;
    }

    /// <summary>بيشغّل العرض. null = تمام، غير كده رسالة الخطأ.</summary>
    public string? Start(string path, int fromSlide, bool useTimings, IList<string> titles)
    {
        Stop();
        Visible = true;
        BringToFront();
        _host.CreateControl();
        var show = new PowerPointShow();
        try
        {
            show.Open(path);
            Total = show.SlideCount;
            show.Start(_host, Math.Clamp(fromSlide, 1, Math.Max(1, Total)), useTimings);
        }
        catch (Exception ex)
        {
            show.Dispose();
            Data.Log.Info("powerpoint start: " + ex);
            return ex is System.Runtime.InteropServices.COMException ? "PowerPoint رفض يفتح الملف: " + ex.Message : ex.Message;
        }
        _show = show;
        _last = 0;
        _slides.Items.Clear();
        for (int i = 1; i <= Total; i++) _slides.Items.Add($"{i}. " + (i <= titles.Count && titles[i - 1].Length > 0 ? titles[i - 1] : "شريحة " + i));
        if (!show.Embedded)
        {
            // ماقدرناش نحطه جوه: شباك PowerPoint فوق المساحة بالظبط ويتحرك معاها
            _owner = FindForm();
            if (_owner != null) { _owner.Move += OwnerMoved; _owner.Resize += OwnerMoved; }
        }
        Fit();
        _poll.Start();
        Poll();
        show.Focus();
        Data.Log.Info($"powerpoint {show.Version}: {Total} slides, embedded={show.Embedded}");
        return null;
    }

    private void OwnerMoved(object? s, EventArgs e) => Fit();
    public void Fit() { if (_show != null) _show.Fit(_host, _show.Embedded); }

    private void Poll()
    {
        if (_show == null) return;
        if (_show.Ended) { _poll.Stop(); Ended?.Invoke(); return; }
        int cur = _show.Current;
        if (cur <= 0 || cur == _last) return;
        _last = cur;
        _pos.Text = $"شريحة {cur} من {Total}";
        if (!_slides.Focused && cur - 1 < _slides.Items.Count) _slides.SelectedIndex = cur - 1;
        SlideChanged?.Invoke(cur);
    }

    public int Current => _last;

    /// <summary>بيقفل العرض. keep = عدد الشرايح اللي عليها كتابة اتحفظت (قبل القفل) لو حد عايز يعمل نسخة.</summary>
    public void Stop()
    {
        _poll.Stop();
        if (_owner != null) { _owner.Move -= OwnerMoved; _owner.Resize -= OwnerMoved; _owner = null; }
        var s = _show; _show = null;
        if (s != null) { try { s.Dispose(); } catch (Exception ex) { Data.Log.Info("powerpoint stop: " + ex.Message); } }
        Visible = false;
    }

    protected override void Dispose(bool disposing) { if (disposing) { Stop(); _poll.Dispose(); } base.Dispose(disposing); }
}
