using System.Text.Encodings.Web;
using System.Text.Json;

namespace LecturePlayer.Data;

/// <summary>حفظ وتحميل ملفات القوائم والإعدادات (JSON).</summary>
public static class Storage
{
    public const string LibraryExtension = ".lpl";
    public const string LibraryFilter = "مكتبة «مسار» (*.lpl)|*.lpl|كل الملفات|*.*";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // عشان العربي يتكتب مقروء في الملف
    };

    /// <summary>المكان القديم (قبل النسخة المحمولة): %AppData%\LecturePlayer</summary>
    public static string LegacyDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LecturePlayer");

    /// <summary>فولدر Data جنب البرنامج نفسه (LecturePlayer.exe).</summary>
    public static string PortableDataDir => Path.Combine(AppContext.BaseDirectory, "Data");

    private static readonly Lazy<string> DataDir = new(ResolveDataDir);

    /// <summary>
    /// فين بتتحفظ القوائم والإعدادات والأدوات:
    /// فولدر Data جنب البرنامج، ولو مفيش صلاحية كتابة هناك (زي Program Files) ← %AppData%.
    /// </summary>
    public static string AppDataDir => DataDir.Value;

    public static bool IsPortable =>
        !string.Equals(Path.GetFullPath(AppDataDir), Path.GetFullPath(LegacyDataDir), StringComparison.OrdinalIgnoreCase);

    private static string ResolveDataDir()
    {
        string portable = PortableDataDir;
        if (!CanWriteTo(portable)) return LegacyDataDir;
        try { MigrateFromLegacy(portable); } catch { /* النقل مش حرج، البرنامج يشتغل عادي */ }
        return portable;
    }

    private static bool CanWriteTo(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// أول تشغيل للنسخة المحمولة: ننقل (نسخ) القوائم والإعدادات والأدوات من %AppData%
    /// لفولدر Data، ونعدّل المسارات اللي في الإعدادات عشان تشاور على المكان الجديد.
    /// الملفات القديمة بتفضل زي ما هي كنسخة احتياطية.
    /// </summary>
    private static void MigrateFromLegacy(string portable)
    {
        string legacy = LegacyDataDir;
        if (!Directory.Exists(legacy)) return;
        if (string.Equals(Path.GetFullPath(legacy), Path.GetFullPath(portable), StringComparison.OrdinalIgnoreCase)) return;

        // لو فولدر Data فيه بيانات بالفعل يبقى اتنقل قبل كده
        if (File.Exists(Path.Combine(portable, "settings.json")) ||
            Directory.EnumerateFiles(portable, "*" + LibraryExtension).Any())
            return;

        foreach (string file in Directory.EnumerateFiles(legacy, "*" + LibraryExtension))
            File.Copy(file, Path.Combine(portable, Path.GetFileName(file)), overwrite: false);

        string legacyTools = Path.Combine(legacy, "tools");
        if (Directory.Exists(legacyTools))
        {
            string tools = Path.Combine(portable, "tools");
            Directory.CreateDirectory(tools);
            foreach (string file in Directory.EnumerateFiles(legacyTools, "*.exe"))
                File.Copy(file, Path.Combine(tools, Path.GetFileName(file)), overwrite: false);
        }

        string legacySettings = Path.Combine(legacy, "settings.json");
        if (File.Exists(legacySettings))
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(legacySettings), Options) ?? new AppSettings();
            settings.LastLibraryPath = Remap(settings.LastLibraryPath, legacy, portable);
            settings.RecentFiles = settings.RecentFiles.Select(f => Remap(f, legacy, portable)!).ToList();
            WriteAtomic(Path.Combine(portable, "settings.json"), JsonSerializer.Serialize(settings, Options));
        }
    }

    /// <summary>قائمة كانت في %AppData% ← نفس الاسم في فولدر Data (لو اتنسخت).</summary>
    private static string? Remap(string? path, string fromDir, string toDir)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string full = Path.GetFullPath(path);
        string from = Path.GetFullPath(fromDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(from, StringComparison.OrdinalIgnoreCase)) return path;
        string candidate = Path.Combine(toDir, full.Substring(from.Length));
        return File.Exists(candidate) ? candidate : path;
    }

    public static string DefaultLibraryPath => Path.Combine(AppDataDir, "مكتبتي" + LibraryExtension);
    private static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    // ---------------- Library ----------------

    public static CourseLibrary LoadLibrary(string path, IReadOnlyList<PathMapping>? mappings = null) =>
        FromJson(File.ReadAllText(path), path, mappings);

    /// <summary>القائمة كنص JSON (للتراجع).</summary>
    public static string Serialize(CourseLibrary lib) => JsonSerializer.Serialize(lib, Options);

    /// <summary>القائمة من نص JSON، مع ترقية الملفات القديمة وحساب المسارات على الجهاز ده.</summary>
    public static CourseLibrary FromJson(string json, string libraryPath, IReadOnlyList<PathMapping>? mappings = null)
    {
        var lib = JsonSerializer.Deserialize<CourseLibrary>(json, Options) ?? new CourseLibrary();
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(libraryPath)) ?? "";

        // ترقية ملفات النسخة الأولى: المحاضرات القديمة تبقى كورس واحد
        lib.Courses ??= new List<Course>();
        lib.Topics ??= new List<Topic>();
        lib.Live ??= new List<LiveChannel>();
        lib.Places ??= new List<Place>();
        lib.Links ??= new List<LinkedFolder>();
        foreach (var l in lib.Links) l.Ignored ??= new List<string>();
        lib.MigrateLinks();
        if (lib.Groups is { Count: > 0 })
            lib.Courses.Insert(0, new Course { Name = lib.Name, Groups = lib.Groups });
        lib.Groups = null;
        lib.Version = 2;

        foreach (var c in lib.Courses)
        {
            c.Groups ??= new List<LectureGroup>();
            c.Tags ??= new List<string>();
            c.StudyDays ??= new List<DayOfWeek>();
            foreach (var g in c.Groups)
            {
                g.Videos ??= new List<VideoItem>();
                g.Attachments ??= new List<Attachment>();
            }
        }

        foreach (var g in lib.AllGroups)
        {
            foreach (var v in g.Videos)
            {
                if (string.IsNullOrWhiteSpace(v.Title))
                    v.Title = Path.GetFileNameWithoutExtension(v.Path);

                v.Bookmarks ??= new List<Bookmark>();
                ResolveLocalPath(v, baseDir, mappings);
            }
        }
        return lib;
    }

    /// <summary>
    /// المسار الفعلي على الجهاز ده: المسار المكتوب ← تحويلات الجهاز ده ← المسار النسبي لملف القائمة.
    /// Path نفسه مش بيتغير (عشان الأجهزة التانية).
    /// </summary>
    public static void ResolveLocalPath(VideoItem v, string baseDir, IReadOnlyList<PathMapping>? mappings)
    {
        if (v.IsOnline || FileCache.Exists(v.Path)) { v.LocalPath = v.Path; return; }

        if (mappings != null)
        {
            foreach (var m in mappings)
            {
                if (string.IsNullOrEmpty(m.From) || !v.Path.StartsWith(m.From, StringComparison.OrdinalIgnoreCase)) continue;
                string candidate = m.To + v.Path.Substring(m.From.Length);
                if (FileCache.Exists(candidate)) { v.LocalPath = candidate; return; }
            }
        }

        if (!string.IsNullOrEmpty(v.RelativePath))
        {
            try
            {
                string candidate = Path.GetFullPath(Path.Combine(baseDir, v.RelativePath));
                if (FileCache.Exists(candidate)) v.LocalPath = candidate;
            }
            catch { /* مسار نسبي غلط */ }
        }
    }

    /// <summary>المكان الفعلي للمرفق على الجهاز ده (رابط، أو ملف بالمسار الكامل أو النسبي).</summary>
    public static string ResolveAttachment(Attachment a, string libraryPath)
    {
        if (a.IsLink || File.Exists(a.Target) || Directory.Exists(a.Target)) return a.Target;
        if (!string.IsNullOrEmpty(a.RelativePath))
        {
            try
            {
                string baseDir = Path.GetDirectoryName(Path.GetFullPath(libraryPath)) ?? "";
                string candidate = Path.GetFullPath(Path.Combine(baseDir, a.RelativePath));
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return a.Target;
    }

    public static void SaveLibrary(string path, CourseLibrary lib)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        foreach (var a in lib.AllGroups.SelectMany(g => g.Attachments))
        {
            if (a.IsLink || !FileCache.Exists(a.Target)) continue;
            string rel = Path.GetRelativePath(baseDir, a.Target);
            a.RelativePath = Path.IsPathRooted(rel) ? null : rel;
        }
        foreach (var v in lib.AllVideos)
        {
            if (v.IsOnline) { v.RelativePath = null; continue; }
            if (!FileCache.Exists(v.LocalPath)) continue; // ملف مش موجود على الجهاز ده: نسيب المسار النسبي زي ما هو
            string rel = Path.GetRelativePath(baseDir, v.LocalPath);
            // المسار النسبي مفيد بس لو الفيديو جنب ملف القائمة (مش في درايف تاني)
            v.RelativePath = Path.IsPathRooted(rel) ? null : rel;
        }
        WriteAtomic(path, JsonSerializer.Serialize(lib, Options));
    }

    // ---------------- Backups ----------------

    public static string BackupDir(string libraryPath) =>
        Path.Combine(AppDataDir, "backups", Path.GetFileNameWithoutExtension(libraryPath));

    /// <summary>نسخة احتياطية من ملف القائمة لو آخر نسخة أقدم من 6 ساعات. بنحتفظ بآخر 10 نسخ.</summary>
    public static void BackupIfDue(string libraryPath, int keep = 10, bool force = false)
    {
        try
        {
            if (!File.Exists(libraryPath)) return;
            string dir = BackupDir(libraryPath);
            Directory.CreateDirectory(dir);
            var existing = new DirectoryInfo(dir).GetFiles("*" + LibraryExtension)
                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            if (!force && existing.Count > 0 && DateTime.UtcNow - existing[0].LastWriteTimeUtc < TimeSpan.FromHours(6)) return;

            string target = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + LibraryExtension);
            File.Copy(libraryPath, target, overwrite: true);
            File.SetLastWriteTimeUtc(target, DateTime.UtcNow);

            foreach (var old in new DirectoryInfo(dir).GetFiles("*" + LibraryExtension)
                         .OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep))
                old.Delete();
        }
        catch (Exception ex) { Log.Error("backup", ex); }
    }

    // ---------------- Settings ----------------

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Options) ?? new AppSettings();
        }
        catch { /* ملف إعدادات بايظ = نبدأ بالافتراضي */ }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        try { WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, Options)); }
        catch { /* الإعدادات مش حاجة حرجة */ }
    }

    public static string DeviceName => Environment.MachineName;

    /// <summary>بنكتب في ملف مؤقت وبعدين نبدّل، عشان لو الجهاز فصل وقت الحفظ الملف الأصلي ميبوظش.</summary>
    private static void WriteAtomic(string path, string content)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + "." + Environment.MachineName + ".tmp"; // اسم مختلف لكل جهاز (OneDrive)
        File.WriteAllText(tmp, content, System.Text.Encoding.UTF8);
        File.Move(tmp, path, overwrite: true);
    }
}
