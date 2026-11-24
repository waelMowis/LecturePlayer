using System.Text;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>تظليل في شجرة الكتب.</summary>
internal sealed record BookHighlightRef(Book Book, Highlight Highlight);

/// <summary>
/// «الكتب» المرحلة 3: التظليل والملاحظات (ومشاركتهم في ملف)، نص الكتب (للبحث في كل الكتب وللذكاء)،
/// OCR للكتب المتصوّرة، الذكاء الاصطناعي (لخّص الفصل، اشرحلي الصفحة، اختبرني)، وهدف القراية اليومي.
/// </summary>
internal sealed partial class MainForm
{
    /// <summary>للفحص: رد Claude بدل الطلب الحقيقي.</summary>
    internal static Func<string, string>? SmokeClaudeReply;

    // =====================================================================
    //  التظليل
    // =====================================================================

    private void OnHighlightRequested(int gen, int page, double[][] rects, string text, string color, bool wantNote, string? cfi)
    {
        if (gen != _readerGen || _currentBook is not { } b) return;
        string? note = null;
        if (wantNote)
        {
            note = AskText("ملاحظة على التظليل", "ملاحظتك على الجزء ده:", "");
            if (note == null) return;
        }
        var h = new Highlight { Page = page, Rects = cfi != null ? "cfi:" + cfi : Highlight.FormatRects(rects), Text = text.Trim(), Color = color, Note = note };
        b.Highlights.Add(h);
        b.Highlights.Sort((x, y) => x.Page != y.Page ? x.Page.CompareTo(y.Page) : x.CreatedUtc.CompareTo(y.CreatedUtc));
        b.TouchProgress();
        MarkDirty();
        _ = _reader?.SetHighlightsAsync(b);
        if (_shelfMode) RebuildShelf();
        ShowStatus(note != null ? "اتظلل واتحفظت الملاحظة ✓" : "اتظلل ✓ — بتلاقيه تحت الكتاب في «الكتب»", 3);
    }

    private void OnHighlightEdited(int gen, string id, string? color, bool delete)
    {
        if (gen != _readerGen || _currentBook is not { } b) return;
        var h = b.Highlights.FirstOrDefault(x => x.Id == id);
        if (h == null) return;
        if (delete) RemoveHighlight(b, h);
        else if (color != null && color != h.Color) { h.Color = color; b.TouchProgress(); MarkDirty(); _ = _reader?.SetHighlightsAsync(b); }
    }

    private void RemoveHighlight(Book b, Highlight h)
    {
        Checkpoint("شيل تظليل");
        b.Highlights.Remove(h);
        b.TouchProgress();
        MarkDirty();
        if (b == _currentBook) _ = _reader?.SetHighlightsAsync(b);
        if (_shelfMode) RebuildShelf();
    }

    private void EditHighlightNote(BookHighlightRef r)
    {
        string? note = AskText("ملاحظة على التظليل", "ملاحظتك:", r.Highlight.Note ?? "");
        if (note == null || note == r.Highlight.Note) return;
        Checkpoint("ملاحظة تظليل");
        r.Highlight.Note = note;
        r.Book.TouchProgress();
        MarkDirty();
        RebuildShelf();
    }

    private void UpdateHighlightNode(TreeNode n, Book b, Highlight h)
    {
        string text = h.Text.Length > 70 ? h.Text[..70] + "…" : h.Text;
        n.Text = "«" + Modern.Bidi(text) + "»" + (string.IsNullOrWhiteSpace(h.Note) ? "" : "  📝");
        _shelfTree.SetRow(n, new LectureTreeView.RowInfo($"ص {h.Page}"));
        var color = h.Color switch { "green" => Color.FromArgb(140, 233, 154), "blue" => Color.FromArgb(116, 192, 252), "pink" => Color.FromArgb(250, 162, 193), _ => Color.FromArgb(255, 224, 102) };
        n.ImageKey = n.SelectedImageKey = ShelfImageKey("highlight", "highlight", null, IconGlyphs.ToHex(color), color, false);
        n.ForeColor = Theme.Text;
        n.NodeFont = _regularNodeFont;
        n.ToolTipText = $"صفحة {h.Page}\n«{h.Text}»" + (string.IsNullOrWhiteSpace(h.Note) ? "" : "\n\n📝 " + h.Note);
    }

    private void BuildHighlightMenu(BookHighlightRef r, ToolStripItemCollection items)
    {
        items.Add(MenuItem($"▶  روح لصفحة {r.Highlight.Page}", () => OpenBook(r.Book, r.Highlight.Page), "Enter"));
        items.Add(Sep());
        items.Add(MenuItem(string.IsNullOrWhiteSpace(r.Highlight.Note) ? "✏  اكتب ملاحظة…" : "✏  تعديل الملاحظة…", () => EditHighlightNote(r), "F2"));
        items.Add(MenuItem("📋  انسخ النص", () => { try { Clipboard.SetText(r.Highlight.Text); ShowStatus("اتنسخ ✓", 2); } catch { } }));
        items.Add(Sep());
        items.Add(MenuItem("🗑  شيل التظليل", () => RemoveHighlight(r.Book, r.Highlight), "Delete"));
    }

    /// <summary>التظليلات والملاحظات والعلامات كملف Markdown (مترتبة بالفصول).</summary>
    internal string BookNotesMarkdown(Book b)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {b.Title}");
        if (!string.IsNullOrWhiteSpace(b.Author)) sb.AppendLine($"**المؤلف:** {b.Author}");
        if (b.PageCount > 0) sb.AppendLine($"**التقدّم:** قريت {b.ReadCount} من {b.PageCount} صفحة ({(int)Math.Round(b.Fraction * 100)}%)");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(b.Notes)) { sb.AppendLine("## ملاحظاتي"); sb.AppendLine(); sb.AppendLine(b.Notes.Trim()); sb.AppendLine(); }
        var items = b.Highlights.Select(h => (h.Page, Kind: 0, Text: $"> {h.Text.Replace("\n", " ")}" + (string.IsNullOrWhiteSpace(h.Note) ? "" : $"\n\n📝 {h.Note}")))
            .Concat(b.Marks.Select(m => (m.Page, Kind: 1, Text: "🔖 علامة" + (string.IsNullOrWhiteSpace(m.Note) ? "" : $": {m.Note}"))))
            .OrderBy(x => x.Page).ThenBy(x => x.Kind).ToList();
        if (items.Count == 0) { sb.AppendLine("_مفيش تظليلات ولا علامات لسه._"); return sb.ToString(); }
        var sections = b.Sections();
        string? current = null;
        foreach (var it in items)
        {
            string? sec = sections.LastOrDefault(s => s.From <= it.Page).Title;
            if (sec != current && sec != null) { sb.AppendLine($"## {sec}"); sb.AppendLine(); current = sec; }
            sb.AppendLine($"**ص {it.Page}**");
            sb.AppendLine();
            sb.AppendLine(it.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private void ExportBookNotes(Book b)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "حفظ التظليلات والملاحظات", Filter = "Markdown (*.md)|*.md|نص (*.txt)|*.txt",
            FileName = NotesExporter.SafeFileName(b.Title) + " - ملاحظات.md"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dlg.FileName, BookNotesMarkdown(b), new UTF8Encoding(false));
        ShowStatus("اتحفظ ✓ " + Path.GetFileName(dlg.FileName), 4);
    }

    // =====================================================================
    //  نص الكتاب (للبحث والذكاء) والـ OCR
    // =====================================================================

    /// <summary>نص الصفحات من..لحد (من الكاش، والناقص بيتقري من الكتاب). ocr = الصفحات الفاضية تتقري بالـ OCR.</summary>
    internal async Task<BookText?> EnsureBookTextAsync(Book b, int from, int to, bool ocr, CancellationToken ct, IProgress<string>? progress = null)
    {
        string file = BookFile(b);
        if (!File.Exists(file)) return null;
        var text = BookText.Load(b);
        if (b.PageCount > 0) to = Math.Min(to, b.PageCount);
        from = Math.Max(1, from);
        bool epub = BookFiles.FormatOf(file) == "epub";
        if (epub) { from = 1; to = 100_000; } // EPUB بيتقري كله مرة واحدة (كل فصل بأول صفحة فيه)
        if (!text.Complete && !text.HasAll(from, to))
        {
            var pane = EnsureReader();
            bool hidden = !pane.Visible;
            if (hidden) { pane.Visible = true; pane.SendToBack(); }
            try
            {
                var got = await pane.ExtractTextAsync(file, from, to,
                    new Progress<(int Done, int Total)>(p => progress?.Report($"بيقرا نص «{b.Title}»… {p.Done}/{Math.Max(p.Total, to - from + 1)}")), ct);
                foreach (var (p, t) in got) text.Pages[p] = t;
                if (got.Count > 0 && text.PageCount == 0) text.PageCount = b.PageCount;
                if (epub || from == 1 && b.PageCount > 0 && to >= b.PageCount && text.HasAll(1, b.PageCount)) text.Complete = got.Count > 0;
                text.Save(b);
            }
            finally { if (hidden && !ReaderShown) pane.Visible = false; }
        }
        if (ocr && !epub) await OcrPagesAsync(b, text, text.EmptyPages(from, to), ct, progress);
        return text;
    }

    /// <summary>الصفحات المتصوّرة (من غير نص) ← صورة ← OCR بتاع الويندوز.</summary>
    private async Task OcrPagesAsync(Book b, BookText text, List<int> pages, CancellationToken ct, IProgress<string>? progress)
    {
        if (pages.Count == 0 || !Ocr.IsSupported) return;
        var pane = EnsureReader();
        bool hidden = !pane.Visible;
        if (hidden) { pane.Visible = true; pane.SendToBack(); }
        string lang = BookLooksArabic(b, text) ? "ar" : "en-US";
        string tmp = Path.Combine(Path.GetTempPath(), "masar-ocr-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        try
        {
            int i = 0;
            foreach (int p in pages)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"بيقرا الصفحات المتصوّرة (OCR)… {++i}/{pages.Count}");
                var png = await pane.RenderPageAsync(BookFile(b), p, 2, ct);
                if (png == null) continue;
                await File.WriteAllBytesAsync(tmp, png, ct);
                string s;
                try { s = await Ocr.RecognizeAsync(tmp, lang); }
                catch (Exception ex) { Log.Info("book ocr: " + ex.Message); if (i == 1) throw; continue; }
                text.Pages[p] = s;
                if (!text.OcrPages.Contains(p)) text.OcrPages.Add(p);
                if (i % 5 == 0) text.Save(b);
            }
        }
        finally
        {
            text.Save(b);
            try { File.Delete(tmp); } catch { }
            if (hidden && !ReaderShown) pane.Visible = false;
        }
    }

    private static bool BookLooksArabic(Book b, BookText text)
    {
        string sample = b.Title + " " + string.Join(" ", text.Pages.Values.Take(5));
        int ar = sample.Count(c => c >= '؀' && c <= 'ۿ'), latin = sample.Count(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        return ar > latin;
    }

    /// <summary>«حوّل الكتاب لنص» (OCR للصفحات المتصوّرة) — عشان البحث والذكاء يشتغلوا عليه.</summary>
    internal async Task OcrBookAsync(Book b)
    {
        if (!Ocr.IsSupported) { Dialogs.Info(this, "قراءة الكلام من الصور (OCR) محتاجة ويندوز 10 أو أحدث."); return; }
        if (b.IsOnline && !File.Exists(BookFile(b)) && !await FetchBookAsync(b, open: false)) return;
        using var cts = new CancellationTokenSource();
        ShowBanner($"بيجهّز نص «{b.Title}»…", BannerKind.Info, "إلغاء", () => cts.Cancel(), seconds: 0, key: "book-ocr");
        try
        {
            int to = b.PageCount > 0 ? b.PageCount : 100_000;
            var text = await EnsureBookTextAsync(b, 1, to, ocr: true, cts.Token, new Progress<string>(s => UpdateBannerText("book-ocr", s)));
            if (text == null) { HideBanner("book-ocr"); return; }
            int empty = text.EmptyPages(1, to).Count;
            ShowBanner($"نص «{b.Title}» جاهز ✓ — {text.Pages.Count} صفحة" + (text.OcrPages.Count > 0 ? $"، منهم {text.OcrPages.Count} بالـ OCR" : "") +
                       (empty > 0 ? $" ({empty} صفحة مفيهاش كلام)" : ""), BannerKind.Success, seconds: 8, key: "book-ocr");
        }
        catch (OperationCanceledException) { HideBanner("book-ocr"); }
        catch (Exception ex) { ShowBanner("مقدرتش أقرا الكتاب: " + ex.Message, BannerKind.Warning, seconds: 12, key: "book-ocr"); }
    }

    // =====================================================================
    //  البحث في كل الكتب
    // =====================================================================

    private BookSearchForm? _bookSearch;

    internal void ShowBookSearch(string? query = null)
    {
        if (!EnsureLibrary()) return;
        if (_bookSearch is { IsDisposed: false })
        {
            _bookSearch.Activate();
            if (query != null) _bookSearch.SetQuery(query);
            return;
        }
        var form = new BookSearchForm(q => _library.Books.SelectMany(b => BookText.Load(b).Search(b, q))
                .OrderByDescending(h => h.Score).ThenBy(h => h.Book.Title).ThenBy(h => h.Page).Take(300).ToList(),
            (hit, q) => { OpenBook(hit.Book, hit.Page); _ = FindInReaderSoonAsync(hit.Book, q); });
        _bookSearch = form;
        form.Show(this);
        if (query != null) form.SetQuery(query);
        _ = IndexAllBooksAsync(form);
    }

    private async Task FindInReaderSoonAsync(Book b, string q)
    {
        for (int i = 0; i < 100 && (!ReaderShown || _currentBook != b || _reader!.Info == null); i++) await Task.Delay(100);
        if (ReaderShown && _currentBook == b) await _reader!.FindAsync(q);
    }

    /// <summary>بيجهّز نص الكتب اللي لسه ماتقريتش (من غير OCR) عشان البحث يلاقي فيها.</summary>
    private async Task IndexAllBooksAsync(BookSearchForm form)
    {
        var todo = _library.Books.Where(b => File.Exists(BookFile(b)) && BookText.Load(b) is var t && !t.Complete && (b.PageCount <= 0 || !t.HasAll(1, b.PageCount))).ToList();
        int total = _library.Books.Count, done = total - todo.Count;
        form.SetStatus(todo.Count == 0 ? $"بيدوّر في {total} كتاب" : $"بيجهّز نص الكتب… {done}/{total}");
        foreach (var b in todo)
        {
            if (form.IsDisposed) return;
            try { await EnsureBookTextAsync(b, 1, b.PageCount > 0 ? b.PageCount : 100_000, ocr: false, CancellationToken.None); }
            catch (Exception ex) { Log.Info("book index: " + ex.Message); }
            done++;
            if (!form.IsDisposed) { form.SetStatus(done < total ? $"بيجهّز نص الكتب… {done}/{total}" : $"بيدوّر في {total} كتاب"); form.Rerun(); }
        }
        int scanned = _library.Books.Count(b => BookText.Load(b) is { Pages.Count: > 0 } t && t.EmptyPages(1, t.Pages.Keys.Max()).Count > t.Pages.Count / 2);
        if (!form.IsDisposed && scanned > 0)
            form.SetStatus($"بيدوّر في {total} كتاب — فيه {scanned} كتاب متصوّر (من غير نص): كليك يمين عليه ← «حوّل الصفحات المتصوّرة لنص» عشان يدخل البحث");
    }

    // =====================================================================
    //  الذكاء الاصطناعي
    // =====================================================================

    private async Task<string?> AskClaudeForBookAsync(string what, (string System, string User, int MaxTokens) prompt, CancellationToken ct)
    {
        if (SmokeClaudeReply != null) return SmokeClaudeReply(prompt.User);
        return await RunClaudeAsync(what, prompt, ct);
    }

    /// <summary>بيجهّز النص (ولو الصفحات متصوّرة بيعرض الـ OCR) وبعدين يشغّل المهمة.</summary>
    private async Task WithBookTextAsync(Book b, int from, int to, string what, Func<BookText, CancellationToken, Task> work)
    {
        if (b.IsOnline && !File.Exists(BookFile(b)) && !await FetchBookAsync(b, open: false)) return;
        var cts = BeginSmart();
        if (cts == null) return;
        try
        {
            ShowStatus($"بيجهّز نص الصفحات {from}–{to}…", 30);
            var text = await EnsureBookTextAsync(b, from, to, ocr: false, cts.Token);
            if (text == null) { Dialogs.Info(this, "ملف الكتاب مش موجود."); return; }
            if (text.EmptyPages(from, to).Count > (to - from + 1) / 2)
            {
                if (!Ocr.IsSupported || !Confirm("الصفحات دي متصوّرة (من غير نص). أقراها بالـ OCR الأول؟ (ممكن تاخد شوية)")) return;
                text = await EnsureBookTextAsync(b, from, to, ocr: true, cts.Token, new Progress<string>(s => ShowStatus(s, 30)));
                if (text == null) return;
            }
            if (text.Joined(from, to).Trim().Length < 40) { Dialogs.Info(this, "مالقيتش كلام كفاية في الصفحات دي."); return; }
            await work(text, cts.Token);
        }
        catch (OperationCanceledException) { ShowStatus("اتلغى"); }
        catch (ClaudeException ex) { Dialogs.Error(this, ex.Message); }
        catch (FormatException ex) { Dialogs.Error(this, $"Claude رد بشكل مش مفهوم ({what}). جرب تاني.\n\n{ex.Message}"); }
        catch (Exception ex) { Log.Error(what, ex); Dialogs.Error(this, $"{what} فشل:\n{ex.Message}"); }
        finally { EndSmart(cts); }
    }

    internal Task SummarizeBookPartAsync(Book b, string part, int from, int to) => WithBookTextAsync(b, from, to, "الملخص", async (text, ct) =>
    {
        string? reply = await AskClaudeForBookAsync("الملخص", AiTasks.BookSummaryPrompt(b.Title, part, text.Joined(from, to)), ct);
        if (reply == null) return;
        _lastBookAiText = reply;
        if (SmokeClaudeReply != null) return;
        new TextViewerForm($"ملخص: {part} — {b.Title}", reply, NotesExporter.SafeFileName(b.Title + " - " + part) + " - ملخص.md",
            Act("حطه في ملاحظات الكتاب", edited => AppendBookNotes(b, $"## ملخص: {part}\n\n{edited}"))).Show(this);
    });

    internal Task ExplainPageAsync(Book b, int page, string? question) => WithBookTextAsync(b, Math.Max(1, page - 1), page + 1, "الشرح", async (text, ct) =>
    {
        string? reply = await AskClaudeForBookAsync("الشرح", AiTasks.BookExplainPrompt(b.Title, page, text.Joined(Math.Max(1, page - 1), page + 1), question), ct);
        if (reply == null) return;
        _lastBookAiText = reply;
        if (SmokeClaudeReply != null) return;
        new TextViewerForm($"صفحة {page} — {b.Title}", (string.IsNullOrWhiteSpace(question) ? "" : $"**سؤالك:** {question}\n\n") + reply,
            NotesExporter.SafeFileName(b.Title) + $" - ص {page}.md",
            Act("حطه في ملاحظات الكتاب", edited => AppendBookNotes(b, $"## صفحة {page}\n\n{edited}"))).Show(this);
    });

    internal Task QuizBookPartAsync(Book b, string part, int from, int to) => WithBookTextAsync(b, from, to, "الاختبار", async (text, ct) =>
    {
        string? reply = await AskClaudeForBookAsync("الاختبار", AiTasks.BookQuizPrompt(b.Title, part, text.Joined(from, to)), ct);
        if (reply == null) return;
        var questions = AiTasks.ParseBookQuiz(reply, b.PageCount);
        _lastBookQuiz = questions;
        if (questions.Count == 0) { Dialogs.Info(this, "Claude مرجعش أسئلة صالحة. جرب تاني."); return; }
        if (SmokeClaudeReply != null) return;
        new QuizForm($"{part} — {b.Title}", questions, page => OpenBook(b, (int)page), (score, total) =>
            LogJournal(new JournalEntry { Kind = JournalEntry.Quiz, StartUtc = DateTime.UtcNow, Score = score, Total = total, CourseId = "book:" + b.Id, VideoPath = b.Path }),
            "روح للصفحة دي في الكتاب").Show(this);
    });

    private string? _lastBookAiText;
    private List<QuizQuestion>? _lastBookQuiz;

    private void AppendBookNotes(Book b, string text)
    {
        b.Notes = string.IsNullOrWhiteSpace(b.Notes) ? text : b.Notes.TrimEnd() + "\n\n---\n" + text;
        b.TouchProgress();
        MarkDirty();
        SaveLibrary();
        ShowStatus("اتحط في ملاحظات الكتاب ✓ (بتطلع في «حفظ التظليلات والملاحظات»)", 5);
    }

    /// <summary>الفصل/الجزء اللي فيه الصفحة.</summary>
    private static (string Title, int From, int To) PartOf(Book b, int page)
    {
        var secs = b.Sections();
        var s = secs.LastOrDefault(x => x.From <= page);
        if (s.Title == null) return ($"صفحات {Math.Max(1, page - 4)}–{page + 5}", Math.Max(1, page - 4), b.PageCount > 0 ? Math.Min(b.PageCount, page + 5) : page + 5);
        var part = s.Parts.LastOrDefault(x => x.From <= page);
        return part.Title != null && part.To - part.From >= 2 ? (part.Title, part.From, part.To) : (s.Title, s.From, s.To);
    }

    /// <summary>قايمة الذكاء من شريط القارئ (✨).</summary>
    private void ShowReaderAiMenu()
    {
        if (!ReaderShown || _currentBook is not { } b) return;
        int page = Math.Max(1, b.LastPage);
        var part = PartOf(b, page);
        var m = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        foreach (var it in BookAiItems(b, page, part)) m.Items.Add(it);
        m.Closed += (_, _) => BeginInvoke(m.Dispose);
        m.Show(Cursor.Position);
    }

    private IEnumerable<ToolStripItem> BookAiItems(Book b, int page, (string Title, int From, int To) part) => new ToolStripItem[]
    {
        MenuItem($"✨  اشرحلي صفحة {page}", () => _ = ExplainPageAsync(b, page, null)),
        MenuItem($"✨  اسأل عن صفحة {page}…", () => { string? q = AskText("اسأل عن الصفحة", $"سؤالك عن صفحة {page}:", ""); if (q != null) _ = ExplainPageAsync(b, page, q); }),
        MenuItem($"✨  لخّص «{part.Title}»", () => _ = SummarizeBookPartAsync(b, part.Title, part.From, part.To)),
        MenuItem($"✨  اختبرني في «{part.Title}»", () => _ = QuizBookPartAsync(b, part.Title, part.From, part.To))
    };

    // =====================================================================
    //  هدف القراية اليومي
    // =====================================================================

    /// <summary>صفحات النهارده (المسجلة + الجلسة الشغالة).</summary>
    private int PagesToday() =>
        JournalEntries.Where(e => e.Kind == JournalEntry.Pages && DateOnly.FromDateTime(e.StartUtc.ToLocalTime()) == TodayLocal).Sum(e => e.Score) + _pagesSession;

    private string ReadingGoalText()
    {
        if (_settings.DailyPagesGoal <= 0) return "";
        int today = PagesToday();
        return $"📖 {today}/{_settings.DailyPagesGoal} ص" + (today >= _settings.DailyPagesGoal ? " ✓" : "");
    }
}
