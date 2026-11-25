using System.Globalization;
using System.Text.Json.Nodes;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// نوشن: من أي موضوع «📤 نوشن ← أنشئ قاعدة نوشن» بيعمل صفحة الموضوع وقاعدتين (الكورسات والمحاضرات بالحضور والمهام)
/// والفيديوهات مربعات ☐ جوه صفحات المحاضرات. بعد كده التقدّم والجديد بيتحدّثوا لوحدهم كل كام دقيقة،
/// والخطة (الموعد، الحضور، TASK/SEARCH/SUMMARY) بتتسحب من نوشن وتظهر في الشجرة والرئيسية، وتتعدّل من هنا كمان.
/// المفتاح متشفّر على الجهاز ده بس، ومسار مابيمسحش حاجة في نوشن.
/// </summary>
internal sealed partial class MainForm
{
    private NotionState? _notionState;
    private bool _notionBusy;
    private long _dueNotionSync = Environment.TickCount64 + 45_000;   // أول مزامنة بعد ما البرنامج يهدى
    private long _dueNotionPull = Environment.TickCount64 + 50_000;
    private readonly List<string> _notionLog = new();
    private string? _notionLastError;
    /// <summary>للفحص التلقائي: صفحة «مسار» في نوشن الوهمي.</summary>
    internal static string? SmokeNotionParent;
    /// <summary>للفحص التلقائي: الشبابيك بتظهر وتتصوّر وتتقفل بدل ما تستنى (الاسم = اسم الصورة).</summary>
    internal static bool SmokeDialogShots;

    /// <summary>في الفحص: الشباك يظهر ويتصوّر ويتقفل (من غير ما يستنى حد) ← true.</summary>
    private bool SmokeShowDialog(FormKit kit, string shot)
    {
        if (!SmokeDialogShots) return false;
        kit.Prepare();
        kit.Form.StartPosition = FormStartPosition.CenterParent;
        kit.Form.Show(this);
        var until = DateTime.UtcNow.AddMilliseconds(700);
        while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(15); }
        SaveSmokeShot(kit.Form, shot);
        kit.Form.Close();
        return true;
    }

    private NotionState NotionStateLoaded => _notionState ??= NotionState.Load();
    private bool NotionReady => !string.IsNullOrWhiteSpace(_settings.NotionToken) && NotionClient.ParseId(_settings.NotionParentPage) != null;
    private IEnumerable<Topic> NotionTopics => _library.Topics.Where(t => t.Notion is { Layout: >= NotionSync.CurrentLayout });
    private IEnumerable<Topic> OldNotionTopics => _library.Topics.Where(t => t.Notion is { Layout: < NotionSync.CurrentLayout });
    private bool _notionOldAsked;
    private static string NotionLogPath => Path.Combine(Storage.AppDataDir, "notion-log.txt");

    private Topic? NotionTopicOf(Course? c) => c != null && _library.TopicOf(c) is { Notion: not null } t ? t : null;
    private Topic? NotionTopicOf(LectureGroup g) => NotionTopicOf(_library.FindCourseOf(g));

    // =====================================================================
    //  التجهيز (المفتاح والصفحة)
    // =====================================================================

    /// <summary>بيطلب المفتاح والصفحة لو ناقصين، ويتأكد إنهم شغالين ← true لو جاهز.</summary>
    private async Task<bool> EnsureNotionSetupAsync(bool force = false)
    {
        if (NotionReady && !force) return true;
        string token = _settings.NotionToken, parent = _settings.NotionParentPage ?? "";
        while (true)
        {
            using var kit = new FormKit("ربط مسار بنوشن", 640);
            kit.Header("خطوتان في نوشن (مرة واحدة)");
            kit.Note("1) اضغط «افتح صفحة الربط» بالأسفل، وأنشئ ربطًا جديدًا (New integration) من نوع Internal.\n" +
                     "     من Capabilities فعّل القراءة والتعديل والإضافة (Read / Update / Insert)، ثم انسخ المفتاح (Secret).\n" +
                     "2) في نوشن أنشئ صفحة اسمها «مسار»، ومن ⋯ في الأعلى اختر Connections، وأضف الربط الخاص بك.\n" +
                     "     سينشئ مسار موضوعاتك داخل هذه الصفحة فقط، ولن يرى باقي نوشن.", 104);
            kit.Header("البيانات");
            var tokenBox = kit.Row("المفتاح (Secret)", new TextBox { Text = token, UseSystemPasswordChar = true, RightToLeft = RightToLeft.No });
            var pageBox = kit.Row("رابط صفحة «مسار»", new TextBox { Text = parent, RightToLeft = RightToLeft.No });
            kit.Note("انسخ رابط الصفحة من نوشن (Share ← Copy link، أو ⋯ ← Copy link). يُحفظ المفتاح مشفّرًا على هذا الجهاز فقط.", 40);
            kit.Buttons("اربط", "إلغاء", ("افتح صفحة الربط", () => OpenInBrowser("https://www.notion.so/my-integrations")));
            if (SmokeShowDialog(kit, "notion-setup")) return false;
            if (kit.Show(this) != DialogResult.OK) return false;
            token = tokenBox.Text.Trim();
            parent = pageBox.Text.Trim();
            string? pid = NotionClient.ParseId(parent);
            if (token.Length < 10) { Dialogs.Error(this, "الصق مفتاح الـ integration (يبدأ غالبًا بـ ntn_ أو secret_)."); continue; }
            if (pid == null) { Dialogs.Error(this, "هذا ليس رابط صفحة نوشن — انسخه من Share ← Copy link."); continue; }
            ShowStatus("جارٍ التحقق من نوشن…", 10);
            try
            {
                using var api = new NotionClient(token);
                string who = await api.MeAsync(CancellationToken.None);
                string title = await api.PageTitleAsync(pid, CancellationToken.None);
                _settings.NotionToken = token;
                _settings.NotionParentPage = parent;
                SaveSettings();
                ShowBanner($"نوشن مربوط ✓ ({who}) — ستُنشأ القواعد داخل «{(title.Length > 0 ? title : "الصفحة")}»", BannerKind.Success, seconds: 8);
                return true;
            }
            catch (NotionException ex) { Dialogs.Error(this, ex.Friendly); }
            catch (Exception ex) { Dialogs.Error(this, "تعذّر الوصول إلى نوشن: " + ex.Message); }
        }
    }

    // =====================================================================
    //  التشغيل (كل العمليات بتعدّي من هنا)
    // =====================================================================

    /// <summary>
    /// بيشغّل عملية نوشن: شريط بالتقدّم (لو مش quiet)، وبعدها حفظ الحالة والمكتبة والسجل.
    /// quiet = التحديث التلقائي: الغلطة بتتسجل وبتظهر مرة في شريط الحالة بس.
    /// </summary>
    private async Task<bool> RunNotionAsync(string what, Func<NotionSync, CancellationToken, Task> op, bool quiet)
    {
        if (_notionBusy) { if (!quiet) ShowStatus("نوشن ما زال ينهي الطلب السابق…", 3); return false; }
        if (!NotionReady && (quiet || !await EnsureNotionSetupAsync())) return false;
        _notionBusy = true;
        var journal = JournalEntries;
        var studyByCourse = journal.Where(e => e.Kind == JournalEntry.Study && e.CourseId != null)
            .GroupBy(e => e.CourseId!).ToDictionary(g => g.Key, g => (Seconds: (double)g.Sum(e => e.Seconds), Last: (DateTime?)g.Max(e => e.StartUtc)));
        using var api = new NotionClient(_settings.NotionToken);
        var sync = new NotionSync(api, _library, NotionStateLoaded)
        {
            StudyOf = c => studyByCourse.TryGetValue(c.Id, out var s) ? s : (0, null),
            DailyOf = t =>
            {
                var ids = _library.CoursesOf(t).ToDictionary(c => c.Id);
                return journal.Where(e => e.Kind == JournalEntry.Study && e.CourseId != null && ids.ContainsKey(e.CourseId))
                    .GroupBy(e => DateOnly.FromDateTime(e.StartUtc.ToLocalTime()))
                    .Select(g => (g.Key, g.Sum(e => e.Seconds) / 60.0, (IReadOnlyCollection<Course>)g.Select(e => ids[e.CourseId!]).Distinct().ToList()));
            },
            Progress = quiet ? null : text => { if (!IsDisposed) ShowBanner("📓 نوشن: " + text, BannerKind.Info, seconds: 0, key: "notion"); }
        };
        if (!quiet) ShowBanner("📓 نوشن: " + what + "…", BannerKind.Info, seconds: 0, key: "notion");
        bool ok = false;
        try
        {
            await op(sync, CancellationToken.None);
            ok = true;
            _notionLastError = null;
            if (!quiet) HideBanner("notion");
        }
        catch (NotionException ex)
        {
            Log.Info($"notion {what}: {ex.Status} {ex.Code} {ex.Message}");
            NotionLog("⚠ " + what + ": " + ex.Friendly);
            if (!quiet && !SmokeTest.Active) { HideBanner("notion"); Dialogs.Error(this, ex.Friendly); }
            else if (_notionLastError != ex.Code) { _notionLastError = ex.Code; ShowStatus("نوشن: " + ex.Friendly, 8); }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            Log.Error("notion " + what, ex);
            NotionLog("⚠ " + what + ": " + ex.Message);
            if (!quiet && !SmokeTest.Active) { HideBanner("notion"); Dialogs.Error(this, ex.Message); }
        }
        finally
        {
            _notionBusy = false;
            NotionStateLoaded.Save();
            foreach (var c in sync.Changes) NotionLog(c);
            if (sync.LibraryChanged && !IsDisposed) { MarkDirty(); RefreshNotionMarks(); }
            Log.Info($"notion {what}: {(ok ? "ok" : "failed")}, {api.Requests} requests, {sync.Changes.Count} changes");
            // خطط اتعدّلت وإحنا مشغولين: تتبعت على طول بعدها (مش لو الطلب ده فشل — وإلا هنلف في دايرة والنت فاصل)
            if (ok && what != PendingPlansJob && NotionStateLoaded.PendingPlan.Count > 0 && !IsDisposed && IsHandleCreated)
                BeginInvoke(() => _ = PushPendingPlansAsync());
        }
        return ok;
    }

    private const string PendingPlansJob = "خطط معلّقة";

    private Task<bool> PushPendingPlansAsync() =>
        RunNotionAsync(PendingPlansJob, async (s, ct) =>
        {
            var ids = NotionStateLoaded.PendingPlan.ToList();
            foreach (var g in _library.AllGroups.Where(g => ids.Contains(g.Id)).ToList())
                if (g.NotionPageId != null && g.Plan != null) await s.PushPlanAsync(g, ct);
                else NotionStateLoaded.PendingPlan.Remove(g.Id);
        }, quiet: true);

    private void NotionLog(string line)
    {
        string entry = DateTime.Now.ToString("yyyy/MM/dd HH:mm") + "  " + line;
        _notionLog.Add(entry);
        if (_notionLog.Count > 300) _notionLog.RemoveRange(0, 100);
        try
        {
            Directory.CreateDirectory(Storage.AppDataDir);
            var fi = new FileInfo(NotionLogPath);
            if (fi.Exists && fi.Length > 512 * 1024) File.WriteAllLines(NotionLogPath, File.ReadAllLines(NotionLogPath).TakeLast(1000));
            File.AppendAllText(NotionLogPath, entry + Environment.NewLine);
        }
        catch (IOException) { }
    }

    /// <summary>من تايمر الواجهة: تحديث التقدّم كل 3 دقايق، وسحب الخطة كل 10 (لو فيه مواضيع مربوطة).</summary>
    private void NotionAutoTick()
    {
        if (_notionBusy || !_settings.NotionAutoSync || !NotionReady || SmokeTest.Active) return;
        // مواضيع اتعملت بالشكل القديم (جدول محاضرات واحد): مرة واحدة نعرض إعادة البناء
        if (!_notionOldAsked && OldNotionTopics.FirstOrDefault() is { } old && Environment.TickCount64 > _dueNotionSync - 30_000)
        {
            _notionOldAsked = true;
            ShowBanner($"📓 قاعدة «{old.Name}» في نوشن بالشكل القديم (كل المحاضرات في جدول واحد) — الشكل الجديد مثل شجرة مسار",
                BannerKind.Warning, "أعد البناء", () => RebuildNotionTopic(old), seconds: 0, key: "notion-old");
        }
        if (Due(ref _dueNotionSync, 180_000) && NotionTopics.Any())
            _ = RunNotionAsync("تحديث تلقائي", async (s, ct) => { foreach (var t in NotionTopics.ToList()) await s.SyncTopicAsync(t, ct); }, quiet: true);
        else if (Due(ref _dueNotionPull, 600_000) && NotionTopics.Any())
            _ = RunNotionAsync("سحب الخطة", async (s, ct) =>
            {
                int n = 0;
                foreach (var t in NotionTopics.ToList()) n += await s.PullPlanAsync(t, ct);
                if (n > 0 && !IsDisposed) { RefreshNotionMarks(); RefreshHomePlan(); }
            }, quiet: true);
    }

    // =====================================================================
    //  أوامر الموضوع
    // =====================================================================

    private async void CreateNotionForTopic(Topic t)
    {
        if (t.Notion != null) { ShowStatus("هذا الموضوع مربوط بنوشن بالفعل", 3); return; }
        if (!await EnsureNotionSetupAsync()) return;
        int courses = _library.CoursesOf(t).Count(), lectures = _library.CoursesOf(t).Sum(c => c.Groups.Count);
        if (!Dialogs.Confirm(this, $"سأنشئ في نوشن صفحة «{t.Name}» بنفس شكل شجرة مسار:\n" +
                                   $"• «الدورات» ({courses} دورة) مقسّمة حسب المصدر وبالترتيب نفسه — الحالة، التقدّم، المشاهدة، الساعات، وقت المذاكرة…\n" +
                                   $"• داخل صفحة كل دورة جدول محاضراتها ({lectures} محاضرة إجمالًا) — الحالة، التقدّم، الموعد، الحضور، TASK، SEARCH، SUMMARY\n" +
                                   "• الفيديوهات مربعات ☐ داخل صفحة كل محاضرة، وتُوسم تلقائيًا عند المشاهدة.\n\n" +
                                   "بعد ذلك تُضاف أي دورة أو محاضرة تُضاف للموضوع هناك تلقائيًا، ويتحدّث التقدّم كل بضع دقائق.\n" +
                                   (lectures > 150 ? $"سيستغرق الإنشاء الأول نحو {Math.Max(1, lectures / 60)} دقيقة (نوشن يقبل عددًا محدودًا من الطلبات في الثانية) — يمكنك متابعة عملك كالمعتاد.\n" : "") +
                                   "\nهل نبدأ؟"))
            return;
        string parent = SmokeNotionParent ?? NotionClient.ParseId(_settings.NotionParentPage)!;
        Checkpoint("ربط الموضوع بنوشن");
        if (await RunNotionAsync($"إنشاء «{t.Name}»", (s, ct) => s.CreateTopicAsync(t, parent, ct), quiet: false))
        {
            ShowBanner($"📓 أُنشئ «{t.Name}» في نوشن ✓ — أصبح التحديث تلقائيًا", BannerKind.Success, "افتحه", () => OpenNotionPage(t.Notion?.PageId), seconds: 12);
            RefreshTopicNode(t);
        }
    }

    private void OpenNotionPage(string? pageId)
    {
        if (pageId == null) return;
        // تطبيق نوشن لو موجود (notion://)، وإلا المتصفح — مش جوه مسار
        string url = NotionClient.PageUrl(pageId);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notion://" + url["https://".Length..]) { UseShellExecute = true })?.Dispose(); }
        catch (Exception) { OpenInBrowser(url); }
    }

    private void SyncNotionTopic(Topic t, bool fromScratch = false) =>
        _ = RunNotionAsync(fromScratch ? $"مزامنة «{t.Name}» من البداية" : $"مزامنة «{t.Name}»", async (s, ct) =>
        {
            await s.SyncTopicAsync(t, ct, recreateMissing: fromScratch);
            await s.PullPlanAsync(t, ct);
            if (!IsDisposed) { ShowStatus($"📓 «{t.Name}» متزامن مع نوشن ✓", 4); RefreshHomePlan(); }
        }, quiet: false);

    private void PullNotionPlan(Topic t) =>
        _ = RunNotionAsync($"سحب خطة «{t.Name}»", async (s, ct) =>
        {
            int n = await s.PullPlanAsync(t, ct);
            if (!IsDisposed) { ShowStatus(n > 0 ? $"📥 جُلبت خطة {n} محاضرة من نوشن" : "الخطة كما هي — لا جديد في نوشن", 5); RefreshHomePlan(); }
        }, quiet: false);

    private void CreateNotionLog(Topic t) =>
        _ = RunNotionAsync("سجل المذاكرة", async (s, ct) =>
        {
            await s.CreateLogAsync(t, ct);
            if (!IsDisposed) ShowStatus("🗓️ أُنشئ «سجل المذاكرة» — يُضاف كل يوم تذاكر فيه تلقائيًا", 6);
        }, quiet: false);

    /// <summary>«أعد البناء»: الصفحة اللي مسار عملها تروح سلة نوشن (بترجع منها لحد 30 يوم)، ويتعمل الموضوع بالشكل الجديد بخطته.</summary>
    private async void RebuildNotionTopic(Topic t)
    {
        HideBanner("notion-old");
        if (t.Notion == null) return;
        if (!await EnsureNotionSetupAsync()) return;
        if (!Dialogs.Confirm(this, $"سأعيد بناء «{t.Name}» في نوشن بنفس شكل شجرة مسار:\n" +
                                   "• الدورات مقسّمة حسب المصدر وبالترتيب نفسه.\n" +
                                   "• في صفحة كل دورة جدول محاضراتها فقط (الموعد، الحضور، TASK، SEARCH، SUMMARY).\n\n" +
                                   "ستُنقل الخطة التي كتبتها (المواعيد والحضور والمهام) إلى الصفحة الجديدة.\n" +
                                   "ستذهب الصفحة القديمة التي أنشأها مسار إلى سلة نوشن (يمكنك إرجاعها خلال 30 يومًا).\n\nهل نبدأ؟"))
            return;
        string parent = SmokeNotionParent ?? NotionClient.ParseId(_settings.NotionParentPage)!;
        Checkpoint("إعادة بناء نوشن");
        if (await RunNotionAsync($"إعادة بناء «{t.Name}»", (s, ct) => s.RebuildTopicAsync(t, parent, ct), quiet: false))
        {
            ShowBanner($"📓 أُعيد إنشاء «{t.Name}» بشكل الشجرة ✓ — والقديمة في السلة", BannerKind.Success, "افتحه", () => OpenNotionPage(t.Notion?.PageId), seconds: 12);
            RefreshTopicNode(t);
            RefreshHomePlan();
        }
    }

    private void UnlinkNotion(Topic t)
    {
        if (!Dialogs.Confirm(this, $"هل تريد فك ربط «{t.Name}» بنوشن؟\nتبقى الصفحة والقواعد في نوشن كما هي (مسار لا يحذف شيئًا)، لكن يتوقف التحديث.\nيمكنك إنشاء قاعدة جديدة لاحقًا.")) return;
        Checkpoint("فك ربط نوشن");
        t.Notion = null;
        t.TouchInfo();
        MarkDirty();
        RefreshTopicNode(t);
        ShowStatus("فُك الربط — نوشن كما هو", 4);
    }

    private void ShowNotionLog()
    {
        var lines = _notionLog.Count > 0 ? _notionLog.AsEnumerable()
            : File.Exists(NotionLogPath) ? File.ReadLines(NotionLogPath).TakeLast(200) : Array.Empty<string>();
        var list = lines.Reverse().Take(200).ToList();
        Dialogs.Info(this, list.Count == 0 ? "لم يُرسل شيء إلى نوشن بعد." : string.Join("\n", list.Take(40)) + (list.Count > 40 ? $"\n… ({list.Count - 40} سطرًا أقدم في {NotionLogPath})" : ""), "سجل مزامنة نوشن");
    }

    private ToolStripMenuItem NotionTopicMenu(Topic t)
    {
        if (t.Notion == null)
            return SubMenuOf("📤  نوشن",
                MenuItem("📓  أنشئ قاعدة نوشن لهذا الموضوع…", () => CreateNotionForTopic(t)),
                Sep(),
                MenuItem("⚙  إعدادات نوشن (المفتاح والصفحة)…", () => _ = EnsureNotionSetupAsync(force: true)));
        if (t.Notion.Layout < NotionSync.CurrentLayout)
            return SubMenuOf("📤  نوشن",
                MenuItem("🧱  أعد البناء بشكل الشجرة (تذهب القديمة إلى السلة)…", () => RebuildNotionTopic(t)),
                MenuItem("↗  افتح الموضوع في نوشن", () => OpenNotionPage(t.Notion.PageId)),
                Sep(),
                MenuItem("✂  فك الربط (لا يُحذف شيء من نوشن)…", () => UnlinkNotion(t)));
        var items = new List<ToolStripItem>
        {
            MenuItem("↗  افتح الموضوع في نوشن", () => OpenNotionPage(t.Notion.PageId)),
            Sep(),
            MenuItem("🔄  زامن الآن", () => SyncNotionTopic(t)),
            MenuItem("📥  اجلب الخطة من نوشن (المواعيد والحضور والمهام)", () => PullNotionPlan(t)),
            MenuItem("📅  وزّع المواعيد على المحاضرات…", () => ScheduleNotion(_library.CoursesOf(t).SelectMany(c => c.Groups).ToList(), $"موضوع «{t.Name}»")),
        };
        if (t.Notion.LogDsId == null) items.Add(MenuItem("🗓️  أنشئ «سجل المذاكرة» اليومي", () => CreateNotionLog(t)));
        items.Add(Sep());
        items.Add(MenuItem("📜  سجل المزامنة", ShowNotionLog));
        items.Add(MenuItem("🔁  زامن من البداية (أعد الصفوف التي حُذفت من نوشن)", () => SyncNotionTopic(t, fromScratch: true)));
        items.Add(MenuItem("🧱  أعد البناء من جديد (تذهب القديمة إلى السلة)…", () => RebuildNotionTopic(t)));
        items.Add(MenuItem("⚙  إعدادات نوشن…", () => _ = EnsureNotionSetupAsync(force: true)));
        items.Add(MenuItem("✂  فك الربط (لا يُحذف شيء من نوشن)…", () => UnlinkNotion(t)));
        return SubMenuOf("📤  نوشن", items.ToArray());
    }

    private ToolStripMenuItem? NotionCourseMenu(Course c)
    {
        if (NotionTopicOf(c) is not { } t) return null;
        var items = new List<ToolStripItem>();
        if (c.NotionPageId != null) items.Add(MenuItem("↗  افتح الدورة في نوشن", () => OpenNotionPage(c.NotionPageId)));
        items.Add(MenuItem("📅  وزّع مواعيد الدورة…", () => ScheduleNotion(c.Groups.ToList(), $"«{c.Name}»")));
        items.Add(MenuItem("🔄  زامن الموضوع الآن", () => SyncNotionTopic(t)));
        return SubMenuOf("📤  نوشن", items.ToArray());
    }

    private ToolStripMenuItem? NotionGroupMenu(LectureGroup g)
    {
        if (NotionTopicOf(g) is not { } t) return null;
        if (g.NotionPageId == null)
            return SubMenuOf("📤  نوشن", MenuItem("🔄  زامن الموضوع (المحاضرة لم تُنشأ هناك بعد)", () => SyncNotionTopic(t)));
        var p = g.Plan ?? new LecturePlan();
        ToolStripMenuItem Check(string text, bool value, Action<LecturePlan> set) =>
            new(text, null, (_, _) => EditPlan(g, set)) { Checked = value, ForeColor = Theme.Text };
        return SubMenuOf("📤  نوشن",
            MenuItem("↗  افتح المحاضرة في نوشن", () => OpenNotionPage(g.NotionPageId)),
            Sep(),
            Check("TASK", p.Task, x => x.Task = !x.Task),
            Check("SEARCH", p.Search, x => x.Search = !x.Search),
            Check("SUMMARY", p.Summary, x => x.Summary = !x.Summary),
            Sep(),
            Check("الحضور: " + NotionSync.Present, p.Attendance == NotionSync.Present, x => x.Attendance = x.Attendance == NotionSync.Present ? null : NotionSync.Present),
            Check("الحضور: " + NotionSync.Absent, p.Attendance == NotionSync.Absent, x => x.Attendance = x.Attendance == NotionSync.Absent ? null : NotionSync.Absent),
            MenuItem(p.Date is { } d ? $"📅  الموعد: {d:yyyy/MM/dd} — غيّره…" : "📅  حدّد الموعد…", () => EditPlanDate(g)),
            Sep(),
            MenuItem("📝  أرسل الملاحظات والعلامات والملخص للمحاضرة", () => SendNotesToNotion(g)));
    }

    // =====================================================================
    //  الخطة
    // =====================================================================

    /// <summary>تعديل الخطة من مسار ← على طول لنوشن (ولو النت فاصل، في المزامنة الجاية).</summary>
    private void EditPlan(LectureGroup g, Action<LecturePlan> change)
    {
        g.Plan ??= new LecturePlan();
        change(g.Plan);
        g.Plan.UpdatedUtc = DateTime.UtcNow;
        NotionStateLoaded.PendingPlan.Add(g.Id);
        MarkDirty();
        RefreshGroup(g);
        RefreshHomePlan();
        if (!_notionBusy) _ = PushPendingPlansAsync(); // لو مشغول: بتتبعت أول ما اللي شغال يخلص
    }

    private void EditPlanDate(LectureGroup g)
    {
        using var kit = new FormKit("موعد المحاضرة", 460);
        kit.Note(Modern.Bidi(g.Name), 26, Theme.Text);
        var has = kit.Row("لها موعد", new ToggleSwitch { Text = "حدّد يومًا", Checked = true });
        var date = kit.Row("اليوم", new DateBox { Value = (g.Plan?.Date ?? DateTime.Today).Date });
        has.CheckedChanged += (_, _) => date.Enabled = has.Checked;
        kit.Note("− / + يومًا بيوم، ومع Shift = أسبوع.");
        kit.Buttons();
        if (SmokeShowDialog(kit, "notion-date")) return;
        if (kit.Show(this) != DialogResult.OK) return;
        DateTime? value = has.Checked ? date.Value.Date : null;
        EditPlan(g, p => p.Date = value);
    }

    /// <summary>«وزّع المواعيد»: من تاريخ، في أيام مذاكرتك، بعدد دقايق في اليوم — والمحاضرات اللي خلصت مابتاخدش موعد.</summary>
    private void ScheduleNotion(List<LectureGroup> lectures, string scope)
    {
        if (lectures.Count == 0) return;
        using var kit = new FormKit("وزّع المواعيد — " + scope, 620);
        var start = kit.Row("من يوم", new DateBox { Value = DateTime.Today });
        var minutes = kit.Row("دقائق في اليوم", new NumberBox { Minimum = 10, Maximum = 600, Increment = 10, Value = Math.Clamp(_settings.DailyGoalMinutes, 10, 600) });
        var days = kit.Row("أيام المذاكرة", new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = true, BackColor = Theme.Panel }, 72);
        var ar = CultureInfo.GetCultureInfo("ar-EG");
        var course = _library.FindCourseOf(lectures[0]);
        var boxes = new List<(DayOfWeek Day, ChipToggle Box)>();
        foreach (var d in new[] { DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            var cb = new ChipToggle(ar.DateTimeFormat.GetDayName(d), course == null || course.IsStudyDay(d)) { Margin = new Padding(0, 0, 6, 6) };
            days.Controls.Add(cb);
            boxes.Add((d, cb));
        }
        var onlyEmpty = kit.Row("المحاضرات التي لها موعد", new ToggleSwitch { Text = "اتركها كما هي (وزّع على التي ليس لها موعد فقط)", Checked = lectures.Any(g => g.Plan?.Date != null) });
        var preview = kit.Note("", 26, Theme.Text);
        void Preview()
        {
            var sel = boxes.Where(x => x.Box.Checked).Select(x => x.Day).ToHashSet();
            var copy = lectures.Select(g => new LectureGroup { Videos = g.Videos, Plan = g.Plan?.Clone() }).ToList();
            var end = NotionSync.Schedule(copy, start.Value, sel, (int)minutes.Value, onlyEmpty.Checked);
            int n = copy.Zip(lectures).Count(x => x.First.Plan?.Date != x.Second.Plan?.Date);
            preview.Text = end is { } e ? $"{n} محاضرة ستأخذ موعدًا — آخرها {e:yyyy/MM/dd} ({ar.DateTimeFormat.GetDayName(e.DayOfWeek)})" : "اختر يومًا واحدًا على الأقل";
        }
        start.ValueChanged += (_, _) => Preview();
        minutes.ValueChanged += (_, _) => Preview();
        onlyEmpty.CheckedChanged += (_, _) => Preview();
        foreach (var (_, b) in boxes) b.CheckedChanged += (_, _) => Preview();
        Preview();
        kit.Buttons("وزّع");
        if (SmokeShowDialog(kit, "notion-schedule")) return;
        if (kit.Show(this) != DialogResult.OK) return;
        var chosen = boxes.Where(x => x.Box.Checked).Select(x => x.Day).ToHashSet();
        if (chosen.Count == 0) return;
        Checkpoint("توزيع المواعيد");
        var before = lectures.ToDictionary(g => g, g => g.Plan?.Date);
        NotionSync.Schedule(lectures, start.Value, chosen, (int)minutes.Value, onlyEmpty.Checked);
        var changed = lectures.Where(g => g.Plan?.Date != before[g]).ToList();
        foreach (var g in changed) NotionStateLoaded.PendingPlan.Add(g.Id);
        MarkDirty();
        foreach (var g in changed) if (_groupNodes.TryGetValue(g, out var node)) UpdateGroupNode(node, g);
        RefreshHomePlan();
        ShowStatus($"📅 {changed.Count} محاضرة أخذت موعدًا", 4);
        var withRows = changed.Where(g => g.NotionPageId != null).ToList();
        if (withRows.Count > 0)
            _ = RunNotionAsync("المواعيد", async (s, ct) => { foreach (var g in withRows) await s.PushPlanAsync(g, ct); }, quiet: false);
    }

    /// <summary>علامات الخطة في الشجرة اتغيرت (بعد سحب أو مزامنة).</summary>
    private void RefreshNotionMarks()
    {
        foreach (var t in NotionTopics)
        {
            RefreshTopicNode(t);
            foreach (var c in _library.CoursesOf(t))
                foreach (var g in c.Groups)
                    if (_groupNodes.TryGetValue(g, out var node)) UpdateGroupNode(node, g);
        }
    }

    private void RefreshTopicNode(Topic t)
    {
        if (_topicNodes.TryGetValue(t, out var tn)) UpdateTopicNode(tn, t);
    }

    /// <summary>جنب المحاضرة في الشجرة: 📅 الموعد (⚠ لو عدّى)، ✔/✖ الحضور، ☑ المهام.</summary>
    private static string PlanMeta(LectureGroup g)
    {
        if (g.Plan is not { } p || p.IsEmpty) return "";
        var parts = new List<string>();
        if (p.Date is { } d)
            parts.Add((d.Date < DateTime.Today && !g.IsCompleted ? "⚠" : "📅") + d.ToString("d/M", CultureInfo.InvariantCulture));
        if (p.Attendance == NotionSync.Present) parts.Add("✔");
        else if (p.Attendance == NotionSync.Absent) parts.Add("✖");
        int tasks = (p.Task ? 1 : 0) + (p.Search ? 1 : 0) + (p.Summary ? 1 : 0);
        if (tasks > 0) parts.Add($"☑{tasks}/3");
        return string.Join(" ", parts);
    }

    private static string PlanTooltip(LectureGroup g)
    {
        if (g.Plan is not { } p || p.IsEmpty) return "";
        static string Box(bool b) => b ? "☑" : "☐";
        return "\n\nنوشن: " + (p.Date is { } d ? $"الموعد {d:yyyy/MM/dd}" : "بلا موعد") +
               (p.Attendance != null ? " · " + p.Attendance : "") +
               $"\n{Box(p.Task)} TASK   {Box(p.Search)} SEARCH   {Box(p.Summary)} SUMMARY";
    }

    /// <summary>للرئيسية: محاضرات موعدها النهارده أو عدّى ولسه ماخلصتش (أو مهامها لسه).</summary>
    private List<(LectureGroup Group, Course Course, bool Overdue, string Left)> NotionDue()
    {
        var today = DateTime.Today;
        var list = new List<(LectureGroup, Course, bool, string)>();
        foreach (var t in NotionTopics)
            foreach (var c in _library.CoursesOf(t))
            {
                if (!InHomeScope(c)) continue;
                foreach (var g in c.Groups)
                {
                    if (g.Plan?.Date is not { } d || d.Date > today) continue;
                    var left = new List<string>();
                    if (!g.IsCompleted) left.Add("المشاهدة");
                    if (!g.Plan.Task) left.Add("TASK");
                    if (!g.Plan.Search) left.Add("SEARCH");
                    if (!g.Plan.Summary) left.Add("SUMMARY");
                    if (left.Count == 0 || g.Plan.Attendance == NotionSync.Absent && g.IsCompleted) continue;
                    // اللي فات عليه أكتر من 3 أسابيع ومفيش فيه حاجة غير المهام: مش «النهارده»
                    if (d.Date < today.AddDays(-21) && g.IsCompleted) continue;
                    list.Add((g, c, d.Date < today, string.Join("، ", left)));
                }
            }
        return list.OrderByDescending(x => x.Item3).ThenBy(x => x.Item1.Plan!.Date).ToList();
    }

    // =====================================================================
    //  المحتوى ← صفحة المحاضرة
    // =====================================================================

    private void SendNotesToNotion(LectureGroup g)
    {
        if (g.NotionPageId == null) { ShowStatus("لم تُنشأ المحاضرة في نوشن بعد — زامن الموضوع أولًا", 4); return; }
        CommitNotes();
        var (blocks, hasSummary, count) = BuildNotionNotes(g);
        if (count == 0)
        {
            Dialogs.Info(this, "لا يوجد ما يُرسل بعد: لا ملاحظات ولا علامات ولا ملخص ولا اختبار على فيديوهات هذه المحاضرة.");
            return;
        }
        _ = RunNotionAsync($"ملاحظات «{g.Name}»", async (s, ct) =>
        {
            await s.SendNotesAsync(g, blocks, hasSummary, ct);
            if (!IsDisposed)
            {
                RefreshGroup(g);
                ShowBanner($"📝 أُرسلت ملاحظات «{g.Name}» إلى نوشن" + (hasSummary ? " — ووُسم SUMMARY ✓" : ""), BannerKind.Success, "افتحها", () => OpenNotionPage(g.NotionPageId), seconds: 8);
            }
        }, quiet: false);
    }

    /// <summary>صندوق «من مسار»: لكل فيديو فيه حاجة — الملاحظات، العلامات بأوقاتها، ملخص Claude، وآخر نتيجة اختبار.</summary>
    private (List<JsonObject> Blocks, bool HasSummary, int Count) BuildNotionNotes(LectureGroup g)
    {
        var blocks = new List<JsonObject>();
        bool hasSummary = false;
        int count = 0;
        var quizzes = JournalEntries.Where(e => e.Kind == JournalEntry.Quiz && e.VideoPath != null).GroupBy(e => e.VideoPath!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(e => e.StartUtc).Last(), StringComparer.OrdinalIgnoreCase);
        foreach (var v in g.Videos)
        {
            var part = new List<JsonObject>();
            if (!string.IsNullOrWhiteSpace(v.Notes)) { part.Add(NotionClient.Paragraph(v.Notes.Trim())); count++; }
            foreach (var b in v.Bookmarks.OrderBy(b => b.TimeMs))
            {
                part.Add(NotionClient.Bullet($"⏱ {TimeFormat.Format(b.TimeMs)}" + (string.IsNullOrWhiteSpace(b.Note) ? "" : " — " + b.Note.Trim())));
                count++;
            }
            string? summary = null;
            try { if (HasLibrary && File.Exists(SummaryPath(v))) summary = File.ReadAllText(SummaryPath(v)); } catch (IOException) { }
            if (!string.IsNullOrWhiteSpace(summary))
            {
                part.Add(NotionClient.Paragraph("✨ ملخص:"));
                foreach (var line in summary.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).Take(60))
                {
                    string text = line.TrimStart('#', ' ', '-', '*', '•').Trim();
                    if (text.Length == 0) continue;
                    part.Add(line.TrimStart().StartsWith('#') ? NotionClient.Heading(text, 3) : NotionClient.Bullet(text));
                }
                hasSummary = true;
                count++;
            }
            if (quizzes.TryGetValue(v.Path, out var q) && q.Total > 0)
            {
                part.Add(NotionClient.Paragraph($"❓ آخر اختبار: {q.Score}/{q.Total} ({q.StartUtc.ToLocalTime():yyyy/MM/dd})"));
                count++;
            }
            if (part.Count == 0) continue;
            blocks.Add(NotionClient.Heading("🎬 " + v.Title, 3));
            blocks.AddRange(part);
        }
        return (blocks, hasSummary, count);
    }
}
