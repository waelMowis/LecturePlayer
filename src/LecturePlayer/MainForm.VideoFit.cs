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
    }

    /// <summary>كل شبابيك VLC جوه مساحة الفيديو تاخد المساحة كلها بالظبط (من أول ركن).</summary>
    internal bool DisableVideoFitForTest;

    private void FitVlcWindows()
    {
        if (DisableVideoFitForTest || !_videoView.IsHandleCreated || IsDisposed) return;
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
