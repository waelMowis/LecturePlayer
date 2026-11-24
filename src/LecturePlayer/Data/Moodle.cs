using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>كورس على موقع Moodle (مهارة تك، ITI، الجامعات…).</summary>
public sealed record MoodleCourse(long Id, string Title, string? Instructor);

/// <summary>نشاط في الكورس. Video = رابط يوتيوب لو لقيناه (وده بيتشغل في «مسار» نفسه).</summary>
public sealed record MoodleModule(long Id, string Name, string ModName, string Url, bool Completed, string? Video = null)
{
    public string? Video { get; set; } = Video;
}

public sealed record MoodleSection(long Id, string Name, List<MoodleModule> Modules);

/// <summary>حساب Moodle محفوظ على الجهاز ده (التوكن متشفّر بحساب الويندوز).</summary>
public sealed class MoodleAccount
{
    public string Site { get; set; } = "";
    public string? SiteName { get; set; }
    public long UserId { get; set; }
    public string? UserName { get; set; }
    public string TokenProtected { get; set; } = "";
    public string PrivateTokenProtected { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Token { get => SecretStore.Unprotect(TokenProtected); set => TokenProtected = SecretStore.Protect(value); }
    [System.Text.Json.Serialization.JsonIgnore]
    public string PrivateToken { get => SecretStore.Unprotect(PrivateTokenProtected); set => PrivateTokenProtected = SecretStore.Protect(value); }
}

/// <summary>
/// ربط Moodle بالطريقة الرسمية بتاعة تطبيق Moodle للموبايل:
/// الدخول بيحصل في متصفحك العادي (فجوجل/مايكروسوفت بيشتغلوا)، والموقع بيرجّع لـ «مسار» توكن عن طريق رابط moodlemobile://،
/// وبعدها «مسار» بيقرا كورساتك وأقسامها وأنشطتها وإكمالك من خدمة الويب بتاعة الموقع.
/// الفيديوهات اللي على يوتيوب (زي مهارة تك) بتتحول لروابط يوتيوب عادية بتشتغل في «مسار» بكل مميزاته.
/// </summary>
public static class Moodle
{
    public const string Scheme = "moodlemobile";
    public const string Service = "moodle_mobile_app";
    public const string DefaultSite = "https://maharatech.gov.eg";

    private static readonly Regex ActivityRx = new(@"^https?://[^?#]+/mod/(?<mod>[a-z0-9_]+)/view\.php\?(?:[^#]*&)?id=(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CourseRx = new(@"^(?<site>https?://.+?)/course/view\.php\?(?:[^#]*&)?id=(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // يوتيوب جوه HTML أو JSON أو JSON جوه JSON (الـ / ممكن تبقى \/ أو \\\/) — watch?v= / embed/ / youtu.be/ / shorts/ / v/
    private static readonly Regex YouTubeRx = new(
        @"(?:youtube(?:-nocookie)?\.com\\*/(?:watch\?(?:[^""'<>\s]*?(?:&|\\u0026)(?:amp;)?)?v=|embed\\*/|v\\*/|shorts\\*/|live\\*/)|youtu\.be\\*/)(?<id>[A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>صفحة نشاط Moodle (/mod/…/view.php?id=) — بتتفتح في متصفحك، ومابتتنزلش.</summary>
    public static bool IsActivity(string? url) => url != null && ActivityRx.IsMatch(url);

    public static string CourseUrl(string site, long id) => $"{site}/course/view.php?id={id}";

    public static (string Site, long Id)? ParseCourseUrl(string? url) =>
        url != null && CourseRx.Match(url) is { Success: true } m ? (m.Groups["site"].Value.TrimEnd('/'), long.Parse(m.Groups["id"].Value)) : null;

    /// <summary>عنوان الموقع من أي رابط فيه (صفحة كورس/نشاط/الرئيسية) → https://host[/مسار-الموقع].</summary>
    public static string? NormalizeSite(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string s = input.Trim();
        if (!s.Contains("://")) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http") || string.IsNullOrEmpty(u.Host)) return null;
        string path = u.AbsolutePath;
        foreach (string marker in new[] { "/course/", "/mod/", "/my/", "/login/", "/user/", "/admin/", "/blocks/", "/local/", "/theme/", "/index.php", "/webservice/" })
        {
            int i = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) { path = path[..i]; break; }
        }
        return (u.Scheme + "://" + u.Authority + path).TrimEnd('/');
    }

    public static string NewPassport() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    /// <summary>
    /// الصفحة اللي بتتفتح في المتصفح: الموقع يطلب الدخول (لو مش داخل) ويرجّع التوكن على moodlemobile://.
    /// confirmed=1: الصفحة بتعرض رابط بيتداس لوحده بدل تحويل مباشر (Location) لـ moodlemobile:// —
    /// جدران الحماية (زي اللي قدام مهارة تك) بترفض التحويل لعنوان بره الموقع وبتطلع «Request Rejected».
    /// </summary>
    public static string LaunchUrl(string site, string passport) =>
        $"{site}/admin/tool/mobile/launch.php?service={Service}&passport={passport}&urlscheme={Scheme}&confirmed=1";

    /// <summary>
    /// اللي المستخدم لزقه بإيده (لو المتصفح مافتحش «مسار»): رابط moodlemobile://token=… كامل، أو الجزء اللي بعد token=.
    /// </summary>
    public static string? NormalizePastedCallback(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim().Trim('"', '\'', '<', '>');
        int i = t.IndexOf(Scheme + "://", StringComparison.OrdinalIgnoreCase);
        if (i >= 0) return t[i..];
        int k = t.IndexOf("token=", StringComparison.OrdinalIgnoreCase);
        if (k >= 0) t = t[(k + 6)..];
        return Regex.IsMatch(t, @"^[A-Za-z0-9+/=_%-]{20,}$") ? Scheme + "://token=" + t : null;
    }

    /// <summary>
    /// moodlemobile://token=BASE64(md5(موقع+passport):::توكن[:::توكن خاص]).
    /// بنتأكد من التوقيع (إن الرد ده بتاع طلبنا وللموقع ده)، وبنجرّب http/https و / في الآخر لأن الموقع ممكن يكتب عنوانه بشكل مختلف.
    /// </summary>
    public static (string Token, string? PrivateToken)? ParseSsoCallback(string? url, string site, string passport)
    {
        if (url == null) return null;
        int i = url.IndexOf("token=", StringComparison.OrdinalIgnoreCase);
        if (!url.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase) || i < 0) return null;
        string b64 = Uri.UnescapeDataString(url[(i + 6)..]).Trim().TrimEnd('/');
        int amp = b64.IndexOfAny(new[] { '&', '#' });
        if (amp >= 0) b64 = b64[..amp];
        string decoded;
        try
        {
            b64 = b64.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        }
        catch (FormatException) { return null; }
        var parts = decoded.Split(":::");
        if (parts.Length < 2 || parts[1].Length < 10) return null;
        string host = site.Contains("://") ? site[(site.IndexOf("://", StringComparison.Ordinal) + 3)..] : site;
        var candidates = new[] { "https://" + host, "http://" + host, "https://" + host + "/", "http://" + host + "/" };
        bool signed = candidates.Any(c => string.Equals(Md5(c + passport), parts[0], StringComparison.OrdinalIgnoreCase));
        if (!signed) return null;
        return (parts[1], parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null);
    }

    private static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    // ======================= قراية الردود =======================

    /// <summary>رد خطأ من Moodle ({"exception":…,"errorcode":…,"message":…}) → الرسالة، أو null.</summary>
    public static string? ErrorOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("exception", out _))
                return (Str(doc.RootElement, "errorcode") ?? "error") + ": " + (Str(doc.RootElement, "message") ?? "");
        }
        catch (JsonException) { return "رد مش مفهوم من الموقع"; }
        return null;
    }

    public static (long UserId, string? Name, string? SiteName, string? SiteUrl) ParseSiteInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return (Long(r, "userid"), Str(r, "fullname"), Str(r, "sitename"), Str(r, "siteurl"));
    }

    public static List<MoodleCourse> ParseCourses(string json)
    {
        var list = new List<MoodleCourse>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            long id = Long(c, "id");
            string? title = Clean(Str(c, "fullname") ?? Str(c, "displayname") ?? Str(c, "shortname"));
            if (id <= 1 || string.IsNullOrWhiteSpace(title)) continue; // 1 = الصفحة الرئيسية للموقع
            list.Add(new MoodleCourse(id, title!, null));
        }
        return list;
    }

    /// <summary>core_course_get_courses_by_field → اسم أول مدرّس لكل كورس.</summary>
    public static Dictionary<long, string> ParseContacts(string json)
    {
        var map = new Dictionary<long, string>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("courses", out var courses) || courses.ValueKind != JsonValueKind.Array) return map;
        foreach (var c in courses.EnumerateArray())
            if (c.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array)
                foreach (var t in contacts.EnumerateArray())
                    if (Clean(Str(t, "fullname")) is { Length: > 0 } n) { map.TryAdd(Long(c, "id"), n); break; }
        return map;
    }

    /// <summary>أنشطة مالهاش لازمة في المذاكرة (عناوين داخل الصفحة، الإعلانات…).</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase) { "label", "forum", "qbank", "subsection" };

    /// <summary>core_course_get_contents → أقسام وأنشطة (المستخبي عنك والفاضي بيتشال).</summary>
    public static List<MoodleSection> ParseContents(string json)
    {
        var sections = new List<MoodleSection>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return sections;
        foreach (var s in doc.RootElement.EnumerateArray())
        {
            if (s.TryGetProperty("uservisible", out var sv) && sv.ValueKind == JsonValueKind.False) continue;
            var mods = new List<MoodleModule>();
            if (s.TryGetProperty("modules", out var modules) && modules.ValueKind == JsonValueKind.Array)
                foreach (var m in modules.EnumerateArray())
                {
                    string mod = Str(m, "modname") ?? "";
                    if (Skipped.Contains(mod)) continue;
                    if (m.TryGetProperty("uservisible", out var mv) && mv.ValueKind == JsonValueKind.False) continue;
                    if (m.TryGetProperty("visible", out var vis) && vis.ValueKind == JsonValueKind.Number && vis.GetInt32() == 0) continue;
                    string? url = Str(m, "url");
                    long id = Long(m, "id");
                    if (id <= 0 || string.IsNullOrEmpty(url)) continue;
                    bool done = m.TryGetProperty("completiondata", out var cd) && cd.ValueKind == JsonValueKind.Object &&
                                Long(cd, "state") is 1 or 2; // 1 = خلص، 2 = خلص ونجح (3 = خلص وسقط)
                    string? video = null;
                    // رابط (mod_url) بيشاور على يوتيوب، أو صفحة فيها رابط يوتيوب في الوصف
                    if (m.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array)
                        foreach (var f in contents.EnumerateArray())
                            if (YouTubeIdIn(Str(f, "fileurl")) is { } yt) { video = yt; break; }
                    video ??= YouTubeIdIn(Str(m, "description"));
                    mods.Add(new MoodleModule(id, Clean(Str(m, "name")) ?? mod, mod, url!, done, video == null ? null : WatchUrl(video)));
                }
            if (mods.Count == 0) continue;
            string name = Clean(Str(s, "name")) ?? "";
            if (name.Length == 0) name = "القسم " + (Long(s, "section") + 1);
            sections.Add(new MoodleSection(Long(s, "id"), name, mods));
        }
        return sections;
    }

    /// <summary>أول فيديو يوتيوب في نص/HTML/JSON (H5P بيحط الرابط جوه JSON بـ \/).</summary>
    public static string? YouTubeIdIn(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = YouTubeRx.Match(text);
        return m.Success ? m.Groups["id"].Value : null;
    }

    private static readonly Regex EmbedRx = new(@"src=\\?[""'](?<u>[^""'\\]*?/mod/hvp/embed\.php[^""'\\]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>رابط embed.php من رد tool_mobile_get_content (HTML جوه JSON، فالـ &amp; و \/ لازم يتفكّوا).</summary>
    public static string? EmbedUrlIn(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        string text = json;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("templates", out var t) && t.ValueKind == JsonValueKind.Array)
                text = string.Join("\n", t.EnumerateArray().Select(x => Str(x, "html") ?? ""));
        }
        catch (JsonException) { }
        var m = EmbedRx.Match(text);
        return m.Success ? WebUtility.HtmlDecode(m.Groups["u"].Value.Replace("\\/", "/")) : null;
    }

    public static string WatchUrl(string id) => "https://www.youtube.com/watch?v=" + id;

    /// <summary>mod_hvp_get_embed_url / tool_mobile_get_autologin_key → الحقل اللي فيه رابط.</summary>
    public static string? UrlField(string json, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? Str(doc.RootElement, field) : null;
        }
        catch (JsonException) { return null; }
    }

    // ======================= الدمج في المكتبة =======================

    public sealed record SyncResult(int AddedItems, int AddedSections, int MarkedWatched, int Videos);

    public static string SectionRef(long id) => "moodle-section:" + id;
    public static string ModuleRef(long id) => "moodle-cm:" + id;

    /// <summary>
    /// بيحط الأقسام والأنشطة في الكورس: الموجود بيفضل (ملاحظاتك ومكانك وأسمائك)، والجديد بيتضاف في مكانه بعلامة «جديد»،
    /// والنشاط اللي اكتشفنا إنه فيديو يوتيوب بيتحوّل لفيديو، واللي خلّصته على الموقع بيتعلّم «اتشاف» (من غير ما نشيل علامة).
    /// </summary>
    public static SyncResult Apply(Course course, string site, MoodleCourse source, List<MoodleSection> sections, bool firstImport)
    {
        course.SourceUrl = CourseUrl(site, source.Id);
        if (!string.IsNullOrWhiteSpace(source.Instructor)) course.Instructor = source.Instructor;
        course.TouchInfo();

        var existing = new Dictionary<string, VideoItem>();
        foreach (var v in course.AllVideos)
            if (v.SourceRef != null) existing.TryAdd(v.SourceRef, v);

        int added = 0, addedSections = 0, watched = 0;
        foreach (var sec in sections)
        {
            string sref = SectionRef(sec.Id);
            var group = course.Groups.FirstOrDefault(g => g.SourceUrl == sref)
                        ?? course.Groups.FirstOrDefault(g => g.SourceUrl == null && string.Equals(g.Name, sec.Name, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                group = new LectureGroup { Name = sec.Name, SourceUrl = sref };
                group.InfoUpdatedUtc = DateTime.UtcNow;
                course.Groups.Add(group);
                addedSections++;
            }
            else if (group.SourceUrl != sref) { group.SourceUrl = sref; group.InfoUpdatedUtc = DateTime.UtcNow; }

            for (int k = 0; k < sec.Modules.Count; k++)
            {
                var mod = sec.Modules[k];
                string mref = ModuleRef(mod.Id);
                string path = mod.Video ?? mod.Url;
                if (existing.TryGetValue(mref, out var v))
                {
                    // كان صفحة ولقيناله فيديو دلوقتي → يبقى فيديو (المكان والملاحظات بيفضلوا)
                    if (mod.Video != null && v.Path != mod.Video) { v.Path = mod.Video; v.Title = StripIcon(v.Title); v.Touch(); }
                }
                else
                {
                    v = new VideoItem
                    {
                        Path = path,
                        Title = mod.Video != null ? mod.Name : Icon(mod.ModName) + mod.Name,
                        SourceRef = mref,
                        IsNew = !firstImport,
                        UpdatedUtc = DateTime.UtcNow
                    };
                    // بعد النشاط اللي قبله في نفس القسم (لو موجود)، وإلا في الأول
                    int at = 0;
                    for (int p = k - 1; p >= 0; p--)
                    {
                        string prev = ModuleRef(sec.Modules[p].Id);
                        int pos = group.Videos.FindIndex(x => x.SourceRef == prev);
                        if (pos >= 0) { at = pos + 1; break; }
                    }
                    if (k > 0 && at == 0) at = group.Videos.Count;
                    group.Videos.Insert(Math.Min(at, group.Videos.Count), v);
                    existing[mref] = v;
                    if (!firstImport) added++;
                }
                if (mod.Completed && !v.Watched) { v.Watched = true; v.Touch(); watched++; }
            }
        }
        int videos = course.AllVideos.Count(v => v.SourceRef != null && !IsActivity(v.Path));
        return new SyncResult(added, firstImport ? 0 : addedSections, watched, videos);
    }

    private static string Icon(string mod) => mod.ToLowerInvariant() switch
    {
        "quiz" => "❓ ",
        "feedback" or "questionnaire" or "survey" or "choice" => "📋 ",
        "assign" => "📝 ",
        "resource" or "folder" => "📎 ",
        "page" or "book" => "📄 ",
        "url" => "🔗 ",
        "hvp" or "h5pactivity" => "🎬 ",
        "customcert" or "certificate" => "🏅 ",
        _ => "🌐 "
    };

    private static string StripIcon(string title)
    {
        int sp = title.IndexOf(' ');
        return sp is > 0 and <= 3 && !char.IsLetterOrDigit(title[0]) ? title[(sp + 1)..] : title;
    }

    // ======================= أدوات =======================

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Long(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n
            : v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out long s) ? s : 0
            : 0;

    /// <summary>أسماء Moodle ممكن يبقى فيها HTML أو كيانات (&amp;) أو وسوم لغات متعددة.</summary>
    private static string? Clean(string? s)
    {
        if (s == null) return null;
        s = Regex.Replace(s, "<span lang=\"(?!ar|en)[^\"]*\" class=\"multilang\">.*?</span>", "", RegexOptions.Singleline);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = WebUtility.HtmlDecode(s);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}

/// <summary>خدمة الويب بتاعة Moodle (REST) بالتوكن — من غير متصفح.</summary>
public sealed class MoodleClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();
    public string Site { get; }
    public string Token { get; }

    public MoodleClient(string site, string token)
    {
        Site = site;
        Token = token;
        _http = new HttpClient(new SocketsHttpHandler { CookieContainer = _cookies, AutomaticDecompression = DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(40) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Masar/" + (typeof(Moodle).Assembly.GetName().Version?.ToString(3) ?? "1"));
    }

    /// <summary>بينادي دالة من خدمة الويب. أخطاء Moodle بترجع InvalidOperationException برسالته.</summary>
    public async Task<string> CallAsync(string function, IEnumerable<KeyValuePair<string, string>>? args, CancellationToken ct)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("wstoken", Token), new("wsfunction", function), new("moodlewsrestformat", "json")
        };
        if (args != null) form.AddRange(args);
        using var resp = await _http.PostAsync(Site + "/webservice/rest/server.php", new FormUrlEncodedContent(form), ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"{function}: HTTP {(int)resp.StatusCode}");
        if (Moodle.ErrorOf(body) is { } err) throw new MoodleException(function, err);
        return body;
    }

    public static KeyValuePair<string, string> Arg(string k, object v) => new(k, Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "");

    /// <summary>صفحة من الموقع (بالكوكيز بتاعة الجلسة لو فيه).</summary>
    public async Task<string?> GetPageAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(ct) : null;
        }
        catch (HttpRequestException ex) { Log.Info("moodle page: " + ex.Message); return null; }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class MoodleException(string function, string message) : InvalidOperationException(message)
{
    public string Function { get; } = function;
    /// <summary>التوكن اتلغى/خلص → لازم دخول تاني.</summary>
    public bool TokenInvalid => Message.StartsWith("invalidtoken", StringComparison.OrdinalIgnoreCase) ||
                                Message.Contains("accessexception", StringComparison.OrdinalIgnoreCase);
}
