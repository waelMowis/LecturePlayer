using LecturePlayer.Data;
using Microsoft.Web.WebView2.WinForms;

namespace LecturePlayer.UI;

/// <summary>
/// شباك يوديمي: صفحة يوديمي الحقيقية (الدخول بيبقى على موقعهم مباشرة — «مسار» مابيشوفش الإيميل ولا الباسورد)،
/// وبعد الدخول نفس الصفحة بتجيب كورساتك ومنهجها وتقدّمك.
/// الجلسة بتتحفظ في بروفايل البرنامج على الجهاز ده بس (ويندوز بيشفّرها) فمش هتحتاج تدخل كل مرة.
/// </summary>
internal sealed class UdemyAccountForm : Form
{
    public WebView2 Web { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Back };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top, Height = 44, Padding = new Padding(12, 0, 12, 0), TextAlign = ContentAlignment.MiddleRight,
        BackColor = Theme.Panel, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10f)
    };

    public UdemyAccountForm(string title = "يوديمي — حسابك")
    {
        Text = title;
        Icon = Theme.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1040, 760);
        MinimumSize = new Size(640, 480);
        BackColor = Theme.Window;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        ShowInTaskbar = false;
        Controls.Add(Web);
        Controls.Add(_status);
    }

    public void Status(string text, Color? color = null)
    {
        if (IsDisposed) return;
        _status.Text = text;
        _status.ForeColor = color ?? Theme.Text;
    }

    private bool OnUdemy =>
        Uri.TryCreate(Web.CoreWebView2?.Source, UriKind.Absolute, out var u) &&
        Uri.TryCreate(Udemy.CurrentOrigin, UriKind.Absolute, out var o) && u.Host == o.Host;

    /// <summary>
    /// بيفتح يوديمي، ولو إنت مش داخل بيسيبك تدخل في نفس الشباك. بيرجّع اسمك (أو null لو قفلت الشباك).
    /// </summary>
    public async Task<string?> SignInAsync(CancellationToken ct)
    {
        await UdemyWeb.InitAsync(Web);
        Status("جارٍ فتح يوديمي…");
        try { await UdemyWeb.NavigateAsync(Web, Udemy.CurrentOrigin + "/home/my-courses/learning/", ct); }
        catch (TimeoutException) { }

        bool sentToLogin = false;
        while (!ct.IsCancellationRequested)
        {
            if (OnUdemy)
            {
                try
                {
                    var (code, body) = await UdemyWeb.FetchAsync(Web, Udemy.MeApi, ct, 15);
                    if (code == 200 && Udemy.ParseMe(body) is { } name) return name.Length > 0 ? name : "حسابك";
                }
                catch (TimeoutException) { }
                catch (Exception ex) when (ex is not OperationCanceledException) { Log.Info("udemy me: " + ex.Message); }

                if (!sentToLogin && !(Web.CoreWebView2.Source ?? "").Contains("/join/", StringComparison.OrdinalIgnoreCase))
                {
                    sentToLogin = true;
                    Web.CoreWebView2.Navigate(Udemy.CurrentOrigin + "/join/login-popup/?next=/home/my-courses/learning/");
                }
            }
            Status("🔐 سجّل دخولك في يوديمي هنا بالبريد نفسه الذي اشتريت به — تذهب البيانات إلى يوديمي مباشرة ولا يراها «مسار».", Theme.Folder);
            await Task.Delay(2500, ct);
        }
        return null;
    }
}
