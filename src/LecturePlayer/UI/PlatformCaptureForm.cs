using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LecturePlayer.Data;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LecturePlayer.UI;

/// <summary>
/// شباك منصة (IT Legend…) جوه «مسار»: بتدخل وبتفتح كورس، و«مسار» بيسجّل <b>شكل</b> البيانات اللي الصفحة بتجيبها من سيرفر المنصة
/// (أسماء الحقول وأنواعها والروابط) — من غير أي توكن أو إيميل أو تليفون أو باسورد — في ملف جنب اللوج،
/// عشان قراية المنهج تتبني على البيانات الحقيقية بدل التخمين.
/// </summary>
internal sealed class PlatformCaptureForm : Form
{
    public WebView2 Web { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Back };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top, Height = 44, Padding = new Padding(12, 0, 12, 0), TextAlign = ContentAlignment.MiddleRight,
        BackColor = Theme.Panel, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10f)
    };
    private readonly string _startUrl;
    private readonly string _host;
    private readonly string _file;
    private readonly Dictionary<string, string> _shapes = new();   // نمط الرابط → الشكل
    private readonly List<string> _pages = new();
    private readonly HashSet<string> _frames = new();
    private int _captured;

    public string CaptureFile => _file;
    internal static Action<CoreWebView2>? SmokeSetup;
    public int Captured => _captured;

    public PlatformCaptureForm(string title, string startUrl, string fileName)
    {
        _startUrl = startUrl;
        _host = new Uri(startUrl).Host.Replace("www.", "");
        _file = Path.Combine(Log.LogDir, fileName);
        Text = title;
        Icon = Theme.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1200, 820);
        MinimumSize = new Size(700, 500);
        BackColor = Theme.Window;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Controls.Add(Web);
        Controls.Add(_status);
        Status("ادخل بالإيميل والباسورد، وافتح الكورس، ودوس على درسين من المنهج — وبعدين اقفل الشباك.");
        Shown += async (_, _) => await StartAsync();
        FormClosed += (_, _) => Save();
    }

    private void Status(string s) { if (!IsDisposed) _status.Text = s; }

    private async Task StartAsync()
    {
        try
        {
            await UdemyWeb.InitAsync(Web);
            var core = Web.CoreWebView2;
            core.WebResourceResponseReceived += OnResponse;
            core.SourceChanged += (_, _) => AddPage(core.Source);
            core.NavigationCompleted += async (_, _) => { AddPage(core.Source); await PageDataAsync(); };
            core.FrameCreated += (_, e) => _ = FrameSrcAsync();
            core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); };
            SmokeSetup?.Invoke(core);
            core.Navigate(_startUrl);
        }
        catch (Exception ex)
        {
            Log.Error("platform capture", ex);
            Status("WebView2 مش شغال: " + ex.Message);
        }
    }

    private void AddPage(string? url)
    {
        if (string.IsNullOrEmpty(url) || url == "about:blank") return;
        string clean = Redact(url);
        if (_pages.Count == 0 || _pages[^1] != clean) _pages.Add(clean);
    }

    /// <summary>بيانات الصفحة نفسها (Next.js بيحطها في __NEXT_DATA__ أو self.__next_f) + أسماء مفاتيح التخزين (من غير القيم).</summary>
    private async Task PageDataAsync()
    {
        try
        {
            string raw = await Web.CoreWebView2.ExecuteScriptAsync(
                "JSON.stringify({ next: (document.getElementById('__NEXT_DATA__') || {}).textContent || '', " +
                "flight: (self.__next_f || []).map(x => (x && x[1]) || '').join('').slice(0, 20000), " +
                "storage: Object.keys(localStorage || {}).concat(Object.keys(sessionStorage || {}).map(k => 'session:' + k)), " +
                "cookies: document.cookie.split(';').map(c => c.split('=')[0].trim()) })");
            if (UdemyWeb.Unwrap(raw) is not { } json) return;
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string page = "PAGE " + Pattern(new Uri(Web.CoreWebView2.Source));
            var sb = new StringBuilder();
            sb.AppendLine("storage keys: " + string.Join(", ", r.GetProperty("storage").EnumerateArray().Select(x => x.GetString())));
            sb.AppendLine("cookie names: " + string.Join(", ", r.GetProperty("cookies").EnumerateArray().Select(x => x.GetString())));
            string next = r.GetProperty("next").GetString() ?? "";
            if (next.Length > 0) sb.AppendLine("__NEXT_DATA__:").AppendLine(Shape(next));
            string flight = r.GetProperty("flight").GetString() ?? "";
            if (flight.Length > 0) sb.AppendLine("RSC flight (" + flight.Length + "):").AppendLine(Redact(flight.Length > 6000 ? flight[..6000] : flight));
            lock (_shapes) { _shapes[page] = sb.ToString(); _captured = _shapes.Count; }
        }
        catch (Exception ex) { Log.Info("platform capture page: " + ex.Message); }
    }

    /// <summary>روابط الإطارات (المشغّل) — من غير otp/playbackInfo.</summary>
    private async Task FrameSrcAsync()
    {
        await Task.Delay(1500);
        try
        {
            string raw = await Web.CoreWebView2.ExecuteScriptAsync(
                "JSON.stringify([...document.querySelectorAll('iframe')].map(f => (f.src || '').slice(0, 300) + ' ' + Math.round(f.getBoundingClientRect().width) + 'x' + Math.round(f.getBoundingClientRect().height)))");
            if (UdemyWeb.Unwrap(raw) is { } json)
                foreach (var s in JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>())
                    _frames.Add(Redact(s));
        }
        catch (Exception) { }
    }

    private static readonly Regex StaticRx = new(@"\.(js|css|png|jpe?g|gif|svg|webp|woff2?|ttf|ico|mp4|m3u8|ts|map)(\?|$)|/_next/(static|image)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private async void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            string uri = e.Request.Uri;
            if (StaticRx.IsMatch(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var u)) return;
            if (!u.Host.Contains(_host, StringComparison.OrdinalIgnoreCase) && !u.Host.Contains("vdocipher", StringComparison.OrdinalIgnoreCase)) return;
            string type = e.Response.Headers.GetHeader("Content-Type") ?? "";
            if (!type.Contains("json", StringComparison.OrdinalIgnoreCase) && !type.Contains("x-component", StringComparison.OrdinalIgnoreCase)) return;
            using var stream = await e.Response.GetContentAsync();
            if (stream == null) return;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();
            string key = e.Request.Method + " " + Pattern(u);
            string shape = type.Contains("json", StringComparison.OrdinalIgnoreCase) ? Shape(body) : "RSC: " + Redact(body.Length > 1500 ? body[..1500] : body);
            lock (_shapes)
            {
                if (_shapes.TryGetValue(key, out var old) && old.Length >= shape.Length) return;
                _shapes[key] = $"status {e.Response.StatusCode}, {body.Length} bytes\n{shape}";
                _captured = _shapes.Count;
            }
            BeginInvoke(() => Status($"✓ اتسجّل شكل {_captured} نوع بيانات — كمّل: افتح الكورس ودوس على درسين، وبعدين اقفل الشباك."));
        }
        catch (Exception ex) { Log.Info("platform capture: " + ex.Message); }
    }

    /// <summary>الرابط من غير أرقام/معرّفات/استعلامات حساسة: /api/courses/{uuid}/lessons?page={n}.</summary>
    private static string Pattern(Uri u)
    {
        string path = Regex.Replace(u.AbsolutePath, @"/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "/{uuid}", RegexOptions.IgnoreCase);
        path = Regex.Replace(path, @"/\d+(?=/|$)", "/{n}");
        string q = string.Join("&", System.Web.HttpUtility.ParseQueryString(u.Query).AllKeys.Where(k => k != null).Select(k => k + "=…"));
        return u.Host + path + (q.Length > 0 ? "?" + q : "");
    }

    private static readonly Regex SecretKey = new("token|otp|password|passwd|secret|signature|auth|jwt|cookie|session|email|phone|mobile|whatsapp|playbackinfo|key$|^key|card|iban|national", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string Redact(string s)
    {
        s = Regex.Replace(s, @"(?i)(otp|token|playbackInfo|signature|key|auth|code)=[^&\s""]+", "$1=***");
        s = Regex.Replace(s, @"[\w.+-]+@[\w-]+\.[\w.]+", "***@***");
        s = Regex.Replace(s, @"\+?\d{9,15}", "***");
        s = Regex.Replace(s, @"eyJ[\w-]+\.[\w-]+\.[\w-]+", "***jwt***");
        return s;
    }

    /// <summary>شكل JSON: المسارات بأنواعها، وعينات قصيرة من القيم (الحساس بيتشال، والقوايم بنشوف أول عنصرين).</summary>
    private static string Shape(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            Walk(doc.RootElement, "$", sb, 0);
            return sb.Length > 12000 ? sb.ToString(0, 12000) + "\n…" : sb.ToString();
        }
        catch (JsonException) { return "(مش JSON صالح) " + Redact(json.Length > 300 ? json[..300] : json); }
    }

    private static void Walk(JsonElement e, string path, StringBuilder sb, int depth)
    {
        if (depth > 12) return;
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    string child = path + "." + p.Name;
                    if (SecretKey.IsMatch(p.Name) && p.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                    {
                        sb.Append(child).Append(" : ").Append(p.Value.ValueKind).AppendLine(" = (محذوف)");
                        continue;
                    }
                    Walk(p.Value, child, sb, depth + 1);
                }
                break;
            case JsonValueKind.Array:
                int n = e.GetArrayLength();
                sb.Append(path).Append(" : array[").Append(n).AppendLine("]");
                int i = 0;
                foreach (var item in e.EnumerateArray())
                {
                    if (i++ >= 2) break;
                    Walk(item, path + "[" + (i - 1) + "]", sb, depth + 1);
                }
                break;
            case JsonValueKind.String:
                string v = e.GetString() ?? "";
                sb.Append(path).Append(" : string = ").AppendLine(Redact(v.Length > 80 ? v[..80] + "…" : v));
                break;
            default:
                sb.Append(path).Append(" : ").Append(e.ValueKind).Append(" = ").AppendLine(e.GetRawText());
                break;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var sb = new StringBuilder();
            sb.AppendLine("# شكل بيانات " + _host + " — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("## الصفحات");
            foreach (var p in _pages) sb.AppendLine(p);
            sb.AppendLine("## الإطارات");
            foreach (var f in _frames) sb.AppendLine(f);
            lock (_shapes)
                foreach (var kv in _shapes)
                {
                    sb.AppendLine();
                    sb.AppendLine("## " + kv.Key);
                    sb.AppendLine(kv.Value);
                }
            File.WriteAllText(_file, sb.ToString(), Encoding.UTF8);
            Log.Info($"platform capture saved: {_file} ({_shapes.Count} shapes, {_pages.Count} pages)");
        }
        catch (Exception ex) { Log.Error("platform capture save", ex); }
    }
}
