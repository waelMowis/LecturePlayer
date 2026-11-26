using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LecturePlayer.Data;

/// <summary>عنصر في درس على منصة الإبداع: مرفق (فيديو/يوتيوب/ملف/موقع) أو تكليف (اختبار/استبيان).</summary>
public sealed record ClasseraItem(string Id, bool IsAssignment, string Type, string Title, double Order)
{
    public bool IsVideo => !IsAssignment && Type is "Youtube" or "Video" or "Vimeo" or "video";
}

public sealed record ClasseraLesson(string Id, string Title, double Order, List<ClasseraItem> Items);

/// <summary>منهج دورة كما تعرضه الصفحة (Lessons/lessons_contents).</summary>
public sealed record ClasseraCourse(string LectureId, string CourseId, List<ClasseraLesson> Lessons);

/// <summary>تقدّمك في الدورة (svaLecture/get_progress_percentage): العناصر المكتملة ونسبة الإنجاز.</summary>
public sealed record ClasseraProgress(HashSet<string> Completed, double Percent, bool Passed);

/// <summary>دورة من «رحلتي التعليمية» (api/v2/trainee/courses).</summary>
public sealed record ClasseraEnrolled(string LectureId, string CourseId, string Title, DateTime? To);

/// <summary>حالة تكليف (Assignments/student_assignment_info): المحاولات والدرجة.</summary>
public sealed record ClasseraAssignmentInfo(string Id, int Submissions, int MaxSubmissions, string? LastGrade, string? MaxMark, bool OutOfAttempts, string? Type);

/// <summary>
/// منصة الإبداع في التعليم الإلكتروني (elearning.classera.com — تدريب كلاسيرا).
/// طلبات الصفحة لسيرفر المنصة مشفّرة، فـ«مسار» لا يطلب منها شيئًا بنفسه: يفتح الصفحة الحقيقية بحسابك جوه «مسار»،
/// ويقرأ الردود التي وصلت إليها (المنهج، والعناصر المكتملة، ونتيجة الاختبار) — نفس ما تراه — من غير توكن ولا كلمة مرور.
/// والتشغيل يكون في صفحة المنصة نفسها، فيُحسب حضورك وتقدّمك للشهادة.
/// شكل البيانات مأخوذ من جلسة حقيقية (26/11/2026).
/// </summary>
public static class Classera
{
    public const string Platform = "منصة الإبداع";
    public const string Site = "https://elearning.classera.com";
    public const string ApiHost = "svaapi.classera.com";
    public const string MyLearning = Site + "/mylearning?tab=courses";

    /// <summary>للفحص الآلي: موقع وهمي (host:port) بدل المنصة، والسيرفر عليه تحت /svaapi/.</summary>
    internal static string? TestHost;
    public static string SiteBase => TestHost != null ? "http://" + TestHost : Site;

    private static bool SiteHost(Uri u) =>
        u.Host.Equals("elearning.classera.com", StringComparison.OrdinalIgnoreCase) ||
        (TestHost != null && u.Authority.Equals(TestHost, StringComparison.OrdinalIgnoreCase));

    /// <summary>رد من سيرفر المنصة (svaapi.classera.com).</summary>
    public static bool IsApi(string? url) =>
        url != null && Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Host.Equals(ApiHost, StringComparison.OrdinalIgnoreCase) ||
         (TestHost != null && u.Authority.Equals(TestHost, StringComparison.OrdinalIgnoreCase) && u.AbsolutePath.StartsWith("/svaapi/", StringComparison.OrdinalIgnoreCase)));

    public enum Api { None, Lessons, Progress, Enrolled, AssignmentInfo }

    public static Api KindOf(string url)
    {
        if (!IsApi(url)) return Api.None;
        string p = new Uri(url).AbsolutePath;
        if (p.EndsWith("/Lessons/lessons_contents", StringComparison.OrdinalIgnoreCase)) return Api.Lessons;
        if (p.EndsWith("/svaLecture/get_progress_percentage", StringComparison.OrdinalIgnoreCase)) return Api.Progress;
        if (p.EndsWith("/trainee/courses", StringComparison.OrdinalIgnoreCase)) return Api.Enrolled;
        if (p.EndsWith("/Assignments/student_assignment_info", StringComparison.OrdinalIgnoreCase)) return Api.AssignmentInfo;
        return Api.None;
    }

    // ======================= الروابط =======================

    /// <summary>رابط المنصة المفهوم: صفحة الدورة، أو صفحة عنصر، أو اختبار.</summary>
    private static Dictionary<string, string>? Param(string? url, string page)
    {
        if (url == null || url.IndexOf("/" + page + "?", StringComparison.OrdinalIgnoreCase) < 0) return null; // سريع: أغلب العناصر ملفات على الجهاز
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !SiteHost(u)) return null;
        if (!u.AbsolutePath.Equals("/" + page, StringComparison.OrdinalIgnoreCase)) return null;
        var q = System.Web.HttpUtility.ParseQueryString(u.Query);
        string? c = q["c"];
        if (string.IsNullOrEmpty(c)) return null;
        try
        {
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(c.Replace('-', '+').Replace('_', '/').PadRight((c.Length + 3) / 4 * 4, '=')));
            using var doc = JsonDocument.Parse(json);
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in doc.RootElement.EnumerateObject())
                d[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
            if (q["title"] is { } t) d["$title"] = t;
            return d;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { return null; }
    }

    /// <summary>صفحة الدورة (course-details) ← رقم الدورة على المنصة (lecture).</summary>
    public static string? CourseLectureOf(string? url) => Param(url, "course-details") is { } d && d.TryGetValue("id", out var id) && id.Length > 0 ? id : null;

    /// <summary>أي صفحة من المنصة فيها رقم الدورة: صفحة الدورة، أو عنصر، أو اختبار.</summary>
    public static string? LectureOfPage(string? url)
    {
        if (CourseLectureOf(url) is { } c) return c;
        return (Param(url, "attachments-details") ?? Param(url, "solve-exam")) is { } d && d.TryGetValue("lecture_id", out var l) ? l : null;
    }

    /// <summary>عنوان الدورة من رابط صفحتها (title=…).</summary>
    public static string? CourseTitleOf(string? url) => Param(url, "course-details") is { } d && d.TryGetValue("$title", out var t) && !string.IsNullOrWhiteSpace(t) ? t.Trim() : null;

    public static string ItemRef(ClasseraItem i) => (i.IsAssignment ? "cls-asg:" : "cls-att:") + i.Id;
    public static string LessonRef(string id) => "cls-lesson:" + id;

    private static string B64(object o) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)));

    /// <summary>صفحة العنصر على المنصة (بنفس صيغة روابط الصفحة نفسها).</summary>
    public static string ItemUrl(ClasseraItem i, string courseId, string lectureId) => i.IsAssignment
        ? $"{SiteBase}/solve-exam?type=exams&title={Uri.EscapeDataString(i.Title)}&c={Uri.EscapeDataString(B64(new Dictionary<string, string> { ["id"] = i.Id, ["courseId"] = courseId, ["lecture_id"] = lectureId }))}"
        : $"{SiteBase}/attachments-details?title={Uri.EscapeDataString(i.Title)}&c={Uri.EscapeDataString(B64(new Dictionary<string, string> { ["id"] = i.Id, ["course_id"] = courseId, ["lecture_id"] = lectureId }))}";

    /// <summary>عنصر من المنصة (يُفتح بصفحته جوه «مسار»).</summary>
    public static bool IsItem(string? url) => Param(url, "attachments-details") != null || Param(url, "solve-exam") != null;
    public static bool IsAssignmentUrl(string? url) => Param(url, "solve-exam") != null;
    public static string? ItemIdOf(string? url) => (Param(url, "attachments-details") ?? Param(url, "solve-exam")) is { } d && d.TryGetValue("id", out var id) ? id : null;

    // ======================= قراءة الردود =======================

    /// <summary>العنوان العربي إن وُجد (المنصة تضع العربي أحيانًا في حقل الإنجليزي والعكس).</summary>
    private static string PickTitle(params string?[] titles)
    {
        foreach (var t in titles)
            if (!string.IsNullOrWhiteSpace(t) && t.Any(ch => ch is >= '؀' and <= 'ۿ')) return t.Trim();
        return titles.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))?.Trim() ?? "";
    }

    public static ClasseraCourse? ParseLessons(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("categorized", out var cats) || cats.ValueKind != JsonValueKind.Array) return null;
        string? lecture = null, course = null;
        var lessons = new List<ClasseraLesson>();
        foreach (var l in cats.EnumerateArray())
        {
            if (Bool(l, "deleted")) continue;
            lecture ??= Str(l, "lecture_id");
            course ??= Str(l, "course_id");
            var items = new List<ClasseraItem>();
            if (l.TryGetProperty("attachments", out var atts) && atts.ValueKind == JsonValueKind.Array)
                foreach (var a in atts.EnumerateArray())
                {
                    if (Str(a, "id") is not { } id || Bool(a, "deleted") || Bool(a, "hide_from_lessons_list")) continue;
                    if (a.TryGetProperty("is_published", out var pub) && (pub.ValueKind == JsonValueKind.False || (pub.ValueKind == JsonValueKind.Number && pub.GetDouble() == 0))) continue;
                    lecture ??= Str(a, "lecture_id");
                    course ??= Str(a, "course_id");
                    items.Add(new ClasseraItem(id, false, Str(a, "type") ?? "", PickTitle(Str(a, "title"), Str(a, "sec_title"), "مرفق"), Num(a, "order")));
                }
            if (l.TryGetProperty("assignments", out var asg) && asg.ValueKind == JsonValueKind.Array)
                foreach (var a in asg.EnumerateArray())
                {
                    if (Str(a, "id") is not { } id || Bool(a, "deleted") || Bool(a, "hide_from_lessons_list")) continue;
                    items.Add(new ClasseraItem(id, true, Str(a, "type") ?? "Exam", PickTitle(Str(a, "title"), Str(a, "sec_title"), "اختبار"), Num(a, "order") + 0.001));
                }
            items = items.OrderBy(i => i.Order).ToList();
            lessons.Add(new ClasseraLesson(Str(l, "id") ?? Guid.NewGuid().ToString("N"), PickTitle(Str(l, "title"), Str(l, "title_ara"), "درس"), Num(l, "lesson_order"), items));
        }
        if (lecture == null || course == null) return null;
        return new ClasseraCourse(lecture, course, lessons.OrderBy(l => l.Order).ToList());
    }

    public static ClasseraProgress? ParseProgress(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("user_completed", out var done)) return null;
        var set = new HashSet<string>();
        void Add(JsonElement arr)
        {
            if (arr.ValueKind != JsonValueKind.Array) return;
            foreach (var x in arr.EnumerateArray())
            {
                var rec = x.TryGetProperty("ContentUserRecord", out var cur) ? cur : x;
                if (Str(rec, "reference_id") is { } id) set.Add(id);
            }
        }
        Add(done);
        if (r.TryGetProperty("optional_user_completed", out var opt)) Add(opt);
        double pct = Num(r, "user_percentage");
        // user_percentage: كسر (0.125 = 12.5%)
        string? passed = Str(r, "passed");
        return new ClasseraProgress(set, pct <= 1 ? pct * 100 : pct, passed is "1" or "true");
    }

    public static List<ClasseraEnrolled> ParseEnrolled(string json)
    {
        var list = new List<ClasseraEnrolled>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var d) || !d.TryGetProperty("courses", out var cs) || cs.ValueKind != JsonValueKind.Array) return list;
        foreach (var c in cs.EnumerateArray())
        {
            if (!c.TryGetProperty("SvaCourse", out var sc)) continue;
            string? courseId = Str(sc, "id");
            string? lecture = c.TryGetProperty("SvaLecture", out var sl) ? Str(sl, "id") : null;
            if (courseId == null || lecture == null) continue;
            DateTime? to = Str(sc, "date_to") is { } s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt) ? dt : null;
            list.Add(new ClasseraEnrolled(lecture, courseId, PickTitle(Str(sc, "title_ar"), Str(sc, "title_en"), "دورة"), to));
        }
        return list;
    }

    public static ClasseraAssignmentInfo? ParseAssignmentInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("assignment_info", out var a) || Str(a, "id") is not { } id) return null;
        string? grade = a.TryGetProperty("last_submission_grade", out var g) && g.ValueKind is JsonValueKind.String or JsonValueKind.Number ? (g.ValueKind == JsonValueKind.String ? g.GetString() : g.GetRawText()) : null;
        return new ClasseraAssignmentInfo(id, (int)Num(a, "student_submissions"), (int)Num(a, "multi_submissions"), string.IsNullOrWhiteSpace(grade) ? null : grade,
            Str(a, "maxMark"), Bool(a, "out_of_attempts"), Str(a, "type"));
    }

    /// <summary>وصف حالة اختبار: «قدّمته (1 من 3) · الدرجة 8 من 10».</summary>
    public static string AssignmentText(ClasseraAssignmentInfo i)
    {
        bool survey = string.Equals(i.Type, "Assessment", StringComparison.OrdinalIgnoreCase);
        if (i.Submissions <= 0) return survey ? "استبيان — لم تُجب عنه بعد" : "اختبار — لم تقدّمه بعد" + (i.MaxSubmissions > 0 ? $" (المحاولات: {i.MaxSubmissions})" : "");
        string s = survey ? "✓ أجبت عن الاستبيان" : "✓ قدّمته" + (i.MaxSubmissions > 1 ? $" ({i.Submissions} من {i.MaxSubmissions})" : "");
        if (!survey && i.LastGrade != null) s += " · الدرجة " + i.LastGrade + (i.MaxMark != null ? " من " + i.MaxMark : "");
        if (i.OutOfAttempts) s += " · انتهت المحاولات";
        return s;
    }

    public static string KindText(ClasseraItem i) => i.IsAssignment
        ? (string.Equals(i.Type, "Assessment", StringComparison.OrdinalIgnoreCase) ? "استبيان" : "اختبار")
        : i.Type switch { "Youtube" => "فيديو يوتيوب", "Video" or "video" => "فيديو", "Vimeo" => "فيديو", "Document" => "ملف", "website" or "Website" => "موقع", _ => "مرفق" };

    private static string Icon(ClasseraItem i) => i.IsAssignment
        ? (string.Equals(i.Type, "Assessment", StringComparison.OrdinalIgnoreCase) ? "📋 " : "📝 ")
        : i.IsVideo ? "" : i.Type switch { "Document" => "📄 ", "website" or "Website" => "🌐 ", _ => "📎 " };

    // ======================= الدمج =======================

    public sealed record SyncResult(int AddedItems, int AddedSections, int MarkedDone, int Total, int Done);

    /// <summary>
    /// الدروس محاضرات، والعناصر (فيديو، ملف، موقع، اختبار، استبيان) بالترتيب. الموجود يبقى (ملاحظاتك ومكانك)،
    /// والجديد يُضاف في مكانه بعلامة «جديد»، والمكتمل على المنصة يُعلَّم «أُنجز».
    /// </summary>
    public static SyncResult Apply(Course course, ClasseraCourse src, ClasseraProgress? progress, string courseUrl, bool firstImport)
    {
        course.SourceUrl = courseUrl;
        course.TouchInfo();
        var existing = new Dictionary<string, VideoItem>();
        foreach (var v in course.AllVideos)
            if (v.SourceRef != null) existing.TryAdd(v.SourceRef, v);

        int added = 0, sections = 0, marked = 0;
        foreach (var lesson in src.Lessons)
        {
            if (lesson.Items.Count == 0) continue;
            string lref = LessonRef(lesson.Id);
            var group = course.Groups.FirstOrDefault(g => g.SourceUrl == lref);
            if (group == null)
            {
                group = new LectureGroup { Name = lesson.Title, SourceUrl = lref, InfoUpdatedUtc = DateTime.UtcNow };
                course.Groups.Add(group);
                sections++;
            }
            for (int k = 0; k < lesson.Items.Count; k++)
            {
                var item = lesson.Items[k];
                string iref = ItemRef(item);
                string url = ItemUrl(item, src.CourseId, src.LectureId);
                if (existing.TryGetValue(iref, out var v))
                {
                    if (v.Path != url) { v.Path = url; v.Touch(); }
                }
                else
                {
                    v = new VideoItem
                    {
                        Path = url, Title = Icon(item) + item.Title, SourceRef = iref, IsNew = !firstImport, UpdatedUtc = DateTime.UtcNow,
                        PlatformInfo = item.IsVideo ? null : KindText(item) + " — يُفتح في صفحة المنصة"
                    };
                    int at = 0;
                    for (int p = k - 1; p >= 0; p--)
                    {
                        int pos = group.Videos.FindIndex(x => x.SourceRef == ItemRef(lesson.Items[p]));
                        if (pos >= 0) { at = pos + 1; break; }
                    }
                    if (k > 0 && at == 0) at = group.Videos.Count;
                    group.Videos.Insert(Math.Min(at, group.Videos.Count), v);
                    existing[iref] = v;
                    if (!firstImport) added++;
                }
                if (progress != null && progress.Completed.Contains(item.Id) && !v.Watched) { v.Watched = true; v.Touch(); marked++; }
            }
        }
        var all = course.AllVideos.Where(v => v.SourceRef?.StartsWith("cls-", StringComparison.Ordinal) == true).ToList();
        return new SyncResult(added, firstImport ? 0 : sections, marked, all.Count, all.Count(v => v.Watched));
    }

    // ======================= أدوات =======================

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null } : null;

    private static double Num(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
        (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0) || (v.ValueKind == JsonValueKind.String && v.GetString() is "1" or "true"));
}
