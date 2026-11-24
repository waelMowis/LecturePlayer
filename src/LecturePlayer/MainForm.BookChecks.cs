using System.Text;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>ملف PDF بسيط للفحص: صفحات فيها نص، وفهرس اختياري (فصول وأجزاء).</summary>
internal static class TestPdf
{
    public sealed record Chapter(string Title, int Page, params (string Title, int Page)[] Parts);

    public static void Write(string path, int pages, string title, params Chapter[] chapters)
    {
        var objs = new Dictionary<int, string>();
        int n = 0;
        int New() => ++n;
        int cat = New(), pagesId = New(), font = New(), outl = New();
        var pageIds = new List<int>();
        for (int i = 1; i <= pages; i++)
        {
            int pid = New(), cid = New();
            pageIds.Add(pid);
            string txt = $"BT /F1 36 Tf 72 700 Td (Page {i}) Tj ET BT /F1 14 Tf 72 650 Td (Masar reader smoke text {i}) Tj ET";
            objs[cid] = $"<< /Length {txt.Length} >>\nstream\n{txt}\nendstream";
            objs[pid] = $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {cid} 0 R >>";
        }
        objs[pagesId] = $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(p => p + " 0 R"))}] /Count {pages} >>";
        objs[font] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>";
        var items = chapters.Select(c => (Id: New(), C: c, Subs: c.Parts.Select(_ => New()).ToArray())).ToList();
        for (int k = 0; k < items.Count; k++)
        {
            var (id, c, subs) = items[k];
            string prev = k > 0 ? $"/Prev {items[k - 1].Id} 0 R " : "";
            string next = k < items.Count - 1 ? $"/Next {items[k + 1].Id} 0 R " : "";
            string kids = subs.Length > 0 ? $"/First {subs[0]} 0 R /Last {subs[^1]} 0 R /Count {subs.Length} " : "";
            objs[id] = $"<< /Title ({c.Title}) /Parent {outl} 0 R {prev}{next}{kids}/Dest [{pageIds[c.Page - 1]} 0 R /XYZ 0 792 null] >>";
            for (int j = 0; j < subs.Length; j++)
            {
                string pv = j > 0 ? $"/Prev {subs[j - 1]} 0 R " : "";
                string nx = j < subs.Length - 1 ? $"/Next {subs[j + 1]} 0 R " : "";
                objs[subs[j]] = $"<< /Title ({c.Parts[j].Title}) /Parent {id} 0 R {pv}{nx}/Dest [{pageIds[c.Parts[j].Page - 1]} 0 R /Fit] >>";
            }
        }
        objs[outl] = items.Count > 0 ? $"<< /Type /Outlines /First {items[0].Id} 0 R /Last {items[^1].Id} 0 R /Count {items.Count} >>" : "<< /Type /Outlines /Count 0 >>";
        objs[cat] = $"<< /Type /Catalog /Pages {pagesId} 0 R /Outlines {outl} 0 R >>";

        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new int[n + 1];
        for (int i = 1; i <= n; i++)
        {
            offsets[i] = Encoding.Latin1.GetByteCount(sb.ToString());
            sb.Append($"{i} 0 obj\n{objs[i]}\nendobj\n");
        }
        int xref = Encoding.Latin1.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {n + 1}\n0000000000 65535 f \n");
        for (int i = 1; i <= n; i++) sb.Append($"{offsets[i]:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {n + 1} /Root {cat} 0 R /Info << /Title ({title}) /Author (Masar Smoke) >> >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(sb.ToString()));
    }
}

internal sealed partial class MainForm
{
    /// <summary>«الكتب»: الرفوف والإضافة والقارئ والتتبّع والدمج والتراجع والقوايم.</summary>
    internal void RunBookChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("books: " + what); }
        bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(20); } return cond(); }

        // ---------- وحدات ----------
        var ps = PageSet.Parse("5,1-3,7-9,8");
        Check(ps.ToString() == "1-3,5,7-9" && ps.Count(0) == 7 && ps.Contains(8) && !ps.Contains(4), "page set: " + ps);
        Check(ps.Add(4) && ps.ToString() == "1-5,7-9" && !ps.Add(4), "page set add");
        Check(PageSet.Parse("x,3-1,,0").ToString() == "1-3", "page set junk");

        var ub = new Book { Title = "u", PageCount = 30, Chapters = new()
        {
            new() { Title = "Intro", Page = 1 }, new() { Title = "Two", Page = 5 }, new() { Title = "A", Page = 6, Level = 1 },
            new() { Title = "B", Page = 9, Level = 1 }, new() { Title = "Three", Page = 15 }, new() { Title = "App", Page = 28 }
        } };
        var secs = ub.Sections();
        Check(secs.Count == 4 && secs[1] is { From: 5, To: 14 } && secs[1].Parts.Count == 2 && secs[1].Parts[1] is { From: 9, To: 14 } && secs[3] is { From: 28, To: 30 },
            "sections: " + string.Join(" | ", secs.Select(s => $"{s.Title} {s.From}-{s.To}")));
        ub.ReadPages = "5-9";
        Check(Math.Abs(ub.FractionOf(5, 14) - 0.5) < 0.01 && !ub.IsDone, "chapter fraction");
        ub.ReadPages = "1-29";
        Check(ub.IsDone && ub.Fraction > 0.95, "95% = done");
        var noToc = new Book { PageCount = 60 };
        Check(noToc.Sections().Select(s => (s.From, s.To)).SequenceEqual(new[] { (1, 25), (26, 50), (51, 60) }), "parts of 25 without outline");

        // الدمج بين الجهازين: الصفحات بتتجمّع، والمكان من الأحدث، والبيانات من الأحدث
        var a = new Book { Id = "x", Title = "A", ReadPages = "1-5", LastPage = 5, ProgressUtc = DateTime.UtcNow.AddMinutes(-5), InfoUpdatedUtc = DateTime.UtcNow };
        var bOther = new Book { Id = "x", Title = "B-old", ReadPages = "10-12", LastPage = 12, ProgressUtc = DateTime.UtcNow, InfoUpdatedUtc = DateTime.UtcNow.AddMinutes(-9) };
        Check(LibrarySync.MergeBook(a, bOther) && a.ReadPages == "1-5,10-12" && a.LastPage == 12 && a.Title == "A", $"merge book: {a.ReadPages} {a.LastPage} {a.Title}");
        var libA = new CourseLibrary(); var libB = new CourseLibrary();
        var sh = new Shelf { Name = "s" }; libB.Shelves.Add(sh); libB.Books.Add(new Book { Title = "new", ShelfId = sh.Id, Path = @"C:\x.pdf" });
        var mr = LibrarySync.Merge(libA, libB);
        Check(libA.Shelves.Count == 1 && libA.Books.Count == 1 && mr.AddedBooks == 1, "merge adds shelf and book");
        var deleted = new HashSet<string> { LibrarySync.BookKey(libB.Books[0]) };
        var libC = new CourseLibrary();
        LibrarySync.Merge(libC, libB, deleted);
        Check(libC.Books.Count == 0, "deleted book does not come back");
        // الحفظ والقراية
        string json = Storage.Serialize(libB);
        var back = Storage.FromJson(json, Path.Combine(Path.GetTempPath(), "x.lpl"));
        Check(back.Books.Count == 1 && back.Books[0].ShelfId == sh.Id && back.Shelves[0].Name == "s", "serialize books");
        Check(!json.Contains("\"localPath\"", StringComparison.OrdinalIgnoreCase), "local path not saved");

        // ---------- الشاشة ----------
        string dir = Path.Combine(Path.GetTempPath(), "masar-books-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, "Sub Shelf"));
        string pdf1 = Path.Combine(dir, "Book One.pdf");
        string pdf2 = Path.Combine(dir, "Book Two.pdf");
        string pdf3 = Path.Combine(dir, "Sub Shelf", "Book Three.pdf");
        TestPdf.Write(pdf1, 30, "First Book", new("Intro", 1), new("Chapter Two", 5, ("Part A", 6), ("Part B", 9)), new("Chapter Three", 15), new("Appendix", 28));
        TestPdf.Write(pdf2, 60, "Second Book");
        TestPdf.Write(pdf3, 8, "Third Book");

        int readSeconds = _settings.ReadSeconds;
        string theme = _settings.ReaderTheme;
        bool wasShelf = _shelfMode;
        int shelvesBefore = _library.Shelves.Count, booksBefore = _library.Books.Count;
        var made = new List<object>();
        try
        {
            SmokeAskText = s => s;
            _settings.ReadSeconds = 1;
            if (_split.Panel2Collapsed) ToggleSidePanel();
            ToggleShelf();
            Check(_shelfMode && _shelfCard.Visible && !_treeCard.Visible && _activityBooks!.Active && !_activityExplorer.Active, "books panel shown");
            OnExplorerActivity();
            Check(!_shelfMode && _treeCard.Visible && _activityExplorer.Active && !_activityBooks.Active, "back to courses");
            ToggleShelf();

            var shelf = AddShelf("رف الفحص", null);
            made.Add(shelf);
            var added = AddBooks(new[] { pdf1 }, shelf);
            Check(added.Count == 1 && _shelfNodes.ContainsKey(added[0]) && _library.ShelfOf(added[0]) == shelf, "add book to shelf");
            var book1 = added[0];
            Check(AddBooks(new[] { pdf1 }, shelf).Count == 0, "same file not added twice");

            var folderShelf = AddBooksFromFolder(dir, shelf);
            made.Add(folderShelf);
            var sub = _library.ShelvesIn(folderShelf).FirstOrDefault();
            Check(folderShelf.SourceFolder != null && _library.ParentOf(folderShelf) == shelf && sub?.Name == "Sub Shelf", "folder = shelf, subfolder = sub-shelf");
            var book2 = _library.Books.First(b => b.LocalPath == Path.GetFullPath(pdf2));
            var book3 = _library.Books.First(b => b.LocalPath == Path.GetFullPath(pdf3));
            Check(book2.ShelfId == folderShelf.Id && book3.ShelfId == sub!.Id && _library.Books.Count(b => b.LocalPath == Path.GetFullPath(pdf1)) == 1, "folder books placed");
            File.Copy(pdf3, Path.Combine(dir, "Sub Shelf", "Book Four.pdf"));
            Check(RefreshShelfFolder(folderShelf, announce: false) == 1, "refresh brings the new book");

            if (!ReaderPane.Available) { Console.WriteLine("SMOKE books: reader not available (no WebView2) — UI flow skipped"); return; }

            // بيانات الكتب في الخلفية: عدد الصفحات والفهرس
            Check(Until(() => book1.PageCount == 30 && book2.PageCount == 60 && book3.PageCount == 8, 40_000), $"probe pages: {book1.PageCount}/{book2.PageCount}/{book3.PageCount}");
            Check(book1.Chapters.Count == 6 && book1.Chapters[2] is { Title: "Part A", Page: 6, Level: 1 } && book1.Author == "Masar Smoke" && book1.Fingerprint != null,
                "outline/author/fingerprint: " + string.Join(",", book1.Chapters.Select(c => c.Title + "@" + c.Page)));
            RebuildShelf();
            var n1 = _shelfNodes[book1];
            Check(n1.Nodes.Count == 4 && n1.Nodes[1].Nodes.Count == 2 && _shelfTree.RowOf(n1)?.Meta == "1/30", "chapters in the tree: " + n1.Nodes.Count + " " + _shelfTree.RowOf(n1)?.Meta);
            Check(_shelfNodes[book2].Nodes.Count == 3, "parts of 25 in the tree");

            // القارئ
            OpenBook(book1);
            Check(Until(() => ReaderShown && _reader!.Info?.Pages == 30, 30_000), "reader opens the book");
            Check(!_controlCard.Visible && !_welcome.Visible && Text.StartsWith(book1.Title), "reader replaces the player chrome");
            _ = _reader!.GoToAsync(7);
            Check(Until(() => book1.LastPage == 7, 10_000), "page change saved: " + book1.LastPage);
            Check(Until(() => book1.Read.Contains(7), 12_000), "page read after dwell: " + book1.ReadPages);
            Check(_pagesSession >= 1, "pages counted for the journal");
            var mark = AddPageMark(book1, 7, "مهم");
            Check(book1.Marks.Count == 1 && ShelfNodeOf(new BookMarkRef(book1, mark)) != null, "page mark in the tree");
            SaveSmokeShot(this, "books-reader");

            // فيديو بعد كتاب ← القارئ يتقفل، والرئيسية فيها «كمّل القراية»
            GoHome();
            Check(!ReaderShown && !_reader.Visible && _controlCard.Visible && _welcome.Visible, "home closes the reader");
            Check(_homeCards.Controls.Count > 0, "home shows the book card");
            if (_journal != null)
                Check(JournalEntries.Any(e => e.Kind == JournalEntry.Pages && e.CourseId == "book:" + book1.Id && e.Score >= 1), "pages in the study journal");
            SaveSmokeShot(this, "books-home");

            // يرجع لنفس الصفحة
            OpenBook(book1);
            Check(Until(() => ReaderShown && _reader.Current is { Page: 7 }, 20_000), "reopen resumes at the page: " + _reader.Current?.Page);
            OpenBook(book1, 15);
            Check(Until(() => _reader.Current is { Page: 15 }, 10_000), "chapter opens at its page: " + _reader.Current?.Page);
            OpenBook(book2);
            Check(Until(() => _reader.Info?.Pages == 60 && _currentBook == book2, 20_000), "switch to another book");
            GoHome();

            // تعليم وتصفير وتراجع (التقدّم الأحدث بيفضل)
            SetBooksDone(new[] { book2 }, true);
            Check(book2.IsDone && _shelfTree.RowOf(_shelfNodes[book2])?.Done == true, "mark book read");
            var sec = new BookSection(book1, "Chapter Two", 5, 14, 0);
            SetSectionRead(sec, true);
            Check(book1.FractionOf(5, 14) == 1, "mark chapter read");
            DeleteShelfItem(book3);
            Check(!_library.Books.Contains(book3), "remove book");
            UndoLast();
            Check(_library.Books.Any(b => b.Id == book3.Id) && _library.Books.First(b => b.Id == book1.Id).FractionOf(5, 14) == 1, "undo brings the book back (progress kept)");

            // القوايم بنفس القاعدة: المجموعات بالترتيب والحذف آخر حاجة
            void Menu(object? tag, string name, bool deleteLast)
            {
                using var m = new ContextMenuStrip();
                BuildShelfNodeMenu(tag, m.Items);
                var items = m.Items.OfType<ToolStripMenuItem>().Select(i => i.Text ?? "").ToList();
                Check(items.Count > 0 && items.All(x => MenuSection(x) >= 0), $"{name}: group icons: " + string.Join(" | ", items));
                var sections = items.Select(MenuSection).ToList();
                Check(sections.Zip(sections.Skip(1)).All(p => p.First <= p.Second), $"{name}: order: " + string.Join(" | ", items));
                if (deleteLast) Check(items[^1].StartsWith("🗑"), $"{name}: delete last");
            }
            var b1 = _library.Books.First(b => b.Id == book1.Id);
            Menu(_library.Shelves.First(s => s.Id == shelf.Id), "shelf", true);
            Menu(b1, "book", true);
            Menu(new BookSection(b1, "Intro", 1, 4, 0), "section", false);
            Menu(new BookMarkRef(b1, b1.Marks[0]), "mark", true);
            Menu(null, "empty", false);

            SaveSmokeShot(this, "books-shelf");
            Console.WriteLine("SMOKE books: ok");
        }
        finally
        {
            SmokeAskText = null;
            if (ReaderShown) CloseReader(showHome: true);
            _settings.ReadSeconds = readSeconds;
            _settings.ReaderTheme = theme;
            var ids = made.OfType<Shelf>().SelectMany(s => _library.Shelves.Where(x => x.Id == s.Id)).SelectMany(_library.ShelfTree).Select(s => s.Id).ToHashSet();
            _library.Books.RemoveAll(b => b.ShelfId != null && ids.Contains(b.ShelfId));
            _library.Shelves.RemoveAll(s => ids.Contains(s.Id));
            if (_library.Shelves.Count != shelvesBefore || _library.Books.Count != booksBefore) Console.WriteLine("SMOKE books: cleanup left extra items");
            ShowShelf(wasShelf);
            RebuildTree();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
