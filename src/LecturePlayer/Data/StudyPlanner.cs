namespace LecturePlayer.Data;

public enum PlanStatus
{
    /// <summary>الموعد عدّى والكورس لسه مخلصش.</summary>
    Overdue,
    /// <summary>المطلوب في اليوم أكتر من الحد المعقول.</summary>
    Unrealistic,
    /// <summary>فاضل جزء من نصيب النهارده.</summary>
    Pending,
    /// <summary>نصيب النهارده خلص.</summary>
    DoneToday,
    /// <summary>النهارده مش يوم مذاكرة للكورس ده.</summary>
    RestDay,
    /// <summary>كورس شغال من غير موعد.</summary>
    NoDeadline
}

public sealed record PlanItem(
    Course Course,
    PlanStatus Status,
    double MinutesPerStudyDay,
    double MinutesDoneToday,
    double MinutesLeftToday,
    double RemainingMinutes,
    int StudyDaysLeft,
    VideoItem? NextVideo);

/// <summary>خطة النهارده: كل كورس شغال ليه موعد، محتاج كام دقيقة النهارده.</summary>
public static class StudyPlanner
{
    /// <summary>أكتر من كده في اليوم للكورس الواحد يعتبر مش واقعي.</summary>
    public const double MaxRealisticMinutesPerDay = 180;

    public static int StudyDaysBetween(Course c, DateOnly from, DateOnly toInclusive)
    {
        int n = 0;
        for (var d = from; d <= toInclusive; d = d.AddDays(1))
            if (c.IsStudyDay(d.DayOfWeek)) n++;
        return n;
    }

    public static VideoItem? NextVideo(Course c) =>
        c.AllVideos.FirstOrDefault(v => !v.Watched && v.LastPositionMs > 0) ?? c.AllVideos.FirstOrDefault(v => !v.Watched);

    public static PlanItem PlanFor(Course c, IEnumerable<JournalEntry> entries, DateOnly today, float defaultRate = 1f)
    {
        double remaining = StudyStats.RemainingMinutes(c, defaultRate);
        double doneToday = StudyStats.MinutesOn(StudyStats.DailyMinutes(entries, c.Id), today);
        var next = NextVideo(c);

        if (c.Deadline is not { } dl)
            return new PlanItem(c, PlanStatus.NoDeadline, 0, doneToday, 0, remaining, 0, next);

        var deadline = DateOnly.FromDateTime(dl);
        if (deadline < today)
            return new PlanItem(c, PlanStatus.Overdue, remaining, doneToday, remaining, remaining, 0, next);

        int daysLeft = StudyDaysBetween(c, today, deadline);
        if (daysLeft == 0)
            return new PlanItem(c, PlanStatus.Unrealistic, remaining, doneToday, 0, remaining, 0, next);

        // نصيب اليوم بيتحسب من المتبقي في أول النهارده (اللي اتذاكر النهارده بيرجع للحسبة)
        double perDay = (remaining + (c.IsStudyDay(today.DayOfWeek) ? doneToday : 0)) / daysLeft;
        double leftToday = c.IsStudyDay(today.DayOfWeek) ? Math.Max(0, Math.Min(perDay - doneToday, remaining)) : 0;

        PlanStatus st = perDay > MaxRealisticMinutesPerDay ? PlanStatus.Unrealistic
            : !c.IsStudyDay(today.DayOfWeek) ? PlanStatus.RestDay
            : leftToday < 0.5 ? PlanStatus.DoneToday
            : PlanStatus.Pending;
        return new PlanItem(c, st, perDay, doneToday, leftToday, remaining, daysLeft, next);
    }

    /// <summary>خطة النهارده لكل الكورسات الشغالة اللي لسه مخلصتش، مترتبة بالأهم.</summary>
    public static List<PlanItem> Today(CourseLibrary lib, IEnumerable<JournalEntry> entries, DateOnly today, float defaultRate = 1f)
    {
        var list = entries as IReadOnlyCollection<JournalEntry> ?? entries.ToList();
        return lib.Courses
            .Where(c => c.Status == CourseStatus.Active && !c.IsCompleted && c.VideoCount > 0)
            .Select(c => PlanFor(c, list, today, defaultRate))
            .OrderBy(p => p.Status switch
            {
                PlanStatus.Overdue => 0, PlanStatus.Unrealistic => 1, PlanStatus.Pending => 2,
                PlanStatus.NoDeadline => 3, PlanStatus.DoneToday => 4, _ => 5
            })
            .ThenByDescending(p => p.Course.Priority)
            .ThenBy(p => p.Course.Deadline ?? DateTime.MaxValue)
            .ThenByDescending(p => p.Course.LastActivityUtc)
            .ToList();
    }

    /// <summary>جملة توضح الموعد واقعي ولا لأ (لشاشة بيانات الكورس).</summary>
    public static string Describe(PlanItem p) => p.Status switch
    {
        PlanStatus.NoDeadline => "مفيش موعد محدد.",
        PlanStatus.Overdue => $"⚠ الموعد عدّى وفاضل {StudyStats.FormatMinutes(p.RemainingMinutes)}.",
        PlanStatus.Unrealistic when p.StudyDaysLeft == 0 => "⚠ مفيش أيام مذاكرة قبل الموعد.",
        PlanStatus.Unrealistic => $"⚠ مش واقعي: محتاج {StudyStats.FormatMinutes(p.MinutesPerStudyDay)} في كل يوم مذاكرة ({p.StudyDaysLeft} يوم). أجّل الموعد أو زوّد الأيام.",
        _ => $"محتاج حوالي {StudyStats.FormatMinutes(p.MinutesPerStudyDay)} في كل يوم مذاكرة ({p.StudyDaysLeft} يوم فاضل)."
    };
}

/// <summary>فلتر الكورسات في القايمة.</summary>
public static class CourseFilter
{
    public const string All = "all";

    public static bool Matches(Course c, string? filter)
    {
        if (string.IsNullOrEmpty(filter) || filter == All) return true;
        if (filter.StartsWith("tag:", StringComparison.Ordinal))
            return c.Tags.Contains(filter[4..], StringComparer.OrdinalIgnoreCase);
        return filter switch
        {
            "active" => c.Status == CourseStatus.Active,
            "planned" => c.Status == CourseStatus.Planned,
            "paused" => c.Status == CourseStatus.Paused,
            "done" => c.Status == CourseStatus.Done || c.IsCompleted,
            "deadline" => c.Deadline != null && c.Status != CourseStatus.Done,
            _ => true
        };
    }

    public static string StatusText(CourseStatus s) => s switch
    {
        CourseStatus.Active => "شغال",
        CourseStatus.Planned => "مخطط",
        CourseStatus.Paused => "متوقف",
        CourseStatus.Done => "خلص",
        _ => s.ToString()
    };

    /// <summary>الوسوم من نص مكتوب بفواصل (، أو ,) — و# في الأول بتتشال.</summary>
    public static List<string> ParseTags(string? text) =>
        (text ?? "").Split(new[] { ',', '،', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.TrimStart('#').Trim()).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
