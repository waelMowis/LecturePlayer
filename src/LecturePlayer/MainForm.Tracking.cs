using System.Diagnostics;
using LecturePlayer.Data;
using LecturePlayer.UI;
using LibVLCSharp.Shared;

namespace LecturePlayer;

/// <summary>
/// المرحلة 2 — التنظيم والتتبع: بيانات الكورس، الفلتر، المرفقات، تسجيل وقت المذاكرة،
/// خطة النهارده، التذكير، بومودورو، والإحصائيات.
/// </summary>
internal sealed partial class MainForm
{
    private StudyJournal? _journal;
    private readonly SessionTracker _session = new();
    private readonly Stopwatch _trackClock = Stopwatch.StartNew();
    private double _lastTrackSec;
    private readonly ToolStripStatusLabel _statusStudy = new() { ForeColor = Theme.Muted };
    private GlyphButton _filterButton = null!;
    private string _activeFilterText = "";
    private readonly ContextMenuStrip _filterMenu = new() { RightToLeft = RightToLeft.Yes, ShowImageMargin = false, ShowCheckMargin = true };
    private readonly Panel _homePlan = new();
    private NotifyIcon? _tray;
    private DateOnly _remindedOn;

    // بومودورو
    private bool _pomoRunning, _pomoBreak;
    private DateTime _pomoEndsUtc;

    private IReadOnlyList<JournalEntry> JournalEntries => _journal?.LoadAll() ?? Array.Empty<JournalEntry>();
    private static DateOnly TodayLocal => DateOnly.FromDateTime(DateTime.Now);

    // =====================================================================
    //  سجل المذاكرة
    // =====================================================================

    private void InitJournal(string libraryPath)
    {
        FlushSession();
        _journal = new StudyJournal(libraryPath);
        UpdateStudyStatus();
    }

    private void LogJournal(JournalEntry e)
    {
        if (_journal == null) return;
        _journal.Append(e);
    }

    private void LogWatched(VideoItem v)
    {
        var g = _library.FindGroupOf(v);
        LogJournal(new JournalEntry
        {
            Kind = JournalEntry.Watched, StartUtc = DateTime.UtcNow,
            CourseId = g != null ? _library.FindCourseOf(g)?.Id : null, GroupId = g?.Id, VideoPath = v.Path
        });
    }

    /// <summary>بتتنادى من التايمر (كل ربع ثانية).</summary>
    private void TrackTick()
    {
        double now = _trackClock.Elapsed.TotalSeconds;
        double elapsed = now - _lastTrackSec;
        _lastTrackSec = now;

        bool playing = _currentVideo != null && _mp.State == VLCState.Playing;
        var course = _currentGroup != null ? _library.FindCourseOf(_currentGroup) : null;
        var finished = _session.Tick(DateTime.UtcNow, playing, course?.Id, _currentGroup?.Id, _currentVideo?.Path, elapsed);
        if (finished != null) { LogJournal(finished); UpdateStudyStatus(); }

        SilenceTick();
        SleepTick();
        FocusTick();
        if (_tick % 4 == 0) PomodoroTick();
        if (_tick % 40 == 0) UpdateStudyStatus();
        if (_tick % 240 == 0) { CheckReminder(); CheckPlaylistSyncDue(); } // كل دقيقة
    }

    private void FlushSession()
    {
        var e = _session.Flush(DateTime.UtcNow);
        if (e != null) LogJournal(e);
    }

    /// <summary>دقايق النهارده (المسجلة + الجلسة الشغالة).</summary>
    private double TodayMinutes() =>
        StudyStats.MinutesOn(StudyStats.DailyMinutes(JournalEntries), TodayLocal) + _session.ActiveSeconds / 60.0;

    private static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    private void UpdateStudyStatus()
    {
        double today = TodayMinutes();
        var streak = StudyStats.Streak(StudyStats.DailyMinutes(JournalEntries), TodayLocal);
        string pomo = _pomoRunning ? $"   {(_pomoBreak ? "☕" : "🍅")} {Clock(PomoRemaining())}" : "";
        string focus = _focusUntilUtc is { } fu ? $"   🎯 {Clock(fu - DateTime.UtcNow)}" : "";
        _statusStudy.Text = $"النهارده {today:0}/{_settings.DailyGoalMinutes} د" + (streak.Current > 0 ? $"  🔥{streak.Current}" : "") + pomo + focus;
        _statusStudy.ForeColor = today >= _settings.DailyGoalMinutes ? Theme.Success : Theme.Muted;
        _statusStudy.ToolTipText = "وقت المذاكرة الفعلي النهارده (الفيديو شغال) — اضغط للإحصائيات";
    }

    // =====================================================================
    //  التذكير (أيقونة جنب الساعة)
    // =====================================================================

    private void BuildTray()
    {
        _statusStudy.IsLink = false;
        _statusStudy.Click += (_, _) => ShowStats();
        _status.Items.Add(_statusStudy);

        try
        {
            _tray = new NotifyIcon
            {
                Icon = Icon ?? SystemIcons.Application,
                Text = AppTitle.Length > 60 ? AppTitle[..60] : AppTitle,
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes }
            };
            _tray.ContextMenuStrip.Items.Add("فتح", null, (_, _) => RestoreFromTray());
            _tray.ContextMenuStrip.Items.Add("الإحصائيات", null, (_, _) => { RestoreFromTray(); ShowStats(); });
            _tray.ContextMenuStrip.Items.Add("بومودورو", null, (_, _) => TogglePomodoro());
            _tray.ContextMenuStrip.Items.Add("خروج", null, (_, _) => Close());
            foreach (ToolStripItem i in _tray.ContextMenuStrip.Items) i.ForeColor = Theme.Text;
            _tray.DoubleClick += (_, _) => RestoreFromTray();
            _tray.BalloonTipClicked += (_, _) => RestoreFromTray();
        }
        catch (Exception ex) { Log.Error("tray", ex); }
    }

    private void RestoreFromTray()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = _settings.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
        Show();
        Activate();
    }

    private void Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try { _tray?.ShowBalloonTip(8000, title, text, icon); }
        catch { /* التنبيهات مقفولة في الويندوز */ }
        ShowStatus(title + " — " + text, 8);
    }

    private void CheckReminder()
    {
        if (!_settings.RemindersEnabled || _remindedOn == TodayLocal) return;
        if (!TimeOnly.TryParse(_settings.ReminderTime, out var at) || TimeOnly.FromDateTime(DateTime.Now) < at) return;
        _remindedOn = TodayLocal;

        double today = TodayMinutes();
        if (today >= _settings.DailyGoalMinutes) return;
        var plan = StudyPlanner.Today(_library, JournalEntries, TodayLocal, _settings.Rate).FirstOrDefault(p => p.Status is PlanStatus.Pending or PlanStatus.Overdue);
        string what = plan != null ? $"\nالأولى: {plan.Course.Name}" + (plan.NextVideo != null ? $" — {plan.NextVideo.Title}" : "") : "";
        Notify("لسه فاضل على هدف النهارده",
            $"ذاكرت {today:0} من {_settings.DailyGoalMinutes} دقيقة.{what}");
    }

    // =====================================================================
    //  بومودورو
    // =====================================================================

    private TimeSpan PomoRemaining()
    {
        var r = _pomoEndsUtc - DateTime.UtcNow;
        return r < TimeSpan.Zero ? TimeSpan.Zero : r;
    }

    private void TogglePomodoro()
    {
        _pomoRunning = !_pomoRunning;
        _pomoBreak = false;
        _pomoEndsUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, _settings.PomodoroWorkMinutes));
        ShowStatus(_pomoRunning ? $"🍅 بومودورو: {_settings.PomodoroWorkMinutes} دقيقة تركيز" : "بومودورو اتقفل");
        UpdateStudyStatus();
    }

    private void PomodoroTick()
    {
        if (!_pomoRunning) return;
        if (DateTime.UtcNow < _pomoEndsUtc)
        {
            if (_tick % 4 == 0) UpdateStudyStatus();
            return;
        }

        _pomoBreak = !_pomoBreak;
        if (_pomoBreak)
        {
            _pomoEndsUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, _settings.PomodoroBreakMinutes));
            if (_settings.PomodoroPausesVideo && _mp.State == VLCState.Playing) _mp.SetPause(true);
            Notify("☕ وقت البريك", $"{_settings.PomodoroBreakMinutes} دقايق راحة. قوم اتحرك واشرب مية.");
        }
        else
        {
            _pomoEndsUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, _settings.PomodoroWorkMinutes));
            Notify("🍅 يلا نكمل", $"{_settings.PomodoroWorkMinutes} دقيقة تركيز.");
        }
        UpdateStudyStatus();
    }

    // =====================================================================
    //  الإحصائيات والإعدادات
    // =====================================================================

    private void ShowStats()
    {
        FlushSession();
        using var f = new StatsForm(_library, JournalEntries, _settings);
        f.ShowDialog(this);
    }

    private void SettingsDialog()
    {
        if (UI.SettingsDialog.Show(this, _settings))
        {
            YtDlp.CookiesBrowser = _settings.OnlineCookiesBrowser;
            ApplyMode();
            SaveSettings();
            UpdateStudyStatus();
            ShowStatus("الإعدادات اتحفظت ✓");
        }
    }

    // =====================================================================
    //  بيانات الكورس والفلتر
    // =====================================================================

    private IEnumerable<string> AllTags() =>
        _library.Courses.SelectMany(c => c.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase);

    private void EditCourseInfo(Course? c)
    {
        if (c == null) { Dialogs.Info(this, "حدد كورس الأول."); return; }
        string before = Storage.Serialize(_library);
        FlushSession();
        if (!CourseInfoDialog.Show(this, c, JournalEntries, AllTags(), _settings.Rate)) return;
        _undo.Push("تعديل بيانات الكورس", before);
        MarkDirty();
        SaveLibrary();
        RefreshFilterCombo();
        RebuildTree();
        SelectItem(c);
    }

    private void BuildFilterButton(Panel row)
    {
        _filterMenu.Renderer = new DarkRenderer();
        _filterButton = new GlyphButton(Glyphs.Filter, 11f) { Dock = DockStyle.Left, Width = LogicalToDeviceUnits(30) };
        _toolTip.SetToolTip(_filterButton, "فلترة الكورسات بالحالة أو الوسم");
        _filterButton.Click += (_, _) => _filterMenu.Show(_filterButton, new Point(0, _filterButton.Height + 2));
        row.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 4, BackColor = Theme.Panel });
        row.Controls.Add(_filterButton);
    }

    private sealed record FilterOption(string Key, string Text)
    {
        public override string ToString() => Text;
    }

    private void RefreshFilterCombo()
    {
        var opts = new List<FilterOption>
        {
            new("all", "كل الكورسات"), new("active", "الشغالة"), new("deadline", "ليها موعد"),
            new("planned", "المخططة"), new("paused", "المتوقفة"), new("done", "اللي خلصت")
        };
        var tags = AllTags().Select(t => new FilterOption("tag:" + t, "#" + t)).ToList();
        var sel = opts.Concat(tags).FirstOrDefault(o => o.Key == _settings.CourseFilter) ?? opts[0];
        _settings.CourseFilter = sel.Key;

        _filterMenu.Items.Clear();
        void Add(FilterOption o)
        {
            var item = new ToolStripMenuItem(Modern.Bidi(o.Text)) { ForeColor = Theme.Text, Checked = o.Key == sel.Key };
            item.Click += (_, _) =>
            {
                _settings.CourseFilter = o.Key;
                RefreshFilterCombo();
                RebuildTree();
            };
            _filterMenu.Items.Add(item);
        }
        foreach (var o in opts) Add(o);
        if (tags.Count > 0)
        {
            _filterMenu.Items.Add(new ToolStripSeparator());
            foreach (var o in tags) Add(o);
        }
        _filterButton.Active = sel.Key != "all";
        _toolTip.SetToolTip(_filterButton, sel.Key == "all" ? "فلترة الكورسات بالحالة أو الوسم" : "الفلتر: " + sel.Text + " (اضغط للتغيير)");
        _activeFilterText = sel.Key == "all" ? "" : sel.Text;
        UpdateLibraryHeader();
    }

    /// <summary>علامات صغيرة جنب اسم الكورس في الشجرة.</summary>
    private static string CourseMarks(Course c)
    {
        string m = c.Status switch
        {
            CourseStatus.Paused => "⏸ ",
            CourseStatus.Planned => "🗓 ",
            _ => ""
        };
        if (c.Priority == 2) m += "‼ ";
        else if (c.Priority == 1) m += "! ";
        return m;
    }

    private string CourseTooltip(Course c)
    {
        var lines = new List<string> { c.Name, $"{c.Groups.Count} محاضرة · {c.VideoCount} فيديو · {CourseFilter.StatusText(c.Status)}" +
            (c.TotalDurationMs > 0 ? " · المدة " + TimeFormat.Format(c.TotalDurationMs) : "") };
        if (c.Tags.Count > 0) lines.Add(string.Join(" ", c.Tags.Select(t => "#" + t)));
        if (!string.IsNullOrWhiteSpace(c.Instructor)) lines.Add("المحاضر: " + c.Instructor);
        if (!string.IsNullOrWhiteSpace(c.Goal)) lines.Add("الهدف: " + c.Goal);
        if (c.Deadline != null)
            lines.Add($"الموعد: {c.Deadline:yyyy/MM/dd} — " + StudyPlanner.Describe(StudyPlanner.PlanFor(c, JournalEntries, TodayLocal, _settings.Rate)));
        if (_library.LinkOf(c) != null) lines.Add("🔄 من فولدر متابَع (الجديد بيتضاف لوحده):\n" + c.SourceFolder);
        return string.Join("\n", lines);
    }

    // =====================================================================
    //  المرفقات
    // =====================================================================

    private ToolStripMenuItem AttachmentsMenu(LectureGroup g)
    {
        var menu = new ToolStripMenuItem(g.Attachments.Count > 0 ? $"📎 المرفقات ({g.Attachments.Count})" : "📎 المرفقات") { ForeColor = Theme.Text };
        foreach (var a in g.Attachments)
        {
            var att = a;
            var item = new ToolStripMenuItem((att.IsLink ? "🔗 " : "📄 ") + att.Name) { ForeColor = Theme.Text, ToolTipText = att.Target };
            item.Click += (_, _) => OpenAttachment(att);
            item.DropDownItems.Add(MenuItem("فتح", () => OpenAttachment(att)));
            if (!att.IsLink) item.DropDownItems.Add(MenuItem("فتح مكانه", () => OpenInExplorer(Storage.ResolveAttachment(att, _libraryPath))));
            item.DropDownItems.Add(MenuItem("إعادة تسمية…", () =>
            {
                string? n = Dialogs.Input(this, "إعادة تسمية", "اسم المرفق:", att.Name);
                if (n == null) return;
                Checkpoint("إعادة تسمية مرفق");
                att.Name = n; TouchGroupInfo(g);
            }));
            item.DropDownItems.Add(MenuItem("شيله من المحاضرة", () =>
            {
                Checkpoint("حذف مرفق");
                IgnoreInFollowed(att.Target);
                g.Attachments.Remove(att); TouchGroupInfo(g);
            }));
            menu.DropDownItems.Add(item);
        }
        if (g.Attachments.Count > 0) menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(MenuItem("إضافة ملفات (PDF، سلايدات، كود)…", () => AddAttachmentFiles(g)));
        menu.DropDownItems.Add(MenuItem("إضافة رابط…", () => AddAttachmentLink(g)));
        return menu;
    }

    private void TouchGroupInfo(LectureGroup g)
    {
        g.InfoUpdatedUtc = DateTime.UtcNow;
        MarkDirty();
        SaveLibrary();
        RefreshGroup(g);
    }

    private void AddAttachmentFiles(LectureGroup g)
    {
        using var dlg = new OpenFileDialog { Multiselect = true, Title = "مرفقات للمحاضرة: " + g.Name, Filter = "كل الملفات|*.*" };
        var first = g.Videos.FirstOrDefault(v => !v.IsOnline && File.Exists(v.LocalPath));
        if (first != null) dlg.InitialDirectory = Path.GetDirectoryName(first.LocalPath);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Checkpoint("إضافة مرفقات");
        foreach (var f in dlg.FileNames)
            if (!g.Attachments.Any(a => string.Equals(a.Target, f, StringComparison.OrdinalIgnoreCase)))
                g.Attachments.Add(new Attachment { Name = Path.GetFileName(f), Target = f });
        TouchGroupInfo(g);
        ShowStatus($"اتضاف {dlg.FileNames.Length} مرفق ✓");
    }

    private void AddAttachmentLink(LectureGroup g)
    {
        string initial = "";
        try { if (Clipboard.ContainsText() && MediaFiles.IsUrl(Clipboard.GetText().Trim())) initial = Clipboard.GetText().Trim(); } catch { }
        string? url = Dialogs.Input(this, "إضافة رابط", "الرابط (https://…):", initial);
        if (url == null) return;
        if (!MediaFiles.IsUrl(url)) { Dialogs.Error(this, "الرابط لازم يبدأ بـ https://"); return; }
        string name = Dialogs.Input(this, "إضافة رابط", "اسم يظهر في المكتبة:", Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url) ?? url;
        Checkpoint("إضافة رابط مرفق");
        g.Attachments.Add(new Attachment { Name = name, Target = url });
        TouchGroupInfo(g);
    }

    private void OpenAttachment(Attachment a)
    {
        string target = Storage.ResolveAttachment(a, _libraryPath);
        if (!a.IsLink && !File.Exists(target) && !Directory.Exists(target))
        {
            Dialogs.Error(this, "الملف مش موجود على الجهاز ده:\n" + a.Target);
            return;
        }
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Dialogs.Error(this, "مقدرتش أفتحه:\n" + ex.Message); }
    }

    // =====================================================================
    //  خطة النهارده في الشاشة الرئيسية
    // =====================================================================

    private void BuildHomePlan()
    {
        _homePlan.Dock = DockStyle.Top;
        _homePlan.Height = 0;
        _homePlan.BackColor = Theme.Panel;
        _homePlan.Padding = new Padding(8, 4, 8, 8);
        _homePlan.RightToLeft = RightToLeft.Yes;
    }

    private void RefreshHomePlan()
    {
        var old = _homePlan.Controls.Cast<Control>().ToList();
        _homePlan.Controls.Clear();
        foreach (var c in old) c.Dispose();

        var entries = JournalEntries;
        var plan = StudyPlanner.Today(_library, entries, TodayLocal, _settings.Rate)
            .Where(p => (p.Status != PlanStatus.NoDeadline || p.Course.Priority > 0) && InHomeScope(p.Course)).Take(5).ToList();
        double today = TodayMinutes();
        var streak = StudyStats.Streak(StudyStats.DailyMinutes(entries), TodayLocal);

        int rowH = LogicalToDeviceUnits(26);
        var rows = new List<Control>();

        var head = new Label
        {
            Dock = DockStyle.Top, Height = LogicalToDeviceUnits(30), ForeColor = Theme.Text, Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight,
            Text = $"النهارده: {today:0} من {_settings.DailyGoalMinutes} دقيقة" + (today >= _settings.DailyGoalMinutes ? " ✓" : "") +
                   (streak.Current > 0 ? $"     🔥 {streak.Current} يوم ورا بعض" : "") + "     (الإحصائيات ←)"
        };
        head.Click += (_, _) => ShowStats();
        rows.Add(head);

        var fresh = NewVideos().Where(x => _library.FindCourseOf(x.Group) is { } fc && InHomeScope(fc)).ToList();
        if (fresh.Count > 0)
        {
            var groupsWithNew = fresh.Select(x => x.Group).Distinct().ToList();
            var row = new Label
            {
                Dock = DockStyle.Top, Height = rowH, Cursor = Cursors.Hand, TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Theme.Accent,
                Text = $"🆕  {fresh.Count} فيديو جديد في قوائم يوتيوب: " + string.Join("، ", groupsWithNew.Take(3).Select(g => Modern.Bidi(g.Name))) +
                       (groupsWithNew.Count > 3 ? "…" : "") + "  (اضغط للتشغيل)"
            };
            var firstNew = fresh[0];
            row.Click += (_, _) => PlayVideo(firstNew.Group, firstNew.Video);
            rows.Add(row);
        }

        int dueReviews = ReviewScheduler.Due(_library, DateTime.UtcNow).Count;
        if (dueReviews > 0)
        {
            var review = new Label
            {
                Dock = DockStyle.Top, Height = rowH, Cursor = Cursors.Hand, TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Theme.Folder, Text = $"📌  عندك {dueReviews} مراجعة النهارده (F7) — دقيقتين بيثبتوا اللي ذاكرته"
            };
            review.Click += (_, _) => ShowReview();
            rows.Add(review);
        }

        foreach (var p in plan)
        {
            string state = p.Status switch
            {
                PlanStatus.Overdue => "⚠ الموعد عدّى",
                PlanStatus.Unrealistic => "⚠ الموعد مش واقعي",
                PlanStatus.Pending => $"فاضل {StudyStats.FormatMinutes(p.MinutesLeftToday)} النهارده",
                PlanStatus.DoneToday => "✓ نصيب النهارده خلص",
                PlanStatus.RestDay => "النهارده مش يوم مذاكرة ليه",
                _ => "أولوية"
            };
            var row = new Label
            {
                Dock = DockStyle.Top, Height = rowH, Cursor = Cursors.Hand, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = p.Status is PlanStatus.Overdue or PlanStatus.Unrealistic ? Theme.Danger
                    : p.Status == PlanStatus.DoneToday ? Theme.Success : Theme.Text,
                Text = $"•  {p.Course.Name}  —  {state}" + (p.NextVideo != null && p.Status != PlanStatus.DoneToday ? $"  ·  الجاي: {p.NextVideo.Title}" : "")
            };
            var course = p.Course;
            row.Click += (_, _) =>
            {
                if (p.Status is PlanStatus.Overdue or PlanStatus.Unrealistic) EditCourseInfo(course);
                else PlayCourse(course);
            };
            rows.Add(row);
        }

        // Dock=Top: آخر واحد بيتضاف بيبقى فوق
        for (int i = rows.Count - 1; i >= 0; i--) _homePlan.Controls.Add(rows[i]);
        _homePlan.Height = rows.Sum(r => r.Height) + _homePlan.Padding.Vertical;
    }
}
