using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LecturePlayer.UI;

/// <summary>
/// شباك شفاف بكسل بكسل (UpdateLayeredWindow): الكاميرا المدوّرة، أقلام الشاشة، لمعة الماوس.
/// clickThrough = الماوس بيعدّي من خلاله للي تحته.
/// </summary>
internal class LayeredWindow : Form
{
    private bool _clickThrough;
    private readonly bool _noActivate;

    public LayeredWindow(bool clickThrough = false, bool noActivate = true)
    {
        _clickThrough = clickThrough;
        _noActivate = noActivate;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00080000 /*WS_EX_LAYERED*/ | 0x00000080 /*TOOLWINDOW*/;
            if (_noActivate) cp.ExStyle |= 0x08000000; // NOACTIVATE
            if (_clickThrough) cp.ExStyle |= 0x00000020; // TRANSPARENT
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => _noActivate;

    /// <summary>الماوس يعدّي (true) أو يتمسك هنا (false).</summary>
    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            if (_clickThrough == value) return;
            _clickThrough = value;
            if (!IsHandleCreated) return;
            long ex = (long)GetWindowLongPtr(Handle, -20);
            ex = value ? ex | 0x20 : ex & ~0x20L;
            SetWindowLongPtr(Handle, -20, new IntPtr(ex));
        }
    }

    /// <summary>بيعرض صورة ARGB (32bpp) مكان الشباك وبمقاسها.</summary>
    public void Present(Bitmap bmp, byte opacity = 255)
    {
        if (!IsHandleCreated || IsDisposed) return;
        IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc), hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(memDc, hBmp);
            var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
            var src = new POINT();
            var dst = new POINT { x = Left, y = Top };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = opacity, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2 /*ULW_ALPHA*/);
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memDc, old);
            if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ---------------- مايتسجلش في الفيديو ----------------
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);
    /// <summary>الشباك ده مايظهرش في التسجيل ولا لقطات الشاشة (ويندوز 10 2004+).</summary>
    public static bool ExcludeFromCapture(Control c) { try { return SetWindowDisplayAffinity(c.Handle, 0x11); } catch { return false; } }

    // ---------------- Win32 ----------------
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    /// <summary>سحب الشباك من أي حتة فيه.</summary>
    public void DragMove() { ReleaseCapture(); SendMessage(Handle, 0xA1 /*NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero); }

    /// <summary>صورة BGRA خام (من ffmpeg) ← Bitmap.</summary>
    public static Bitmap FromBgra(byte[] data, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(data, 0, bd.Scan0, Math.Min(data.Length, bd.Stride * h)); }
        finally { bmp.UnlockBits(bd); }
        return bmp;
    }
}
