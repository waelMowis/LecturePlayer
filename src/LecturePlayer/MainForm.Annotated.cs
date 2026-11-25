using System.Text.Json.Nodes;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// «النسخة المشروحة»: PDF جديد = الكتاب + الشرح عليه (الخطوط فيكتور، والعربي والملصقات صور عالية الدقة، والملاحظات تعليقات PDF).
/// بيتحفظ في فولدر «شروحاتي» جنب المكتبة ويظهر في رف «شروحاتي»، ويتحدّث لوحده لما تقفل الكتاب أو تسيبه دقيقة.
/// الأصل مابيتلمسش أبدًا.
/// </summary>
internal partial class MainForm
{
    private const string AnnotatedShelfKind = "annotated";
    private const string AnnotatedFolderName = "شروحاتي";
    private readonly HashSet<string> _annotQueue = new(StringComparer.Ordinal);
    private readonly HashSet<string> _annotBusy = new(StringComparer.Ordinal);
    private long _dueAnnot;
    /// <summary>النسخة اتعملت من أنهي نسخة من ملف الشرح (وقته) — أدق من مقارنة الأوقات (ساعات الأجهزة ممكن تختلف مع المزامنة).</summary>
    private readonly Dictionary<string, DateTime> _annotBuiltFrom = new(StringComparer.Ordinal);

    private string AnnotatedDir => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_libraryPath)) ?? ".", AnnotatedFolderName);

    private Book? AnnotatedCopyOf(Book b) => _library.Books.FirstOrDefault(x => x.AnnotatedOf == b.Id);

    private Shelf AnnotatedShelf()
    {
        var s = _library.Shelves.FirstOrDefault(x => x.Kind == AnnotatedShelfKind);
        if (s != null) return s;
        s = AddShelf(AnnotatedFolderName, null, checkpoint: false);
        s.Kind = AnnotatedShelfKind;
        s.IconKey = "pen";
        s.TouchInfo();
        return s;
    }

    private static bool CanAnnotate(Book b) => b.AnnotatedOf == null && BookFiles.FormatOf(b.IsOnline ? b.Path : b.LocalPath) == "pdf";

    /// <summary>اسم ملف النسخة: «اسم الكتاب — مشروح.pdf» (ولو الاسم متاخد لكتاب تاني بنزوّد جزء من رقمه).</summary>
    private string AnnotatedTarget(Book b)
    {
        if (AnnotatedCopyOf(b) is { } copy && !string.IsNullOrEmpty(copy.LocalPath)) return copy.LocalPath;
        string safe = new string(b.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        if (safe.Length > 80) safe = safe[..80];
        if (safe.Length == 0) safe = "كتاب";
        string file = Path.Combine(AnnotatedDir, safe + " — مشروح.pdf");
        if (File.Exists(file) && _library.Books.Any(x => string.Equals(x.LocalPath, file, StringComparison.OrdinalIgnoreCase) && x.AnnotatedOf != b.Id))
            file = Path.Combine(AnnotatedDir, $"{safe} — مشروح ({b.Id[..6]}).pdf");
        return file;
    }

    /// <summary>النسخة محتاجة تتحدّث؟ (الشرح أحدث من الملف، أو لسه ماتعملتش)</summary>
    private bool NeedsAnnotated(Book b)
    {
        if (!HasLibrary || !CanAnnotate(b) || b.InkCount == 0) return false;
        var ink = InkStore.Stamp(InkStore.FileFor(_libraryPath, b.Id));
        if (ink == DateTime.MinValue) return false;
        string target = AnnotatedTarget(b);
        if (_annotBuiltFrom.TryGetValue(b.Id, out var from) && File.Exists(target)) return from != ink;
        return !File.Exists(target) || File.GetLastWriteTimeUtc(target) < ink;
    }

    private void QueueAnnotated(Book b) { if (NeedsAnnotated(b)) _annotQueue.Add(b.Id); }

    /// <summary>كل ثانية: النسخ اللي مستنية بتتعمل لما الكتاب يتقفل أو يعدّي دقيقة من غير رسم.</summary>
    private void AnnotatedTick()
    {
        if (_ink is { } s && s.Book.InkCount > 0 && !s.Dirty && s.ChangedUtc > DateTime.MinValue
            && DateTime.UtcNow - s.ChangedUtc > TimeSpan.FromSeconds(60) && ReaderShown && _currentBook == s.Book)
        {
            s.ChangedUtc = DateTime.MinValue;
            QueueAnnotated(s.Book);
        }
        if (_annotQueue.Count == 0 || !Due(ref _dueAnnot, 2000)) return;
        foreach (string id in _annotQueue.ToList())
        {
            if (_annotBusy.Contains(id)) continue;
            if (_inkByGen.Values.Any(x => x.Book.Id == id && x.Dirty)) continue; // لسه بيتحفظ
            _annotQueue.Remove(id);
            if (_library.Books.FirstOrDefault(x => x.Id == id) is { } b) _ = BuildAnnotatedCopyAsync(b, quiet: true);
            break; // واحدة واحدة
        }
    }

    /// <summary>بيعمل/يحدّث النسخة المشروحة (أو يصدّرها لمكان تاني لو target متحدد).</summary>
    internal async Task<bool> BuildAnnotatedCopyAsync(Book b, bool quiet, string? exportTo = null, JsonObject? fromDoc = null)
    {
        if (!HasLibrary || !CanAnnotate(b)) return false;
        if (!ReaderPane.Available) { if (!quiet) ShowStatus("يحتاج إلى قارئ الكتب (WebView2)", 5); return false; }
        if (_annotBusy.Contains(b.Id))
        {
            if (quiet) { _annotQueue.Add(b.Id); return false; }
            // طلب منك وفي نسخة تلقائية شغالة: نستناها تخلص وبعدين نكمّل
            var until = DateTime.UtcNow.AddMinutes(3);
            while (_annotBusy.Contains(b.Id) && DateTime.UtcNow < until) await Task.Delay(200);
            if (exportTo == null && !NeedsAnnotated(b)) { ShowStatus("النسخة المشروحة محدّثة ✓", 4); return true; }
        }
        if (!_annotBusy.Add(b.Id)) return false;
        try
        {
            if (ReaderShown && _currentBook == b && _reader != null) { await _reader.FlushInkAsync(); await Task.Delay(150); }
            SaveAllInk();
            var inkStamp = InkStore.Stamp(InkStore.FileFor(_libraryPath, b.Id));
            var doc = fromDoc ?? (_ink?.Book == b ? _ink.Doc : InkStore.Load(InkStore.FileFor(_libraryPath, b.Id)));
            if (InkStore.Count(doc) == 0) { if (!quiet) ShowStatus("لا يوجد شرح على هذا الكتاب بعد — افتحه واضغط 🖊", 5); return false; }
            string src = BookFile(b);
            if (!File.Exists(src)) { if (!quiet) ShowStatus("ملف الكتاب غير موجود", 5); return false; }
            var copy = AnnotatedCopyOf(b);
            if (exportTo == null && copy != null && ReaderShown && _currentBook == copy)
            {
                _annotQueue.Add(b.Id); // النسخة مفتوحة قدامك: نستنى لما تقفلها
                return false;
            }
            string target = exportTo ?? AnnotatedTarget(b);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var pane = EnsureReader();
            if (!quiet) ShowStatus($"جارٍ إنشاء النسخة المشروحة من «{b.Title}»…", 30);
            var progress = new Progress<double>(p => { if (!quiet) ShowStatus($"جارٍ إنشاء النسخة المشروحة… {(int)(p * 100)}%", 10); });
            var r = await pane.BuildAnnotatedAsync(src, doc, target, b.Title, progress, CancellationToken.None);
            if (!r.Ok)
            {
                Log.Info("annotated copy failed: " + r.Error);
                if (!quiet || exportTo != null) ShowBanner("تعذّر إنشاء النسخة المشروحة: " + r.Error, BannerKind.Warning, seconds: 10, key: "annot");
                return false;
            }
            FileCache.Forget(target);
            Log.Info($"annotated copy: {b.Title} → {target} ({r.Size / 1024} KB, {r.Pages} pages)");
            if (exportTo != null) { ShowStatus("صُدِّرت النسخة المشروحة ✓", 5); return true; }
            _annotBuiltFrom[b.Id] = inkStamp;
            if (copy == null)
            {
                var shelf = AnnotatedShelf();
                copy = new Book
                {
                    Title = b.Title + " — مشروح", Path = target, LocalPath = target, ShelfId = shelf.Id, AnnotatedOf = b.Id, IconKey = "pen",
                    Author = b.Author, RightToLeft = b.RightToLeft, Spread = b.Spread
                };
                copy.TouchInfo();
                _library.Books.Add(copy);
                MarkDirty();
                RebuildShelf();
                ShowStatus($"أُنشئت النسخة المشروحة في «{AnnotatedFolderName}» 🖊 — تتحدّث تلقائيًا كلما شرحت", 6);
            }
            else
            {
                copy.PageCount = r.Pages;
                if (_shelfMode) RefreshBookRows(copy);
                if (!quiet) ShowStatus("حُدِّثت النسخة المشروحة ✓", 4);
            }
            QueueProbe(new[] { copy });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("annotated copy", ex);
            if (!quiet) ShowBanner("تعذّر إنشاء النسخة المشروحة: " + ex.Message, BannerKind.Warning, seconds: 10, key: "annot");
            return false;
        }
        finally { _annotBusy.Remove(b.Id); }
    }

    private void ExportAnnotatedAs(Book b)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "حفظ نسخة مشروحة", Filter = "PDF|*.pdf", FileName = b.Title + " — مشروح.pdf", RestoreDirectory = true, OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _ = BuildAnnotatedCopyAsync(b, quiet: false, exportTo: dlg.FileName);
    }

    private async void ExportPagePng(Book b)
    {
        if (!(ReaderShown && _currentBook == b && _reader != null)) { OpenBook(b); return; }
        int page = Math.Max(1, _reader.Current?.Page ?? b.LastPage);
        string? path = SmokeSavePath;
        if (path == null)
        {
            using var dlg = new SaveFileDialog { Title = "حفظ الصفحة صورة", Filter = "صورة PNG|*.png", FileName = $"{b.Title} - الصفحة {page}.png", RestoreDirectory = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
        }
        var r = await _reader.PagePngAsync(page, path, 2.5);
        if (r.Ok) ShowStatus($"حُفظت الصفحة {page} مع الشرح ✓", 4);
        else ShowBanner("تعذّر حفظ الصورة: " + r.Error, BannerKind.Warning, seconds: 8);
    }

    /// <summary>للفحص: مكان حفظ من غير شباك «حفظ باسم».</summary>
    internal string? SmokeSavePath;

    // ---------------- نسخ الشرح ----------------

    private void SaveInkVersion(Book b, string? name = null)
    {
        if (!HasLibrary) return;
        SaveAllInk();
        var doc = _ink?.Book == b ? _ink.Doc : InkStore.Load(InkStore.FileFor(_libraryPath, b.Id));
        if (InkStore.Count(doc) == 0) { ShowStatus("لا يوجد شرح لحفظ نسخة منه", 4); return; }
        name ??= AskText("اسم النسخة", "اسم يذكّرك بها (مثلًا: قبل الامتحان، شرح الأستاذ):", DateTime.Now.ToString("d MMMM"));
        if (name == null) return;
        InkStore.SaveVersion(_libraryPath, b.Id, doc, name);
        ShowStatus($"حُفظت نسخة «{name}» من الشرح ✓", 4);
    }

    private void RestoreInkVersion(Book b, InkStore.Version v)
    {
        if (!Confirm($"هل تريد إرجاع الشرح إلى نسخة «{v.Name}» ({v.Local:yyyy-MM-dd HH:mm})؟\nسيُحفظ الشرح الحالي نسخةً أولًا لتتمكن من الرجوع إليه.")) return;
        SaveAllInk();
        string file = InkStore.FileFor(_libraryPath, b.Id);
        bool live = _ink?.Book == b;
        var doc = live ? _ink!.Doc : InkStore.Load(file);
        if (InkStore.Count(doc) > 0) InkStore.SaveVersion(_libraryPath, b.Id, doc, "قبل الرجوع إلى " + v.Name);
        InkStore.Restore(doc, InkStore.Load(v.File));
        if (live)
        {
            _ink!.Dirty = true;
            SaveInk(_ink);
            if (ReaderShown && _currentBook == b) _ = _reader?.MergeInkAsync(_ink.Doc);
        }
        else
        {
            InkStore.Save(file, doc);
            b.InkCount = InkStore.Count(doc); b.TouchProgress(); MarkPositionDirty();
        }
        QueueAnnotated(b);
        ShowStatus($"أُرجع الشرح إلى نسخة «{v.Name}» ✓", 5);
    }

    private ToolStripItem[] InkVersionItems(Book b)
    {
        var items = new List<ToolStripItem> { MenuItem("💾  احفظ نسخة من الشرح الآن…", () => SaveInkVersion(b)) };
        var list = HasLibrary ? InkStore.Versions(_libraryPath, b.Id) : new();
        if (list.Count > 0) items.Add(Sep());
        foreach (var v in list.Take(30))
            items.Add(SubMenuOf($"🕘  {v.Name} — {v.Local:yyyy-MM-dd HH:mm} ({v.Count})",
                MenuItem("↺  ارجع إلى هذه النسخة", () => RestoreInkVersion(b, v)),
                MenuItem("📤  صدّرها PDF…", () =>
                {
                    using var dlg = new SaveFileDialog { Title = "حفظ النسخة PDF", Filter = "PDF|*.pdf", FileName = $"{b.Title} — {v.Name}.pdf", RestoreDirectory = true };
                    if (dlg.ShowDialog(this) == DialogResult.OK) _ = BuildAnnotatedCopyAsync(b, quiet: false, exportTo: dlg.FileName, fromDoc: InkStore.Load(v.File));
                }),
                MenuItem("🗑  احذف هذه النسخة", () => { if (Confirm($"هل تريد حذف نسخة «{v.Name}»؟")) { try { File.Delete(v.File); } catch { } } })));
        return items.ToArray();
    }

    /// <summary>بنود الشرح في قائمة الكتاب (بالترتيب: فتح ← تقدّم/نسخ ← تصدير).</summary>
    private void AddAnnotatedMenu(Book b, ToolStripItemCollection items, int section)
    {
        if (!CanAnnotate(b)) return;
        var copy = AnnotatedCopyOf(b);
        if (section == 0 && copy != null)
            items.Add(MenuItem("🖊  افتح النسخة المشروحة" + (NeedsAnnotated(b) ? " (ستُحدَّث)" : ""), () => { if (NeedsAnnotated(b)) _ = BuildAnnotatedCopyAsync(b, quiet: false).ContinueWith(_ => BeginInvoke(() => OpenBook(copy))); else OpenBook(copy); }));
        if (section == 5 && b.InkCount > 0)
            items.Add(SubMenuOf("↺  نسخ الشرح (حفظ/رجوع)", InkVersionItems(b)));
        if (section == 6 && b.InkCount > 0)
        {
            items.Add(SubMenuOf("📤  الشرح (PDF / صورة)",
                MenuItem(copy == null ? "🖊  أنشئ النسخة المشروحة في «شروحاتي»" : "🔄  حدّث النسخة المشروحة الآن", () => _ = BuildAnnotatedCopyAsync(b, quiet: false)),
                MenuItem("📤  صدّر نسخة مشروحة إلى مكان آخر…", () => ExportAnnotatedAs(b)),
                MenuItem("🖼  الصفحة الحالية مع الشرح صورة (PNG)…", () => ExportPagePng(b))));
        }
    }
}
