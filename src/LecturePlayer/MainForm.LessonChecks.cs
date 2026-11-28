using System.Text.Json;
using System.Text.Json.Nodes;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

internal sealed partial class MainForm
{
    /// <summary>«📘 شرح حصة»: القائمة والزرار، والحصة بسبورتها ودفترها (مقطع لكل حصة في دفتر المجموعة)، والمحتويات الثابتة والإضافة والإزالة.</summary>
    internal void RunLessonChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("lesson: " + what); }
        static bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(20); } return cond(); }
        T Wait<T>(Task<T> t, int ms) { Check(Until(() => t.IsCompleted, ms), "timed out"); if (t.IsFaulted) throw t.Exception!.GetBaseException(); return t.Result; }
        static IEnumerable<ToolStripMenuItem> All(ToolStripItemCollection c) =>
            c.OfType<ToolStripMenuItem>().SelectMany(i => new[] { i }.Concat(All(i.DropDownItems)));

        // ---------- القائمة والزرار ----------
        Check(_lessonQuick.Text!.Contains("حصة") && _lessonQuick.Available, "quick lesson button: " + _lessonQuick.Text);
        var keys = All(_lessonMenu.DropDownItems).Select(i => i.ShortcutKeys).ToList();
        Check(keys.Contains(Keys.Control | Keys.Alt | Keys.L) && keys.Contains(Keys.Control | Keys.Alt | Keys.O), "lesson shortcuts");
        if (!HasLibrary || !ReaderPane.Available) { Console.WriteLine("SMOKE lesson: menus ok, reader/library not available — skipped"); return; }

        SmokeDialogShots = true;
        try { NewLessonDialog(); } finally { SmokeDialogShots = false; }

        string dir = Path.Combine(Path.GetTempPath(), "masar-ls-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string pdf = Path.Combine(dir, "كتاب الحصة.pdf");
        TestPdf.Write(pdf, 6, "Lesson Book");
        string mp4 = Path.Combine(dir, "شرح المعلم.mp4");
        File.WriteAllBytes(mp4, new byte[2048]);
        bool hadLessons = _library.Shelves.Any(s => s.Kind == LessonsShelfKind), hadNotebooks = _library.Shelves.Any(s => s.Kind == NotebooksShelfKind);
        var cls = new ClassGroup { Name = "مجموعة الفحص" };
        _library.Classes.Add(cls);
        var made = new List<StudySession>();
        var books = new List<Book>();
        try
        {
            // ---------- الحصة الأولى: سبورة في «حصصي» ودفتر جديد للمجموعة ----------
            var s1 = Wait(CreateLessonAsync("حصة الفحص 1", cls.Id), 20_000)!;
            made.Add(s1);
            var board = _library.Books.FirstOrDefault(b => b.Id == s1.BoardId);
            var nb = _library.Books.FirstOrDefault(b => b.Id == s1.NotebookId);
            if (board != null) books.Add(board);
            if (nb != null) books.Add(nb);
            Check(ActiveSession == s1 && s1.Lesson && s1.ClassId == cls.Id && cls.CurrentSessionId == s1.Id, "lesson opened as the class lesson");
            Check(board != null && board.IsBoard && !board.IsNotebook && board.Title == "حصة الفحص 1 — سبورة" && _library.ShelfOf(board)?.Kind == LessonsShelfKind, "board in «حصصي»: " + board?.Title);
            Check(nb != null && nb.IsNotebook && cls.NotebookId == nb.Id && _library.ShelfOf(nb)?.Kind == NotebooksShelfKind && nb.Title == "دفتر — مجموعة الفحص", "class notebook: " + nb?.Title);
            Check(s1.Items is { Count: 2 } && s1.Tabs.Count == 2, "board + notebook items");
            Check(Until(() => ReaderShown && _currentBook == board, 20_000), "lesson opens on its board");
            Check(_tabStrip.SessionText.Contains("📘") && _tabStrip.ActionText.Contains("إنهاء"), "tab strip shows the lesson: " + _tabStrip.SessionText);

            // ---------- محتويات ثابتة: كتاب يُفتح ثم يُغلق تبويبه ويبقى ----------
            Check(AddFilesToLesson(s1, new[] { pdf, mp4 }, openFirst: true) == 2, "book + video added");
            var book = _library.Books.First(b => string.Equals(b.LocalPath, Path.GetFullPath(pdf), StringComparison.OrdinalIgnoreCase));
            books.Add(book);
            Check(_library.ShelfOf(book)?.Kind == LessonsShelfKind, "new book goes to «حصصي»");
            Check(Until(() => ReaderShown && _currentBook == book && _tabs.Any(t => t.Ref == book.Id), 20_000), "book opened in a tab");
            CloseTab(_tabs.First(t => t.Ref == book.Id));
            Check(!_tabs.Any(t => t.Ref == book.Id) && s1.Items!.Any(t => t.Ref == book.Id), "closed tab stays in the lesson");
            var link = AddLessonLink(s1, "https://youtu.be/dQw4w9WgXcQ?t=5", "رابط الفحص");
            Check(link != null && link.Path == "https://www.youtube.com/watch?v=dQw4w9WgXcQ" && LessonGroup(s1, false)?.Videos.Contains(link) == true, "youtube link (link only) in the lesson lecture");
            var lg = LessonGroup(s1, false)!;
            Check(_library.FindCourseOf(lg)?.Name == LessonsName && lg.Name == s1.Name && lg.Videos.Count == 2, "lesson lecture in course «حصصي»");
            var entries = LessonEntries(s1);
            Check(entries.Count == 5 && entries.Count(e => e.Glyph == "🔗") == 1 && entries.Count(e => e.Glyph == "🎬") == 1 && entries.Any(e => e.Detail == "سبورة الحصة") && entries.Any(e => e.Detail.StartsWith("دفتر الحصة")),
                "contents: " + string.Join(" | ", entries.Select(e => e.Glyph + e.Title + ":" + e.Detail)));
            SmokeDialogShots = true;
            try { LessonContentsDialog(s1); } finally { SmokeDialogShots = false; }
            RemoveLessonEntry(s1, entries.First(e => e.Book == book));
            Check(!s1.Items!.Any(t => t.Ref == book.Id) && _library.Books.Contains(book) && File.Exists(pdf), "«أزل من الحصة» keeps the file");
            RemoveLessonEntry(s1, entries.First(e => e.Video == link));
            Check(lg.Videos.Count == 1 && LessonEntries(s1).Count == 3, "link removed");

            // الحصة تُحفظ وتُقرأ بحقولها
            var back = JsonSerializer.Deserialize<StudySession>(JsonSerializer.Serialize(s1))!;
            Check(back.Lesson && back.Items?.Count == 2 && back.BoardId == s1.BoardId && back.NotebookSection == s1.NotebookSection && back.GroupId == s1.GroupId, "lesson fields persist");

            // ---------- الحصة الثانية والدفتر مفتوح: مقطع جديد في نفس الدفتر (من جوّه القارئ) ----------
            OpenBook(nb!);
            Check(Until(() => ReaderShown && _currentBook == nb && _reader!.Info != null, 20_000), "notebook open");
            var s2 = Wait(CreateLessonAsync("حصة الفحص 2", cls.Id), 20_000)!;
            made.Add(s2);
            if (_library.Books.FirstOrDefault(b => b.Id == s2.BoardId) is { } b2) books.Add(b2);
            Check(s1.Done && ActiveSession == s2 && cls.CurrentSessionId == s2.Id, "previous class lesson ended");
            Check(s2.NotebookId == nb!.Id && s2.NotebookSection != null && s2.NotebookSection != s1.NotebookSection, "same class notebook, new section");
            Check(Until(() => JsonNode.Parse(File.ReadAllText(nb.LocalPath)) is JsonObject m && (m["sections"] as JsonArray)?.Count == 2
                && m["sections"]![1]!["name"]?.GetValue<string>() == "حصة الفحص 2" && m["sec"]?.GetValue<string>() == s2.NotebookSection, 10_000), "section «حصة الفحص 2» saved in the notebook");

            // ---------- حصة بلا مجموعة والدفتر مقفول: دفتر جديد للحصة ----------
            var s3 = Wait(CreateLessonAsync("حصة حرة", null), 20_000)!;
            made.Add(s3);
            foreach (var id in new[] { s3.BoardId, s3.NotebookId }) if (_library.Books.FirstOrDefault(b => b.Id == id) is { } x) books.Add(x);
            Check(s2.Done && s3.ClassId == null && s3.NotebookId != nb.Id && _library.Books.First(b => b.Id == s3.NotebookId).Title == "حصة حرة — دفتر", "own notebook without a class");
            Until(() => false, 500);
            SaveSmokeShot(this, "lesson");
            Console.WriteLine("SMOKE lesson: ok");
        }
        finally
        {
            SmokeLessonFiles = null;
            if (_sessionId != null) EndSession(markDone: false);
            if (ReaderShown) CloseReader(showHome: true);
            foreach (var s in made) { _library.Sessions.Remove(s); }
            foreach (var b in books.Distinct())
            {
                if (b.IsBoard) try { File.Delete(b.LocalPath); } catch { }
                try { File.Delete(InkStore.FileFor(_libraryPath, b.Id)); } catch { }
                _library.Books.Remove(b);
            }
            foreach (var c in _library.Courses.Where(c => c.Name == LessonsName)) c.Groups.RemoveAll(g => made.Any(s => s.GroupId == g.Id));
            _library.Courses.RemoveAll(c => c.Name == LessonsName && c.Groups.Count == 0);
            _library.Classes.Remove(cls);
            if (!hadLessons) _library.Shelves.RemoveAll(s => s.Kind == LessonsShelfKind && !_library.Books.Any(x => x.ShelfId == s.Id));
            if (!hadNotebooks) _library.Shelves.RemoveAll(s => s.Kind == NotebooksShelfKind && !_library.Books.Any(x => x.ShelfId == s.Id));
            _tabs.Clear(); _activeTab = -1; TabsChanged();
            RebuildTree();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
