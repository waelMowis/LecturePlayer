using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LecturePlayer.Data;

/// <summary>
/// سطر واحد في سجل المذاكرة. أنواعه:
/// • study   — جلسة مذاكرة (وقت فعلي اتفرجت فيه).
/// • watched — فيديو خلص.
/// • rewind  — رجعت لورا في فيديو (لاكتشاف الأجزاء الصعبة).
/// • quiz    — نتيجة اختبار (لمؤشر الفهم).
/// • review  — مراجعة علامة (المراجعة المتباعدة).
/// </summary>
public sealed class JournalEntry
{
    public const string Study = "study", Watched = "watched", Rewind = "rewind", Quiz = "quiz", Review = "review";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = Study;
    public DateTime StartUtc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public DateTime EndUtc { get; set; }

    /// <summary>للجلسات: الثواني الفعلية اللي الفيديو كان شغال فيها.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Seconds { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CourseId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? GroupId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? VideoPath { get; set; }

    /// <summary>للرجوع لورا: من فين لفين (مللي ثانية).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long FromMs { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long ToMs { get; set; }

    /// <summary>للاختبارات: الدرجة من الإجمالي.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Score { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Total { get; set; }

    public string Device { get; set; } = "";
}

/// <summary>
/// سجل المذاكرة: ملف JSONL لكل جهاز جنب ملف القائمة
/// (<c>مكتبتي.sessions.LAPTOP.jsonl</c>). كل جهاز بيكتب في ملفه بس، فمفيش تعارض مع OneDrive،
/// والقراية بتجمع ملفات كل الأجهزة.
/// </summary>
public sealed class StudyJournal
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    private readonly object _lock = new();
    private List<JournalEntry>? _cache;
    private DateTime _cacheStamp;

    public string LibraryPath { get; }
    public string Device { get; }

    public StudyJournal(string libraryPath, string? device = null)
    {
        LibraryPath = Path.GetFullPath(libraryPath);
        Device = string.IsNullOrWhiteSpace(device) ? Storage.DeviceName : device;
    }

    private string Dir => Path.GetDirectoryName(LibraryPath) ?? ".";
    private string BaseName => Path.GetFileNameWithoutExtension(LibraryPath);

    public string OwnFile => Path.Combine(Dir, $"{BaseName}.sessions.{SafeName(Device)}.jsonl");

    public IEnumerable<string> AllFiles =>
        Directory.Exists(Dir) ? Directory.EnumerateFiles(Dir, $"{BaseName}.sessions.*.jsonl") : Enumerable.Empty<string>();

    public static string SafeName(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
        return sb.Length == 0 ? "device" : sb.ToString();
    }

    public void Append(JournalEntry e)
    {
        if (string.IsNullOrEmpty(e.Device)) e.Device = Device;
        string line = JsonSerializer.Serialize(e, Json) + "\n";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(OwnFile, line, new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Error("journal append", ex); }
            _cache?.Add(e);
        }
    }

    /// <summary>كل السجلات من كل الأجهزة (بتتقري من الديسك لو حاجة اتغيرت).</summary>
    public IReadOnlyList<JournalEntry> LoadAll()
    {
        lock (_lock)
        {
            var files = AllFiles.ToList();
            DateTime stamp = files.Select(f => File.GetLastWriteTimeUtc(f)).DefaultIfEmpty().Max();
            stamp = stamp.AddTicks(files.Count); // عشان لو ملف اتضاف بنفس الوقت
            if (_cache != null && stamp == _cacheStamp) return _cache;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var all = new List<JournalEntry>();
            foreach (var f in files)
            {
                string[] lines;
                try { lines = ReadAllLinesShared(f); }
                catch (Exception ex) { Log.Error("journal read " + f, ex); continue; }
                foreach (var raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    try
                    {
                        var e = JsonSerializer.Deserialize<JournalEntry>(raw, Json);
                        if (e != null && seen.Add(e.Id)) all.Add(e);
                    }
                    catch { /* سطر ناقص (الملف بيتكتب أو المزامنة لسه مخلصتش) */ }
                }
            }
            all.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            _cache = all;
            _cacheStamp = stamp;
            return all;
        }
    }

    private static string[] ReadAllLinesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        return sr.ReadToEnd().Split('\n');
    }
}

/// <summary>
/// بيجمع وقت المذاكرة الفعلي وإنت بتتفرج: كل ثانية الفيديو شغال فيها بتتحسب،
/// والجلسة بتتقفل لما الفيديو يتغير أو يقف أكتر من 5 دقايق أو البرنامج يتقفل.
/// </summary>
public sealed class SessionTracker
{
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(5);
    public const int MinSeconds = 30;

    private JournalEntry? _current;
    private DateTime _lastActiveUtc;
    private double _activeSeconds;

    public JournalEntry? Current => _current;
    public double ActiveSeconds => _activeSeconds;

    /// <summary>بتتنادى كل شوية. playing = الفيديو شغال دلوقتي.</summary>
    /// <returns>جلسة خلصت ومحتاجة تتكتب، أو null.</returns>
    public JournalEntry? Tick(DateTime nowUtc, bool playing, string? courseId, string? groupId, string? videoPath, double elapsedSeconds)
    {
        JournalEntry? finished = null;

        if (_current != null && (_current.VideoPath != videoPath || nowUtc - _lastActiveUtc > IdleLimit))
            finished = Close(_current.VideoPath != videoPath ? nowUtc : _lastActiveUtc);

        if (!playing || videoPath == null) return finished;

        if (_current == null)
        {
            _current = new JournalEntry
            {
                Kind = JournalEntry.Study, StartUtc = nowUtc, CourseId = courseId, GroupId = groupId, VideoPath = videoPath
            };
            _activeSeconds = 0;
        }
        else
        {
            // لو الـ Tick اتأخر (الجهاز نام مثلًا) منحسبش الوقت ده
            _activeSeconds += Math.Clamp(elapsedSeconds, 0, 5);
        }
        _lastActiveUtc = nowUtc;
        return finished;
    }

    /// <summary>قفل الجلسة الحالية (لما البرنامج يتقفل).</summary>
    public JournalEntry? Flush(DateTime nowUtc) => _current == null ? null : Close(_lastActiveUtc == default ? nowUtc : _lastActiveUtc);

    private JournalEntry? Close(DateTime endUtc)
    {
        var e = _current!;
        _current = null;
        int secs = (int)Math.Round(_activeSeconds);
        _activeSeconds = 0;
        if (secs < MinSeconds) return null;
        e.EndUtc = endUtc;
        e.Seconds = secs;
        return e;
    }
}
