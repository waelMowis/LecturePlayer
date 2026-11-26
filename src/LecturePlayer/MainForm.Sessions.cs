using System.Globalization;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// الجلسات: مجموعة تبويبات محفوظة باسم (جلسة مذاكرة للطالب، أو حصة للمعلم) بمواضعها وخطتها وواجبها.
/// وللمعلم: مجموعات/صفوف لكل منها جدول أسبوعي وحصة حالية — الحصة التالية تبدأ من حيث توقفت المجموعة.
/// </summary>
internal partial class MainForm
{
    private string? _sessionId;

    internal StudySession? ActiveSession => _sessionId == null ? null : _library.Sessions.FirstOrDefault(s => s.Id == _sessionId);

    internal bool IsTeacher => _settings.Role is "teacher" or "both";
    /// <summary>التسمية حسب الدور: «حصة» للمعلم، و«جلسة» لغيره.</summary>
    internal string SessionWord => _settings.Role == "teacher" ? "حصة" : "جلسة";

    private static readonly string[] DayNames = { "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت" };

    /// <summary>كل تغيير في التبويبات يُنسخ إلى الجلسة النشطة (بمواضعها).</summary>
    private void MirrorSession()
    {
        if (ActiveSession is not { } s) return;
        var tabs = _tabs.Select(t => t.Copy()).ToList();
        bool same = tabs.Count == s.Tabs.Count && tabs.Zip(s.Tabs).All(p => p.First.Same(p.Second) && p.First.Page == p.Second.Page
            && p.First.PositionMs == p.Second.PositionMs && p.First.ItemPath == p.Second.ItemPath);
        if (same) return;
        s.Tabs = tabs;
        s.UpdatedUtc = DateTime.UtcNow;
        MarkPositionDirty();
    }

    // ---------------- قائمة الجلسة ----------------

    internal ContextMenuStrip? LastSessionMenu;

    private void ShowSessionMenu(Point screen)
    {
        var m = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        m.Items.AddRange(SessionMenuItems().ToArray());
        m.Closed += (_, _) => BeginInvoke(m.Dispose);
        m.Show(screen);
        LastSessionMenu = m;
    }

    /// <summary>عناصر قائمة الجلسة (زر الجلسة، و«الأدوات ← الجلسات»).</summary>
    private List<ToolStripItem> SessionMenuItems()
    {
        var items = new List<ToolStripItem>();
        if (!HasLibrary) { items.Add(new ToolStripMenuItem("افتح مكتبة أولًا") { Enabled = false }); return items; }
        string w = SessionWord;
        var s = ActiveSession;
        if (s != null)
        {
            items.Add(new ToolStripLabel($"{w} مفتوحة: {s.Name}") { ForeColor = Theme.Muted });
            items.Add(Item($"📝  الخطة والواجب…", () => SessionNotesDialog(s)));
            items.Add(Item($"📤  تصدير ال{w} (PDF للطالب، وحزمة «مسار»، والتسجيل بالفصول)…", () => ExportSessionDialog(s)));
            items.Add(Item("✏  إعادة التسمية…", () => RenameSession(s)));
            items.Add(Item(s.ClassId != null ? $"⏹  إنهاء ال{w} (المرة القادمة تبدأ من هنا)" : $"⏹  إغلاق ال{w} (تبقى التبويبات مفتوحة)", () => EndSession(markDone: s.ClassId != null)));
            items.Add(new ToolStripSeparator());
        }
        items.Add(Item($"➕  {w} جديدة من التبويبات المفتوحة…", () => NewSessionDialog(fromTabs: true)));
        items.Add(Item($"➕  {w} جديدة فارغة…", () => NewSessionDialog(fromTabs: false)));

        var recent = _library.Sessions.Where(x => x.Id != _sessionId).OrderByDescending(x => x.LastOpenedUtc).Take(15).ToList();
        var open = SubMenu($"📂  افتح {w}");
        foreach (var x in recent)
        {
            var target = x;
            string cls = x.ClassId != null && _library.Classes.FirstOrDefault(c => c.Id == x.ClassId) is { } cg ? $"  ·  {cg.Name}" : "";
            open.DropDownItems.Add(Item($"{(x.Done ? "✓ " : "")}{x.Name}{cls}  ·  {x.Tabs.Count} تبويب", () => OpenSession(target)));
        }
        if (recent.Count == 0) open.DropDownItems.Add(new ToolStripMenuItem("لا توجد بعد") { Enabled = false });
        items.Add(open);
        if (s != null) items.Add(Item($"🗑  احذف ال{w}…", () => DeleteSession(s)));

        items.Add(new ToolStripSeparator());
        if (IsTeacher) items.Add(ClassesMenu());
        items.Add(RoleMenu());
        return items;
    }

    // ---------------- إنشاء وفتح وإغلاق ----------------

    internal StudySession? NewSessionDialog(bool fromTabs, string? name = null, string? classId = null)
    {
        if (!EnsureLibrary()) return null;
        string w = SessionWord;
        string suggested = name ?? $"{(w == "حصة" ? "حصة" : "جلسة مذاكرة")} — {DateTime.Now.ToString("d MMMM", new CultureInfo("ar-EG"))}";
        if (name == null)
        {
            name = SmokeAskText != null ? SmokeAskText(suggested) : Dialogs.Input(this, $"{w} جديدة", $"اسم ال{w}:", suggested);
            if (string.IsNullOrWhiteSpace(name)) return null;
        }
        if (_activeTab >= 0) CaptureTab(_activeTab);
        var s = new StudySession { Name = name.Trim(), ClassId = classId, Tabs = fromTabs ? _tabs.Select(t => t.Copy()).ToList() : new() };
        _library.Sessions.Add(s);
        MarkDirty();
        if (fromTabs)
        {
            _sessionId = s.Id;
            TabsChanged();
            ShowStatus($"📚 حُفظت ال{w} «{s.Name}» — أي تبويب تفتحه أو تغلقه يُحفظ فيها", 5);
        }
        else OpenSession(s);
        return s;
    }

    /// <summary>يفتح جلسة: تبويباتها بدل المفتوحة الآن، ويفتح أولها من موضعه في الجلسة.</summary>
    internal void OpenSession(StudySession s)
    {
        if (_activeTab >= 0) CaptureTab(_activeTab);
        MirrorSession();
        _tabsBusy = true;
        try
        {
            if (PresentationShown) EndPresentation(showHome: false);
            FinishSuspendedPresentation();
            StopPlaybackCore(clearCurrent: true);
        }
        finally { _tabsBusy = false; }
        _tabs.Clear();
        foreach (var t in s.Tabs) if (!_tabs.Any(x => x.Same(t))) _tabs.Add(t.Copy());
        _sessionId = s.Id;
        _activeTab = -1;
        s.LastOpenedUtc = DateTime.UtcNow;
        MarkPositionDirty();
        TabsChanged();
        RefreshHome();
        if (_tabs.Count > 0) ActivateTab(0);
        else ShowStatus($"📚 «{s.Name}»: افتح ملفاتها (كتاب، عرض، سبورة، فيديو) وستُحفظ فيها", 6);
        if (!string.IsNullOrWhiteSpace(s.Plan) || !string.IsNullOrWhiteSpace(s.Homework))
            ShowBanner($"📚 {s.Name}" + (string.IsNullOrWhiteSpace(s.Plan) ? "" : "  ·  الخطة: " + FirstLine(s.Plan)) +
                       (string.IsNullOrWhiteSpace(s.Homework) ? "" : "  ·  الواجب: " + FirstLine(s.Homework)),
                BannerKind.Info, "الخطة والواجب", () => SessionNotesDialog(s), seconds: 12, key: "session");
    }

    private static string FirstLine(string? text)
    {
        string t = (text ?? "").Trim().Split('\n')[0].Trim();
        return t.Length > 70 ? t[..70] + "…" : t;
    }

    /// <summary>إغلاق الجلسة: تبقى التبويبات مفتوحة دون جلسة. حصة المجموعة تُعلَّم منتهية (التالية تبدأ من مواضعها).</summary>
    internal void EndSession(bool markDone)
    {
        if (ActiveSession is not { } s) return;
        if (_activeTab >= 0) CaptureTab(_activeTab);
        MirrorSession();
        if (markDone) { s.Done = true; s.UpdatedUtc = DateTime.UtcNow; MarkDirty(); }
        _sessionId = null;
        TabsChanged();
        ShowStatus(markDone ? $"✓ انتهت «{s.Name}» — الحصة القادمة تبدأ من حيث توقفتم" : $"أُغلقت «{s.Name}» — محفوظة في «افتح {SessionWord}»", 5);
        RefreshHome();
    }

    private void RenameSession(StudySession s)
    {
        string? name = Dialogs.Input(this, "إعادة التسمية", $"اسم ال{SessionWord}:", s.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        s.Name = name.Trim();
        s.UpdatedUtc = DateTime.UtcNow;
        MarkDirty();
        RefreshTabStrip();
    }

    private void DeleteSession(StudySession s)
    {
        if (SmokeAskText == null && !Dialogs.Confirm(this, $"هل تريد حذف «{s.Name}»؟\nلا تُحذف الملفات ولا التقدّم — فقط قائمة التبويبات والخطة والواجب.")) return;
        if (_sessionId == s.Id) _sessionId = null;
        _library.Sessions.Remove(s);
        _deletedKeys.Add(LibrarySync.SessionKey(s));
        foreach (var c in _library.Classes.Where(c => c.CurrentSessionId == s.Id)) { c.CurrentSessionId = null; c.UpdatedUtc = DateTime.UtcNow; }
        MarkDirty();
        TabsChanged();
        RefreshHome();
    }

    /// <summary>خطة الجلسة وواجبها (تظهر عند فتحها وفي الرئيسية، وتدخل في التصدير).</summary>
    internal void SessionNotesDialog(StudySession s)
    {
        using var kit = new FormKit($"الخطة والواجب — {s.Name}", 560);
        kit.Header("الخطة");
        var plan = kit.Full(new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Text = s.Plan ?? "" }, 120);
        kit.Note("ما ستشرحه أو تذاكره في هذه ال" + SessionWord + "، نقطة في كل سطر.");
        kit.Header("الواجب");
        var hw = kit.Full(new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Text = s.Homework ?? "" }, 90);
        kit.Buttons("حفظ");
        if (SmokeShowDialog(kit, "session-notes")) return;
        if (kit.Show(this) != DialogResult.OK) return;
        s.Plan = string.IsNullOrWhiteSpace(plan.Text) ? null : plan.Text.Trim();
        s.Homework = string.IsNullOrWhiteSpace(hw.Text) ? null : hw.Text.Trim();
        s.UpdatedUtc = DateTime.UtcNow;
        MarkDirty();
        RefreshHome();
    }

    // ---------------- المجموعات (للمعلم) ----------------

    private ToolStripMenuItem ClassesMenu()
    {
        var menu = SubMenu("👥  المجموعات والجدول");
        foreach (var c in _library.Classes)
        {
            var cls = c;
            var sub = SubMenu($"{c.Name}  ·  {ScheduleText(c)}");
            sub.DropDownItems.Add(Item("▶  ابدأ الحصة (من حيث توقفتم)", () => StartClass(cls)));
            sub.DropDownItems.Add(Item("📅  الجدول…", () => ClassScheduleDialog(cls)));
            sub.DropDownItems.Add(Item("✏  إعادة التسمية…", () => RenameClass(cls)));
            sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(Item("🗑  احذف المجموعة…", () => DeleteClass(cls)));
            menu.DropDownItems.Add(sub);
        }
        if (_library.Classes.Count > 0) menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(Item("➕  مجموعة أو صف جديد…", () => NewClassDialog()));
        return menu;
    }

    private static string ScheduleText(ClassGroup c) =>
        c.Schedule.Count == 0 ? "بلا جدول" : string.Join("، ", c.Schedule.OrderBy(x => x.Day).ThenBy(x => x.StartTime).Take(3).Select(x => $"{DayNames[Math.Clamp(x.Day, 0, 6)]} {x.Start}"))
                                              + (c.Schedule.Count > 3 ? "…" : "");

    internal ClassGroup? NewClassDialog(string? name = null)
    {
        if (!EnsureLibrary()) return null;
        name ??= SmokeAskText != null ? SmokeAskText("الصف الأول — أ") : Dialogs.Input(this, "مجموعة جديدة", "اسم المجموعة أو الصف (مثل «الصف الثالث — أ» أو «مجموعة السبت»):", "");
        if (string.IsNullOrWhiteSpace(name)) return null;
        var c = new ClassGroup { Name = name.Trim() };
        _library.Classes.Add(c);
        MarkDirty();
        if (SmokeAskText == null || SmokeDialogShots) ClassScheduleDialog(c);
        RefreshHome();
        return c;
    }

    private void RenameClass(ClassGroup c)
    {
        string? name = Dialogs.Input(this, "إعادة التسمية", "اسم المجموعة:", c.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        c.Name = name.Trim();
        c.UpdatedUtc = DateTime.UtcNow;
        MarkDirty();
        RefreshHome();
    }

    private void DeleteClass(ClassGroup c)
    {
        if (!Dialogs.Confirm(this, $"هل تريد حذف المجموعة «{c.Name}»؟\nتبقى حصصها السابقة في «افتح حصة».")) return;
        _library.Classes.Remove(c);
        _deletedKeys.Add(LibrarySync.ClassKey(c));
        MarkDirty();
        RefreshHome();
    }

    /// <summary>الجدول الأسبوعي: لكل يوم موعد (الساعة والمدة) أو لا شيء.</summary>
    internal void ClassScheduleDialog(ClassGroup c)
    {
        using var kit = new FormKit($"جدول «{c.Name}»", 560);
        kit.Header("المواعيد الأسبوعية");
        kit.Note("اكتب ساعة البداية (مثل 08:30) والمدة بالدقائق لكل يوم فيه حصة، واترك الأيام الأخرى فارغة.");
        var rows = new List<(int Day, TextBox Time, NumberBox Minutes)>();
        for (int d = 0; d < 7; d++)
        {
            var slot = c.Schedule.FirstOrDefault(x => x.Day == d);
            var panel = new Panel { BackColor = Theme.Panel };
            var time = FormKit.Style(new TextBox { Text = slot?.Start ?? "", PlaceholderText = "—", RightToLeft = RightToLeft.No, TextAlign = HorizontalAlignment.Center });
            var minutes = new NumberBox { Minimum = 10, Maximum = 240, Increment = 5, Value = Math.Clamp(slot?.Minutes ?? 45, 10, 240) };
            var lbl = new Label { Text = "دقيقة", ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight, RightToLeft = RightToLeft.Yes };
            panel.Controls.Add(time); panel.Controls.Add(minutes); panel.Controls.Add(lbl);
            panel.Layout += (_, _) =>
            {
                int h = panel.Height, x = panel.Width;
                time.SetBounds(x - 90, (h - time.PreferredHeight) / 2, 90, time.PreferredHeight); x -= 100;
                minutes.SetBounds(x - 90, 0, 90, h); x -= 96;
                lbl.SetBounds(x - 60, 0, 60, h);
            };
            kit.Row(DayNames[d], panel, 30);
            rows.Add((d, time, minutes));
        }
        kit.Buttons("حفظ");
        if (SmokeShowDialog(kit, "class-schedule")) return;
        if (kit.Show(this) != DialogResult.OK) return;
        var list = new List<ClassSlot>();
        foreach (var (d, time, minutes) in rows)
        {
            string t = time.Text.Trim().Replace('٫', ':').Replace('.', ':');
            if (t.Length == 0) continue;
            if (!TimeSpan.TryParse(t.Contains(':') ? t : t + ":00", CultureInfo.InvariantCulture, out var ts) || ts.TotalHours >= 24)
            {
                Dialogs.Error(this, $"وقت غير صحيح ليوم {DayNames[d]}: «{time.Text}». اكتبه مثل 08:30.");
                return;
            }
            list.Add(new ClassSlot { Day = d, Start = ts.ToString(@"hh\:mm"), Minutes = (int)minutes.Value });
        }
        c.Schedule = list;
        c.UpdatedUtc = DateTime.UtcNow;
        MarkDirty();
        RefreshHome();
    }

    /// <summary>
    /// ابدأ حصة المجموعة: إن كانت حصتها الحالية لم تنتهِ تُفتح، وإلا تُنشأ حصة جديدة بنفس ملفات السابقة
    /// ومن مواضعها — فكل مجموعة تكمل من حيث توقفت هي.
    /// </summary>
    internal StudySession? StartClass(ClassGroup c)
    {
        if (!EnsureLibrary()) return null;
        var cur = c.CurrentSessionId == null ? null : _library.Sessions.FirstOrDefault(s => s.Id == c.CurrentSessionId);
        if (cur != null && !cur.Done) { OpenSession(cur); return cur; }
        int n = _library.Sessions.Count(s => s.ClassId == c.Id) + 1;
        var next = new StudySession
        {
            Name = $"{c.Name} — حصة {n}",
            ClassId = c.Id,
            Tabs = cur?.Tabs.Select(t => t.Copy()).ToList() ?? new()
        };
        _library.Sessions.Add(next);
        c.CurrentSessionId = next.Id;
        c.UpdatedUtc = DateTime.UtcNow;
        MarkDirty();
        OpenSession(next);
        return next;
    }

    /// <summary>حصص اليوم (بالترتيب) من جداول المجموعات.</summary>
    internal List<(ClassGroup Class, ClassSlot Slot)> TodaysClasses(DateTime? now = null)
    {
        var day = (int)(now ?? DateTime.Now).DayOfWeek;
        return _library.Classes.SelectMany(c => c.Schedule.Where(s => s.Day == day).Select(s => (c, s)))
            .OrderBy(x => x.s.StartTime).ToList();
    }

    // ---------------- الرئيسية ----------------

    /// <summary>
    /// سطور الرئيسية حسب الدور: المعلم يرى حصص اليوم (أو الحصة القادمة، أو دعوة لإضافة أول مجموعة)،
    /// والطالب يرى «تابع» لآخر جلسة (أو دعوة لبدء جلسة مذاكرة).
    /// </summary>
    private IEnumerable<Control> SessionHomeRows(int rowH)
    {
        if (!HasLibrary || _settings.Role == null) yield break;
        Label Row(string text, Color color, Action click)
        {
            var row = new Label
            {
                Dock = DockStyle.Top, Height = rowH, Cursor = Cursors.Hand, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight,
                ForeColor = color, Text = text
            };
            row.Click += (_, _) => click();
            return row;
        }
        var now = DateTime.Now;
        if (IsTeacher)
        {
            if (_library.Classes.Count == 0)
                yield return Row("🧑‍🏫  أضف مجموعتك أو صفك الأول وجدوله — وبعدها «ابدأ الحصة» تكمل من حيث توقفتم  (اضغط)", Theme.Accent, () => NewClassDialog());
            var today = TodaysClasses(now);
            foreach (var (c, slot) in today.Take(4))
            {
                var start = now.Date + slot.StartTime;
                bool live = now >= start && now <= start.AddMinutes(slot.Minutes);
                bool past = now > start.AddMinutes(slot.Minutes);
                var cur = c.CurrentSessionId == null ? null : _library.Sessions.FirstOrDefault(s => s.Id == c.CurrentSessionId);
                string hw = cur?.Homework is { Length: > 0 } h ? "  ·  الواجب: " + FirstLine(h) : "";
                var target = c;
                yield return Row($"🧑‍🏫  {slot.Start} — {Modern.Bidi(c.Name)} ({slot.Minutes} د)" + (live ? "  ·  الآن" : "") + hw + "  (اضغط لبدء الحصة)",
                    live ? Theme.Success : past ? Theme.Muted : Theme.Text, () => StartClass(target));
            }
            if (today.Count == 0 && NextClass(now) is var (nc, nslot, nday) && nc != null)
                yield return Row($"🧑‍🏫  لا حصص اليوم — القادمة: {DayNames[nday]} {nslot!.Start} — {Modern.Bidi(nc.Name)}  (اضغط للتحضير)", Theme.Muted, () => StartClass(nc));
            else if (today.Count == 0 && _library.Classes.Count > 0)
                yield return Row("🧑‍🏫  لا مواعيد في جداول المجموعات بعد — أضفها من «📚 ← المجموعات والجدول»  (اضغط)", Theme.Muted, () => ClassScheduleDialog(_library.Classes[0]));
        }
        var last = _library.Sessions.Where(s => !s.Done && s.Id != _sessionId && s.Tabs.Count > 0 && s.ClassId == null).OrderByDescending(s => s.LastOpenedUtc).FirstOrDefault();
        if (last != null)
            yield return Row($"📚  تابع «{Modern.Bidi(last.Name)}» — {last.Tabs.Count} تبويب" + (last.Homework is { Length: > 0 } h ? "  ·  الواجب: " + FirstLine(h) : ""), Theme.Text, () => OpenSession(last));
        else if (_settings.Role == "student" && !_library.Sessions.Any())
            yield return Row("📚  افتح ما تذاكره (فيديو، كتاب) ثم احفظه «جلسة مذاكرة» من زر 📚 أعلى المساحة — تعود إليها بضغطة  (اضغط لإنشائها الآن)", Theme.Accent,
                () => NewSessionDialog(fromTabs: _tabs.Count > 0));
    }

    /// <summary>أقرب حصة قادمة بعد اليوم (خلال أسبوع).</summary>
    private (ClassGroup? Class, ClassSlot? Slot, int Day) NextClass(DateTime now)
    {
        for (int d = 1; d <= 7; d++)
        {
            int day = ((int)now.DayOfWeek + d) % 7;
            var hit = _library.Classes.SelectMany(c => c.Schedule.Where(s => s.Day == day).Select(s => (c, s))).OrderBy(x => x.s.StartTime).FirstOrDefault();
            if (hit.c != null) return (hit.c, hit.s, day);
        }
        return (null, null, 0);
    }

    /// <summary>تغيير الدور من القائمة أو الإعدادات: التسميات والرئيسية وقائمة الأدوات تتبعه فورًا.</summary>
    internal void SetRole(string role)
    {
        _settings.Role = role;
        SaveSettings();
        RefreshTabStrip();
        RefreshHome();
        ShowStatus(role switch { "teacher" => "👤 وضع المعلم: «حصة» والمجموعات وجدولها", "both" => "👤 طالب ومعلم: الجلسات والمجموعات معًا", _ => "👤 وضع الطالب: جلسات المذاكرة" }, 4);
    }

    private ToolStripMenuItem RoleMenu()
    {
        var m = SubMenu("👤  أستخدم البرنامج");
        foreach (var (key, text) in new[] { ("student", "طالبًا (جلسات مذاكرة)"), ("teacher", "معلمًا (حصص ومجموعات)"), ("both", "طالبًا ومعلمًا") })
        {
            var k = key;
            var it = Item(text, () => SetRole(k));
            it.Checked = (_settings.Role ?? "student") == key;
            m.DropDownItems.Add(it);
        }
        return m;
    }

    // ---------------- الدور (أول تشغيل) ----------------

    /// <summary>سؤال واحد عند أول تشغيل: يغيّر التسميات فقط ويُظهر المجموعات للمعلم.</summary>
    private void AskRoleIfNeeded()
    {
        if (_settings.Role != null || SmokeAskText != null) return;
        using var kit = new FormKit("كيف ستستخدم «مسار»؟", 520);
        kit.Note("الطالب: جلسات مذاكرة يحفظ فيها ما يفتحه ويعود إليه.\nالمعلم: حصص ومجموعات بجدول أسبوعي، وكل مجموعة تكمل من حيث توقفت، وتصدير الحصة للطلاب.\n" +
                 "يُسأل مرة واحدة، وتغيّره متى شئت من زر 📚 أعلى المساحة أو من الإعدادات ← شكل البرنامج.", 76, Theme.Text);
        var role = kit.Row("أنا", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
        role.Items.AddRange(new object[] { "طالب — أذاكر دوراتي وكتبي", "معلم — أشرح لطلابي ومجموعاتي", "الاثنان" });
        role.SelectedIndex = 0;
        kit.Buttons("متابعة");
        kit.Show(this);
        SetRole(role.SelectedIndex switch { 1 => "teacher", 2 => "both", _ => "student" });
    }
}
