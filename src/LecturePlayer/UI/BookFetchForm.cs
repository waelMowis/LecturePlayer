using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LecturePlayer.UI;

/// <summary>
/// كتاب خاص (Google Drive / Dropbox / OneDrive بحسابك): الرابط بيتفتح جوه «مسار»، تسجّل دخول لو طلب،
/// وأول ما الملف يبتدي يتنزل «مسار» بيلقطه ويحطه في الكاش. ولو الموقع منع الدخول من جوه البرامج (جوجل بيعمل كده أحيانًا)
/// فيه زرار يفتحه في متصفحك و«مسار» يلقط الملف من فولدر التنزيلات.
/// </summary>
internal sealed class BookFetchForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
    private readonly Label _hint = new();
    private readonly string _startUrl;
    private readonly string _target;

    /// <summary>المسار في الكاش لو الملف اتلقط.</summary>
    public string? Result { get; private set; }
    /// <summary>المستخدم اختار «افتحه في متصفحي».</summary>
    public bool UseBrowser { get; private set; }

    public BookFetchForm(string title, string startUrl, string target)
    {
        _startUrl = startUrl;
        _target = target;
        Text = "هات الكتاب بحسابك — " + title;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 720);
        MinimumSize = new Size(640, 480);
        Font = new Font("Segoe UI", 9.5f);

        var top = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Panel, Padding = new Padding(12, 8, 12, 8) };
        _hint.Dock = DockStyle.Fill;
        _hint.ForeColor = Theme.Text;
        _hint.TextAlign = ContentAlignment.MiddleRight;
        _hint.Text = "سجّل دخول لو الصفحة طلبت، وبعدين دوس «تنزيل» (Download) — «مسار» هيلقط الكتاب لوحده ويفتحه.\n" +
                     "لو الموقع قال إن المتصفح ده مش آمن (جوجل أحيانًا بيمنع الدخول من جوه البرامج): دوس «افتحه في متصفحي».";
        var browser = new PillButton("افتحه في متصفحي", PillStyle.Ghost) { Dock = DockStyle.Left, Width = 150 };
        browser.Click += (_, _) => { UseBrowser = true; DialogResult = DialogResult.Retry; Close(); };
        top.Controls.Add(_hint);
        top.Controls.Add(browser);
        Controls.Add(_web);
        Controls.Add(top);
        Shown += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            await UdemyWeb.InitAsync(_web);
            var core = _web.CoreWebView2;
            core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); };
            core.DownloadStarting += OnDownload;
            core.Navigate(_startUrl);
        }
        catch (Exception ex)
        {
            _hint.Text = "مقدرتش أفتح الصفحة جوه البرنامج: " + ex.Message + " — دوس «افتحه في متصفحي».";
        }
    }

    private void OnDownload(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        string tmp = _target + ".fetch";
        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        Directory.CreateDirectory(Path.GetDirectoryName(_target)!);
        e.ResultFilePath = tmp;
        e.Handled = true; // من غير شريط التنزيلات بتاع المتصفح
        var op = e.DownloadOperation;
        _hint.Text = "بينزّل الكتاب…";
        op.BytesReceivedChanged += (_, _) =>
        {
            long total = (long)(op.TotalBytesToReceive ?? 0);
            _hint.Text = total > 0 ? $"بينزّل الكتاب… {op.BytesReceived * 100 / total}%" : $"بينزّل الكتاب… {op.BytesReceived / 1024} KB";
        };
        op.StateChanged += (_, _) =>
        {
            if (op.State == CoreWebView2DownloadState.Interrupted) { _hint.Text = "التنزيل وقف — جرّب تاني أو دوس «افتحه في متصفحي»."; return; }
            if (op.State != CoreWebView2DownloadState.Completed) return;
            try
            {
                var head = new byte[1024];
                int n;
                using (var fs = File.OpenRead(tmp)) n = fs.Read(head, 0, head.Length);
                if (!Data.BookSources.LooksLikePdf(head, n)) { _hint.Text = "الملف اللي اتنزل مش PDF — اختار ملف الكتاب نفسه."; return; }
                File.Move(tmp, _target, overwrite: true);
                Result = _target;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { _hint.Text = "مقدرتش أحفظ الملف: " + ex.Message; }
        };
    }
}
