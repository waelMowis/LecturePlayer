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
    /// <summary>
    /// كل حاجة (صفحة القارئ وPDF.js والكتب نفسها) بتتقدّم من البرنامج على عنوان واحد وهمي (نفس المصدر = مفيش مشاكل CORS):
    /// /book/{رقم}/{الاسم} = ملف الكتاب من مكانه على الجهاز (بيدعم Range للكتب الكبيرة)، والباقي من فولدر Reader.
    /// </summary>
    public const string ReaderHost = "reader.masar";

    public sealed record DocInfo(int Pages, string? Fingerprint, string? Title, string? Author, List<Data.BookChapter> Outline);
    public sealed record State(int Page, int Pages, string Scale, double? Top, double? Left, int Spread, string Theme, bool Rtl, string? Cfi = null);

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
    /// <summary>تظليل جديد: (رقم الفتحة، الصفحة، المستطيلات، النص، اللون، عايز ملاحظة).</summary>
    public event Action<int, int, double[][], string, string, bool, string?>? HighlightRequested;
    /// <summary>تعديل تظليل: (رقم الفتحة، الرقم، اللون الجديد أو null، اتشال).</summary>
    public event Action<int, string, string?, bool>? HighlightEdited;
    public event Action<Keys>? KeyForwarded;
    /// <summary>شرح اتغير: (رقم الفتحة، الصفحات اللي اتغيرت كاملة، أوقات المسح).</summary>
    public event Action<int, JsonElement, JsonElement>? InkChanged;
    /// <summary>إعدادات أدوات الشرح اتغيرت (الألوان والمقاسات والملصقات): JSON.</summary>
    public event Action<string>? InkPrefsChanged;
    /// <summary>الصفحة عايزة ملف: (رقم الفتحة، رقم الطلب، النوع open-pdf/save-pdf/save-self، اسم مقترح).</summary>
    public event Action<int, string, string, string>? PickRequested;
    /// <summary>محرر الصفحات حفظ: لكل صفحة جديدة [الصفحة القديمة (0 = جديدة)، اللفة].</summary>
    public event Action<int, int[][]>? PagesRemapped;
    /// <summary>السبورة اتغيرت (صفحات/خلفيات): (رقم الفتحة، JSON).</summary>
    public event Action<int, string>? BoardChanged;

    /// <summary>رقم آخر كتاب اتفتح (الرسايل القديمة بتتجاهل).</summary>
    public int Generation { get; private set; }
    public State? Current { get; private set; }
    public DocInfo? Info { get; private set; }
    public bool IsReady { get; private set; }

    private Task? _init;
    private TaskCompletionSource? _pageReady;
    /// <summary>الملفات المسموح بيها (رقم ← المسار): الصفحة ماتقدرش تطلب أي ملف تاني من الجهاز.</summary>
    private readonly Dictionary<string, string> _served = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<DocInfo?>> _probes = new();
    private readonly Dictionary<string, (Dictionary<int, string> Pages, TaskCompletionSource<bool> Done, IProgress<(int Done, int Total)>? Progress)> _texts = new();
    private readonly Dictionary<string, TaskCompletionSource<byte[]?>> _images = new();
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    /// <summary>عناوين الحفظ (توكن ← الملف): الصفحة تقدر تكتب الملفات دي بس، كل توكن مرة واحدة.</summary>
    private readonly Dictionary<string, string> _saves = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (TaskCompletionSource<JobResult> Done, IProgress<double>? Progress)> _jobs = new();

    public sealed record JobResult(bool Ok, string? Error, long Size, int Pages);

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
        core.AddWebResourceRequestedFilter($"https://{ReaderHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnResource;
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
        if (done != _pageReady.Task) throw new TimeoutException("لم تُفتح صفحة القارئ");
        IsReady = true;
        Data.Log.Info("reader: ready (" + UdemyWeb.RuntimeVersion + ")");
    }

    /// <summary>بيفتح كتاب من ملف على الجهاز.</summary>
    public async Task<int> OpenAsync(string file, string title, Data.Book b, int readSeconds, string theme, IEnumerable<int> read,
        System.Text.Json.Nodes.JsonObject? ink = null, string? inkPrefs = null)
    {
        await EnsureReadyAsync();
        int gen = ++Generation;
        Current = null;
        Info = null;
        var core = Web.CoreWebView2;
        string url = Serve(file);
        var o = new Dictionary<string, object?>
        {
            ["gen"] = gen, ["url"] = url, ["title"] = title, ["format"] = Data.BookFiles.FormatOf(file), ["cfi"] = b.LastCfi,
            ["page"] = Math.Max(1, b.LastPage), ["top"] = b.LastTop, ["left"] = b.LastLeft,
            ["scale"] = b.Zoom ?? "page-width", ["spread"] = b.Spread, ["rtl"] = b.RightToLeft,
            ["theme"] = theme, ["readSeconds"] = readSeconds, ["read"] = read.ToArray(),
            ["marks"] = b.Marks.Select(m => m.Page).Distinct().ToArray(),
            ["highlights"] = HighlightsJson(b),
            ["ink"] = ink?.DeepClone(),
            ["board"] = Data.BookFiles.FormatOf(file) == "board" ? SafeJson(File.ReadAllText(file)) : null,
            ["inkPrefs"] = string.IsNullOrWhiteSpace(inkPrefs) ? null : SafeJson(inkPrefs)
        };
        await core.ExecuteScriptAsync("masar.open(" + JsonSerializer.Serialize(o) + ")");
        return gen;
    }

    private static System.Text.Json.Nodes.JsonNode? SafeJson(string json)
    {
        try { return System.Text.Json.Nodes.JsonNode.Parse(json); } catch { return null; }
    }

    /// <summary>شرح أحدث (من جهاز تاني) بيتدمج في الكتاب المفتوح.</summary>
    public Task MergeInkAsync(System.Text.Json.Nodes.JsonObject doc) => Run("masar.setInk(" + JsonSerializer.Serialize(doc.ToJsonString()) + ")");
    /// <summary>القارئ يبعت أي تغيير لسه مابعتهوش.</summary>
    public Task FlushInkAsync() => Run("masar.inkFlush()");
    public Task SetInkModeAsync(bool on) => Run("masar.ink(" + (on ? "true" : "false") + ")");
    public async Task<string?> EvalAsync(string js)
    {
        if (!IsReady || Web.CoreWebView2 == null) return null;
        try { return await Web.CoreWebView2.ExecuteScriptAsync(js); } catch (Exception ex) { Data.Log.Info("reader js: " + ex.Message); return null; }
    }

    /// <summary>عنوان الملف جوه القارئ (رقم جديد لكل نسخة من الملف عشان الكاش).</summary>
    private string Serve(string file)
    {
        string full = Path.GetFullPath(file);
        string token = Stamp(full);
        _served[token] = full;
        return $"https://{ReaderHost}/book/{token}/{Uri.EscapeDataString(Path.GetFileName(full))}";
    }

    private static string Stamp(string file)
    {
        string key = file.ToUpperInvariant();
        try { var i = new FileInfo(file); key += "|" + i.Length + "|" + i.LastWriteTimeUtc.Ticks; } catch { }
        return Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..20].ToLowerInvariant();
    }

    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript; charset=utf-8", [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8", [".wasm"] = "application/wasm", [".svg"] = "image/svg+xml", [".png"] = "image/png",
        [".gif"] = "image/gif", [".json"] = "application/json", [".pdf"] = "application/pdf", [".epub"] = "application/epub+zip",
        [".txt"] = "text/plain; charset=utf-8"
    };

    /// <summary>بيرد على طلبات القارئ: ملفات القارئ من فولدره، والكتب من مكانها (مع Range).</summary>
    private void OnResource(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var env = Web.CoreWebView2.Environment;
            var uri = new Uri(e.Request.Uri);
            string path = Uri.UnescapeDataString(uri.AbsolutePath);
            if (path.StartsWith("/save/", StringComparison.Ordinal) && e.Request.Method == "POST")
            {
                SaveRequest(e, env, path[6..]);
                return;
            }
            if (path.StartsWith("/book/", StringComparison.Ordinal))
            {
                string[] parts = path.Split('/', 4, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !_served.TryGetValue(parts[1], out var file) || !File.Exists(file))
                {
                    e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "Content-Type: text/plain");
                    return;
                }
                e.Response = FileResponse(env, file, e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null);
                return;
            }
            string root = Path.GetFullPath(ReaderDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string local = Path.GetFullPath(Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            if (!local.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(local))
            {
                e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "Content-Type: text/plain");
                return;
            }
            string type = Mime.TryGetValue(Path.GetExtension(local), out var t) ? t : "application/octet-stream";
            var fs = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            e.Response = env.CreateWebResourceResponse(fs, 200, "OK", $"Content-Type: {type}\r\nContent-Length: {fs.Length}\r\nCache-Control: no-cache");
        }
        catch (Exception ex)
        {
            Data.Log.Info("reader resource: " + ex.Message);
            try { e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(null, 500, "Error", "Content-Type: text/plain"); } catch { }
        }
    }

    /// <summary>
    /// الصفحة بتبعت ملف اتعمل (PDF/PNG): بيتكتب في ملف مؤقت (على طول)، وبعدين يستبدل الهدف في الخلفية
    /// مع إعادة محاولة (مضاد الفيروسات أو القارئ ممكن يكونوا ماسكين الملف لحظة) — والواجهة ماتقفش.
    /// </summary>
    private void SaveRequest(CoreWebView2WebResourceRequestedEventArgs e, CoreWebView2Environment env, string token)
    {
        var body = e.Request.Content;
        if (!_saves.Remove(token, out var target) || body == null)
        {
            e.Response = env.CreateWebResourceResponse(null, 403, "Forbidden", "Content-Type: text/plain");
            return;
        }
        string tmp = target + ".masar-part";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                body.CopyTo(fs);
            if (new FileInfo(tmp).Length == 0) throw new IOException("الملف فارغ");
        }
        catch (Exception ex)
        {
            try { File.Delete(tmp); } catch { }
            e.Response = SaveError(env, ex);
            return;
        }
        var deferral = e.GetDeferral();
        _ = FinishSaveAsync(e, deferral, env, tmp, target);
    }

    private async Task FinishSaveAsync(CoreWebView2WebResourceRequestedEventArgs e, CoreWebView2Deferral deferral, CoreWebView2Environment env, string tmp, string target)
    {
        Exception? error = null;
        await Task.Run(() =>
        {
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(tmp, target, overwrite: true); return; }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 8) { Thread.Sleep(200); }
                catch (Exception) { break; }
            }
            // ويندوز مابيسمحش يستبدل ملف حد فاتحه (حتى بمشاركة الحذف) — بس بيسمح نكتب جواه لو فاتحينه بمشاركة الكتابة (زي القارئ)
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (var src = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var dst = new FileStream(target, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    {
                        src.CopyTo(dst);
                        dst.SetLength(src.Length);
                    }
                    try { File.Delete(tmp); } catch { }
                    return;
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 15) { Thread.Sleep(300); }
                catch (Exception ex) { error = ex; try { File.Delete(tmp); } catch { } return; }
            }
        });
        try
        {
            e.Response = error == null
                ? env.CreateWebResourceResponse(new MemoryStream("ok"u8.ToArray()), 200, "OK", "Content-Type: text/plain")
                : SaveError(env, error);
        }
        catch (Exception ex) { Data.Log.Info("reader save response: " + ex.Message); }
        finally { deferral.Complete(); }
    }

    private static CoreWebView2WebResourceResponse SaveError(CoreWebView2Environment env, Exception ex)
    {
        Data.Log.Info("reader save: " + ex.Message);
        var msg = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ex is IOException or UnauthorizedAccessException ? "الملف مفتوح في برنامج آخر أو لا توجد صلاحية: " + ex.Message : ex.Message));
        return env.CreateWebResourceResponse(msg, 500, "Error", "Content-Type: text/plain; charset=utf-8");
    }

    private string SaveUrl(string target)
    {
        string token = Guid.NewGuid().ToString("N");
        _saves[token] = Path.GetFullPath(target);
        return $"https://{ReaderHost}/save/{token}";
    }

    /// <summary>شغلانة في الصفحة (نسخة مشروحة/صورة/تعديل صفحات) ← بتستنى رسالة job.</summary>
    private async Task<JobResult> RunJobAsync(Func<string, string> script, IProgress<double>? progress, CancellationToken ct, int timeoutMs = 600_000)
    {
        await EnsureReadyAsync();
        string id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _jobs[id] = (tcs, progress);
        try
        {
            await Web.CoreWebView2.ExecuteScriptAsync(script(JsonSerializer.Serialize(id)));
            using (ct.Register(() => tcs.TrySetCanceled()))
            {
                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, CancellationToken.None));
                return done == tcs.Task ? await tcs.Task : new JobResult(false, "استغرقت العملية وقتًا أطول من اللازم", 0, 0);
            }
        }
        finally { _jobs.Remove(id); }
    }

    /// <summary>رد على طلب ملف من الصفحة (رابط قراية/حفظ، أو null = اتلغى).</summary>
    public Task PickResultAsync(string id, string? url) => Run($"masar.pickResult({JsonSerializer.Serialize(id)}, {JsonSerializer.Serialize(url)})");
    /// <summary>رابط حفظ لمرة واحدة لملف معين.</summary>
    public string SaveUrlFor(string target) => SaveUrl(target);
    /// <summary>رابط قراية لملف من الجهاز.</summary>
    public string ServeUrl(string file) => Serve(file);
    public Task OpenPageEditorAsync() => Run("masar.pageEditor()");
    public Task BoardImageAsync(string dataUrl, string name) => Run($"masar.boardImage({JsonSerializer.Serialize(dataUrl)}, {JsonSerializer.Serialize(name)})");
    public Task BoardExportAsync(string which, bool send) => Run($"masar.boardExport({JsonSerializer.Serialize(which)}, {(send ? "true" : "false")})");

    /// <summary>نسخة مشروحة من ملف: الأصل + الشرح ← target.</summary>
    public Task<JobResult> BuildAnnotatedAsync(string srcFile, System.Text.Json.Nodes.JsonObject ink, string target, string title, IProgress<double>? progress, CancellationToken ct)
    {
        string src = Serve(srcFile), save = SaveUrl(target);
        return RunJobAsync(id => $"masar.annotate({id}, {JsonSerializer.Serialize(src)}, {JsonSerializer.Serialize(save)}, {JsonSerializer.Serialize(ink.ToJsonString())}, {JsonSerializer.Serialize(title)})", progress, ct);
    }

    /// <summary>سبورة (ملفها وشرحها) ← PDF دون فتحها.</summary>
    public Task<JobResult> BoardPdfFileAsync(string boardFile, System.Text.Json.Nodes.JsonObject ink, string target, string title, CancellationToken ct)
    {
        string save = SaveUrl(target);
        string board = File.ReadAllText(boardFile);
        return RunJobAsync(id => $"masar.boardPdfFile({id}, {JsonSerializer.Serialize(board)}, {JsonSerializer.Serialize(ink.ToJsonString())}, {JsonSerializer.Serialize(save)}, {JsonSerializer.Serialize(title)})", null, ct);
    }

    /// <summary>جزء من ملف مجمّع: صفحات من PDF (من 0، أو كلها) أو صورة بمقاس صفحة (بالنقاط).</summary>
    public sealed record ComposePart(string? Pdf = null, int[]? Pages = null, string? Image = null, double W = 0, double H = 0);

    /// <summary>ملف PDF واحد من أجزاء بالترتيب.</summary>
    public Task<JobResult> ComposeAsync(IEnumerable<ComposePart> parts, string target, string title, IProgress<double>? progress, CancellationToken ct)
    {
        string save = SaveUrl(target);
        var arr = new System.Text.Json.Nodes.JsonArray();
        foreach (var p in parts)
        {
            var o = new System.Text.Json.Nodes.JsonObject();
            if (p.Pdf != null) { o["pdf"] = Serve(p.Pdf); if (p.Pages is { Length: > 0 }) o["pages"] = new System.Text.Json.Nodes.JsonArray(p.Pages.Select(x => (System.Text.Json.Nodes.JsonNode)x).ToArray()); }
            else if (p.Image != null) { o["img"] = Serve(p.Image); o["w"] = p.W; o["h"] = p.H; }
            else continue;
            arr.Add(o);
        }
        return RunJobAsync(id => $"masar.compose({id}, {JsonSerializer.Serialize(arr.ToJsonString())}, {JsonSerializer.Serialize(save)}, {JsonSerializer.Serialize(title)})", progress, ct);
    }

    /// <summary>صورة صفحة من الكتاب المفتوح بالشرح اللي عليها.</summary>
    public Task<JobResult> PagePngAsync(int page, string target, double scale = 2)
    {
        string save = SaveUrl(target);
        return RunJobAsync(id => $"masar.pagePng({id}, {page}, {JsonSerializer.Serialize(save)}, {scale.ToString(System.Globalization.CultureInfo.InvariantCulture)})", null, CancellationToken.None, 120_000);
    }

    private static CoreWebView2WebResourceResponse FileResponse(CoreWebView2Environment env, string file, string? range)
    {
        var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        long len = fs.Length;
        string type = Mime.TryGetValue(Path.GetExtension(file), out var t) ? t : "application/octet-stream";
        const string common = "Accept-Ranges: bytes\r\nCache-Control: no-store";
        if (range != null && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) && !range.Contains(','))
        {
            string[] r = range[6..].Split('-');
            bool okStart = long.TryParse(r[0], out long start);
            bool okEnd = r.Length > 1 && long.TryParse(r[1], out long end0);
            long end = okEnd ? long.Parse(r[1]) : len - 1;
            if (!okStart && okEnd) { start = Math.Max(0, len - long.Parse(r[1])); end = len - 1; okStart = true; }
            if (okStart && start < len && end >= start)
            {
                end = Math.Min(end, len - 1);
                int count = (int)Math.Min(end - start + 1, 64L << 20);
                var buf = new byte[count];
                fs.Seek(start, SeekOrigin.Begin);
                int read = 0;
                while (read < count) { int n = fs.Read(buf, read, count - read); if (n <= 0) break; read += n; }
                fs.Dispose();
                return env.CreateWebResourceResponse(new MemoryStream(buf, 0, read), 206, "Partial Content",
                    $"Content-Type: {type}\r\nContent-Length: {read}\r\nContent-Range: bytes {start}-{start + read - 1}/{len}\r\n{common}");
            }
            fs.Dispose();
            return env.CreateWebResourceResponse(null, 416, "Range Not Satisfiable", $"Content-Range: bytes */{len}\r\n{common}");
        }
        if (len <= 16L << 20)
        {
            // ملف صغير: نقراه كله ونقفله على طول (لو PDF.js قطع الطلب الملف كان بيفضل ممسوك لحد ما الذاكرة تتنضف)
            var mem = new MemoryStream((int)len);
            fs.CopyTo(mem);
            fs.Dispose();
            mem.Position = 0;
            return env.CreateWebResourceResponse(mem, 200, "OK", $"Content-Type: {type}\r\nContent-Length: {mem.Length}\r\n{common}");
        }
        return env.CreateWebResourceResponse(fs, 200, "OK", $"Content-Type: {type}\r\nContent-Length: {len}\r\n{common}");
    }

    public Task CloseBookAsync() => Run("masar.close()");
    public Task GoToAsync(int page) => Run($"masar.goTo({page})");
    public Task StepAsync(int delta) => Run($"masar.step({delta})");
    public Task FocusAsync() => Run("masar.focus()");
    public Task SetMarksAsync(IEnumerable<int> pages) => Run("masar.setMarks(" + JsonSerializer.Serialize(pages.Distinct().ToArray()) + ")");
    public Task SetReadAsync(IEnumerable<int> pages) => Run("masar.setRead(" + JsonSerializer.Serialize(pages.ToArray()) + ")");
    public Task SetReadSecondsAsync(int s) => Run($"masar.setReadSeconds({s})");
    public Task FindAsync(string q) => Run("masar.find(" + JsonSerializer.Serialize(q) + ")");
    public Task SetHighlightsAsync(Data.Book b) => Run("masar.setHighlights(" + JsonSerializer.Serialize(HighlightsJson(b)) + ")");

    private static object[] HighlightsJson(Data.Book b) =>
        b.Highlights.Select(h => h.Rects.StartsWith("cfi:", StringComparison.Ordinal)
            ? (object)new { id = h.Id, page = h.Page, cfi = h.Rects[4..], rects = Array.Empty<double[]>(), color = h.Color }
            : new { id = h.Id, page = h.Page, cfi = (string?)null, rects = h.ParseRects(), color = h.Color }).ToArray();

    /// <summary>نص الصفحات (file = null: الكتاب المفتوح). progress = (اتعمل، الإجمالي).</summary>
    public async Task<Dictionary<int, string>> ExtractTextAsync(string? file, int from, int to, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        await EnsureReadyAsync();
        string id = Guid.NewGuid().ToString("N");
        var entry = (Pages: new Dictionary<int, string>(), Done: new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), Progress: progress);
        _texts[id] = entry;
        try
        {
            string js = file == null ? $"masar.pageText({JsonSerializer.Serialize(id)}, {from}, {to})"
                                     : $"masar.extract({JsonSerializer.Serialize(id)}, {JsonSerializer.Serialize(Serve(file))}, {from}, {to})";
            await Web.CoreWebView2.ExecuteScriptAsync(js);
            using (ct.Register(() => entry.Done.TrySetCanceled()))
                await entry.Done.Task;
            return entry.Pages;
        }
        finally { _texts.Remove(id); }
    }

    /// <summary>صورة PNG للصفحة (للـ OCR). null لو فشل.</summary>
    public async Task<byte[]?> RenderPageAsync(string? file, int page, double scale, CancellationToken ct)
    {
        await EnsureReadyAsync();
        string id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _images[id] = tcs;
        try
        {
            string url = file == null ? "null" : JsonSerializer.Serialize(Serve(file));
            await Web.CoreWebView2.ExecuteScriptAsync($"masar.renderPage({JsonSerializer.Serialize(id)}, {url}, {page}, {scale.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
            using (ct.Register(() => tcs.TrySetCanceled()))
            {
                var done = await Task.WhenAny(tcs.Task, Task.Delay(60_000, ct));
                return done == tcs.Task ? await tcs.Task : null;
            }
        }
        finally { _images.Remove(id); }
    }

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
            string id = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<DocInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _probes[id] = tcs;
            string url = Serve(file);
            await core.ExecuteScriptAsync($"masar.probe({JsonSerializer.Serialize(id)}, {JsonSerializer.Serialize(url)})");
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            _probes.Remove(id);
            if (done != tcs.Task) Data.Log.Info("reader: probe timed out: " + Path.GetFileName(file));
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
        [JsonPropertyName("rects")] public double[][]? Rects { get; set; }
        [JsonPropertyName("cfi")] public string? Cfi { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("color")] public string? Color { get; set; }
        [JsonPropertyName("note")] public bool Note { get; set; }
        [JsonPropertyName("del")] public bool Del { get; set; }
        [JsonPropertyName("data")] public string? Data { get; set; }
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("done")] public bool Done { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("tomb")] public JsonElement Tomb { get; set; }
        [JsonPropertyName("prefs")] public JsonElement Prefs { get; set; }
        [JsonPropertyName("progress")] public double? Progress { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("map")] public int[][]? Map { get; set; }
        [JsonPropertyName("board")] public JsonElement Board { get; set; }
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
                case "log":
                    Data.Log.Info("reader page: " + m.Message);
                    break;
                case "text" when m.Id != null && _texts.TryGetValue(m.Id, out var tx):
                    if (m.Pages.ValueKind == JsonValueKind.Array)
                        foreach (var pair in m.Pages.EnumerateArray())
                            if (pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() == 2)
                                tx.Pages[pair[0].GetInt32()] = pair[1].GetString() ?? "";
                    tx.Progress?.Report((tx.Pages.Count, m.Total));
                    if (m.Error != null) Data.Log.Info("reader text: " + m.Error);
                    if (m.Done) tx.Done.TrySetResult(m.Error == null);
                    break;
                case "image" when m.Id != null && _images.TryGetValue(m.Id, out var img):
                    if (m.Error != null) Data.Log.Info("reader image: " + m.Error);
                    img.TrySetResult(m.Data != null ? Convert.FromBase64String(m.Data) : null);
                    break;
                case "highlight" when m.Gen == Generation && (m.Rects is { Length: > 0 } || m.Cfi != null):
                    HighlightRequested?.Invoke(m.Gen, m.Page, m.Rects ?? Array.Empty<double[]>(), m.Text ?? "", m.Color ?? "yellow", m.Note, m.Cfi);
                    break;
                case "hledit" when m.Gen == Generation && m.Id != null:
                    HighlightEdited?.Invoke(m.Gen, m.Id, m.Color, m.Del);
                    break;
                case "probe":
                    if (!m.Ok) Data.Log.Info($"reader: probe failed: {m.Code} {m.Message}");
                    if (m.Id != null && _probes.TryGetValue(m.Id, out var tcs)) tcs.TrySetResult(m.Ok ? ToInfo(m) : null);
                    break;
                case "loaded" when m.Gen == Generation:
                    Info = ToInfo(m);
                    Loaded?.Invoke(m.Gen, Info);
                    break;
                case "state" when m.Gen == Generation:
                    Current = new State(m.Page, m.Pages.ValueKind == JsonValueKind.Number ? m.Pages.GetInt32() : 0, m.Scale ?? "page-width", m.Top, m.Left, m.Spread, m.Theme ?? "normal", m.Rtl, m.Cfi);
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
                case "job" when m.Id != null && _jobs.TryGetValue(m.Id, out var job):
                    if (m.Progress is { } pr) job.Progress?.Report(pr);
                    else job.Done.TrySetResult(new JobResult(m.Ok, m.Error, m.Size, m.Pages.ValueKind == JsonValueKind.Number ? m.Pages.GetInt32() : 0));
                    break;
                case "pick" when m.Gen == Generation && m.Id != null:
                    PickRequested?.Invoke(m.Gen, m.Id, m.Kind ?? "", m.Name ?? "");
                    break;
                case "board" when m.Board.ValueKind == JsonValueKind.Object:
                    BoardChanged?.Invoke(m.Gen, m.Board.GetRawText());
                    break;
                case "pemap" when m.Gen == Generation && m.Map != null:
                    PagesRemapped?.Invoke(m.Gen, m.Map);
                    break;
                case "ink":
                    InkChanged?.Invoke(m.Gen, m.Pages, m.Tomb);
                    break;
                case "inkprefs" when m.Prefs.ValueKind == JsonValueKind.Object:
                    InkPrefsChanged?.Invoke(m.Prefs.GetRawText());
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
