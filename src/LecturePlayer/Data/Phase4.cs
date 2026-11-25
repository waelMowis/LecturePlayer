using System.Globalization;
using System.Net;
using System.Text;

namespace LecturePlayer.Data;

// =====================================================================
//  (2) تخطي السكوت
// =====================================================================

/// <summary>اكتشاف فترات السكوت بـ ffmpeg (silencedetect) وتخطيها وقت التشغيل.</summary>
public static class SilenceDetector
{
    public sealed record Range(long StartMs, long EndMs)
    {
        public long Length => EndMs - StartMs;
    }

    public static IReadOnlyList<string> Args(string file, int noiseDb = -35, double minSeconds = 0.8) => new[]
    {
        "-hide_banner", "-nostats", "-vn", "-i", file,
        "-af", string.Create(CultureInfo.InvariantCulture, $"silencedetect=noise={noiseDb}dB:d={minSeconds}"),
        "-f", "null", "-"
    };

    public static List<Range> Parse(string ffmpegOutput, long durationMs = 0)
    {
        var list = new List<Range>();
        double? start = null;
        foreach (var raw in ffmpegOutput.Split('\n'))
        {
            string line = raw.Trim();
            int i = line.IndexOf("silence_start:", StringComparison.Ordinal);
            if (i >= 0 && TryNum(line[(i + 14)..], out double s)) { start = Math.Max(0, s); continue; }
            i = line.IndexOf("silence_end:", StringComparison.Ordinal);
            if (i >= 0 && start != null && TryNum(line[(i + 12)..], out double e))
            {
                if (e > start) list.Add(new Range((long)(start.Value * 1000), (long)(e * 1000)));
                start = null;
            }
        }
        if (start != null && durationMs > start * 1000) list.Add(new Range((long)(start.Value * 1000), durationMs));
        return list;
    }

    private static bool TryNum(string s, out double v)
    {
        s = s.Trim();
        int end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] is '.' or '-' or 'e')) end++;
        return double.TryParse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>لو إحنا جوه سكوت طويل كفاية: نروح لآخره (مع هامش صغير عشان أول الكلام ميتقطعش).</summary>
    public static long? SkipTo(IReadOnlyList<Range> ranges, long posMs, long minMs = 1200, long marginMs = 250)
    {
        foreach (var r in ranges)
        {
            if (r.Length < minMs) continue;
            if (posMs >= r.StartMs + marginMs && posMs < r.EndMs - marginMs * 2) return r.EndMs - marginMs;
            if (r.StartMs > posMs) break;
        }
        return null;
    }

    public static string Serialize(IEnumerable<Range> ranges) =>
        string.Join("\n", ranges.Select(r => $"{r.StartMs},{r.EndMs}"));

    public static List<Range> Deserialize(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(','))
            .Where(p => p.Length == 2 && long.TryParse(p[0], out _) && long.TryParse(p[1], out _))
            .Select(p => new Range(long.Parse(p[0]), long.Parse(p[1])))
            .OrderBy(r => r.StartMs).ToList();

    public static async Task<List<Range>> DetectAsync(string file, long durationMs, CancellationToken ct)
    {
        string ffmpeg = Tools.FindFfmpeg() ?? throw new FileNotFoundException("لم يُنزَّل ffmpeg");
        var r = await ProcessRunner.RunAsync(ffmpeg, Args(file), TimeSpan.FromHours(1), ct);
        if (r.ExitCode != 0) throw new InvalidOperationException(ProcessRunner.LastErrorLine(r.StdErr, "فشل ffmpeg"));
        return Parse(r.StdErr, durationMs);
    }
}

// =====================================================================
//  (8) المراجعة المتباعدة من العلامات
// =====================================================================

public sealed record ReviewItem(Course Course, LectureGroup Group, VideoItem Video, Bookmark Bookmark);

/// <summary>مراجعة متباعدة: بعد يوم، 3، أسبوع، 3 أسابيع، شهرين. «مش فاكرها» ترجّعها لأول.</summary>
public static class ReviewScheduler
{
    public static readonly int[] IntervalsDays = { 1, 3, 7, 21, 60 };

    public static void Schedule(Bookmark b, DateTime nowUtc)
    {
        b.ReviewStage = 0;
        b.ReviewDueUtc = nowUtc.AddDays(IntervalsDays[0]);
    }

    public static void Unschedule(Bookmark b)
    {
        b.ReviewDueUtc = null;
        b.ReviewStage = 0;
    }

    public static void Grade(Bookmark b, bool remembered, DateTime nowUtc)
    {
        if (!remembered)
        {
            Schedule(b, nowUtc);
            return;
        }
        b.ReviewStage++;
        b.ReviewDueUtc = b.ReviewStage >= IntervalsDays.Length ? null : nowUtc.AddDays(IntervalsDays[b.ReviewStage]);
    }

    public static bool IsMastered(Bookmark b) => b.ReviewDueUtc == null && b.ReviewStage >= IntervalsDays.Length;

    public static IEnumerable<ReviewItem> All(CourseLibrary lib) =>
        from c in lib.Courses
        from g in c.Groups
        from v in g.Videos
        from b in v.Bookmarks
        where b.ReviewDueUtc != null || IsMastered(b)
        select new ReviewItem(c, g, v, b);

    public static List<ReviewItem> Due(CourseLibrary lib, DateTime nowUtc) =>
        All(lib).Where(i => i.Bookmark.ReviewDueUtc is { } d && d <= nowUtc).OrderBy(i => i.Bookmark.ReviewDueUtc).ToList();
}

// =====================================================================
//  (16) الأجزاء الصعبة (من الرجوع لورا)
// =====================================================================

public static class DifficultSpots
{
    public sealed record Spot(long StartMs, long EndMs, int Count);

    /// <summary>رجوع لورا بين 3 ثواني و3 دقايق = غالبًا معادة جزء مش واضح.</summary>
    public static bool IsRewind(long fromMs, long toMs) => fromMs - toMs is >= 3000 and <= 180_000;

    public static List<Spot> Detect(IEnumerable<JournalEntry> entries, string videoPath, int minCount = 2, long windowMs = 20_000)
    {
        var rewinds = entries.Where(e => e.Kind == JournalEntry.Rewind && string.Equals(e.VideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.ToMs).ToList();
        var spots = new List<Spot>();
        int i = 0;
        while (i < rewinds.Count)
        {
            long start = rewinds[i].ToMs, end = rewinds[i].FromMs;
            int j = i + 1;
            while (j < rewinds.Count && rewinds[j].ToMs - start <= windowMs) { end = Math.Max(end, rewinds[j].FromMs); j++; }
            int count = j - i;
            if (count >= minCount) spots.Add(new Spot(start, Math.Min(end, start + 120_000), count));
            i = j;
        }
        return spots;
    }
}

// =====================================================================
//  (11) مؤشر الفهم
// =====================================================================

public sealed record Understanding(int Score, int CoveragePct, int? QuizPct, int? ReviewPct, int DifficultSpots, string Level, string Advice);

public static class UnderstandingIndex
{
    /// <summary>
    /// المؤشر من 100: المشاهدة 30%، الاختبارات 50%، المراجعة 20%، وخصم لحد 10 للأجزاء الصعبة اللي ملهاش علامة مراجعة.
    /// من غير اختبارات المؤشر مبيعديش 60 (متقدرش تقول فاهم من غير ما تختبر نفسك).
    /// </summary>
    public static Understanding For(Course c, IEnumerable<JournalEntry> entries)
    {
        var list = entries as IReadOnlyCollection<JournalEntry> ?? entries.ToList();
        var paths = new HashSet<string>(c.AllVideos.Select(v => v.Path), StringComparer.OrdinalIgnoreCase);
        int coverage = c.VideoCount == 0 ? 0 : c.WatchedCount * 100 / c.VideoCount;

        // آخر اختبار لكل فيديو
        var quizzes = list.Where(e => e.Kind == JournalEntry.Quiz && e.Total > 0 && e.VideoPath != null && paths.Contains(e.VideoPath))
            .GroupBy(e => e.VideoPath!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(e => e.StartUtc).Last())
            .ToList();
        int? quiz = quizzes.Count == 0 ? null : (int)Math.Round(quizzes.Average(e => e.Score * 100.0 / e.Total));

        var reviewed = c.AllVideos.SelectMany(v => v.Bookmarks).Where(b => b.ReviewDueUtc != null || ReviewScheduler.IsMastered(b)).ToList();
        int? review = reviewed.Count == 0 ? null
            : (int)Math.Round(reviewed.Average(b => Math.Min(b.ReviewStage, ReviewScheduler.IntervalsDays.Length) * 100.0 / ReviewScheduler.IntervalsDays.Length));

        int unresolved = 0;
        foreach (var v in c.AllVideos)
            foreach (var s in DifficultSpots.Detect(list, v.Path))
                if (!v.Bookmarks.Any(b => b.ReviewDueUtc != null && b.TimeMs >= s.StartMs - 10_000 && b.TimeMs <= s.EndMs)) unresolved++;

        double score;
        if (quiz == null)
            score = Math.Min(60, coverage * 0.6 + (review ?? 0) * 0.2);
        else
        {
            double wQuiz = 0.5, wReview = 0.2;
            if (review == null) { wQuiz += wReview; wReview = 0; }
            score = coverage * 0.3 + quiz.Value * wQuiz + (review ?? 0) * wReview;
        }
        score -= Math.Min(10, unresolved * 2);
        int final = (int)Math.Round(Math.Clamp(score, 0, 100), MidpointRounding.AwayFromZero);

        string level = final < 40 ? "يحتاج إلى مراجعة" : final < 70 ? "جيد" : final < 85 ? "فاهم" : "متمكن";
        string advice = quiz == null ? "اختبر نفسك (Ctrl+Q) ليصبح المؤشر دقيقًا."
            : unresolved > 0 ? $"يوجد {unresolved} جزء صعب (عدت إليه عدة مرات) — ضع له علامة مراجعة."
            : quiz < 70 ? "راجع الفيديوهات التي أخطأت في أسئلتها."
            : coverage < 100 ? "أكمل باقي الفيديوهات."
            : "ممتاز — استمر في المراجعة المتباعدة.";
        return new Understanding(final, coverage, quiz, review, unresolved, level, advice);
    }
}

// =====================================================================
//  (9) وضع التركيز الصارم: حجب المواقع من ملف hosts
// =====================================================================

public static class FocusBlocker
{
    public const string BeginMarker = "# >>> LecturePlayer focus (auto) >>>";
    public const string EndMarker = "# <<< LecturePlayer focus (auto) <<<";

    public static readonly string[] DefaultSites =
        { "facebook.com", "instagram.com", "tiktok.com", "x.com", "twitter.com", "reddit.com", "netflix.com" };

    public static string HostsPath => Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");

    public static List<string> NormalizeSites(IEnumerable<string> sites)
    {
        var set = new List<string>();
        foreach (var raw in sites)
        {
            string s = raw.Trim().ToLowerInvariant();
            if (s.Length == 0 || s.StartsWith('#')) continue;
            if (s.Contains("://")) s = s[(s.IndexOf("://", StringComparison.Ordinal) + 3)..];
            s = s.Split('/', '?', '#', ':')[0].Trim('.');
            if (s.StartsWith("www.")) s = s[4..];
            if (s.Length == 0 || !s.Contains('.') || s.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '-'))) continue;
            foreach (var host in new[] { s, "www." + s })
                if (!set.Contains(host)) set.Add(host);
        }
        return set;
    }

    public static bool IsBlocked(string hosts) => hosts.Contains(BeginMarker, StringComparison.Ordinal);

    public static string RemoveBlock(string hosts)
    {
        int b = hosts.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (b < 0) return hosts;
        int e = hosts.IndexOf(EndMarker, b, StringComparison.Ordinal);
        string after = e < 0 ? "" : hosts[(e + EndMarker.Length)..].TrimStart('\r', '\n');
        string before = hosts[..b].TrimEnd('\r', '\n', ' ');
        return before + (before.Length > 0 ? "\r\n" : "") + after;
    }

    public static string AddBlock(string hosts, IEnumerable<string> sites)
    {
        var list = NormalizeSites(sites);
        string clean = RemoveBlock(hosts).TrimEnd('\r', '\n');
        if (list.Count == 0) return clean + "\r\n";
        var sb = new StringBuilder(clean);
        if (clean.Length > 0) sb.Append("\r\n\r\n");
        sb.Append(BeginMarker).Append("\r\n");
        foreach (var s in list) sb.Append("0.0.0.0 ").Append(s).Append("\r\n");
        sb.Append(EndMarker).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>بيتنادى من البرنامج وهو شغال كمسؤول (--focus-block / --focus-unblock).</summary>
    public static int Apply(bool block, IEnumerable<string> sites)
    {
        try
        {
            string hosts = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : "";
            string updated = block ? AddBlock(hosts, sites) : RemoveBlock(hosts);
            if (updated != hosts) File.WriteAllText(HostsPath, updated, new UTF8Encoding(false));
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ipconfig", "/flushdns")
                { CreateNoWindow = true, UseShellExecute = false });
                p?.WaitForExit(5000);
            }
            catch { }
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("focus hosts", ex);
            return 2;
        }
    }
}

// =====================================================================
//  (6) معمل الكود
// =====================================================================

public enum CodeLang { CSharp, Python, JavaScript }

public static class CodeRunner
{
    public static CodeLang Detect(string code)
    {
        string c = code;
        int py = 0, js = 0, cs = 0;
        if (c.Contains("def ") || c.Contains("import ") && !c.Contains(';')) py += 2;
        if (c.Contains("print(") && !c.Contains(';')) py += 2;
        if (c.Contains("elif ") || c.Contains("self.")) py += 2;
        if (c.Contains("console.log")) js += 3;
        if (c.Contains("function ") || c.Contains("const ") || c.Contains("let ") || c.Contains("=>") && c.Contains("const")) js += 2;
        if (c.Contains("Console.Write") || c.Contains("using System") || c.Contains("namespace ") || c.Contains("public class")) cs += 3;
        if (c.Contains("var ") && c.Contains(';')) cs += 1;
        if (c.Contains("string ") || c.Contains("int ") || c.Contains("void ")) cs += 1;
        return py > js && py > cs ? CodeLang.Python : js > cs ? CodeLang.JavaScript : CodeLang.CSharp;
    }

    public static string Extension(CodeLang l) => l switch { CodeLang.Python => ".py", CodeLang.JavaScript => ".js", _ => ".cs" };

    public static string Label(CodeLang l) => l switch { CodeLang.Python => "Python", CodeLang.JavaScript => "JavaScript", _ => "C#" };

    /// <summary>الأمر اللي بيشغّل الملف. C#: <c>dotnet run file.cs</c> (محتاج .NET 10 SDK).</summary>
    public static (string Exe, string[] Args) Command(CodeLang lang, string file) => lang switch
    {
        CodeLang.Python => (OperatingSystem.IsWindows() ? "py" : "python3", new[] { file }),
        CodeLang.JavaScript => ("node", new[] { file }),
        _ => (DotnetExe(), new[] { "run", file })
    };

    private static string DotnetExe()
    {
        string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return !string.IsNullOrEmpty(host) && File.Exists(host) ? host : "dotnet";
    }

    /// <summary>
    /// C#: ملفات «dotnet run file.cs» بتتبني AOT افتراضيًا (وده بينزّل باكدج كبيرة أول مرة ومش لازم هنا)،
    /// فبنقفله لو الكود مش محدد.
    /// </summary>
    public static string Prepare(CodeLang lang, string code)
    {
        if (lang != CodeLang.CSharp || code.Contains("#:property PublishAot", StringComparison.Ordinal)) return code;
        return "#:property PublishAot=false\n" + code;
    }

    public static async Task<ProcessRunner.Result> RunAsync(CodeLang lang, string code, string workDir, TimeSpan timeout,
        Action<string>? onLine, CancellationToken ct)
    {
        Directory.CreateDirectory(workDir);
        string file = Path.Combine(workDir, "Program" + Extension(lang));
        await File.WriteAllTextAsync(file, Prepare(lang, code), new UTF8Encoding(false), ct);
        var (exe, args) = Command(lang, file);
        var env = new Dictionary<string, string> { ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1", ["PYTHONIOENCODING"] = "utf-8" };
        try { return await ProcessRunner.RunAsync(exe, args, timeout, ct, onLine, workDir, env); }
        catch (System.ComponentModel.Win32Exception)
        {
            string need = lang switch { CodeLang.Python => "Python", CodeLang.JavaScript => "Node.js", _ => ".NET 10 SDK" };
            throw new FileNotFoundException($"لتشغيل {Label(lang)} يجب أن يكون {need} مثبّتًا على الجهاز.");
        }
    }
}

// =====================================================================
//  (15) «اللي فات» لما ترجع لكورس بعد غياب
// =====================================================================

public static class Recap
{
    public static int DaysAway(Course c, DateTime nowUtc) =>
        c.LastActivityUtc == default ? 0 : (int)(nowUtc - c.LastActivityUtc).TotalDays;

    public static bool ShouldOffer(Course c, DateTime nowUtc, int minDays = 3) =>
        c.WatchedCount > 0 && !c.IsCompleted && DaysAway(c, nowUtc) >= minDays;

    /// <summary>آخر فيديوهات اتشافت (أو اتبدأت) في الكورس.</summary>
    public static List<VideoItem> RecentlyWatched(Course c, int n = 3) =>
        c.AllVideos.Where(v => v.Watched || v.LastPositionMs > 0).OrderByDescending(v => v.UpdatedUtc).Take(n).ToList();

    public static string LocalRecap(Course c, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        int days = DaysAway(c, nowUtc);
        sb.AppendLine($"# «{c.Name}» — ما سبق");
        sb.AppendLine();
        sb.AppendLine($"آخر مرة ذاكرت هذه الدورة منذ {days} يوم. أكملت {c.WatchedCount} من {c.VideoCount} فيديو.");
        if (!string.IsNullOrWhiteSpace(c.Goal)) sb.AppendLine($"هدفك: {c.Goal}");
        sb.AppendLine();
        foreach (var v in RecentlyWatched(c).AsEnumerable().Reverse())
        {
            sb.AppendLine($"## {v.Title}" + (v.Watched ? " ✓" : $" (توقفت عند {Transcript.Short(v.LastPositionMs)})"));
            foreach (var b in v.Bookmarks.OrderBy(b => b.TimeMs))
                sb.AppendLine($"- [{Transcript.Short(b.TimeMs)}] {(string.IsNullOrWhiteSpace(b.Note) ? "علامة" : b.Note)}");
            if (!string.IsNullOrWhiteSpace(v.Notes))
            {
                sb.AppendLine();
                sb.AppendLine(v.Notes.Trim());
            }
            sb.AppendLine();
        }
        var next = StudyPlanner.NextVideo(c);
        if (next != null) sb.AppendLine($"**التالي:** {next.Title}");
        return sb.ToString();
    }

    public static (string System, string User, int MaxTokens) Prompt(Course c, IReadOnlyList<(VideoItem Video, Transcript? Transcript)> recent)
    {
        var sb = new StringBuilder();
        foreach (var (v, t) in recent)
        {
            sb.Append($"<video title=\"{v.Title}\">\n");
            if (!string.IsNullOrWhiteSpace(v.Notes)) sb.Append("<my_notes>\n").Append(v.Notes.Trim()).Append("\n</my_notes>\n");
            if (t != null) sb.Append("<transcript>\n").Append(t.ToTimedText(30, 20_000)).Append("\n</transcript>\n");
            sb.Append("</video>\n");
        }
        return (
            "أنت مساعد مذاكرة. الطالب عاد إلى دورة بعد غياب ويريد أن يتذكر بسرعة أين توقف. " +
            "اكتب بالعربية الفصحى المبسّطة، والمصطلحات التقنية بالإنجليزية. لا تخترع شيئًا غير موجود في النص.",
            $"الدورة: «{c.Name}». هذه آخر فيديوهات شاهدها (الأقدم أولًا):\n\n{sb}\n" +
            "اكتب «ما سبق» في أقل من 250 كلمة: أهم 5 أفكار شُرحت، وأي كود أو مصطلح يجب أن يتذكره، " +
            "وفي النهاية سؤالان سريعان يختبر بهما نفسه قبل أن يكمل.",
            900);
    }
}

// =====================================================================
//  (12) صفحة الإنجازات
// =====================================================================

public static class AchievementPage
{
    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string BuildHtml(CourseLibrary lib, IReadOnlyList<JournalEntry> entries, int goalMinutes, DateOnly today, string ownerName = "")
    {
        var daily = StudyStats.DailyMinutes(entries);
        var streak = StudyStats.Streak(daily, today);
        double total = daily.Values.Sum();
        int videosDone = lib.WatchedCount;
        var done = lib.Courses.Where(c => c.IsCompleted || c.Status == CourseStatus.Done).ToList();
        int quizzes = entries.Count(e => e.Kind == JournalEntry.Quiz);
        int mastered = lib.AllVideos.SelectMany(v => v.Bookmarks).Count(ReviewScheduler.IsMastered);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>إنجازاتي — {H(lib.Name)}</title><style>");
        sb.Append("body{font-family:'Segoe UI',Tahoma,sans-serif;background:#16181d;color:#e6e6e6;margin:0;padding:32px;line-height:1.6}");
        sb.Append("h1{margin:0 0 4px;font-size:30px}h2{margin:32px 0 12px;color:#8ab4ff}.muted{color:#9aa0a6}");
        sb.Append(".kpis{display:flex;flex-wrap:wrap;gap:12px;margin-top:20px}.kpi{background:#20232b;border-radius:12px;padding:14px 18px;min-width:150px}");
        sb.Append(".kpi b{display:block;font-size:26px;color:#fff}.cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:14px}");
        sb.Append(".card{background:#20232b;border-radius:12px;padding:16px}.card h3{margin:0 0 6px;font-size:17px}");
        sb.Append(".bar{height:8px;background:#2c3039;border-radius:4px;overflow:hidden;margin:8px 0}.bar i{display:block;height:100%;background:#4f8cff}");
        sb.Append(".cert{border:2px solid #d4af37;background:linear-gradient(135deg,#232018,#1b1d22)}.cert h3{color:#f1d27a}");
        sb.Append(".tag{display:inline-block;background:#2c3039;border-radius:10px;padding:0 8px;margin-left:4px;font-size:12px}");
        sb.Append("svg{direction:ltr}@media print{body{background:#fff;color:#000}.kpi,.card{background:#f3f3f3}}</style></head><body>");

        sb.Append($"<h1>🎓 إنجازاتي{(ownerName.Length > 0 ? " — " + H(ownerName) : "")}</h1>");
        sb.Append($"<div class=\"muted\">{H(lib.Name)} · حتى {today:yyyy/MM/dd}</div>");
        sb.Append("<div class=\"kpis\">");
        Kpi(sb, StudyStats.FormatMinutes(total), "وقت مذاكرة فعلي");
        Kpi(sb, videosDone.ToString(), "فيديو مكتمل");
        Kpi(sb, done.Count.ToString(), "دورة مكتملة");
        Kpi(sb, $"{streak.Best} يوم", "أطول سلسلة");
        Kpi(sb, $"{streak.Current} يوم", "السلسلة الحالية");
        Kpi(sb, quizzes.ToString(), "اختبار");
        Kpi(sb, mastered.ToString(), "فكرة ثبتت بالمراجعة");
        sb.Append("</div>");

        sb.Append("<h2>آخر 12 أسبوعًا</h2>").Append(HeatmapSvg(daily, today, goalMinutes));

        if (done.Count > 0)
        {
            sb.Append("<h2>شهادات الدورات المكتملة</h2><div class=\"cards\">");
            foreach (var c in done)
            {
                var u = UnderstandingIndex.For(c, entries);
                double mins = StudyStats.DailyMinutes(entries, c.Id).Values.Sum();
                sb.Append("<div class=\"card cert\">");
                sb.Append($"<h3>🏅 {H(c.Name)}</h3>");
                if (!string.IsNullOrWhiteSpace(c.Instructor)) sb.Append($"<div class=\"muted\">المحاضر: {H(c.Instructor)}</div>");
                sb.Append($"<div>{c.VideoCount} فيديو · {TimeFormat.Format(c.TotalDurationMs)} محتوى · {StudyStats.FormatMinutes(mins)} مذاكرة</div>");
                sb.Append($"<div>مؤشر الفهم: <b>{u.Score}</b>/100 ({H(u.Level)})</div>");
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }

        var active = lib.Courses.Where(c => !done.Contains(c) && c.VideoCount > 0).ToList();
        if (active.Count > 0)
        {
            sb.Append("<h2>قيد الدراسة</h2><div class=\"cards\">");
            foreach (var c in active.OrderByDescending(c => c.LastActivityUtc))
            {
                int pct = c.WatchedCount * 100 / Math.Max(1, c.VideoCount);
                var u = UnderstandingIndex.For(c, entries);
                sb.Append("<div class=\"card\">");
                sb.Append($"<h3>{H(c.Name)}</h3>");
                foreach (var t in c.Tags) sb.Append($"<span class=\"tag\">#{H(t)}</span>");
                sb.Append($"<div class=\"bar\"><i style=\"width:{pct}%\"></i></div>");
                sb.Append($"<div>{c.WatchedCount} من {c.VideoCount} فيديو ({pct}%) · مؤشر الفهم {u.Score}/100</div>");
                if (!string.IsNullOrWhiteSpace(c.Goal)) sb.Append($"<div class=\"muted\">الهدف: {H(c.Goal)}</div>");
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }

        sb.Append("<p class=\"muted\" style=\"margin-top:40px\">أُنشئت بواسطة «مسار».</p></body></html>");
        return sb.ToString();
    }

    private static void Kpi(StringBuilder sb, string value, string label) =>
        sb.Append($"<div class=\"kpi\"><b>{H(value)}</b><span class=\"muted\">{H(label)}</span></div>");

    public static string HeatmapSvg(IReadOnlyDictionary<DateOnly, double> daily, DateOnly today, int goal)
    {
        var cells = StudyStats.Heatmap(daily, today, 12);
        string[] colors = { "#2c3039", "#24452f", "#2f6b41", "#3d9156", "#4fc16e" };
        const int size = 14, gap = 3;
        var sb = new StringBuilder($"<svg width=\"{12 * (size + gap)}\" height=\"{7 * (size + gap)}\" role=\"img\" aria-label=\"heatmap\">");
        foreach (var c in cells)
            sb.Append($"<rect x=\"{c.Week * (size + gap)}\" y=\"{c.Day * (size + gap)}\" width=\"{size}\" height=\"{size}\" rx=\"3\" fill=\"{colors[StudyStats.HeatLevel(c.Minutes, goal)]}\"><title>{c.Date:yyyy/MM/dd}: {Math.Round(c.Minutes)} د</title></rect>");
        return sb.Append("</svg>").ToString();
    }
}
