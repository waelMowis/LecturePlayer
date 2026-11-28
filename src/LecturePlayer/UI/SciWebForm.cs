using System.Drawing.Imaging;
using LecturePlayer.Data;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LecturePlayer.UI;

/// <summary>
/// مصادر العلوم الأونلاين (المرحلة الخامسة): محاكيات PhET، وGeoGebra، وDesmos بتتفتح من مواقعها الرسمية
/// في نافذة جوه «مسار» (مش جوه صفحة السبورة — كود المواقع معزول عن القارئ)، وزر «التقط إلى السبورة»
/// بياخد صورة الشاشة الظاهرة وينزّلها على الصفحة المفتوحة ومعاها اسم المصدر والرابط.
/// التنقل مسموح جوه مواقع المصادر بس؛ أي رابط تاني بيفتح في المتصفح.
/// </summary>
internal sealed class SciWebForm : Form
{
    /// <summary>المواقع المسموحة (والنطاقات الفرعية بتاعتها).</summary>
    private static readonly string[] Hosts = { "phet.colorado.edu", "geogebra.org", "desmos.com" };

    public static SciWebForm? Current { get; private set; }
    /// <summary>صورة اللقطة (JPEG) + الرابط + عنوان الصفحة + المصدر.</summary>
    public event Action<byte[], string, string, string>? Captured;

    public WebView2 Web { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
    private readonly Label _status = new() { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0), TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10f) };
    private readonly Button _capture = new() { Text = "⤓  التقط إلى السبورة", Dock = DockStyle.Left, Width = 190, FlatStyle = FlatStyle.Flat };
    private readonly Button _back = new() { Text = "→", Dock = DockStyle.Right, Width = 44, FlatStyle = FlatStyle.Flat };
    private string _url, _source;
    public bool PageLoaded { get; private set; }

    public static bool Allowed(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && Hosts.Any(h => u.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    private SciWebForm(string url, string title, string source)
    {
        _url = url; _source = source;
        Text = title + " — مسار";
        Icon = Theme.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1240, 820);
        MinimumSize = new Size(760, 520);
        BackColor = Theme.Window;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        foreach (var b in new[] { _capture, _back }) { b.FlatAppearance.BorderSize = 0; b.ForeColor = Color.White; }
        _capture.BackColor = Theme.Accent;
        _capture.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _back.BackColor = Theme.Panel;
        _back.Font = new Font("Segoe UI", 12f);
        _capture.Click += async (_, _) => await CaptureAsync();
        _back.Click += (_, _) => { if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack(); };
        var bar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Panel, Padding = new Padding(6) };
        bar.Controls.Add(_status);
        bar.Controls.Add(_back);
        bar.Controls.Add(_capture);
        Controls.Add(Web);
        Controls.Add(bar);
        Status("جارٍ فتح الصفحة…");
        Shown += async (_, _) => await StartAsync();
        FormClosed += (_, _) => { if (Current == this) Current = null; };
    }

    /// <summary>بيفتح النافذة (أو يستخدم المفتوحة) على الرابط.</summary>
    public static SciWebForm Open(IWin32Window owner, string url, string title, string source)
    {
        if (Current is { IsDisposed: false } f)
        {
            f._url = url; f._source = source; f.Text = title + " — مسار";
            f.PageLoaded = false;
            if (f.Web.CoreWebView2 != null) f.Web.CoreWebView2.Navigate(url);
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Activate();
            return f;
        }
        Current = new SciWebForm(url, title, source);
        Current.Show(owner);
        return Current;
    }

    private void Status(string s) { if (!IsDisposed) _status.Text = s; }

    private async Task StartAsync()
    {
        try
        {
            await UdemyWeb.InitAsync(Web);
            var core = Web.CoreWebView2;
            core.NavigationStarting += (_, e) =>
            {
                if (Allowed(e.Uri)) { PageLoaded = false; Status("جارٍ التحميل…"); return; }
                e.Cancel = true;
                OpenOutside(e.Uri);
            };
            core.NewWindowRequested += (_, e) => { e.Handled = true; if (Allowed(e.Uri)) core.Navigate(e.Uri); else OpenOutside(e.Uri); };
            core.NavigationCompleted += (_, e) =>
            {
                PageLoaded = true;
                Status(e.IsSuccess ? "جاهز — اضبط المحاكاة ثم اضغط «التقط إلى السبورة». الصورة تنزل على الصفحة المفتوحة ومعها اسم المصدر."
                    : "تعذّر فتح الصفحة — تأكد من الاتصال بالإنترنت.");
            };
            core.DocumentTitleChanged += (_, _) => { if (!IsDisposed && !string.IsNullOrWhiteSpace(core.DocumentTitle)) Text = core.DocumentTitle + " — مسار"; };
            core.Navigate(_url);
        }
        catch (Exception ex)
        {
            Log.Error("sci web", ex);
            Status("WebView2 لا يعمل: " + ex.Message);
        }
    }

    private static void OpenOutside(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http")) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); } catch { }
    }

    /// <summary>صورة الجزء الظاهر من الصفحة (JPEG بعرض 1600 على الأكثر).</summary>
    public async Task<byte[]?> CaptureAsync()
    {
        var core = Web.CoreWebView2;
        if (core == null) return null;
        try
        {
            _capture.Enabled = false;
            using var ms = new MemoryStream();
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            ms.Position = 0;
            using var src = new Bitmap(ms);
            double k = Math.Min(1, 1600.0 / src.Width);
            using var dst = new Bitmap(Math.Max(1, (int)(src.Width * k)), Math.Max(1, (int)(src.Height * k)));
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.Clear(Color.White);
                g.DrawImage(src, 0, 0, dst.Width, dst.Height);
            }
            using var outMs = new MemoryStream();
            var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var ps = new EncoderParameters(1);
            ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
            dst.Save(outMs, jpeg, ps);
            var bytes = outMs.ToArray();
            Captured?.Invoke(bytes, core.Source, core.DocumentTitle ?? "", _source);
            Status("✓ نزلت اللقطة على السبورة — تقدر تلتقط تاني بعد ما تغيّر المحاكاة.");
            return bytes;
        }
        catch (Exception ex)
        {
            Log.Error("sci web capture", ex);
            Status("تعذّر التقاط الصورة: " + ex.Message);
            return null;
        }
        finally { if (!IsDisposed) _capture.Enabled = true; }
    }
}
