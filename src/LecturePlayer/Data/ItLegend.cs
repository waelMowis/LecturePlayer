using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>درس/عنصر من منهج IT Legend (Type: 0 فيديو، 1 امتحان، 4 مراجعة…).</summary>
public sealed record ItLegendNode(string Id, string Title, int Type, int Order, long DurationMs, bool Done);

public sealed record ItLegendModule(string Id, string Title, int Order, List<ItLegendNode> Nodes);

public sealed record ItLegendCourse(string Id, string Title, string? Instructor, int Progress);

/// <summary>
/// IT Legend (itlegend.net): المنهج من نفس واجهة الموقع اللي صفحة الكورس بتستخدمها (api.itlegend.net)،
/// والتشغيل بصفحة الكورس الحقيقية جوه «مسار» (المشغّل VdoCipher محمي — مفيش تنزيل).
/// شكل البيانات متاخد من جلسة حقيقية (HAR) — 24/11/2026.
/// </summary>
public static class ItLegend
{
    public const string Site = "https://itlegend.net";
    public const string Api = "https://api.itlegend.net/api";
    public const string LearnPath = Site + "/ar/learn-path";

    private static readonly Regex CourseRx = new(@"^https?://(?<host>[^/?#]+)/(?:[a-z]{2}/)?learn-path/course-player/(?<id>[0-9a-f-]{36})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LessonRx = new(@"^https?://(?<host>[^/?#]+)/(?:[a-z]{2}/)?learn-path/course-player/(?<id>[0-9a-f-]{36})#node=(?<node>[0-9a-f-]{36})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>للفحص الآلي: موقع وهمي (host:port) بدل itlegend.net.</summary>
    internal static string? TestHost;
    private static bool HostOk(Match m)
    {
        string h = m.Groups["host"].Value;
        return h.Equals("itlegend.net", StringComparison.OrdinalIgnoreCase) || h.Equals("www.itlegend.net", StringComparison.OrdinalIgnoreCase) ||
               (TestHost != null && h.Equals(TestHost, StringComparison.OrdinalIgnoreCase));
    }
    private static string SiteBase => TestHost != null ? "http://" + TestHost : Site;

    public static string CourseUrl(string courseId) => $"{SiteBase}/ar/learn-path/course-player/{courseId}";
    public static string? CourseIdOf(string? url) => url != null && CourseRx.Match(url) is { Success: true } m && HostOk(m) ? m.Groups["id"].Value.ToLowerInvariant() : null;

    /// <summary>رابط درس عندنا: صفحة الكورس + #node=… (+ عنوان القسم والدرس عشان نختاره من المنهج في الصفحة).</summary>
    public static string LessonUrl(string courseId, ItLegendModule module, ItLegendNode node, string? nextModule) =>
        $"{CourseUrl(courseId)}#node={node.Id}&m={Uri.EscapeDataString(module.Title)}&mo={module.Order}&t={Uri.EscapeDataString(node.Title)}&o={node.Order}" +
        (nextModule != null ? "&n=" + Uri.EscapeDataString(nextModule) : "");

    public static bool IsLesson(string? url) => url != null && LessonRx.Match(url) is { Success: true } m && HostOk(m);

    public sealed record LessonRef(string CourseId, string NodeId, string ModuleTitle, int ModuleOrder, string Title, int Order, string? NextModule);

    public static LessonRef? ParseLesson(string? url)
    {
        if (url == null || LessonRx.Match(url) is not { Success: true } m || !HostOk(m)) return null;
        int hash = url.IndexOf('#');
        var q = System.Web.HttpUtility.ParseQueryString(url[(hash + 1)..]);
        int.TryParse(q["mo"], out int mo);
        int.TryParse(q["o"], out int o);
        return new LessonRef(m.Groups["id"].Value.ToLowerInvariant(), m.Groups["node"].Value.ToLowerInvariant(),
            q["m"] ?? "", mo, q["t"] ?? "", o, q["n"]);
    }

    public static string NodeRef(string id) => "itl-node:" + id;
    public static string ModuleRef(string id) => "itl-module:" + id;

    // ======================= قراية الردود =======================

    /// <summary>{"success":false,"message":…} → الرسالة؛ أو null لو تمام.</summary>
    public static string? ErrorOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                return Str(r, "message") ?? Str(r, "errorCode") ?? "رفض الموقع الطلب";
            return null;
        }
        catch (JsonException) { return "رد غير مفهوم من الموقع"; }
    }

    /// <summary>/MemberCoursePlayer/{id} → اسم الكورس والمحاضر ونسبة الإنجاز.</summary>
    public static ItLegendCourse? ParseCourse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) return null;
        string? id = Str(d, "courseId");
        if (id == null) return null;
        string title = (Str(d, "titleAr") ?? Str(d, "titleEn") ?? "IT Legend").Trim();
        string? instructor = (Str(d, "instructorArName") ?? Str(d, "instructorEnName"))?.Trim();
        int progress = d.TryGetProperty("progressPercentage", out var p) && p.TryGetInt32(out int pp) ? pp : 0;
        return new ItLegendCourse(id.ToLowerInvariant(), title, string.IsNullOrWhiteSpace(instructor) ? null : instructor, progress);
    }

    /// <summary>/CourseNode/{id}/nodes → الأقسام بالترتيب، وجوه كل قسم الدروس بالترتيب.</summary>
    public static List<ItLegendModule> ParseNodes(string json)
    {
        var list = new List<ItLegendModule>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
        foreach (var m in data.EnumerateArray())
        {
            var nodes = new List<ItLegendNode>();
            if (m.TryGetProperty("nodes", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var n in arr.EnumerateArray())
                {
                    string? id = Str(n, "nodeId");
                    if (id == null) continue;
                    if (n.TryGetProperty("isAccessible", out var acc) && acc.ValueKind == JsonValueKind.False) continue;
                    int type = Int(n, "type");
                    bool done = Bool(n, "isWatched") || Bool(n, "isPassed");
                    nodes.Add(new ItLegendNode(id.ToLowerInvariant(), (Str(n, "titleAr") ?? Str(n, "titleEn") ?? "درس").Trim(),
                        type, Int(n, "displayOrder"), ParseDuration(Str(n, "duration")), done));
                }
            nodes.Sort((a, b) => a.Order.CompareTo(b.Order));
            if (nodes.Count == 0) continue;
            list.Add(new ItLegendModule((Str(m, "moduleId") ?? "").ToLowerInvariant(),
                (Str(m, "moduleTitleAr") ?? Str(m, "moduleTitleEn") ?? "قسم").Trim(), Int(m, "moduleOrder"), nodes));
        }
        list.Sort((a, b) => a.Order.CompareTo(b.Order));
        return list;
    }

    /// <summary>"00:17:22" → ms.</summary>
    public static long ParseDuration(string? s) =>
        s != null && TimeSpan.TryParseExact(s, new[] { @"hh\:mm\:ss", @"h\:mm\:ss", @"mm\:ss" }, CultureInfo.InvariantCulture, out var t)
            ? (long)t.TotalMilliseconds : 0;

    private static string Icon(int type) => type switch { 0 => "", 1 => "❓ ", 4 => "📄 ", _ => "🌐 " };

    // ======================= الدمج =======================

    public sealed record SyncResult(int AddedItems, int AddedSections, int MarkedWatched);

    /// <summary>
    /// الأقسام والدروس في الكورس: الموجود بيفضل (ملاحظاتك ومكانك وأسمائك)، والجديد بيتضاف في مكانه بعلامة «جديد»،
    /// واللي خلّصته على الموقع بيتعلّم «اتشاف» (من غير ما نشيل علامة).
    /// </summary>
    public static SyncResult Apply(Course course, ItLegendCourse source, List<ItLegendModule> modules, bool firstImport)
    {
        course.SourceUrl = CourseUrl(source.Id);
        if (source.Instructor != null) course.Instructor = source.Instructor;
        course.TouchInfo();

        var existing = new Dictionary<string, VideoItem>();
        foreach (var v in course.AllVideos)
            if (v.SourceRef != null) existing.TryAdd(v.SourceRef, v);

        int added = 0, addedSections = 0, watched = 0;
        for (int mi = 0; mi < modules.Count; mi++)
        {
            var mod = modules[mi];
            string? nextTitle = mi + 1 < modules.Count ? modules[mi + 1].Title : null;
            string mref = ModuleRef(mod.Id);
            var group = course.Groups.FirstOrDefault(g => g.SourceUrl == mref)
                        ?? course.Groups.FirstOrDefault(g => g.SourceUrl == null && string.Equals(g.Name, mod.Title, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                group = new LectureGroup { Name = mod.Title, SourceUrl = mref };
                group.InfoUpdatedUtc = DateTime.UtcNow;
                course.Groups.Add(group);
                addedSections++;
            }
            else if (group.SourceUrl != mref) { group.SourceUrl = mref; group.InfoUpdatedUtc = DateTime.UtcNow; }

            for (int k = 0; k < mod.Nodes.Count; k++)
            {
                var node = mod.Nodes[k];
                string nref = NodeRef(node.Id);
                string path = LessonUrl(source.Id, mod, node, nextTitle);
                if (existing.TryGetValue(nref, out var v))
                {
                    if (v.Path != path) { v.Path = path; v.Touch(); } // الترتيب/الاسم اتغير على الموقع
                    if (node.DurationMs > 0 && v.DurationMs != node.DurationMs) { v.DurationMs = node.DurationMs; v.Touch(); }
                }
                else
                {
                    v = new VideoItem
                    {
                        Path = path,
                        Title = Icon(node.Type) + node.Title,
                        SourceRef = nref,
                        DurationMs = node.DurationMs,
                        IsNew = !firstImport,
                        UpdatedUtc = DateTime.UtcNow
                    };
                    int at = 0;
                    for (int p = k - 1; p >= 0; p--)
                    {
                        string prev = NodeRef(mod.Nodes[p].Id);
                        int pos = group.Videos.FindIndex(x => x.SourceRef == prev);
                        if (pos >= 0) { at = pos + 1; break; }
                    }
                    if (k > 0 && at == 0) at = group.Videos.Count;
                    group.Videos.Insert(Math.Min(at, group.Videos.Count), v);
                    existing[nref] = v;
                    if (!firstImport) added++;
                }
                if (node.Done && !v.Watched) { v.Watched = true; v.Touch(); watched++; }
            }
        }
        return new SyncResult(added, firstImport ? 0 : addedSections, watched);
    }

    // ======================= اختيار الدرس في الصفحة =======================

    /// <summary>
    /// سكربت بيختار الدرس من منهج صفحة الكورس: بيفتح القسم (لو مقفول) وبيدوس على الدرس بالاسم (ولو الاسم متكرر، بالترتيب جوه القسم).
    /// بيرجّع نص بيقول عمل إيه (للتشخيص). التأكيد الحقيقي إن الصفحة طلبت الدرس من السيرفر (/CourseVideo/…/videos/{node}).
    /// </summary>
    public static string SelectScript(LessonRef l) => "(() => {" +
        "const norm = s => (s || '').replace(/\\s+/g, ' ').trim().toLowerCase();" +
        $"const mt = norm({Js(l.ModuleTitle)}), nt = norm({Js(l.Title)}), nx = norm({Js(l.NextModule ?? "")}), mo = {l.ModuleOrder};" +
        // كل العناصر اللي نصها بيبدأ بعنوان (أصغر عنصر = أقرب حاجة للنص)
        "const all = [...document.querySelectorAll('button, a, li, div, span, p, h3, h4, h5')];" +
        "const own = e => norm(e.innerText);" +
        "const smallest = list => list.filter(e => !list.some(x => x !== e && e.contains(x)));" +
        "const starts = (t) => smallest(all.filter(e => { const s = own(e); return s === t || s.startsWith(t + ' ') || s.startsWith(t + '\\n'); }));" +
        "const visible = e => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0; };" +
        "const clickable = e => { for (let x = e; x && x !== document.body; x = x.parentElement) {" +
        "  if (x.tagName === 'BUTTON' || x.tagName === 'A' || x.onclick || x.getAttribute('role') === 'button' || getComputedStyle(x).cursor === 'pointer') return x; } return e; };" +
        // 1) القسم
        "let headers = starts(mt);" +
        "if (headers.length === 0) return 'no-module:' + mt;" +
        "const header = headers.length > 1 && mo > 0 && headers[mo - 1] ? headers[mo - 1] : headers[0];" +
        // 2) الدروس اللي بعد القسم ده وقبل القسم اللي بعده
        // القسم اللي بعده (عشان «intro» اللي في قسم تاني مايتحسبش)
        "const next = nx ? starts(nx).find(e => header.compareDocumentPosition(e) & Node.DOCUMENT_POSITION_FOLLOWING) : null;" +
        "const inModule = () => starts(nt).filter(e => visible(e) && (header.compareDocumentPosition(e) & Node.DOCUMENT_POSITION_FOLLOWING)" +
        "  && !(next && (next.compareDocumentPosition(e) & Node.DOCUMENT_POSITION_FOLLOWING)) && !header.contains(e));" +
        "let lessons = inModule();" +
        "if (lessons.length === 0) { clickable(header).click(); return 'opened-module'; }" +
        // لو الاسم متكرر (intro في كل قسم): أول واحد بعد القسم ده
        "const target = lessons[0];" +
        "clickable(target).click();" +
        "return 'clicked:' + lessons.length;" +
        "})()";

    private static string Js(string s) => JsonSerializer.Serialize(s);

    // ======================= أدوات =======================

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;
    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>واجهة api.itlegend.net بتوكن جلستك (اللي صفحة الموقع نفسها بتستخدمه) — مابيتحفظش على القرص.</summary>
public sealed class ItLegendClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(40) };

    public ItLegendClient(string bearer)
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? bearer : "Bearer " + bearer);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Platform", "website");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", ItLegend.Site);
        _http.DefaultRequestHeaders.Referrer = new Uri(ItLegend.Site + "/");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    /// <summary>للفحص الآلي: سيرفر وهمي بدل api.itlegend.net.</summary>
    internal static string? TestApi;
    private static string ApiBase => TestApi ?? ItLegend.Api;

    public async Task<string> GetAsync(string path, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(ApiBase + path, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if ((int)resp.StatusCode is 401 or 403) throw new UnauthorizedAccessException("انتهت جلسة الدخول إلى IT Legend — افتح النافذة وسجّل الدخول مرة أخرى");
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"IT Legend رد بـ {(int)resp.StatusCode}");
        if (ItLegend.ErrorOf(body) is { } err) throw new InvalidOperationException(err);
        return body;
    }

    public void Dispose() => _http.Dispose();
}
