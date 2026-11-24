using System.Net;
using System.Text;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

internal sealed partial class MainForm
{
    /// <summary>«الكتب» المرحلة 2: الروابط والتنزيل والتحديث والخاص (المتصفح/التنزيلات) وملفات الكورسات وموارد Moodle.</summary>
    internal void RunBookLinkChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("book links: " + what); }
        bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(20); } return cond(); }

        // ---------- الروابط ----------
        var d = BookSources.Parse("https://drive.google.com/file/d/1AbCdEfGhIjKlMnOpQrStUv/view?usp=sharing");
        Check(d is { Kind: BookSources.Kind.GoogleDrive, Id: "1AbCdEfGhIjKlMnOpQrStUv" } && d.Download.StartsWith("https://drive.usercontent.google.com/download?id=1AbCdEfGhIjKlMnOpQrStUv"), "drive: " + d?.Download);
        Check(BookSources.Parse("https://drive.google.com/open?id=1AbCdEfGhIjKlMnOpQrStUv")?.Kind == BookSources.Kind.GoogleDrive, "drive open?id");
        var docs = BookSources.Parse("https://docs.google.com/document/d/1ZyXwVuTsRqPoNmLkJiHg/edit");
        Check(docs is { Kind: BookSources.Kind.GoogleDocs } && docs.Download == "https://docs.google.com/document/d/1ZyXwVuTsRqPoNmLkJiHg/export?format=pdf", "docs: " + docs?.Download);
        var db = BookSources.Parse("https://www.dropbox.com/scl/fi/abc123/My%20Book.pdf?rlkey=xyz789&dl=0");
        Check(db is { Kind: BookSources.Kind.Dropbox } && db.Download.Contains("rlkey=xyz789") && db.Download.Contains("dl=1") && !db.Download.Contains("dl=0"), "dropbox: " + db?.Download);
        var od = BookSources.Parse("https://1drv.ms/b/s!AbCdEf123");
        Check(od is { Kind: BookSources.Kind.OneDrive } && od.Download.StartsWith("https://api.onedrive.com/v1.0/shares/u!") && od.Download.EndsWith("/root/content"), "onedrive: " + od?.Download);
        Check(BookSources.Parse("https://uni-my.sharepoint.com/:b:/g/personal/x/EaBc?e=1")!.Download.EndsWith("&download=1"), "sharepoint");
        Check(BookSources.Parse("https://example.com/files/book.pdf") is { Kind: BookSources.Kind.Direct } && BookSources.Parse("not a link") == null && BookSources.Parse("ftp://x/y.pdf") == null, "direct / junk");
        Check(BookSources.SameStamp("e:a|s:10", "e:a|s:10") && !BookSources.SameStamp("e:a", "e:b") && BookSources.SameStamp("s:10", "m:2020") && !BookSources.SameStamp("m:1|s:10", "m:1|s:11"), "stamps");
        Check(BookSources.LooksLikePdf(Encoding.ASCII.GetBytes("\n%PDF-1.7"), 9) && !BookSources.LooksLikePdf(Encoding.ASCII.GetBytes("<html>"), 6), "pdf sniff");

        // موارد Moodle: ملف PDF بيتقري من خدمة الويب
        var sections = Moodle.ParseContents("""
            [{"id":1,"name":"S1","section":1,"modules":[
              {"id":11,"name":"Slides","modname":"resource","url":"https://m.test/mod/resource/view.php?id=11",
               "contents":[{"type":"file","filename":"slides.pdf","mimetype":"application/pdf","fileurl":"https://m.test/webservice/pluginfile.php/5/mod_resource/content/1/slides.pdf?forcedownload=1"}]},
              {"id":12,"name":"Doc","modname":"resource","url":"https://m.test/mod/resource/view.php?id=12",
               "contents":[{"type":"file","filename":"notes.docx","mimetype":"application/msword","fileurl":"https://m.test/x.docx"}]}]}]
            """);
        Check(sections[0].Modules[0].Pdf?.EndsWith("slides.pdf?forcedownload=1") == true && sections[0].Modules[1].Pdf == null, "moodle pdf resource");
        var mc = new Course { Name = "m" };
        Moodle.Apply(mc, "https://m.test", new MoodleCourse(1, "m", null), sections, firstImport: true);
        Check(mc.AllVideos.First().FileUrl?.Contains("slides.pdf") == true && mc.AllVideos.First().IsMoodlePage, "moodle apply keeps the file");

        // ---------- سيرفر محلي: PDF بـ ETag، وصفحة «سجّل دخول» ----------
        string dir = Path.Combine(Path.GetTempPath(), "masar-booklinks-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string pdf = Path.Combine(dir, "src.pdf");
        TestPdf.Write(pdf, 12, "Linked", new TestPdf.Chapter("One", 1), new TestPdf.Chapter("Two", 6));
        byte[] pdfBytes = File.ReadAllBytes(pdf);
        string etag = "\"v1\"";
        var listener = new HttpListener();
        int port = 0;
        for (int tries = 0; tries < 5 && !listener.IsListening; tries++)
        {
            port = Random.Shared.Next(42000, 52000);
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try { listener.Start(); } catch (HttpListenerException) { }
        }
        Check(listener.IsListening, "local server");
        var serve = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { break; }
                try
                {
                    var res = ctx.Response;
                    if (ctx.Request.Url!.AbsolutePath.EndsWith("/private"))
                    {
                        byte[] html = Encoding.UTF8.GetBytes("<html><body>Sign in to continue</body></html>");
                        res.ContentType = "text/html";
                        res.OutputStream.Write(html);
                    }
                    else
                    {
                        res.ContentType = "application/pdf";
                        res.Headers["ETag"] = etag;
                        res.Headers["Content-Disposition"] = "attachment; filename=\"Linked Book.pdf\"";
                        string? range = ctx.Request.Headers["Range"];
                        if (range == "bytes=0-0")
                        {
                            res.StatusCode = 206;
                            res.Headers["Content-Range"] = $"bytes 0-0/{pdfBytes.Length}";
                            res.OutputStream.Write(pdfBytes, 0, 1);
                        }
                        else res.OutputStream.Write(pdfBytes);
                    }
                    res.Close();
                }
                catch { }
            }
        });

        string Url(string p) => $"http://127.0.0.1:{port}/{p}";
        var added = new List<Book>();
        var cacheFiles = new List<string>();
        try
        {
            SmokeAskText = s => s;
            SmokeDownloadsFolder = Path.Combine(dir, "Downloads");
            Directory.CreateDirectory(SmokeDownloadsFolder);
            var shelf = AddShelf("روابط الفحص", null);

            // تنزيل + اسم الملف من السيرفر + علامة النسخة
            string bookUrl = Url("files/book.pdf");
            var b = AddBookLinks(new[] { bookUrl }, shelf).Single();
            added.Add(b);
            cacheFiles.Add(BookSources.CachePathFor(bookUrl));
            Check(b.IsOnline && b.Title == "book" && AddBookLinks(new[] { bookUrl }, shelf).Count == 0, "add link (once): " + b.Title);
            var t = FetchBookAsync(b, open: false);
            Check(Until(() => t.IsCompleted, 20_000) && t.Result, "download the book");
            string cache = BookSources.CachePathFor(bookUrl);
            Check(File.Exists(cache) && File.ReadAllBytes(cache).Length == pdfBytes.Length && BookSources.ReadStamp(cache)?.Contains("e:v1") == true, "cached with stamp: " + BookSources.ReadStamp(cache));
            Check(b.Title == "Linked Book", "title from the server file name: " + b.Title);
            Check(Until(() => b.PageCount == 12, 20_000), "probe of the downloaded book: " + b.PageCount);

            // التحديث على المصدر
            var same = BookSources.ChangedAsync(bookUrl, BookSources.ReadStamp(cache), CancellationToken.None);
            Check(Until(() => same.IsCompleted, 10_000) && same.Result == false, "not changed: " + same.Result);
            etag = "\"v2\"";
            var changed = BookSources.ChangedAsync(bookUrl, BookSources.ReadStamp(cache), CancellationToken.None);
            Check(Until(() => changed.IsCompleted, 10_000) && changed.Result == true, "changed on the source");

            // يتفتح من النسخة المتنزلة
            OpenBook(b);
            Check(Until(() => ReaderShown && _currentBook == b && _reader!.Info?.Pages == 12, 20_000), "online book opens from the cache");
            Check(Until(() => _bannerKey == "book-dl", 10_000), "newer version offered");
            GoHome();

            // خاص: الرابط بيرجّع صفحة دخول → يعرض «هاته بحسابك»، ومن المتصفح بيتلقط من التنزيلات
            string privUrl = Url("files/private");
            var pb = AddBookLinks(new[] { privUrl }, shelf).Single();
            added.Add(pb);
            cacheFiles.Add(BookSources.CachePathFor(privUrl));
            var pt = FetchBookAsync(pb, open: false);
            Check(Until(() => pt.IsCompleted, 15_000) && !pt.Result && _bannerKey == "book-dl" && _bannerAction.Visible, "private link asks for sign-in");
            CaptureFromBrowser(pb, open: false);
            Thread.Sleep(300);
            File.WriteAllText(Path.Combine(SmokeDownloadsFolder, "old.txt"), "x");
            File.Copy(pdf, Path.Combine(SmokeDownloadsFolder, "Private Book.pdf"));
            Check(Until(() => File.Exists(BookSources.CachePathFor(privUrl)), 20_000), "picked up from the downloads folder");
            Check(Until(() => pb.Title == "Private Book" && pb.PageCount == 12, 20_000), "captured book named and probed: " + pb.Title + " " + pb.PageCount);

            // مرفق PDF في كورس ← القارئ، ويتحفظ في «ملفات الكورسات»
            var course = new Course { Name = "كورس الفحص" };
            var g = new LectureGroup { Name = "محاضرة" };
            course.Groups.Add(g);
            _library.Courses.Add(course);
            var att = new Attachment { Name = "شيت المحاضرة", Target = pdf };
            g.Attachments.Add(att);
            OpenAttachment(att);
            Check(Until(() => ReaderShown && _currentBook?.Path == pdf && _reader!.Info?.Pages == 12, 20_000), "attachment opens in the reader");
            var cf = _library.ShelfOf(_currentBook!);
            Check(cf?.Name == CourseFilesShelfName && _currentBook!.Title == "شيت المحاضرة", "course files shelf");
            added.Add(_currentBook!);

            // مورد Moodle (PDF) ← بيتنزل ويتفتح في القارئ
            string res = Url("files/moodle-slides.pdf");
            cacheFiles.Add(BookSources.CachePathFor(res));
            var mv = new VideoItem { Path = "https://moodle.masar.test/mod/resource/view.php?id=77", Title = "📎 سلايدات", FileUrl = res };
            g.Videos.Add(mv);
            PlayVideo(g, mv);
            Check(Until(() => ReaderShown && _currentBook?.Path == res && _reader!.Info?.Pages == 12, 25_000), "moodle pdf resource opens in the reader");
            added.Add(_currentBook!);
            GoHome();
            _library.Courses.Remove(course);

            // القوايم: «🔄» للكتاب من رابط قبل التعديل
            using (var m = new ContextMenuStrip())
            {
                BuildShelfNodeMenu(b, m.Items);
                var items = m.Items.OfType<ToolStripMenuItem>().Select(i => i.Text ?? "").ToList();
                var secs = items.Select(MenuSection).ToList();
                Check(items.Any(i => i.StartsWith("🔄")) && secs.All(x => x >= 0) && secs.Zip(secs.Skip(1)).All(p => p.First <= p.Second), "online book menu: " + string.Join(" | ", items));
            }
            Console.WriteLine("SMOKE book links: ok");
        }
        finally
        {
            SmokeAskText = null;
            StopDownloadsWatch();
            SmokeDownloadsFolder = null;
            if (ReaderShown) CloseReader(showHome: true);
            var ids = added.Select(x => x.Id).ToHashSet();
            _library.Books.RemoveAll(x => ids.Contains(x.Id));
            _library.Shelves.RemoveAll(s => s.Name is "روابط الفحص" or CourseFilesShelfName && !_library.Books.Any(x => x.ShelfId == s.Id));
            RebuildTree();
            try { listener.Stop(); } catch { }
            foreach (string f in cacheFiles) { try { File.Delete(f); File.Delete(f + ".stamp"); } catch { } }
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
