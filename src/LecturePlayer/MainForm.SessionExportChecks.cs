using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

internal sealed partial class MainForm
{
    /// <summary>تصدير الحصة: PDF للطالب (غلاف + سبورة + صفحات مشروحة + واجب)، وحزمة «مسار» تُستقبل كجلسة، وفيديو بفصول.</summary>
    internal void RunSessionExportChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("session export: " + what); }
        static bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(20); } return cond(); }
        T Wait<T>(Task<T> t, int ms) { Check(Until(() => t.IsCompleted, ms), "timed out"); return t.Result; }

        // ---------- حسابات بلا واجهة ----------
        var items = new List<ScreenInk.Item> { new() { Tool = ScreenInk.Tool.Pen, Color = Color.Red, Width = 4, Pts = { new(100, 50), new(300, 150) } } };
        var norm = ScreenInk.Normalize(items, new SizeF(1000, 500));
        Check(Math.Abs(norm[0].Pts[1].X - 0.3f) < 1e-4 && Math.Abs(norm[0].Width - 0.008f) < 1e-5, "normalize");
        // مساحة أعرض من الشريحة (2:1 مقابل 16:9): الشريحة في الوسط بحواف على الجانبين
        var mapped = SlideItemsToImage(new[] { new ScreenInk.Item { Tool = ScreenInk.Tool.Pen, Width = 0.01f, Pts = { new(0.5f, 0.5f), new((1 - (16f / 9f) / 2f) / 2f, 0) } } }, 2f, 1600, 900);
        Check(Math.Abs(mapped[0].Pts[0].X - 800) < 1 && Math.Abs(mapped[0].Pts[0].Y - 450) < 1 && Math.Abs(mapped[0].Pts[1].X) < 1 && Math.Abs(mapped[0].Pts[1].Y) < 1 && Math.Abs(mapped[0].Width - 9) < 0.5, "slide letterbox mapping");
        var ch = ChaptersOf(new VideoItem { Path = "x", Bookmarks = { new Bookmark { TimeMs = 5000, Note = "المتغيرات" }, new Bookmark { TimeMs = 5300, Note = "قريبة جدًا" }, new Bookmark { TimeMs = 20000, Note = "" } } });
        Check(ch.Count == 3 && ch[0] == (0, "البداية") && ch[1].Title == "المتغيرات" && ch[2].Title == "علامة", "chapters from marks");

        if (!HasLibrary || !ReaderPane.Available) { Console.WriteLine("SMOKE session export: reader/library not available — skipped"); return; }
        string dir = Path.Combine(Path.GetTempPath(), "masar-sx-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string pdfFile = Path.Combine(dir, "درس اليوم.pdf");
        TestPdf.Write(pdfFile, 8, "Lesson Book");
        string boardFile = Path.Combine(dir, "سبورة الحصة" + BookFiles.BoardExtension);
        File.WriteAllText(boardFile, new JsonObject
        {
            ["v"] = 1, ["title"] = "سبورة الحصة",
            ["pages"] = new JsonArray(
                new JsonObject { ["id"] = "pA", ["w"] = 595, ["h"] = 842, ["bg"] = "white", ["pat"] = "grid", ["name"] = "" },
                new JsonObject { ["id"] = "pB", ["w"] = 595, ["h"] = 842, ["bg"] = "green", ["pat"] = "none", ["name"] = "" })
        }.ToJsonString(), new UTF8Encoding(false));
        JsonNode Rect(string id, string color) => JsonNode.Parse($$"""{"id":"{{id}}","t":"shape","k":"rect","x1":100,"y1":300,"x2":400,"y2":500,"c":"{{color}}","sw":3,"ly":"p","u":1}""")!;

        var shelf = AddShelf("تصدير الفحص", null);
        var added = AddBooks(new[] { pdfFile }, shelf);
        var book = added[0];
        var board = new Book { Title = "سبورة الحصة", Path = boardFile, LocalPath = boardFile, ShelfId = shelf.Id, PageCount = 2 };
        _library.Books.Add(board);
        InkStore.Save(InkStore.FileFor(_libraryPath, book.Id), JsonNode.Parse("{\"v\":1,\"pages\":{},\"del\":{}}")!.AsObject().Also(d => { d["pages"]!["2"] = new JsonArray(Rect("a", "#e03131")); d["pages"]!["5"] = new JsonArray(Rect("b", "#1971c2")); }));
        InkStore.Save(InkStore.FileFor(_libraryPath, board.Id), JsonNode.Parse("{\"v\":1,\"pages\":{},\"del\":{}}")!.AsObject().Also(d => { d["pages"]!["pA"] = new JsonArray(Rect("c", "#2f9e44")); }));

        // فيديو قصير بعلامتين (تسجيل الحصة)
        string? ff = Tools.FindFfmpeg();
        string video = Path.Combine(dir, "تسجيل الحصة.mp4");
        if (ff != null)
        {
            var mk = Wait(ProcessRunner.RunAsync(ff, new[] { "-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=duration=6:size=320x240:rate=10", "-c:v", "mpeg4", video }, TimeSpan.FromSeconds(60)), 60_000);
            if (mk.ExitCode != 0) Console.WriteLine("SMOKE session export: could not make a test video: " + mk.StdErr);
        }
        var course = new Course { Name = "تسجيلات الفحص" };
        var group = new LectureGroup { Name = "اليوم" };
        var rec = new VideoItem { Path = video, Title = "تسجيل الحصة", DurationMs = 6000, Bookmarks = { new Bookmark { TimeMs = 2000, Note = "المتغيرات" }, new Bookmark { TimeMs = 4000, Note = "الشروط" } } };
        group.Videos.Add(rec); course.Groups.Add(group); _library.Courses.Add(course);

        var session = new StudySession
        {
            Name = "حصة الفحص", Plan = "مقدمة\nالمتغيرات\nالشروط", Homework = "حل تمارين الصفحة 5",
            Tabs = { new WorkTab { Kind = WorkTab.BookKind, Ref = board.Id, Title = board.Title }, new WorkTab { Kind = WorkTab.BookKind, Ref = book.Id, Title = book.Title, Page = 5 } },
            Recordings = { video }
        };
        _library.Sessions.Add(session);
        var importedShelves = new List<Shelf>();
        var importedBooks = new List<Book>();
        try
        {
            SmokeExportFolder = Path.Combine(dir, "out");
            SmokeDialogShots = true;
            try { ExportSessionDialog(session); } finally { SmokeDialogShots = false; }
            bool withVideo = ff != null && File.Exists(video);
            var r = Wait(ExportSessionAsync(session, new SessionExportOptions(true, true, withVideo), SmokeExportFolder, CancellationToken.None), 180_000);
            Console.WriteLine($"SMOKE session export: pdf {r.PdfPages}p, zip {r.Zip != null}, videos {r.Videos.Count}, notes: {string.Join(" | ", r.Notes)}");
            Check(r.Pdf != null && File.Exists(r.Pdf) && r.PdfPages == 6, $"student pdf: cover + 2 board pages + 2 annotated pages + homework = 6 (got {r.PdfPages})");
            var probe = _reader!.ProbeAsync(r.Pdf!);
            Check(Until(() => probe.IsCompleted, 30_000) && probe.Result?.Pages == 6, "pdf opens with 6 pages: " + probe.Result?.Pages);
            Check(r.Zip != null && File.Exists(r.Zip), "zip made");
            using (var z = ZipFile.OpenRead(r.Zip!))
            {
                var names = z.Entries.Select(e => e.FullName).ToList();
                Check(names.Contains(SessionManifest) && names.Count(n => n.StartsWith("books/")) == 2 && names.Count(n => n.Contains(".annotations/")) == 2, "zip entries: " + string.Join(", ", names));
            }
            if (withVideo)
            {
                Check(r.Videos.Count == 1 && File.Exists(r.Videos[0]), "video with chapters");
                var info = Wait(ProcessRunner.RunAsync(ff!, new[] { "-hide_banner", "-i", r.Videos[0] }, TimeSpan.FromSeconds(30)), 30_000);
                Check(info.StdErr.Contains("Chapter #0:2") && info.StdErr.Contains("المتغيرات"), "chapters inside the mp4:\n" + info.StdErr);
                string txt = File.ReadAllText(Directory.GetFiles(r.Folder, "*الفصول.txt").Single());
                Check(txt.Contains("00:00 البداية") && txt.Contains("00:02 المتغيرات") && txt.Contains("00:04 الشروط"), "chapters text:\n" + txt);
            }

            // الاستقبال: رف + جلسة بتبويبات الكتب الجديدة وخطتها وواجبها
            LastImportedSession = null;
            var recv = ReceiveSharedShelfAsync(r.Zip!, null, Path.Combine(dir, "recv"));
            var got = Wait(recv, 60_000);
            Check(got != null && got.Books.Count == 2 && got.Annotations == 2, "package received: " + got?.Books.Count + "/" + got?.Annotations);
            importedShelves.AddRange(got!.Shelves); importedBooks.AddRange(got.Books);
            var s2 = LastImportedSession;
            Check(s2 != null && s2.Name == "حصة الفحص" && s2.Homework == session.Homework && s2.Plan == session.Plan && s2.Tabs.Count == 2
                  && s2.Tabs.All(t => got.Books.Any(b => b.Id == t.Ref)) && s2.Tabs[1].Page == 5, "session imported with mapped tabs");
            var nb = got.Books.First(b => !b.IsBoard);
            Check(InkStore.PagesWithInk(InkStore.Load(InkStore.FileFor(_libraryPath, nb.Id))).SequenceEqual(new[] { 2, 5 }), "imported ink");
            Console.WriteLine("SMOKE session export: ok");
        }
        finally
        {
            SmokeExportFolder = null;
            _library.Sessions.RemoveAll(x => x == session || x == LastImportedSession);
            _library.Courses.Remove(course);
            foreach (var b in importedBooks.Append(book).Append(board)) _library.Books.Remove(b);
            foreach (var s in importedShelves.Append(shelf)) _library.Shelves.Remove(s);
            HideBanner("import-session");
            RebuildTree();
            if (_shelfMode) RebuildShelf();
        }
    }
}

internal static class JsonObjectExtensions
{
    public static JsonObject Also(this JsonObject o, Action<JsonObject> f) { f(o); return o; }
}
