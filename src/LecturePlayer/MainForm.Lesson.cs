using System.Globalization;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// «📘 شرح حصة»: حزمة جاهزة من الأدوات الموجودة — سبورة باسم الحصة، ودفتر (مقطع جديد في دفتر المجموعة،
/// أو دفتر جديد للحصة)، وكتب وعروض وفيديوهات وروابط يوتيوب وتسجيلات الشاشة.
/// كل ما يُفتح أو يُضاف أثناء الحصة يدخل «محتويات الحصة» ويبقى فيها حتى لو أُغلق تبويبه، ويُصدَّر كله في النهاية.
/// </summary>
internal partial class MainForm
{
    private const string LessonsShelfKind = "lessons";
    private const string LessonsName = "حصصي";
    private ToolStripMenuItem _lessonMenu = null!, _lessonQuick = null!;
    /// <summary>عناصر «الحصة المفتوحة» في أول القائمة (تتبني كل ما القائمة تتفتح).</summary>
    private readonly List<ToolStripItem> _lessonMenuLive = new();

    /// <summary>للفحص: اختيارات «شرح حصة» من غير شباك (الاسم، المجموعة، مقاس وخلفية السبورة).</summary>
    internal (string Name, string? ClassId, int Size, int Bg)? SmokeLessonChoice;
    /// <summary>للفحص: الملفات اللي «أضف إلى الحصة» تختارها من غير شباك.</summary>
    internal string[]? SmokeLessonFiles;

    private Shelf LessonsShelf()
    {
        var s = _library.Shelves.FirstOrDefault(x => x.Kind == LessonsShelfKind);
        if (s != null) return s;
        s = AddShelf(LessonsName, null, checkpoint: false);
        s.Kind = LessonsShelfKind;
        s.IconKey = "whiteboard";
        s.TouchInfo();
        return s;
    }

    /// <summary>محاضرة الحصة في دورة «حصصي» (فيها فيديوهاتها وروابطها وتسجيلاتها).</summary>
    private LectureGroup? LessonGroup(StudySession s, bool create)
    {
        if (s.GroupId != null && _library.AllGroups.FirstOrDefault(g => g.Id == s.GroupId) is { } found) return found;
        if (!create) return null;
        var course = _library.Courses.FirstOrDefault(c => c.Name == LessonsName);
        if (course == null) { course = new Course { Name = LessonsName }; _library.Courses.Add(course); }
        var g = new LectureGroup { Name = s.Name };
        course.Groups.Add(g);
        course.Expanded = true;
        s.GroupId = g.Id;
        s.UpdatedUtc = DateTime.UtcNow;
        MarkDirty();
        RebuildTree();
        return g;
    }

    private static WorkTab BookTab(Book b) => new() { Kind = WorkTab.BookKind, Ref = b.Id, Title = b.Title, Page = Math.Max(1, b.LastPage) };

    /// <summary>يضيف عنصرًا لمحتويات الحصة (لو مش موجود) — يرجّع true لو اتضاف.</summary>
    private bool AddLessonItem(StudySession s, WorkTab t)
    {
        s.Items ??= new();
        if (s.Items.Any(x => x.SameItem(t))) return false;
        s.Items.Add(t.Copy());
        s.UpdatedUtc = DateTime.UtcNow;
        MarkPositionDirty();
        return true;
    }

    /// <summary>كل تبويب مفتوح في الحصة يُسجَّل في محتوياتها (بموضعه) — ولا يُشال منها لو اتقفل.</summary>
    private bool MirrorLessonItems(StudySession s, List<WorkTab> tabs)
    {
        if (!s.Lesson) return false;
        s.Items ??= new();
        var lg = LessonGroup(s, create: false);
        bool changed = false;
        foreach (var t in tabs)
        {
            // فيديوهات محاضرة الحصة نفسها جزء منها أصلًا
            if (t.Kind == WorkTab.CourseKind && (t.ItemPath == null || lg?.Videos.Any(v => string.Equals(v.Path, t.ItemPath, StringComparison.OrdinalIgnoreCase)) == true)) continue;
            var hit = s.Items.FirstOrDefault(x => x.SameItem(t));
            if (hit == null) { s.Items.Add(t.Copy()); changed = true; continue; }
            if (hit.Page != t.Page || hit.PositionMs != t.PositionMs || hit.Title != t.Title || hit.GroupId != t.GroupId)
            {
                hit.Page = t.Page; hit.PositionMs = t.PositionMs; hit.Title = t.Title; hit.GroupId = t.GroupId;
                changed = true;
            }
        }
        return changed;
    }

    // ---------------- إنشاء الحصة ----------------

    /// <summary>المجموعة اللي موعدها الآن (أو مجموعة الحصة المفتوحة).</summary>
    private ClassGroup? ClassNow()
    {
        var now = DateTime.Now;
        foreach (var (c, slot) in TodaysClasses(now))
        {
            var start = now.Date + slot.StartTime;
            if (now >= start.AddMinutes(-10) && now <= start.AddMinutes(slot.Minutes)) return c;
        }
        return ActiveSession?.ClassId is { } id ? _library.Classes.FirstOrDefault(c => c.Id == id) : null;
    }

    private string DefaultLessonName(ClassGroup? c) => c != null
        ? $"{c.Name} — حصة {_library.Sessions.Count(s => s.ClassId == c.Id) + 1}"
        : "حصة — " + DateTime.Now.ToString("d MMMM", new CultureInfo("ar-EG"));

    /// <summary>«📘 شرح حصة»: اسم الحصة (ومجموعتها وسبورتها) — وبعدها تبدأ على السبورة.</summary>
    internal void NewLessonDialog()
    {
        if (!EnsureLibrary()) return;
        var now = ClassNow();
        string name = DefaultLessonName(now);
        string? classId = now?.Id;
        int size = 0, bg = 0;
        bool addBook = false, addPres = false;
        if (SmokeLessonChoice is { } sc) (name, classId, size, bg) = sc;
        else
        {
            using var kit = new FormKit("📘 شرح حصة جديدة", 560);
            kit.Header("الحصة");
            var nameBox = kit.Row("اسم الحصة", FormKit.Style(new TextBox { Text = name, RightToLeft = RightToLeft.Yes }));
            ComboBox? clsBox = null;
            var classes = _library.Classes.ToList();
            if (classes.Count > 0)
            {
                clsBox = kit.Row("المجموعة", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
                clsBox.Items.Add("بدون مجموعة");
                foreach (var c in classes) clsBox.Items.Add(c.Name);
                clsBox.SelectedIndex = now != null ? classes.IndexOf(now) + 1 : 0;
                string auto = name;
                clsBox.SelectedIndexChanged += (_, _) =>
                {
                    // الاسم المقترح يتبع المجموعة (إلا لو المعلم كتب اسمًا بنفسه)
                    if (nameBox.Text.Trim() != auto) return;
                    auto = DefaultLessonName(clsBox.SelectedIndex > 0 ? classes[clsBox.SelectedIndex - 1] : null);
                    nameBox.Text = auto;
                };
                kit.Note("مع مجموعة: مقطع جديد باسم الحصة في دفتر المجموعة، والحصة تظهر في حصصها. بدون مجموعة: دفتر جديد للحصة.");
            }
            else kit.Note("ستُنشأ سبورة باسم الحصة ودفتر لها. ولو أضفت مجموعاتك (قائمة الحصص) يصير لكل مجموعة دفتر واحد بمقطع لكل حصة.");
            kit.Header("السبورة");
            var sizeBox = kit.Row("المقاس", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
            foreach (var x in BoardSizes) sizeBox.Items.Add(x.Name);
            sizeBox.SelectedIndex = 0;
            var bgBox = kit.Row("الخلفية", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
            foreach (var x in BoardBgs) bgBox.Items.Add(x.Name);
            bgBox.SelectedIndex = 0;
            kit.Header("أضف الآن (اختياري)");
            var book = kit.Full(new ToggleSwitch { Text = "📚 كتاب أو ملف PDF للشرح (نسخة كاملة منه تدخل الحصة)" }, 30);
            var pres = kit.Full(new ToggleSwitch { Text = "📽 عرض تقديمي PowerPoint" }, 30);
            kit.Note("وأثناء الحصة تضيف من قائمة «شرح حصة» ← «➕ أضف إلى الحصة»: كتبًا وعروضًا وسبورات وفيديوهات وروابط يوتيوب وتسجيل الشاشة. وكل ملف تفتحه يدخل الحصة تلقائيًا.");
            kit.Buttons("📘  ابدأ الحصة");
            if (SmokeShowDialog(kit, "lesson-new")) return;
            if (kit.Show(this) != DialogResult.OK) return;
            if (!string.IsNullOrWhiteSpace(nameBox.Text)) name = nameBox.Text.Trim();
            classId = clsBox is { SelectedIndex: > 0 } ? classes[clsBox.SelectedIndex - 1].Id : null;
            size = sizeBox.SelectedIndex; bg = bgBox.SelectedIndex;
            addBook = book.Checked; addPres = pres.Checked;
        }
        var task = CreateLessonAsync(name, classId, size, bg);
        if (addBook || addPres)
            task.ContinueWith(t =>
            {
                if (t.Result is not { } s) return;
                if (addBook) AddLessonFilesDialog(s, presentations: false);
                if (addPres) AddLessonFilesDialog(s, presentations: true);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// ينشئ الحصة: سبورة «X — سبورة» في رف «حصصي»، ودفتر (مقطع باسم الحصة في دفتر المجموعة، أو دفتر جديد)،
    /// ويفتحها على السبورة.
    /// </summary>
    internal async Task<StudySession?> CreateLessonAsync(string name, string? classId, int size = 0, int bg = 0)
    {
        if (!EnsureLibrary()) return null;
        name = string.IsNullOrWhiteSpace(name) ? DefaultLessonName(null) : name.Trim();
        var cls = classId == null ? null : _library.Classes.FirstOrDefault(c => c.Id == classId);
        Checkpoint("شرح حصة");
        // الدفتر: مقطع جديد في دفتر المجموعة (لو مفتوح في القارئ يتضاف من جوّه)
        Book? nb = cls?.NotebookId is { } nid ? _library.Books.FirstOrDefault(b => b.Id == nid && b.IsNotebook && File.Exists(b.LocalPath)) : null;
        string? section = null;
        if (nb != null)
        {
            if (ReaderShown && _currentBook == nb && _reader != null) section = await _reader.BoardSectionAsync(name);
            section ??= AddNotebookSectionToFile(nb, name);
            if (section != null) { nb.LastPage = 1; } else nb = null;
        }
        if (nb == null)
        {
            nb = CreateNotebookBook(cls != null ? "دفتر — " + cls.Name : name + " — دفتر", name, 0);
            section = NotebookFirstSection(nb);
            if (cls != null) { cls.NotebookId = nb.Id; cls.UpdatedUtc = DateTime.UtcNow; }
        }
        var board = CreateBoardBook(name + " — سبورة", size, bg, 0, null, LessonsShelf());
        var s = new StudySession
        {
            Name = name, ClassId = cls?.Id, Lesson = true,
            BoardId = board.Id, NotebookId = nb.Id, NotebookSection = section,
            Items = new() { BookTab(board), BookTab(nb) }
        };
        s.Items[1].Page = 1;
        s.Tabs = s.Items.Select(t => t.Copy()).ToList();
        s.StartTabs = s.Tabs.Select(t => t.Copy()).ToList();
        if (cls != null)
        {
            // حصة محضّرة للمجموعة (خطة وواجب من غير ملفات) تتنقل للحصة الجديدة
            if (cls.CurrentSessionId != null && _library.Sessions.FirstOrDefault(x => x.Id == cls.CurrentSessionId) is { Done: false, Lesson: false } prep && prep.Tabs.Count == 0)
            {
                s.Plan ??= prep.Plan;
                _library.Sessions.Remove(prep);
                _deletedKeys.Add(LibrarySync.SessionKey(prep));
            }
            cls.CurrentSessionId = s.Id;
            cls.UpdatedUtc = DateTime.UtcNow;
        }
        // حصة مجموعة مفتوحة (أو حصة سابقة لنفس المجموعة)؟ تنتهي أولًا (موضعها يُحفظ لها)
        if (ActiveSession is { ClassId: not null }) EndSession(markDone: true);
        _library.Sessions.Add(s);
        MarkDirty();
        RebuildShelf();
        OpenSession(s);
        ShowStatus($"📘 بدأت «{s.Name}»: السبورة والدفتر جاهزان — وكل ما تفتحه أو تضيفه يدخل الحصة", 7);
        return s;
    }

    private static string? NotebookFirstSection(Book nb)
    {
        try { return System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(nb.LocalPath))?["sec"]?.GetValue<string>(); }
        catch { return null; }
    }

    // ---------------- الإضافة إلى الحصة ----------------

    /// <summary>الحصة المفتوحة — أو رسالة إن لم توجد.</summary>
    private StudySession? LessonOrSay()
    {
        if (ActiveSession is { Lesson: true } s) return s;
        ShowBanner("ابدأ «📘 شرح حصة» أولًا — ثم أضف إليها ما تريد", BannerKind.Info, "شرح حصة جديدة", () => NewLessonDialog(), seconds: 10, key: "lesson-none");
        return null;
    }

    /// <summary>ملفات من الجهاز: كتب PDF/EPUB (رف «حصصي»)، وعروض PowerPoint (رف «عروضي»)، وفيديوهات (محاضرة الحصة).</summary>
    internal void AddLessonFilesDialog(StudySession s, bool presentations = false, bool videos = false)
    {
        string[]? files = SmokeLessonFiles;
        if (files == null)
        {
            using var dlg = new OpenFileDialog
            {
                Title = videos ? "اختر فيديو أو أكثر للحصة" : presentations ? "اختر عرض PowerPoint للحصة" : "اختر كتابًا أو ملفًا للحصة (تدخل نسخة كاملة منه في ملف الحصة)",
                Filter = videos ? MediaFiles.DialogFilter
                    : presentations ? "عروض PowerPoint|*.pptx;*.ppsx;*.pptm;*.ppt;*.pps|كل الملفات|*.*"
                    : "كتب وملفات (PDF و EPUB و PowerPoint)|*.pdf;*.epub;*.pptx;*.ppsx;*.pptm;*.ppt;*.pps|كل الملفات|*.*",
                Multiselect = true, RestoreDirectory = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            files = dlg.FileNames;
        }
        AddFilesToLesson(s, files, openFirst: true);
    }

    /// <summary>يضيف ملفات للحصة حسب نوعها — يرجّع عدد ما أُضيف.</summary>
    internal int AddFilesToLesson(StudySession s, IEnumerable<string> files, bool openFirst)
    {
        int n = 0;
        Book? firstBook = null;
        (LectureGroup G, VideoItem V)? firstVideo = null;
        var list = files.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var f in list.Where(BookFiles.IsBookFile))
        {
            var b = _library.Books.FirstOrDefault(x => string.Equals(x.LocalPath, f, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase))
                    ?? AddBooks(new[] { f }, Pptx.IsPresentation(f) ? PresentationsShelf() : LessonsShelf()).FirstOrDefault();
            if (b == null) continue;
            if (AddLessonItem(s, BookTab(b))) n++;
            firstBook ??= b;
        }
        var media = list.Where(f => MediaFiles.IsMedia(f) && !BookFiles.IsBookFile(f)).ToList();
        if (media.Count > 0)
        {
            var g = LessonGroup(s, create: true)!;
            var added = new List<VideoItem>();
            foreach (var f in media)
            {
                var v = g.Videos.FirstOrDefault(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase));
                if (v == null) { v = VideoItem.FromFile(f); g.Videos.Add(v); added.Add(v); n++; }
                firstVideo ??= (g, v);
            }
            if (added.Count > 0) { MarkDirty(); RebuildTree(); QueueProbe(added); }
        }
        if (n > 0) RebuildShelf();
        if (n == 0 && list.Count > 0 && firstBook == null && firstVideo == null) ShowStatus("هذه الملفات ليست كتبًا ولا عروضًا ولا فيديوهات", 5);
        else ShowStatus(n > 0 ? $"📘 أُضيف {n} إلى «{s.Name}»" : "موجودة في الحصة بالفعل", 4);
        if (openFirst)
        {
            if (firstBook != null) OpenBook(firstBook);
            else if (firstVideo is { } fv) PlayVideo(fv.G, fv.V);
        }
        return n;
    }

    /// <summary>رابط يوتيوب (أو أي رابط فيديو): يدخل الحصة كرابط فقط.</summary>
    internal VideoItem? AddLessonLink(StudySession s, string url, string? title = null)
    {
        url = url.Trim();
        if (!MediaFiles.IsUrl(url)) return null;
        if (Moodle.YouTubeIdIn(url) is { } yt) url = "https://www.youtube.com/watch?v=" + yt;
        var g = LessonGroup(s, create: true)!;
        var v = g.Videos.FirstOrDefault(x => x.Path == url);
        if (v != null) return v;
        v = new VideoItem { Path = url, Title = string.IsNullOrWhiteSpace(title) ? "فيديو يوتيوب" : title.Trim() };
        g.Videos.Add(v);
        s.UpdatedUtc = DateTime.UtcNow;
        MarkDirty();
        RebuildTree();
        if (string.IsNullOrWhiteSpace(title)) _ = FillLinkTitleAsync(v);
        return v;
    }

    /// <summary>العنوان والمدة من يوتيوب (لو الأداة موجودة والنت شغال) — من غير ما يعطّل شيئًا.</summary>
    private async Task FillLinkTitleAsync(VideoItem v)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            string? title = await YtDlp.OEmbedTitleAsync(v.Path, cts.Token);
            if (YtDlp.FindYtDlp() != null && (await YtDlp.ListAsync(v.Path, false, cts.Token)).Entries.FirstOrDefault() is { } e)
            {
                title ??= e.Title;
                if (e.DurationMs > 0) v.DurationMs = e.DurationMs;
            }
            if (_closing) return;
            if (!string.IsNullOrWhiteSpace(title)) v.Title = title;
            MarkDirty();
            RebuildTree();
        }
        catch (Exception ex) { Log.Info("lesson link title: " + ex.Message); }
    }

    private void AddLessonLinkDialog(StudySession s)
    {
        string? pre = null;
        try { if (Clipboard.ContainsText() && MediaFiles.ExtractUrls(Clipboard.GetText()).FirstOrDefault() is { } u) pre = u; } catch { }
        string? url = SmokeAskText != null ? SmokeAskText("url") : Dialogs.Input(this, "🔗 رابط يوتيوب للحصة", "الصق رابط الفيديو (يدخل الحصة رابطًا فقط، ويفتحه الطالب من يوتيوب):", pre ?? "");
        if (string.IsNullOrWhiteSpace(url)) return;
        var v = AddLessonLink(s, url);
        if (v == null) { Dialogs.Error(this, "هذا ليس رابطًا صحيحًا. انسخ رابط الفيديو من المتصفح والصقه هنا."); return; }
        ShowStatus($"🔗 أُضيف الرابط إلى «{s.Name}»", 4);
    }

    private void AddLessonBoard(StudySession s)
    {
        int n = (s.Items ?? new()).Count(t => _library.Books.FirstOrDefault(b => b.Id == t.Ref) is { IsBoard: true, IsNotebook: false }) + 1;
        var b = CreateBoardBook($"{s.Name} — سبورة {n}", 0, 0, 0, null, LessonsShelf());
        AddLessonItem(s, BookTab(b));
        OpenBook(b);
    }

    /// <summary>من مكتبتك: كتب وعروض وسبورات ودفاتر موجودة (اختيار متعدد).</summary>
    internal void AddLessonFromLibraryDialog(StudySession s)
    {
        var all = _library.Books.Where(b => b.AnnotatedOf == null && File.Exists(b.LocalPath))
            .OrderByDescending(b => b.LastOpenedUtc ?? b.AddedUtc).ToList();
        using var kit = new FormKit("📚 أضف من مكتبتك إلى الحصة", 600);
        var filter = kit.Row("بحث", FormKit.Style(new TextBox { RightToLeft = RightToLeft.Yes }));
        var list = kit.Full(new ListBox
        {
            SelectionMode = SelectionMode.MultiExtended, BackColor = Theme.Back, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
            RightToLeft = RightToLeft.Yes, IntegralHeight = false, Font = new Font("Segoe UI", 10f)
        }, 320);
        kit.Note("اختر ملفًا أو أكثر (Ctrl أو Shift للاختيار المتعدد). الكتب تدخل ملف الحصة كاملة، والسبورات والدفاتر بشرحها.");
        var shown = new List<Book>();
        void Fill()
        {
            string q = filter.Text.Trim();
            shown = all.Where(b => q.Length == 0 || b.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)).Take(300).ToList();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var b in shown)
                list.Items.Add($"{(b.IsPresentation ? "📽" : b.IsNotebook ? "📓" : b.IsBoard ? "🧑‍🏫" : "📖")}  {b.Title}" + (_library.ShelfOf(b) is { } sh ? $"   ·  {sh.Name}" : ""));
            list.EndUpdate();
        }
        filter.TextChanged += (_, _) => Fill();
        list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) kit.Form.DialogResult = DialogResult.OK; };
        Fill();
        kit.Buttons("أضف إلى الحصة");
        if (SmokeShowDialog(kit, "lesson-library")) return;
        if (kit.Show(this) != DialogResult.OK) return;
        var picked = list.SelectedIndices.Cast<int>().Where(i => i < shown.Count).Select(i => shown[i]).ToList();
        int n = picked.Count(b => AddLessonItem(s, BookTab(b)));
        ShowStatus(n > 0 ? $"📘 أُضيف {n} إلى «{s.Name}»" : "موجودة في الحصة بالفعل", 4);
        if (picked.Count == 1) OpenBook(picked[0]);
    }

    // ---------------- محتويات الحصة ----------------

    internal sealed record LessonEntry(string Glyph, string Title, string Detail, WorkTab? Item, Book? Book, LectureGroup? Group, VideoItem? Video);

    /// <summary>كل محتويات الحصة: سبوراتها ودفترها وكتبها وعروضها، وفيديوهاتها وروابطها وتسجيلاتها.</summary>
    internal List<LessonEntry> LessonEntries(StudySession s)
    {
        var list = new List<LessonEntry>();
        foreach (var t in s.Items ?? s.Tabs)
        {
            if (t.Kind == WorkTab.BookKind)
            {
                var b = _library.Books.FirstOrDefault(x => x.Id == t.Ref);
                if (b == null) { list.Add(new LessonEntry("⚠", t.Title ?? "", "لم يعد في المكتبة", t, null, null, null)); continue; }
                string glyph = b.IsPresentation ? "📽" : b.IsNotebook ? "📓" : b.IsBoard ? "🧑‍🏫" : "📖";
                string detail = b.Id == s.BoardId ? "سبورة الحصة"
                    : b.Id == s.NotebookId ? "دفتر الحصة" + (s.NotebookSection != null ? " (مقطع الحصة)" : "")
                    : b.IsPresentation ? "عرض تقديمي" : b.IsNotebook ? "دفتر" : b.IsBoard ? "سبورة"
                    : $"كتاب ({b.PageCount} صفحة) — يدخل الحصة كاملًا";
                if (t.Page is int p and > 1 && !b.IsBoard) detail += $" · وصلت {(b.IsPresentation ? "الشريحة" : "ص")} {p}";
                list.Add(new LessonEntry(glyph, b.Title, detail, t, b, null, null));
            }
            else if (t.Kind == WorkTab.CourseKind && t.ItemPath != null)
            {
                var v = _library.AllVideos.FirstOrDefault(x => string.Equals(x.Path, t.ItemPath, StringComparison.OrdinalIgnoreCase));
                var g = v != null ? _library.FindGroupOf(v) : null;
                bool url = MediaFiles.IsUrl(t.ItemPath);
                list.Add(new LessonEntry(url ? "🔗" : "🎬", v?.Title ?? t.Title ?? "", (url ? "رابط" : "فيديو") + (t.PositionMs is long ms and > 0 ? " · وصلت " + TimeFormat.Format(ms) : ""), t, null, g, v));
            }
        }
        if (LessonGroup(s, create: false) is { } lg)
            foreach (var v in lg.Videos)
            {
                bool url = MediaFiles.IsUrl(v.Path), rec = s.Recordings.Contains(v.Path);
                list.Add(new LessonEntry(rec ? "⏺" : url ? "🔗" : "🎬", v.Title, rec ? $"تسجيل الشاشة · {v.Bookmarks.Count} علامة" : url ? "رابط يوتيوب (رابط فقط)" : "فيديو من جهازك",
                    null, null, lg, v));
            }
        foreach (var r in s.Recordings.Where(r => list.All(e => e.Video?.Path != r)))
        {
            var v = _library.AllVideos.FirstOrDefault(x => x.Path == r);
            list.Add(new LessonEntry("⏺", v?.Title ?? Path.GetFileNameWithoutExtension(r), "تسجيل الشاشة", null, null, v != null ? _library.FindGroupOf(v) : null, v));
        }
        return list;
    }

    private void OpenLessonEntry(LessonEntry e)
    {
        if (e.Book != null) { OpenBook(e.Book); return; }
        if (e.Video != null && e.Group != null) { PlayVideo(e.Group, e.Video); return; }
        ShowStatus("هذا العنصر لم يعد موجودًا", 4);
    }

    /// <summary>«أزل من الحصة»: لا يُحذف أي ملف — الكتاب يبقى على رفه، والفيديو الخارجي في دورته.</summary>
    internal void RemoveLessonEntry(StudySession s, LessonEntry e)
    {
        if (e.Item != null)
        {
            s.Items?.RemoveAll(x => x.SameItem(e.Item));
            // تبويبه لو مفتوح يتقفل (وإلا يرجع يتسجّل)
            var open = _tabs.FirstOrDefault(x => x.Same(e.Item) && (x.Kind != WorkTab.CourseKind || string.Equals(x.ItemPath, e.Item.ItemPath, StringComparison.OrdinalIgnoreCase)));
            if (open != null) CloseTab(open);
            if (e.Book?.Id == s.BoardId) s.BoardId = null;
            if (e.Book?.Id == s.NotebookId) { s.NotebookId = null; s.NotebookSection = null; }
        }
        else if (e.Video != null)
        {
            s.Recordings.Remove(e.Video.Path);
            if (e.Group != null && e.Group.Id == s.GroupId)
            {
                if (_currentVideo == e.Video) GoHome();
                e.Group.Videos.Remove(e.Video);
                RebuildTree();
            }
        }
        s.UpdatedUtc = DateTime.UtcNow;
        MarkDirty();
    }

    internal void LessonContentsDialog(StudySession s)
    {
        if (_activeTab >= 0) CaptureTab(_activeTab);
        MirrorSession();
        using var kit = new FormKit($"📋 محتويات الحصة — {s.Name}", 640);
        var lv = kit.Full(new ListView
        {
            View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, MultiSelect = false,
            RightToLeft = RightToLeft.Yes, RightToLeftLayout = true, Font = new Font("Segoe UI", 10f)
        }, 330);
        lv.Columns.Add("", 34);
        lv.Columns.Add("العنصر", 300);
        lv.Columns.Add("", 230);
        List<LessonEntry> entries = new();
        void Fill()
        {
            entries = LessonEntries(s);
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var e in entries) lv.Items.Add(new ListViewItem(new[] { e.Glyph, e.Title, e.Detail }) { ForeColor = e.Glyph == "⚠" ? Theme.Muted : Theme.Text });
            lv.EndUpdate();
        }
        var count = kit.Note("");
        void Count() => count.Text = entries.Count == 0 ? "الحصة فارغة بعد." : $"{entries.Count} عنصرًا — كلها تُصدَّر في ملف الحصة (والكتب كاملة).";
        Fill(); Count();
        LessonEntry? Sel() => lv.SelectedIndices.Count > 0 && lv.SelectedIndices[0] < entries.Count ? entries[lv.SelectedIndices[0]] : null;
        lv.DoubleClick += (_, _) => { if (Sel() is { } e) { kit.Form.DialogResult = DialogResult.OK; BeginInvoke(() => OpenLessonEntry(e)); } };
        var addMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        kit.Form.Disposed += (_, _) => addMenu.Dispose();
        void Later(Action a) { kit.Form.DialogResult = DialogResult.OK; BeginInvoke(a); }
        addMenu.Items.AddRange(LessonAddItems(s, Later).ToArray());
        kit.Buttons("تم", null,
            ("▶  افتح", () => { if (Sel() is { } e) Later(() => OpenLessonEntry(e)); }),
            ("✖  أزل من الحصة", () =>
            {
                if (Sel() is not { } e) return;
                if (SmokeAskText == null && !Dialogs.Confirm(kit.Form, $"إزالة «{e.Title}» من الحصة؟\nلا يُحذف أي ملف — يبقى في مكتبتك.")) return;
                RemoveLessonEntry(s, e); Fill(); Count();
            }),
            ("➕  أضف…", () => addMenu.Show(Cursor.Position)));
        if (SmokeShowDialog(kit, "lesson-contents")) return;
        kit.Show(this);
    }

    /// <summary>أوامر «➕ أضف إلى الحصة» (في القائمة وفي شباك المحتويات).</summary>
    private List<ToolStripItem> LessonAddItems(StudySession s, Action<Action>? wrap = null)
    {
        ToolStripMenuItem It(string text, Action a) => Item(text, () => { if (wrap != null) wrap(a); else a(); });
        return new List<ToolStripItem>
        {
            It("📚  كتاب أو ملف من جهازك…", () => AddLessonFilesDialog(s)),
            It("📚  من مكتبتك (كتاب أو عرض أو سبورة أو دفتر)…", () => AddLessonFromLibraryDialog(s)),
            It("📽  عرض PowerPoint…", () => AddLessonFilesDialog(s, presentations: true)),
            It("🧑‍🏫  سبورة أخرى للحصة", () => AddLessonBoard(s)),
            new ToolStripSeparator(),
            It("🎬  فيديو من جهازك…", () => AddLessonFilesDialog(s, videos: true)),
            It("🔗  رابط يوتيوب…", () => AddLessonLinkDialog(s)),
            It(Recording ? "⏹  أوقف تسجيل الشاشة" : "⏺  سجّل الشاشة الآن…", () => { if (Recording) _ = StopRecordingAsync(save: true); else StartRecordingDialog(LessonGroup(s, create: true)); })
        };
    }

    // ---------------- القائمة والزرار ----------------

    /// <summary>أوامر الحصة المفتوحة (أول قائمة «شرح حصة»، وزر الحصة في شريط التبويبات).</summary>
    internal List<ToolStripItem> LessonLiveItems(StudySession s)
    {
        var add = SubMenu("➕  أضف إلى الحصة");
        add.DropDownItems.AddRange(LessonAddItems(s).ToArray());
        return new List<ToolStripItem>
        {
            new ToolStripLabel($"الحصة المفتوحة: {s.Name}{LessonRemainingText(s)}") { ForeColor = Theme.Success },
            Item("📋  محتويات الحصة…", () => LessonContentsDialog(s)),
            add,
            Item("📝  الخطة والواجب…", () => SessionNotesDialog(s)),
            Item("📤  تصدير الحصة (PDF كامل وملف «مسار» للطلاب)…", () => ExportSessionDialog(s)),
            Item("✏  إعادة التسمية…", () => RenameSession(s)),
            Item("⏹  إنهاء الحصة…", () => EndLessonDialog(s)),
            new ToolStripSeparator()
        };
    }

    private IEnumerable<StudySession> RecentLessons(int n = 10) =>
        _library.Sessions.Where(x => x.Lesson).OrderByDescending(x => x.LastOpenedUtc).Take(n);

    private void ShowLessonsShelf()
    {
        var shelf = LessonsShelf();
        RebuildTree();
        if (_split.Panel2Collapsed) ToggleSidePanel();
        ShowShelf(true);
        if (ShelfNodeOf(shelf) is { } n) { n.Expand(); SelectShelfItem(shelf); }
    }

    /// <summary>قائمة «شرح حصة»: حصة جديدة، والحصة المفتوحة (محتوياتها والإضافة والتصدير والإنهاء)، وآخر الحصص، وفتح حصة من ملف.</summary>
    private ToolStripMenuItem BuildLessonMenu()
    {
        var m = new ToolStripMenuItem("شرح حصة") { ForeColor = Theme.Text };
        var recent = new ToolStripMenuItem("🕘  آخر الحصص") { ForeColor = Theme.Text };
        recent.DropDownItems.Add(new ToolStripMenuItem("…"));
        m.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("📘  شرح حصة جديدة…", () => NewLessonDialog(), Keys.Control | Keys.Alt | Keys.L),
            recent,
            Item("📂  رف «حصصي»", () => { if (EnsureLibrary()) ShowLessonsShelf(); }),
            new ToolStripSeparator(),
            Item("📂  افتح حصة من ملف (للطالب)…", () => OpenLessonFileDialog(), Keys.Control | Keys.Alt | Keys.O)
        });
        m.DropDownOpening += (_, _) =>
        {
            foreach (var old in _lessonMenuLive) { m.DropDownItems.Remove(old); old.Dispose(); }
            _lessonMenuLive.Clear();
            if (HasLibrary && ActiveSession is { Lesson: true } s)
            {
                _lessonMenuLive.AddRange(LessonLiveItems(s));
                for (int i = 0; i < _lessonMenuLive.Count; i++) m.DropDownItems.Insert(i, _lessonMenuLive[i]);
            }
            foreach (var old in recent.DropDownItems.Cast<ToolStripItem>().ToList()) old.Dispose();
            recent.DropDownItems.Clear();
            var list = HasLibrary ? RecentLessons().ToList() : new List<StudySession>();
            if (list.Count == 0) recent.DropDownItems.Add(new ToolStripMenuItem("(لا يوجد بعد)") { Enabled = false, ForeColor = Theme.Muted });
            foreach (var x in list)
            {
                var target = x;
                string title = x.Name.Length > 45 ? x.Name[..45] + "…" : x.Name;
                recent.DropDownItems.Add(Item($"{(x.Id == _sessionId ? "● " : x.Done ? "✓ " : "")}{(x.ImportedFrom != null ? "📥 " : "📘 ")}{title}", () => { if (target.Id != _sessionId) OpenSession(target); }));
            }
        };
        return m;
    }

    /// <summary>الزرار السريع جنب «سبورة»: المعلم يبدأ حصة، والطالب يفتح حصة من ملف.</summary>
    private void ApplyLessonQuick()
    {
        if (_lessonQuick == null) return;
        bool student = _settings.Role == "student";
        _lessonQuick.Text = student ? "📂  افتح حصة" : "📘  شرح حصة";
        _lessonQuick.ToolTipText = student
            ? "افتح ملف حصة من معلمك (ملف مضغوط من «مسار»): الكتب والسبورات والدفتر والعروض والفيديوهات والروابط"
            : "حصة جديدة جاهزة: سبورة ودفتر باسمها، وكل ما تفتحه أثناءها يدخل فيها — وفي النهاية تصدّرها PDF وملف «مسار» للطلاب";
    }
}
