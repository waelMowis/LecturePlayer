using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LecturePlayer.UI;

/// <summary>
/// قارئ الكتب جوه مساحة الفيديو: PDF.js جوه WebView2 (من غير نت)، وبيبلّغ «مسار» بالصفحة والمكان
/// والصفحات اللي اتقرت فعلًا. الملفات بتتقري من مكانها على الجهاز (مفيش نسخ).
/// </summary>
internal sealed class ReaderPane : Panel
{
    public const string ReaderHost = "reader.masar";
    private const string BookHost = "book.masar";
    private const string ProbeHost = "probe.masar";

    public sealed record DocInfo(int Pages, string? Fingerprint, string? Title, string? Author, List<Data.BookChapter> Outline);
    public sealed record State(int Page, int Pages, string Scale, double? Top, double? Left, int Spread, string Theme, bool Rtl);

    public WebView2 Web { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(30, 31, 34) };

    /// <summary>الكتاب اتفتح (رقم الفتحة، البيانات).</summary>
    public event Action<int, DocInfo>? Loaded;
    public event Action<int, State>? StateChanged;
    /// <summary>صفحات اتقرت (قعدت قدامك المدة المحددة).</summary>
    public event Action<int, int[]>? PagesRead;
    /// <summary>كل 5 ثواني: بتقرا دلوقتي ولا سايب الشاشة.</summary>
    public event Action<int, bool>? Tick;
    /// <summary>أمر من شريط القارئ (mark / fullscreen) والصفحة.</summary>
    public event Action<int, string, int>? Command;
    public event Action<int, string, string>? Failed;
    public event Action<Keys>? KeyForwarded;

    /// <summary>رقم آخر كتاب اتفتح (الرسايل القديمة بتتجاهل).</summary>
    public int Generation { get; private set; }
    public State? Current { get; private set; }
    public DocInfo? Info { get; private set; }
    public bool IsReady { get; private set; }

    private Task? _init;
    private TaskCompletionSource? _pageReady;
    private string? _bookDir;
    private readonly Dictionary<string, TaskCompletionSource<DocInfo?>> _probes = new();
    private readonly SemaphoreSlim _probeGate = new(1, 1);

    public ReaderPane()
    {
        BackColor = Color.FromArgb(30, 31, 34);
        Controls.Add(Web);
        Visible = false;
    }

    /// <summary>فولدر القارئ جنب البرنامج (PDF.js وصفحة القارئ).</summary>
    public static string ReaderDir => Path.Combine(AppContext.BaseDirectory, "Reader");
    public static bool Available => File.Exists(Path.Combine(ReaderDir, "reader.html")) && UdemyWeb.RuntimeVersion != null;

    public Task EnsureReadyAsync() => _init ??= InitAsync();

    private async Task InitAsync()
    {
        // ممكن يتجهّز وهو مستخبي (بيانات الكتب الجديدة في الخلفية): لازم يبقى ليه شباك
        _ = Handle;
        _ = Web.Handle;
        await UdemyWeb.InitAsync(Web);
        var core = Web.CoreWebView2;
        core.Settings.IsZoomControlEnabled = false;      // Ctrl+العجلة = تكبير الكتاب مش الصفحة
        core.Settings.AreDefaultContextMenusEnabled = true; // نسخ النص
        try { core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark; } catch { }
        core.SetVirtualHostNameToFolderMapping(ReaderHost, ReaderDir, CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnMessage;
        core.NewWindowRequested += (_, e) =>
        {
            // رابط خارجي جوه الكتاب: يفتح في المتصفح
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https" || u.Scheme == "mailto"))
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true })?.Dispose(); } catch { }
        };
        core.NavigationStarting += (_, e) =>
        {
            // الصفحة نفسها بس — أي تنقل تاني (رابط جوه الكتاب) يتلغي
            if (!e.Uri.StartsWith("https://" + ReaderHost + "/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };
        _pageReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        core.Navigate($"https://{ReaderHost}/reader.html");
        var done = await Task.WhenAny(_pageReady.Task, Task.Delay(20_000));
        if (done != _pageReady.Task) throw new TimeoutException("صفحة القارئ مافتحتش");
        IsReady = true;
    }

    /// <summary>بيفتح كتاب من ملف على الجهاز.</summary>
    public async Task<int> OpenAsync(string file, string title, Data.Book b, int readSeconds, string theme, IEnumerable<int> read)
    {
        await EnsureReadyAsync();
        int gen = ++Generation;
        Current = null;
        Info = null;
        string dir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? "";
        var core = Web.CoreWebView2;
        if (!string.Equals(_bookDir, dir, StringComparison.OrdinalIgnoreCase))
        {
            if (_bookDir != null) try { core.ClearVirtualHostNameToFolderMapping(BookHost); } catch { }
            core.SetVirtualHostNameToFolderMapping(BookHost, dir, CoreWebView2HostResourceAccessKind.Allow);
            _bookDir = dir;
        }
        string url = $"https://{BookHost}/{Uri.EscapeDataString(Path.GetFileName(file))}?v={Stamp(file)}";
        var o = new Dictionary<string, object?>
        {
            ["gen"] = gen, ["url"] = url, ["title"] = title,
            ["page"] = Math.Max(1, b.LastPage), ["top"] = b.LastTop, ["left"] = b.LastLeft,
            ["scale"] = b.Zoom ?? "page-width", ["spread"] = b.Spread, ["rtl"] = b.RightToLeft,
            ["theme"] = theme, ["readSeconds"] = readSeconds, ["read"] = read.ToArray(),
            ["marks"] = b.Marks.Select(m => m.Page).Distinct().ToArray()
        };
        await core.ExecuteScriptAsync("masar.open(" + JsonSerializer.Serialize(o) + ")");
        return gen;
    }

    private static string Stamp(string file)
    {
        try { var i = new FileInfo(file); return i.Length.ToString("x") + i.LastWriteTimeUtc.Ticks.ToString("x"); }
        catch { return "0"; }
    }

    public Task CloseBookAsync() => Run("masar.close()");
    public Task GoToAsync(int page) => Run($"masar.goTo({page})");
    public Task StepAsync(int delta) => Run($"masar.step({delta})");
    public Task FocusAsync() => Run("masar.focus()");
    public Task SetMarksAsync(IEnumerable<int> pages) => Run("masar.setMarks(" + JsonSerializer.Serialize(pages.Distinct().ToArray()) + ")");
    public Task SetReadAsync(IEnumerable<int> pages) => Run("masar.setRead(" + JsonSerializer.Serialize(pages.ToArray()) + ")");
    public Task SetReadSecondsAsync(int s) => Run($"masar.setReadSeconds({s})");
    public Task FindAsync(string q) => Run("masar.find(" + JsonSerializer.Serialize(q) + ")");

    private async Task Run(string js)
    {
        if (!IsReady || Web.CoreWebView2 == null) return;
        try { await Web.CoreWebView2.ExecuteScriptAsync(js); } catch (Exception ex) { Data.Log.Info("reader js: " + ex.Message); }
    }

    /// <summary>
    /// بيانات كتاب من غير ما يتعرض (عدد الصفحات والفهرس والبصمة) — للكتب اللي لسه متضافة.
    /// null لو الملف مش PDF سليم أو محمي بكلمة سر.
    /// </summary>
    public async Task<DocInfo?> ProbeAsync(string file, int timeoutMs = 30_000)
    {
        await EnsureReadyAsync();
        await _probeGate.WaitAsync();
        try
        {
            var core = Web.CoreWebView2;
            string dir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? "";
            try { core.ClearVirtualHostNameToFolderMapping(ProbeHost); } catch { }
            core.SetVirtualHostNameToFolderMapping(ProbeHost, dir, CoreWebView2HostResourceAccessKind.Allow);
            string id = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<DocInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _probes[id] = tcs;
            string url = $"https://{ProbeHost}/{Uri.EscapeDataString(Path.GetFileName(file))}?v={Stamp(file)}";
            await core.ExecuteScriptAsync($"masar.probe({JsonSerializer.Serialize(id)}, {JsonSerializer.Serialize(url)})");
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            _probes.Remove(id);
            return done == tcs.Task ? tcs.Task.Result : null;
        }
        finally { _probeGate.Release(); }
    }

    // ---------------- الرسايل من الصفحة ----------------

    private sealed class Msg
    {
        [JsonPropertyName("t")] public string? T { get; set; }
        [JsonPropertyName("gen")] public int Gen { get; set; }
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("pages")] public JsonElement Pages { get; set; }
        [JsonPropertyName("page")] public int Page { get; set; }
        [JsonPropertyName("fingerprint")] public string? Fingerprint { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
        [JsonPropertyName("outline")] public List<OutlineItem>? Outline { get; set; }
        [JsonPropertyName("scale")] public string? Scale { get; set; }
        [JsonPropertyName("top")] public double? Top { get; set; }
        [JsonPropertyName("left")] public double? Left { get; set; }
        [JsonPropertyName("spread")] public int Spread { get; set; }
        [JsonPropertyName("theme")] public string? Theme { get; set; }
        [JsonPropertyName("rtl")] public bool Rtl { get; set; }
        [JsonPropertyName("active")] public bool Active { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("k")] public int K { get; set; }
        [JsonPropertyName("c")] public bool C { get; set; }
        [JsonPropertyName("s")] public bool S { get; set; }
        [JsonPropertyName("a")] public bool A { get; set; }
    }

    private sealed class OutlineItem
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("page")] public int Page { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
    }

    private static DocInfo ToInfo(Msg m) => new(
        m.Pages.ValueKind == JsonValueKind.Number ? m.Pages.GetInt32() : 0, m.Fingerprint, Clean(m.Title), Clean(m.Author),
        (m.Outline ?? new()).Where(o => !string.IsNullOrWhiteSpace(o.Title) && o.Page > 0)
            .Select(o => new Data.BookChapter { Title = o.Title!.Trim(), Page = o.Page, Level = Math.Clamp(o.Level, 0, 1) }).ToList());

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        Msg? m;
        try { m = JsonSerializer.Deserialize<Msg>(e.TryGetWebMessageAsString()); }
        catch (Exception ex) { Data.Log.Info("reader msg: " + ex.Message); return; }
        if (m == null) return;
        try
        {
            switch (m.T)
            {
                case "ready":
                    _pageReady?.TrySetResult();
                    break;
                case "probe":
                    if (m.Id != null && _probes.TryGetValue(m.Id, out var tcs)) tcs.TrySetResult(m.Ok ? ToInfo(m) : null);
                    break;
                case "loaded" when m.Gen == Generation:
                    Info = ToInfo(m);
                    Loaded?.Invoke(m.Gen, Info);
                    break;
                case "state" when m.Gen == Generation:
                    Current = new State(m.Page, m.Pages.ValueKind == JsonValueKind.Number ? m.Pages.GetInt32() : 0, m.Scale ?? "page-width", m.Top, m.Left, m.Spread, m.Theme ?? "normal", m.Rtl);
                    StateChanged?.Invoke(m.Gen, Current);
                    break;
                case "read" when m.Gen == Generation && m.Pages.ValueKind == JsonValueKind.Array:
                    PagesRead?.Invoke(m.Gen, m.Pages.EnumerateArray().Select(x => x.GetInt32()).ToArray());
                    break;
                case "tick" when m.Gen == Generation:
                    Tick?.Invoke(m.Gen, m.Active);
                    break;
                case "cmd" when m.Gen == Generation:
                    Command?.Invoke(m.Gen, m.Name ?? "", m.Page);
                    break;
                case "error" when m.Gen == Generation:
                    Failed?.Invoke(m.Gen, m.Code ?? "error", m.Message ?? "");
                    break;
                case "key":
                    var key = (Keys)m.K;
                    if (m.C) key |= Keys.Control;
                    if (m.S) key |= Keys.Shift;
                    if (m.A) key |= Keys.Alt;
                    KeyForwarded?.Invoke(key);
                    break;
            }
        }
        catch (Exception ex) { Data.Log.Error("reader " + m.T, ex); }
    }
}
