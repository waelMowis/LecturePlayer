using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// تصدير «شرح حصة» في النهاية:
///  • PDF كامل: غلاف (الخطة والمحتوى) ← السبورات ← مقطع الحصة في الدفتر ← الشرائح كلها بالرسم ← صفحات الشرح من الكتب
///    (أو الكتاب كاملًا) ← صفحة الفيديوهات والروابط (قابلة للنقر) ← الواجب.
///  • ملف «مسار» (ZIP) يفتحه الطالب من «📂 افتح حصة»: الكتب كاملة، والسبورات والدفتر (مقطع الحصة) بالشرح وأدوات أردوينو والبرمجة بالكتل،
///    والعروض برسمها، والفيديوهات والتسجيلات بعلاماتها، وروابط يوتيوب، ونسخة من ملف PDF — في مرور واحد (من غير إعادة فتح الملف).
/// </summary>
internal partial class MainForm
{
    internal const int LessonManifestVersion = 2;

    /// <summary>فيديو في الحصة: ملف على الجهاز (يدخل الحزمة) أو رابط (يدخل رابطًا فقط).</summary>
    internal sealed record LessonVideo(VideoItem Video, bool Recording, string? File, string? Url);

    /// <summary>كتب الحصة بالترتيب: سبورتها، والسبورات الأخرى، والدفتر، والعروض، والكتب.</summary>
    internal List<(WorkTab Tab, Book Book)> LessonBooks(StudySession s)
    {
        var list = new List<(WorkTab, Book)>();
        foreach (var t in (s.Items ?? s.Tabs).Where(t => t.Kind == WorkTab.BookKind))
            if (_library.Books.FirstOrDefault(b => b.Id == t.Ref) is { } b && list.All(x => x.Item2 != b)) list.Add((t, b));
        int Rank(Book b) => b.Id == s.BoardId ? 0 : b.IsBoard && !b.IsNotebook ? 1 : b.IsNotebook ? 2 : b.IsPresentation ? 3 : 4;
        return list.Select((x, i) => (x, i)).OrderBy(p => Rank(p.x.Item2)).ThenBy(p => p.i).Select(p => p.x).ToList();
    }

    internal List<LessonVideo> LessonVideos(StudySession s)
    {
        var list = new List<LessonVideo>();
        void Add(VideoItem? v)
        {
            if (v == null || list.Any(x => x.Video == v)) return;
            bool rec = s.Recordings.Contains(v.Path);
            if (v.IsWebLesson || v.IsMoodlePage) list.Add(new LessonVideo(v, rec, null, null));             // درس منصة: بالاسم فقط
            else if (v.IsOnline) list.Add(new LessonVideo(v, rec, null, v.Path));
            else list.Add(new LessonVideo(v, rec, File.Exists(v.LocalPath) ? v.LocalPath : null, null));
        }
        if (LessonGroup(s, create: false) is { } g) foreach (var v in g.Videos) Add(v);
        foreach (var t in (s.Items ?? s.Tabs).Where(t => t.Kind == WorkTab.CourseKind && t.ItemPath != null))
            Add(_library.AllVideos.FirstOrDefault(x => string.Equals(x.Path, t.ItemPath, StringComparison.OrdinalIgnoreCase)));
        foreach (var r in s.Recordings) Add(_library.AllVideos.FirstOrDefault(x => x.Path == r));
        return list;
    }

    /// <summary>حجم الحزمة تقريبًا (الكتب كاملة والفيديوهات).</summary>
    internal long LessonBytes(StudySession s) =>
        LessonBooks(s).Sum(x => BookFile(x.Book) is { } f && File.Exists(f) ? new FileInfo(f).Length : 0)
        + LessonVideos(s).Sum(v => v.File != null ? new FileInfo(v.File).Length : 0);

    /// <summary>نسخة من الدفتر فيها مقطع الحصة وحده (لملف PDF والحزمة) — null لو المقطع مش موجود.</summary>
    private static string? NotebookSectionCopy(string file, string section, string dir)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject m || m["pages"] is not JsonArray pages) return null;
            var keep = pages.OfType<JsonObject>().Where(p => (string?)p["sec"] == section).Select(p => p.DeepClone()).ToArray();
            if (keep.Length == 0) return null;
            m["pages"] = new JsonArray(keep);
            if (m["sections"] is JsonArray secs) m["sections"] = new JsonArray(secs.OfType<JsonObject>().Where(x => (string?)x["id"] == section).Select(x => x.DeepClone()).ToArray());
            m["sec"] = section;
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, Path.GetFileName(file));
            File.WriteAllText(copy, m.ToJsonString(), new UTF8Encoding(false));
            return copy;
        }
        catch (Exception ex) { Log.Error("notebook section copy", ex); return null; }
    }

    /// <summary>شرح الدفتر لصفحات المقطع وحده.</summary>
    private static JsonObject InkForPages(JsonObject ink, IEnumerable<string> pageIds)
    {
        var ids = pageIds.ToHashSet();
        var copy = (JsonObject)ink.DeepClone();
        if (copy["pages"] is JsonObject pages) foreach (var k in pages.Select(p => p.Key).ToList()) if (!ids.Contains(k)) pages.Remove(k);
        if (copy["del"] is JsonObject del) foreach (var k in del.Select(p => p.Key).ToList()) if (!ids.Contains(k)) del.Remove(k);
        return copy;
    }

    private static IEnumerable<string> BoardPageIds(string boardFile)
    {
        try { return (JsonNode.Parse(File.ReadAllText(boardFile))?["pages"] as JsonArray)?.OfType<JsonObject>().Select(p => (string?)p["id"] ?? "").ToList() ?? new List<string>(); }
        catch { return new List<string>(); }
    }

    // ---------------- ملف PDF الكامل ----------------

    private async Task<int> BuildLessonPdfAsync(StudySession s, string target, bool fullBooks, List<string> notes, CancellationToken ct)
    {
        if (!ReaderPane.Available) { notes.Add("ملف PDF يحتاج إلى «Microsoft Edge WebView2»"); return 0; }
        await FlushInkForCloseAsync();   // أدوات أردوينو والبرمجة بالكتل تحفظ في السبورة، والرسم الأخير يتكتب
        var reader = EnsureReader();
        await reader.EnsureReadyAsync();
        string tmp = Path.Combine(Path.GetTempPath(), "masar-lesson-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var parts = new List<ReaderPane.ComposePart>();
            var contents = new List<string>();
            int k = 0;
            foreach (var (t, b) in LessonBooks(s))
            {
                k++;
                ct.ThrowIfCancellationRequested();
                string file = BookFile(b);
                if (!File.Exists(file)) { notes.Add($"«{b.Title}» غير موجود على هذا الجهاز"); continue; }
                var ink = HasLibrary ? InkStore.Load(InkStore.FileFor(_libraryPath, b.Id)) : InkStore.Empty();
                if (b.IsBoard)
                {
                    string src = file, what = b.IsNotebook ? "📓 دفتر" : "🧑‍🏫 سبورة";
                    if (b.IsNotebook && b.Id == s.NotebookId && s.NotebookSection != null && NotebookSectionCopy(file, s.NotebookSection, Path.Combine(tmp, k + "-nb")) is { } sec)
                    { src = sec; what = "📓 مقطع الحصة في دفتر"; }
                    string outp = Path.Combine(tmp, $"{k}-board.pdf");
                    var r = await reader.BoardPdfFileAsync(src, ink, outp, b.Title, ct);
                    if (r.Ok) { parts.Add(new(Pdf: outp)); contents.Add($"{what} «{b.Title}» — {r.Pages} صفحة"); }
                    else notes.Add($"«{b.Title}»: {r.Error}");
                }
                else if (b.IsPresentation)
                {
                    bool pp = PowerPointShow.InstalledVersion() != null;
                    var slides = await Task.Run(() => pp ? ExportAnnotatedSlides(b, tmp, k, notes, all: true) : TextSlidesWithInk(b, tmp, k, notes), ct);
                    foreach (var (img, w, h) in slides) parts.Add(new(Image: img, W: 842, H: Math.Round(842.0 * h / w)));
                    contents.Add($"📽 عرض «{b.Title}» — " + (slides.Count == 0 ? "العرض كاملًا في ملف «مسار»" : pp ? $"{slides.Count} شريحة بالرسم" : $"{slides.Count} شريحة مشروحة (نصها ورسمك)"));
                }
                else if (BookFiles.FormatOf(file) == "pdf")
                {
                    var inked = InkStore.PagesWithInk(ink);
                    if (inked.Length > 0)
                    {
                        string outp = Path.Combine(tmp, $"{k}-book.pdf");
                        var r = await reader.BuildAnnotatedAsync(file, ink, outp, b.Title, null, ct);
                        if (r.Ok)
                        {
                            parts.Add(fullBooks ? new(Pdf: outp) : new(Pdf: outp, Pages: inked.Select(p => p - 1).ToArray()));
                            contents.Add($"📖 «{b.Title}» — " + (fullBooks ? $"الكتاب كاملًا بالشرح ({b.PageCount} صفحة)" : $"صفحات الشرح: {PageList(inked)}"));
                        }
                        else notes.Add($"«{b.Title}»: {r.Error}");
                    }
                    else if (fullBooks) { parts.Add(new(Pdf: file)); contents.Add($"📖 «{b.Title}» — الكتاب كاملًا ({b.PageCount} صفحة)"); }
                    else contents.Add($"📖 «{b.Title}» — حتى الصفحة {t.Page ?? b.LastPage} (لا شرح عليه؛ الكتاب كاملًا في ملف «مسار»)");
                }
                else contents.Add($"📖 «{b.Title}» — الكتاب كاملًا في ملف «مسار»");
            }

            // الفيديوهات والروابط (الروابط قابلة للنقر)
            var videos = LessonVideos(s);
            ReaderPane.ComposePart? videoPage = null;
            if (videos.Count > 0)
            {
                var lines = new List<ReaderPane.TextLine>();
                var recs = new List<ReaderPane.TextLine>();
                foreach (var v in videos)
                {
                    string len = v.Video.DurationMs > 0 ? $" ({TimeFormat.Format(v.Video.DurationMs)})" : "";
                    if (v.Url != null) lines.Add(new($"🔗 {v.Video.Title}{len} — {v.Url}", v.Url));
                    else if (v.File != null) (v.Recording ? recs : lines).Add(new($"{(v.Recording ? "⏺" : "🎬")} {v.Video.Title}{len} — في ملف «مسار»" + (v.Video.Bookmarks.Count > 0 ? $" · {v.Video.Bookmarks.Count} علامة" : "")));
                    else lines.Add(new($"🎓 {v.Video.Title} — على المنصة"));
                    if (v.Recording) foreach (var (ms, title) in ChaptersOf(v.Video).Where(c => c.Ms > 0)) recs.Add(new($"   {TimeFormat.Format(ms)}  {title}"));
                }
                var sections = new List<(string, List<ReaderPane.TextLine>)>();
                if (lines.Count > 0) sections.Add(("الفيديوهات والروابط", lines));
                if (recs.Count > 0) sections.Add(("تسجيل الحصة", recs));
                string vf = Path.Combine(tmp, "8-videos.png");
                var vr = await reader.TextPageAsync("فيديوهات الحصة", s.Name, sections, vf);
                if (vr.Ok) videoPage = new(Image: vf, W: 595, H: 842, Links: vr.Links); else notes.Add("صفحة الفيديوهات: " + vr.Error);
                contents.Add($"🎬 {videos.Count(v => v.Url == null)} فيديو · 🔗 {videos.Count(v => v.Url != null)} رابط");
            }
            if (videoPage != null) parts.Add(videoPage);

            string? cls = s.ClassId != null ? _library.Classes.FirstOrDefault(c => c.Id == s.ClassId)?.Name : null;
            string date = DateTime.Now.ToString("dddd d MMMM yyyy", new CultureInfo("ar-EG"));
            var cover = new List<(string Head, List<string> Lines)>();
            if (!string.IsNullOrWhiteSpace(s.Plan)) cover.Add(("الخطة", SplitLines(s.Plan)));
            if (contents.Count > 0) cover.Add(("محتوى الحصة", contents));
            cover.Add(("", new List<string> { "افتح ملف «مسار» المرفق من «📂 افتح حصة» لتجد الحصة كاملة: الكتب والسبورات والدفتر والعروض والفيديوهات." }));
            string coverFile = Path.Combine(tmp, "0-cover.png");
            var cr = await reader.TextPageAsync(s.Name, (cls != null ? cls + "  ·  " : "") + date, cover, coverFile);
            if (cr.Ok) parts.Insert(0, new(Image: coverFile, W: 595, H: 842)); else notes.Add("الغلاف: " + cr.Error);
            if (!string.IsNullOrWhiteSpace(s.Homework))
            {
                string hw = Path.Combine(tmp, "9-homework.png");
                var hr = await reader.TextPageAsync("الواجب", s.Name, new List<(string, List<string>)> { ("", SplitLines(s.Homework)) }, hw);
                if (hr.Ok) parts.Add(new(Image: hw, W: 595, H: 842)); else notes.Add("الواجب: " + hr.Error);
            }
            var res = await reader.ComposeAsync(parts, target, s.Name, null, ct);
            if (!res.Ok) { notes.Add("ملف PDF: " + res.Error); return 0; }
            return res.Pages;
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    /// <summary>
    /// من غير PowerPoint: الشرائح اللي عليها رسم بس — صورة بنص الشريحة (العنوان والمحتوى) والرسم فوقه.
    /// </summary>
    private List<(string File, int W, int H)> TextSlidesWithInk(Book b, string tmp, int k, List<string> notes)
    {
        var result = new List<(string, int, int)>();
        var overlay = HasLibrary ? SlideInk.Load(_libraryPath, b.Id) : new();
        string file = BookFile(b);
        if (overlay.Count == 0 || !Pptx.IsOpenXml(file))
        {
            notes.Add($"«{b.Title}»: صور الشرائح تحتاج إلى PowerPoint على هذا الجهاز — العرض نفسه داخل ملف «مسار»");
            return result;
        }
        notes.Add($"«{b.Title}»: PowerPoint غير مثبت، فدخلت الشرائح المشروحة فقط (نصها ورسمك)");
        float aspect = SlideInk.AspectOf(_libraryPath, b.Id);
        List<Pptx.Slide> slides;
        try { slides = Pptx.Read(file); } catch (Exception ex) { notes.Add($"«{b.Title}»: {ex.Message}"); return result; }
        const int W = 1600, H = 900;
        foreach (var (n, items) in overlay.Where(x => x.Value.Count > 0).OrderBy(x => x.Key))
        {
            var sl = slides.FirstOrDefault(x => x.Index == n);
            string f = Path.Combine(tmp, $"{k}-tslide{n}.png");
            using (var bmp = new Bitmap(W, H))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                var flags = TextFormatFlags.RightToLeft | TextFormatFlags.Right | TextFormatFlags.WordBreak;
                using (var tf = new Font("Segoe UI", 40, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, sl?.Title is { Length: > 0 } title ? title : $"الشريحة {n}", tf, new Rectangle(80, 60, W - 160, 120), Color.FromArgb(30, 30, 34), flags);
                using (var bf = new Font("Segoe UI", 28, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, sl?.Text ?? "", bf, new Rectangle(80, 200, W - 160, H - 280), Color.FromArgb(60, 60, 66), flags);
                using (var mf = new Font("Segoe UI", 20, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, $"{b.Title} — الشريحة {n}", mf, new Rectangle(80, H - 60, W - 160, 40), Color.FromArgb(140, 140, 150), flags);
                ScreenInk.RenderItems(g, SlideItemsToImage(items, aspect, W, H));
                bmp.Save(f, ImageFormat.Png);
            }
            result.Add((f, W, H));
        }
        return result;
    }

    // ---------------- ملف «مسار» للطالب ----------------

    private void BuildLessonZip(StudySession s, string target, string? pdf, CancellationToken ct)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "masar-lzip-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var books = LessonBooks(s);
            var shelf = new Shelf { Name = s.Name, IconKey = "whiteboard" };
            var clones = books.Select(x => { var c = System.Text.Json.JsonSerializer.Deserialize<Book>(System.Text.Json.JsonSerializer.Serialize(x.Book))!; c.ShelfId = shelf.Id; return c; }).ToList();
            // الدفتر: مقطع الحصة وحده بشرحه
            var fileOverride = new Dictionary<string, string>();
            var inkOverride = new Dictionary<string, string>();
            if (s.NotebookId != null && s.NotebookSection != null && books.FirstOrDefault(x => x.Book.Id == s.NotebookId).Book is { } nb
                && NotebookSectionCopy(BookFile(nb), s.NotebookSection, Path.Combine(tmp, "nb")) is { } sec)
            {
                fileOverride[nb.Id] = sec;
                string inkFile = Path.Combine(tmp, nb.Id + ".json");
                InkStore.Save(inkFile, InkForPages(InkStore.Load(InkStore.FileFor(_libraryPath, nb.Id)), BoardPageIds(sec)));
                inkOverride[nb.Id] = inkFile;
                if (clones.FirstOrDefault(c => c.Id == nb.Id) is { } nc) { nc.PageCount = BoardPageIds(sec).Count(); nc.LastPage = 1; }
            }
            string FileOf(Book x) => fileOverride.TryGetValue(x.Id, out var f) ? f : _library.Books.FirstOrDefault(o => o.Id == x.Id) is { } orig ? BookFile(orig) : "";
            long bytes = clones.Sum(c => FileOf(c) is { Length: > 0 } f && File.Exists(f) ? new FileInfo(f).Length : 0);
            var plan = new ShelfShare.Plan(s.Name, new() { shelf }, clones, new(), new(), bytes);

            // الفيديوهات: الملفات في «videos/»، والروابط روابط
            var extraFiles = new List<(string, string)>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var vids = new JsonArray();
            foreach (var v in LessonVideos(s))
            {
                var o = new JsonObject { ["title"] = v.Video.Title, ["recording"] = v.Recording };
                if (v.Video.DurationMs > 0) o["durationMs"] = v.Video.DurationMs;
                if (v.File != null)
                {
                    string name = Path.GetFileName(v.File), stem = Path.GetFileNameWithoutExtension(v.File), ext = Path.GetExtension(v.File);
                    for (int i = 2; !used.Add(name); i++) name = $"{stem} ({i}){ext}";
                    extraFiles.Add((v.File, "videos/" + name));
                    o["file"] = "videos/" + name;
                }
                else if (v.Url != null) o["url"] = v.Url;
                else o["platform"] = true;
                if (v.Video.Bookmarks.Count > 0)
                    o["bookmarks"] = new JsonArray(v.Video.Bookmarks.OrderBy(x => x.TimeMs).Select(x => (JsonNode)new JsonObject { ["ms"] = x.TimeMs, ["note"] = x.Note }).ToArray());
                vids.Add(o);
            }
            if (pdf != null && File.Exists(pdf)) extraFiles.Add((pdf, Path.GetFileName(pdf)));

            var items = new JsonArray();
            foreach (var (t, b) in books)
            {
                var o = new JsonObject { ["kind"] = WorkTab.BookKind, ["ref"] = b.Id, ["title"] = b.Title };
                if (t.Page is int p) o["page"] = b.Id == s.NotebookId && fileOverride.ContainsKey(b.Id) ? 1 : p;
                items.Add(o);
            }
            string? cls = s.ClassId != null ? _library.Classes.FirstOrDefault(c => c.Id == s.ClassId)?.Name : null;
            var manifest = new JsonObject
            {
                ["v"] = LessonManifestVersion, ["app"] = "Masar", ["kind"] = "lesson", ["lessonId"] = s.LessonId ?? s.Id,
                ["name"] = s.Name, ["class"] = cls, ["plan"] = s.Plan, ["homework"] = s.Homework, ["created"] = DateTime.UtcNow.ToString("o"),
                ["board"] = s.BoardId, ["notebook"] = s.NotebookId,
                ["tabs"] = items.DeepClone(), ["items"] = items, ["videos"] = vids,
                ["pdf"] = pdf != null && File.Exists(pdf) ? Path.GetFileName(pdf) : null
            };
            string readme =
                $"هذه حصة من «مسار»: {s.Name}\r\n\r\n" +
                "افتحها في «مسار» من «📂 افتح حصة» (أو قائمة «شرح حصة» ← «افتح حصة من ملف») واختر هذا الملف:\r\n" +
                "ستجد الكتب كاملة، والسبورات والدفتر بشرح المعلم، والعروض، والفيديوهات والروابط — في حصة واحدة.\r\n\r\n" +
                (pdf != null ? $"وملف PDF للحصة كلها: {Path.GetFileName(pdf)}\r\n" : "");
            ShelfShare.Export(plan, _libraryPath, FileOf, target, new ShelfShare.Options(Progress: false, Annotations: true, AnnotatedCopies: false), null, ct,
                new ShelfShare.Extra(extraFiles, new[] { (SessionManifest, ManifestJson(manifest)) }, b => inkOverride.GetValueOrDefault(b.Id), readme));
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}

internal partial class MainForm
{
    /// <summary>للفحص: «الكتاب كامل في PDF» من غير شباك.</summary>
    internal bool SmokeLessonFullBooks;

    /// <summary>تصدير «شرح حصة»: قائمة بمحتوياتها وحجمها، وPDF كامل (والكتاب كاملًا اختياريًا)، وملف «مسار»، والتسجيل بالفصول.</summary>
    private void LessonExportDialog(StudySession s)
    {
        var books = LessonBooks(s);
        var videos = LessonVideos(s);
        var marked = SessionVideosWithMarks(s);
        long bytes = LessonBytes(s);
        using var kit = new FormKit($"📤 تصدير الحصة «{s.Name}»", 620);
        kit.Header("محتويات الحصة");
        var counts = new List<string>();
        void Count(int n, string what) { if (n > 0) counts.Add($"{n} {what}"); }
        Count(books.Count(x => x.Book.IsBoard && !x.Book.IsNotebook), "🧑‍🏫 سبورة");
        Count(books.Count(x => x.Book.IsNotebook), "📓 دفتر");
        Count(books.Count(x => x.Book.IsPresentation), "📽 عرض");
        Count(books.Count(x => !x.Book.IsBoard && !x.Book.IsPresentation), "📖 كتاب");
        Count(videos.Count(v => v.File != null && !v.Recording), "🎬 فيديو");
        Count(videos.Count(v => v.Recording && v.File != null), "⏺ تسجيل");
        Count(videos.Count(v => v.Url != null), "🔗 رابط");
        kit.Note((counts.Count == 0 ? "الحصة فارغة." : string.Join("  ·  ", counts)) + $"\nحجم ملف «مسار» حوالي {YtDlp.FormatSize(bytes)}", 40, Theme.Text);
        if (bytes > 2L * 1024 * 1024 * 1024)
            kit.Note("⚠ الملف أكبر من 2 جيجابايت: يصعب إرساله في المحادثات — ارفعه على Google Drive أو OneDrive وأرسل الرابط، أو أزل فيديو كبيرًا من «محتويات الحصة».", 40, Color.FromArgb(240, 180, 90));
        var missing = books.Where(x => !File.Exists(BookFile(x.Book))).Select(x => x.Book.Title).ToList();
        if (missing.Count > 0) kit.Note("⚠ غير موجود على هذا الجهاز (لن يدخل): " + string.Join("، ", missing.Take(4)), 22, Color.FromArgb(240, 180, 90));
        kit.Header("ماذا تريد أن تصدّر؟");
        var pdf = kit.Row("ملف PDF", new ToggleSwitch { Text = "الحصة كاملة: السبورات والدفتر والشرائح والشرح والروابط والواجب", Checked = true });
        var full = kit.Row("الكتب", new ToggleSwitch { Text = "الكتاب كامل في PDF (لا صفحات الشرح فقط)", Checked = false });
        var zip = kit.Row("ملف «مسار»", new ToggleSwitch { Text = "ZIP يفتحه الطالب من «📂 افتح حصة» بكل المحتويات", Checked = true });
        var vid = kit.Row("التسجيل", new ToggleSwitch { Text = marked.Count > 0 ? $"MP4 بفصول عند كل علامة ({marked.Count} فيديو)" : "لا يوجد تسجيل بعلامات", Checked = marked.Count > 0, Enabled = marked.Count > 0 });
        pdf.CheckedChanged += (_, _) => full.Enabled = pdf.Checked;
        kit.Header("المكان");
        string root = SmokeExportFolder ?? DefaultExportRoot;
        var folder = kit.Row("المجلد", new TextBox { Text = root, RightToLeft = RightToLeft.No });
        kit.Note("يُنشأ في المجلد مجلد باسم الحصة. ملف «مسار» فيه الكتب كاملة دائمًا، ونسخة من ملف PDF. صور الشرائح تحتاج إلى PowerPoint على هذا الجهاز.", 40);
        kit.Buttons("📤  تصدير");
        if (SmokeDialogShots && SmokeShowDialog(kit, "lesson-export")) return;
        if (SmokeExportFolder == null && kit.Show(this) != DialogResult.OK) return;
        var opts = new SessionExportOptions(pdf.Checked, zip.Checked, vid.Checked && marked.Count > 0, pdf.Checked && (full.Checked || SmokeLessonFullBooks));
        if (!opts.Pdf && !opts.Zip && !opts.Video) return;
        _ = RunSessionExportAsync(s, opts, folder.Text.Trim().Length > 0 ? folder.Text.Trim() : root);
    }
}
