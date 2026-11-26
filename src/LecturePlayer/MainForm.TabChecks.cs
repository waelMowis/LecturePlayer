using LecturePlayer.Data;
using LecturePlayer.UI;
using LibVLCSharp.Shared;

namespace LecturePlayer;

internal sealed partial class MainForm
{
    /// <summary>التبويبات والجلسات والمجموعات: فتح وتبديل وإغلاق وحفظ واستعادة، وموضع كل مجموعة في حصتها.</summary>
    internal void RunTabChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("tabs: " + what); }
        static bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(20); } return cond(); }
        if (!HasLibrary) { Console.WriteLine("SMOKE tabs: no library — skipped"); return; }

        // ---------- البيانات: الدمج والحفظ ----------
        var a = new CourseLibrary(); var b = new CourseLibrary();
        var s1 = new StudySession { Name = "قديمة", Tabs = { new WorkTab { Kind = WorkTab.BookKind, Ref = "x", Page = 3 } }, UpdatedUtc = DateTime.UtcNow.AddHours(-1) };
        a.Sessions.Add(s1);
        b.Sessions.Add(new StudySession { Id = s1.Id, Name = "أحدث", Tabs = { new WorkTab { Kind = WorkTab.BookKind, Ref = "x", Page = 9 } }, UpdatedUtc = DateTime.UtcNow });
        b.Classes.Add(new ClassGroup { Name = "مجموعة", Schedule = { new ClassSlot { Day = 2, Start = "09:30", Minutes = 50 } } });
        LibrarySync.Merge(a, b);
        Check(a.Sessions.Count == 1 && a.Sessions[0].Name == "أحدث" && a.Sessions[0].Tabs[0].Page == 9 && a.Classes.Count == 1, "merge sessions/classes");
        var back = Storage.FromJson(Storage.Serialize(a), Path.Combine(Path.GetTempPath(), "t.lpl"));
        Check(back.Sessions[0].Tabs[0].Page == 9 && back.Classes[0].Schedule[0].StartTime == new TimeSpan(9, 30, 0), "serialize sessions/classes");
        var c2 = new CourseLibrary();
        LibrarySync.Merge(c2, a, new HashSet<string> { LibrarySync.SessionKey(a.Sessions[0]) });
        Check(c2.Sessions.Count == 0, "deleted session does not come back");

        if (!ReaderPane.Available) { Console.WriteLine("SMOKE tabs: reader not available — UI flow skipped"); return; }

        string dir = Path.Combine(Path.GetTempPath(), "masar-tabs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string pdf1 = Path.Combine(dir, "Tab Book.pdf"), pdf2 = Path.Combine(dir, "Class Book.pdf"), wav = Path.Combine(dir, "tab tone.wav");
        TestPdf.Write(pdf1, 12, "Tab Book");
        TestPdf.Write(pdf2, 20, "Class Book");
        WriteToneWav(wav, seconds: 30);

        string? role = _settings.Role;
        var keepTabs = _tabs.Select(t => t.Copy()).ToList();
        string? keepSession = _sessionId;
        var course = new Course { Name = "Tabs Course" };
        var group = new LectureGroup { Name = "Tabs Lecture" };
        var tone = new VideoItem { Path = wav, Title = "Tab tone", DurationMs = 30_000 };
        group.Videos.Add(tone);
        course.Groups.Add(group);
        Shelf? shelf = null;
        var sessionsBefore = _library.Sessions.ToList();
        var classesBefore = _library.Classes.ToList();
        try
        {
            SmokeAskText = s => s;
            _settings.Role = "student";
            CloseAllTabs();
            _sessionId = null;
            _library.Courses.Add(course);
            RebuildTree();
            shelf = AddShelf("تبويبات الفحص", null);
            var books = AddBooks(new[] { pdf1, pdf2 }, shelf);
            var book = books.First(x => x.LocalPath == Path.GetFullPath(pdf1));
            var classBook = books.First(x => x.LocalPath == Path.GetFullPath(pdf2));

            // كتاب ← تبويب
            OpenBook(book);
            Check(Until(() => ReaderShown && _currentBook == book && _reader!.Info?.Pages == 12, 20_000), "book opens");
            Check(_tabs.Count == 1 && _activeTab == 0 && _tabs[0].Kind == WorkTab.BookKind && _tabs[0].Ref == book.Id && _tabStrip!.Visible, "book tab added and strip shown");

            // فيديو ← تبويب ثانٍ، والكتاب يبقى تبويبًا
            PlayVideo(group, tone, 0);
            Check(Until(() => _mp.State == VLCState.Playing && _mp.Time > 800, 20_000), "video plays");
            Check(_tabs.Count == 2 && _activeTab == 1 && _tabs[1].Kind == WorkTab.CourseKind && _tabs[1].Ref == course.Id && _tabs[1].ItemPath == tone.Path && !ReaderShown, "video tab added");
            Check(_tabStrip!.Items.Count == 2 && _tabStrip.Items[1].Active && _tabStrip.Items[1].Glyph == "▶", "strip items");

            // التبديل: الفيديو يتوقف ويُحفظ موضعه، والكتاب يعود
            ActivateTab(0);
            Check(Until(() => ReaderShown && _currentBook == book, 20_000), "switch back to the book");
            Check(_currentVideo == null && _activeTab == 0 && tone.LastPositionMs > 500 && _tabs[1].PositionMs > 500, $"video stopped and its place saved ({tone.LastPositionMs})");
            // Ctrl+2 = التبويب الثاني
            Check(HandleTabKeys(Keys.Control | Keys.D2) && Until(() => _currentVideo == tone && _mp.State == VLCState.Playing, 20_000) && _activeTab == 1, "Ctrl+2 switches tab");
            Check(HandleTabKeys(Keys.Control | Keys.Tab) && Until(() => ReaderShown && _currentBook == book, 20_000), "Ctrl+Tab cycles");
            SaveSmokeShot(this, "tabs-strip");
            // الفأرة فوق الشريط بعد إغلاق تبويب وقبل إعادة الرسم (كان يرمي Index out of range عند إغلاق سبورة)
            var move = typeof(Control).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            _tabStrip.SetItems(new() { new TabStrip.Item("📖", "a", "", true), new TabStrip.Item("▶", "b", "", false), new TabStrip.Item("🧑‍🏫", "c", "", false) });
            _tabStrip.SetItems(new() { new TabStrip.Item("📖", "a", "", true) });
            for (int x = 0; x < _tabStrip.Width; x += 8) move.Invoke(_tabStrip, new object[] { new MouseEventArgs(MouseButtons.None, 0, x, _tabStrip.Height / 2, 0) });
            RefreshTabStrip();

            // جلسة من التبويبات المفتوحة
            var session = NewSessionDialog(fromTabs: true, name: "جلسة الفحص");
            Check(session != null && ActiveSession == session && session.Tabs.Count == 2 && _tabStrip.SessionActive && _tabStrip.SessionText.Contains("جلسة الفحص"), "session from open tabs");
            // إغلاق التبويب المعروض (Ctrl+W): يعود إلى الرئيسية، والجلسة تتبعه
            Check(HandleTabKeys(Keys.Control | Keys.W), "Ctrl+W handled");
            Check(!ReaderShown && _tabs.Count == 1 && _activeTab == -1 && _welcome.Visible && session!.Tabs.Count == 1 && session.Tabs[0].Ref == course.Id, "close shown tab → home, session mirrored");
            // الحفظ والاستعادة
            Check(_settings.OpenTabs.TryGetValue(_tabsKey, out var st) && st.Tabs.Count == 1 && st.SessionId == session.Id, "tabs state saved in settings");
            RestoreTabs();
            Check(_tabs.Count == 1 && _activeTab == -1 && _sessionId == session.Id && _tabStrip.Visible, "tabs restored");
            EndSession(markDone: false);
            Check(_sessionId == null && _tabs.Count == 1 && !session.Done, "session closed, tabs stay");
            CloseAllTabs();
            Check(_tabs.Count == 0 && _tabStrip.Items.Count == 0 && _tabStrip.Visible && _currentVideo == null, "all tabs closed (the 📚 button stays reachable)");
            OpenSession(session);
            Check(Until(() => _currentVideo == tone, 20_000) && _tabs.Count == 1 && _activeTab == 0 && ActiveSession == session, "session reopens its tabs");
            ShowSessionMenu(PointToScreen(Point.Empty));
            Check(LastSessionMenu?.Items.Count > 4, "session menu");
            LastSessionMenu?.Close();
            ShowTabMenu(0, PointToScreen(Point.Empty));
            Check(LastTabMenu?.Items.Count >= 6, "tab menu");
            LastTabMenu?.Close();
            EndSession(markDone: false);
            CloseAllTabs();

            // الدور يغيّر ما يظهر فعلًا: الشريط الجانبي، وأعلى الرئيسية، وترتيب قائمة الأدوات، وزر الجلسة
            string RoleText() => string.Join("|", _homeRole.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(c.Controls.Cast<Control>()).Concat(c.Controls.Cast<Control>().SelectMany(x => x.Controls.Cast<Control>())))
                .Select(c => c is ActionTile at ? at.Title + " " + at.Subtitle : c.Text + " " + c.AccessibleDescription));
            var savedSessions = _library.Sessions.ToList(); var savedClasses = _library.Classes.ToList();
            _library.Sessions.Clear(); _library.Classes.Clear();
            GoHome();
            SetRole("student");
            RefreshHome();
            Check(_homeRole.Visible && RoleText().Contains("جلساتي") && RoleText().Contains("جلسة مذاكرة جديدة") && !RoleText().Contains("حصص"), "student home: «جلساتي»: " + RoleText());
            Check(_activityReview.Visible && _activityStats.Visible && !_activityClasses.Visible && !_activityBoard.Visible && !_activityRecord.Visible, "student rail: review + stats, no board/record/classes");
            ShowToolsMenu(_activityTools);
            var heads = LastToolsMenu!.Items.OfType<ToolStripLabel>().Select(x => x.Text).ToList();
            LastToolsMenu.Close();
            Check(heads.IndexOf("المذاكرة") >= 0 && heads.IndexOf("المذاكرة") < heads.IndexOf("الشرح") && heads.Contains("الجلسات"), "student tools menu: study first: " + string.Join("|", heads));
            SetRole("teacher");
            RefreshHome();
            Check(RoleText().Contains("أضف مجموعاتك") && !RoleText().Contains("جلساتي"), "teacher home invites the classes table");
            Check(_activityClasses.Visible && _activityBoard.Visible && _activityRecord.Visible && !_activityReview.Visible && !_activityStats.Visible, "teacher rail: classes + board + record");
            Check(_tabStrip!.SessionText.Contains("👥") && _tabStrip.SessionText.Contains("الحصة"), "teacher session button «👥 الحصة»");
            ShowToolsMenu(_activityTools);
            heads = LastToolsMenu!.Items.OfType<ToolStripLabel>().Select(x => x.Text).ToList();
            LastToolsMenu.Close();
            Check(heads.IndexOf("الشرح") < heads.IndexOf("المذاكرة") && heads.Contains("الحصص"), "teacher tools menu: teaching first");
            var tmenu = TeacherMenuItems();
            Check(tmenu.OfType<ToolStripMenuItem>().Any(i => i.Text.Contains("المجموعات والجدول")) && tmenu.OfType<ToolStripMenuItem>().Any(i => i.Text.Contains("أستخدم البرنامج"))
                  && tmenu.OfType<ToolStripMenuItem>().Any(i => i.Text.Contains("الحصص المحفوظة")), "teacher menu: classes table + saved lessons + role switch");
            ShowClassesActivityMenu(_activityClasses);
            Check(LastClassesMenu?.Visible == true, "rail «الحصص» opens the classes menu");
            LastClassesMenu?.Close();
            var probe = new ClassGroup { Name = "مجموعة الغد", Schedule = { new ClassSlot { Day = ((int)DateTime.Now.DayOfWeek + 1) % 7, Start = "09:15", Minutes = 45 } } };
            _library.Classes.Add(probe);
            RefreshHome();
            Check(RoleText().Contains("لا حصص اليوم") && RoleText().Contains("09:15") && RoleText().Contains("مجموعة الغد"), "teacher home shows the classes and the next one: " + RoleText());
            SetRole("both");
            Check(_activityClasses.Visible && _activityReview.Visible && !_activityStats.Visible, "both: classes + review");
            SetRole("teacher");
            _library.Classes.Clear();
            _library.Sessions.AddRange(savedSessions); _library.Classes.AddRange(savedClasses);

            // جدول المجموعات: الوقت بالأرقام العربية، وإعادة التسمية بالمعرّف، والحذف بالغياب
            Check(TryParseTime("٨:٣٠", out var t1) && t1 == new TimeSpan(8, 30, 0) && TryParseTime("9.15", out var t2) && t2 == new TimeSpan(9, 15, 0) && !TryParseTime("25:00", out _), "time parsing");
            var keepCls = _library.Classes.Select(c => (c.Name, Day: -1, Start: "", Minutes: 45, Id: (string?)c.Id)).ToList();
            int today0 = (int)DateTime.Now.DayOfWeek;
            var rows = keepCls.Concat(new (string, int, string, int, string?)[]
            {
                ("فحص ٣/أ", today0, "08:00", 45, null), ("فحص ٣/أ", (today0 + 2) % 7, "10:00", 45, null),
                ("فحص ٣/ب", today0, "08:50", 45, null), ("فحص ٤/أ", today0, "09:40", 45, null),
                ("فحص ٤/ب", today0, "10:30", 45, null), ("فحص ٥/أ", today0, "11:20", 45, null)
            }).ToList();
            ApplyClassRows(rows);
            var a3 = _library.Classes.FirstOrDefault(c => c.Name == "فحص ٣/أ");
            Check(a3 != null && a3.Schedule.Count == 2 && _library.Classes.Count(c => c.Name.StartsWith("فحص")) == 5, "classes table creates 5 classes (one with 2 slots)");
            Check(TodaysClasses().Count(x => x.Class.Name.StartsWith("فحص")) == 5, "5 classes today");
            rows = rows.Where(r => r.Item1 != "فحص ٥/أ").Select(r => r.Item1 == "فحص ٣/أ" ? ("فحص ٣/أ (معدّل)", r.Item2, r.Item3, r.Item4, (string?)a3!.Id) : r).ToList();
            ApplyClassRows(rows);
            Check(a3!.Name == "فحص ٣/أ (معدّل)" && _library.Classes.Contains(a3) && a3.Schedule.Count == 2 && !_library.Classes.Any(c => c.Name == "فحص ٥/أ"), "rename keeps the class; a missing class is removed");
            GoHome();
            RefreshHome();
            Check(RoleText().Contains("حصص اليوم") && RoleText().Contains("فحص ٤/ب") && RoleText().Contains("ابدأ الحصة"), "teacher home: today's class cards");
            SaveSmokeShot(_welcome, "teacher-home");
            SmokeDialogShots = true;
            try { ClassesManagerDialog(); } finally { SmokeDialogShots = false; }
            _library.Classes.RemoveAll(c => c.Name.StartsWith("فحص"));

            // المعلم: مجموعة بجدول، والحصة التالية تبدأ من حيث توقفت المجموعة
            _settings.Role = "teacher";
            RefreshTabStrip();
            Check(SessionWord == "حصة" && IsTeacher && _tabStrip.SessionText.Contains("حصة"), "teacher wording");
            var cls = NewClassDialog("الصف الأول — أ")!;
            cls.Schedule.Add(new ClassSlot { Day = (int)DateTime.Now.DayOfWeek, Start = "00:00", Minutes = 1439 });
            Check(TodaysClasses().Any(x => x.Class == cls), "today's classes");
            var lesson1 = StartClass(cls)!;
            Check(ActiveSession == lesson1 && lesson1.ClassId == cls.Id && cls.CurrentSessionId == lesson1.Id && _tabs.Count == 0, "class session started");
            Check(_tabStrip.ActionText.Contains("إنهاء الحصة") && _tabStrip.SessionText.Contains("👥") && (_tabStrip.SessionText.Contains("متبقٍ") || DateTime.Now.TimeOfDay > new TimeSpan(23, 58, 0)), "lesson strip: name, time left, «إنهاء الحصة»: " + _tabStrip.SessionText);
            OpenBook(classBook);
            Check(Until(() => ReaderShown && _currentBook == classBook && _reader!.Info != null, 20_000), "class book opens");
            _ = _reader!.GoToAsync(7);
            Check(Until(() => classBook.LastPage == 7, 10_000), "class book page 7");
            lesson1.Plan = "المتغيرات"; lesson1.Homework = "حل تمارين الصفحة 7";
            EndSession(markDone: true);
            Check(lesson1.Done && lesson1.Tabs.Count == 1 && lesson1.Tabs[0].Page == 7 && _tabStrip.ActionText == "", "lesson ends with its place");
            CloseAllTabs();
            // مجموعة أخرى قرأت الكتاب نفسه إلى صفحة 3 (الموضع العام يتغيّر)
            OpenBook(classBook, 3);
            Check(Until(() => ReaderShown && classBook.LastPage == 3, 20_000), "global place moved to 3");
            CloseAllTabs();
            var lesson2 = StartClass(cls)!;
            Check(lesson2 != lesson1 && lesson2.Name.EndsWith("2") && lesson2.Tabs.Count == 1 && cls.CurrentSessionId == lesson2.Id && lesson2.StartTabs?[0].Page == 7, "next lesson continues the class files");
            Check(Until(() => ReaderShown && _currentBook == classBook && classBook.LastPage == 7, 20_000), $"class continues from its own page (7), not the global one ({classBook.LastPage})");
            GoHome();
            Check(Until(() => _welcome.Visible, 5000), "home");
            RefreshHome();
            Check(RoleText().Contains("الصف الأول — أ") && RoleText().Contains("جارية الآن") && RoleText().Contains("إنهاء الحصة"), "home card shows the running lesson: " + RoleText());
            SmokeDialogShots = true;
            try { SessionNotesDialog(lesson1); ClassScheduleDialog(cls); EndLessonDialog(lesson2); } finally { SmokeDialogShots = false; }
            ActivateTab(0);
            Check(Until(() => ReaderShown && _currentBook == classBook && _reader!.Info != null && classBook.LastPage == 7, 20_000), "back to the class book (page 7)");
            Thread.Sleep(400); Application.DoEvents();
            _ = _reader!.GoToAsync(9);
            Check(Until(() => classBook.LastPage == 9, 10_000), "lesson 2 reaches page 9");
            EndLesson(lesson2, "  تمارين الصفحة 9  ");
            Check(lesson2.Done && lesson2.Homework == "تمارين الصفحة 9" && lesson2.Tabs[0].Page == 9 && _sessionId == null && _tabs.Count == 1, "end lesson: homework + place, tabs stay open");

            // فصل آخر يأخذ الدرس نفسه لأول مرة: يبدأ من حيث بدأت الحصة السابقة (7)، لا من حيث انتهت (9)
            var clsB = NewClassDialog("الصف الأول — ب")!;
            var lessonB = StartClass(clsB)!;
            Check(lessonB.ClassId == clsB.Id && lessonB.Tabs.Count == 1 && lessonB.Tabs[0].Page == 7, $"first lesson of another class starts where the lesson started (7): {lessonB.Tabs.FirstOrDefault()?.Page}");
            Check(Until(() => ReaderShown && _currentBook == classBook && classBook.LastPage == 7, 20_000), "class B opens at page 7");
            // الانتقال لفصل آخر ينهي الحصة المفتوحة ويحفظ موضعها
            var lesson3 = StartClass(cls)!;
            Check(lessonB.Done && lesson3.ClassId == cls.Id && lesson3.Tabs[0].Page == 9 && ActiveSession == lesson3, "switching class ends the open lesson; class A continues from 9");
            Check(Until(() => ReaderShown && _currentBook == classBook && classBook.LastPage == 9, 20_000), "class A opens at page 9");
            SmokeDialogShots = true;
            try { PrepareClass(clsB); } finally { SmokeDialogShots = false; }
            var prepared = _library.Sessions.FirstOrDefault(x => x.Id == clsB.CurrentSessionId);
            Check(prepared != null && prepared != lessonB && !prepared.Done && prepared.Tabs.Count == 1 && prepared.Tabs[0].Page == 7 && ActiveSession == lesson3, "«حضّر» creates the next lesson of class B without opening it");
            EndSession(markDone: false);
            CloseAllTabs();
            Console.WriteLine("SMOKE tabs: ok");
        }
        finally
        {
            SmokeAskText = null;
            _settings.Role = role;
            CloseAllTabs();
            _sessionId = null;
            _library.Sessions.RemoveAll(s => !sessionsBefore.Contains(s));
            _library.Classes.RemoveAll(c => !classesBefore.Contains(c));
            _library.Courses.Remove(course);
            if (shelf != null)
            {
                _library.Books.RemoveAll(x => x.ShelfId == shelf.Id);
                _library.Shelves.Remove(shelf);
            }
            foreach (var t in keepTabs) _tabs.Add(t);
            _sessionId = keepSession;
            TabsChanged();
            RebuildTree();
            if (_shelfMode) RebuildShelf();
        }
    }
}
