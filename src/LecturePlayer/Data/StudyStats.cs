using System.Globalization;
using System.Text;

namespace LecturePlayer.Data;

/// <summary>إحصائيات المذاكرة: الدقايق اليومية، السلسلة (Streak)، الخريطة الحرارية، التوقعات، والتقرير الأسبوعي.</summary>
public static class StudyStats
{
    /// <summary>الأسبوع بيبدأ السبت.</summary>
    public const DayOfWeek WeekStart = DayOfWeek.Saturday;

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo? tz = null) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz ?? TimeZoneInfo.Local));

    public static DateOnly StartOfWeek(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek - (int)WeekStart + 7) % 7));

    /// <summary>دقايق المذاكرة لكل يوم (بالتوقيت المحلي). ممكن تتفلتر بكورس.</summary>
    public static SortedDictionary<DateOnly, double> DailyMinutes(IEnumerable<JournalEntry> entries, string? courseId = null, TimeZoneInfo? tz = null)
    {
        var map = new SortedDictionary<DateOnly, double>();
        foreach (var e in entries)
        {
            if (e.Kind != JournalEntry.Study || e.Seconds <= 0) continue;
            if (courseId != null && e.CourseId != courseId) continue;
            var d = LocalDate(e.StartUtc, tz);
            map[d] = map.GetValueOrDefault(d) + e.Seconds / 60.0;
        }
        return map;
    }

    public static double MinutesOn(IReadOnlyDictionary<DateOnly, double> daily, DateOnly d) => daily.TryGetValue(d, out var m) ? m : 0;

    public sealed record StreakInfo(int Current, int Best, int FreezesUsed, bool TodayDone, IReadOnlySet<DateOnly> FrozenDays);

    /// <summary>
    /// السلسلة: كام يوم ورا بعض حققت فيهم الحد الأدنى. مسموح «تجميد» يوم واحد في الأسبوع
    /// (يوم فاتك من غير ما السلسلة تتقطع). النهارده لو لسه مخلصتش مش بيقطع السلسلة.
    /// </summary>
    public static StreakInfo Streak(IReadOnlyDictionary<DateOnly, double> daily, DateOnly today, double minMinutes = 10, int freezesPerWeek = 1)
    {
        var frozen = new HashSet<DateOnly>();
        if (daily.Count == 0) return new StreakInfo(0, 0, 0, false, frozen);

        DateOnly first = daily.Keys.Min();
        if (first > today) first = today;
        int running = 0, best = 0;
        var freezesByWeek = new Dictionary<DateOnly, int>();
        var runFrozen = new List<DateOnly>();
        bool todayDone = MinutesOn(daily, today) >= minMinutes;

        for (var d = first; d <= today; d = d.AddDays(1))
        {
            bool met = MinutesOn(daily, d) >= minMinutes;
            if (met)
            {
                running++;
                best = Math.Max(best, running);
                continue;
            }
            if (d == today) break; // النهارده لسه فيه وقت

            var wk = StartOfWeek(d);
            int used = freezesByWeek.GetValueOrDefault(wk);
            if (running > 0 && used < freezesPerWeek)
            {
                freezesByWeek[wk] = used + 1;
                runFrozen.Add(d);
                continue; // يوم متجمد: مبيزودش العدد ومبيقطعش
            }
            running = 0;
            runFrozen.Clear();
        }

        // التجميد بيتحسب بس لو السلسلة الحالية لسه شغالة (مش يوم في آخرها من غير مذاكرة بعده)
        foreach (var f in runFrozen) frozen.Add(f);
        return new StreakInfo(running, best, frozen.Count, todayDone, frozen);
    }

    public sealed record HeatCell(DateOnly Date, int Week, int Day, double Minutes);

    /// <summary>خريطة حرارية: آخر N أسبوع، كل عمود أسبوع (من السبت للجمعة).</summary>
    public static List<HeatCell> Heatmap(IReadOnlyDictionary<DateOnly, double> daily, DateOnly today, int weeks = 12)
    {
        var cells = new List<HeatCell>();
        DateOnly start = StartOfWeek(today).AddDays(-7 * (weeks - 1));
        for (int w = 0; w < weeks; w++)
            for (int d = 0; d < 7; d++)
            {
                var date = start.AddDays(w * 7 + d);
                if (date > today) continue;
                cells.Add(new HeatCell(date, w, d, MinutesOn(daily, date)));
            }
        return cells;
    }

    /// <summary>مستوى اللون (0..4) بالنسبة للهدف اليومي.</summary>
    public static int HeatLevel(double minutes, double goal)
    {
        if (minutes <= 0) return 0;
        if (goal <= 0) goal = 60;
        double r = minutes / goal;
        return r < 0.34 ? 1 : r < 0.67 ? 2 : r < 1 ? 3 : 4;
    }

    /// <summary>الوقت الفعلي المتبقي للكورس بالدقايق (بسرعة الكورس).</summary>
    public static double RemainingMinutes(Course c, float defaultRate = 1f)
    {
        float rate = c.Rate ?? defaultRate;
        if (rate <= 0.1f) rate = 1f;
        return c.RemainingMs / 60000.0 / rate;
    }

    public sealed record ForecastInfo(double RemainingMinutes, double AvgMinutesPerDay, DateOnly? FinishDate, int? DaysLeft);

    /// <summary>هتخلص الكورس إمتى لو كملت بنفس معدل آخر N يوم.</summary>
    public static ForecastInfo Forecast(Course c, IEnumerable<JournalEntry> entries, DateOnly today, int lookbackDays = 14, float defaultRate = 1f)
    {
        double remaining = RemainingMinutes(c, defaultRate);
        var daily = DailyMinutes(entries, c.Id);
        DateOnly from = today.AddDays(-(lookbackDays - 1));
        double sum = daily.Where(kv => kv.Key >= from && kv.Key <= today).Sum(kv => kv.Value);
        double avg = sum / lookbackDays;
        if (remaining <= 0.01) return new ForecastInfo(0, avg, today, 0);
        if (avg < 0.5) return new ForecastInfo(remaining, avg, null, null);
        int days = (int)Math.Ceiling(remaining / avg);
        return new ForecastInfo(remaining, avg, today.AddDays(days), days);
    }

    /// <summary>تقرير أسبوعي Markdown.</summary>
    public static string WeeklyReport(CourseLibrary lib, IEnumerable<JournalEntry> entries, DateOnly anyDayInWeek, int goalMinutes, DateOnly? today = null)
    {
        var list = entries.ToList();
        DateOnly ws = StartOfWeek(anyDayInWeek), we = ws.AddDays(6);
        DateOnly now = today ?? DateOnly.FromDateTime(DateTime.Now);
        var daily = DailyMinutes(list);
        var prevTotal = Enumerable.Range(0, 7).Sum(i => MinutesOn(daily, ws.AddDays(i - 7)));
        double total = 0; int daysStudied = 0, goalDays = 0;
        var sb = new StringBuilder();
        var ar = CultureInfo.GetCultureInfo("ar-EG");

        sb.AppendLine($"# تقرير المذاكرة: {ws:yyyy-MM-dd} → {we:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("| اليوم | الدقايق | الهدف |");
        sb.AppendLine("|---|---:|:---:|");
        for (int i = 0; i < 7; i++)
        {
            var d = ws.AddDays(i);
            double m = MinutesOn(daily, d);
            total += m;
            if (m > 0) daysStudied++;
            if (m >= goalMinutes) goalDays++;
            string mark = d > now ? "" : m >= goalMinutes ? "✓" : "✗";
            sb.AppendLine($"| {ar.DateTimeFormat.GetDayName(d.DayOfWeek)} {d:MM-dd} | {m:0} | {mark} |");
        }
        sb.AppendLine();
        sb.AppendLine($"- **الإجمالي:** {FormatMinutes(total)}" + (prevTotal > 0 ? $" ({(total >= prevTotal ? "+" : "")}{(total - prevTotal) / prevTotal * 100:0}% عن الأسبوع اللي فات)" : ""));
        sb.AppendLine($"- **أيام مذاكرة:** {daysStudied} من 7 — حققت الهدف ({goalMinutes} دقيقة) في {goalDays} يوم");

        var inWeek = list.Where(e => { var d = LocalDate(e.StartUtc); return d >= ws && d <= we; }).ToList();
        int finished = inWeek.Count(e => e.Kind == JournalEntry.Watched);
        sb.AppendLine($"- **فيديوهات خلصت:** {finished}");
        var streak = Streak(daily, we < now ? we : now);
        sb.AppendLine($"- **السلسلة:** {streak.Current} يوم (أحسن سلسلة {streak.Best})");
        sb.AppendLine();

        if (lib.Topics.Count > 0)
        {
            sb.AppendLine("## المواضيع");
            sb.AppendLine();
            sb.AppendLine("| الموضوع | وقت الأسبوع | التقدم | التوقع |");
            sb.AppendLine("|---|---:|---:|---|");
            foreach (var t in TopicStats.AllWithPlaces(lib, list, we < now ? we : now))
            {
                string fc = t.Videos > 0 && t.Watched == t.Videos ? "خلص" : t.FinishDate is { } fd ? $"{fd:yyyy-MM-dd}" : "مفيش معدل كفاية";
                sb.AppendLine($"| {Escape(t.Name)} | {FormatMinutes(t.WeekMinutes)} | {t.Percent}% | {fc} |");
            }
            sb.AppendLine();
        }

        var perCourse = inWeek.Where(e => e.Kind == JournalEntry.Study && e.CourseId != null)
            .GroupBy(e => e.CourseId!).Select(g => (Id: g.Key, Min: g.Sum(e => e.Seconds) / 60.0))
            .OrderByDescending(x => x.Min).ToList();
        if (perCourse.Count > 0)
        {
            sb.AppendLine("## الكورسات");
            sb.AppendLine();
            sb.AppendLine("| الكورس | الوقت | التقدم | التوقع |");
            sb.AppendLine("|---|---:|---:|---|");
            foreach (var (id, min) in perCourse)
            {
                var c = lib.Courses.FirstOrDefault(x => x.Id == id);
                if (c == null) continue;
                var f = Forecast(c, list, now);
                string pct = c.VideoCount == 0 ? "-" : $"{c.WatchedCount * 100 / c.VideoCount}%";
                string fc = f.DaysLeft == 0 ? "خلص" : f.FinishDate is { } fd ? $"{fd:yyyy-MM-dd}" : "مفيش معدل كفاية";
                sb.AppendLine($"| {Escape(c.Name)} | {FormatMinutes(min)} | {pct} | {fc} |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("|", "\\|");

    public static string FormatMinutes(double minutes)
    {
        int m = (int)Math.Round(minutes);
        if (m < 60) return $"{m} دقيقة";
        return m % 60 == 0 ? $"{m / 60} ساعة" : $"{m / 60} س {m % 60} د";
    }
}

/// <summary>ملخص موضوع (التفريعة الرئيسية): مجموع كورساته في التقدم والوقت والتوقع والفهم.</summary>
public sealed record TopicSummary(
    Topic? Topic, string Name, int Courses, int Videos, int Watched,
    double RemainingMinutes, double TodayMinutes, double WeekMinutes, double TotalMinutes,
    double AvgMinutesPerDay, DateOnly? FinishDate, int? Understanding, Place? Place = null)
{
    public int Percent => Videos == 0 ? 0 : Watched * 100 / Videos;
}

public static class TopicStats
{
    public const string NoTopicName = "من غير موضوع";

    /// <summary>سجلات المذاكرة الخاصة بكورسات الموضوع (null = كل حاجة).</summary>
    public static List<JournalEntry> EntriesOf(CourseLibrary lib, Topic? topic, IEnumerable<JournalEntry> entries, bool noTopic = false)
    {
        if (topic == null && !noTopic) return entries.ToList();
        var ids = new HashSet<string>(lib.CoursesOf(topic).Select(c => c.Id), StringComparer.Ordinal);
        var paths = new HashSet<string>(lib.CoursesOf(topic).SelectMany(c => c.AllVideos).Select(v => v.Path), StringComparer.OrdinalIgnoreCase);
        return entries.Where(e => e.CourseId != null ? ids.Contains(e.CourseId) : e.VideoPath != null && paths.Contains(e.VideoPath)).ToList();
    }

    /// <summary>سجلات المذاكرة الخاصة بمجموعة كورسات (مثلًا كورسات مكان).</summary>
    public static List<JournalEntry> EntriesOfCourses(IEnumerable<Course> courses, IEnumerable<JournalEntry> entries)
    {
        var list = courses.ToList();
        var ids = new HashSet<string>(list.Select(c => c.Id), StringComparer.Ordinal);
        var paths = new HashSet<string>(list.SelectMany(c => c.AllVideos).Select(v => v.Path), StringComparer.OrdinalIgnoreCase);
        return entries.Where(e => e.CourseId != null ? ids.Contains(e.CourseId) : e.VideoPath != null && paths.Contains(e.VideoPath)).ToList();
    }

    public static TopicSummary For(CourseLibrary lib, Topic? topic, IReadOnlyList<JournalEntry> entries, DateOnly today,
        float defaultRate = 1f, int lookbackDays = 14) =>
        Summarize(topic, null, topic?.Name ?? NoTopicName, lib.CoursesOf(topic).ToList(),
            EntriesOf(lib, topic, entries, noTopic: topic == null), today, defaultRate, lookbackDays);

    /// <summary>ملخص مكان (ROUTE مثلًا): مجموع كورساته. الاسم «Backend › ROUTE».</summary>
    public static TopicSummary ForPlace(CourseLibrary lib, Place place, IReadOnlyList<JournalEntry> entries, DateOnly today,
        float defaultRate = 1f, int lookbackDays = 14)
    {
        var courses = lib.CoursesInPlace(place).ToList();
        var topic = lib.TopicOf(place);
        string name = topic != null ? $"{topic.Name} › {place.Name}" : place.Name;
        return Summarize(topic, place, name, courses, EntriesOfCourses(courses, entries), today, defaultRate, lookbackDays);
    }

    private static TopicSummary Summarize(Topic? topic, Place? place, string name, List<Course> courses, List<JournalEntry> mine,
        DateOnly today, float defaultRate, int lookbackDays)
    {
        var daily = StudyStats.DailyMinutes(mine);
        var ws = StudyStats.StartOfWeek(today);
        double week = Enumerable.Range(0, 7).Sum(i => StudyStats.MinutesOn(daily, ws.AddDays(i)));
        double remaining = courses.Sum(c => StudyStats.RemainingMinutes(c, defaultRate));
        DateOnly from = today.AddDays(-(lookbackDays - 1));
        double avg = daily.Where(kv => kv.Key >= from && kv.Key <= today).Sum(kv => kv.Value) / lookbackDays;
        DateOnly? finish = remaining <= 0.01 ? today : avg >= 0.5 ? today.AddDays((int)Math.Ceiling(remaining / avg)) : null;

        // الفهم: متوسط الكورسات اللي فيها فيديوهات، بوزن عدد الفيديوهات
        var withVideos = courses.Where(c => c.VideoCount > 0).ToList();
        int? understanding = withVideos.Count == 0 ? null
            : (int)Math.Round(withVideos.Sum(c => UnderstandingIndex.For(c, mine).Score * (double)c.VideoCount) / withVideos.Sum(c => c.VideoCount));

        return new TopicSummary(topic, name, courses.Count, courses.Sum(c => c.VideoCount), courses.Sum(c => c.WatchedCount),
            remaining, StudyStats.MinutesOn(daily, today), week, daily.Values.Sum(), avg, finish, understanding, place);
    }

    /// <summary>كل المواضيع بالترتيب، وفي الآخر «من غير موضوع» لو فيه كورسات برا.</summary>
    public static List<TopicSummary> All(CourseLibrary lib, IReadOnlyList<JournalEntry> entries, DateOnly today, float defaultRate = 1f)
    {
        var list = lib.Topics.Select(t => For(lib, t, entries, today, defaultRate)).ToList();
        if (lib.CoursesOf(null).Any()) list.Add(For(lib, null, entries, today, defaultRate));
        return list;
    }

    /// <summary>زي <see cref="All"/>، وبعد كل موضوع أماكنه (ROUTE، Udemy…).</summary>
    public static List<TopicSummary> AllWithPlaces(CourseLibrary lib, IReadOnlyList<JournalEntry> entries, DateOnly today, float defaultRate = 1f)
    {
        var list = new List<TopicSummary>();
        foreach (var t in lib.Topics)
        {
            list.Add(For(lib, t, entries, today, defaultRate));
            list.AddRange(lib.PlacesOf(t).Select(p => ForPlace(lib, p, entries, today, defaultRate)));
        }
        list.AddRange(lib.PlacesOf(null).Select(p => ForPlace(lib, p, entries, today, defaultRate)));
        if (lib.CoursesOf(null).Any(c => lib.PlaceOf(c) == null)) list.Add(For(lib, null, entries, today, defaultRate));
        return list;
    }
}
