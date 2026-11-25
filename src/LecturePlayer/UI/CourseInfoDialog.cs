using System.Globalization;
using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>بيانات الكورس: الحالة، الأولوية، الوسوم، المحاضر، المصدر، الهدف، الموعد، وأيام المذاكرة.</summary>
internal static class CourseInfoDialog
{
    private static readonly DayOfWeek[] Week =
    {
        DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday,
        DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
    };

    /// <returns>true لو اتحفظ تغيير.</returns>
    public static bool Show(IWin32Window owner, Course c, IReadOnlyList<JournalEntry> journal, IEnumerable<string> knownTags, float defaultRate)
    {
        using var kit = new FormKit("بيانات الدورة", 560);
        var name = kit.Row("الاسم", new TextBox { Text = c.Name });

        var status = kit.Row("الحالة", new SelectBox());
        foreach (CourseStatus s in Enum.GetValues<CourseStatus>()) status.Items.Add(CourseFilter.StatusText(s));
        status.SelectedIndex = (int)c.Status;

        var priority = kit.Row("الأولوية", new SelectBox());
        priority.Items.AddRange(new[] { "عادية", "مهمة", "عاجلة" });
        priority.SelectedIndex = Math.Clamp(c.Priority, 0, 2);

        var tags = kit.Row("الوسوم", new TextBox { Text = string.Join("، ", c.Tags) });
        var known = knownTags.Except(c.Tags, StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        if (known.Count > 0) kit.Note("موجودة: " + string.Join("، ", known));

        var instructor = kit.Row("المحاضر", new TextBox { Text = c.Instructor ?? "" });
        var source = kit.Row("المصدر (رابط)", new TextBox { Text = c.SourceUrl ?? "", RightToLeft = RightToLeft.No });
        var goal = kit.Row("الهدف", new TextBox { Text = c.Goal ?? "", Multiline = true, ScrollBars = ScrollBars.Vertical }, 54);

        kit.Header("الموعد والخطة");
        var hasDeadline = kit.Row("لها موعد", new ToggleSwitch { Text = "أريد إنهاءها قبل تاريخ معيّن", Checked = c.Deadline != null });
        var deadline = kit.Row("التاريخ", new DateBox { Value = (c.Deadline ?? DateTime.Today.AddDays(30)).Date, Enabled = c.Deadline != null });
        kit.Note("− / + يومًا بيوم، ومع Shift = أسبوع، أو اكتب التاريخ yyyy/MM/dd.");

        var days = kit.Row("أيام المذاكرة", new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = true, BackColor = Theme.Panel }, 72);
        var ar = CultureInfo.GetCultureInfo("ar-EG");
        var dayBoxes = new List<(DayOfWeek Day, ChipToggle Box)>();
        foreach (var d in Week)
        {
            var cb = new ChipToggle(ar.DateTimeFormat.GetDayName(d), c.StudyDays.Count == 0 || c.StudyDays.Contains(d))
            {
                Margin = new Padding(0, 0, 6, 6)
            };
            days.Controls.Add(cb);
            dayBoxes.Add((d, cb));
        }

        var plan = kit.Note("", 44, Theme.Text);

        void UpdatePlan()
        {
            deadline.Enabled = hasDeadline.Checked;
            var tmp = new Course { Groups = c.Groups, Rate = c.Rate, Id = c.Id };
            tmp.Deadline = hasDeadline.Checked ? deadline.Value.Date : null;
            tmp.StudyDays = SelectedDays();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var p = StudyPlanner.PlanFor(tmp, journal, today, defaultRate);
            var f = StudyStats.Forecast(tmp, journal, today, defaultRate: defaultRate);
            string fc = f.DaysLeft == 0 ? "اكتملت الدورة."
                : f.FinishDate is { } fd ? $"بمعدلك في آخر أسبوعين ({StudyStats.FormatMinutes(f.AvgMinutesPerDay)}/يوم) ستنتهي نحو {fd:yyyy/MM/dd}."
                : $"متبقٍ {StudyStats.FormatMinutes(f.RemainingMinutes)} — لا يوجد معدل مذاكرة كافٍ للتوقع بعد.";
            plan.Text = StudyPlanner.Describe(p) + "\n" + fc;
            plan.ForeColor = p.Status is PlanStatus.Unrealistic or PlanStatus.Overdue ? Theme.Danger : Theme.Text;
        }

        List<DayOfWeek> SelectedDays()
        {
            var sel = dayBoxes.Where(x => x.Box.Checked).Select(x => x.Day).ToList();
            return sel.Count == 7 ? new List<DayOfWeek>() : sel; // كل الأيام = فاضية
        }

        hasDeadline.CheckedChanged += (_, _) => UpdatePlan();
        deadline.ValueChanged += (_, _) => UpdatePlan();
        foreach (var (_, box) in dayBoxes) box.CheckedChanged += (_, _) => UpdatePlan();
        UpdatePlan();

        kit.Buttons();
        kit.Form.FormClosing += (_, e) =>
        {
            if (kit.Form.DialogResult != DialogResult.OK) return;
            if (string.IsNullOrWhiteSpace(name.Text)) { e.Cancel = true; Dialogs.Error(kit.Form, "لا يمكن أن يكون الاسم فارغًا."); return; }
            if (dayBoxes.All(x => !x.Box.Checked)) { e.Cancel = true; Dialogs.Error(kit.Form, "اختر يوم مذاكرة واحدًا على الأقل."); }
        };
        if (kit.Show(owner) != DialogResult.OK) return false;

        c.Name = name.Text.Trim();
        c.Status = (CourseStatus)status.SelectedIndex;
        c.Priority = priority.SelectedIndex;
        c.Tags = CourseFilter.ParseTags(tags.Text);
        c.Instructor = NullIfEmpty(instructor.Text);
        c.SourceUrl = NullIfEmpty(source.Text);
        c.Goal = NullIfEmpty(goal.Text);
        c.Deadline = hasDeadline.Checked ? deadline.Value.Date : null;
        c.StudyDays = SelectedDays();
        c.TouchInfo();
        return true;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
