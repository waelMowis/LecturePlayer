using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// محرر الصفحات: بيشتغل على «نسخة عمل» في «شروحاتي» (الأصل مابيتلمسش).
/// الصفحة بتطلب الملفات من البرنامج (فتح PDF للضم، مكان حفظ للاستخراج، أو نسخة العمل نفسها) —
/// والصفحة ماتقدرش تكتب أي ملف تاني.
/// </summary>
internal partial class MainForm
{
    private Book? _peOnOpen;

    private Book? WorkCopyOf(Book b) => _library.Books.FirstOrDefault(x => x.WorkCopyOf == b.Id);

    /// <summary>من القائمة أو زرار 📑: لو ده الأصل بنعمل/نفتح نسخة العمل، ولو هو نسخة العمل بنفتح المحرر.</summary>
    internal void OpenPageEditor(Book b)
    {
        if (!HasLibrary || BookFiles.FormatOf(BookFile(b)) != "pdf") { ShowStatus("محرر الصفحات لملفات PDF فقط", 4); return; }
        if (b.WorkCopyOf != null)
        {
            if (ReaderShown && _currentBook == b) _ = _reader?.OpenPageEditorAsync();
            else { _peOnOpen = b; OpenBook(b); }
            return;
        }
        var copy = WorkCopyOf(b);
        if (copy == null || !File.Exists(copy.LocalPath))
        {
            if (!Confirm($"يُجرى تعديل الصفحات على «نسخة عمل» من «{b.Title}» في مجلد «{AnnotatedFolderName}» — لن يُلمس الكتاب الأصلي.\nسيُنسخ الشرح الذي عليه معها. هل نتابع؟")) return;
            copy = CreateWorkCopy(b, copy);
            if (copy == null) return;
        }
        _peOnOpen = copy;
        if (ReaderShown && _currentBook == copy) { _peOnOpen = null; _ = _reader?.OpenPageEditorAsync(); }
        else OpenBook(copy);
    }

    private Book? CreateWorkCopy(Book b, Book? existing)
    {
        try
        {
            SaveAllInk();
            string src = BookFile(b);
            if (!File.Exists(src)) { ShowStatus("ملف الكتاب غير موجود", 4); return null; }
            string safe = new string(b.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
            if (safe.Length > 80) safe = safe[..80];
            string target = Path.Combine(AnnotatedDir, (safe.Length > 0 ? safe : "كتاب") + " — نسخة عمل.pdf");
            if (File.Exists(target) && existing == null) target = Path.Combine(AnnotatedDir, $"{safe} — نسخة عمل ({b.Id[..6]}).pdf");
            Directory.CreateDirectory(AnnotatedDir);
            File.Copy(src, target, overwrite: true);
            var copy = existing ?? new Book
            {
                Title = b.Title + " — نسخة عمل", ShelfId = AnnotatedShelf().Id, WorkCopyOf = b.Id, IconKey = "pen",
                Author = b.Author, RightToLeft = b.RightToLeft, Spread = b.Spread, PageCount = b.PageCount, Chapters = b.Chapters.ToList()
            };
            copy.Path = target; copy.LocalPath = target;
            copy.TouchInfo();
            if (existing == null) _library.Books.Add(copy);
            // الشرح بيتنسخ معاها (ملف شرح للنسخة لوحدها)
            var ink = InkStore.Load(InkStore.FileFor(_libraryPath, b.Id));
            if (InkStore.Count(ink) > 0) { InkStore.Save(InkStore.FileFor(_libraryPath, copy.Id), ink); copy.InkCount = InkStore.Count(ink); }
            MarkDirty();
            RebuildShelf();
            Log.Info($"work copy: {b.Title} → {target}");
            return copy;
        }
        catch (Exception ex)
        {
            Log.Error("work copy", ex);
            ShowBanner("تعذّر إنشاء نسخة العمل: " + ex.Message, BannerKind.Warning, seconds: 10);
            return null;
        }
    }

    /// <summary>الصفحة عايزة ملف.</summary>
    private void OnReaderPick(int gen, string id, string kind, string name)
    {
        if (gen != _readerGen || _currentBook is not { } b || _reader == null) { _ = _reader?.PickResultAsync(id, null); return; }
        string? url = null;
        switch (kind)
        {
            case "save-self":
                // نسخة العمل بس — الأصل عمره ما بيتكتب عليه
                if (b.WorkCopyOf != null && File.Exists(BookFile(b))) url = _reader.SaveUrlFor(BookFile(b));
                else ShowStatus("يُحفظ التعديل في نسخة العمل فقط", 4);
                break;
            case "save-pdf":
            case "save-png":
            {
                bool png = kind == "save-png";
                string? path = SmokeSavePath;
                if (path == null)
                {
                    using var dlg = new SaveFileDialog { Title = png ? "حفظ صورة" : "حفظ PDF", Filter = png ? "صورة PNG|*.png" : "PDF|*.pdf", FileName = string.IsNullOrWhiteSpace(name) ? (png ? "صفحة.png" : "صفحات.pdf") : name, RestoreDirectory = true };
                    if (dlg.ShowDialog(this) == DialogResult.OK) path = dlg.FileName;
                }
                if (path != null)
                {
                    if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(BookFile(b)), StringComparison.OrdinalIgnoreCase) ||
                        _library.Books.Any(x => x.WorkCopyOf == null && string.Equals(x.LocalPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
                        ShowStatus("لن أكتب فوق كتاب في المكتبة — اختر اسمًا آخر", 5);
                    else { url = _reader.SaveUrlFor(path); _lastPickSave = path; }
                }
                break;
            }
            case "open-pdf":
            {
                string? path = SmokeOpenPath;
                if (path == null)
                {
                    using var dlg = new OpenFileDialog { Title = "اختر ملف PDF لضم صفحاته", Filter = "PDF|*.pdf", RestoreDirectory = true };
                    if (dlg.ShowDialog(this) == DialogResult.OK) path = dlg.FileName;
                }
                if (path != null && File.Exists(path)) url = _reader.ServeUrl(path);
                break;
            }
        }
        _ = _reader.PickResultAsync(id, url);
    }

    /// <summary>للفحص: ملف يتضم من غير شباك «فتح».</summary>
    internal string? SmokeOpenPath;

    /// <summary>
    /// بعد ما المحرر حفظ: العلامات والتظليل والمقروء والفهرس ومكانك بيمشوا مع صفحاتهم.
    /// map[i] = [الصفحة القديمة (0 = صفحة جديدة)، اللفة].
    /// </summary>
    private void OnPagesRemapped(int gen, int[][] map)
    {
        if (gen != _readerGen || _currentBook is not { } b) return;
        int NewOf(int old) { for (int i = 0; i < map.Length; i++) if (map[i].Length > 0 && map[i][0] == old) return i + 1; return 0; }
        int RotOf(int newPage) => newPage > 0 && map[newPage - 1].Length > 1 ? map[newPage - 1][1] : 0;
        foreach (var m in b.Marks.ToList()) { int n = NewOf(m.Page); if (n == 0) b.Marks.Remove(m); else m.Page = n; }
        // التظليل مستطيلات على الصفحة: لو الصفحة اتلفت مابقاش مكانه صح فبيتشال
        foreach (var h in b.Highlights.ToList()) { int n = NewOf(h.Page); if (n == 0 || RotOf(n) != 0) b.Highlights.Remove(h); else h.Page = n; }
        b.ReadPages = PageSet.Of(b.Read.Pages.Select(NewOf).Where(n => n > 0)).ToString();
        b.Chapters = b.Chapters.Select(c => new BookChapter { Title = c.Title, Level = c.Level, Page = NewOf(c.Page) }).Where(c => c.Page > 0).OrderBy(c => c.Page).ToList();
        int last = NewOf(Math.Max(1, b.LastPage));
        b.LastPage = last > 0 ? last : 1; b.LastTop = null; b.LastLeft = null;
        b.PageCount = map.Length;
        b.TouchProgress(); b.TouchInfo();
        MarkDirty();
        if (_shelfMode) RebuildShelf();
    }

    /// <summary>الملف اتغير: نقفله ونفتحه تاني على نفس الصفحة.</summary>
    private void ReloadAfterPageEdit(Book b)
    {
        SaveAllInk();
        FileCache.Forget(b.LocalPath);
        int page = Math.Max(1, b.LastPage);
        CloseReader(showHome: false);
        OpenBook(b, page);
        ShowStatus("حُفظت الصفحات ✓", 4);
    }
}
