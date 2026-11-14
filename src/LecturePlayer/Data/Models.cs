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

    /// <summary>رابط قائمة التشغيل على يوتيوب اللي المحاضرة دي جاية منها (عشان نجيب الجديد).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceUrl { get; set; }

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
    public bool Expanded { get; set; } = true;

    /// <summary>آخر تعديل للاسم (للدمج بين الأجهزة).</summary>
    public DateTime InfoUpdatedUtc { get; set; }
    public void TouchInfo() => InfoUpdatedUtc = DateTime.UtcNow;
}

/// <summary>كورس = مجموعة محاضرات.</summary>
public sealed class Course
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool Expanded { get; set; } = true;
    public List<LectureGroup> Groups { get; set; } = new();

    /// <summary>سرعة التشغيل الخاصة بالكورس ده (null = السرعة العامة).</summary>
    public float? Rate { get; set; }

    /// <summary>الموضوع اللي الكورس تحته (null = برا أي موضوع).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TopicId { get; set; }

    // ---------- بيانات الكورس (المرحلة 2) ----------
    [JsonConverter(typeof(JsonStringEnumConverter<CourseStatus>))]
    public CourseStatus Status { get; set; } = CourseStatus.Active;

    /// <summary>الأولوية: 0 عادية، 1 مهمة، 2 عاجلة.</summary>
    public int Priority { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Instructor { get; set; }
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

    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool Maximized { get; set; }
}

public sealed class PathMapping
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}
