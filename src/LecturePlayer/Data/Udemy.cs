using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>كورس من كورساتي على يوديمي.</summary>
public sealed record UdemyCourse(long Id, string Title, string Url, string? Instructor);

/// <summary>درس: فيديو أو مقال أو اختبار (الفيديو بيتشغل في «مسار»، والباقي بيتفتح في نفس المساحة كصفحة).</summary>
public sealed record UdemyLecture(long Id, string Title, long DurationMs, string Kind);

/// <summary>قسم (Section) = محاضرة في «مسار».</summary>
public sealed record UdemyChapter(long Id, string Title, List<UdemyLecture> Lectures);

/// <summary>
/// يوديمي: الروابط وقراية ردود الموقع وبناء/تحديث الكورس في المكتبة.
/// مفيش هنا أي اتصال — البيانات بتتجاب من جوه جلسة الدخول (WebView2) عشان يوديمي يعاملها زي المتصفح.
/// التشغيل نفسه من يوديمي (الفيديوهات محمية) — مفيش تنزيل ولا فك حماية.
/// </summary>
public static class Udemy
{
    public const string Origin = "https://www.udemy.com";

    /// <summary>للفحص التلقائي: عنوان بديل بنفس شكل يوديمي (صفحات اختبار جوه الجهاز).</summary>
    public static string? TestOrigin { get; set; }

    public static string CurrentOrigin => TestOrigin ?? Origin;

    private static readonly Regex LectureRx = new(@"^https?://[^/]+/course/(?<slug>[^/?#]+)/learn/(?<kind>lecture|quiz|practice)/(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>رابط درس يوديمي (www.udemy.com أو حساب شركة xxx.udemy.com).</summary>
    public static bool IsLecture(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        var m = LectureRx.Match(url);
        if (!m.Success) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        string host = u.Host.ToLowerInvariant();
        string? test = TestOrigin != null && Uri.TryCreate(TestOrigin, UriKind.Absolute, out var t) ? t.Host.ToLowerInvariant() : null;
        return host == "www.udemy.com" || host == "udemy.com" || host.EndsWith(".udemy.com", StringComparison.Ordinal) || host == test;
    }

    public static long LectureId(string? url) =>
        url != null && LectureRx.Match(url) is { Success: true } m && long.TryParse(m.Groups["id"].Value, out long id) ? id : 0;

    public static string? CourseSlug(string? url)
    {
        if (url == null) return null;
        var m = Regex.Match(url, @"/course/(?<slug>[^/?#]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["slug"].Value : null;
    }

    public static string LectureUrl(string slug, long lectureId) => $"{CurrentOrigin}/course/{slug}/learn/lecture/{lectureId}";

    /// <summary>رابط عنصر من المنهج: درس، أو اختبار (/learn/quiz/)، أو تمرين (/learn/practice/).</summary>
    public static string ItemUrl(string slug, UdemyLecture item) => item.Kind switch
    {
        "Quiz" => $"{CurrentOrigin}/course/{slug}/learn/quiz/{item.Id}",
        "Practice" => $"{CurrentOrigin}/course/{slug}/learn/practice/{item.Id}",
        _ => LectureUrl(slug, item.Id)
    };

    /// <summary>«lecture:123» / «quiz:45» — الأرقام بتتكرر بين الدروس والاختبارات، فالمفتاح فيه النوع.</summary>
    public static string? ItemKey(string? url) =>
        url != null && LectureRx.Match(url) is { Success: true } m ? m.Groups["kind"].Value.ToLowerInvariant() + ":" + m.Groups["id"].Value : null;
    public static string CourseUrl(string slug) => $"{CurrentOrigin}/course/{slug}/";

    // ---------------- روابط الواجهة (نفس اللي موقع يوديمي بيستخدمها) ----------------

    public static string MeApi => "/api-2.0/users/me/?fields[user]=id,title,display_name";
    public static string SubscribedApi(int page) =>
        $"/api-2.0/users/me/subscribed-courses/?page={page}&page_size=100&ordering=-last_accessed" +
        "&fields[course]=id,title,url,visible_instructors&fields[user]=title,display_name";
    public static string CurriculumApi(long courseId, int page) =>
        $"/api-2.0/courses/{courseId}/subscriber-curriculum-items/?page={page}&page_size=200" +
        "&fields[lecture]=title,object_index,is_published,asset&fields[chapter]=title,object_index" +
        "&fields[quiz]=title,object_index,type&fields[practice]=title&fields[asset]=asset_type,time_estimation,length";
    public static string ProgressApi(long courseId) =>
        $"/api-2.0/users/me/subscribed-courses/{courseId}/progress/?fields[course]=completed_lecture_ids";

    // ---------------- قراية الردود ----------------

    /// <summary>«/api-2.0/users/me/» ← اسم المستخدم لو داخل، null لو مش داخل.</summary>
    public static string? ParseMe(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (!r.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || id.GetInt64() <= 0) return null;
            return Str(r, "display_name") ?? Str(r, "title") ?? "";
        }
        catch (JsonException) { return null; }
    }

    /// <summary>صفحة من كورساتي + هل فيه صفحة تانية.</summary>
    public static (List<UdemyCourse> Courses, bool HasNext) ParseSubscribed(string json)
    {
        var list = new List<UdemyCourse>();
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in results.EnumerateArray())
            {
                long id = c.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt64() : 0;
                string? title = Str(c, "title");
                string? url = Str(c, "url");
                if (id <= 0 || title == null) continue;
                string? instructor = c.TryGetProperty("visible_instructors", out var ins) && ins.ValueKind == JsonValueKind.Array
                    ? ins.EnumerateArray().Select(x => Str(x, "display_name") ?? Str(x, "title")).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    : null;
                list.Add(new UdemyCourse(id, title.Trim(), url ?? $"/course/{id}/", instructor?.Trim()));
            }
        }
        bool next = r.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String && n.GetString()!.Length > 0;
        return (list, next);
    }

    /// <summary>
    /// المنهج بالترتيب: الأقسام ودروسها. الدروس اللي قبل أول قسم بتتحط في قسم «البداية».
    /// الاختبارات والتمارين بتتضاف كدروس (بتتفتح في نفس المساحة) عشان الترتيب يفضل زي يوديمي.
    /// </summary>
    public static (List<UdemyChapter> Chapters, bool HasNext) ParseCurriculum(string json, List<UdemyChapter>? into = null)
    {
        var chapters = into ?? new List<UdemyChapter>();
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in results.EnumerateArray())
            {
                string cls = Str(item, "_class") ?? "";
                long id = item.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt64() : 0;
                string title = (Str(item, "title") ?? "").Trim();
                if (cls == "chapter")
                {
                    chapters.Add(new UdemyChapter(id, title, new List<UdemyLecture>()));
                    continue;
                }
                if (cls is not ("lecture" or "quiz" or "practice")) continue;
                if (item.TryGetProperty("is_published", out var pub) && pub.ValueKind == JsonValueKind.False) continue;
                if (chapters.Count == 0) chapters.Add(new UdemyChapter(0, "البداية", new List<UdemyLecture>()));

                string kind = cls == "lecture" ? "Video" : cls == "quiz" ? "Quiz" : "Practice";
                long ms = 0;
                if (item.TryGetProperty("asset", out var asset) && asset.ValueKind == JsonValueKind.Object)
                {
                    kind = cls == "lecture" ? Str(asset, "asset_type") ?? "Video" : kind;
                    double sec = Num(asset, "time_estimation");
                    if (sec <= 0) sec = Num(asset, "length");
                    ms = (long)(sec * 1000);
                }
                // الأرقام اللي من غير «_class» (ردود قديمة) بتتعامل كدروس
                chapters[^1].Lectures.Add(new UdemyLecture(id, title.Length > 0 ? title : $"Lecture {id}", ms, kind));
            }
        }
        bool next = r.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String && n.GetString()!.Length > 0;
        return (chapters, next);
    }

    public static HashSet<long> ParseProgress(string json)
    {
        var set = new HashSet<long>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("completed_lecture_ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
                foreach (var x in ids.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.Number) set.Add(x.GetInt64());
        }
        catch (JsonException) { }
        return set;
    }

    // ---------------- الكورس في المكتبة ----------------

    /// <summary>نتيجة التحديث.</summary>
    public sealed record SyncResult(int AddedLectures, int AddedSections, int MarkedWatched);

    /// <summary>
    /// بيبني/يحدّث كورس «مسار» من منهج يوديمي:
    /// القسم = محاضرة، والدرس = فيديو بالرابط بتاعه. اللي موجود بيفضل بنفس الكائن (ملاحظاتك ومكانك وعلاماتك)،
    /// والجديد بيتضاف في مكانه بعلامة «جديد» (إلا أول استيراد). اللي خلّصته على يوديمي بيتعلّم «اتشاف» (ومابنشيلش «اتشاف» أبدًا).
    /// </summary>
    public static SyncResult Apply(Course course, UdemyCourse source, List<UdemyChapter> chapters, ISet<long> completed, bool firstImport)
    {
        string slug = CourseSlug(source.Url) ?? source.Id.ToString(CultureInfo.InvariantCulture);
        course.UdemyId = source.Id;
        course.SourceUrl = CourseUrl(slug);
        if (string.IsNullOrWhiteSpace(course.Instructor) && source.Instructor != null) course.Instructor = source.Instructor;

        // كل الدروس الموجودة (بالرقم) — حتى لو المستخدم نقلها لمحاضرة تانية
        var existing = new Dictionary<string, VideoItem>();
        foreach (var v in course.AllVideos)
            if (ItemKey(v.Path) is { } k) existing.TryAdd(k, v);

        int addedLectures = 0, addedSections = 0, watched = 0;
        foreach (var ch in chapters)
        {
            string key = "udemy-chapter:" + ch.Id.ToString(CultureInfo.InvariantCulture);
            var group = course.Groups.FirstOrDefault(g => g.SourceUrl == key)
                        ?? course.Groups.FirstOrDefault(g => g.SourceUrl == null && string.Equals(g.Name, ch.Title, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                group = new LectureGroup { Name = ch.Title.Length > 0 ? ch.Title : "قسم", SourceUrl = key };
                course.Groups.Add(group);
                if (!firstImport) addedSections++;
            }
            group.SourceUrl ??= key;

            foreach (var lec in ch.Lectures)
            {
                string url = ItemUrl(slug, lec);
                string itemKey = ItemKey(url)!;
                if (existing.TryGetValue(itemKey, out var v))
                {
                    if (v.DurationMs <= 0 && lec.DurationMs > 0) { v.DurationMs = lec.DurationMs; v.Touch(); }
                }
                else
                {
                    v = new VideoItem
                    {
                        Path = url,
                        Title = lec.Kind is "Video" or "" ? lec.Title : $"{KindIcon(lec.Kind)} {lec.Title}",
                        DurationMs = lec.DurationMs,
                        IsNew = !firstImport
                    };
                    v.Touch();
                    // في مكانه: بعد آخر درس قبله في نفس القسم
                    int at = group.Videos.Count;
                    int idx = ch.Lectures.IndexOf(lec);
                    for (int k = idx - 1; k >= 0; k--)
                    {
                        string prev = ItemKey(ItemUrl(slug, ch.Lectures[k]))!;
                        int pos = group.Videos.FindIndex(x => ItemKey(x.Path) == prev);
                        if (pos >= 0) { at = pos + 1; break; }
                        if (k == 0) at = 0;
                    }
                    if (idx == 0) at = 0;
                    group.Videos.Insert(Math.Clamp(at, 0, group.Videos.Count), v);
                    existing[itemKey] = v;
                    if (!firstImport) addedLectures++;
                }
                // التقدّم على يوديمي بالدروس بس (completed_lecture_ids)
                if (lec.Kind is not ("Quiz" or "Practice") && completed.Contains(lec.Id) && !v.Watched)
                {
                    v.Watched = true;
                    v.Touch();
                    watched++;
                }
            }
        }
        course.TouchInfo();
        return new SyncResult(addedLectures, addedSections, watched);
    }

    private static string KindIcon(string kind) => kind switch
    {
        "Article" => "📄",
        "Quiz" => "❓",
        "Practice" => "🧪",
        "File" or "E-Book" or "Presentation" => "📎",
        _ => "▶"
    };

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
