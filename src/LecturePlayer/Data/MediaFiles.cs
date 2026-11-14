using System.Globalization;
using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>التعامل مع ملفات الميديا: الامتدادات، قراءة فولدر، ترتيب طبيعي.</summary>
public static class MediaFiles
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // فيديو
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg",
        ".ts", ".m2ts", ".mts", ".3gp", ".ogv", ".vob",
        // صوت (لو في محاضرات صوتية)
        ".mp3", ".m4a", ".aac", ".wav", ".flac", ".ogg", ".opus", ".wma"
    };

    public static bool IsMedia(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>رابط http / https.</summary>
    public static bool IsUrl(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>كل الروابط اللي في نص (سطر سطر أو مفصولة بمسافات).</summary>
    public static List<string> ExtractUrls(string? text) =>
        (text ?? "").Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().Trim('"', '\'', '<', '>'))
            .Where(IsUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string DialogFilter =>
        "ملفات الفيديو والصوت|" + string.Join(";", Extensions.Select(e => "*" + e)) + "|كل الملفات|*.*";

    /// <summary>ملفات الميديا الموجودة مباشرة في الفولدر (مش الفرعية) بترتيب طبيعي.</summary>
    public static List<string> InDirectory(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir)
                .Where(IsMedia)
                .OrderBy(Path.GetFileName, NaturalComparer.Instance)
                .ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>كل الفولدرات الفرعية (بشكل متكرر) مع تجاهل اللي مفيش صلاحية عليها.</summary>
    public static IEnumerable<string> AllSubDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            }).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    // ---------------- قراءة فولدرات الكورسات ----------------

    /// <summary>
    /// كل فولدر (الرئيسي أو أي فولدر فرعي) فيه فيديوهات مباشرة = محاضرة.
    /// اسم المحاضرة = مسار الفولدر نسبةً للفولدر الرئيسي.
    /// </summary>
    public static List<LectureGroup> ScanFolder(string root)
    {
        root = Path.GetFullPath(root);
        var dirs = new List<string> { root };
        dirs.AddRange(AllSubDirectories(root).OrderBy(d => Path.GetRelativePath(root, d), NaturalComparer.Instance));

        var groups = new List<LectureGroup>();
        foreach (string dir in dirs)
        {
            var files = InDirectory(dir);
            if (files.Count == 0) continue;
            groups.Add(new LectureGroup
            {
                Name = dir == root ? new DirectoryInfo(root).Name : Path.GetRelativePath(root, dir),
                Videos = files.Select(VideoItem.FromFile).ToList(),
                Expanded = groups.Count == 0
            });
        }
        return groups;
    }

    /// <summary>فولدر فيه أكتر من كورس: كل فولدر مباشر جواه = كورس، والفولدرات اللي جوه الكورس = محاضرات.</summary>
    public static List<Course> ScanMultiCourse(string root)
    {
        root = Path.GetFullPath(root);
        var courses = new List<Course>();

        var rootFiles = InDirectory(root);
        if (rootFiles.Count > 0)
        {
            string name = new DirectoryInfo(root).Name;
            courses.Add(new Course
            {
                Name = name,
                Groups = { new LectureGroup { Name = name, Videos = rootFiles.Select(VideoItem.FromFile).ToList() } }
            });
        }

        IEnumerable<string> subs;
        try { subs = Directory.EnumerateDirectories(root).OrderBy(Path.GetFileName, NaturalComparer.Instance).ToList(); }
        catch { subs = Array.Empty<string>(); }

        foreach (string sub in subs)
        {
            if ((new DirectoryInfo(sub).Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
            var groups = ScanFolder(sub);
            if (groups.Count == 0) continue;
            courses.Add(new Course { Name = new DirectoryInfo(sub).Name, Groups = groups, Expanded = courses.Count == 0 });
        }
        return courses;
    }

    /// <summary>
    /// نتيجة ScanFolder شكلها فولدر فيه أكتر من كورس؟
    /// (مفيش فيديوهات في الفولدر الرئيسي، وكل المحاضرات في مستوى تاني أو أعمق، وجايين من فولدرين مختلفين على الأقل)
    /// </summary>
    public static bool LooksLikeMultiCourse(IReadOnlyList<LectureGroup> scanned)
    {
        char[] seps = { '\\', '/' };
        if (scanned.Count < 2 || !scanned.All(g => g.Name.IndexOfAny(seps) > 0)) return false;
        return scanned.Select(g => g.Name.Split(seps)[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2;
    }

    // ---------------- اكتشاف رقم الجزء (Part 01) ----------------

    private static readonly Regex PartRegex = new(
        @"(?:\b(?:part|lesson|lec|lecture|episode|ep|video|session)|الجزء|جزء|الدرس|درس|حلقة|الحلقة)\s*[-_.#]?\s*0*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>بيرجّع رقم الجزء من اسم الفيديو، مثلاً "13. Part 01 Object" ⇐ 1.</summary>
    public static int? PartNumber(string title)
    {
        var m = PartRegex.Match(title);
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null;
    }

    /// <summary>
    /// بيقسم لستة فيديوهات لمجموعات: مجموعة جديدة تبدأ كل ما رقم الجزء يرجع لورا
    /// (يعني Part 12 بعدها Part 01 = محاضرة جديدة).
    /// </summary>
    public static List<List<VideoItem>> SplitByPartReset(IReadOnlyList<VideoItem> videos)
    {
        var chunks = new List<List<VideoItem>>();
        var current = new List<VideoItem>();
        int? previous = null;

        foreach (var v in videos)
        {
            int? n = PartNumber(v.Title);
            if (current.Count > 0 && n.HasValue && previous.HasValue && n.Value <= previous.Value)
            {
                chunks.Add(current);
                current = new List<VideoItem>();
            }
            current.Add(v);
            if (n.HasValue) previous = n;
        }
        if (current.Count > 0) chunks.Add(current);
        return chunks;
    }
}

/// <summary>ترتيب طبيعي زي الويندوز: Part 2 قبل Part 10.</summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        x ??= ""; y ??= "";
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                string nx = x[si..i].TrimStart('0'), ny = y[sj..j].TrimStart('0');
                if (nx.Length != ny.Length) return nx.Length.CompareTo(ny.Length);
                int c = string.CompareOrdinal(nx, ny);
                if (c != 0) return c;
            }
            else
            {
                int si = i, sj = j;
                while (i < x.Length && !char.IsDigit(x[i])) i++;
                while (j < y.Length && !char.IsDigit(y[j])) j++;
                int c = string.Compare(x[si..i], y[sj..j], StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}

public static class TimeFormat
{
    public static string Format(long ms)
    {
        if (ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
