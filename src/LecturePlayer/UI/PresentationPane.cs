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
    private int _pendingSlide;
    private DateTime _pendingUntil;
    private readonly List<(PillButton Btn, int Type, Color? Color)> _penButtons = new();
    private PillButton _erase = null!, _inkBtn = null!;
    private ScreenInk? _ink;
    internal ScreenInk? InkOverlay => _ink;
    private readonly Dictionary<int, List<ScreenInk.Item>> _inkPages = new();
    private int _inkPage;

    /// <summary>أدوات الشرح فوق الشريحة (شباك شفاف مملوك لـ «مسار» فوق مساحة العرض بالظبط).</summary>
    public void ToggleInk()
    {
        if (_ink is { IsDisposed: false }) { _ink.Close(); return; }   // FormClosed بيحفظ رسم الشريحة
        if (!Running) return;
        var form = FindForm();
        _ink = new ScreenInk(_host.RectangleToScreen(_host.ClientRectangle), overlay: true);
        if (form != null) _ink.Owner = form;
        _ink.FormClosed += (_, _) => { if (_ink != null) { _inkPages[_inkPage] = _ink.TakePage(); _ink = null; } _inkBtn.Style = PillStyle.Primary; };
        _inkPage = Math.Max(1, _last);
        _ink.Show();
        _ink.LoadPage(_inkPages.GetValueOrDefault(_inkPage));
        _inkBtn.Style = PillStyle.Ghost;
    }

    /// <summary>الشريحة اتغيرت: الرسم بتاع القديمة يتحفظ، وبتاع الجديدة يرجع.</summary>
    private void InkSlide(int n)
    {
        if (_ink == null || n == _inkPage) return;
        _inkPages[_inkPage] = _ink.TakePage();
        _inkPage = n;
        _ink.LoadPage(_inkPages.GetValueOrDefault(n));
    }

    private void InkFollow() { if (_ink is { IsDisposed: false } && _host.IsHandleCreated && Visible) _ink.MoveTo(_host.RectangleToScreen(_host.ClientRectangle)); }

    public event Action<int>? SlideChanged;
    public event Action? Ended;
    public event Action<string>? Command;
    public PowerPointShow? Show => _show;
    public bool Running => _show != null || _images != null;
    public int Total { get; private set; }
    public bool Embedded => _show?.Embedded ?? _images != null;

    public PresentationPane()
    {
        BackColor = Color.Black;
        Visible = false;
        Controls.Add(_host);
        _host.Controls.Add(_view);
        Controls.Add(_bar);
        _view.Key += k =>
        {
            switch (k)
            {
                case Keys.Right or Keys.Up or Keys.PageUp or Keys.Back: Previous(); break;   // عربي: اليمين = اللي قبلها
                case Keys.Left or Keys.Down or Keys.PageDown or Keys.Space or Keys.Enter: Next(); break;
                case Keys.Home: GoTo(1); break;
                case Keys.End: GoTo(Total); break;
                case Keys.Escape: Command?.Invoke("end"); break;
            }
        };
        _view.Clicked += Next;
        PillButton B(string text, string tip, Action click, PillStyle style = PillStyle.Ghost)
        {
            var b = new PillButton(text, style) { Height = 30, Margin = new Padding(3, 0, 3, 0) };
            b.Width = Math.Max(36, TextRenderer.MeasureText(text, Font).Width + 22);
            b.Click += (_, _) => { click(); if (_images != null) _view.Focus(); else _show?.Focus(); };
            new ToolTip().SetToolTip(b, tip);
            _bar.Controls.Add(b);
            return b;
        }
        B("✕  إغلاق العرض", "إنهاء العرض والرجوع (Esc)", () => Command?.Invoke("end"), PillStyle.Primary);
        B("⏮", "أول شريحة (Home)", () => GoTo(1));
        B("›", "الشريحة اللي قبلها", Previous);
        _bar.Controls.Add(_pos);
        B("‹", "اللي بعدها (المسافة / السهم)", Next);
        _slides.Margin = new Padding(6, 1, 6, 0);
        _slides.SelectedIndexChanged += (_, _) => { if (_slides.Focused && _slides.SelectedIndex >= 0) { GoTo(_slides.SelectedIndex + 1); if (_images != null) _view.Focus(); else _show?.Focus(); } };
        _bar.Controls.Add(_slides);
        // أدوات الشرح الكاملة فوق الشريحة: أقلام وتظليل وخطوط وأسهم وأشكال وألوان ومقاسات (كل شريحة ليها رسمها)
        _inkBtn = B("🖊  أقلام وأشكال", "أدوات الشرح فوق الشريحة: قلم، تظليل، خط، سهم، مستطيل، دايرة، ليزر، ممحاة، ألوان ومقاسات", ToggleInk, PillStyle.Primary);
        // قلم PowerPoint نفسه (الكتابة دي بتتحفظ في نسخة مشروحة من العرض)
        void Pen(string text, string tip, int type, Color? c)
        {
            var b = c is { } col ? new DotButton(col) { Height = 30, Width = 34, Margin = new Padding(3, 0, 3, 0) } : new PillButton(text, PillStyle.Ghost) { Height = 30, Margin = new Padding(3, 0, 3, 0) };
            if (c == null) b.Width = Math.Max(36, TextRenderer.MeasureText(text, Font).Width + 22);
            b.Click += (_, _) => { SetPointer(type, c); if (_images != null) _view.Focus(); else _show?.Focus(); };
            new ToolTip().SetToolTip(b, tip);
            _bar.Controls.Add(b);
            _penButtons.Add((b, type, c));
        }
        Pen("➤", "الماوس العادي", PowerPointShow.PointerAutoArrow, null);
        Pen("", "قلم PowerPoint أحمر (بيتحفظ في نسخة مشروحة)", PowerPointShow.PointerPen, Color.FromArgb(224, 49, 49));
        Pen("", "قلم PowerPoint أزرق", PowerPointShow.PointerPen, Color.FromArgb(25, 113, 194));
        Pen("", "قلم PowerPoint أخضر", PowerPointShow.PointerPen, Color.FromArgb(47, 158, 68));
        Pen("", "قلم PowerPoint أصفر", PowerPointShow.PointerPen, Color.FromArgb(245, 159, 0));
        Pen("ممحاة", "ممحاة PowerPoint", PowerPointShow.PointerEraser, null);
        _erase = B("امسح الكتابة", "امسح كتابة PowerPoint من الشريحة", () => _show?.EraseDrawing());
        B("🎬", "حوّل العرض لفيديو", () => Command?.Invoke("video"));
        _host.Resize += (_, _) => { Fit(); InkFollow(); };
        _host.LocationChanged += (_, _) => InkFollow();
        _poll.Tick += (_, _) => InkFollow();   // الشباك نفسه اتحرك
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
        int from = 1;
        try
        {
            show.Open(path);
            Total = show.SlideCount;
            from = Math.Clamp(fromSlide, 1, Math.Max(1, Total));
            show.Start(_host, from, useTimings);
        }
        catch (Exception ex)
        {
            Data.Log.Info("powerpoint start: " + ex);
            // العرض نفسه مااشتغلش بس الملف اتفتح: نعرض الشرايح صور جوه «مسار» بدل ما نسيب المساحة سودا
            if (Total > 0 && StartImages(show, titles, from)) return null;
            show.Dispose();
            Visible = false;
            return ex is System.Runtime.InteropServices.COMException ? "PowerPoint رفض يفتح الملف: " + ex.Message : ex.Message;
        }
        if (!show.Embedded)
        {
            // شباك PowerPoint مارضيش يتحط جوه «مسار»: مانسيبوش يفتح لوحده برّه — نعرض الشرايح صور هنا
            Data.Log.Info("powerpoint not embedded → images inside Masar");
            if (StartImages(show, titles, from)) return null;
            show.Dispose();
            Visible = false;
            return "PowerPoint مارضيش يتعرض جوه «مسار»";
        }
        _show = show;
        _last = 0;
        // الرجوع لآخر شريحة: GoTo على طول بعد التشغيل ممكن يضيع (الشباك لسه بيتعمل) — بنكرره لحد ما يوصل،
        // ومانبلّغش «الشريحة 1» في النص عشان آخر مكان مايتمسحش
        _pendingSlide = from > 1 ? from : 0;
        _pendingUntil = DateTime.UtcNow.AddSeconds(5);
        FillSlides(titles);
        SetPensEnabled(true);
        Fit();
        _poll.Start();
        Poll();
        show.Focus();
        Data.Log.Info($"powerpoint {show.Version}: {Total} slides, embedded={show.Embedded}");
        return null;
    }

    private void FillSlides(IList<string> titles)
    {
        _slides.Items.Clear();
        for (int i = 1; i <= Total; i++) _slides.Items.Add($"{i}. " + (i <= titles.Count && titles[i - 1].Length > 0 ? titles[i - 1] : "شريحة " + i));
    }

    private void SetPensEnabled(bool on) { foreach (var (b, _, _) in _penButtons) b.Visible = on; _erase.Visible = on; }

    // ---------------- الشرايح كصور (لو PowerPoint مارضيش يتحط جوه) ----------------
    private readonly SlideView _view = new() { Dock = DockStyle.Fill, Visible = false };
    private List<string>? _images;
    private string? _imagesDir;
    private bool _imagesEnded;
    /// <summary>العرض شغال كصور ثابتة (من غير حركات) جوه «مسار».</summary>
    public bool ImagesMode => _images != null;

    private bool StartImages(PowerPointShow show, IList<string> titles, int from)
    {
        try
        {
            // الصور الأول، وبعدين نقفل العرض (الخروج من العرض ممكن يقفل الملف لو كان مفتوح من غير شباك)
            _imagesDir = Path.Combine(Path.GetTempPath(), "masar-slides-" + Guid.NewGuid().ToString("N")[..8]);
            int w = Math.Clamp(Screen.FromControl(this).Bounds.Width, 1280, 2560);
            show.ExportThumbs(_imagesDir, w);
            try { show.StopShow(); } catch { }
            var files = Enumerable.Range(1, Total).Select(i => Path.Combine(_imagesDir, $"{i}.png")).ToList();
            if (!files.Any(File.Exists)) return false;
            show.Dispose();   // PowerPoint يتقفل: مفيش أي شباك برّه «مسار»
            _images = files;
            _imagesEnded = false;
            FillSlides(titles);
            SetPensEnabled(false);
            _view.Visible = true;
            _view.BringToFront();
            _last = 0;
            GoToImage(from);
            _view.Focus();
            _poll.Start();
            return true;
        }
        catch (Exception ex) { Data.Log.Info("slides as images: " + ex.Message); return false; }
    }

    private int _imgIndex;
    private void GoToImage(int n)
    {
        if (_images == null) return;
        if (n > _images.Count) { _imagesEnded = true; return; }
        n = Math.Clamp(n, 1, _images.Count);
        _imgIndex = n;
        _view.SetImage(File.Exists(_images[n - 1]) ? _images[n - 1] : null);
        _pos.Text = $"شريحة {n} من {Total}";
        if (!_slides.Focused && n - 1 < _slides.Items.Count) _slides.SelectedIndex = n - 1;
        if (n != _last) { _last = n; InkSlide(n); SlideChanged?.Invoke(n); }
    }

    private void Next() { if (_images != null) GoToImage(_imgIndex + 1); else _show?.Next(); }
    private void Previous() { if (_images != null) GoToImage(_imgIndex - 1); else _show?.Previous(); }
    private void GoTo(int n) { if (_images != null) GoToImage(n); else _show?.GoTo(n); }

    private void OwnerMoved(object? s, EventArgs e) => Fit();
    public void Fit() { if (_show != null) _show.Fit(_host, _show.Embedded); }

    private void Poll()
    {
        if (_images != null) { if (_imagesEnded) { _poll.Stop(); Ended?.Invoke(); } return; }
        if (_show == null) return;
        if (_show.Ended) { _poll.Stop(); Ended?.Invoke(); return; }
        int cur = _show.Current;
        if (_pendingSlide > 0)
        {
            if (cur == _pendingSlide || DateTime.UtcNow > _pendingUntil) _pendingSlide = 0;
            else { _show.GoTo(_pendingSlide); return; }
        }
        if (cur <= 0 || cur == _last) return;
        _last = cur;
        _pos.Text = $"شريحة {cur} من {Total}";
        if (!_slides.Focused && cur - 1 < _slides.Items.Count) _slides.SelectedIndex = cur - 1;
        InkSlide(cur);
        SlideChanged?.Invoke(cur);
    }

    public int Current => _last;

    /// <summary>يستخبى والعرض شغال (نفس الشريحة).</summary>
    public void Suspend()
    {
        _poll.Stop();
        _ink?.Hide();
        Visible = false;
    }

    /// <summary>يرجع فورًا زي ما كان.</summary>
    public void Resume()
    {
        Visible = true;
        BringToFront();
        Fit();
        if (_ink is { IsDisposed: false }) { _ink.Show(); InkFollow(); }
        _poll.Start();
        if (_images != null) _view.Focus(); else _show?.Focus();
    }

    /// <summary>بيقفل العرض. keep = عدد الشرايح اللي عليها كتابة اتحفظت (قبل القفل) لو حد عايز يعمل نسخة.</summary>
    public void Stop()
    {
        _poll.Stop();
        var ink = _ink; _ink = null;
        if (ink != null) { try { ink.Close(); } catch { } }
        _inkPages.Clear();
        if (_owner != null) { _owner.Move -= OwnerMoved; _owner.Resize -= OwnerMoved; _owner = null; }
        var s = _show; _show = null;
        if (s != null) { try { s.Dispose(); } catch (Exception ex) { Data.Log.Info("powerpoint stop: " + ex.Message); } }
        if (_images != null)
        {
            _images = null;
            _view.SetImage(null);
            _view.Visible = false;
            var dir = _imagesDir; _imagesDir = null;
            if (dir != null) try { Directory.Delete(dir, true); } catch { }
        }
        Visible = false;
    }

    protected override void Dispose(bool disposing) { if (disposing) { Stop(); _poll.Dispose(); } base.Dispose(disposing); }
}

/// <summary>شريحة كصورة: بتتظبط على المساحة (من غير تشويه)، وبتاخد الكيبورد والكليك.</summary>
internal sealed class SlideView : Control
{
    private Image? _img;
    public event Action<Keys>? Key;
    public event Action? Clicked;
    public SlideView()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
        TabStop = true;
    }
    public void SetImage(string? file)
    {
        var old = _img;
        _img = null;
        if (file != null) try { using var fs = File.OpenRead(file); _img = Image.FromStream(fs); } catch { }
        old?.Dispose();
        Invalidate();
    }
    protected override bool IsInputKey(Keys k) => true;
    protected override void OnKeyDown(KeyEventArgs e) { Key?.Invoke(e.KeyCode); e.Handled = true; }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) Clicked?.Invoke(); base.OnMouseDown(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.Black);
        if (_img == null) return;
        float k = Math.Min((float)Width / _img.Width, (float)Height / _img.Height);
        int w = (int)(_img.Width * k), h = (int)(_img.Height * k);
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_img, (Width - w) / 2, (Height - h) / 2, w, h);
    }
    protected override void Dispose(bool disposing) { if (disposing) { _img?.Dispose(); _img = null; } base.Dispose(disposing); }
}

/// <summary>زرار لون: دايرة ملونة بجد (الإيموجي الملون مابيترسمش في WinForms).</summary>
internal sealed class DotButton : PillButton
{
    private readonly Color _dot;
    public DotButton(Color dot) : base("", PillStyle.Ghost) { _dot = dot; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int d = Math.Min(Width, Height) - 12;
        using var b = new SolidBrush(_dot);
        e.Graphics.FillEllipse(b, (Width - d) / 2f, (Height - d) / 2f, d, d);
        using var p = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f);
        e.Graphics.DrawEllipse(p, (Width - d) / 2f, (Height - d) / 2f, d, d);
    }
}
