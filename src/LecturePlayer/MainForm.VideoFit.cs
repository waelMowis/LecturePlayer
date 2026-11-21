using LecturePlayer.Data;
using System.Runtime.InteropServices;

namespace LecturePlayer;

/// <summary>
/// المشغل (VLC) بيعمل جوه مساحة الفيديو شباك خاص بيه يرسم فيه. البرنامج اتجاهه من اليمين للشمال، فالويندوز بيعكس الإحداثيات،
/// ولما الشباك الكبير يصغر فجأة (المشغل الصغير) شباك VLC كان بيفضل محسوب على مكانه القديم: الصورة تتزاح ويبان ركن منها بس
/// وجنبها مساحة سودا. هنا بنظبط مكان ومقاس شبابيك VLC بإيدينا على قد مساحة الفيديو مع كل تغيير في المقاس.
/// </summary>
internal sealed partial class MainForm
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);

    /// <summary>للتشخيص: كل عنصر من الفيديو لحد الشباك: نوعه ومقاسه ومكانه.</summary>
    internal string DescribeLayoutChain()
    {
        var sb = new System.Text.StringBuilder();
        GetWindowRect(Handle, out var f);
        sb.Append($"form {Bounds} client {ClientSize} win ({f.Left},{f.Top})-({f.Right},{f.Bottom}) state {WindowState}");
        for (Control? c = _videoView; c != null && c != this; c = c.Parent)
            sb.Append($" | {c.GetType().Name}{(c.Name.Length > 0 ? ":" + c.Name : "")} {c.Bounds} dock {c.Dock} vis {c.Visible}");
        sb.Append($" | split dist {_split.SplitterDistance} p1min {_split.Panel1MinSize} p2collapsed {_split.Panel2Collapsed}");
        return sb.ToString();
    }

    /// <summary>للفحص: مساحة الفيديو لازم تبقى جوه الشباك.</summary>
    internal string? VideoAreaOutsideWindow()
    {
        if (!_videoView.IsHandleCreated) return null;
        GetWindowRect(Handle, out var f);
        GetWindowRect(_videoView.Handle, out var v);
        if (v.Left < f.Left - 2 || v.Top < f.Top - 2 || v.Right > f.Right + 2 || v.Bottom > f.Bottom + 2)
            return $"video ({v.Left},{v.Top})-({v.Right},{v.Bottom}) outside window ({f.Left},{f.Top})-({f.Right},{f.Bottom})";
        return null;
    }

    /// <summary>للتشخيص: شجرة شبابيك مساحة الفيديو بالمقاسات.</summary>
    internal string DescribeVideoWindows()
    {
        if (!_videoView.IsHandleCreated) return "no handle";
        var sb = new System.Text.StringBuilder();
        GetWindowRect(_videoView.Handle, out var host);
        sb.Append($"area ({host.Left},{host.Top})-({host.Right},{host.Bottom})");
        void Walk(IntPtr parent, int depth)
        {
            if (depth > 4) return;
            for (IntPtr c = GetWindow(parent, GW_CHILD); c != IntPtr.Zero; c = GetWindow(c, GW_HWNDNEXT))
            {
                var name = new System.Text.StringBuilder(128);
                GetClassName(c, name, 128);
                GetWindowRect(c, out var r);
                sb.Append($" | {new string('>', depth + 1)}{name} ({r.Left},{r.Top})-({r.Right},{r.Bottom}){(IsWindowVisible(c) ? "" : " hidden")}");
                Walk(c, depth + 1);
            }
        }
        Walk(_videoView.Handle, 0);
        return sb.ToString();
    }

    private const uint GW_CHILD = 5, GW_HWNDNEXT = 2;
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200, SWP_ASYNCWINDOWPOS = 0x4000;

    private void InitVideoFit()
    {
        _videoView.Resize += (_, _) => FitVlcWindows();
        _videoView.SizeChanged += (_, _) => FitVlcWindows();
        ResizeEnd += (_, _) => FitVlcWindows();
    }

    private DateTime _videoFitLoggedUtc = DateTime.MinValue;

    /// <summary>
    /// السبب الحقيقي لمشكلة المشغل الصغير: الـ SplitContainer لما المكتبة تكون مستخبية (Panel2Collapsed)
    /// وهو يصغر فجأة، الجزء اللي فيه الفيديو (Panel1) بيفضل بمقاسه القديم الكبير — فالفيديو يبان مقصوص ومزاح.
    /// الحل: نخفي ونظهر المكتبة تاني في نفس اللحظة، فالـ SplitContainer يعيد حساب مقاس الجزء ده صح.
    /// </summary>
    private bool _fixingSplit;
    /// <summary>للفحص بس: نقفل الإصلاح التلقائي عشان نثبت إن المشكلة بتحصل من غيره.</summary>
    private bool _splitAutoFixDisabledForTest = DisableSplitFixForSmoke;
    /// <summary>للفحص بس: نسخة جديدة من الشباك تفتح من غير الإصلاح (عشان نثبت إن المشكلة بتحصل).</summary>
    internal static bool DisableSplitFixForSmoke;

    /// <summary>للفحص: مقاس جزء الفيديو/الرئيسية قصاد الشباك بعد الفتح.</summary>
    internal string StartupLayoutReport(out bool ok)
    {
        bool fill = !_split.Panel2Collapsed
            ? _split.Panel1.Width + _split.Panel2.Width + _split.SplitterWidth >= _split.ClientSize.Width - 2
            : Math.Abs(_split.Panel1.Width - _split.ClientSize.Width) <= 2 && Math.Abs(_split.Panel1.Height - _split.ClientSize.Height) <= 2;
        // واللي جوه الجزء (كارت الفيديو/الرئيسية وشريط التحكم) لازم يكون بعرضه
        bool content = Math.Abs(_videoCard.Width - _split.Panel1.ClientSize.Width) <= 2
                       && Math.Abs(_controlCard.Width - _split.Panel1.ClientSize.Width) <= 2
                       && _welcome.Width >= _videoCard.Width - LogicalToDeviceUnits(30);
        ok = fill && content && _split.Panel2Collapsed == !_settings.SidePanelVisible;
        return $"state {WindowState}, form {Bounds.Size}, split {_split.ClientSize}, panel1 {_split.Panel1.Size}, panel2 {(_split.Panel2Collapsed ? "collapsed" : _split.Panel2.Size.ToString())}, " +
               $"dist {_split.SplitterDistance}, video card {_videoCard.Size}, controls {_controlCard.Size}, home {_welcome.Size} visible {_welcome.Visible}, library {(HasLibrary ? _library.Name : "-")}";
    }

    private void FixSplitPanelSize()
    {
        if (!_split.Panel2Collapsed || _fixingSplit || IsDisposed) return;
        _fixingSplit = true;
        try { FixSplitPanelSizeCore(); }
        finally { _fixingSplit = false; }
    }

    /// <summary>
    /// فحص تلقائي لنفس اللي حصل عند المستخدم: القايمة مستخبية والشباك بيفتح بمقاس صغير وبعدين يتكبّر
    /// ← جزء الفيديو/الرئيسية لازم ياخد الشباك كله (كان بيفضل بعرضه القديم والباقي فاضي).
    /// </summary>
    internal void RunSplitLayoutChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("layout: " + what); }
        var state = WindowState;
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        bool side = _settings.SidePanelVisible;
        string Sizes() => $"card {_videoCard.Width}, panel1 {_split.Panel1.Width}x{_split.Panel1.Height}, split {_split.ClientSize.Width}x{_split.ClientSize.Height}, home {_welcome.Width}";
        void Small()
        {
            WindowState = FormWindowState.Normal;
            Bounds = new Rectangle(40, 40, 900, 560);
            Application.DoEvents();
        }
        try
        {
            // من غير الإصلاح: نثبت إن المشكلة حقيقية (للسجل بس)
            _splitAutoFixDisabledForTest = true;
            Small();
            _settings.SidePanelVisible = true;
            _split.Panel2Collapsed = false;
            ApplySidePanelWidth();
            Application.DoEvents();
            _settings.SidePanelVisible = false;
            _split.Panel2Collapsed = true;   // زي ApplySettings وقت الفتح
            WindowState = FormWindowState.Maximized;
            Application.DoEvents();
            Console.WriteLine("SMOKE layout without fix: " + Sizes());

            // بالإصلاح
            _splitAutoFixDisabledForTest = false;
            Small();
            _split.Panel2Collapsed = false;
            _split.Panel2Collapsed = true;
            WindowState = FormWindowState.Maximized;
            Application.DoEvents();
            Application.DoEvents();
            Console.WriteLine("SMOKE layout with fix: " + Sizes());
            Check(Math.Abs(_split.Panel1.Width - _split.ClientSize.Width) <= 2 && Math.Abs(_split.Panel1.Height - _split.ClientSize.Height) <= 2
                  && Math.Abs(_videoCard.Width - _split.Panel1.ClientSize.Width) <= 2,
                "hidden library: home/video must fill the window: " + Sizes());

            // إظهار المكتبة وإخفاها تاني من غير ماتتلخبط
            ToggleSidePanel();
            Application.DoEvents();
            Check(!_split.Panel2Collapsed && _split.Panel2.Width >= LogicalToDeviceUnits(240), "show library: " + _split.Panel2.Width);
            ToggleSidePanel();
            Application.DoEvents();
            Check(_split.Panel2Collapsed && Math.Abs(_split.Panel1.Width - _split.ClientSize.Width) <= 2, "hide library again: " + Sizes());

            // وتصغير الشباك وهي مستخبية
            WindowState = FormWindowState.Normal;
            Bounds = new Rectangle(40, 40, 1000, 640);
            Application.DoEvents();
            Application.DoEvents();
            Check(Math.Abs(_split.Panel1.Width - _split.ClientSize.Width) <= 2, "restore down: " + Sizes());
        }
        finally
        {
            _splitAutoFixDisabledForTest = false;
            _settings.SidePanelVisible = side;
            WindowState = FormWindowState.Normal;
            Bounds = bounds;
            _split.Panel2Collapsed = !side;
            WindowState = state;
            Application.DoEvents();
            if (side) ApplySidePanelWidth(); else FixSplitPanelSize();
        }
        Console.WriteLine("SMOKE layout OK");
    }

    private void FixSplitPanelSizeCore()
    {
        var want = _split.ClientSize;
        if (_split.Panel1.Size == want)
        {
            // الجزء نفسه بمقاسه الصح بس اللي جواه لسه مترصوص على عرض قديم
            if (Math.Abs(_videoCard.Width - _split.Panel1.ClientSize.Width) > 2) _split.Panel1.PerformLayout();
            return;
        }
        _split.SuspendLayout();
        try
        {
            _split.Panel2Collapsed = false;
            _split.Panel2Collapsed = true;
        }
        catch (InvalidOperationException) { _split.Panel2Collapsed = true; /* مقاس مؤقت أثناء التصغير */ }
        _split.ResumeLayout(true);
        if (_split.Panel1.Size != want)
        {
            // لو لسه: نضبطه بإيدينا
            _split.Panel1.Size = want;
            _split.Panel1.PerformLayout();
        }
    }

    /// <summary>
    /// بيتنده كل ثانية والفيديو شغال: لو شباك الصورة طالع بره مساحة الفيديو (أيًا كان السبب) بنرجّعه،
    /// ونسجّل شكل الشبابيك في السجل (مرة كل دقيقة بالكتير) عشان لو حصل تاني نعرف السبب بالظبط.
    /// </summary>
    private void HealVideoFit()
    {
        if (DisableVideoFitForTest) return;
        FixSplitPanelSize();
        string? misfit;
        try { misfit = VlcWindowsMisfit(); } catch { return; }
        if (misfit == null || misfit == "no vlc window" || misfit == "no handle") return;
        if (DateTime.UtcNow - _videoFitLoggedUtc > TimeSpan.FromMinutes(1))
        {
            _videoFitLoggedUtc = DateTime.UtcNow;
            Log.Info($"video window out of place ({(_miniMode ? "mini" : _fullscreen ? "fullscreen" : "normal")}, dpi {DeviceDpi}): {DescribeVideoWindows()}");
        }
        FitVlcWindows();
        // لو شباك VLC الداخلي نفسه لسه طالع بره، نظبطه هو كمان على قد المساحة
        if (VlcWindowsMisfit() is { } still && still != "no vlc window")
            try { FitDeep(_videoView.Handle, 0); } catch { }
    }

    private static void FitDeep(IntPtr parent, int depth)
    {
        if (depth > 3 || !GetClientRect(parent, out var rc)) return;
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        if (w <= 0 || h <= 0) return;
        for (IntPtr child = GetWindow(parent, GW_CHILD); child != IntPtr.Zero; child = GetWindow(child, GW_HWNDNEXT))
        {
            SetWindowPos(child, IntPtr.Zero, 0, 0, w, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
            FitDeep(child, depth + 1);
        }
    }

    /// <summary>كل شبابيك VLC جوه مساحة الفيديو تاخد المساحة كلها بالظبط (من أول ركن).</summary>
    internal bool DisableVideoFitForTest;

    private void FitVlcWindows()
    {
        if (DisableVideoFitForTest || !_videoView.IsHandleCreated || IsDisposed) return;
        FixSplitPanelSize();
        try { FitChildren(_videoView.Handle, depth: 0); } catch { }
    }

    /// <summary>بس شبابيك VLC اللي على طول جوه مساحة الفيديو (VLC بيحط الصورة جواها بنسبتها الصح).</summary>
    private static void FitChildren(IntPtr parent, int depth, bool topOnly = true)
    {
        if (!GetClientRect(parent, out var rc)) return;
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        if (w <= 0 || h <= 0) return;
        for (IntPtr child = GetWindow(parent, GW_CHILD); child != IntPtr.Zero; child = GetWindow(child, GW_HWNDNEXT))
            SetWindowPos(child, IntPtr.Zero, 0, 0, w, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
    }


    /// <summary>للفحص: هل كل شبابيك VLC مغطية مساحة الفيديو بالظبط؟ بيرجّع وصف أول شباك مش مظبوط (أو null).</summary>
    internal string? VlcWindowsMisfit()
    {
        if (!_videoView.IsHandleCreated) return "no handle";
        GetWindowRect(_videoView.Handle, out var host);
        int found = 0;
        string? bad = null;
        void Walk(IntPtr parent, int depth)
        {
            if (depth > 3) return;
            for (IntPtr child = GetWindow(parent, GW_CHILD); child != IntPtr.Zero; child = GetWindow(child, GW_HWNDNEXT))
            {
                if (!IsWindowVisible(child)) continue;
                found++;
                GetWindowRect(child, out var r);
                // لازم يبقى جوه مساحة الفيديو (VLC ممكن يسيب حواف سودا عشان نسبة الصورة، ده طبيعي)
                if (r.Left < host.Left - 2 || r.Top < host.Top - 2 || r.Right > host.Right + 2 || r.Bottom > host.Bottom + 2)
                    bad ??= $"vlc window ({r.Left},{r.Top})-({r.Right},{r.Bottom}) vs video area ({host.Left},{host.Top})-({host.Right},{host.Bottom})";
                Walk(child, depth + 1);
            }
        }
        Walk(_videoView.Handle, 0);
        return found == 0 ? "no vlc window" : bad;
    }
}
