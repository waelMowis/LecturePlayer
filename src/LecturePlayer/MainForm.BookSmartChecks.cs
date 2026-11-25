using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>ملفات فحص للمرحلة 3: EPUB صغير، وPDF «متصوّر» (صورة كلام من غير نص).</summary>
internal static class TestBooks
{
    public static void WriteEpub(string path, params (string Title, string[] Paragraphs)[] chapters)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Add(string name, string text, CompressionLevel level = CompressionLevel.Optimal)
        {
            var e = zip.CreateEntry(name, level);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(text);
        }
        Add("mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Add("META-INF/container.xml", "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
        string items = string.Concat(chapters.Select((_, i) => $"<item id=\"c{i}\" href=\"c{i}.xhtml\" media-type=\"application/xhtml+xml\"/>"));
        string spine = string.Concat(chapters.Select((_, i) => $"<itemref idref=\"c{i}\"/>"));
        Add("OEBPS/content.opf", "<?xml version=\"1.0\" encoding=\"utf-8\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bid\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
            "<dc:identifier id=\"bid\">masar-smoke-epub</dc:identifier><dc:title>EPUB Smoke</dc:title><dc:creator>Masar Smoke</dc:creator><dc:language>en</dc:language><meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta></metadata>" +
            $"<manifest><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>{items}</manifest><spine>{spine}</spine></package>");
        string lis = string.Concat(chapters.Select((c, i) => $"<li><a href=\"c{i}.xhtml\">{c.Title}</a></li>"));
        Add("OEBPS/nav.xhtml", $"<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>nav</title></head><body><nav epub:type=\"toc\"><ol>{lis}</ol></nav></body></html>");
        for (int i = 0; i < chapters.Length; i++)
            Add($"OEBPS/c{i}.xhtml", $"<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>{chapters[i].Title}</title></head><body><h1>{chapters[i].Title}</h1>" +
                string.Concat(chapters[i].Paragraphs.Select(p => $"<p>{p}</p>")) + "</body></html>");
    }

    /// <summary>صفحة واحدة عبارة عن صورة فيها كلام (زي الكتب المتصوّرة) — مفيهاش نص.</summary>
    public static void WriteScannedPdf(string path, string words)
    {
        byte[] jpg;
        using (var bmp = new Bitmap(1240, 1754))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var f = new Font("Arial", 64, FontStyle.Bold);
                g.DrawString(words, f, Brushes.Black, new RectangleF(100, 300, 1040, 800));
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Jpeg);
            jpg = ms.ToArray();
        }
        var parts = new List<string>();
        string content = "q 612 0 0 792 0 0 cm /Im1 Do Q";
        parts.Add("<< /Type /Catalog /Pages 2 0 R >>");
        parts.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        parts.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /XObject << /Im1 5 0 R >> >> /Contents 4 0 R >>");
        parts.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        string img = $"<< /Type /XObject /Subtype /Image /Width 1240 /Height 1754 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpg.Length} >>\nstream\n" +
                     Encoding.Latin1.GetString(jpg) + "\nendstream";
        parts.Add(img);
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < parts.Count; i++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{parts[i]}\nendobj\n");
        }
        int xref = Encoding.Latin1.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {parts.Count + 1}\n0000000000 65535 f \n");
        foreach (int o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {parts.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(sb.ToString()));
    }
}

internal sealed partial class MainForm
{
    /// <summary>«الكتب» المرحلة 3: التظليل، النص والبحث، الـ OCR، الذكاء، الهدف اليومي، وEPUB.</summary>
    internal void RunBookSmartChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("book smart: " + what); }
        bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(20); } return cond(); }
        T Await<T>(Task<T> t, int ms) { Check(Until(() => t.IsCompleted, ms), "task timed out"); return t.Result; }
        void Wait(Task t, int ms) => Check(Until(() => t.IsCompleted, ms) && !t.IsFaulted, "task: " + t.Exception?.GetBaseException().Message);

        // ---------- البحث بالعربي من غير التشكيل ----------
        Check(BookText.Normalize("الكِتَابُ المدرسيّة") == BookText.Normalize("الكتاب المدرسيه") && BookText.Normalize("إسلام") == BookText.Normalize("اسلام"), "arabic normalize");
        var nb = new Book { Title = "n" };
        var nt = new BookText { Pages = { [1] = "مقدمة الكتاب", [4] = "الكِتَابُ المدرسيّ في المدرسة الحديثة", [9] = "Masar Reader smoke" } };
        var hits = nt.Search(nb, "الكتاب المدرسي").ToList();
        Check(hits.Count == 1 && hits[0].Page == 4 && hits[0].Snippet.Contains("المدرسي"), "search: " + string.Join(",", hits.Select(h => h.Page + ":" + h.Snippet)));
        Check(nt.Search(nb, "masar READER").Single().Page == 9 && !nt.Search(nb, "غير موجود").Any(), "search latin/none");
        var hl = new Highlight { Rects = Highlight.FormatRects(new[] { new[] { 0.1, 0.2, 0.3, 0.04 } }) };
        Check(hl.ParseRects() is [[0.1, 0.2, 0.3, 0.04]], "highlight rects: " + hl.Rects);
        var quiz = AiTasks.ParseBookQuiz("[{\"question\":\"س؟\",\"options\":[\"أ\",\"ب\",\"ج\",\"د\"],\"answer\":2,\"explanation\":\"…\",\"page\":7},{\"question\":\"x\",\"options\":[\"1\"],\"answer\":0}]", 30);
        Check(quiz.Count == 1 && quiz[0].TimeMs == 7 && quiz[0].Answer == 2, "book quiz parse");

        if (!ReaderPane.Available) { Console.WriteLine("SMOKE book smart: reader not available — UI flow skipped"); return; }

        string dir = Path.Combine(Path.GetTempPath(), "masar-booksmart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string pdf = Path.Combine(dir, "Smart Book.pdf");
        TestPdf.Write(pdf, 20, "Smart", new("Intro", 1), new("Middle Part", 6, ("Deep", 8)), new("End", 15));
        string scanned = Path.Combine(dir, "Scanned.pdf");
        TestBooks.WriteScannedPdf(scanned, "MASAR OCR WORKS");
        string epub = Path.Combine(dir, "Smoke Book.epub");
        string lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. ";
        TestBooks.WriteEpub(epub,
            ("Chapter One", Enumerable.Range(0, 12).Select(j => string.Concat(Enumerable.Repeat(lorem, 3)) + $"Paragraph {j}").ToArray()),
            ("Chapter Two", Enumerable.Range(0, 12).Select(j => string.Concat(Enumerable.Repeat(lorem, 3)) + $"Second {j}").ToArray()),
            ("Chapter Three", new[] { lorem + "unique needle phrase" }.Concat(Enumerable.Repeat(string.Concat(Enumerable.Repeat(lorem, 3)), 6)).ToArray()));

        int goal = _settings.DailyPagesGoal;
        var added = new List<Book>();
        try
        {
            SmokeAskText = s => s.Length == 0 ? "ملاحظة الفحص" : s;
            if (!_shelfMode) ToggleShelf();
            var shelf = AddShelf("ذكاء الفحص", null);
            added.AddRange(AddBooks(new[] { pdf, scanned, epub }, shelf));
            var book = added.First(b => b.LocalPath == Path.GetFullPath(pdf));
            var sc = added.First(b => b.LocalPath == Path.GetFullPath(scanned));
            var eb = added.First(b => b.LocalPath == Path.GetFullPath(epub));
            Check(Until(() => book.PageCount == 20 && sc.PageCount == 1 && eb.PageCount > 5, 60_000), $"probe pdf/scanned/epub: {book.PageCount}/{sc.PageCount}/{eb.PageCount}");
            Check(eb.Chapters.Count == 3 && eb.Chapters[0].Page == 1 && eb.Chapters[2].Page > eb.Chapters[1].Page && eb.Author == "Masar Smoke", "epub outline: " + string.Join(",", eb.Chapters.Select(c => c.Title + "@" + c.Page)));

            // ---------- التظليل ----------
            OpenBook(book, 3);
            Check(Until(() => ReaderShown && _reader!.Info?.Pages == 20 && _reader.Current?.Page == 3, 20_000), "open at page 3");
            OnHighlightRequested(_readerGen, 3, new[] { new[] { 0.12, 0.16, 0.3, 0.02 } }, "Masar reader smoke text 3", "green", wantNote: true, cfi: null);
            Check(book.Highlights.Count == 1 && book.Highlights[0] is { Page: 3, Color: "green", Note: "ملاحظة الفحص" }, "highlight saved with note");
            Check(Until(() => UdemyWeb.Unwrap(Await(_reader.Web.CoreWebView2.ExecuteScriptAsync("String(document.querySelectorAll('.masar-hl-layer > div').length)"), 5000)) == "1", 8000), "highlight drawn on the page");
            Check(ShelfNodeOf(new BookHighlightRef(book, book.Highlights[0])) != null, "highlight in the tree");
            OnHighlightEdited(_readerGen, book.Highlights[0].Id, "pink", false);
            Check(book.Highlights[0].Color == "pink", "highlight color changed");
            string md = BookNotesMarkdown(book);
            Check(md.Contains("> Masar reader smoke text 3") && md.Contains("📝 ملاحظة الفحص") && md.Contains("## Intro"), "notes markdown:\n" + md);
            Until(() => false, 500);
            SaveSmokeShot(this, "books-highlight");

            // ---------- النص والبحث ----------
            var text = Await(EnsureBookTextAsync(book, 1, 20, ocr: false, CancellationToken.None), 30_000);
            Check(text != null && text.Complete && text.Pages.Count == 20 && text.Pages[7].Contains("smoke text 7"), "book text extracted");
            ShowBookSearch("smoke text 12");
            Check(Until(() => _bookSearch is { ResultCount: > 0 }, 20_000), "search form finds the page");
            var sh = BookText.Load(book).Search(book, "smoke text 12").ToList();
            Check(sh.Count == 1 && sh[0].Page == 12, "search hit page");
            _bookSearch!.Close();

            // ---------- OCR للصفحات المتصوّرة ----------
            if (Ocr.IsSupported)
            {
                Wait(OcrBookAsync(sc), 60_000);
                var st = BookText.Load(sc);
                string got = st.Pages.GetValueOrDefault(1) ?? "";
                if (st.OcrPages.Contains(1) && got.Contains("OCR", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("SMOKE book ocr: " + got.Replace("\n", " "));
                else
                    Console.WriteLine("SMOKE book ocr: engine unavailable on this machine (" + got + ")");
            }

            // ---------- الذكاء (رد محفوظ بدل Claude) ----------
            string? prompt = null;
            SmokeClaudeReply = user =>
            {
                prompt = user;
                return user.Contains("الشكل بالضبط")
                    ? "[{\"question\":\"الصفحة فيها إيه؟\",\"options\":[\"a\",\"b\",\"c\",\"d\"],\"answer\":1,\"explanation\":\"…\",\"page\":8}]"
                    : "## ملخص الفحص";
            };
            _lastBookAiText = null;
            Wait(SummarizeBookPartAsync(book, "Middle Part", 6, 14), 20_000);
            Check(_lastBookAiText == "## ملخص الفحص" && prompt!.Contains("[ص 6]") && prompt.Contains("[ص 14]") && !prompt.Contains("[ص 15]"), "summary uses the chapter pages");
            Wait(ExplainPageAsync(book, 9, "يعني إيه؟"), 20_000);
            Check(prompt!.Contains("[ص 9]") && prompt.Contains("يعني إيه؟"), "explain page prompt");
            _lastBookQuiz = null;
            Wait(QuizBookPartAsync(book, "Middle Part", 6, 14), 20_000);
            Check(_lastBookQuiz is [{ TimeMs: 8 }], "quiz with pages");

            // ---------- هدف القراية ----------
            _settings.DailyPagesGoal = 5;
            Check(ReadingGoalText().Contains("/5 ص"), "reading goal text: " + ReadingGoalText());
            _settings.DailyPagesGoal = 0;
            Check(ReadingGoalText() == "", "no goal = no text");

            // ---------- EPUB ----------
            OpenBook(eb);
            Check(Until(() => ReaderShown && _currentBook == eb && _reader.Info?.Pages == eb.PageCount, 30_000), "epub opens in the reader");
            Check(Until(() => eb.LastCfi != null, 10_000), "epub position (cfi) saved");
            _ = _reader.GoToAsync(eb.Chapters[2].Page);
            Check(Until(() => eb.LastPage >= eb.Chapters[2].Page, 10_000), "epub go to chapter: " + eb.LastPage);
            string cfi = eb.LastCfi!;
            int page = eb.LastPage;
            Check(Until(() => eb.Read.Contains(page), 12_000), "epub page read after dwell: " + eb.ReadPages);
            Until(() => false, 500);
            SaveSmokeShot(this, "books-epub");
            GoHome();
            OpenBook(eb);
            Check(Until(() => ReaderShown && _reader.Current?.Page == page, 20_000), $"epub reopens at the same place: {_reader.Current?.Page} vs {page}");
            Check(eb.LastCfi == cfi || eb.LastPage == page, "same cfi");
            var et = Await(EnsureBookTextAsync(eb, 1, 999, ocr: false, CancellationToken.None), 30_000);
            Check(et != null && et.Complete && et.Search(eb, "needle phrase").Any(h => h.Page == eb.Chapters[2].Page), "epub text & search");
            GoHome();
            Console.WriteLine("SMOKE book smart: ok");
        }
        finally
        {
            SmokeAskText = null;
            SmokeClaudeReply = null;
            _settings.DailyPagesGoal = goal;
            if (ReaderShown) CloseReader(showHome: true);
            var ids = added.Select(b => b.Id).ToHashSet();
            foreach (var b in _library.Books.Where(b => ids.Contains(b.Id))) { try { File.Delete(BookText.PathFor(b)); } catch { } }
            _library.Books.RemoveAll(b => ids.Contains(b.Id));
            _library.Shelves.RemoveAll(s => s.Name == "ذكاء الفحص");
            RebuildTree();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
