using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// العروض التقديمية (pptx/ppsx/ppt/pptm) على الرف زي الكتب: الشرايح = فصول، الشريحة اللي اتشافت = صفحة اتقرت،
/// بتتعرض بـ PowerPoint نفسه جوه «مسار» (بالأنيميشن)، وتتحول لفيديو بعلامة لكل شريحة.
/// </summary>
internal partial class MainForm
{
    private PresentationPane? _presPane;
    private Book? _presBook;
    private DateTime _presSlideSince = DateTime.UtcNow, _presStartUtc;
    private int _presViewed;

    private bool PresentationShown => _presPane?.Running == true && _presBook != null;

    private PresentationPane EnsurePresentationPane()
    {
        if (_presPane != null) return _presPane;
        var p = new PresentationPane { Dock = DockStyle.Fill };
        _videoArea.Controls.Add(p);
        p.SlideChanged += OnPresentationSlide;
        p.Ended += () => BeginInvoke(() => EndPresentation(showHome: true));
        p.Command += c =>
        {
            if (c == "end") CloseActiveTab();
            else if (c == "video" && _presBook != null) ConvertPresentationToVideo(_presBook);
        };
        _presPane = p;
        return p;
    }

    /// <summary>بيقرا الشرايح (العناوين والنص والملاحظات) من غير PowerPoint — للرف والبحث والذكاء.</summary>
    internal bool ApplyPresentationInfo(Book b)
    {
        string file = BookFile(b);
        if (!Pptx.IsOpenXml(file) || !File.Exists(file)) return false;
        try
        {
            var slides = Pptx.Read(file);
            if (slides.Count == 0) return false;
            b.PageCount = slides.Count;
            b.Chapters = slides.Select(s => new BookChapter { Page = s.Index, Title = (s.Hidden ? "(مخفية) " : "") + (s.Title.Length > 0 ? s.Title : "شريحة " + s.Index) }).ToList();
            var text = new BookText { PageCount = slides.Count, Complete = true };
            foreach (var s in slides) text.Pages[s.Index] = string.Join("\n", new[] { s.Title, s.Text, s.Notes.Length > 0 ? "ملاحظات: " + s.Notes : "" }.Where(x => x.Length > 0));
            text.Save(b);
            b.TouchInfo();
            MarkDirty();
            return true;
        }
        catch (Exception ex) { Log.Info("pptx read: " + ex.Message); return false; }
    }

    /// <summary>
    /// «📽 عرض PowerPoint…»: تختار ملف (أو أكتر) وبيتحط على الرف — في الرف اللي واقف عليه، أو رف «عروضي».
    /// لو ملف واحد بيتفتح على طول.
    /// </summary>
    internal void AddPresentationsDialog(Shelf? shelf = null)
    {
        if (!EnsureLibrary()) return;
        using var dlg = new OpenFileDialog
        {
            Title = "اختر عرض PowerPoint" + (shelf != null ? $" — سيوضع في «{shelf.Name}»" : ""),
            Filter = "عروض PowerPoint|*.pptx;*.ppsx;*.pptm;*.ppt;*.pps|كل الملفات|*.*", Multiselect = true, RestoreDirectory = true
        };
        var files = SmokePresentationFiles ?? (dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileNames : null);
        if (files == null) return;
        var picked = files.Where(Pptx.IsPresentation).ToList();
        if (picked.Count == 0) { ShowStatus("هذا ليس ملف PowerPoint", 4); return; }
        shelf ??= PresentationsShelf();
        var added = AddBooks(picked, shelf);
        if (!_shelfMode || _split.Panel2Collapsed) ToggleShelf();
        RebuildShelf();
        // ملف واحد (جديد أو كان موجود): نفتحه على طول — ده غالبًا اللي إنت عايزه
        var target = added.FirstOrDefault() ?? _library.Books.FirstOrDefault(b => string.Equals(Path.GetFullPath(b.Path), Path.GetFullPath(picked[0]), StringComparison.OrdinalIgnoreCase));
        if (picked.Count == 1 && target != null) { SelectShelfItem(target); OpenBook(target); }
        else AnnounceAdded(added.Count, picked.Count);
    }
    internal string[]? SmokePresentationFiles;

    private Shelf PresentationsShelf()
    {
        var s = _library.Shelves.FirstOrDefault(x => x.Kind == "presentations");
        if (s != null) return s;
        s = AddShelf("عروضي", null);
        s.Kind = "presentations";
        MarkDirty();
        return s;
    }

    internal void OpenPresentation(Book b, int? slide = null)
    {
        string file = BookFile(b);
        if (!File.Exists(file)) { ShowStatus("ملف العرض غير موجود", 5); return; }
        if (PowerPointShow.InstalledVersion() == null)
        {
            ShowBanner("عرض الشرائح بالحركات داخل «مسار» يحتاج إلى Microsoft PowerPoint (2016 أو أحدث / 365) — الشرائح والنص ظاهرة في الرف", BannerKind.Warning,
                "افتحه بالبرنامج المعتاد", () => OpenExternally(b), seconds: 0, key: "pptx");
            return;
        }
        if (PresentationShown && _presBook == b && slide == null) { _presPane!.Resume(); return; }   // مفتوح أصلًا
        // نفس العرض كان متشال على جنب (فتحت سبورة/كتاب): يرجع فورًا من نفس الشريحة، من غير ما PowerPoint يفتح من الأول
        if (_presSuspended == b && _presPane is { Running: true } sp && slide == null)
        {
            if (_currentVideo != null || _currentLive != null || UdemyActive) LeaveVideoForPage();
            if (ReaderShown) CloseReader(showHome: false);
            _presSuspended = null;
            _presBook = b;
            _welcome.Visible = false; _videoView.Visible = false; ControlChromeVisible = false;
            sp.Resume();
            _presSlideSince = DateTime.UtcNow;
            b.LastOpenedUtc = DateTime.UtcNow;
            UpdateTitle();
            return;
        }
        FinishSuspendedPresentation();
        if (PresentationShown) EndPresentation(showHome: false);
        if (_currentVideo != null || _currentLive != null || UdemyActive) LeaveVideoForPage();
        if (ReaderShown) CloseReader(showHome: false);
        if (_fullscreen && _miniMode) ToggleMiniMode();
        var pane = EnsurePresentationPane();
        _welcome.Visible = false;
        _videoView.Visible = false;
        ControlChromeVisible = false;
        int from = Math.Max(1, slide ?? b.LastPage);
        ShowStatus($"جارٍ فتح «{b.Title}»…", 10);
        var titles = b.Chapters.OrderBy(c => c.Page).Select(c => c.Title).ToList();
        _presBook = b; // قبل التشغيل: أول شريحة بتتبلّغ وهو بيبدأ
        _presSlideSince = DateTime.UtcNow;
        string? error = pane.Start(file, from, useTimings: false, titles);
        if (error != null)
        {
            _presBook = null;
            _videoView.Visible = true; ControlChromeVisible = true; _welcome.Visible = true;
            ShowBanner("تعذّر عرض الملف: " + error, BannerKind.Warning, "افتحه بالبرنامج المعتاد", () => OpenExternally(b), seconds: 0, key: "pptx");
            return;
        }
        _presStartUtc = DateTime.UtcNow;
        _presViewed = 0;
        if (HasLibrary) pane.SetInkPages(SlideInk.Load(_libraryPath, b.Id));
        b.LastOpenedUtc = DateTime.UtcNow;
        _library.LastBookId = b.Id;
        if (pane.Total > 0 && b.PageCount != pane.Total) { b.PageCount = pane.Total; b.TouchInfo(); }
        MarkPositionDirty();
        UpdateTitle();
        if (!pane.ImagesMode) HideBanner("pptx");
        if (_shelfMode) RefreshBookRows(b);
        if (pane.ImagesMode)
            ShowBanner($"«{b.Title}» معروض داخل «مسار» كشرائح ثابتة (دون الحركات) — لم يقبل PowerPoint على هذا الجهاز أن يُضمَّن داخل النافذة. الأسهم/المسافة للشرائح، وEsc للإنهاء.", BannerKind.Info, seconds: 12, key: "pptx");
        else ShowStatus($"«{b.Title}» — المسافة/الأسهم للشرائح، وEsc للإنهاء", 6);
    }

    /// <summary>شريحة جديدة: اللي قبلها تتحسب «اتشافت» لو قعدت قدامها وقت القراية.</summary>
    private void OnPresentationSlide(int n)
    {
        if (_presBook is not { } b) return;
        MarkPresentationViewed(b);
        if (Recording && b.LastPage != n) RecordingMark($"📽 {b.Title} — شريحة {n}");
        b.LastPage = n;
        b.TouchProgress();
        _presSlideSince = DateTime.UtcNow;
        MarkPositionDirty();
        if (_shelfMode) RefreshBookRows(b);
    }

    private void MarkPresentationViewed(Book b)
    {
        int cur = b.LastPage;
        if (cur <= 0 || DateTime.UtcNow - _presSlideSince < TimeSpan.FromSeconds(Math.Max(1, _settings.ReadSeconds))) return;
        var set = b.Read;
        if (set.Add(cur)) { b.ReadPages = set.ToString(); _presViewed++; b.TouchProgress(); }
    }

    /// <summary>كل ثانية والعرض شغال: الشريحة اللي قدامك تتحسب لما يعدّي وقت القراية.</summary>
    private void PresentationTick()
    {
        if (_presSuspended != null && DateTime.UtcNow - _presSuspendedUtc > TimeSpan.FromMinutes(20)) FinishSuspendedPresentation();
        if (!PresentationShown || _presBook is not { } b) return;
        if (DateTime.UtcNow - _presSlideSince >= TimeSpan.FromSeconds(Math.Max(1, _settings.ReadSeconds)) && !b.Read.Contains(b.LastPage))
        {
            MarkPresentationViewed(b);
            if (_shelfMode) RefreshBookRows(b);
        }
    }

    private Book? _presSuspended;
    private DateTime _presSuspendedUtc;

    /// <summary>
    /// فتحت حاجة تانية (سبورة، كتاب، فيديو) والعرض شغال: العرض بيستخبى بس (PowerPoint يفضل فاتح على نفس الشريحة)
    /// عشان لما ترجعله يظهر فورًا. بيتقفل بجد لو فتحت عرض تاني، أو عدّى عليه 20 دقيقة مستخبي.
    /// </summary>
    private void SuspendPresentation()
    {
        if (!PresentationShown || _presPane is not { } pane || _presBook is not { } b) return;
        MarkPresentationViewed(b);
        SaveSlideInk(pane, b);
        _presSuspended = b;
        _presSuspendedUtc = DateTime.UtcNow;
        _presBook = null;
        pane.Suspend();
        _videoView.Visible = true;
        if (!_fullscreen) ControlChromeVisible = true;
        if (_shelfMode) RefreshBookRows(b);
        MarkPositionDirty();
    }

    /// <summary>رسم «أقلام وأشكال» على الشرائح يُحفظ مع المكتبة (يعود عند الفتح ويدخل في التصدير).</summary>
    private void SaveSlideInk(PresentationPane pane, Book b)
    {
        if (!HasLibrary) return;
        var pages = pane.InkPages();
        if (pages.Count == 0 && !File.Exists(SlideInk.FileFor(_libraryPath, b.Id))) return;
        SlideInk.Save(_libraryPath, b.Id, pages, pane.InkAspect);
    }

    /// <summary>العرض المستخبي يتقفل بجد (نسخة مشروحة لو كتبت، وسجل المذاكرة).</summary>
    private void FinishSuspendedPresentation()
    {
        if (_presSuspended is not { } b) return;
        _presSuspended = null;
        _presBook = b;
        EndPresentation(showHome: false, restoreView: false);
    }

    /// <summary>قفل العرض: لو PowerPoint احتفظ بالكتابة (اخترت «احتفظ») بتتحفظ نسخة مشروحة في «شروحاتي».</summary>
    internal void EndPresentation(bool showHome) => EndPresentation(showHome, restoreView: true);

    private void EndPresentation(bool showHome, bool restoreView)
    {
        var pane = _presPane; var b = _presBook;
        if (pane == null || b == null) { pane?.Stop(); return; }
        _presBook = null;
        SaveSlideInk(pane, b);
        try
        {
            MarkPresentationViewed(b);
            var show = pane.Show;
            show?.StopShow(); // PowerPoint بيسأل «تحتفظ بالكتابة؟» لو كتبت
            int ink = show?.InkSlides() ?? 0;
            if (ink > 0 && show != null && HasLibrary) SaveAnnotatedPresentation(b, show, ink);
        }
        catch (Exception ex) { Log.Info("end presentation: " + ex.Message); }
        pane.Stop();
        if (_presViewed > 0)
            LogJournal(new JournalEntry
            {
                Kind = JournalEntry.Pages, StartUtc = _presStartUtc, EndUtc = DateTime.UtcNow,
                CourseId = "book:" + b.Id, VideoPath = b.Path, Score = _presViewed, Total = b.PageCount
            });
        if (restoreView)
        {
            _videoView.Visible = true;
            if (!_fullscreen) ControlChromeVisible = true;
        }
        if (showHome) { _welcome.Visible = true; RefreshHome(); }
        if (_shelfMode) RefreshBookRows(b);
        MarkPositionDirty();
        UpdateTitle();
    }

    private void SaveAnnotatedPresentation(Book b, PowerPointShow show, int inkSlides)
    {
        try
        {
            var copy = AnnotatedCopyOf(b);
            string safe = new string(b.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
            string target = copy?.LocalPath ?? Path.Combine(AnnotatedDir, (safe.Length > 0 ? safe : "عرض") + " — مشروح" + (Path.GetExtension(BookFile(b)).ToLowerInvariant() == ".ppt" ? ".ppt" : ".pptx"));
            Directory.CreateDirectory(AnnotatedDir);
            show.SaveCopyAs(target);
            if (copy == null)
            {
                copy = new Book { Title = b.Title + " — مشروح", Path = target, LocalPath = target, ShelfId = AnnotatedShelf().Id, AnnotatedOf = b.Id, IconKey = "pen", PageCount = b.PageCount, Chapters = b.Chapters.ToList() };
                copy.TouchInfo();
                _library.Books.Add(copy);
                MarkDirty();
                RebuildShelf();
            }
            ShowBanner($"🖊 حُفظت الكتابة الموجودة على {inkSlides} شريحة في نسخة مشروحة في «{AnnotatedFolderName}» — الأصل كما هو", BannerKind.Success,
                "افتحها", () => OpenPresentation(copy), seconds: 12);
        }
        catch (Exception ex) { Log.Error("annotated pptx", ex); ShowStatus("تعذّر حفظ نسخة بالكتابة: " + ex.Message, 6); }
    }

    // ---------------- تحويل لفيديو ----------------

    /// <summary>للفحص: الاختيارات من غير شباك.</summary>
    internal (int Height, int Seconds, bool Timings)? SmokeVideoChoice;
    private bool _videoBusy;

    internal async void ConvertPresentationToVideo(Book b)
    {
        if (_videoBusy) { ShowStatus("يوجد فيديو قيد الإنشاء الآن…", 4); return; }
        if (PowerPointShow.InstalledVersion() == null) { ShowStatus("تحويل العرض إلى فيديو يحتاج إلى PowerPoint", 5); return; }
        int height = 1080, seconds = 5; bool timings = true;
        if (SmokeVideoChoice is { } sc) (height, seconds, timings) = sc;
        else
        {
            using var kit = new FormKit("حوّل العرض إلى فيديو — " + b.Title, 540);
            var q = kit.Row("الجودة", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
            q.Items.AddRange(new object[] { "1080p (أوضح)", "720p (أصغر)", "480p (أصغر جدًا)" });
            q.SelectedIndex = 0;
            var sec = kit.Row("ثوانٍ لكل شريحة", new NumberBox { Minimum = 1, Maximum = 120, Value = 5 });
            var tim = kit.Row("التوقيت", new ToggleSwitch { Text = "استخدم التوقيت والتسجيل الصوتي الموجودين في العرض (إن وجدا)", Checked = true });
            kit.Note("ينشئ PowerPoint الفيديو بالحركات في الخلفية — ويُضاف إلى دورة «العروض التقديمية» بعلامة لكل شريحة.", 40, Theme.Muted);
            kit.Buttons("أنشئ الفيديو");
            if (kit.Show(this) != DialogResult.OK) return;
            height = q.SelectedIndex switch { 1 => 720, 2 => 480, _ => 1080 };
            seconds = (int)sec.Value; timings = tim.Checked;
        }
        string dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_libraryPath)) ?? ".", "فيديوهات العروض");
        Directory.CreateDirectory(dir);
        string safe = new string(b.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        string mp4 = Path.Combine(dir, (safe.Length > 0 ? safe : "عرض") + ".mp4");
        for (int i = 2; File.Exists(mp4); i++) mp4 = Path.Combine(dir, $"{safe} ({i}).mp4");

        _videoBusy = true;
        var own = _presPane?.Show == null || _presBook != b ? new PowerPointShow() : null;
        var show = own ?? _presPane!.Show!;
        try
        {
            if (own != null) own.Open(BookFile(b));
            var durations = show.SlideSeconds(timings, seconds);
            show.CreateVideo(mp4, timings, seconds, height, 30, 85);
            var started = DateTime.UtcNow;
            int status;
            while (true)
            {
                await Task.Delay(1000);
                status = show.VideoStatus;
                if (status is PowerPointShow.VideoDone or PowerPointShow.VideoFailed) break;
                if (status == PowerPointShow.VideoNone && DateTime.UtcNow - started > TimeSpan.FromSeconds(10)) break; // ماابتداش أصلًا
                ShowStatus($"🎬 PowerPoint ينشئ الفيديو… {(int)(DateTime.UtcNow - started).TotalSeconds} ث", 5);
                if (DateTime.UtcNow - started > TimeSpan.FromHours(2)) { status = PowerPointShow.VideoFailed; break; }
            }
            if (status != PowerPointShow.VideoDone || !File.Exists(mp4)) throw new Exception("لم يستطع PowerPoint إكمال الفيديو");
            AddPresentationVideo(b, mp4, durations);
        }
        catch (Exception ex)
        {
            Log.Error("pptx video", ex);
            ShowBanner("تعذّر إنشاء الفيديو: " + ex.Message, BannerKind.Warning, seconds: 10);
        }
        finally
        {
            own?.Dispose();
            _videoBusy = false;
        }
    }

    /// <summary>الفيديو بيتضاف لكورس «العروض التقديمية» بمحاضرة باسم العرض وعلامة عند كل شريحة.</summary>
    internal LectureGroup AddPresentationVideo(Book b, string mp4, double[] slideSeconds)
    {
        const string courseName = "العروض التقديمية";
        Checkpoint("فيديو من عرض");
        var course = _library.Courses.FirstOrDefault(c => c.Name == courseName);
        if (course == null) { course = new Course { Name = courseName }; _library.Courses.Add(course); }
        var g = course.Groups.FirstOrDefault(x => x.Name == b.Title) ?? new LectureGroup { Name = b.Title };
        if (!course.Groups.Contains(g)) course.Groups.Add(g);
        var v = VideoItem.FromFile(mp4);
        var titles = b.Chapters.OrderBy(c => c.Page).Select(c => c.Title).ToList();
        double t = 0;
        for (int i = 0; i < slideSeconds.Length; i++)
        {
            if (slideSeconds[i] <= 0) continue; // شريحة مخفية
            v.Bookmarks.Add(new Bookmark { TimeMs = (long)(t * 1000), Note = i < titles.Count ? titles[i] : $"شريحة {i + 1}" });
            t += slideSeconds[i];
        }
        g.Videos.Add(v);
        course.Expanded = true; g.Expanded = true;
        MarkDirty();
        RebuildTree();
        QueueProbe(new[] { v });
        ShowBanner($"🎬 أُنشئ فيديو «{b.Title}» ({v.Bookmarks.Count} علامة) في دورة «{courseName}»", BannerKind.Success, "شغّله", () => PlayVideo(g, v), seconds: 15);
        return g;
    }
}
