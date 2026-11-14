using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>سطر واحد من نص الفيديو بوقته.</summary>
public sealed record Cue(long StartMs, long EndMs, string Text);

/// <summary>نص الفيديو (تفريغ): من ترجمة يوتيوب، أو whisper على الجهاز، أو ملف ترجمة.</summary>
public sealed class Transcript
{
    public List<Cue> Cues { get; } = new();

    public bool IsEmpty => Cues.Count == 0;
    public long DurationMs => Cues.Count == 0 ? 0 : Cues.Max(c => c.EndMs);

    // ---------------- القراية ----------------

    private static readonly Regex TimeRegex = new(@"(?:(\d+):)?(\d{1,2}):(\d{2})[.,](\d{1,3})", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>بيقرا VTT أو SRT.</summary>
    public static Transcript Parse(string text)
    {
        var t = new Transcript();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int arrow = line.IndexOf("-->", StringComparison.Ordinal);
            if (arrow < 0) continue;
            if (!TryTime(line[..arrow], out long start) || !TryTime(line[(arrow + 3)..], out long end)) continue;

            var sb = new StringBuilder();
            for (i++; i < lines.Length && lines[i].Trim().Length > 0 && !lines[i].Contains("-->"); i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[i]);
            }
            i--; // السطر الفاضي/التوقيت الجاي يتقري في اللفة الجاية
            string body = CleanText(sb.ToString());
            if (body.Length > 0) t.Cues.Add(new Cue(start, Math.Max(start, end), body));
        }
        t.Cues.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        return t;
    }

    private static bool TryTime(string s, out long ms)
    {
        ms = 0;
        var m = TimeRegex.Match(s);
        if (!m.Success) return false;
        int h = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        int min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        int sec = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        string frac = m.Groups[4].Value.PadRight(3, '0');
        ms = ((h * 60L + min) * 60 + sec) * 1000 + int.Parse(frac, CultureInfo.InvariantCulture);
        return true;
    }

    private static string CleanText(string s)
    {
        s = TagRegex.Replace(s, "");
        s = System.Net.WebUtility.HtmlDecode(s);
        var lines = s.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// ترجمة يوتيوب التلقائية بتكرر كل سطر في الـ cue اللي بعده (سطور «بتتدحرج»).
    /// بنسيب كل سطر مرة واحدة بس عند أول ظهور ليه.
    /// </summary>
    public Transcript WithoutRollingDuplicates()
    {
        var t = new Transcript();
        string? last = null, prev = null;
        foreach (var c in Cues)
        {
            foreach (var raw in c.Text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line == last || line == prev) continue;
                t.Cues.Add(new Cue(c.StartMs, c.EndMs, line));
                prev = last;
                last = line;
            }
        }
        // نهاية كل سطر = بداية اللي بعده (عشان الترجمة متتراكبش)
        for (int i = 0; i < t.Cues.Count - 1; i++)
        {
            var c = t.Cues[i];
            long next = t.Cues[i + 1].StartMs;
            if (next > c.StartMs) t.Cues[i] = c with { EndMs = Math.Min(Math.Max(c.EndMs, c.StartMs + 500), next) };
        }
        return t;
    }

    // ---------------- الكتابة ----------------

    public static string Stamp(long ms, bool vtt = true)
    {
        var ts = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}{(vtt ? '.' : ',')}{ts.Milliseconds:000}";
    }

    public string ToVtt()
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var c in Cues)
            sb.Append(Stamp(c.StartMs)).Append(" --> ").Append(Stamp(c.EndMs)).Append('\n').Append(c.Text).Append("\n\n");
        return sb.ToString();
    }

    /// <summary>وقت مختصر: 4:05 أو 1:02:03.</summary>
    public static string Short(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}" : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    /// <summary>«4:05» أو «1:02:03» ← مللي ثانية.</summary>
    public static long? ParseShort(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Trim().Trim('[', ']').Split(':');
        if (parts.Length is < 2 or > 3) return null;
        long total = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0) return null;
            total = total * 60 + (long)v;
        }
        return total * 1000;
    }

    /// <summary>نص للذكاء الاصطناعي: سطر لكل ~20 ثانية وفي أوله الوقت.</summary>
    public string ToTimedText(int secondsPerLine = 20, int maxChars = 150_000)
    {
        var sb = new StringBuilder();
        long blockStart = -1;
        var block = new StringBuilder();
        void Flush()
        {
            if (block.Length == 0) return;
            sb.Append('[').Append(Short(blockStart)).Append("] ").Append(block.ToString().Trim()).Append('\n');
            block.Clear();
        }
        foreach (var c in Cues)
        {
            if (blockStart < 0 || c.StartMs - blockStart >= secondsPerLine * 1000L) { Flush(); blockStart = c.StartMs; }
            block.Append(c.Text.Replace('\n', ' ')).Append(' ');
            if (sb.Length > maxChars) break;
        }
        Flush();
        if (sb.Length > maxChars) { sb.Length = maxChars; sb.Append("\n[… النص اتقص لأنه طويل جدًا]"); }
        return sb.ToString();
    }

    public sealed record Chunk(long StartMs, long EndMs, string Text);

    /// <summary>تقسيم لأجزاء (~ عدد حروف) للبحث والسؤال.</summary>
    public List<Chunk> Chunks(int targetChars = 900)
    {
        var list = new List<Chunk>();
        var sb = new StringBuilder();
        long start = -1, end = 0;
        foreach (var c in Cues)
        {
            if (start < 0) start = c.StartMs;
            sb.Append(c.Text.Replace('\n', ' ')).Append(' ');
            end = c.EndMs;
            if (sb.Length >= targetChars)
            {
                list.Add(new Chunk(start, end, sb.ToString().Trim()));
                sb.Clear();
                start = -1;
            }
        }
        if (sb.Length > 0) list.Add(new Chunk(start, end, sb.ToString().Trim()));
        return list;
    }
}

/// <summary>
/// نصوص الفيديوهات متخزنة جنب ملف القائمة في فولدر <c>transcripts</c> (فبتتزامن مع الأجهزة التانية).
/// اسم الملف = بصمة مسار الفيديو.
/// </summary>
public static class TranscriptStore
{
    public static string Dir(string libraryPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(libraryPath)) ?? ".", "transcripts");

    public static string Key(VideoItem v) => Hash(v.Path.ToUpperInvariant());

    public static string Hash(string text) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

    public static string PathFor(string libraryPath, VideoItem v) => Path.Combine(Dir(libraryPath), Key(v) + ".vtt");

    public static bool Has(string libraryPath, VideoItem v) => File.Exists(PathFor(libraryPath, v));

    public static Transcript? Load(string libraryPath, VideoItem v)
    {
        string p = PathFor(libraryPath, v);
        if (!File.Exists(p)) return null;
        try { return Transcript.Parse(File.ReadAllText(p)); }
        catch (Exception ex) { Log.Error("transcript load", ex); return null; }
    }

    public static string Save(string libraryPath, VideoItem v, Transcript t)
    {
        string p = PathFor(libraryPath, v);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, t.ToVtt(), new UTF8Encoding(false));
        return p;
    }
}

/// <summary>بحث في نصوص كل الفيديوهات (في الذاكرة)، مع تطبيع العربي (أ إ آ ← ا، ة ← ه، ى ← ي، من غير تشكيل).</summary>
public sealed class TranscriptIndex
{
    public sealed record Hit(VideoItem Video, long TimeMs, string Snippet, double Score);
    private sealed record Entry(VideoItem Video, Transcript.Chunk Chunk, string Norm, Dictionary<string, int> Terms);

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, int> _docFreq = new(StringComparer.Ordinal);

    public int VideoCount { get; private set; }
    public int ChunkCount => _entries.Count;

    public void Add(VideoItem v, Transcript t, int chunkChars = 600)
    {
        VideoCount++;
        foreach (var ch in t.Chunks(chunkChars))
        {
            string norm = Normalize(ch.Text);
            var terms = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var w in Words(norm)) terms[w] = terms.GetValueOrDefault(w) + 1;
            foreach (var w in terms.Keys) _docFreq[w] = _docFreq.GetValueOrDefault(w) + 1;
            _entries.Add(new Entry(v, ch, norm, terms));
        }
    }

    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s.ToLowerInvariant())
        {
            switch (ch)
            {
                case 'أ': case 'إ': case 'آ': case 'ٱ': sb.Append('ا'); break;
                case 'ة': sb.Append('ه'); break;
                case 'ى': sb.Append('ي'); break;
                case 'ؤ': sb.Append('و'); break;
                case 'ئ': sb.Append('ي'); break;
                case 'ـ': break; // تطويل
                default:
                    if (ch >= 'ً' && ch <= 'ٟ' || ch == 'ٰ') break; // تشكيل
                    sb.Append(char.IsLetterOrDigit(ch) || ch == '#' || ch == '+' ? ch : ' ');
                    break;
            }
        }
        return sb.ToString();
    }

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "في", "من", "علي", "الي", "عن", "ان", "او", "و", "ده", "دي", "اللي", "هو", "هي", "ما", "لا", "مع", "كده", "يعني",
        "the", "a", "an", "of", "to", "in", "is", "and", "or", "it", "this", "that", "for", "on", "we", "you", "be"
    };

    public static IEnumerable<string> Words(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1 && !Stop.Contains(w));

    /// <summary>بحث بالكلمات: كل الكلمات لازم تكون موجودة (أو الجملة بالظبط بين علامات تنصيص).</summary>
    public List<Hit> Search(string query, int max = 200)
    {
        string q = Normalize(query).Trim();
        if (q.Length == 0) return new();
        var words = Words(q).ToList();
        if (words.Count == 0) words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var hits = new List<Hit>();
        foreach (var e in _entries)
        {
            if (!words.All(w => e.Norm.Contains(w, StringComparison.Ordinal))) continue;
            double score = Score(e, words) + (e.Norm.Contains(q, StringComparison.Ordinal) ? 5 : 0);
            hits.Add(new Hit(e.Video, e.Chunk.StartMs, Snippet(e.Chunk.Text, words), score));
        }
        return hits.OrderByDescending(h => h.Score).Take(max).ToList();
    }

    /// <summary>أنسب الأجزاء لسؤال (لـ «اسأل الكورس»): ترتيب BM25 مبسط من غير شرط كل الكلمات.</summary>
    public List<(VideoItem Video, Transcript.Chunk Chunk, double Score)> Retrieve(string question, int topK = 8)
    {
        var words = Words(Normalize(question)).Distinct().ToList();
        return _entries
            .Select(e => (e.Video, e.Chunk, Score: Score(e, words)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();
    }

    private double Score(Entry e, IReadOnlyList<string> words)
    {
        const double k1 = 1.4, b = 0.75;
        double avgLen = _entries.Count == 0 ? 1 : _entries.Average(x => x.Terms.Values.Sum());
        int len = e.Terms.Values.Sum();
        double score = 0;
        foreach (var w in words)
        {
            // تطابق جزئي (الكلمة جوه كلمة أطول: «الكلاس» ← «كلاس»)
            int tf = e.Terms.TryGetValue(w, out int exact) ? exact
                : e.Terms.Where(kv => kv.Key.Length > w.Length && kv.Key.Contains(w, StringComparison.Ordinal)).Sum(kv => kv.Value);
            if (tf == 0) continue;
            int df = Math.Max(1, _docFreq.GetValueOrDefault(w, 1));
            double idf = Math.Log(1 + (_entries.Count - df + 0.5) / (df + 0.5));
            score += idf * tf * (k1 + 1) / (tf + k1 * (1 - b + b * len / avgLen));
        }
        return score;
    }

    private static string Snippet(string text, IReadOnlyList<string> words, int radius = 70)
    {
        string norm = Normalize(text);
        int pos = words.Select(w => norm.IndexOf(w, StringComparison.Ordinal)).Where(p => p >= 0).DefaultIfEmpty(0).Min();
        int start = Math.Max(0, pos - radius), end = Math.Min(text.Length, pos + radius * 2);
        return (start > 0 ? "…" : "") + text[start..end].Trim() + (end < text.Length ? "…" : "");
    }
}
