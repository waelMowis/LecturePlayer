using System.Runtime.InteropServices;

namespace LecturePlayer.UI;

/// <summary>
/// عرض PowerPoint بالأنيميشن جوه «مسار» (COM من غير ربط بنسخة معينة: Office 2016/2019/2021/2024/365، 32 أو 64 بت).
/// العرض بيشتغل في «شباك» (ppShowTypeWindow) وبيتحط جوه مساحة الفيديو (SetParent) — ولو ماقدرش: الشباك بيتحط فوقها بالظبط.
/// </summary>
internal sealed class PowerPointShow : IDisposable
{
    // ثوابت PowerPoint
    const int MsoTrue = -1, MsoFalse = 0;
    const int PpShowTypeWindow = 2, PpShowAll = 1;
    const int PpSlideShowDone = 5;
    public const int PointerArrow = 1, PointerPen = 2, PointerEraser = 5, PointerAutoArrow = 4;
    public const int VideoNone = 0, VideoInProgress = 1, VideoQueued = 2, VideoDone = 3, VideoFailed = 4;

    /// <summary>للفحص: بديل لـ PowerPoint (كائن بنفس الأعضاء).</summary>
    internal static Func<object>? TestFactory;

    private dynamic? _app, _pres, _win;
    private bool _startedApp;
    public bool Embedded { get; private set; }
    public IntPtr Hwnd { get; private set; }
    public string? Version { get; private set; }

    /// <summary>PowerPoint متثبت؟ (رقم النسخة أو null)</summary>
    public static string? InstalledVersion()
    {
        if (TestFactory != null) return "test";
        try
        {
            var t = Type.GetTypeFromProgID("PowerPoint.Application", throwOnError: false);
            if (t == null) return null;
            using var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"PowerPoint.Application\CurVer");
            return (k?.GetValue(null) as string)?.Replace("PowerPoint.Application.", "") ?? "?";
        }
        catch { return null; }
    }

    private static object CreateApp(out bool started)
    {
        started = true;
        if (TestFactory != null) return TestFactory();
        // لو PowerPoint مفتوح أصلًا بنستخدمه (ومانقفلوش في الآخر)
        try
        {
            if (GetActiveObject("PowerPoint.Application") is { } running) { started = false; return running; }
        }
        catch { }
        var t = Type.GetTypeFromProgID("PowerPoint.Application", throwOnError: true)!;
        return Activator.CreateInstance(t)!;
    }

    [DllImport("oleaut32.dll", PreserveSig = false)] static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);
    [DllImport("ole32.dll")] static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);
    private static object? GetActiveObject(string progId)
    {
        if (CLSIDFromProgID(progId, out var clsid) != 0) return null;
        try { GetActiveObject(ref clsid, IntPtr.Zero, out var o); return o; } catch { return null; }
    }

    /// <summary>بيفتح الملف (للقراية بس، من غير شباك تعديل).</summary>
    public void Open(string path)
    {
        _app = CreateApp(out _startedApp);
        try { Version = (string)_app.Version; } catch { Version = "?"; }
        _pres = _app.Presentations.Open(path, MsoTrue, MsoFalse, MsoFalse);
    }

    public int SlideCount { get { try { return (int)_pres!.Slides.Count; } catch { return 0; } } }

    /// <summary>بيشغّل العرض من شريحة معينة ويحطه جوه host (أو فوقه).</summary>
    public void Start(Control host, int fromSlide, bool useTimings)
    {
        var ss = _pres!.SlideShowSettings;
        ss.ShowType = PpShowTypeWindow;
        ss.RangeType = PpShowAll;
        ss.ShowWithAnimation = MsoTrue;
        ss.ShowWithNarration = MsoTrue;
        ss.AdvanceMode = useTimings ? 2 : 1; // 2 = بالتوقيت المتسجّل، 1 = يدوي
        _win = ss.Run();
        Hwnd = new IntPtr((int)_win.HWND);
        Embedded = Embed(host);
        if (fromSlide > 1) GoTo(fromSlide);
    }

    // ---------------- الشباك ----------------
    const int GWL_STYLE = -16;
    const long WS_CHILD = 0x40000000, WS_POPUP = 0x80000000, WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_VISIBLE = 0x10000000, WS_SYSMENU = 0x00080000;
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr h);

    /// <summary>العرض جوه مساحة «مسار»: ابن للـ host من غير إطار.</summary>
    private bool Embed(Control host)
    {
        if (Hwnd == IntPtr.Zero || !IsWindow(Hwnd)) return false;
        try
        {
            long style = (long)GetWindowLongPtr(Hwnd, GWL_STYLE);
            style = (style | WS_CHILD | WS_VISIBLE) & ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU);
            SetWindowLongPtr(Hwnd, GWL_STYLE, new IntPtr(style));
            if (SetParent(Hwnd, host.Handle) == IntPtr.Zero && Marshal.GetLastWin32Error() != 0) return false;
            Fit(host, true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>بيظبط مقاس العرض على الـ host (جوه) أو مكانه على الشاشة (فوق).</summary>
    public void Fit(Control host, bool embedded)
    {
        if (Hwnd == IntPtr.Zero || !IsWindow(Hwnd)) return;
        if (embedded) MoveWindow(Hwnd, 0, 0, host.ClientSize.Width, host.ClientSize.Height, true);
        else
        {
            var r = host.RectangleToScreen(host.ClientRectangle);
            SetWindowPos(Hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, 0x0010 /*NOACTIVATE*/);
        }
    }

    public void Focus() { if (Hwnd != IntPtr.Zero) SetFocus(Hwnd); }

    // ---------------- التحكم ----------------
    public void Next() { try { _win!.View.Next(); } catch { } }
    public void Previous() { try { _win!.View.Previous(); } catch { } }
    public void GoTo(int n) { try { _win!.View.GotoSlide(n); } catch { } }
    /// <summary>الشريحة الحالية (0 لو العرض خلص).</summary>
    public int Current { get { try { return (int)_win!.View.CurrentShowPosition; } catch { return 0; } } }
    public bool Ended { get { try { return _win == null || (int)_win.View.State == PpSlideShowDone; } catch { return true; } } }

    /// <summary>قلم PowerPoint نفسه (الحبر بيتحفظ في الشريحة لو اخترت «احتفظ» آخر العرض).</summary>
    public void Pointer(int type, Color? color = null)
    {
        try
        {
            _win!.View.PointerType = type;
            if (color is { } c) _win.View.PointerColor.RGB = c.R | (c.G << 8) | (c.B << 16);
        }
        catch { }
    }
    public void EraseDrawing() { try { _win!.View.EraseDrawing(); } catch { } }

    /// <summary>صور مصغّرة للشرائح (PNG).</summary>
    public void ExportThumbs(string dir, int width, IProgress<int>? progress = null)
    {
        Directory.CreateDirectory(dir);
        int n = SlideCount;
        float w = 0, h = 0;
        try { w = (float)_pres!.PageSetup.SlideWidth; h = (float)_pres.PageSetup.SlideHeight; } catch { }
        int height = w > 0 ? (int)(width * h / w) : width * 9 / 16;
        for (int i = 1; i <= n; i++)
        {
            string f = Path.Combine(dir, $"{i}.png");
            try { _pres!.Slides[i].Export(f, "PNG", width, height); } catch { }
            progress?.Report(i);
        }
    }

    /// <summary>الحبر اللي اتحفظ في الشرايح (لو اخترت «احتفظ بالكتابة»): في كام شريحة.</summary>
    public int InkSlides()
    {
        int n = 0;
        try
        {
            foreach (dynamic slide in _pres!.Slides)
                foreach (dynamic sh in slide.Shapes)
                    if ((int)sh.Type is 22 or 23) { n++; break; } // msoInk / msoInkComment
        }
        catch { }
        return n;
    }

    public void SaveCopyAs(string path) => _pres!.SaveCopyAs(path);

    /// <summary>فيديو من العرض بالأنيميشن (PowerPoint بيعمله في الخلفية).</summary>
    public void CreateVideo(string path, bool useTimings, int secondsPerSlide, int height, int fps, int quality) =>
        _pres!.CreateVideo(path, useTimings ? MsoTrue : MsoFalse, secondsPerSlide, height, fps, quality);
    public int VideoStatus { get { try { return (int)_pres!.CreateVideoStatus; } catch { return VideoFailed; } } }

    /// <summary>مدة كل شريحة في الفيديو (للعلامات): التوقيت المتسجّل أو الافتراضي.</summary>
    public double[] SlideSeconds(bool useTimings, int secondsPerSlide)
    {
        int n = SlideCount;
        var r = new double[n];
        for (int i = 1; i <= n; i++)
        {
            double s = secondsPerSlide;
            try
            {
                dynamic t = _pres!.Slides[i].SlideShowTransition;
                if ((int)t.Hidden == MsoTrue) { r[i - 1] = 0; continue; }
                if (useTimings && (int)t.AdvanceOnTime == MsoTrue) s = (double)(float)t.AdvanceTime;
                s += (double)(float)t.Duration;
            }
            catch { }
            r[i - 1] = s;
        }
        return r;
    }

    public void StopShow()
    {
        try { if (_win != null && !Ended) _win.View.Exit(); } catch { }
        _win = null;
        Hwnd = IntPtr.Zero;
    }

    public void Dispose()
    {
        StopShow();
        try { _pres?.Close(); } catch { }
        try { if (_startedApp && _app != null && (int)_app.Presentations.Count == 0) _app.Quit(); } catch { }
        foreach (var o in new object?[] { _pres, _app })
            if (o != null && Marshal.IsComObject(o)) try { Marshal.FinalReleaseComObject(o); } catch { }
        _pres = null; _app = null;
    }
}
