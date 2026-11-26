using System.Globalization;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// وضع المعلم في الواجهة نفسها: زر «الحصص» في الشريط الجانبي، و«حصص اليوم» في الرئيسية (بطاقة لكل مجموعة:
/// موعدها، ومن أين تبدأ، والواجب، و«ابدأ الحصة»)، وأثناء الحصة: اسمها والوقت المتبقي وزر «إنهاء الحصة»
/// (الواجب والتصدير، ثم الحصة التالية). وللطالب: «جلساتي» في الرئيسية، والمراجعة والإحصائيات في الشريط الجانبي.
/// </summary>
internal partial class MainForm
{
    private readonly FlowLayoutPanel _homeRole = new();
    private System.Windows.Forms.Timer? _lessonTimer;
    private string? _lessonOverNotified;
    private string _homeRoleSignature = "";

    internal FlowLayoutPanel HomeRolePanel => _homeRole;

    // =====================================================================
    //  الشريط الجانبي حسب الدور
    // =====================================================================

    /// <summary>
    /// الطالب: الرئيسية · المكتبة · المراجعة · الإحصائيات · الأدوات.
    /// المعلم: الرئيسية · المكتبة · الحصص · السبورة · التسجيل · الأدوات. والاثنان: الحصص والسبورة والتسجيل والمراجعة.
    /// </summary>
    private void ApplyRoleRail()
    {
        string? r = _settings.Role;
        bool teacher = r is "teacher" or "both", student = r is "student" or "both";
        if (_activityClasses != null) _activityClasses.Visible = teacher;
        if (_activityBoard != null) _activityBoard.Visible = r != "student";
        if (_activityRecord != null) _activityRecord.Visible = r != "student";
        if (_activityReview != null) _activityReview.Visible = student;
        if (_activityStats != null) _activityStats.Visible = r == "student";
        _fitRail?.Invoke();
    }

    // =====================================================================
    //  قائمة «الحصص» (الشريط الجانبي، وزر «👥 الحصة» أعلى المساحة)
    // =====================================================================

    internal ContextMenuStrip? LastClassesMenu;

    private void ShowClassesActivityMenu(Control anchor)
    {
        var m = NewClassesMenu();
        m.Show(anchor, new Point(-4, 0), ToolStripDropDownDirection.Left);   // جنب الزرار ناحية الشاشة (الشريط على اليمين)
    }

    private void ShowClassesMenuAt(Point screen) => NewClassesMenu().Show(screen);

    private ContextMenuStrip NewClassesMenu()
    {
        var m = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        m.Items.AddRange(TeacherMenuItems().ToArray());
        m.Closed += (_, _) => BeginInvoke(m.Dispose);
        LastClassesMenu = m;
        return m;
    }

    /// <summary>عناصر قائمة الحصص: الحصة الجارية، وحصص اليوم، والمجموعات الأخرى، والجدول.</summary>
    internal List<ToolStripItem> TeacherMenuItems()
    {
        var items = new List<ToolStripItem>();
        if (!HasLibrary)
        {
            items.Add(new ToolStripLabel("المجموعات والحصص محفوظة داخل المكتبة") { ForeColor = Theme.Muted });
            items.Add(Item("📂  افتح مكتبتك لتظهر حصصك…", () => EnsureLibrary()));
            items.Add(new ToolStripSeparator());
            items.Add(RoleMenu());
            return items;
        }
        var now = DateTime.Now;
        if (ActiveSession is { ClassId: not null } live)
        {
            items.Add(new ToolStripLabel($"الآن: {live.Name}{LessonRemainingText(live)}") { ForeColor = Theme.Success });
            items.Add(Item("⏹  إنهاء الحصة (الواجب والتصدير)…", () => EndLessonDialog(live)));
            items.Add(Item("📝  الخطة والواجب…", () => SessionNotesDialog(live)));
            items.Add(Item("📤  تصدير الحصة للطلاب…", () => ExportSessionDialog(live)));
            items.Add(new ToolStripSeparator());
        }
        var today = TodaysClasses(now);
        items.Add(new ToolStripLabel(today.Count > 0 ? $"حصص اليوم — {DayNames[(int)now.DayOfWeek]}" : "لا حصص في جدول اليوم") { ForeColor = Theme.Muted });
        foreach (var (c, slot) in today)
        {
            var cls = c;
            var (status, _) = ClassStatus(c, slot, now);
            items.Add(Item($"▶  {slot.Start}  {c.Name}  ·  {status}", () => StartClass(cls)));
        }
        var others = _library.Classes.Where(c => today.All(t => t.Class != c)).ToList();
        if (others.Count > 0)
        {
            var sub = SubMenu(today.Count > 0 ? "▶  حصة لمجموعة أخرى" : "▶  ابدأ حصة مجموعة");
            foreach (var c in others) { var cls = c; sub.DropDownItems.Add(Item($"{c.Name}  ·  {ScheduleText(c)}", () => StartClass(cls))); }
            items.Add(sub);
        }
        items.Add(new ToolStripSeparator());
        items.Add(Item("📅  المجموعات والجدول (كل المجموعات في جدول واحد)…", () => ClassesManagerDialog()));
        items.Add(Item("➕  مجموعة جديدة…", () => NewClassDialog()));
        if (_library.Classes.Count > 0) items.Add(ClassesMenu());
        items.Add(new ToolStripSeparator());
        var sessions = SubMenu("📚  الحصص المحفوظة والجلسات");
        sessions.DropDownItems.AddRange(SessionMenuItems(teacherItems: false).ToArray());
        items.Add(sessions);
        items.Add(RoleMenu());
        return items;
    }

    // =====================================================================
    //  وقت الحصة
    // =====================================================================

    /// <summary>موعد اليوم لمجموعة الحصة (الأقرب للآن) — null لو مالهاش موعد اليوم.</summary>
    private (DateTime Start, DateTime End)? LessonWindow(StudySession s, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        if (s.ClassId == null || _library.Classes.FirstOrDefault(c => c.Id == s.ClassId) is not { } c) return null;
        var slots = c.Schedule.Where(x => x.Day == (int)t.DayOfWeek).Select(x => (Start: t.Date + x.StartTime, End: t.Date + x.StartTime + TimeSpan.FromMinutes(x.Minutes))).ToList();
        if (slots.Count == 0) return null;
        return slots.OrderBy(x => t >= x.Start && t <= x.End ? 0 : 1).ThenBy(x => Math.Abs((x.Start - t).TotalMinutes)).First();
    }

    /// <summary>« · متبقٍ 23 د» أثناء موعد الحصة، و« · انتهى الوقت» بعده.</summary>
    internal string LessonRemainingText(StudySession s, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        if (LessonWindow(s, t) is not { } w || t < w.Start) return "";
        if (t > w.End) return "  ·  انتهى الوقت";
        int left = (int)Math.Ceiling((w.End - t).TotalMinutes);
        return $"  ·  متبقٍ {left} د";
    }

    private void UpdateLessonTimer()
    {
        bool need = HasLibrary && (ActiveSession?.ClassId != null || (IsTeacher && _library.Classes.Count > 0));
        if (!need) { _lessonTimer?.Stop(); return; }
        if (_lessonTimer == null)
        {
            _lessonTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
            _lessonTimer.Tick += (_, _) => LessonTick();
        }
        if (!_lessonTimer.Enabled) _lessonTimer.Start();
    }

    private void LessonTick()
    {
        if (IsDisposed) return;
        if (ActiveSession is { ClassId: not null } s)
        {
            RefreshTabStrip();
            if (LessonWindow(s) is { } w && DateTime.Now > w.End && _lessonOverNotified != s.Id)
            {
                _lessonOverNotified = s.Id;
                ShowBanner($"⏰ انتهى وقت «{s.Name}» — أنهِ الحصة لتكتب الواجب، والحصة القادمة لهذه المجموعة تبدأ من هنا",
                    BannerKind.Warning, "إنهاء الحصة", () => { if (ActiveSession == s) EndLessonDialog(s); }, seconds: 40, key: "lesson-over");
            }
        }
        else UpdateLessonTimer();
        if (_welcome.Visible && IsTeacher && HomeRoleSignature() != _homeRoleSignature) RefreshHomeRole();
    }

    // =====================================================================
    //  بدء الحصة وتحضيرها وإنهاؤها
    // =====================================================================

    /// <summary>الحصة التالية للمجموعة (تُنشأ ولا تُفتح): بملفات السابقة ومواضعها، أو — لأول حصة — بالملفات المفتوحة.</summary>
    private StudySession CreateNextLesson(ClassGroup c)
    {
        var prev = c.CurrentSessionId == null ? null : _library.Sessions.FirstOrDefault(s => s.Id == c.CurrentSessionId);
        int n = _library.Sessions.Count(s => s.ClassId == c.Id) + 1;
        var tabs = prev != null ? prev.Tabs.Select(t => t.Copy()).ToList() : FirstLessonTabs();
        var next = new StudySession
        {
            Name = $"{c.Name} — حصة {n}",
            ClassId = c.Id,
            Tabs = tabs,
            StartTabs = tabs.Select(t => t.Copy()).ToList()
        };
        _library.Sessions.Add(next);
        c.CurrentSessionId = next.Id;
        c.UpdatedUtc = DateTime.UtcNow;
        MarkDirty();
        return next;
    }

    /// <summary>
    /// أول حصة لمجموعة: الملفات المفتوحة الآن. ولو هي نفس ملفات آخر حصة انتهت لمجموعة أخرى
    /// (المعلم شرح الدرس نفسه لفصل قبلها) تبدأ من حيث بدأت تلك الحصة، لا من حيث انتهت.
    /// </summary>
    private List<WorkTab> FirstLessonTabs()
    {
        if (_activeTab >= 0) CaptureTab(_activeTab);
        var open = _tabs.Select(t => t.Copy()).ToList();
        if (open.Count == 0) return open;
        var last = _library.Sessions.Where(s => s.ClassId != null && s.Done && s.StartTabs is { Count: > 0 })
            .OrderByDescending(s => s.UpdatedUtc).FirstOrDefault();
        bool sameFiles = last != null && last.Tabs.Count == open.Count && open.All(o => last.Tabs.Any(t => t.Same(o)));
        return sameFiles ? last!.StartTabs!.Select(t => t.Copy()).ToList() : open;
    }

    /// <summary>«📝 حضّر»: خطة الحصة القادمة وواجبها قبل أن تبدأ.</summary>
    internal void PrepareClass(ClassGroup c)
    {
        var cur = c.CurrentSessionId == null ? null : _library.Sessions.FirstOrDefault(s => s.Id == c.CurrentSessionId);
        var lesson = cur is { Done: false } ? cur : CreateNextLesson(c);
        SessionNotesDialog(lesson);
        RefreshHome();
    }

    /// <summary>
    /// إنهاء الحصة: الواجب (يظهر في بطاقة المجموعة ويدخل في ملف الطلاب)، وتصدير اختياري.
    /// الحصة القادمة لهذه المجموعة تبدأ من المواضع الحالية.
    /// </summary>
    internal void EndLessonDialog(StudySession s)
    {
        if (_activeTab >= 0) CaptureTab(_activeTab);
        MirrorSession();
        using var kit = new FormKit($"إنهاء الحصة — {s.Name}", 580);
        kit.Header("أين توقفتم");
        var places = s.Tabs.Select(TabPlaceText).ToList();
        kit.Note(places.Count == 0 ? "لم يُفتح ملف في هذه الحصة." : string.Join("\n", places), 22, Theme.Text);
        kit.Note("الحصة القادمة لهذه المجموعة تبدأ من هنا تلقائيًا.");
        kit.Header("الواجب");
        var hw = kit.Full(new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Text = s.Homework ?? "" }, 84);
        kit.Note("يظهر في بطاقة المجموعة في الرئيسية وعند بدء حصتها القادمة.");
        kit.Header("للطلاب");
        var export = kit.Full(new ToggleSwitch { Text = "صدّر الحصة الآن: PDF بصفحات الشرح والسبورة والشرائح والواجب", Checked = false }, 32);
        kit.Buttons("⏹  إنهاء الحصة");
        if (SmokeShowDialog(kit, "end-lesson")) return;
        if (kit.Show(this) != DialogResult.OK) return;
        EndLesson(s, hw.Text);
        if (export.Checked) ExportSessionDialog(s);
    }

    /// <summary>ينهي الحصة ويعرض الحصة التالية اليوم (إن وُجدت) بزر «ابدأها».</summary>
    internal void EndLesson(StudySession s, string? homework)
    {
        s.Homework = string.IsNullOrWhiteSpace(homework) ? null : homework.Trim();
        s.UpdatedUtc = DateTime.UtcNow;
        if (_sessionId == s.Id) EndSession(markDone: true);
        else { s.Done = true; MarkDirty(); }
        HideBanner("lesson-over");
        var now = DateTime.Now;
        var next = TodaysClasses(now).FirstOrDefault(x => x.Class.Id != s.ClassId && now.Date + x.Slot.StartTime + TimeSpan.FromMinutes(x.Slot.Minutes) > now);
        if (next.Class != null)
        {
            var cls = next.Class;
            ShowBanner($"✓ انتهت «{s.Name}» — التالية: {next.Slot.Start} {cls.Name}", BannerKind.Success, "ابدأها", () => StartClass(cls), seconds: 30, key: "next-class");
        }
    }

    /// <summary>وصف موضع تبويب: «📖 الكتاب — ص 34» أو «▶ الدرس 3 — 12:30».</summary>
    private string TabPlaceText(WorkTab t)
    {
        string glyph = TabGlyph(t), title = t.Title ?? "";
        if (t.Kind == WorkTab.BookKind)
        {
            var b = _library.Books.FirstOrDefault(x => x.Id == t.Ref);
            string unit = b?.IsPresentation == true ? "شريحة" : "ص";
            return t.Page is int p and > 0 ? $"{glyph} {title} — {unit} {p}" : $"{glyph} {title}";
        }
        var v = t.ItemPath == null ? null : _library.AllVideos.FirstOrDefault(x => x.Path == t.ItemPath);
        string where = v != null ? v.Title : title;
        return t.PositionMs is long ms and > 0 ? $"{glyph} {where} — {TimeFormat.Format(ms)}" : $"{glyph} {where}";
    }

    // =====================================================================
    //  الرئيسية: «حصص اليوم» للمعلم، و«جلساتي» للطالب
    // =====================================================================

    private void BuildHomeRole()
    {
        _homeRole.Dock = DockStyle.Top;
        _homeRole.FlowDirection = FlowDirection.LeftToRight;   // مع RightToLeft = من اليمين
        _homeRole.RightToLeft = RightToLeft.Yes;
        _homeRole.WrapContents = true;
        _homeRole.AutoSize = true;
        _homeRole.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _homeRole.BackColor = Theme.Panel;
        _homeRole.Padding = new Padding(0, 0, 0, LogicalToDeviceUnits(8));
        _homeRole.Visible = false;
        _homeRole.Resize += (_, _) => FitHomeRoleHeader();
    }

    private void FitHomeRoleHeader()
    {
        foreach (var l in _homeRole.Controls.OfType<Label>().Where(l => l.Tag as string == "head"))
            l.Width = Math.Max(LogicalToDeviceUnits(200), _homeRole.ClientSize.Width - LogicalToDeviceUnits(12));
    }

    private string HomeRoleSignature()
    {
        if (!HasLibrary || _settings.Role == null) return "";
        var now = DateTime.Now;
        return _settings.Role + "|" + _sessionId + "|" + string.Join(",", TodaysClasses(now).Select(x => x.Class.Id + ClassStatus(x.Class, x.Slot, now).Status));
    }

    private void RefreshHomeRole()
    {
        _homeRoleSignature = HomeRoleSignature();
        _homeRole.SuspendLayout();
        var old = _homeRole.Controls.Cast<Control>().ToList();
        _homeRole.Controls.Clear();
        foreach (var c in old) c.Dispose();
        bool show = HasLibrary && _settings.Role != null;
        _homeRole.Visible = show;
        if (show)
        {
            if (IsTeacher) AddTeacherHome();
            if (_settings.Role is "student" or "both") AddStudentHome();
        }
        FitHomeRoleHeader();
        _homeRole.ResumeLayout(true);
        UpdateLessonTimer();
    }

    private Label HomeHeader(string text, string? sub = null)
    {
        var l = new Label
        {
            Tag = "head", AutoSize = false, Height = LogicalToDeviceUnits(sub == null ? 34 : 50), Width = LogicalToDeviceUnits(600),
            Margin = new Padding(LogicalToDeviceUnits(6), LogicalToDeviceUnits(4), LogicalToDeviceUnits(6), 0),
            ForeColor = Theme.Text, RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold), Text = sub == null ? text : text + "\n", AccessibleDescription = sub
        };
        if (sub != null)
        {
            l.Paint += (_, e) =>
            {
                using var f = new Font("Segoe UI", 9f);
                var r = new Rectangle(0, l.Height / 2 + 2, l.Width, l.Height / 2 - 2);
                TextRenderer.DrawText(e.Graphics, sub, f, r, Theme.Muted, TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis);
            };
            l.TextAlign = ContentAlignment.TopRight;
        }
        _homeRole.Controls.Add(l);
        _homeRole.SetFlowBreak(l, true);
        return l;
    }

    private void AddTeacherHome()
    {
        var now = DateTime.Now;
        var today = TodaysClasses(now);
        string day = DayNames[(int)now.DayOfWeek];
        if (_library.Classes.Count == 0)
        {
            HomeHeader("👥  حصصك", "أضف مجموعاتك (الفصول) ومواعيدها مرة واحدة — وكل يوم تجد هنا حصصه، وكل مجموعة تكمل من حيث توقفت");
            _homeRole.Controls.Add(TeacherActionCard(Glyphs.Add, "أضف مجموعاتك وجدولها", "كل الفصول في جدول واحد: الاسم واليوم والساعة والمدة", Theme.Accent, () => ClassesManagerDialog()));
            return;
        }
        if (today.Count > 0)
        {
            int done = today.Count(x => ClassStatus(x.Class, x.Slot, now).Status == "انتهت ✓");
            HomeHeader($"👥  حصص اليوم — {day}", $"{today.Count} حصص" + (done > 0 ? $"  ·  انتهت {done}" : "") +
                "  ·  «ابدأ الحصة» يفتح ملفات المجموعة من حيث توقفت، و«إنهاء الحصة» يحفظ موضعها وواجبها");
            foreach (var (c, slot) in today) _homeRole.Controls.Add(ClassCard(c, slot, now));
        }
        else
        {
            var (nc, nslot, nday) = NextClass(now);
            HomeHeader($"👥  مجموعاتك — لا حصص اليوم ({day})", nc != null ? $"القادمة: {DayNames[nday]} {nslot!.Start} — {nc.Name}" : "لا مواعيد في الجدول بعد — أضفها من «المجموعات والجدول»");
            foreach (var c in _library.Classes) _homeRole.Controls.Add(ClassCard(c, null, now));
        }
        _homeRole.Controls.Add(TeacherActionCard(Glyphs.Calendar, "المجموعات والجدول", "أضف فصلًا أو غيّر المواعيد — كل المجموعات في جدول واحد", Theme.Folder, () => ClassesManagerDialog()));
    }

    /// <summary>حالة حصة اليوم: «جارية الآن» · «الآن» · «قادمة» · «انتهت ✓» · «فات موعدها».</summary>
    private (string Status, Color Color) ClassStatus(ClassGroup c, ClassSlot? slot, DateTime now)
    {
        var cur = c.CurrentSessionId == null ? null : _library.Sessions.FirstOrDefault(s => s.Id == c.CurrentSessionId);
        if (cur != null && cur.Id == _sessionId) return ("جارية الآن", Theme.Success);
        if (slot == null) return (cur is { Done: false } ? "لم تُنهَ" : "", Theme.Muted);
        var start = now.Date + slot.StartTime;
        var end = start.AddMinutes(slot.Minutes);
        bool doneToday = cur is { Done: true } && cur.UpdatedUtc.ToLocalTime().Date == now.Date;
        if (doneToday) return ("انتهت ✓", Theme.Muted);
        if (now >= start && now <= end) return ("الآن", Theme.Success);
        if (now < start) return ("قادمة", Theme.Text);
        return ("فات موعدها", Theme.Folder);
    }

    private Control ClassCard(ClassGroup c, ClassSlot? slot, DateTime now)
    {
        var cur = c.CurrentSessionId == null ? null : _library.Sessions.FirstOrDefault(s => s.Id == c.CurrentSessionId);
        var (status, statusColor) = ClassStatus(c, slot, now);
        bool running = cur != null && cur.Id == _sessionId;
        bool live = status is "الآن" or "جارية الآن";
        var card = new Card
        {
            Fill = live ? Modern.Blend(Theme.PanelLight, Theme.Success, 0.10) : Theme.PanelLight,
            Size = new Size(LogicalToDeviceUnits(316), LogicalToDeviceUnits(150)),
            Margin = new Padding(LogicalToDeviceUnits(6)),
            Padding = new Padding(14, 10, 14, 10),
            RightToLeft = RightToLeft.Yes
        };
        card.BackColor = card.Fill;
        string time = slot != null ? $"{slot.Start} – {(slot.StartTime + TimeSpan.FromMinutes(slot.Minutes)):hh\\:mm}" : ScheduleText(c);
        var head = new Label
        {
            Dock = DockStyle.Top, Height = 26, AutoEllipsis = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Theme.Text,
            Text = Modern.Bidi(c.Name), TextAlign = ContentAlignment.MiddleRight
        };
        var info = new Label
        {
            Dock = DockStyle.Top, Height = 20, AutoEllipsis = true, ForeColor = statusColor, TextAlign = ContentAlignment.MiddleRight,
            Text = time + (status.Length > 0 ? "  ·  " + status : "") + (running ? LessonRemainingText(cur!, now) : "")
        };
        string from = cur == null || cur.Tabs.Count == 0
            ? (_tabs.Count > 0 && cur == null ? "أول حصة: تبدأ بالملفات المفتوحة الآن" : "أول حصة: افتح ملفاتها بعد البدء")
            : (cur.Done ? "تبدأ من: " : "تتابع من: ") + string.Join("  ·  ", cur.Tabs.Take(2).Select(TabPlaceText)) + (cur.Tabs.Count > 2 ? $"  +{cur.Tabs.Count - 2}" : "");
        var place = new Label { Dock = DockStyle.Top, Height = 20, AutoEllipsis = true, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleRight, Text = from };
        string note = cur switch
        {
            { Done: false, Plan: { Length: > 0 } p } => "الخطة: " + FirstLine(p),
            { Homework: { Length: > 0 } h } => (cur.Done ? "واجب آخر حصة: " : "الواجب: ") + FirstLine(h),
            _ => ""
        };
        var noteLabel = new Label { Dock = DockStyle.Top, Height = 20, AutoEllipsis = true, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight, Text = note };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = LogicalToDeviceUnits(34), FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = card.Fill, Padding = Padding.Empty };
        PillButton Btn(string text, PillStyle style, Action click)
        {
            var b = new PillButton(text, style) { Height = LogicalToDeviceUnits(30), Margin = new Padding(0, 2, LogicalToDeviceUnits(6), 0) };
            b.Click += (_, _) => click();
            buttons.Controls.Add(b);
            return b;
        }
        if (running && cur != null)
        {
            Btn("⏹  إنهاء الحصة", PillStyle.Primary, () => EndLessonDialog(cur));
            Btn("📝 الخطة والواجب", PillStyle.Ghost, () => SessionNotesDialog(cur));
        }
        else
        {
            Btn(cur is { Done: false } ? "▶  تابع الحصة" : "▶  ابدأ الحصة", live || status == "قادمة" || slot == null ? PillStyle.Primary : PillStyle.Ghost, () => StartClass(c));
            Btn("📝 حضّر", PillStyle.Ghost, () => PrepareClass(c));
        }
        card.Controls.Add(buttons);
        card.Controls.Add(noteLabel);
        card.Controls.Add(place);
        card.Controls.Add(info);
        card.Controls.Add(head);
        _toolTip.SetToolTip(card, cur != null && cur.Tabs.Count > 0 ? string.Join("\n", cur.Tabs.Select(TabPlaceText)) : "تبدأ الحصة بالملفات المفتوحة، وكل ما تفتحه فيها يُحفظ لهذه المجموعة");
        return card;
    }

    private Control TeacherActionCard(string glyph, string title, string sub, Color color, Action click)
    {
        var t = new ActionTile(glyph, title, sub)
        {
            IconColor = color,
            Size = new Size(LogicalToDeviceUnits(316), LogicalToDeviceUnits(150)),
            Margin = new Padding(LogicalToDeviceUnits(6))
        };
        t.Click += (_, _) => click();
        _toolTip.SetToolTip(t, sub);
        return t;
    }

    private void AddStudentHome()
    {
        var list = _library.Sessions.Where(s => s.ClassId == null && !s.Done).OrderByDescending(s => s.Id == _sessionId).ThenByDescending(s => s.LastOpenedUtc).Take(4).ToList();
        HomeHeader("📚  جلساتي", list.Count == 0
            ? "الجلسة = ما تذاكره الآن (فيديو، كتاب، سبورة) محفوظًا باسم ومواضعه — تعود إليه بضغطة"
            : "تابع جلسة من حيث توقفت، أو ابدأ جلسة جديدة بالملفات المفتوحة");
        foreach (var s in list) _homeRole.Controls.Add(SessionCard(s));
        _homeRole.Controls.Add(TeacherActionCard(Glyphs.Add, "جلسة مذاكرة جديدة", _tabs.Count > 0 ? $"تحفظ التبويبات المفتوحة ({_tabs.Count}) باسم" : "افتح ما ستذاكره، وكل ما تفتحه يُحفظ فيها",
            Theme.Accent, () => NewSessionDialog(fromTabs: _tabs.Count > 0)));
    }

    private Control SessionCard(StudySession s)
    {
        bool open = s.Id == _sessionId;
        var card = new Card
        {
            Fill = open ? Modern.Blend(Theme.PanelLight, Theme.Accent, 0.12) : Theme.PanelLight,
            Size = new Size(LogicalToDeviceUnits(316), LogicalToDeviceUnits(150)),
            Margin = new Padding(LogicalToDeviceUnits(6)),
            Padding = new Padding(14, 10, 14, 10),
            RightToLeft = RightToLeft.Yes, Cursor = Cursors.Hand
        };
        card.BackColor = card.Fill;
        var head = new Label { Dock = DockStyle.Top, Height = 26, AutoEllipsis = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Theme.Text, Text = Modern.Bidi(s.Name), TextAlign = ContentAlignment.MiddleRight };
        var info = new Label
        {
            Dock = DockStyle.Top, Height = 20, AutoEllipsis = true, ForeColor = open ? Theme.Success : Theme.Muted, TextAlign = ContentAlignment.MiddleRight,
            Text = (open ? "مفتوحة الآن  ·  " : RelativeDay(s.LastOpenedUtc) + "  ·  ") + $"{s.Tabs.Count} تبويب"
        };
        var place = new Label
        {
            Dock = DockStyle.Top, Height = 20, AutoEllipsis = true, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleRight,
            Text = s.Tabs.Count == 0 ? "فارغة — افتح ملفاتها" : string.Join("  ·  ", s.Tabs.Take(2).Select(TabPlaceText))
        };
        var note = new Label
        {
            Dock = DockStyle.Top, Height = 20, AutoEllipsis = true, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight,
            Text = s.Homework is { Length: > 0 } h ? "الواجب: " + FirstLine(h) : s.Plan is { Length: > 0 } p ? "الخطة: " + FirstLine(p) : ""
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = LogicalToDeviceUnits(34), FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = card.Fill };
        var go = new PillButton(open ? "📝 الخطة والواجب" : "▶  تابع", open ? PillStyle.Ghost : PillStyle.Primary) { Height = LogicalToDeviceUnits(30), Margin = new Padding(0, 2, LogicalToDeviceUnits(6), 0) };
        go.Click += (_, _) => { if (open) SessionNotesDialog(s); else OpenSession(s); };
        buttons.Controls.Add(go);
        card.Controls.Add(buttons);
        card.Controls.Add(note);
        card.Controls.Add(place);
        card.Controls.Add(info);
        card.Controls.Add(head);
        if (!open) foreach (Control c in new Control[] { card, head, info, place, note }) c.Click += (_, _) => OpenSession(s);
        return card;
    }

    // =====================================================================
    //  المجموعات والجدول: كل الفصول في جدول واحد
    // =====================================================================

    /// <summary>
    /// جدول واحد لكل المجموعات: سطر لكل موعد (المجموعة · اليوم · الساعة · المدة). مجموعة لها أكثر من موعد = أكثر من سطر بنفس الاسم.
    /// تغيير الاسم في سطر مجموعة موجودة = إعادة تسمية (تبقى حصصها ومواضعها).
    /// </summary>
    internal void ClassesManagerDialog()
    {
        if (!EnsureLibrary()) return;
        using var kit = new FormKit("المجموعات والجدول", 700);
        kit.Header("مواعيد المجموعات");
        kit.Note("سطر لكل حصة في الأسبوع: اسم المجموعة (الفصل)، واليوم، وساعة البداية (مثل 08:30)، والمدة بالدقائق. " +
                 "مجموعة لها أكثر من حصة = أكثر من سطر بنفس الاسم. اترك اليوم فارغًا لمجموعة بلا موعد ثابت.");
        var grid = new DataGridView
        {
            AllowUserToAddRows = true, AllowUserToDeleteRows = true, AllowUserToResizeRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect,
            BackgroundColor = Theme.Input, BorderStyle = BorderStyle.None, GridColor = Theme.Border, EnableHeadersVisualStyles = false,
            RightToLeft = RightToLeft.Yes, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = LogicalToDeviceUnits(30), EditMode = DataGridViewEditMode.EditOnEnter
        };
        grid.RowTemplate.Height = LogicalToDeviceUnits(28);
        grid.DefaultCellStyle.BackColor = Theme.Input;
        grid.DefaultCellStyle.ForeColor = Theme.Text;
        grid.DefaultCellStyle.SelectionBackColor = Theme.AccentDark;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Panel;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        var colName = new DataGridViewTextBoxColumn { HeaderText = "المجموعة / الفصل", FillWeight = 40 };
        var colDay = new DataGridViewComboBoxColumn { HeaderText = "اليوم", FillWeight = 22, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing };
        colDay.Items.Add("—");
        colDay.Items.AddRange(DayNames.Cast<object>().ToArray());
        colDay.DefaultCellStyle.BackColor = Theme.Input;
        colDay.DefaultCellStyle.ForeColor = Theme.Text;
        var colStart = new DataGridViewTextBoxColumn { HeaderText = "الساعة", FillWeight = 18 };
        colStart.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        var colMin = new DataGridViewTextBoxColumn { HeaderText = "المدة (د)", FillWeight = 14 };
        colMin.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        var colId = new DataGridViewTextBoxColumn { Visible = false };
        grid.Columns.AddRange(colName, colDay, colStart, colMin, colId);
        grid.EditingControlShowing += (_, e) =>
        {
            e.Control.BackColor = Theme.Input;
            e.Control.ForeColor = Theme.Text;
        };
        grid.DataError += (_, e) => e.ThrowException = false;
        foreach (var c in _library.Classes)
        {
            if (c.Schedule.Count == 0) grid.Rows.Add(c.Name, "—", "", "45", c.Id);
            foreach (var slot in c.Schedule.OrderBy(x => x.Day).ThenBy(x => x.StartTime))
                grid.Rows.Add(c.Name, DayNames[Math.Clamp(slot.Day, 0, 6)], slot.Start, slot.Minutes.ToString(CultureInfo.InvariantCulture), c.Id);
        }
        // الصف الجديد يأخذ يوم اليوم والمدة المعتادة
        grid.DefaultValuesNeeded += (_, e) =>
        {
            e.Row.Cells[1].Value = DayNames[(int)DateTime.Now.DayOfWeek];
            e.Row.Cells[3].Value = _library.Classes.SelectMany(c => c.Schedule).Select(s => s.Minutes).DefaultIfEmpty(45).First().ToString(CultureInfo.InvariantCulture);
        };
        kit.Full(grid, 300);
        kit.Note("للحذف: حدّد السطر واضغط Delete. حذف كل سطور مجموعة يحذفها من القائمة (حصصها السابقة تبقى في «الحصص المحفوظة»).");
        kit.Buttons("حفظ");
        if (SmokeShowDialog(kit, "classes-manager")) return;
        if (kit.Show(this) != DialogResult.OK) return;
        grid.EndEdit();
        var rows = new List<(string Name, int Day, string Start, int Minutes, string? Id)>();
        foreach (DataGridViewRow r in grid.Rows)
        {
            if (r.IsNewRow) continue;
            string name = (r.Cells[0].Value as string ?? "").Trim();
            string dayText = r.Cells[1].Value as string ?? "—";
            string start = (r.Cells[2].Value as string ?? "").Trim();
            string minText = (r.Cells[3].Value as string ?? "").Trim();
            if (name.Length == 0 && start.Length == 0) continue;
            if (name.Length == 0) { Dialogs.Error(this, $"سطر بلا اسم مجموعة (الساعة {start})."); return; }
            int day = Array.IndexOf(DayNames, dayText);
            if (day >= 0 || start.Length > 0)
            {
                if (!TryParseTime(start, out var ts)) { Dialogs.Error(this, $"وقت غير صحيح لـ«{name}»: «{start}». اكتبه مثل 08:30."); return; }
                if (day < 0) { Dialogs.Error(this, $"اختر اليوم لـ«{name}» ({start})."); return; }
                start = ts.ToString(@"hh\:mm");
            }
            int minutes = int.TryParse(minText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? Math.Clamp(m, 5, 300) : 45;
            rows.Add((name, day, start, minutes, r.Cells[4].Value as string));
        }
        ApplyClassRows(rows);
    }

    internal static bool TryParseTime(string text, out TimeSpan ts)
    {
        string t = text.Trim().Replace('٫', ':').Replace('.', ':');
        foreach (var (a, b) in new[] { ('٠', '0'), ('١', '1'), ('٢', '2'), ('٣', '3'), ('٤', '4'), ('٥', '5'), ('٦', '6'), ('٧', '7'), ('٨', '8'), ('٩', '9') }) t = t.Replace(a, b);
        ts = default;
        if (t.Length == 0) return false;
        return TimeSpan.TryParse(t.Contains(':') ? t : t + ":00", CultureInfo.InvariantCulture, out ts) && ts >= TimeSpan.Zero && ts.TotalHours < 24;
    }

    /// <summary>يطبّق سطور الجدول: المجموعة الموجودة (بمعرّفها) تُعاد تسميتها ويُستبدل جدولها، والجديدة تُنشأ، والمحذوفة تُحذف.</summary>
    internal void ApplyClassRows(List<(string Name, int Day, string Start, int Minutes, string? Id)> rows)
    {
        var keep = new HashSet<string>();
        var byName = new Dictionary<string, ClassGroup>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var grp in rows.GroupBy(r => r.Id != null && _library.Classes.Any(c => c.Id == r.Id) ? "id:" + r.Id : "name:" + r.Name.ToLowerInvariant()))
        {
            var first = grp.First();
            ClassGroup? c = first.Id != null ? _library.Classes.FirstOrDefault(x => x.Id == first.Id) : null;
            c ??= byName.GetValueOrDefault(first.Name) ?? _library.Classes.FirstOrDefault(x => string.Equals(x.Name, first.Name, StringComparison.CurrentCultureIgnoreCase) && !keep.Contains(x.Id));
            if (c == null) { c = new ClassGroup { Name = first.Name }; _library.Classes.Add(c); }
            if (keep.Contains(c.Id))
                c.Schedule.AddRange(grp.Where(r => r.Day >= 0).Select(r => new ClassSlot { Day = r.Day, Start = r.Start, Minutes = r.Minutes }));
            else
            {
                c.Name = first.Name;
                c.Schedule = grp.Where(r => r.Day >= 0).Select(r => new ClassSlot { Day = r.Day, Start = r.Start, Minutes = r.Minutes }).ToList();
            }
            c.UpdatedUtc = DateTime.UtcNow;
            keep.Add(c.Id);
            byName[c.Name] = c;
        }
        foreach (var c in _library.Classes.Where(c => !keep.Contains(c.Id)).ToList())
        {
            _library.Classes.Remove(c);
            _deletedKeys.Add(LibrarySync.ClassKey(c));
        }
        MarkDirty();
        RefreshTabStrip();
        RefreshHome();
        ShowStatus($"📅 حُفظ الجدول: {_library.Classes.Count} مجموعة، {_library.Classes.Sum(c => c.Schedule.Count)} حصة في الأسبوع", 5);
    }
}
