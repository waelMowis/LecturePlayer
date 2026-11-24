using System.IO.Compression;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// كورسات Moodle (مهارة تك، ITI، الجامعات…):
/// الدخول في متصفحك العادي بنفس طريقة تطبيق Moodle للموبايل (فجوجل ومايكروسوفت شغالين)، والموقع بيرجّع لـ «مسار» توكن،
/// وبعدها «مسار» بيقرا كورساتك وأقسامها وأنشطتها وإكمالك. فيديوهات يوتيوب (زي مهارة تك) بتبقى فيديوهات عادية في «مسار»
/// (تشغيل، سرعة، معاينة، ترجمة، تنزيل…)، والأنشطة التانية (اختبارات، استبيانات…) بتتفتح في المتصفح.
/// </summary>
internal sealed partial class MainForm
{
    private TaskCompletionSource<string>? _moodleSso;
    private bool _moodleBusy;

    /// <summary>من النسخة التانية اللي المتصفح فتحها برابط moodlemobile://.</summary>
    internal void MoodleSsoReceived(string url)
    {
        if (_moodleSso?.TrySetResult(url) == true)
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }
        else Log.Info("moodle sso: callback with no pending sign-in");
    }

    private static string SiteLabel(MoodleAccount? a, string site) =>
        !string.IsNullOrWhiteSpace(a?.SiteName) ? a!.SiteName!.Trim() : new Uri(site).Host.Replace("www.", "");

    private MoodleAccount? MoodleAccountFor(string site) =>
        _settings.MoodleAccounts.FirstOrDefault(a => string.Equals(a.Site, site, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Moodle ← كورساتي: يدخل (لو محتاج)، يعرض كورساتك تختار منها، ويستوردها أو يحدّثها.
    /// refresh = كورس موجود: بيحدّث أقسامه وأنشطته وإكمالك بس.
    /// </summary>
    private async void MoodleImport(Course? refresh = null, string? siteForTest = null, ImportTarget? target = null)
    {
        if (_moodleBusy) { ShowStatus("فيه استيراد من Moodle شغال دلوقتي…"); return; }
        string? site = refresh != null ? Moodle.ParseCourseUrl(refresh.SourceUrl)?.Site : siteForTest ?? AskMoodleSite();
        if (site == null) return;
        _moodleBusy = true;
        try
        {
            var account = await EnsureMoodleAccountAsync(site);
            if (account == null) return;
            using var client = new MoodleClient(account.Site, account.Token);
            string platform = SiteLabel(account, account.Site);
            using var cts = new CancellationTokenSource();

            ShowBanner($"🎓 {platform}: بجيب كورساتك…", BannerKind.Info, "إلغاء", cts.Cancel, seconds: 0, key: "moodle");
            var courses = Moodle.ParseCourses(await client.CallAsync("core_enrol_get_users_courses",
                new[] { MoodleClient.Arg("userid", account.UserId) }, cts.Token));
            await FillMoodleInstructorsAsync(client, courses, cts.Token);

            List<MoodleCourse> chosen;
            Topic? topic = null;
            string? topicName = null;
            if (refresh != null)
            {
                long id = Moodle.ParseCourseUrl(refresh.SourceUrl)!.Value.Id;
                chosen = courses.Where(c => c.Id == id).ToList();
                if (chosen.Count == 0) { HideBanner("moodle"); MoodleNotice(false, $"«{refresh.Name}» مش لاقيه في الكورسات المسجّل فيها على {platform}."); return; }
            }
            else
            {
                if (courses.Count == 0) { HideBanner("moodle"); MoodleNotice(false, $"مفيش كورسات مسجّل فيها على {platform} للحساب ده."); return; }
                HideBanner("moodle");
                string Url(long id) => Moodle.CourseUrl(account.Site, id);
                var pick = PickPlatformCourses(this, platform, platform,
                    courses.Select(c => (c.Id, c.Title, c.Instructor, _library.Courses.Any(x => x.SourceUrl == Url(c.Id)))).ToList(),
                    "كورسات " + platform, "والكورس جواه أقسام الموقع كمحاضرات، وفيديوهات يوتيوب كفيديوهات، والاختبارات والأنشطة التانية بتتفتح في المتصفح.", target);
                if (pick == null) return;
                chosen = courses.Where(c => pick.Value.Ids.Contains(c.Id)).ToList();
                topicName = pick.Value.Topic;
                topic = _library.Topics.FirstOrDefault(t => string.Equals(t.Name, topicName, StringComparison.OrdinalIgnoreCase));
            }

            var results = new List<(Course Course, Moodle.SyncResult Sync, bool New)>();
            Checkpoint(refresh != null ? "تحديث من " + platform : "استيراد من " + platform);
            for (int i = 0; i < chosen.Count; i++)
            {
                var mc = chosen[i];
                ShowBanner($"🎓 ({i + 1}/{chosen.Count}) بقرا «{mc.Title}»…", BannerKind.Info, "إلغاء", cts.Cancel, seconds: 0, key: "moodle");
                var sections = Moodle.ParseContents(await client.CallAsync("core_course_get_contents",
                    new[] { MoodleClient.Arg("courseid", mc.Id) }, cts.Token));
                await ResolveMoodleVideosAsync(client, mc, sections, (done, total) =>
                    ShowBanner($"🎓 ({i + 1}/{chosen.Count}) «{mc.Title}»: بدوّر على الفيديوهات {done}/{total}…", BannerKind.Info, "إلغاء", cts.Cancel, seconds: 0, key: "moodle"),
                    cts.Token);

                string url = Moodle.CourseUrl(account.Site, mc.Id);
                var course = refresh ?? _library.Courses.FirstOrDefault(c => c.SourceUrl == url);
                bool isNew = course == null;
                if (course == null)
                {
                    course = new Course { Name = mc.Title };
                    topic = PlaceImportedCourse(course, target, topic, topicName ?? "كورسات " + platform, platform, mc.Instructor);
                    _library.Courses.Add(course);
                }
                results.Add((course, Moodle.Apply(course, account.Site, mc, sections, firstImport: isNew), isNew));
            }

            HideBanner("moodle");
            if (results.Count == 0) return;
            MarkDirty();
            RebuildTree();
            SelectItem(results[0].Course);
            int videos = results.Sum(r => r.Sync.Videos), items = results.Sum(r => r.Course.VideoCount);
            string vidNote = videos == 0 && items > 0
                ? " — ملحوظة: مالقيتش روابط الفيديوهات، فهتتفتح في المتصفح (ابعتلنا لوج البرنامج نصلّحها)"
                : $" — منهم {videos} فيديو بيشتغلوا هنا";
            ShowBanner(results.Any(r => r.New)
                    ? $"🎓 اتضاف {results.Count(r => r.New)} كورس من {platform} ({items} عنصر){vidNote}"
                    : $"🎓 اتحدّث من {platform}: {results.Sum(r => r.Sync.AddedItems)} عنصر جديد، و{results.Sum(r => r.Sync.MarkedWatched)} اتعلّم «اتشاف» من تقدّمك هناك{vidNote}",
                videos == 0 && items > 0 ? BannerKind.Warning : BannerKind.Success, seconds: 12);
        }
        catch (OperationCanceledException) { HideBanner("moodle"); ShowStatus("اتلغى الاستيراد"); }
        catch (MoodleException ex) when (ex.TokenInvalid)
        {
            HideBanner("moodle");
            _settings.MoodleAccounts.RemoveAll(a => string.Equals(a.Site, site, StringComparison.OrdinalIgnoreCase));
            SaveSettings();
            MoodleNotice(false, "الدخول على الموقع انتهى — جرّب تاني وهيطلب منك تدخل من المتصفح.");
        }
        catch (Exception ex)
        {
            Log.Error("moodle import", ex);
            HideBanner("moodle");
            MoodleNotice(true, "الاستيراد من Moodle مانجحش:\n" + ex.Message);
        }
        finally { _moodleBusy = false; }
    }

    /// <summary>رسالة للمستخدم (وفي الفحص الآلي بتتسجل بدل ما تفتح شباك يستنى).</summary>
    private void MoodleNotice(bool error, string text)
    {
        if (SmokeMoodleNotice != null) { SmokeMoodleNotice(text); return; }
        if (error) Dialogs.Error(this, text); else Dialogs.Info(this, text);
    }

    internal static Action<string>? SmokeMoodleNotice;

    private string? AskMoodleSite()
    {
        using var kit = new FormKit("Moodle / مهارة تك — كورساتي", 560);
        kit.Header("🎓  الموقع");
        var box = kit.Row("الموقع:", new TextBox { Text = _settings.MoodleLastSite ?? Moodle.DefaultSite });
        kit.Note("مهارة تك، أو أي موقع Moodle (ITI، جامعتك…). ممكن تحط رابط أي صفحة من الموقع.", 24, Theme.Muted);
        bool known = _settings.MoodleAccounts.Count > 0;
        kit.Note("الدخول بيحصل في متصفحك العادي (جوجل/مايكروسوفت/باسورد) — «مسار» مابيشوفش بيانات دخولك.\n" +
                 "لما المتصفح يسألك «تفتح الرابط ده بـ «مسار»؟» دوس فتح." +
                 (known ? "\nالمواقع اللي دخلتها قبل كده مش هتطلب دخول تاني." : ""), known ? 62 : 44, Theme.Muted);
        kit.Buttons("متابعة", "إلغاء");
        if (kit.Show(this) != DialogResult.OK) return null;
        string? site = Moodle.NormalizeSite(box.Text);
        if (site == null) { MoodleNotice(false, "اكتب عنوان الموقع، زي maharatech.gov.eg"); return null; }
        _settings.MoodleLastSite = site;
        SaveSettings();
        return site;
    }

    /// <summary>الحساب المحفوظ لو التوكن لسه شغال، وإلا دخول جديد من المتصفح.</summary>
    private async Task<MoodleAccount?> EnsureMoodleAccountAsync(string site)
    {
        var saved = MoodleAccountFor(site);
        if (saved != null && saved.Token.Length > 0)
        {
            try
            {
                using var c = new MoodleClient(saved.Site, saved.Token);
                var info = Moodle.ParseSiteInfo(await c.CallAsync("core_webservice_get_site_info", null, CancellationToken.None));
                if (info.UserId > 0) { saved.SiteName = info.SiteName ?? saved.SiteName; return saved; }
            }
            catch (MoodleException ex) when (ex.TokenInvalid) { Log.Info("moodle: saved token expired"); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                MoodleNotice(true, "مقدرتش أوصل للموقع — اتأكد من النت.\n" + ex.Message);
                return null;
            }
        }
        return await MoodleSignInAsync(site);
    }

    /// <summary>
    /// الدخول بطريقة تطبيق Moodle: صفحة launch.php في متصفحك → بتدخل عادي → الموقع بيفتح moodlemobile://token=… → «مسار» بياخده.
    /// </summary>
    private async Task<MoodleAccount?> MoodleSignInAsync(string site)
    {
        string passport = Moodle.NewPassport();
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        _moodleSso = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var protocol = UrlProtocol.Register(Moodle.Scheme, exe);
        string launch = Moodle.LaunchUrl(site, passport);

        using var kit = new FormKit("الدخول على " + new Uri(site).Host, 560);
        kit.Header("🔐  كمّل الدخول في المتصفح");
        kit.Note("اتفتحت صفحة الموقع في متصفحك:\n" +
                 "1) لو مش داخل، ادخل عادي (جوجل/مايكروسوفت/باسورد).\n" +
                 "2) المتصفح هيسألك «تفتح الرابط ده بتطبيق؟» ← اختار «مسار» ودوس فتح.\n" +
                 "الشباك ده هيقفل لوحده أول ما الدخول يوصل.", 104);
        var status = kit.Note("مستني…", 24, Theme.Muted);
        kit.Header("✋  لو المتصفح ماسألكش");
        kit.Note("في الصفحة اللي اتفتحت، كليك يمين على رابط «Click here to launch the app» (أو «اضغط هنا…») ← «نسخ الرابط»، والزقه هنا:", 40, Theme.Muted);
        var paste = kit.Row("الرابط:", new TextBox());
        paste.TextChanged += (_, _) =>
        {
            if (Moodle.NormalizePastedCallback(paste.Text) is { } cb) _moodleSso?.TrySetResult(cb);
            else if (paste.Text.Length > 0) status.Text = "ده مش رابط الدخول — لازم يبدأ بـ moodlemobile://";
        };
        kit.Buttons("خلاص", "إلغاء", ("افتح المتصفح تاني", () => OpenInBrowser(launch)));
        kit.Ok!.Visible = false;
        string? callback = null;
        kit.Form.Shown += async (_, _) =>
        {
            if (SmokeMoodleSignIn == null) OpenInBrowser(launch);
            var done = await Task.WhenAny(_moodleSso.Task, Task.Delay(TimeSpan.FromMinutes(15)));
            if (kit.Form.IsDisposed) return;
            if (done == _moodleSso.Task) { callback = _moodleSso.Task.Result; kit.Form.DialogResult = DialogResult.OK; }
            else status.Text = "الوقت خلص — دوس «افتح المتصفح تاني» أو إلغاء.";
        };
        SmokeMoodleSignIn?.Invoke(kit.Form, launch);
        var result = kit.Show(this);
        _moodleSso = null;
        if (result != DialogResult.OK || callback == null) return null;

        var tokens = Moodle.ParseSsoCallback(callback, site, passport);
        if (tokens == null)
        {
            Log.Info("moodle sso: callback rejected (" + callback.Length + " chars)");
            MoodleNotice(true, "الرد اللي رجع من الموقع مش مطابق للطلب ده (ممكن يكون من دخول قديم). جرّب تاني.");
            return null;
        }
        using var client = new MoodleClient(site, tokens.Value.Token);
        var info = Moodle.ParseSiteInfo(await client.CallAsync("core_webservice_get_site_info", null, CancellationToken.None));
        var account = new MoodleAccount
        {
            Site = site, SiteName = info.SiteName, UserId = info.UserId, UserName = info.Name,
            Token = tokens.Value.Token, PrivateToken = tokens.Value.PrivateToken ?? ""
        };
        _settings.MoodleAccounts.RemoveAll(a => string.Equals(a.Site, site, StringComparison.OrdinalIgnoreCase));
        _settings.MoodleAccounts.Add(account);
        SaveSettings();
        Log.Info($"moodle: signed in to {site} (user {info.UserId})");
        ShowStatus($"أهلًا {info.Name} — دخلت على {SiteLabel(account, site)} ✓", 4);
        return account;
    }

    internal static Action<Form, string>? SmokeMoodleSignIn;

    private static async Task FillMoodleInstructorsAsync(MoodleClient client, List<MoodleCourse> courses, CancellationToken ct)
    {
        if (courses.Count == 0) return;
        try
        {
            var map = Moodle.ParseContacts(await client.CallAsync("core_course_get_courses_by_field", new[]
            {
                MoodleClient.Arg("field", "ids"), MoodleClient.Arg("value", string.Join(",", courses.Select(c => c.Id)))
            }, ct));
            for (int i = 0; i < courses.Count; i++)
                if (map.TryGetValue(courses[i].Id, out string? n)) courses[i] = courses[i] with { Instructor = n };
        }
        catch (MoodleException ex) { Log.Info("moodle contacts: " + ex.Message); } // المدرّس مش أساسي
    }

    /// <summary>
    /// الأنشطة اللي ممكن يكون جواها فيديو يوتيوب (H5P/صفحة): بندوّر على الرابط بأكتر من طريقة رسمية،
    /// وبنكتب في اللوج إيه اللي نجح وإيه اللي لأ عشان لو موقع مختلف نصلّحه من أول مرة.
    /// </summary>
    private static async Task ResolveMoodleVideosAsync(MoodleClient client, MoodleCourse course, List<MoodleSection> sections,
        Action<int, int> progress, CancellationToken ct)
    {
        var todo = sections.SelectMany(s => s.Modules)
            .Where(m => m.Video == null && m.ModName is "hvp" or "h5pactivity" or "page" or "url" or "videotime" or "resource")
            .ToList();
        if (todo.Count == 0) return;
        var stats = new Dictionary<string, int>();
        var errors = new Dictionary<string, string>();
        void Stat(string k) { lock (stats) stats[k] = stats.GetValueOrDefault(k) + 1; }
        void Err(string k, string e) { lock (errors) errors.TryAdd(k, e); }

        // H5P الجديد (mod_h5pactivity): الملف نفسه فيه content.json وفيه رابط يوتيوب
        Dictionary<long, string>? h5pPackages = null;
        if (todo.Any(m => m.ModName == "h5pactivity"))
        {
            try
            {
                string json = await client.CallAsync("mod_h5pactivity_get_h5pactivities_by_courses",
                    new[] { MoodleClient.Arg("courseids[0]", course.Id) }, ct);
                h5pPackages = ParseH5pPackages(json);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Err("h5pactivity-list", ex.Message); }
        }

        int done = 0;
        var hvp = new HvpState();
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(todo.Select(async m =>
        {
            await gate.WaitAsync(ct);
            try
            {
                string? id = null;
                switch (m.ModName)
                {
                    case "hvp":
                        id = await YouTubeInHvpAsync(client, m.Id, hvp, Stat, Err, ct);
                        break;
                    case "h5pactivity" when h5pPackages != null && h5pPackages.TryGetValue(m.Id, out string? pkg):
                        id = await YouTubeInH5pPackageAsync(client, pkg, ct);
                        Stat(id != null ? "h5pactivity:ok" : "h5pactivity:no-youtube");
                        break;
                }
                // آخر محاولة: الصفحة نفسها (بيشتغل لو الموقع بيعرضها من غير دخول، أو للصفحات العامة)
                if (id == null && m.ModName is "page" or "url" or "videotime")
                {
                    id = Moodle.YouTubeIdIn(await client.GetPageAsync(m.Url, ct));
                    if (id != null) Stat(m.ModName + "-page:ok");
                }
                if (id != null) m.Video = Moodle.WatchUrl(id);
                else Stat(m.ModName + ":not-found");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Err(m.ModName, ex.Message); Stat(m.ModName + ":error"); }
            finally
            {
                gate.Release();
                int d = Interlocked.Increment(ref done);
                if (d % 4 == 0 || d == todo.Count) progress(d, todo.Count);
            }
        }));
        Log.Info($"moodle videos «{course.Title}» ({course.Id}): {todo.Count} candidates → " +
                 string.Join(", ", stats.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)) +
                 (errors.Count > 0 ? " | errors: " + string.Join(" ; ", errors.Select(e => e.Key + ": " + e.Value)) : ""));
    }

    /// <summary>حالة H5P القديم (mod_hvp) أثناء الاستيراد: هل بقى معانا جلسة على الموقع، والقفل للطريقة الرسمية.</summary>
    private sealed class HvpState
    {
        public volatile bool HasSession;
        public volatile bool MobileFailed;
        public readonly SemaphoreSlim Mobile = new(1);
    }

    /// <summary>
    /// mod_hvp: نفس طريقة تطبيق Moodle الرسمي — tool_mobile_get_content بيرجّع صفحة embed.php برمز دخول مؤقت،
    /// والصفحة دي فيها إعدادات H5P ومنها رابط يوتيوب. أول مرة بتعمل جلسة على الموقع، فالباقي بنقراه بالجلسة على طول.
    /// (الرمز المؤقت واحد لكل مستخدم، فالطريقة دي بتمشي واحدة واحدة.)
    /// </summary>
    private static async Task<string?> YouTubeInHvpAsync(MoodleClient client, long cmid, HvpState st,
        Action<string> stat, Action<string, string> err, CancellationToken ct)
    {
        string direct = client.Site + "/mod/hvp/embed.php?id=" + cmid;
        if (st.HasSession && Moodle.YouTubeIdIn(await client.GetPageAsync(direct, ct)) is { } fast) { stat("hvp-session:ok"); return fast; }
        if (st.MobileFailed) return null;
        await st.Mobile.WaitAsync(ct);
        try
        {
            if (st.HasSession && Moodle.YouTubeIdIn(await client.GetPageAsync(direct, ct)) is { } again) { stat("hvp-session:ok"); return again; }
            string json = await client.CallAsync("tool_mobile_get_content", new[]
            {
                MoodleClient.Arg("component", "mod_hvp"), MoodleClient.Arg("method", "mobile_course_view"),
                MoodleClient.Arg("args[0][name]", "cmid"), MoodleClient.Arg("args[0][value]", cmid)
            }, ct);
            string? embed = Moodle.EmbedUrlIn(json);
            if (embed == null) { stat("hvp-mobile:no-iframe"); err("hvp-mobile", json.Length > 240 ? json[..240] : json); return null; }
            string? html = await client.GetPageAsync(embed, ct);
            string? id = Moodle.YouTubeIdIn(html);
            if (html != null && html.Contains("H5PIntegration", StringComparison.Ordinal)) st.HasSession = true;
            stat(id != null ? "hvp-mobile:ok" : html == null ? "hvp-mobile:http-error" : "hvp-mobile:no-youtube");
            return id;
        }
        catch (MoodleException ex)
        {
            err("hvp-mobile", ex.Message);
            stat("hvp-mobile:error");
            if (ex.Message.Contains("servicenotavailable", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("invalidparameter", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("accessexception", StringComparison.OrdinalIgnoreCase)) st.MobileFailed = true;
            return null;
        }
        finally { st.Mobile.Release(); }
    }

    private static Dictionary<long, string> ParseH5pPackages(string json)
    {
        var map = new Dictionary<long, string>();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("h5pactivities", out var list)) return map;
        foreach (var a in list.EnumerateArray())
        {
            if (!a.TryGetProperty("coursemodule", out var cm) || !a.TryGetProperty("package", out var pk) || pk.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
            foreach (var f in pk.EnumerateArray())
                if (f.TryGetProperty("fileurl", out var u) && u.GetString() is { } url) { map[cm.GetInt64()] = url; break; }
        }
        return map;
    }

    private static async Task<string?> YouTubeInH5pPackageAsync(MoodleClient client, string fileUrl, CancellationToken ct)
    {
        string url = fileUrl + (fileUrl.Contains('?') ? "&" : "?") + "token=" + Uri.EscapeDataString(client.Token);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        await using var stream = await http.GetStreamAsync(url, ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("content/content.json");
        if (entry == null) return null;
        using var r = new StreamReader(entry.Open());
        return Moodle.YouTubeIdIn(await r.ReadToEndAsync(ct));
    }

    private void MoodleSignOut()
    {
        if (_settings.MoodleAccounts.Count == 0) { ShowStatus("مفيش حسابات Moodle محفوظة على الجهاز ده"); return; }
        string sites = string.Join("، ", _settings.MoodleAccounts.Select(a => SiteLabel(a, a.Site)));
        if (!Dialogs.Confirm(this, $"تشيل دخولك المحفوظ على: {sites}؟\nالكورسات بتفضل في مكتبتك، والتحديث الجاي هيطلب منك تدخل تاني.")) return;
        _settings.MoodleAccounts.Clear();
        SaveSettings();
        ShowStatus("اتشال الدخول المحفوظ ✓", 4);
    }

}
