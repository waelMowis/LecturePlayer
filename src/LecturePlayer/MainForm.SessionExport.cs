using System.Drawing.Imaging;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// تصدير الجلسة أو الحصة:
///  • ملف PDF للطالب: الغلاف (الاسم والمجموعة والتاريخ والخطة والمحتوى) + صفحات السبورات + الصفحات المشروحة من الكتب
///    + الشرائح المشروحة + الواجب.
///  • حزمة «مسار» (ZIP): الكتب والسبورات والعروض وشرحها والجلسة نفسها — تُفتح في «مسار» عند الطالب أو على جهاز آخر.
///  • فيديو التسجيل بالفصول: نسخة MP4 فيها فصل عند كل علامة، وملف نصي بالفصول (لوصف يوتيوب مثلًا).
/// </summary>
internal partial class MainForm
{
    internal sealed record SessionExportOptions(bool Pdf, bool Zip, bool Video);
    internal sealed record SessionExportResult(string Folder, string? Pdf, int PdfPages, string? Zip, List<string> Videos, List<string> Notes);

    internal const string SessionManifest = "masar-session.json";
    internal static string? SmokeExportFolder;
    internal SessionExportResult? LastSessionExport;
    internal StudySession? LastImportedSession;
    private bool _exporting;

    private static string SafeFileName(string name, string fallback)
    {
        string s = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim().TrimEnd('.');
        if (s.Length > 80) s = s[..80];
        return s.Length > 0 ? s : fallback;
    }

    private static string DefaultExportRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "مسار", "تصدير");

    /// <summary>الفيديوهات ذات العلامات في الجلسة (التسجيلات خاصة) — لها نسخة بالفصول.</summary>
    private List<(VideoItem Video, string Course)> SessionVideosWithMarks(StudySession s)
    {
        var list = new List<(VideoItem, string)>();
        var paths = s.Recordings.Concat(s.Tabs.Where(t => t.Kind == WorkTab.CourseKind && t.ItemPath != null).Select(t => t.ItemPath!));
        foreach (var path in paths)
        {
            var v = _library.AllVideos.FirstOrDefault(x => x.Path == path);
            if (v == null || v.Bookmarks.Count == 0 || v.IsOnline || v.IsWebLesson || !File.Exists(v.LocalPath)) continue;
            if (list.Any(x => x.Item1 == v)) continue;
            var g = _library.FindGroupOf(v);
            list.Add((v, (g != null ? _library.FindCourseOf(g)?.Name : null) ?? ""));
        }
        return list;
    }

    internal void ExportSessionDialog(StudySession s)
    {
        if (_exporting) { ShowStatus("تصدير آخر يعمل الآن…", 4); return; }
        if (_activeTab >= 0 && _sessionId == s.Id) { CaptureTab(_activeTab); MirrorSession(); }
        var videos = SessionVideosWithMarks(s);
        using var kit = new FormKit($"تصدير «{s.Name}»", 600);
        kit.Header("ماذا تريد أن تصدّر؟");
        var pdf = kit.Row("ملف للطالب", new ToggleSwitch { Text = "PDF: الغلاف والخطة، والسبورات، والصفحات والشرائح المشروحة، والواجب", Checked = true });
        var zip = kit.Row("حزمة «مسار»", new ToggleSwitch { Text = "ZIP: الملفات بشرحها والجلسة — تُفتح في «مسار» عند الطالب", Checked = true });
        var vid = kit.Row("التسجيل", new ToggleSwitch { Text = videos.Count > 0 ? $"MP4 بفصول عند كل علامة ({videos.Count} فيديو)" : "لا يوجد تسجيل بعلامات في هذه ال" + SessionWord, Checked = videos.Count > 0, Enabled = videos.Count > 0 });
        kit.Header("المكان");
        string root = SmokeExportFolder ?? DefaultExportRoot;
        var folder = kit.Row("المجلد", new TextBox { Text = root, RightToLeft = RightToLeft.No });
        kit.Note("يُنشأ بداخله مجلد باسم ال" + SessionWord + ". الشرائح المشروحة تحتاج إلى PowerPoint على هذا الجهاز.");
        kit.Buttons("تصدير");
        if (SmokeDialogShots && SmokeShowDialog(kit, "session-export")) return;
        if (SmokeExportFolder == null && kit.Show(this) != DialogResult.OK) return;
        var opts = new SessionExportOptions(pdf.Checked, zip.Checked, vid.Checked && videos.Count > 0);
        if (!opts.Pdf && !opts.Zip && !opts.Video) return;
        _ = RunSessionExportAsync(s, opts, folder.Text.Trim().Length > 0 ? folder.Text.Trim() : root);
    }

    internal async Task<SessionExportResult?> RunSessionExportAsync(StudySession s, SessionExportOptions o, string root)
    {
        if (_exporting) return null;
        _exporting = true;
        try
        {
            ShowBanner($"📤 جارٍ تصدير «{s.Name}»…", BannerKind.Info, seconds: 0, key: "export");
            var r = await ExportSessionAsync(s, o, root, CancellationToken.None);
            LastSessionExport = r;
            var parts = new List<string>();
            if (r.Pdf != null) parts.Add($"PDF ({r.PdfPages} صفحة)");
            if (r.Zip != null) parts.Add("حزمة «مسار»");
            if (r.Videos.Count > 0) parts.Add($"{r.Videos.Count} فيديو بالفصول");
            string msg = parts.Count > 0 ? $"📤 صُدّرت «{s.Name}»: " + string.Join("، ", parts) : $"لم يُصدَّر شيء من «{s.Name}»";
            if (r.Notes.Count > 0) msg += "  ·  " + string.Join("  ·  ", r.Notes.Take(2));
            ShowBanner(msg, parts.Count > 0 ? BannerKind.Success : BannerKind.Warning, "افتح المجلد", () => OpenFolder(r.Folder), seconds: 0, key: "export");
            return r;
        }
        catch (Exception ex)
        {
            Log.Error("session export", ex);
            ShowBanner("تعذّر التصدير: " + ex.Message, BannerKind.Warning, seconds: 12, key: "export");
            return null;
        }
        finally { _exporting = false; }
    }

    internal async Task<SessionExportResult> ExportSessionAsync(StudySession s, SessionExportOptions o, string root, CancellationToken ct)
    {
        string folder = Path.Combine(root, SafeFileName(s.Name, SessionWord));
        Directory.CreateDirectory(folder);
        var notes = new List<string>();
        string? pdf = null, zip = null;
        int pages = 0;
        var videos = new List<string>();
        if (o.Pdf)
        {
            string target = Path.Combine(folder, SafeFileName(s.Name, "ملف") + ".pdf");
            pages = await BuildStudentPdfAsync(s, target, notes, ct);
            if (pages > 0) pdf = target;
        }
        if (o.Zip)
        {
            string target = Path.Combine(folder, SafeFileName(s.Name, "حزمة") + " — مسار.zip");
            await Task.Run(() => BuildSessionZip(s, target), ct);
            zip = target;
        }
        if (o.Video)
        {
            foreach (var (v, _) in SessionVideosWithMarks(s))
            {
                var made = await ExportVideoWithChaptersAsync(v, folder, ct);
                if (made != null) videos.Add(made); else notes.Add($"تعذّر تجهيز «{v.Title}» بالفصول");
            }
        }
        Log.Info($"session export «{s.Name}»: pdf={pages}p zip={zip != null} videos={videos.Count} notes={string.Join(" | ", notes)}");
        return new SessionExportResult(folder, pdf, pages, zip, videos, notes);
    }

    // ---------------- ملف الطالب ----------------

    private async Task<int> BuildStudentPdfAsync(StudySession s, string target, List<string> notes, CancellationToken ct)
    {
        if (!ReaderPane.Available) { notes.Add("ملف PDF يحتاج إلى «Microsoft Edge WebView2»"); return 0; }
        var reader = EnsureReader();
        await reader.EnsureReadyAsync();
        string tmp = Path.Combine(Path.GetTempPath(), "masar-export-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var parts = new List<ReaderPane.ComposePart>();
            var contents = new List<string>();
            int k = 0;
            foreach (var t in s.Tabs)
            {
                k++;
                ct.ThrowIfCancellationRequested();
                if (t.Kind == WorkTab.CourseKind)
                {
                    var v = t.ItemPath == null ? null : _library.AllVideos.FirstOrDefault(x => x.Path == t.ItemPath);
                    long pos = t.PositionMs ?? v?.LastPositionMs ?? 0;
                    contents.Add($"▶ {t.Title}" + (v != null ? $" — {v.Title}" + (pos > 0 ? $" (حتى {TimeFormat.Format(pos)})" : "") : "") + (v?.Bookmarks.Count > 0 ? $" · {v.Bookmarks.Count} علامة" : ""));
                    continue;
                }
                var b = _library.Books.FirstOrDefault(x => x.Id == t.Ref);
                if (b == null) continue;
                string file = BookFile(b);
                if (!File.Exists(file)) { notes.Add($"«{b.Title}» غير موجود على هذا الجهاز"); continue; }
                var ink = HasLibrary ? InkStore.Load(InkStore.FileFor(_libraryPath, b.Id)) : InkStore.Empty();
                if (b.IsBoard)
                {
                    string outp = Path.Combine(tmp, $"{k}-board.pdf");
                    var r = await reader.BoardPdfFileAsync(file, ink, outp, b.Title, ct);
                    if (r.Ok) { parts.Add(new(Pdf: outp)); contents.Add($"🧑‍🏫 سبورة «{b.Title}» — {r.Pages} صفحة"); }
                    else notes.Add($"سبورة «{b.Title}»: {r.Error}");
                }
                else if (b.IsPresentation)
                {
                    var slides = await Task.Run(() => ExportAnnotatedSlides(b, tmp, k, notes), ct);
                    foreach (var (img, w, h) in slides) parts.Add(new(Image: img, W: 842, H: Math.Round(842.0 * h / w)));
                    contents.Add($"📽 عرض «{b.Title}» — " + (slides.Count > 0 ? $"{slides.Count} شريحة مشروحة" : $"حتى الشريحة {t.Page ?? b.LastPage}"));
                }
                else if (BookFiles.FormatOf(file) == "pdf")
                {
                    var inked = InkStore.PagesWithInk(ink);
                    if (inked.Length > 0)
                    {
                        string outp = Path.Combine(tmp, $"{k}-book.pdf");
                        var r = await reader.BuildAnnotatedAsync(file, ink, outp, b.Title, null, ct);
                        if (r.Ok) { parts.Add(new(Pdf: outp, Pages: inked.Select(p => p - 1).ToArray())); contents.Add($"📖 «{b.Title}» — الصفحات المشروحة: {PageList(inked)}"); }
                        else notes.Add($"«{b.Title}»: {r.Error}");
                    }
                    else contents.Add($"📖 «{b.Title}» — حتى الصفحة {t.Page ?? b.LastPage} (لا شرح عليها)");
                }
                else contents.Add($"📖 «{b.Title}» — حتى الصفحة {t.Page ?? b.LastPage}");
            }

            string? cls = s.ClassId != null ? _library.Classes.FirstOrDefault(c => c.Id == s.ClassId)?.Name : null;
            string date = DateTime.Now.ToString("dddd d MMMM yyyy", new CultureInfo("ar-EG"));
            var cover = new List<(string Head, List<string> Lines)>();
            if (!string.IsNullOrWhiteSpace(s.Plan)) cover.Add(("الخطة", SplitLines(s.Plan)));
            if (contents.Count > 0) cover.Add(("المحتوى", contents));
            string coverFile = Path.Combine(tmp, "0-cover.png");
            RenderTextPage(coverFile, s.Name, (cls != null ? cls + "  ·  " : "") + date, cover);
            parts.Insert(0, new(Image: coverFile, W: 595, H: 842));
            if (!string.IsNullOrWhiteSpace(s.Homework))
            {
                string hw = Path.Combine(tmp, "9-homework.png");
                RenderTextPage(hw, "الواجب", s.Name, new() { ("", SplitLines(s.Homework)) });
                parts.Add(new(Image: hw, W: 595, H: 842));
            }
            var res = await reader.ComposeAsync(parts, target, s.Name, null, ct);
            if (!res.Ok) { notes.Add("ملف PDF: " + res.Error); return 0; }
            return res.Pages;
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    private static List<string> SplitLines(string text) =>
        text.Replace("\r", "").Split('\n').Select(l => l.Trim().TrimStart('-', '•', '*').Trim()).Where(l => l.Length > 0).ToList();

    private static string PageList(int[] pages)
    {
        var parts = new List<string>();
        for (int i = 0; i < pages.Length; i++)
        {
            int a = pages[i], b = a;
            while (i + 1 < pages.Length && pages[i + 1] == b + 1) b = pages[++i];
            parts.Add(a == b ? a.ToString() : $"{a}–{b}");
        }
        return string.Join("، ", parts);
    }

    /// <summary>صفحة A4 نصية (غلاف/واجب) كصورة: عنوان وسطر فرعي وأقسام بنقاط — بخط يدعم العربية.</summary>
    internal static void RenderTextPage(string file, string title, string subtitle, List<(string Head, List<string> Lines)> sections)
    {
        const int W = 1240, H = 1754, M = 110;
        using var bmp = new Bitmap(W, H);
        bmp.SetResolution(150, 150);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var rtl = new StringFormat(StringFormatFlags.DirectionRightToLeft) { Alignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord };
            using var accent = new SolidBrush(Color.FromArgb(0, 92, 175));
            using var ink = new SolidBrush(Color.FromArgb(30, 30, 34));
            using var muted = new SolidBrush(Color.FromArgb(110, 110, 118));
            using var fTitle = new Font("Segoe UI", 26f, FontStyle.Bold);
            using var fSub = new Font("Segoe UI", 12.5f);
            using var fHead = new Font("Segoe UI", 15f, FontStyle.Bold);
            using var fBody = new Font("Segoe UI", 12.5f);
            g.FillRectangle(accent, 0, 0, W, 18);
            float y = 90;
            var sz = g.MeasureString(title, fTitle, W - 2 * M, rtl);
            g.DrawString(title, fTitle, ink, new RectangleF(M, y, W - 2 * M, sz.Height + 4), rtl);
            y += sz.Height + 10;
            if (subtitle.Length > 0)
            {
                g.DrawString(subtitle, fSub, muted, new RectangleF(M, y, W - 2 * M, 60), rtl);
                y += 64;
            }
            using (var pen = new Pen(Color.FromArgb(220, 222, 228), 2)) g.DrawLine(pen, M, y, W - M, y);
            y += 30;
            foreach (var (head, lines) in sections)
            {
                if (y > H - 200) break;
                if (head.Length > 0)
                {
                    g.DrawString(head, fHead, accent, new RectangleF(M, y, W - 2 * M, 50), rtl);
                    y += 58;
                }
                foreach (var line in lines)
                {
                    string text = "•  " + line;
                    var ls = g.MeasureString(text, fBody, W - 2 * M - 20, rtl);
                    if (y + ls.Height > H - 120) { g.DrawString("…", fBody, muted, new RectangleF(M, y, W - 2 * M, 40), rtl); y = H; break; }
                    g.DrawString(text, fBody, ink, new RectangleF(M, y, W - 2 * M - 20, ls.Height + 4), rtl);
                    y += ls.Height + 12;
                }
                y += 24;
            }
            g.DrawString("أُعدّ بـ «مسار»", fSub, muted, new RectangleF(M, H - 90, W - 2 * M, 40), rtl);
        }
        bmp.Save(file, ImageFormat.Png);
    }

    /// <summary>
    /// الشرائح المشروحة: ما عليه حبر PowerPoint (في النسخة المشروحة) أو رسم «أقلام وأشكال» — صورة لكل شريحة والرسم فوقها.
    /// </summary>
    private List<(string File, int W, int H)> ExportAnnotatedSlides(Book b, string tmp, int k, List<string> notes)
    {
        var result = new List<(string, int, int)>();
        var overlay = HasLibrary ? SlideInk.Load(_libraryPath, b.Id) : new();
        float aspect = HasLibrary ? SlideInk.AspectOf(_libraryPath, b.Id) : 0;
        if (PowerPointShow.InstalledVersion() == null)
        {
            if (overlay.Count > 0 || AnnotatedCopyOf(b) != null) notes.Add($"الشرائح المشروحة من «{b.Title}» تحتاج إلى PowerPoint على هذا الجهاز");
            return result;
        }
        var copy = AnnotatedCopyOf(b);
        string src = copy != null && File.Exists(BookFile(copy)) ? BookFile(copy) : BookFile(b);
        PowerPointShow? show = null;
        try
        {
            show = new PowerPointShow();
            show.Open(src);
            var slides = show.InkSlideIndices().Union(overlay.Keys).Where(n => n >= 1 && n <= show.SlideCount).Distinct().OrderBy(n => n).ToList();
            foreach (int n in slides)
            {
                string f = Path.Combine(tmp, $"{k}-slide{n}.png");
                if (!show.ExportSlide(n, f, 1600)) continue;
                int w, h;
                using (var img = Image.FromFile(f)) { w = img.Width; h = img.Height; }
                if (overlay.TryGetValue(n, out var items) && items.Count > 0)
                {
                    using var bmp = new Bitmap(f);
                    using var canvas = new Bitmap(bmp.Width, bmp.Height);
                    using (var g = Graphics.FromImage(canvas))
                    {
                        g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
                        ScreenInk.RenderItems(g, SlideItemsToImage(items, aspect, w, h));
                    }
                    bmp.Dispose();
                    canvas.Save(f, ImageFormat.Png);
                }
                result.Add((f, w, h));
            }
        }
        catch (Exception ex)
        {
            Log.Error("export slides", ex);
            notes.Add($"«{b.Title}»: تعذّر تصدير الشرائح ({ex.Message})");
        }
        finally { try { show?.Dispose(); } catch { } }
        return result;
    }

    /// <summary>
    /// الرسم محفوظ نسبةً إلى مساحة العرض كلها، والشريحة في وسطها بنسبتها (حواف سوداء على الجانبين أو أعلى وأسفل):
    /// تحويله إلى بكسلات صورة الشريحة.
    /// </summary>
    internal static List<ScreenInk.Item> SlideItemsToImage(IEnumerable<ScreenInk.Item> items, float areaAspect, int w, int h)
    {
        float s = h > 0 ? w / (float)h : 16f / 9f, a = areaAspect > 0 ? areaAspect : s;
        float fx = 1, fy = 1, x0 = 0, y0 = 0, areaH = h;
        if (a > s) { fx = s / a; x0 = (1 - fx) / 2; }
        else if (a < s) { fy = a / s; y0 = (1 - fy) / 2; areaH = h / fy; }
        return items.Select(i => new ScreenInk.Item
        {
            Tool = i.Tool, Color = i.Color, Width = Math.Max(1f, i.Width * areaH),
            Pts = i.Pts.Select(p => new PointF((p.X - x0) / fx * w, (p.Y - y0) / fy * h)).ToList()
        }).ToList();
    }

    // ---------------- حزمة «مسار» ----------------

    private void BuildSessionZip(StudySession s, string target)
    {
        var books = s.Tabs.Where(t => t.Kind == WorkTab.BookKind).Select(t => _library.Books.FirstOrDefault(b => b.Id == t.Ref)).OfType<Book>().Distinct().ToList();
        var shelf = new Shelf { Name = s.Name };
        var clones = books.Select(b => { var c = JsonSerializer.Deserialize<Book>(JsonSerializer.Serialize(b))!; c.ShelfId = shelf.Id; return c; }).ToList();
        string FileOf(Book x) => _library.Books.FirstOrDefault(o => o.Id == x.Id) is { } orig ? BookFile(orig) : "";
        long bytes = clones.Sum(c => FileOf(c) is { Length: > 0 } f && File.Exists(f) ? new FileInfo(f).Length : 0);
        var plan = new ShelfShare.Plan(s.Name, new() { shelf }, clones, new(), new(), bytes);
        ShelfShare.Export(plan, _libraryPath, FileOf, target, new ShelfShare.Options(Progress: true, Annotations: true, AnnotatedCopies: false), null, CancellationToken.None);

        var tabs = new JsonArray();
        foreach (var t in s.Tabs)
        {
            if (t.Kind == WorkTab.BookKind && !books.Any(b => b.Id == t.Ref)) continue;
            var o = new JsonObject { ["kind"] = t.Kind, ["ref"] = t.Ref, ["title"] = t.Title };
            if (t.Page is int p) o["page"] = p;
            if (t.Kind == WorkTab.CourseKind && t.ItemPath != null && _library.AllVideos.FirstOrDefault(x => x.Path == t.ItemPath) is { } v)
            { o["video"] = v.Title; if (t.PositionMs is long ms) o["positionMs"] = ms; }
            tabs.Add(o);
        }
        string? cls = s.ClassId != null ? _library.Classes.FirstOrDefault(c => c.Id == s.ClassId)?.Name : null;
        var manifest = new JsonObject
        {
            ["v"] = 1, ["app"] = "Masar", ["name"] = s.Name, ["class"] = cls, ["plan"] = s.Plan, ["homework"] = s.Homework,
            ["created"] = DateTime.UtcNow.ToString("o"), ["tabs"] = tabs
        };
        using var zip = ZipFile.Open(target, ZipArchiveMode.Update);
        var e = zip.CreateEntry(SessionManifest, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    /// <summary>بعد استقبال حزمة: إن كان فيها جلسة تُضاف بتبويبات الكتب الجديدة وخطتها وواجبها.</summary>
    private StudySession? ImportSessionFromZip(string zipPath, ShelfShare.ImportResult r)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var e = zip.GetEntry(SessionManifest);
            if (e == null) return null;
            using var rd = new StreamReader(e.Open(), Encoding.UTF8);
            if (JsonNode.Parse(rd.ReadToEnd()) is not JsonObject m) return null;
            var s = new StudySession
            {
                Name = (string?)m["name"] ?? r.Name,
                Plan = (string?)m["plan"],
                Homework = (string?)m["homework"]
            };
            foreach (var t in (m["tabs"] as JsonArray ?? new()).OfType<JsonObject>())
            {
                if ((string?)t["kind"] != WorkTab.BookKind) continue;
                string? old = (string?)t["ref"];
                if (old == null || !r.IdMap.TryGetValue(old, out var id) || r.Books.All(b => b.Id != id)) continue;
                s.Tabs.Add(new WorkTab { Kind = WorkTab.BookKind, Ref = id, Title = (string?)t["title"], Page = (int?)t["page"] });
            }
            _library.Sessions.Add(s);
            MarkDirty();
            return s;
        }
        catch (Exception ex) { Log.Error("import session", ex); return null; }
    }

    // ---------------- الفيديو بالفصول ----------------

    /// <summary>فصول من العلامات: (البداية، العنوان) بالترتيب، وأولها من الصفر.</summary>
    internal static List<(long Ms, string Title)> ChaptersOf(VideoItem v)
    {
        var marks = v.Bookmarks.Where(b => b.TimeMs >= 0).OrderBy(b => b.TimeMs)
            .Select(b => (b.TimeMs, string.IsNullOrWhiteSpace(b.Note) ? "علامة" : b.Note!.Replace('\n', ' ').Trim())).ToList();
        var r = new List<(long, string)>();
        if (marks.Count == 0 || marks[0].TimeMs > 1000) r.Add((0, "البداية"));
        foreach (var m in marks) if (r.Count == 0 || m.TimeMs - r[^1].Item1 >= 1000) r.Add(m);
        return r;
    }

    private async Task<string?> ExportVideoWithChaptersAsync(VideoItem v, string folder, CancellationToken ct)
    {
        if (Tools.FindFfmpeg() == null && !await EnsureFfmpegAsync("تصدير الفيديو بالفصول")) return null;
        string ff = Tools.FindFfmpeg()!;
        var chapters = ChaptersOf(v);
        long duration = v.DurationMs > 0 ? v.DurationMs : chapters[^1].Ms + 60_000;
        var meta = new StringBuilder(";FFMETADATA1\n");
        meta.Append("title=").Append(Escape(v.Title)).Append('\n');
        for (int i = 0; i < chapters.Count; i++)
        {
            long end = i + 1 < chapters.Count ? chapters[i + 1].Ms : Math.Max(duration, chapters[i].Ms + 1000);
            meta.Append("[CHAPTER]\nTIMEBASE=1/1000\n").Append($"START={chapters[i].Ms}\nEND={end}\n").Append("title=").Append(Escape(chapters[i].Title)).Append('\n');
        }
        string name = SafeFileName(v.Title, "تسجيل");
        string metaFile = Path.Combine(Path.GetTempPath(), "masar-chapters-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        await File.WriteAllTextAsync(metaFile, meta.ToString(), new UTF8Encoding(false), ct);
        string output = Path.Combine(folder, name + " — بالفصول.mp4");
        try
        {
            var r = await ProcessRunner.RunAsync(ff, new[] { "-y", "-hide_banner", "-loglevel", "error", "-i", v.LocalPath, "-i", metaFile,
                "-map", "0", "-map_metadata", "1", "-map_chapters", "1", "-c", "copy", "-movflags", "+faststart", output }, TimeSpan.FromMinutes(20), ct);
            if (r.ExitCode != 0 || !File.Exists(output)) { Log.Info("chapters ffmpeg: " + r.StdErr); return null; }
        }
        finally { try { File.Delete(metaFile); } catch { } }
        var txt = new StringBuilder();
        txt.Append(v.Title).Append("\r\n\r\n");
        foreach (var (ms, title) in chapters) txt.Append(TimeFormat.Format(ms)).Append(' ').Append(title).Append("\r\n");
        await File.WriteAllTextAsync(Path.Combine(folder, name + " — الفصول.txt"), txt.ToString(), new UTF8Encoding(true), ct);
        return output;

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("=", "\\=").Replace(";", "\\;").Replace("#", "\\#").Replace("\n", " ");
    }
}
