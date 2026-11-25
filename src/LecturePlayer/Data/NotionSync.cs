using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LecturePlayer.Data;

/// <summary>
/// حالة المزامنة مع نوشن على الجهاز ده (مش في ملف المكتبة): آخر قيم اتبعتت لكل صف/مربع عشان مانبعتش نفس الحاجة تاني،
/// وأعلى حالة اتبعتت لكل محاضرة (الحالة بتتحرك لقدّام بس)، والصفوف اللي اتعرفت لكل موضوع (عشان نعلّم اللي اتشال).
/// </summary>
public sealed class NotionState
{
    public Dictionary<string, string> Hash { get; set; } = new();
    public Dictionary<string, int> Rank { get; set; } = new();
    public Dictionary<string, bool> Checked { get; set; } = new();
    public Dictionary<string, string> BlockText { get; set; } = new();
    /// <summary>صف نوشن ← (الموضوع، نوع، رقم في مسار).</summary>
    public Dictionary<string, string> Known { get; set; } = new();
    /// <summary>صفوف اتعلّمت «اتشال من مسار» أو اتمسحت من نوشن نفسه (مش بنرجّعها لوحدنا).</summary>
    public HashSet<string> Detached { get; set; } = new();
    /// <summary>محاضرات خطتها اتغيّرت هنا ولسه ماتبعتتش (النت كان فاصل مثلًا).</summary>
    public HashSet<string> PendingPlan { get; set; } = new();
    /// <summary>سجل المذاكرة: موضوع|يوم ← صف.</summary>
    public Dictionary<string, string> LogRows { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    public static string DefaultPath => Path.Combine(Storage.AppDataDir, "notion-state.json");

    public static NotionState Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (File.Exists(path)) return JsonSerializer.Deserialize<NotionState>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception ex) { Log.Info("notion state: " + ex.Message); }
        return new NotionState();
    }

    public void Save(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, path, true);
        }
        catch (Exception ex) { Log.Info("notion state save: " + ex.Message); }
    }
}

/// <summary>
/// مسار ↔ نوشن لموضوع: بيبني صفحة الموضوع وقاعدتين (الكورسات، والمحاضرات مربوطة بالكورسات)، والفيديوهات مربعات ☐
/// جوه صفحة كل محاضرة، وبيحدّث التقدّم لوحده، ويضيف الجديد، ويسحب الخطة (الموعد، الحضور، المهام) من نوشن.
/// مش بيمسح أي صف في نوشن أبدًا: اللي اتشال من مسار بيتعلّم «اتشال من مسار».
/// </summary>
public sealed class NotionSync
{
    // أسماء الأعمدة (لو اتغيرت في نوشن، المزامنة بتقول إن العمود مش موجود)
    public const string CTitle = "الكورس", COrder = "ترتيب", CSource = "المصدر", CSubSource = "المصدر الفرعي", CTeacher = "المدرّس", CStatus = "الحالة",
        CProgress = "التقدّم", CLectures = "المحاضرات", CWatched = "اتشاف", CHours = "المدة (ساعة)", CStudy = "وقت المذاكرة (ساعة)",
        CLast = "آخر مذاكرة", CDeadline = "موعد الانتهاء", CLink = "الرابط";
    public const string LTitle = "المحاضرة", LOrder = "#", LStatus = "الحالة", LProgress = "التقدّم", LVideos = "اتشاف",
        LMinutes = "المدة (دقيقة)", LDate = "الموعد", LAttendance = "الحضور", LTask = "TASK", LSearch = "SEARCH", LSummary = "SUMMARY", LLast = "آخر مشاهدة";
    public const string NoSource = "بدون مصدر";
    public const int CurrentLayout = 2;
    public const string GTitle = "اليوم", GDate = "التاريخ", GMinutes = "الدقايق", GCourses = "الكورسات";

    public const string NotStarted = "لم يبدأ", Started = "جاري", Done = "خلص", Paused = "متوقف", Planned = "مخطط", Removed = "اتشال من مسار";
    public const string Present = "حضرت", Absent = "غبت";

    private readonly NotionClient _api;
    private readonly CourseLibrary _lib;
    private readonly NotionState _state;

    /// <summary>وقت المذاكرة الفعلي للكورس (ثواني) وآخر يوم ذاكرته فيه — من سجل المذاكرة.</summary>
    public Func<Course, (double Seconds, DateTime? LastUtc)>? StudyOf { get; set; }
    /// <summary>دقايق المذاكرة لكل يوم في الموضوع (لسجل المذاكرة): (اليوم، الدقايق، الكورسات).</summary>
    public Func<Topic, IEnumerable<(DateOnly Day, double Minutes, IReadOnlyCollection<Course> Courses)>>? DailyOf { get; set; }
    /// <summary>بيتنده بنص التقدّم (للشريط).</summary>
    public Action<string>? Progress { get; set; }
    /// <summary>كل اللي اتغيّر في نوشن في المرة دي (للسجل).</summary>
    public List<string> Changes { get; } = new();
    /// <summary>المكتبة اتغيرت (أرقام صفوف جديدة) — لازم تتحفظ.</summary>
    public bool LibraryChanged { get; private set; }

    public NotionSync(NotionClient api, CourseLibrary lib, NotionState state)
    {
        _api = api;
        _lib = lib;
        _state = state;
    }

    // =====================================================================
    //  الشكل
    // =====================================================================

    private static JsonObject Options(params (string Name, string Color)[] o)
    {
        var arr = new JsonArray();
        foreach (var (n, c) in o) arr.Add(new JsonObject { ["name"] = n, ["color"] = c });
        return new JsonObject { ["options"] = arr };
    }

    private static JsonObject StatusOptions() => Options((NotStarted, "gray"), (Planned, "purple"), (Started, "blue"), (Paused, "orange"), (Done, "green"), (Removed, "red"));
    private static JsonObject Num(string format = "number") => new() { ["number"] = new JsonObject { ["format"] = format } };
    private static JsonObject Empty(string type) => new() { [type] = new JsonObject() };

    public static JsonObject CoursesSchema() => new()
    {
        [CTitle] = Empty("title"),
        [COrder] = Num(),
        [CSource] = new JsonObject { ["select"] = Options() },
        [CSubSource] = new JsonObject { ["select"] = Options() },
        [CStatus] = new JsonObject { ["select"] = StatusOptions() },
        [CProgress] = Num("percent"),
        [CWatched] = Empty("rich_text"),
        [CLectures] = Num(),
        [CHours] = Num(),
        [CStudy] = Num(),
        [CLast] = Empty("date"),
        [CDeadline] = Empty("date"),
        [CTeacher] = Empty("rich_text"),
        [CLink] = Empty("url"),
    };

    /// <summary>محاضرات كورس واحد (قاعدة جوه صفحة الكورس، زي LEC في KAIZEN).</summary>
    public static JsonObject LecturesSchema() => new()
    {
        [LTitle] = Empty("title"),
        [LOrder] = Num(),
        [LStatus] = new JsonObject { ["select"] = StatusOptions() },
        [LProgress] = Num("percent"),
        [LVideos] = Empty("rich_text"),
        [LMinutes] = Num(),
        [LDate] = Empty("date"),
        [LAttendance] = new JsonObject { ["select"] = Options((Present, "green"), (Absent, "red")) },
        [LTask] = Empty("checkbox"),
        [LSearch] = Empty("checkbox"),
        [LSummary] = Empty("checkbox"),
        [LLast] = Empty("date"),
    };

    public static JsonObject LogSchema(string coursesDsId) => new()
    {
        [GTitle] = Empty("title"),
        [GDate] = Empty("date"),
        [GMinutes] = Num(),
        [GCourses] = new JsonObject { ["relation"] = new JsonObject { ["data_source_id"] = coursesDsId, ["type"] = "single_property", ["single_property"] = new JsonObject() } },
    };

    // =====================================================================
    //  الإنشاء
    // =====================================================================

    /// <summary>صفحة الموضوع + قاعدة الكورسات (مقسومة بالمصدر وبترتيب مسار)، وجوه كل كورس قاعدة محاضراته، وبعدين يملاهم.</summary>
    public async Task CreateTopicAsync(Topic t, string parentPageId, CancellationToken ct)
    {
        if (t.Notion != null) throw new InvalidOperationException("الموضوع مربوط بنوشن بالفعل");
        Progress?.Invoke("جارٍ إنشاء صفحة الموضوع…");
        string page = await _api.CreatePageAsync(
            new JsonObject { ["type"] = "page_id", ["page_id"] = parentPageId },
            new JsonObject { ["title"] = NotionClient.TitleProp(t.Name) }, "📚",
            new JsonArray(NotionClient.Callout("أُنشئت من «مسار» — بنفس ترتيب المكتبة، ويتحدّث التقدّم والجديد تلقائيًا.", "🎓",
                new JsonArray(
                    NotionClient.Bullet("الدورات مقسّمة حسب المصدر. اضغط على أي دورة لتجد جدول محاضراتها داخل صفحتها."),
                    NotionClient.Bullet("الموعد والحضور وTASK وSEARCH وSUMMARY الخاصة بك: عدّلها هنا أو من مسار."),
                    NotionClient.Bullet("لا تغيّر أسماء الأعمدة (المزامنة تبحث عنها بالاسم). مسار لا يحذف شيئًا هنا.")))), ct);
        Progress?.Invoke("جارٍ إنشاء قاعدة الدورات…");
        var (cdb, cds) = await _api.CreateDatabaseAsync(page, "الكورسات", "🎓", CoursesSchema(), ct);
        await ConfigureViewAsync(cdb, cds, COrder, CSource, ct);
        t.Notion = new NotionTopicLink { Layout = CurrentLayout, PageId = page, CoursesDbId = cdb, CoursesDsId = cds, CreatedUtc = DateTime.UtcNow };
        _state.Hash["topic:" + page] = Hash(t.Name);
        LibraryChanged = true;
        Changes.Add($"أُنشئت صفحة «{t.Name}» وقاعدة الدورات");
        // أرقام قديمة لكورسات الموضوع من ربط قديم اتفك ماتنفعش (اللي في موضوع تاني مربوط بنسيبها عشان صفها القديم يتعلّم)
        var linked = _lib.Topics.Where(x => x.Notion != null && x != t).Select(x => x.Notion!.CoursesDsId).ToHashSet();
        foreach (var c in _lib.CoursesOf(t))
            if (c.NotionDsId == null || !linked.Contains(c.NotionDsId)) ResetCourse(c);
        await SyncTopicAsync(t, ct);
    }

    /// <summary>
    /// «أعد البناء»: الصفحة القديمة (اللي مسار عملها) بتروح سلة نوشن (بترجع منها لحد 30 يوم)، وبيتعمل الموضوع من جديد.
    /// الخطة (الموعد والحضور والمهام) محفوظة في مسار فبتتنقل للصفحة الجديدة.
    /// </summary>
    public async Task RebuildTopicAsync(Topic t, string parentPageId, CancellationToken ct)
    {
        if (t.Notion is { } old)
        {
            try { await PullPlanAsync(t, ct); } catch (NotionException ex) when (IsGone(ex)) { }
            Progress?.Invoke("جارٍ نقل الصفحة القديمة إلى السلة…");
            try
            {
                await _api.PatchAsync("pages/" + old.PageId, new JsonObject { ["in_trash"] = true }, ct);
                Changes.Add($"نُقلت صفحة «{t.Name}» القديمة إلى سلة نوشن");
            }
            catch (NotionException ex) when (IsGone(ex)) { }
            foreach (var k in _state.Known.Where(kv => kv.Value.StartsWith(t.Id + "|", StringComparison.Ordinal)).Select(kv => kv.Key).ToList())
                _state.Known.Remove(k);
            t.Notion = null;
            foreach (var c in _lib.CoursesOf(t)) ResetCourse(c);
            LibraryChanged = true;
        }
        await CreateTopicAsync(t, parentPageId, ct);
    }

    private static void ResetCourse(Course c)
    {
        c.NotionPageId = null; c.NotionDsId = null; c.NotionLecturesDbId = null; c.NotionLecturesDsId = null;
        foreach (var g in c.Groups) ResetLecture(g);
    }

    /// <summary>
    /// شكل الجدول في نوشن: الترتيب (بعمود) والتقسيم (بعمود select) — من الـ Views API.
    /// لو نوشن رفض (خطة قديمة أو API اتغير) بنكمّل عادي: الجدول بيشتغل، بس من غير الترتيب.
    /// </summary>
    private async Task ConfigureViewAsync(string dbId, string dsId, string sortBy, string? groupBy, CancellationToken ct)
    {
        try
        {
            var views = await _api.GetAsync("views?database_id=" + dbId, ct);
            string? viewId = views["results"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>();
            if (viewId == null) return;
            var body = new JsonObject { ["sorts"] = new JsonArray(new JsonObject { ["property"] = sortBy, ["direction"] = "ascending" }) };
            if (groupBy != null)
            {
                var ds = await _api.GetAsync("data_sources/" + dsId, ct);
                string? pid = ds["properties"]?[groupBy]?["id"]?.GetValue<string>();
                if (pid != null)
                    body["configuration"] = new JsonObject
                    {
                        ["type"] = "table",
                        ["group_by"] = new JsonObject { ["type"] = "select", ["property_id"] = pid, ["sort"] = new JsonObject { ["type"] = "manual" }, ["hide_empty_groups"] = true }
                    };
            }
            await _api.PatchAsync("views/" + viewId, body, ct);
        }
        catch (NotionException ex)
        {
            Log.Info($"notion view: {ex.Status} {ex.Message}");
            Changes.Add("لم يُضبط ترتيب العرض في نوشن تلقائيًا (رتّب الجدول بعمود «" + sortBy + "» من نوشن)");
        }
    }

    /// <summary>سجل المذاكرة اليومي للموضوع (قاعدة تالتة).</summary>
    public async Task CreateLogAsync(Topic t, CancellationToken ct)
    {
        var link = t.Notion ?? throw new InvalidOperationException("الموضوع غير مربوط بنوشن");
        if (link.LogDsId != null) return;
        var (db, ds) = await _api.CreateDatabaseAsync(link.PageId, "سجل المذاكرة", "🗓️", LogSchema(link.CoursesDsId), ct);
        link.LogDbId = db;
        link.LogDsId = ds;
        LibraryChanged = true;
        Changes.Add($"أُنشئ «سجل المذاكرة» لموضوع «{t.Name}»");
        await SyncLogAsync(t, ct);
    }

    // =====================================================================
    //  التحديث (مسار ← نوشن)
    // =====================================================================

    /// <summary>كل كورسات الموضوع ومحاضراته وفيديوهاته: الجديد يتعمل، واللي اتغيّر يتحدّث، واللي اتشال يتعلّم.</summary>
    public async Task SyncTopicAsync(Topic t, CancellationToken ct, bool recreateMissing = false)
    {
        var link = t.Notion ?? throw new InvalidOperationException("الموضوع غير مربوط بنوشن");
        if (recreateMissing) _state.Detached.Clear();

        // اسم الموضوع
        string th = Hash(t.Name);
        if (!_state.Hash.TryGetValue("topic:" + link.PageId, out var oldTh) || oldTh != th)
        {
            await _api.UpdatePageAsync(link.PageId, new JsonObject { ["title"] = NotionClient.TitleProp(t.Name) }, ct);
            _state.Hash["topic:" + link.PageId] = th;
            Changes.Add($"أصبح اسم الموضوع «{t.Name}»");
        }

        if (link.Layout < CurrentLayout) throw new InvalidOperationException(OldLayoutMessage(t));
        var ordered = TreeOrder(t);
        var courses = ordered.Select(x => x.Course).ToList();
        int done = 0, total = courses.Sum(c => c.Groups.Count) + courses.Count;
        var alive = new HashSet<string>();
        for (int ci = 0; ci < ordered.Count; ci++)
        {
            ct.ThrowIfCancellationRequested();
            var (c, top, sub) = ordered[ci];
            await SyncCourseAsync(t, link, c, ci + 1, top, sub, ct);
            if (c.NotionPageId != null) alive.Add(c.NotionPageId);
            Progress?.Invoke($"«{c.Name}» ({++done}/{total})");
            var groups = c.Groups.ToList(); // المكتبة ممكن تتغير وإحنا مستنيين نوشن
            for (int i = 0; i < groups.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var g = groups[i];
                await SyncLectureAsync(t, link, c, g, i + 1, ct);
                if (g.NotionPageId != null) alive.Add(g.NotionPageId);
                Progress?.Invoke($"«{c.Name}» ({++done}/{total})");
            }
        }

        // اللي اتشال من الموضوع (اتمسح أو اتنقل لموضوع تاني): «اتشال من مسار» مرة واحدة
        foreach (var kv in _state.Known.Where(k => k.Value.StartsWith(t.Id + "|", StringComparison.Ordinal)).ToList())
        {
            if (alive.Contains(kv.Key) || _state.Detached.Contains(kv.Key)) continue;
            await MarkRemovedAsync(kv.Key, ct);
        }
        if (link.LogDsId != null) await SyncLogAsync(t, ct);
    }

    private async Task MarkRemovedAsync(string pageId, CancellationToken ct)
    {
        try
        {
            await _api.UpdatePageAsync(pageId, new JsonObject { [CStatus] = NotionClient.SelectProp(Removed) }, ct);
            Changes.Add("وُسم صف بـ «اتشال من مسار»");
        }
        catch (NotionException ex) when (IsGone(ex)) { }
        _state.Detached.Add(pageId);
        _state.Known.Remove(pageId);
    }

    private static bool IsGone(NotionException ex) =>
        ex.Status == 404 || (ex.Status == 400 && (ex.Message.Contains("archived", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("trash", StringComparison.OrdinalIgnoreCase)));

    public static string OldLayoutMessage(Topic t) => $"قاعدة «{t.Name}» في نوشن بالشكل القديم — أعد بناءها بالشكل الجديد (📤 نوشن ← 🧱 أعد البناء).";

    /// <summary>كورسات الموضوع بنفس ترتيب شجرة مسار: كل مصدر (مصادره الفرعية الأول وبعدين كورساته)، وبعدها الكورسات اللي تحت الموضوع على طول.</summary>
    public List<(Course Course, Place? Top, Place? Sub)> TreeOrder(Topic t)
    {
        var list = new List<(Course, Place?, Place?)>();
        foreach (var top in _lib.TopPlacesOf(t))
        {
            foreach (var sub in _lib.ChildrenOf(top))
                foreach (var c in _lib.CoursesInPlace(sub)) list.Add((c, top, sub));
            foreach (var c in _lib.CoursesInPlace(top)) list.Add((c, top, null));
        }
        foreach (var c in _lib.DirectCoursesOf(t)) list.Add((c, null, null));
        foreach (var c in _lib.CoursesOf(t)) if (!list.Any(x => x.Item1 == c)) list.Add((c, _lib.PlaceOf(c), null));
        return list;
    }

    private async Task SyncCourseAsync(Topic t, NotionTopicLink link, Course c, int order, Place? top, Place? sub, CancellationToken ct)
    {
        if (c.NotionPageId != null && c.NotionDsId != link.CoursesDsId)
        {
            // الكورس اتنقل من موضوع تاني مربوط: صفه القديم يتعلّم، ويتعمل صف هنا بمحاضراته
            await MarkRemovedAsync(c.NotionPageId, ct);
            ResetCourse(c);
            LibraryChanged = true;
        }
        var props = CourseProps(c, order, top, sub);
        string h = Hash(props.ToJsonString());
        if (c.NotionPageId == null)
        {
            string id = await _api.CreatePageAsync(new JsonObject { ["type"] = "data_source_id", ["data_source_id"] = link.CoursesDsId }, props, "🎓",
                string.IsNullOrWhiteSpace(c.Goal) ? null : new JsonArray(NotionClient.Paragraph("🎯 " + c.Goal)), ct);
            c.NotionPageId = id;
            c.NotionDsId = link.CoursesDsId;
            LibraryChanged = true;
            _state.Hash[id] = h;
            _state.Known[id] = $"{t.Id}|c|{c.Id}";
            Changes.Add($"دورة جديدة في نوشن: «{c.Name}»");
        }
        else
        {
            _state.Known[c.NotionPageId] = $"{t.Id}|c|{c.Id}";
            if (_state.Detached.Contains(c.NotionPageId)) return;
            if (!_state.Hash.TryGetValue(c.NotionPageId, out var old) || old != h)
            {
                try
                {
                    await _api.UpdatePageAsync(c.NotionPageId, props, ct);
                    _state.Hash[c.NotionPageId] = h;
                    Changes.Add($"حُدِّثت «{c.Name}»");
                }
                catch (NotionException ex) when (IsGone(ex))
                {
                    _state.Detached.Add(c.NotionPageId);
                    Changes.Add($"حُذف صف «{c.Name}» من نوشن — لن يعود تلقائيًا (🔄 أعد المزامنة من البداية إن أردته)");
                    return;
                }
            }
        }
        // جدول محاضرات الكورس جوه صفحته
        if (c.NotionLecturesDsId == null)
        {
            var (ldb, lds) = await _api.CreateDatabaseAsync(c.NotionPageId!, "المحاضرات", "📖", LecturesSchema(), ct);
            await ConfigureViewAsync(ldb, lds, LOrder, null, ct);
            c.NotionLecturesDbId = ldb;
            c.NotionLecturesDsId = lds;
            LibraryChanged = true;
        }
    }

    private async Task SyncLectureAsync(Topic t, NotionTopicLink link, Course c, LectureGroup g, int order, CancellationToken ct)
    {
        if (c.NotionPageId == null || c.NotionLecturesDsId == null || _state.Detached.Contains(c.NotionPageId)) return;
        string lds = c.NotionLecturesDsId;
        if (g.NotionPageId != null && g.NotionDsId != lds)
        {
            await MarkRemovedAsync(g.NotionPageId, ct);
            ResetLecture(g);
        }
        var props = LectureProps(g, order);
        int rank = Rank(g);
        if (g.NotionPageId == null)
        {
            // الخطة لو جاية من موضوع تاني (أو اتكتبت هنا قبل الربط) بتتبعت مع الصف
            if (g.Plan != null) foreach (var kv in PlanProps(g.Plan)) props[kv.Key] = kv.Value!.DeepClone();
            string id = await _api.CreatePageAsync(new JsonObject { ["type"] = "data_source_id", ["data_source_id"] = lds }, props, null, null, ct);
            g.NotionPageId = id;
            g.NotionDsId = lds;
            LibraryChanged = true;
            _state.Hash[id] = Hash(LectureProps(g, order).ToJsonString());
            _state.Rank[id] = rank;
            _state.Known[id] = $"{t.Id}|l|{g.Id}";
            _state.PendingPlan.Remove(g.Id);
            foreach (var v in g.Videos) v.NotionBlockId = null;
            await SyncVideosAsync(g, ct);
            Changes.Add($"محاضرة جديدة في نوشن: «{g.Name}»");
            return;
        }
        _state.Known[g.NotionPageId] = $"{t.Id}|l|{g.Id}";
        if (_state.Detached.Contains(g.NotionPageId)) return;
        try
        {
            // الحالة بتتحرك لقدّام بس: لو إنت علّمتها «خلص» في نوشن، مسار مايرجّعهاش
            if (_state.Rank.TryGetValue(g.NotionPageId, out int pushed) && rank < pushed) props.Remove(LStatus);
            string h = Hash(props.ToJsonString());
            if (!_state.Hash.TryGetValue(g.NotionPageId, out var old) || old != h)
            {
                await _api.UpdatePageAsync(g.NotionPageId, props, ct);
                _state.Hash[g.NotionPageId] = h;
                if (props.ContainsKey(LStatus)) _state.Rank[g.NotionPageId] = Math.Max(rank, _state.Rank.GetValueOrDefault(g.NotionPageId));
                Changes.Add($"حُدِّثت «{g.Name}»");
            }
            if (_state.PendingPlan.Contains(g.Id) && g.Plan != null)
            {
                await _api.UpdatePageAsync(g.NotionPageId, PlanProps(g.Plan), ct);
                _state.PendingPlan.Remove(g.Id);
            }
            await SyncVideosAsync(g, ct);
        }
        catch (NotionException ex) when (IsGone(ex))
        {
            _state.Detached.Add(g.NotionPageId);
            Changes.Add($"حُذف صف «{g.Name}» من نوشن — لن يعود تلقائيًا");
        }
    }

    private static void ResetLecture(LectureGroup g)
    {
        g.NotionPageId = null;
        g.NotionDsId = null;
        g.NotionNotesBlockId = null;
        foreach (var v in g.Videos) v.NotionBlockId = null;
    }

    /// <summary>الفيديوهات مربعات ☐ جوه صفحة المحاضرة: الجديد يتضاف بعد آخر واحد، واللي اتشاف يتعلّم، والاسم لو اتغيّر.</summary>
    private async Task SyncVideosAsync(LectureGroup g, CancellationToken ct)
    {
        string page = g.NotionPageId!;
        var videos = g.Videos.ToList();
        var fresh = videos.Where(v => v.NotionBlockId == null).ToList();
        if (fresh.Count > 0)
        {
            var blocks = new List<JsonObject>();
            bool first = videos.All(v => v.NotionBlockId == null);
            if (first) blocks.Add(NotionClient.Heading("🎬 الفيديوهات", 3));
            blocks.AddRange(fresh.Select(v => NotionClient.ToDo(VideoText(v), v.Watched)));
            string? after = first ? null : videos.LastOrDefault(v => v.NotionBlockId != null)?.NotionBlockId;
            var ids = await AppendAfterAsync(page, blocks, after, ct);
            int skip = first ? 1 : 0;
            for (int i = 0; i < fresh.Count && i + skip < ids.Count; i++)
            {
                fresh[i].NotionBlockId = ids[i + skip];
                _state.Checked[ids[i + skip]] = fresh[i].Watched;
                _state.BlockText[ids[i + skip]] = VideoText(fresh[i]);
            }
            LibraryChanged = true;
        }
        foreach (var v in videos)
        {
            if (v.NotionBlockId == null || fresh.Contains(v)) continue;
            string text = VideoText(v);
            bool checkedChanged = !_state.Checked.TryGetValue(v.NotionBlockId, out bool was) || was != v.Watched;
            bool textChanged = _state.BlockText.TryGetValue(v.NotionBlockId, out var oldText) && oldText != text;
            if (!checkedChanged && !textChanged) continue;
            var todo = new JsonObject { ["checked"] = v.Watched };
            if (textChanged) todo["rich_text"] = NotionClient.Rich(text);
            try
            {
                await _api.UpdateBlockAsync(v.NotionBlockId, new JsonObject { ["to_do"] = todo }, ct);
            }
            catch (NotionException ex) when (IsGone(ex))
            {
                // المربع اتمسح من الصفحة: نسيبه (مانرجّعهوش في نص كلامك)
                _state.Detached.Add(v.NotionBlockId);
            }
            _state.Checked[v.NotionBlockId] = v.Watched;
            _state.BlockText[v.NotionBlockId] = text;
        }
    }

    /// <summary>بيضيف بلوكات بعد بلوك معيّن (أو في الآخر) ← أرقامها بالترتيب.</summary>
    private async Task<List<string>> AppendAfterAsync(string parent, List<JsonObject> blocks, string? afterId, CancellationToken ct)
    {
        var ids = new List<string>();
        for (int i = 0; i < blocks.Count; i += 100)
        {
            var chunk = new JsonArray();
            foreach (var b in blocks.Skip(i).Take(100)) chunk.Add(b.DeepClone());
            var body = new JsonObject { ["children"] = chunk };
            string? after = ids.Count > 0 ? ids[^1] : afterId;
            if (after != null) body["position"] = new JsonObject { ["type"] = "after_block", ["after_block"] = new JsonObject { ["id"] = after } };
            var r = await _api.PatchAsync($"blocks/{parent}/children", body, ct);
            foreach (var x in r["results"]?.AsArray() ?? new JsonArray()) ids.Add(x?["id"]?.GetValue<string>() ?? "");
        }
        return ids;
    }

    private static string VideoText(VideoItem v) =>
        v.Title + (v.DurationMs > 0 ? $"  ({TimeFormat.Format(v.DurationMs)})" : "");

    // ---------------- القيم ----------------

    /// <summary>
    /// التقدّم: اللي اتشاف كامل + مكانك في اللي بدأته (بالمدة، أو بالعدد لو المدد مش معروفة).
    /// مايوصلش 100% غير لما كله يتعلّم «اتشاف» — عشان النسبة والحالة يقولوا نفس الحاجة.
    /// </summary>
    public static double Fraction(IEnumerable<VideoItem> videos)
    {
        var list = videos.ToList();
        if (list.Count == 0) return 0;
        if (list.All(v => v.Watched)) return 1;
        double f;
        if (list.Any(v => v.DurationMs <= 0))
            f = list.Sum(v => v.Watched ? 1.0 : v.DurationMs > 0 ? Math.Clamp((double)v.LastPositionMs / v.DurationMs, 0, 0.95) : 0) / list.Count;
        else
        {
            long total = list.Sum(v => v.DurationMs);
            long seen = list.Sum(v => v.Watched ? v.DurationMs : Math.Clamp(v.LastPositionMs, 0, (long)(v.DurationMs * 0.95)));
            f = (double)seen / total;
        }
        return Math.Min(Math.Floor(f * 100) / 100, 0.99);
    }

    private static bool AnyStarted(IEnumerable<VideoItem> videos) => videos.Any(v => v.Watched || v.LastPositionMs > 0);

    public static string LectureState(LectureGroup g) => g.IsCompleted ? Done : AnyStarted(g.Videos) ? Started : NotStarted;
    private static int Rank(LectureGroup g) => g.IsCompleted ? 2 : AnyStarted(g.Videos) ? 1 : 0;

    public static string CourseState(Course c) =>
        c.IsCompleted || c.Status == LecturePlayer.Data.CourseStatus.Done ? Done
        : c.Status == LecturePlayer.Data.CourseStatus.Paused ? Paused
        : AnyStarted(c.AllVideos) ? Started
        : c.Status == LecturePlayer.Data.CourseStatus.Planned ? Planned
        : NotStarted;

    private static string SelectSafe(string s) => s.Replace(",", "،").Trim() is { Length: > 0 } x ? (x.Length > 90 ? x[..90] : x) : "—";

    private JsonObject CourseProps(Course c, int order, Place? top, Place? sub)
    {
        var study = StudyOf?.Invoke(c) ?? (0, null);
        return new JsonObject
        {
            [CTitle] = NotionClient.TitleProp(c.Name),
            [COrder] = NotionClient.NumberProp(order),
            [CSource] = NotionClient.SelectProp(top != null ? SelectSafe(top.Name) : NoSource),
            [CSubSource] = NotionClient.SelectProp(sub != null ? SelectSafe(sub.Name) : null),
            [CStatus] = NotionClient.SelectProp(CourseState(c)),
            [CProgress] = NotionClient.NumberProp(Fraction(c.AllVideos)),
            [CWatched] = NotionClient.TextProp($"{c.WatchedCount} من {c.VideoCount}"),
            [CLectures] = NotionClient.NumberProp(c.Groups.Count),
            [CHours] = NotionClient.NumberProp(Math.Round(c.TotalDurationMs / 3_600_000.0, 1)),
            [CStudy] = NotionClient.NumberProp(Math.Round(study.Seconds / 3600.0, 1)),
            [CLast] = NotionClient.DateProp(study.LastUtc?.ToLocalTime().Date),
            [CDeadline] = NotionClient.DateProp(c.Deadline?.Date),
            [CTeacher] = NotionClient.TextProp(c.Instructor),
            [CLink] = NotionClient.UrlProp(LinkOf(c)),
        };
    }

    private static string? LinkOf(Course c)
    {
        if (!string.IsNullOrWhiteSpace(c.SourceUrl) && MediaFiles.IsUrl(c.SourceUrl)) return c.SourceUrl;
        if (c.UdemyId is { } id && c.AllVideos.FirstOrDefault(v => v.IsUdemy) is { } uv && Uri.TryCreate(uv.Path, UriKind.Absolute, out var u))
        {
            int learn = u.AbsolutePath.IndexOf("/learn/", StringComparison.Ordinal);
            if (learn > 0) return u.GetLeftPart(UriPartial.Authority) + u.AbsolutePath[..learn] + "/";
        }
        return null;
    }

    private static JsonObject LectureProps(LectureGroup g, int order)
    {
        DateTime? last = g.Videos.Where(v => v.Watched || v.LastPositionMs > 0).Select(v => (DateTime?)v.UpdatedUtc).DefaultIfEmpty(null).Max();
        return new JsonObject
        {
            [LTitle] = NotionClient.TitleProp(g.Name),
            [LOrder] = NotionClient.NumberProp(order),
            [LStatus] = NotionClient.SelectProp(LectureState(g)),
            [LProgress] = NotionClient.NumberProp(Fraction(g.Videos)),
            [LVideos] = NotionClient.TextProp($"{g.WatchedCount} من {g.Videos.Count}"),
            [LMinutes] = NotionClient.NumberProp(Math.Round(g.TotalDurationMs / 60_000.0)),
            [LLast] = NotionClient.DateProp(last is { Year: > 2000 } l ? l.ToLocalTime().Date : null),
        };
    }

    public static JsonObject PlanProps(LecturePlan p) => new()
    {
        [LDate] = NotionClient.DateProp(p.Date?.Date),
        [LAttendance] = NotionClient.SelectProp(p.Attendance),
        [LTask] = NotionClient.CheckProp(p.Task),
        [LSearch] = NotionClient.CheckProp(p.Search),
        [LSummary] = NotionClient.CheckProp(p.Summary),
    };

    private static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..16];

    // =====================================================================
    //  الخطة (نوشن ← مسار، ومسار ← نوشن)
    // =====================================================================

    /// <summary>بيسحب الموعد والحضور والمهام لكل محاضرات الموضوع ← عدد المحاضرات اللي خطتها اتغيرت.</summary>
    public async Task<int> PullPlanAsync(Topic t, CancellationToken ct)
    {
        var link = t.Notion ?? throw new InvalidOperationException("الموضوع غير مربوط بنوشن");
        var byPage = _lib.CoursesOf(t).SelectMany(c => c.Groups).Where(g => g.NotionPageId != null)
            .GroupBy(g => g.NotionPageId!).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var rows = new List<JsonObject>();
        if (link.Layout < CurrentLayout) rows.AddRange(await _api.QueryAllAsync(link.LecturesDsId, ct));
        else
            foreach (var c in _lib.CoursesOf(t).Where(c => c.NotionLecturesDsId != null && c.NotionPageId != null && !_state.Detached.Contains(c.NotionPageId)).ToList())
            {
                try { rows.AddRange(await _api.QueryAllAsync(c.NotionLecturesDsId!, ct)); }
                catch (NotionException ex) when (IsGone(ex)) { }
            }
        int changed = 0;
        foreach (var row in rows)
        {
            string id = row["id"]?.GetValue<string>() ?? "";
            if (!byPage.TryGetValue(id, out var g)) continue;
            if (row["in_trash"]?.GetValue<bool>() == true) continue;
            if (_state.PendingPlan.Contains(g.Id)) continue; // تعديلك هنا لسه ماتبعتش: هو الأحدث
            var plan = new LecturePlan
            {
                Date = NotionClient.ReadDate(row, LDate),
                Attendance = NotionClient.ReadSelect(row, LAttendance),
                Task = NotionClient.ReadCheck(row, LTask),
                Search = NotionClient.ReadCheck(row, LSearch),
                Summary = NotionClient.ReadCheck(row, LSummary),
                UpdatedUtc = NotionClient.ReadEdited(row) is { Year: > 2000 } e ? e : DateTime.UtcNow
            };
            if (g.Plan == null ? plan.IsEmpty : g.Plan.SameAs(plan)) continue;
            g.Plan = plan.IsEmpty ? null : plan;
            changed++;
            LibraryChanged = true;
            // الحالة اللي إنت كتبتها في نوشن: لو «خلص» بإيدك، مسار مايرجّعهاش
            if (NotionClient.ReadSelect(row, LStatus) is { } st)
                _state.Rank[id] = Math.Max(_state.Rank.GetValueOrDefault(id), st == Done ? 2 : st == Started ? 1 : 0);
        }
        link.PlanPulledUtc = DateTime.UtcNow;
        if (changed > 0) Changes.Add($"جُلبت خطة {changed} محاضرة من نوشن ({t.Name})");
        return changed;
    }

    /// <summary>تعديل الخطة من مسار (مربع، حضور، موعد) ← نوشن على طول. لو مافيش نت بيتبعت في المزامنة الجاية.</summary>
    public async Task PushPlanAsync(LectureGroup g, CancellationToken ct)
    {
        if (g.NotionPageId == null || g.Plan == null) return;
        _state.PendingPlan.Add(g.Id);
        await _api.UpdatePageAsync(g.NotionPageId, PlanProps(g.Plan), ct);
        _state.PendingPlan.Remove(g.Id);
        Changes.Add($"أُرسلت خطة «{g.Name}» إلى نوشن");
    }

    /// <summary>
    /// «وزّع المواعيد»: المحاضرات اللي مالهاش موعد (أو كلها) بتاخد مواعيد ورا بعض من تاريخ البداية في أيام المذاكرة،
    /// كل يوم بالدقايق دي (محاضرة أطول من اليوم بتاخد أكتر من يوم). المحاضرات اللي خلصت مابتاخدش موعد.
    /// ← آخر موعد.
    /// </summary>
    public static DateTime? Schedule(IReadOnlyList<LectureGroup> lectures, DateTime start, ICollection<DayOfWeek> days, int minutesPerDay, bool onlyEmpty)
    {
        if (minutesPerDay <= 0 || days.Count == 0) return null;
        DateTime day = start.Date;
        while (!days.Contains(day.DayOfWeek)) day = day.AddDays(1);
        double left = minutesPerDay;
        DateTime? last = null;
        foreach (var g in lectures)
        {
            if (g.IsCompleted) continue;
            if (onlyEmpty && g.Plan?.Date != null) continue;
            double need = Math.Max(5, g.Videos.Where(v => !v.Watched).Sum(v => Math.Max(0, v.DurationMs - v.LastPositionMs)) / 60_000.0);
            // لو مش هتكفي في اللي فاضل من اليوم (وفاضل أقل من النص): من بكرة
            if (need > left && left < minutesPerDay * 0.5) { day = Next(day, days); left = minutesPerDay; }
            g.Plan ??= new LecturePlan();
            g.Plan.Date = day;
            g.Plan.UpdatedUtc = DateTime.UtcNow;
            last = day;
            left -= need;
            while (left <= 0) { day = Next(day, days); left += minutesPerDay; }
        }
        return last;
    }

    private static DateTime Next(DateTime d, ICollection<DayOfWeek> days)
    {
        do d = d.AddDays(1); while (!days.Contains(d.DayOfWeek));
        return d;
    }

    // =====================================================================
    //  المحتوى (ملاحظات، علامات، ملخص، اختبار) ← صفحة المحاضرة
    // =====================================================================

    /// <summary>
    /// صندوق «📝 من مسار» جوه صفحة المحاضرة: لو اتبعت قبل كده، القديم بيتشال (بتاعنا إحنا) والجديد مكانه — من غير تكرار.
    /// hasSummary = فيه ملخص ← SUMMARY يتعلّم ✓.
    /// </summary>
    public async Task SendNotesAsync(LectureGroup g, IReadOnlyList<JsonObject> content, bool hasSummary, CancellationToken ct)
    {
        if (g.NotionPageId == null) throw new InvalidOperationException("لم يُنشأ صف لهذه المحاضرة في نوشن بعد — زامن الموضوع أولًا");
        if (g.NotionNotesBlockId != null) await _api.TrashBlockAsync(g.NotionNotesBlockId, ct);
        var first = new JsonArray();
        foreach (var b in content.Take(100)) first.Add(b.DeepClone());
        string stamp = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
        var ids = await _api.AppendAsync(g.NotionPageId, new[] { NotionClient.Callout($"📝 من مسار — {stamp}", "🎓", first) }, ct);
        g.NotionNotesBlockId = ids.FirstOrDefault();
        if (content.Count > 100 && g.NotionNotesBlockId != null)
            await _api.AppendAsync(g.NotionNotesBlockId, content.Skip(100).ToList(), ct);
        LibraryChanged = true;
        if (hasSummary)
        {
            g.Plan ??= new LecturePlan();
            if (!g.Plan.Summary)
            {
                g.Plan.Summary = true;
                g.Plan.UpdatedUtc = DateTime.UtcNow;
                await PushPlanAsync(g, ct);
            }
        }
        Changes.Add($"أُرسلت ملاحظات «{g.Name}» إلى نوشن");
    }

    // =====================================================================
    //  سجل المذاكرة
    // =====================================================================

    /// <summary>آخر 14 يوم: صف لكل يوم فيه مذاكرة (الدقايق والكورسات)، وبيتحدّث لو زاد.</summary>
    public async Task SyncLogAsync(Topic t, CancellationToken ct)
    {
        var link = t.Notion;
        if (link?.LogDsId == null || DailyOf == null) return;
        var since = DateOnly.FromDateTime(DateTime.Now).AddDays(-13);
        foreach (var (day, minutes, courses) in DailyOf(t).Where(d => d.Day >= since && d.Minutes >= 1).OrderBy(d => d.Day))
        {
            var props = new JsonObject
            {
                [GTitle] = NotionClient.TitleProp(day.ToString("yyyy/MM/dd") + " — " + DayName(day.DayOfWeek)),
                [GDate] = NotionClient.DateProp(day.ToDateTime(TimeOnly.MinValue)),
                [GMinutes] = NotionClient.NumberProp(Math.Round(minutes)),
                [GCourses] = NotionClient.RelationProp(courses.Select(c => c.NotionPageId ?? "").ToArray()),
            };
            string key = t.Id + "|" + day.ToString("yyyy-MM-dd");
            string h = Hash(props.ToJsonString());
            if (_state.LogRows.TryGetValue(key, out var row))
            {
                if (_state.Hash.TryGetValue(row, out var old) && old == h) continue;
                if (_state.Detached.Contains(row)) continue;
                try { await _api.UpdatePageAsync(row, props, ct); }
                catch (NotionException ex) when (IsGone(ex)) { _state.Detached.Add(row); continue; }
            }
            else
            {
                row = await _api.CreatePageAsync(new JsonObject { ["type"] = "data_source_id", ["data_source_id"] = link.LogDsId }, props, "🗓️", null, ct);
                _state.LogRows[key] = row;
            }
            _state.Hash[row] = h;
        }
    }

    private static string DayName(DayOfWeek d) => d switch
    {
        DayOfWeek.Saturday => "السبت", DayOfWeek.Sunday => "الأحد", DayOfWeek.Monday => "الاتنين", DayOfWeek.Tuesday => "التلات",
        DayOfWeek.Wednesday => "الأربع", DayOfWeek.Thursday => "الخميس", _ => "الجمعة"
    };
}
