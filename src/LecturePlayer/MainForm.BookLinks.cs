using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// «الكتب» المرحلة 2: كتب من روابط (Google Drive / Google Docs / Dropbox / OneDrive / رابط PDF مباشر) بتتنزل في كاش
/// على الجهاز ده وبنعرف لو اتحدّثت، والخاص بحسابك من نافذة جوه البرنامج أو من متصفحك (و«مسار» بيلقط الملف من التنزيلات)،
/// وملفات PDF اللي جوه الكورسات (المرفقات وموارد Moodle) بتتفتح في القارئ وتتتابع زي أي كتاب.
/// </summary>
internal sealed partial class MainForm
{
    private const string CourseFilesShelfName = "ملفات الكورسات";
    private readonly SemaphoreSlim _bookDownloadGate = new(1, 1);
    private CancellationTokenSource? _bookDlCts;
    private DownloadsWatcher? _downloadsWatcher;
    private System.Windows.Forms.Timer? _downloadsWatchTimeout;
    /// <summary>للفحص: فولدر «التنزيلات» بدل الحقيقي، ومن غير فتح المتصفح.</summary>
    internal static string? SmokeDownloadsFolder;

    /// <summary>الملف اللي القارئ بيفتحه: الكاش للروابط، والمسار على الجهاز للملفات.</summary>
    private static string BookFile(Book b) => b.IsOnline ? BookSources.CachePathFor(b.Path) : b.LocalPath;

    // =====================================================================
    //  إضافة من رابط
    // =====================================================================

    private void AddBookLinksDialog(Shelf? shelf)
    {
        if (!EnsureLibrary()) return;
        string initial = "";
        try { if (Clipboard.ContainsText() && BookSources.Parse(Clipboard.GetText().Trim()) != null) initial = Clipboard.GetText().Trim(); } catch { }
        string? text = AskText("كتاب من رابط",
            "رابط الكتاب: Google Drive، أو Google Docs، أو Dropbox، أو OneDrive، أو رابط PDF مباشر (ممكن كذا رابط بمسافة بينهم):", initial);
        if (text == null) return;
        var urls = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(t => BookSources.Parse(t) != null).ToList();
        if (urls.Count == 0) { Dialogs.Info(this, "مالقيتش رابط في اللي كتبته."); return; }
        var added = AddBookLinks(urls, shelf);
        if (added.Count == 0) { ShowStatus("الروابط دي موجودة على الرف أصلًا", 4); return; }
        _ = FetchBooksAsync(added, openFirst: added.Count == 1);
    }

    internal List<Book> AddBookLinks(IEnumerable<string> urls, Shelf? shelf)
    {
        var links = urls.Select(BookSources.Parse).OfType<BookSources.Link>()
            .Where(l => !_library.Books.Any(b => string.Equals(b.Path, l.Original, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(l => l.Original, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        var added = new List<Book>();
        if (links.Count == 0) return added;
        Checkpoint(links.Count == 1 ? "كتاب من رابط" : "كتب من روابط");
        foreach (var l in links)
        {
            var b = new Book { Title = BookSources.TitleFor(l, null), Path = l.Original, LocalPath = l.Original, ShelfId = shelf?.Id };
            b.TouchInfo();
            _library.Books.Add(b);
            added.Add(b);
        }
        if (shelf != null) shelf.Expanded = true;
        MarkDirty();
        if (!_shelfMode) ShowShelf(true);
        RebuildShelf();
        SelectShelfItem(added[0]);
        return added;
    }

    private async Task FetchBooksAsync(List<Book> books, bool openFirst)
    {
        for (int i = 0; i < books.Count; i++)
            if (!await FetchBookAsync(books[i], open: openFirst && i == 0) && books.Count > 1) break;
    }

    /// <summary>الاسم لسه اللي اتعمل من الرابط (فاسم الملف الحقيقي أحسن منه).</summary>
    private static bool IsDefaultTitle(Book b) => BookSources.Parse(b.Path) is { } l && b.Title == BookSources.TitleFor(l, null);

    /// <summary>رابط التنزيل بصلاحيتك لو محتاج (ملفات Moodle بالتوكن بتاع حسابك على الموقع).</summary>
    private string AuthorizedUrl(string url)
    {
        if (!url.Contains("/pluginfile.php/", StringComparison.OrdinalIgnoreCase)) return url;
        var account = _settings.MoodleAccounts.FirstOrDefault(a => a.Site.Length > 0 && url.StartsWith(a.Site, StringComparison.OrdinalIgnoreCase));
        if (account == null) return url;
        string token;
        try { token = account.Token; } catch { return url; }
        if (string.IsNullOrEmpty(token)) return url;
        string u = url.Contains("/webservice/pluginfile.php/", StringComparison.OrdinalIgnoreCase) ? url : url.Replace("/pluginfile.php/", "/webservice/pluginfile.php/", StringComparison.OrdinalIgnoreCase);
        return u + (u.Contains('?') ? "&" : "?") + "token=" + Uri.EscapeDataString(token);
    }

    /// <summary>بينزّل الكتاب في الكاش (لو open: يفتحه بعدها). false لو مانزلش.</summary>
    internal async Task<bool> FetchBookAsync(Book b, bool open)
    {
        if (BookSources.Parse(b.Path) is not { } link) return false;
        string target = BookSources.CachePathFor(b.Path);
        await _bookDownloadGate.WaitAsync();
        var cts = _bookDlCts = new CancellationTokenSource();
        try
        {
            ShowBanner($"بينزّل «{b.Title}» من {BookSources.KindName(link.Kind)}…", BannerKind.Info, "إلغاء", () => cts.Cancel(), seconds: 0, key: "book-dl");
            long lastUi = 0;
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                if (Environment.TickCount64 - lastUi < 300 && p.Done != p.Total) return;
                lastUi = Environment.TickCount64;
                string amount = p.Total > 0 ? $"{p.Done * 100 / p.Total}%" : $"{p.Done / (1024 * 1024.0):0.0} MB";
                UpdateBannerText("book-dl", $"بينزّل «{b.Title}»… {amount}");
            });
            var d = await BookSources.DownloadAsync(AuthorizedUrl(b.Path), target, progress, cts.Token);
            BookSources.WriteStamp(target, d.Stamp);
            FileCache.Forget(target);
            if (d.FileName != null && IsDefaultTitle(b))
            {
                b.Title = BookFiles.TitleFromFile(d.FileName);
                b.TouchInfo();
                MarkDirty();
            }
            HideBanner("book-dl");
            Log.Info($"book downloaded: {link.Kind} {d.Bytes / 1024} KB");
            AfterBookFetched(b, open);
            return true;
        }
        catch (OperationCanceledException) { HideBanner("book-dl"); ShowStatus("اتلغى التنزيل", 3); }
        catch (BookSources.NotPdfException ex) when (ex.NeedsSignIn)
        {
            Log.Info("book download needs sign-in: " + ex.Message);
            ShowBanner(ex.Message, BannerKind.Warning, "هاته بحسابك", () => FetchWithAccount(b, open), seconds: 0, key: "book-dl");
        }
        catch (Exception ex)
        {
            Log.Info("book download failed: " + ex.Message);
            ShowBanner("مقدرتش أنزّل الكتاب: " + ex.Message, BannerKind.Warning, "حاول تاني", () => _ = FetchBookAsync(b, open), seconds: 0, key: "book-dl");
        }
        finally
        {
            cts.Dispose();
            if (_bookDlCts == cts) _bookDlCts = null;
            _bookDownloadGate.Release();
        }
        return false;
    }

    private void AfterBookFetched(Book b, bool open)
    {
        if (!_library.Books.Contains(b)) return;
        if (open) OpenBook(b);
        else QueueProbe(new[] { b });
        if (_shelfMode) RefreshBookRowsOrTree(b);
    }

    private void UpdateBannerText(string key, string text)
    {
        if (_bannerKey == key && _banner != null && _banner.Visible) _bannerText.Text = text;
    }

    /// <summary>الملف خاص: نافذة جوه البرنامج تسجّل فيها دخول، وأول ما يتنزل بيتلقط.</summary>
    private void FetchWithAccount(Book b, bool open)
    {
        if (BookSources.Parse(b.Path) is not { } link) return;
        string target = BookSources.CachePathFor(b.Path);
        // درايف/Docs/Dropbox: رابط التنزيل نفسه (بعد الدخول بيبدأ لوحده)؛ OneDrive والروابط التانية: صفحة المشاركة
        string start = link.Kind is BookSources.Kind.GoogleDrive or BookSources.Kind.GoogleDocs or BookSources.Kind.Dropbox ? link.Download : link.Original;
        using var f = new BookFetchForm(b.Title, start, target);
        var r = f.ShowDialog(this);
        if (r == DialogResult.OK && f.Result != null)
        {
            BookSources.WriteStamp(target, "manual");
            FileCache.Forget(target);
            HideBanner("book-dl");
            AfterBookFetched(b, open);
        }
        else if (f.UseBrowser) CaptureFromBrowser(b, open);
    }

    /// <summary>بيفتح الرابط في متصفحك (اللي إنت داخل فيه بحسابك) ويستنى الملف في فولدر التنزيلات.</summary>
    internal void CaptureFromBrowser(Book b, bool open)
    {
        StopDownloadsWatch();
        string target = BookSources.CachePathFor(b.Path);
        var w = new DownloadsWatcher(SmokeDownloadsFolder);
        _downloadsWatcher = w;
        w.Found += file =>
        {
            if (_downloadsWatcher != w) return;
            StopDownloadsWatch();
            try
            {
                Directory.CreateDirectory(BookSources.CacheDir);
                target = Path.ChangeExtension(target, Path.GetExtension(file).ToLowerInvariant() == ".epub" ? ".epub" : ".pdf");
                File.Copy(file, target, overwrite: true);
                BookSources.WriteStamp(target, "manual");
                FileCache.Forget(target);
                if (IsDefaultTitle(b)) { b.Title = BookFiles.TitleFromFile(file); b.TouchInfo(); MarkDirty(); }
                HideBanner("book-dl");
                ShowStatus($"لقطت «{Path.GetFileName(file)}» ✓", 4);
                AfterBookFetched(b, open);
            }
            catch (Exception ex) { ShowBanner("مقدرتش آخد الملف: " + ex.Message, BannerKind.Warning, seconds: 10, key: "book-dl"); }
        };
        if (SmokeDownloadsFolder == null && BookSources.Parse(b.Path) is { } link) OpenInBrowser(link.Original);
        ShowBanner("نزّل الكتاب من متصفحك (بحسابك) — «مسار» مستني الملف في فولدر التنزيلات وهيفتحه لوحده",
            BannerKind.Info, "إلغاء", StopDownloadsWatch, seconds: 0, key: "book-dl");
        _downloadsWatchTimeout = new System.Windows.Forms.Timer { Interval = 15 * 60 * 1000 };
        _downloadsWatchTimeout.Tick += (_, _) => { StopDownloadsWatch(); HideBanner("book-dl"); };
        _downloadsWatchTimeout.Start();
    }

    private void StopDownloadsWatch()
    {
        _downloadsWatcher?.Dispose();
        _downloadsWatcher = null;
        _downloadsWatchTimeout?.Dispose();
        _downloadsWatchTimeout = null;
        if (_bannerKey == "book-dl") HideBanner("book-dl");
    }

    /// <summary>بعد ما الكتاب يتفتح من الكاش: هل اتحدّث على المصدر؟</summary>
    private async Task CheckBookUpdateAsync(Book b)
    {
        string cache = BookSources.CachePathFor(b.Path);
        string? stamp = BookSources.ReadStamp(cache);
        if (stamp is null or "manual") return;
        bool? changed;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            changed = await BookSources.ChangedAsync(AuthorizedUrl(b.Path), stamp, cts.Token);
        if (changed != true || IsDisposed || !_library.Books.Contains(b)) return;
        ShowBanner($"فيه نسخة أحدث من «{b.Title}» على المصدر", BannerKind.Info, "حدّثه", () => _ = RefreshOnlineBookAsync(b), seconds: 0, key: "book-dl");
    }

    internal async Task RefreshOnlineBookAsync(Book b)
    {
        bool reopen = ReaderShown && _currentBook == b;
        if (reopen) CloseReader(showHome: false);
        if (await FetchBookAsync(b, open: false) && reopen) OpenBook(b);
        else if (reopen) OpenBook(b);
    }

    // =====================================================================
    //  ملفات PDF من الكورسات
    // =====================================================================

    /// <summary>ملف PDF من كورس (مرفق أو مورد Moodle): بيتفتح في القارئ ويتحفظ في رف «ملفات الكورسات».</summary>
    internal void OpenCoursePdf(string pathOrUrl, string title)
    {
        if (!EnsureLibrary()) return;
        bool online = MediaFiles.IsUrl(pathOrUrl);
        var b = _library.Books.FirstOrDefault(x => string.Equals(x.Path, pathOrUrl, StringComparison.OrdinalIgnoreCase)
                                                || !online && !x.IsOnline && string.Equals(x.LocalPath, pathOrUrl, StringComparison.OrdinalIgnoreCase));
        if (b == null)
        {
            Checkpoint("ملف من الكورس");
            var shelf = _library.Shelves.FirstOrDefault(s => s.Name == CourseFilesShelfName && s.ParentId == null);
            if (shelf == null)
            {
                shelf = new Shelf { Name = CourseFilesShelfName, IconKey = "document_text" };
                shelf.TouchInfo();
                _library.Shelves.Add(shelf);
            }
            b = new Book { Title = string.IsNullOrWhiteSpace(title) ? BookFiles.TitleFromFile(pathOrUrl) : title, Path = pathOrUrl, LocalPath = pathOrUrl, ShelfId = shelf.Id };
            b.TouchInfo();
            _library.Books.Add(b);
            MarkDirty();
            RebuildShelf();
        }
        OpenBook(b);
    }

    /// <summary>المرفق ده يتفتح في القارئ؟ (ملف PDF، أو رابط لملف PDF)</summary>
    private static bool IsReadableAttachment(Attachment a, string target) =>
        ReaderPane.Available && (a.IsLink
            ? MediaFiles.IsUrl(target) && Uri.TryCreate(target, UriKind.Absolute, out var u) && u.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            : BookFiles.IsBookFile(target) && File.Exists(target));
}
