using System.Text.Json.Serialization;

namespace LecturePlayer.Data;

/// <summary>فيديو واحد داخل محاضرة.</summary>
public sealed class VideoItem
{
    /// <summary>المسار الكامل للملف.</summary>
    public string Path { get; set; } = "";

    /// <summary>المسار نسبةً لمكان ملف القائمة (عشان لو نقلت فولدر الكورس كله مع ملف القائمة يفضل شغال).</summary>
    public string? RelativePath { get; set; }

    public string Title { get; set; } = "";

    /// <summary>مدة الفيديو بالمللي ثانية (0 = لسه متعرفتش).</summary>
    public long DurationMs { get; set; }

    /// <summary>آخر مكان وقفت عنده.</summary>
    public long LastPositionMs { get; set; }

    public bool Watched { get; set; }

    /// <summary>ملاحظاتك على الفيديو ده.</summary>
    public string? Notes { get; set; }

    /// <summary>علامات مرجعية جوه الفيديو (B).</summary>
    public List<Bookmark> Bookmarks { get; set; } = new();

    /// <summary>
    /// آخر مرة حالة الفيديو اتغيرت (مكان الاستكمال، المشاهدة، الملاحظات، العلامات).
    /// بيستخدم في الدمج بين الأجهزة: الأحدث يكسب.
    /// </summary>
    public DateTime UpdatedUtc { get; set; }

    public void Touch() => UpdatedUtc = DateTime.UtcNow;

    /// <summary>فيديو من النت (يوتيوب وغيره): Path فيه الرابط.</summary>
    [JsonIgnore] public bool IsOnline => MediaFiles.IsUrl(Path);
    /// <summary>درس يوديمي: بيتشغل جوه صفحة يوديمي (محمي) — مايتنزلش ومايتقراش بـ yt-dlp.</summary>
    [JsonIgnore] public bool IsUdemy => Udemy.IsLecture(Path);
    /// <summary>فيديو نت ينفع يتنزل/يتقري بـ yt-dlp (يوتيوب وغيره، مش يوديمي).</summary>
    [JsonIgnore] public bool IsDownloadable => IsOnline && !IsWebLesson && !IsMoodlePage;
    /// <summary>درس IT Legend (صفحة الكورس + #node=…) — مشغّل VdoCipher محمي.</summary>
    [JsonIgnore] public bool IsItLegend => ItLegend.IsLesson(Path);
    /// <summary>درس بيتشغل بصفحة المنصة جوه مساحة الفيديو (يوديمي، IT Legend).</summary>
    [JsonIgnore] public bool IsWebLesson => IsUdemy || IsItLegend;
    /// <summary>صفحة نشاط على موقع Moodle (اختبار/استبيان/ملف…) — بتتفتح في متصفحك (اللي إنت داخل بيه).</summary>
    [JsonIgnore] public bool IsMoodlePage => Moodle.IsActivity(Path);
    /// <summary>العنصر ده جاي منين على المنصة («moodle-cm:123») عشان التحديث يلاقيه حتى لو اتغير رابطه.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceRef { get; set; }

    private string? _localPath;

    /// <summary>
    /// المسار الفعلي على الجهاز ده. ممكن يختلف عن Path لو الكورس متحط في مكان تاني
    /// على جهاز تاني (Path بيفضل زي ما هو عشان الجهاز التاني).
    /// </summary>
    [JsonIgnore]
    public string LocalPath
    {
        get => _localPath ?? Path;
        set => _localPath = string.Equals(value, Path, StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    /// <summary>فيديو جديد اتجاب في تحديث قائمة يوتيوب ولسه متفتحش.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsNew { get; set; }

    public static VideoItem FromUrl(OnlineEntry e) => new()
    {
        Path = e.Url,
        Title = e.Title,
        DurationMs = e.DurationMs
    };

    public static VideoItem FromFile(string path) => new()
    {
        Path = System.IO.Path.GetFullPath(path),
        Title = System.IO.Path.GetFileNameWithoutExtension(path)
    };
}

/// <summary>علامة مرجعية في ثانية معينة، ومعاها ملاحظة اختيارية.</summary>
public sealed class Bookmark
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long TimeMs { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>المراجعة المتباعدة: ميعاد المراجعة الجاية (null = مش في المراجعة أو اتثبتت).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ReviewDueUtc { get; set; }

    /// <summary>مرحلة المراجعة (0..5). 5 = اتثبتت.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ReviewStage { get; set; }
}

public enum CourseStatus { Active, Planned, Paused, Done }

public enum LiveKind { Radio, Tv }

/// <summary>
/// محطة راديو أو قناة تليفزيون بث مباشر (المفضلة، والقنوات اللي المستخدم ضافها بنفسه).
/// بتتحفظ في ملف المكتبة فبتتزامن بين الأجهزة، ومالهاش «اتشاف» ولا تقدم.
/// </summary>
public sealed class LiveChannel
{
    /// <summary>محطات Radio Browser: «rb:» + رقم المحطة، عشان نفس المحطة ماتتكررش من جهازين.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter<LiveKind>))]
    public LiveKind Kind { get; set; } = LiveKind.Radio;
    /// <summary>البلد أو المجموعة (من ملف M3U).</summary>
    public string? Group { get; set; }
    public string? Tags { get; set; }
    public string? Logo { get; set; }
    /// <summary>رقم المحطة في Radio Browser (لو جاية من الدليل).</summary>
    public string? StationUuid { get; set; }
    public bool Favorite { get; set; }
    public DateTime InfoUpdatedUtc { get; set; }
    public void TouchInfo() => InfoUpdatedUtc = DateTime.UtcNow;
}

/// <summary>مرفق في محاضرة: ملف على الجهاز أو رابط.</summary>
public sealed class Attachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>مسار ملف أو رابط http.</summary>
    public string Target { get; set; } = "";

    /// <summary>مسار نسبي لملف القائمة (لو الملف جنبها).</summary>
    public string? RelativePath { get; set; }

    [JsonIgnore] public bool IsLink => MediaFiles.IsUrl(Target);
}

/// <summary>مجموعة = محاضرة فيها كذا فيديو.</summary>
public sealed class LectureGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool Expanded { get; set; } = true;
    public List<VideoItem> Videos { get; set; } = new();
    /// <summary>الفولدر اللي المحاضرة جاية منه (لو تبع فولدر مربوط).</summary>
    public string? SourceFolder { get; set; }

    /// <summary>رابط قائمة التشغيل على يوتيوب اللي المحاضرة دي جاية منها (عشان نجيب الجديد).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceUrl { get; set; }
    /// <summary>القسم جاي من يوديمي («udemy-chapter:رقم») — مش قائمة يوتيوب.</summary>
    [JsonIgnore] public bool IsUdemySection => SourceUrl?.StartsWith("udemy-chapter:", StringComparison.Ordinal) == true;
    /// <summary>مربوط بقائمة تشغيل (يوتيوب…) بتتحدّث بـ yt-dlp.</summary>
    /// <summary>قسم من كورس Moodle («moodle-section:رقم»).</summary>
    [JsonIgnore] public bool IsMoodleSection => SourceUrl?.StartsWith("moodle-section:", StringComparison.Ordinal) == true;
    [JsonIgnore] public bool IsPlaylist => !string.IsNullOrWhiteSpace(SourceUrl) && !IsUdemySection && !IsMoodleSection && !IsItLegendSection;
    [JsonIgnore] public bool IsItLegendSection => SourceUrl?.StartsWith("itl-module:", StringComparison.Ordinal) == true;

    /// <summary>آخر مرة اتجاب فيها الجديد من القائمة.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastSyncedUtc { get; set; }

    [JsonIgnore] public int NewCount => Videos.Count(v => v.IsNew);

    /// <summary>مرفقات المحاضرة: PDF، سلايدات، كود، روابط.</summary>
    public List<Attachment> Attachments { get; set; } = new();

    /// <summary>آخر تعديل للمرفقات (للدمج بين الأجهزة).</summary>
    public DateTime InfoUpdatedUtc { get; set; }

    [JsonIgnore] public int WatchedCount => Videos.Count(v => v.Watched);
    [JsonIgnore] public long TotalDurationMs => Videos.Sum(v => v.DurationMs);
    [JsonIgnore] public bool IsCompleted => Videos.Count > 0 && Videos.All(v => v.Watched);
}

/// <summary>
/// موضوع = تفريعة رئيسية فوق الكورسات (مثلًا «C#» أو «قواعد البيانات»).
/// الكورسات نفسها في <see cref="CourseLibrary.Courses"/>، وكل كورس بيشاور على موضوعه بـ <see cref="Course.TopicId"/>.
/// </summary>
public sealed class Topic
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>أيقونة مخصصة (مفتاح من <c>IconGlyphs</c>)؛ null = أيقونة النوع العادية.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconKey { get; set; }
    /// <summary>لون مخصص «#RRGGBB» للأيقونة والاسم؛ null = اللون العادي.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ColorHex { get; set; }

    public bool Expanded { get; set; } = true;

    /// <summary>قديم (4.1.30): بيتنقل لـ <see cref="CourseLibrary.Links"/> وقت الفتح.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceFolder { get; set; }

    /// <summary>آخر تعديل للاسم (للدمج بين الأجهزة).</summary>
    public DateTime InfoUpdatedUtc { get; set; }
    public void TouchInfo() => InfoUpdatedUtc = DateTime.UtcNow;
}

/// <summary>
/// مصدر (Place) = الجهة اللي الكورسات جاية منها جوه الموضوع (منصة، أكاديمية، قناة، مدرّس).
/// الهيكل: موضوع ← مكان ← كورس ← محاضرة ← فيديو. المكان اختياري: الكورس ممكن يبقى تحت الموضوع على طول.
/// الكورس بيفضل شايل <see cref="Course.TopicId"/> بتاع موضوعه، وفوقه <see cref="Course.PlaceId"/>؛
/// عشان كده أي نسخة قديمة تفتح القائمة تشوف الكورسات تحت مواضيعها عادي.
/// </summary>
public sealed class Place
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>أيقونة مخصصة (مفتاح من <c>IconGlyphs</c>)؛ null = أيقونة النوع العادية.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconKey { get; set; }
    /// <summary>لون مخصص «#RRGGBB» للأيقونة والاسم؛ null = اللون العادي.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ColorHex { get; set; }

    /// <summary>الموضوع اللي المكان جواه (null = برا أي موضوع).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TopicId { get; set; }
    public bool Expanded { get; set; } = true;

    /// <summary>
    /// المصدر الأكبر لو ده مصدر فرعي (مثلًا منصة ← مدرّس). مستويين بس: المصدر الفرعي مايبقاش جواه مصادر.
    /// النسخ القديمة بتتجاهله فالمصدر الفرعي بيظهر عندها جنب الأكبر.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentId { get; set; }

    /// <summary>قديم (4.1.31): بيتنقل لـ <see cref="CourseLibrary.Links"/> وقت الفتح.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SourceFolders { get; set; }

    /// <summary>آخر تعديل للاسم/الموضوع/الفولدرات (للدمج بين الأجهزة).</summary>
    public DateTime InfoUpdatedUtc { get; set; }
    public void TouchInfo() => InfoUpdatedUtc = DateTime.UtcNow;
}

/// <summary>
/// فولدر متابَع (غالبًا على Google Drive): البرنامج بيقراه كل دقيقتين ويضيف الجديد لوحده.
/// مكانه في المكتبة (الموضوع/المصدر) هو اللي الكورسات الجديدة بتتحط فيه.
/// </summary>
public sealed class LinkedFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    /// <summary>true = الفولدر كله كورس واحد؛ false = كل فولدر جواه كورس لوحده.</summary>
    public bool SingleCourse { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TopicId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlaceId { get; set; }
    /// <summary>كورسات/محاضرات/فيديوهات من الفولدر ده المستخدم مسحها من المكتبة: المزامنة مش بترجّعها.</summary>
    public List<string> Ignored { get; set; } = new();
    public DateTime InfoUpdatedUtc { get; set; }
    public void TouchInfo() => InfoUpdatedUtc = DateTime.UtcNow;
}

/// <summary>كورس = مجموعة محاضرات.</summary>
public sealed class Course
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>أيقونة مخصصة (مفتاح من <c>IconGlyphs</c>)؛ null = أيقونة النوع العادية.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconKey { get; set; }
    /// <summary>لون مخصص «#RRGGBB» للأيقونة والاسم؛ null = اللون العادي.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ColorHex { get; set; }

    public bool Expanded { get; set; } = true;
    public List<LectureGroup> Groups { get; set; } = new();

    /// <summary>سرعة التشغيل الخاصة بالكورس ده (null = السرعة العامة).</summary>
    public float? Rate { get; set; }

    /// <summary>الموضوع اللي الكورس تحته (null = برا أي موضوع).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TopicId { get; set; }

    /// <summary>المكان اللي الكورس تحته جوه موضوعه (null = تحت الموضوع على طول).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlaceId { get; set; }
    /// <summary>الفولدر اللي الكورس جاي منه (لو تبع فولدر مربوط).</summary>
    public string? SourceFolder { get; set; }

    // ---------- بيانات الكورس (المرحلة 2) ----------
    [JsonConverter(typeof(JsonStringEnumConverter<CourseStatus>))]
    public CourseStatus Status { get; set; } = CourseStatus.Active;

    /// <summary>الأولوية: 0 عادية، 1 مهمة، 2 عاجلة.</summary>
    public int Priority { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Instructor { get; set; }
    /// <summary>رقم الكورس على يوديمي (كورس مستورد من حسابي) — للتحديث ومزامنة التقدّم.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? UdemyId { get; set; }
    public string? SourceUrl { get; set; }

    /// <summary>الهدف من الكورس (بيظهر في الشاشة الرئيسية).</summary>
    public string? Goal { get; set; }

    /// <summary>عايز تخلصه إمتى (تاريخ بس، من غير وقت).</summary>
    public DateTime? Deadline { get; set; }

    /// <summary>أيام المذاكرة للكورس ده (فاضية = كل الأيام).</summary>
    public List<DayOfWeek> StudyDays { get; set; } = new();

    /// <summary>آخر تعديل لبيانات الكورس (للدمج بين الأجهزة).</summary>
    public DateTime InfoUpdatedUtc { get; set; }
    public void TouchInfo() => InfoUpdatedUtc = DateTime.UtcNow;

    [JsonIgnore] public long RemainingMs => AllVideos.Where(v => !v.Watched).Sum(v => Math.Max(0, v.DurationMs - v.LastPositionMs));

    public bool IsStudyDay(DayOfWeek d) => StudyDays.Count == 0 || StudyDays.Contains(d);

    [JsonIgnore] public IEnumerable<VideoItem> AllVideos => Groups.SelectMany(g => g.Videos);

    /// <summary>آخر نشاط في الكورس (لشاشة «كمّل اللي بدأته»).</summary>
    [JsonIgnore] public DateTime LastActivityUtc => AllVideos.Select(v => v.UpdatedUtc).DefaultIfEmpty().Max();
    [JsonIgnore] public int VideoCount => Groups.Sum(g => g.Videos.Count);
    [JsonIgnore] public int WatchedCount => Groups.Sum(g => g.WatchedCount);
    [JsonIgnore] public long TotalDurationMs => Groups.Sum(g => g.TotalDurationMs);
    [JsonIgnore] public bool IsCompleted => VideoCount > 0 && WatchedCount == VideoCount;
}

/// <summary>ملف القائمة (.lpl): مكتبة فيها كذا كورس، وكل كورس فيه محاضرات.</summary>
public sealed class CourseLibrary
{
    public int Version { get; set; } = 2;
    public string Name { get; set; } = "مكتبتي";

    /// <summary>المواضيع بالترتيب (التفريعة الرئيسية).</summary>
    public List<Topic> Topics { get; set; } = new();
    /// <summary>الأماكن بالترتيب (كل مكان بيشاور على موضوعه).</summary>
    public List<Place> Places { get; set; } = new();
    /// <summary>الفولدرات المتابَعة (الجديد فيها بيتضاف لوحده).</summary>
    public List<LinkedFolder> Links { get; set; } = new();
    public List<Course> Courses { get; set; } = new();

    /// <summary>البث المباشر: المفضلة والقنوات المضافة.</summary>
    public List<LiveChannel> Live { get; set; } = new();

    /// <summary>
    /// موجودة بس عشان نقرا ملفات النسخة الأولى (اللي كانت محاضرات من غير كورسات).
    /// بتتحول لكورس واحد وقت التحميل، ومش بتتكتب تاني.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LectureGroup>? Groups { get; set; }

    public string? LastGroupId { get; set; }
    public string? LastVideoPath { get; set; }

    [JsonIgnore] public IEnumerable<LectureGroup> AllGroups => Courses.SelectMany(c => c.Groups);
    [JsonIgnore] public IEnumerable<VideoItem> AllVideos => AllGroups.SelectMany(g => g.Videos);

    public LectureGroup? FindGroupOf(VideoItem video) => AllGroups.FirstOrDefault(g => g.Videos.Contains(video));
    public Course? FindCourseOf(LectureGroup group) => Courses.FirstOrDefault(c => c.Groups.Contains(group));
    public Course? FindCourseOf(VideoItem video) => Courses.FirstOrDefault(c => c.Groups.Any(g => g.Videos.Contains(video)));

    /// <summary>موضوع الكورس (null لو برا أي موضوع أو الموضوع اتمسح).</summary>
    public Topic? TopicOf(Course c) => c.TopicId == null ? null : Topics.FirstOrDefault(t => t.Id == c.TopicId);

    /// <summary>كورسات الموضوع بترتيبها في القايمة (null = الكورسات اللي برا أي موضوع).</summary>
    public IEnumerable<Course> CoursesOf(Topic? t) =>
        t == null ? Courses.Where(c => TopicOf(c) == null) : Courses.Where(c => c.TopicId == t.Id);

    /// <summary>
    /// مكان الكورس، بس لو المكان موجود وفي نفس موضوع الكورس.
    /// (لو حد نقل الكورس لموضوع تاني من نسخة قديمة، الكورس يرجع تحت موضوعه على طول.)
    /// </summary>
    public Place? PlaceOf(Course c)
    {
        if (c.PlaceId == null) return null;
        var p = Places.FirstOrDefault(x => x.Id == c.PlaceId);
        return p != null && p.TopicId == TopicOf(c)?.Id ? p : null;
    }

    public Topic? TopicOf(Place p) => p.TopicId == null ? null : Topics.FirstOrDefault(t => t.Id == p.TopicId);

    /// <summary>أماكن الموضوع بترتيبها (null = الأماكن اللي برا أي موضوع).</summary>
    public IEnumerable<Place> PlacesOf(Topic? t) =>
        t == null ? Places.Where(p => TopicOf(p) == null) : Places.Where(p => p.TopicId == t.Id);

    /// <summary>
    /// المصدر الأكبر بتاع المصدر الفرعي (null لو مصدر أساسي، أو لو الأكبر اتمسح أو في موضوع تاني،
    /// أو لو الأكبر نفسه فرعي — مستويين بس).
    /// </summary>
    public Place? ParentOf(Place p)
    {
        if (p.ParentId == null || p.ParentId == p.Id) return null;
        var parent = Places.FirstOrDefault(x => x.Id == p.ParentId);
        if (parent == null || parent.TopicId != p.TopicId || parent.ParentId != null && Places.Any(x => x.Id == parent.ParentId)) return null;
        return parent;
    }

    /// <summary>المصادر الأساسية في الموضوع (اللي مش جوه مصدر تاني).</summary>
    public IEnumerable<Place> TopPlacesOf(Topic? t) => PlacesOf(t).Where(p => ParentOf(p) == null);

    /// <summary>مصادر الموضوع بالترتيب اللي بتظهر بيه: كل مصدر أساسي وبعده الفرعية بتاعته (Depth = 1).</summary>
    public IEnumerable<(Place Place, int Depth)> OrderedPlacesOf(Topic? t)
    {
        foreach (var p in TopPlacesOf(t))
        {
            yield return (p, 0);
            foreach (var c in ChildrenOf(p)) yield return (c, 1);
        }
    }

    /// <summary>المصادر الفرعية جوه المصدر.</summary>
    public IEnumerable<Place> ChildrenOf(Place p) => Places.Where(x => x != p && ParentOf(x) == p);

    /// <summary>كورسات المكان بترتيبها (اللي فيه على طول، من غير المصادر الفرعية).</summary>
    public IEnumerable<Course> CoursesInPlace(Place p) => Courses.Where(c => c.PlaceId == p.Id && PlaceOf(c) == p);

    /// <summary>كل كورسات المصدر: اللي فيه على طول واللي في مصادره الفرعية.</summary>
    public IEnumerable<Course> AllCoursesUnder(Place p)
    {
        var ids = new HashSet<string>(ChildrenOf(p).Select(x => x.Id).Append(p.Id));
        return Courses.Where(c => c.PlaceId != null && ids.Contains(c.PlaceId) && PlaceOf(c) != null);
    }

    /// <summary>ينفع المصدر ده يتحط جوه المصدر ده؟ (مستويين بس، ومش جوه نفسه)</summary>
    public bool CanNest(Place child, Place parent) =>
        child != parent && ParentOf(parent) == null && !ChildrenOf(child).Any();

    /// <summary>يحط المصدر جوه مصدر تاني (أو يطلّعه منه لو null). بياخد موضوع الأكبر، وكورساته معاه.</summary>
    public void NestPlace(Place child, Place? parent)
    {
        if (parent != null && !CanNest(child, parent)) throw new InvalidOperationException("مستويين بس من المصادر");
        if (parent != null && parent.TopicId != child.TopicId) MovePlace(child, TopicOf(parent));
        child.ParentId = parent?.Id;
        child.TouchInfo();
    }

    /// <summary>كورسات الموضوع اللي مش جوه أي مكان (بتظهر تحت الموضوع على طول).</summary>
    public IEnumerable<Course> DirectCoursesOf(Topic? t) => CoursesOf(t).Where(c => PlaceOf(c) == null);

    /// <summary>
    /// يحط الكورس في مكان (أو يطلعه منه لو null). الكورس بياخد موضوع المكان.
    /// </summary>
    public void MoveCourse(Course c, Topic? topic, Place? place)
    {
        c.TopicId = place != null ? place.TopicId : topic?.Id;
        c.PlaceId = place?.Id;
        c.TouchInfo();
    }

    /// <summary>ينقل المصدر لموضوع تاني، بمصادره الفرعية وكل الكورسات. بيبقى مصدر أساسي هناك.</summary>
    public void MovePlace(Place p, Topic? topic)
    {
        var family = ChildrenOf(p).Append(p).ToList();
        var courses = family.SelectMany(CoursesInPlace).ToList();
        if (ParentOf(p) != null) p.ParentId = null;
        foreach (var x in family) { x.TopicId = topic?.Id; x.TouchInfo(); }
        foreach (var c in courses) { c.TopicId = topic?.Id; c.TouchInfo(); }
    }

    /// <summary>
    /// يمسح المصدر: كورساته بتروح للمصدر الأكبر (لو فرعي) أو تحت الموضوع على طول،
    /// ومصادره الفرعية بتبقى مصادر أساسية.
    /// </summary>
    public void RemovePlace(Place p)
    {
        var parent = ParentOf(p);
        foreach (var c in Courses.Where(c => c.PlaceId == p.Id)) { c.PlaceId = parent?.Id; c.TouchInfo(); }
        foreach (var child in ChildrenOf(p).ToList()) { child.ParentId = null; child.TouchInfo(); }
        Places.Remove(p);
    }

    /// <summary>الموضوع والمصدر اللي الفولدر المتابَع بيحط فيهم كورساته الجديدة.</summary>
    public (Topic? Topic, Place? Place) TargetOf(LinkedFolder l)
    {
        var p = l.PlaceId == null ? null : Places.FirstOrDefault(x => x.Id == l.PlaceId);
        if (p != null) return (TopicOf(p), p);
        return (l.TopicId == null ? null : Topics.FirstOrDefault(t => t.Id == l.TopicId), null);
    }

    /// <summary>الفولدر المتابَع اللي الكورس جاي منه (null لو مش من فولدر متابَع).</summary>
    public LinkedFolder? LinkOf(Course c)
    {
        if (string.IsNullOrWhiteSpace(c.SourceFolder)) return null;
        string cp = FolderLink.Norm(c.SourceFolder);
        return Links.FirstOrDefault(l => { string lp = FolderLink.Norm(l.Path); return cp == lp || cp.StartsWith(lp + "\\") || cp.StartsWith(lp + "/"); });
    }

    /// <summary>
    /// ترقية: نسخ 4.1.30/4.1.31 كانت بتحفظ الفولدر المربوط على الموضوع أو المكان؛ دلوقتي كلهم في <see cref="Links"/>.
    /// </summary>
    public void MigrateLinks()
    {
        foreach (var t in Topics.Where(t => !string.IsNullOrWhiteSpace(t.SourceFolder)))
        {
            if (!Links.Any(l => FolderLink.Norm(l.Path) == FolderLink.Norm(t.SourceFolder!)))
                Links.Add(new LinkedFolder { Path = t.SourceFolder!, TopicId = t.Id, InfoUpdatedUtc = t.InfoUpdatedUtc });
            t.SourceFolder = null;
        }
        foreach (var p in Places)
        {
            foreach (string f in (p.SourceFolders ?? new()).Where(f => !string.IsNullOrWhiteSpace(f)))
                if (!Links.Any(l => FolderLink.Norm(l.Path) == FolderLink.Norm(f)))
                    Links.Add(new LinkedFolder { Path = f, TopicId = p.TopicId, PlaceId = p.Id, InfoUpdatedUtc = p.InfoUpdatedUtc });
            p.SourceFolders = null;
        }
    }

    /// <summary>المسار المقروء: «الموضوع › المصدر › الكورس».</summary>
    public string PathOf(Course c)
    {
        var parts = new List<string>();
        if (TopicOf(c) is { } t) parts.Add(t.Name);
        if (PlaceOf(c) is { } p)
        {
            if (ParentOf(p) is { } pp) parts.Add(pp.Name);
            parts.Add(p.Name);
        }
        parts.Add(c.Name);
        return string.Join(" › ", parts);
    }

    [JsonIgnore] public int GroupCount => Courses.Sum(c => c.Groups.Count);
    [JsonIgnore] public int VideoCount => Courses.Sum(c => c.VideoCount);
    [JsonIgnore] public int WatchedCount => Courses.Sum(c => c.WatchedCount);
    [JsonIgnore] public long TotalDurationMs => Courses.Sum(c => c.TotalDurationMs);
}

/// <summary>إعدادات البرنامج نفسه (مش الكورس).</summary>
public sealed class AppSettings
{
    public string? LastLibraryPath { get; set; }
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>
    /// لما البرنامج يفتح: false (الافتراضي) = يفتح فاضي وتختار المكتبة من «مكتباتك» أو «فتح مكتبة»؛ true = يفتح آخر مكتبة على طول.
    /// </summary>
    public bool OpenLastLibraryOnStart { get; set; }

    /// <summary>
    /// المكتبة اللي كانت مفتوحة لما البرنامج قفل عشان يثبّت تحديث: بتتفتح مرة واحدة بعد التحديث
    /// (المثبّت بيشغّل البرنامج من غير ملف). null = مفيش.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReopenAfterUpdate { get; set; }
    public DateTime ReopenAfterUpdateUtc { get; set; }

    /// <summary>الصوت من 0 لـ 200 (أكتر من 100 = تضخيم).</summary>
    public int Volume { get; set; } = 80;

    /// <summary>توحيد مستوى الصوت (فلتر normvol في VLC) — بيتطبق بعد إعادة التشغيل.</summary>
    public bool NormalizeAudio { get; set; }

    public bool AlwaysOnTop { get; set; }
    /// <summary>الوقت تحت الفيديو: true = المدة كلها، false = الباقي.</summary>
    public bool TimeShowsTotal { get; set; }

    /// <summary>
    /// تحويل مسارات خاص بالجهاز ده: لو الكورس على الجهاز ده في مكان غير المكتوب في القائمة
    /// (مثلًا D:\Courses على اللابتوب و E:\Study على جهاز الشغل).
    /// </summary>
    public List<PathMapping> PathMappings { get; set; } = new();

    /// <summary>فيديوهات يوتيوب المتنزلة على الجهاز ده: الرابط ← الملف.</summary>
    public Dictionary<string, string> OfflineFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ---------- التنزيل من يوتيوب ----------
    /// <summary>آخر اختيارات في شاشة التنزيل (بتتفتكر للمرة الجاية).</summary>
    public DownloadKind DownloadKind { get; set; } = DownloadKind.Mp4;
    public int DownloadMaxHeight { get; set; } = 720;
    public int DownloadMp3Kbps { get; set; } = 192;
    public bool DownloadEmbedCover { get; set; } = true;
    public bool DownloadChapters { get; set; } = true;
    public bool DownloadSubtitles { get; set; }
    /// <summary>فولدر الحفظ (فاضي = Downloads\مسار).</summary>
    public string DownloadFolder { get; set; } = "";
    /// <summary>آخر فولدر اتحفظ فيه «حفظ في مكتبة جديدة».</summary>
    public string ExportFolder { get; set; } = "";
    /// <summary>كام تنزيل مع بعض (1 أو 2) — يوتيوب بيبطّأ اللي بينزّل كتير مع بعض.</summary>
    public int DownloadParallel { get; set; } = 1;
    /// <summary>تنبيه الحقوق اتعرض مرة.</summary>
    public bool DownloadNoticeShown { get; set; }
    /// <summary>تنزيلات ماخلصتش (بتكمّل في التشغيل الجاي من مكان ماوقفت).</summary>
    public List<PendingDownload> PendingDownloads { get; set; } = new();

    /// <summary>صورة معاينة لما تقف بالماوس على شريط التقديم (محتاجة ffmpeg).</summary>
    public bool SeekPreview { get; set; } = true;

    /// <summary>مستودع GitHub للتحديثات (owner/repo) — فاضي = مفيش تحقق.</summary>
    public string UpdateRepo { get; set; } = "";
    public bool CheckUpdates { get; set; } = true;
    /// <summary>إصدار المستخدم قال «تخطّاه» (مايتعرضش تاني لوحده).</summary>
    public string SkippedUpdate { get; set; } = "";
    /// <summary>آخر إصدار اشتغل على الجهاز ده (عشان نقول «اتحدّث ✓» بعد التحديث).</summary>
    public string LastRunVersion { get; set; } = "";
    /// <summary>
    /// الوضع المتقدم: كل الأدوات (الذكاء الاصطناعي، معمل الكود، التركيز، الإحصائيات…).
    /// null = لسه ماتحددش (أول تشغيل بعد التحديث): المستخدم الجديد بيبدأ بسيط، والقديم متقدم.
    /// </summary>
    public bool? AdvancedMode { get; set; }
    /// <summary>الجولة التعريفية اتعرضت قبل كده.</summary>
    public bool TourShown { get; set; }
    public float Rate { get; set; } = 1f;
    public bool AutoPlayNext { get; set; } = true;
    /// <summary>التكرار بعد ما الفيديو يخلص: 0 = من غير، 1 = الفيديو ده، 2 = المحاضرة كلها (من أولها تاني).</summary>
    public int RepeatMode { get; set; }
    public bool ContinueToNextGroup { get; set; } = false;
    public bool ResumePosition { get; set; } = true;

    /// <summary>أعلى جودة لفيديوهات النت (0 = أعلى حاجة متاحة).</summary>
    public int OnlineMaxHeight { get; set; } = 1080;
    /// <summary>كوكيز متصفح لـ yt-dlp (firefox / chrome / edge / brave / opera)، فاضي = من غير. على الجهاز ده بس.</summary>
    public string OnlineCookiesBrowser { get; set; } = "";

    /// <summary>لو الرابط فيه فيديو وقائمة تشغيل مع بعض: هات القائمة كلها.</summary>
    public bool WholePlaylist { get; set; } = true;

    // ---------- تتبع المذاكرة (المرحلة 2) ----------
    /// <summary>هدف المذاكرة اليومي بالدقايق.</summary>
    public int DailyGoalMinutes { get; set; } = 60;

    /// <summary>تذكير يومي لو مخلصتش الهدف (وقت محلي "HH:mm"، فاضي = مفيش).</summary>
    public string ReminderTime { get; set; } = "21:00";
    public bool RemindersEnabled { get; set; } = true;
    public int PomodoroWorkMinutes { get; set; } = 25;
    public int PomodoroBreakMinutes { get; set; } = 5;

    /// <summary>وقف الفيديو تلقائيًا لما وقت البريك ييجي.</summary>
    public bool PomodoroPausesVideo { get; set; } = true;

    /// <summary>فلتر الكورسات في القايمة: all / active / planned / paused / done / tag:اسم.</summary>
    public string CourseFilter { get; set; } = "all";

    // ---------- الذكاء الاصطناعي (المرحلة 3) ----------
    /// <summary>مفتاح Claude متشفّر بحساب الويندوز (DPAPI). الإعدادات دي خاصة بالجهاز ومش بتتزامن.</summary>
    public string ClaudeApiKeyProtected { get; set; } = "";

    /// <summary>حسابات Moodle (مهارة تك…) على الجهاز ده — التوكن متشفّر بحساب الويندوز ومابيتزامنش.</summary>
    public List<MoodleAccount> MoodleAccounts { get; set; } = new();
    public string? MoodleLastSite { get; set; }

    [JsonIgnore]
    public string ClaudeApiKey
    {
        get => SecretStore.Unprotect(ClaudeApiKeyProtected);
        set => ClaudeApiKeyProtected = SecretStore.Protect(value?.Trim());
    }

    public string AiModel { get; set; } = ClaudeClient.DefaultModel;

    /// <summary>حد التوكنز في الشهر (0 = من غير حد).</summary>
    public long AiMonthlyTokenCap { get; set; } = 2_000_000;
    public string AiUsageMonth { get; set; } = "";
    public long AiUsageTokens { get; set; }

    /// <summary>موديل whisper للتفريغ على الجهاز: base / small / medium.</summary>
    public string WhisperModel { get; set; } = "small";

    /// <summary>لغة التفريغ: auto / ar / en.</summary>
    public string TranscriptLanguage { get; set; } = "auto";

    /// <summary>اعرض النص كترجمة على الفيديو لما يكون موجود.</summary>
    public bool ShowTranscriptAsSubtitle { get; set; }

    // ---------- أفكار جريئة (المرحلة 4) ----------
    /// <summary>تخطي فترات السكوت تلقائيًا (فيديوهات على الجهاز، محتاج ffmpeg).</summary>
    public bool SkipSilence { get; set; }

    /// <summary>إجمالي الوقت اللي اتوفر من تخطي السكوت (ثواني).</summary>
    public long SilenceSavedSeconds { get; set; }

    /// <summary>وضع التركيز: احجب المواقع دي (محتاج صلاحية مسؤول مرة عند البداية ومرة عند النهاية).</summary>
    public bool FocusBlockSites { get; set; }
    public List<string> FocusSites { get; set; } = FocusBlocker.DefaultSites.ToList();
    public int FocusMinutes { get; set; } = 50;

    /// <summary>اعرض «اللي فات» لما ترجع لكورس بعد عدد الأيام دي (0 = لأ).</summary>
    public int RecapAfterDays { get; set; } = 3;

    /// <summary>فلتر الموضوع في الشاشة الرئيسية (فاضي = كل المواضيع، "-" = من غير موضوع).</summary>
    public string HomeTopicFilter { get; set; } = "";

    /// <summary>فلتر الموضوع في شاشة الإحصائيات.</summary>
    public string StatsTopicFilter { get; set; } = "";

    /// <summary>تحديث قوائم يوتيوب تلقائيًا (جلب الفيديوهات الجديدة) كل كام ساعة.</summary>
    public bool AutoSyncPlaylists { get; set; } = true;
    public int PlaylistSyncHours { get; set; } = 12;

    public int SidePanelWidth { get; set; } = 400;
    public bool SidePanelVisible { get; set; } = true;
    /// <summary>لوحة العلامات والملاحظات مفتوحة؟ (مقفولة افتراضيًا عشان الشجرة تاخد المساحة)</summary>
    public bool NotesExpanded { get; set; }

    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>تنزيل محفوظ عشان يكمّل بعد قفل البرنامج.</summary>
public sealed class PendingDownload
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DownloadKind Kind { get; set; }
    public int MaxHeight { get; set; }
    public int Mp3Kbps { get; set; } = 192;
    public bool EmbedCover { get; set; } = true;
    public bool EmbedChapters { get; set; } = true;
    public bool Subtitles { get; set; }
    public string OutDir { get; set; } = "";
    public string Template { get; set; } = "";
    /// <summary>رابط فيديو المكتبة (لو التنزيل «جوه المكتبة»): الملف بيتربط بيه.</summary>
    public string? LibraryUrl { get; set; }
    public long EstimatedBytes { get; set; }
}

public sealed class PathMapping
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}
