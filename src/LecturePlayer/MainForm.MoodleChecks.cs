using System.Net;
using System.Security.Cryptography;
using System.Text;
using LecturePlayer.Data;
using Microsoft.Win32;

namespace LecturePlayer;

/// <summary>
/// فحص Moodle التلقائي: قراية الردود والدمج، والرحلة كلها قدام موقع Moodle وهمي على الجهاز
/// (دخول بالتوكن، الكورسات، الأقسام، H5P بالطريقة الرسمية بتاعة تطبيق الموبايل، يوتيوب، الإكمال، والتحديث).
/// </summary>
internal sealed partial class MainForm
{
    internal void RunMoodleChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("moodle: " + what); }

        // ---------- الروابط ----------
        Check(Moodle.NormalizeSite("maharatech.gov.eg/course/view.php?id=2288") == "https://maharatech.gov.eg", "normalize host");
        Check(Moodle.NormalizeSite("https://lms.uni.edu/moodle/mod/hvp/view.php?id=5") == "https://lms.uni.edu/moodle", "normalize subfolder site");
        Check(Moodle.NormalizeSite("not a url ::") == null, "normalize junk");
        Check(Moodle.IsActivity("https://maharatech.gov.eg/mod/quiz/view.php?id=17000") && !Moodle.IsActivity("https://maharatech.gov.eg/course/view.php?id=1"), "activity urls");
        Check(Moodle.ParseCourseUrl("https://lms.uni.edu/moodle/course/view.php?id=42") is { Site: "https://lms.uni.edu/moodle", Id: 42 }, "course url");
        Check(new VideoItem { Path = "https://maharatech.gov.eg/mod/quiz/view.php?id=3" } is { IsMoodlePage: true, IsOnline: true, IsDownloadable: false }, "moodle page not downloadable");
        Check(new LectureGroup { SourceUrl = "moodle-section:5" } is { IsMoodleSection: true, IsPlaylist: false }, "moodle section is not a playlist");

        // ---------- يوتيوب جوه H5P (JSON جوه JSON) ----------
        Check(Moodle.YouTubeIdIn("""H5PIntegration = {"contents":{"cid-1":{"jsonContent":"{\"interactiveVideo\":{\"video\":{\"files\":[{\"path\":\"https:\\\/\\\/www.youtube.com\\\/watch?v=dQw4w9WgXcQ\",\"mime\":\"video\\\/YouTube\"}]}}}"}}};""") == "dQw4w9WgXcQ", "youtube in h5p json");
        Check(Moodle.YouTubeIdIn("<iframe src=\"https://www.youtube-nocookie.com/embed/abcdefghijk?rel=0\">") == "abcdefghijk", "youtube embed");
        Check(Moodle.YouTubeIdIn("https://youtu.be/abcdefghijk") == "abcdefghijk" && Moodle.YouTubeIdIn("youtube.com/watch?v=short") == null, "youtu.be / too short");
        Check(Moodle.EmbedUrlIn("""{"templates":[{"id":"main","html":"<iframe width=\"100%\" src=\"https://m.test/mod/hvp/embed.php?id=5&user_id=7&secret=ab%2Bc%3D\" frameborder=\"0\"></iframe>"}],"javascript":""}""")
              == "https://m.test/mod/hvp/embed.php?id=5&user_id=7&secret=ab%2Bc%3D", "embed url from mobile content");

        // ---------- رجوع الدخول (moodlemobile://) ----------
        const string site0 = "https://maharatech.gov.eg";
        string passport = Moodle.NewPassport();
        string Sig(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
        string Cb(string sig, string rest) => "moodlemobile://token=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(sig + ":::" + rest));
        Check(Moodle.ParseSsoCallback(Cb(Sig(site0 + passport), "0123456789abcdef0123456789abcdef:::PRIV123"), site0, passport) is { Token: "0123456789abcdef0123456789abcdef", PrivateToken: "PRIV123" }, "sso callback");
        Check(Moodle.ParseSsoCallback(Cb(Sig(site0 + passport), "0123456789abcdef0123456789abcdef"), site0, passport) is { PrivateToken: null }, "sso without private token");
        Check(Moodle.ParseSsoCallback(Cb(Sig(site0 + "otherpassport"), "0123456789abcdef0123456789abcdef"), site0, passport) == null, "sso with someone else's passport rejected");
        Check(Moodle.NormalizePastedCallback("  \"moodlemobile://token=QUJDREVGR0hJSktMTU5PUFFSU1RVVg==\" ") == "moodlemobile://token=QUJDREVGR0hJSktMTU5PUFFSU1RVVg==" &&
              Moodle.NormalizePastedCallback("QUJDREVGR0hJSktMTU5PUFFSU1RVVg==") == "moodlemobile://token=QUJDREVGR0hJSktMTU5PUFFSU1RVVg==" &&
              Moodle.NormalizePastedCallback("https://maharatech.gov.eg/") == null, "pasted callback");
        Check(Moodle.ParseSsoCallback("moodlemobile://token=%%%", site0, passport) == null && Moodle.ParseSsoCallback("https://x", site0, passport) == null, "sso junk");

        // ---------- الدمج ----------
        string contents = FakeMoodle.Contents("https://m.test", extra: false);
        var sections = Moodle.ParseContents(contents);
        Check(sections.Count == 2 && sections[0].Name == "Overview" && sections[1].Modules.Count == 4, "contents shape: " + string.Join(" | ", sections.Select(x => x.Name + ":" + x.Modules.Count)));
        Check(sections[1].Modules.Single(m => m.ModName == "url").Video == Moodle.WatchUrl("urlVideo123"), "mod_url to youtube");
        var course = new Course { Name = "SQL" };
        sections[0].Modules[0].Video = Moodle.WatchUrl("mtVideo0100");
        var r1 = Moodle.Apply(course, "https://m.test", new MoodleCourse(2288, "SQL", "رامي"), sections, firstImport: true);
        Check(course.SourceUrl == "https://m.test/course/view.php?id=2288" && course.Instructor == "رامي" && course.Groups.Count == 2, "first import course");
        Check(r1 is { AddedItems: 0, MarkedWatched: 1, Videos: 2 } && course.AllVideos.All(v => !v.IsNew), "first import counters: " + r1);
        var quiz = course.AllVideos.Single(v => v.SourceRef == "moodle-cm:103");
        Check(quiz.IsMoodlePage && quiz.Title.StartsWith("❓"), "quiz is a page with icon");
        var unresolved = course.AllVideos.Single(v => v.SourceRef == "moodle-cm:101");
        Check(unresolved.IsMoodlePage && unresolved.Watched, "unresolved h5p stays a page, completion copied");
        unresolved.Notes = "ملاحظتي";
        // التحديث: لقينا فيديو 101، ونشاط جديد اتضاف
        var s2 = Moodle.ParseContents(FakeMoodle.Contents("https://m.test", extra: true));
        foreach (var m in s2.SelectMany(x => x.Modules).Where(m => m.ModName == "hvp")) m.Video = Moodle.WatchUrl($"mtVideo{m.Id:D4}");
        var r2 = Moodle.Apply(course, "https://m.test", new MoodleCourse(2288, "SQL", null), s2, firstImport: false);
        Check(r2.AddedItems == 1 && unresolved.Path == Moodle.WatchUrl("mtVideo0101") && unresolved.Notes == "ملاحظتي" && unresolved.Title == "CH00_VID02", "refresh upgrades page→video keeping notes: " + r2 + " " + unresolved.Title);
        var added = course.AllVideos.Single(v => v.SourceRef == "moodle-cm:105");
        Check(added.IsNew && course.Groups[1].Videos.IndexOf(added) == course.Groups[1].Videos.Count - 1, "new activity flagged and placed in order");

        RunMoodleFlowCheck();
    }

    /// <summary>الرحلة كلها قدام موقع Moodle وهمي (HttpListener على الجهاز).</summary>
    private void RunMoodleFlowCheck()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("moodle flow: " + what); }
        bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(15); } return cond(); }

        using var server = FakeMoodle.Start();
        string site = server.Site;
        var notices = new List<string>();
        bool hadKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\moodlemobile") is { } k0 && Close(k0);
        string? signInLaunch = null;
        SmokeMoodleNotice = t => notices.Add(t);
        SmokeMoodleSignIn = (form, launch) =>
        {
            signInLaunch = launch;
            form.Shown += (_, _) =>
            {
                // اللي المتصفح كان هيعمله: الموقع بيرجّع moodlemobile://token=… و«مسار» التاني بيبعته بالـ pipe
                Check(Registry.CurrentUser.OpenSubKey(@"Software\Classes\moodlemobile\shell\open\command")?.GetValue("") is string cmd && cmd.Contains("%1"), "protocol registered while signing in");
                string pass = System.Text.RegularExpressions.Regex.Match(launch, "passport=([0-9a-f]+)").Groups[1].Value;
                string sig = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(site + pass))).ToLowerInvariant();
                string cb = "moodlemobile://token=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(sig + ":::" + FakeMoodle.Token + ":::PRIV"));
                BeginInvoke(() => MoodleSsoReceived(cb));
            };
        };
        SmokeCoursePicker = (form, picked) => form.Shown += (_, _) => form.BeginInvoke(() => form.DialogResult = DialogResult.OK);
        var accountsBefore = _settings.MoodleAccounts.ToList();
        Course? course = null;
        try
        {
            MoodleImport(siteForTest: site);
            Check(Until(() => !_moodleBusy, 60_000), "import finished");
            Check(notices.Count == 0, "no error notices: " + string.Join(" | ", notices));
            Check(signInLaunch?.StartsWith(site + "/admin/tool/mobile/launch.php?service=moodle_mobile_app&passport=") == true && signInLaunch.EndsWith("&urlscheme=moodlemobile&confirmed=1"), "launch url: " + signInLaunch);
            Check(hadKey || Registry.CurrentUser.OpenSubKey(@"Software\Classes\moodlemobile") == null, "protocol registration removed after sign-in");
            var acc = MoodleAccountFor(site);
            Check(acc is { UserId: 7, SiteName: "مهارة تك" } && acc.Token == FakeMoodle.Token && !acc.TokenProtected.Contains(FakeMoodle.Token), "account saved with encrypted token");

            course = _library.Courses.FirstOrDefault(c => c.SourceUrl == Moodle.CourseUrl(site, 2288));
            Check(course != null, "course imported");
            Check(_library.Places.FirstOrDefault(x => x.Id == course!.PlaceId) is { Name: "رامي محمد" } p && _library.Places.FirstOrDefault(x => x.Id == p.ParentId) is { Name: "مهارة تك" }, "placed under platform ← instructor");
            var vids = course.AllVideos.ToList();
            Check(vids.Count(v => v.IsOnline && !v.IsMoodlePage) == 4, "4 youtube videos: " + string.Join(" | ", vids.Select(v => v.Title + "=" + v.Path)));
            Check(vids.Single(v => v.SourceRef == "moodle-cm:100").Path == Moodle.WatchUrl("mtVideo0100") &&
                  vids.Single(v => v.SourceRef == "moodle-cm:102").Path == Moodle.WatchUrl("mtVideo0102"), "h5p → youtube via the mobile route then the session");
            Check(server.MobileCalls == 1 && server.SessionEmbeds >= 2, $"one official mobile call, the rest by session (mobile {server.MobileCalls}, session {server.SessionEmbeds})");
            Check(vids.Single(v => v.SourceRef == "moodle-cm:101").Watched && vids.Single(v => v.SourceRef == "moodle-cm:103").IsMoodlePage, "completion + quiz page");
            Check(vids.Single(v => v.SourceRef == "moodle-cm:102").IsDownloadable, "imported youtube video is a normal online video");

            // التحديث: بالتوكن المحفوظ (من غير دخول) والجديد بيتضاف
            server.Extra = true;
            signInLaunch = null;
            MoodleImport(course);
            Check(Until(() => !_moodleBusy, 60_000), "refresh finished");
            Check(notices.Count == 0 && signInLaunch == null, "refresh used the saved token: " + string.Join(" | ", notices));
            Check(course.AllVideos.Any(v => v.SourceRef == "moodle-cm:105" && v.IsNew), "refresh adds the new activity");

            // التوكن اتلغى على الموقع → يتشال ونقول للمستخدم
            server.RevokeToken = true;
            MoodleImport(course);
            Check(Until(() => !_moodleBusy, 60_000), "revoked finished");
            Check(MoodleAccountFor(site) == null || signInLaunch != null, "revoked token leads to a new sign-in");
            Console.WriteLine($"SMOKE moodle flow: ok ({course.VideoCount} items)");
        }
        finally
        {
            SmokeMoodleNotice = null;
            SmokeMoodleSignIn = null;
            SmokeCoursePicker = null;
            _settings.MoodleAccounts = accountsBefore;
            if (course != null) _library.Courses.Remove(course);
            foreach (var t in _library.Topics.Where(t => t.Name.Contains("مهارة تك")).ToList())
            {
                _library.Places.RemoveAll(p => p.TopicId == t.Id);
                _library.Topics.Remove(t);
            }
            RebuildTree();
        }
    }

    private static bool Close(RegistryKey k) { k.Dispose(); return true; }

    /// <summary>موقع Moodle وهمي على الجهاز بالدوال اللي «مسار» بيستخدمها.</summary>
    private sealed class FakeMoodle : IDisposable
    {
        public const string Token = "f00dfacef00dfacef00dfacef00dface";
        private readonly HttpListener _listener = new();
        public string Site { get; }
        public volatile bool Extra;
        public volatile bool RevokeToken;
        public int MobileCalls, SessionEmbeds;
        private string? _secret;

        private FakeMoodle(int port)
        {
            Site = $"http://localhost:{port}";
            _listener.Prefixes.Add(Site + "/");
        }

        public static FakeMoodle Start()
        {
            for (int attempt = 0; ; attempt++)
            {
                int port = Random.Shared.Next(41000, 48000);
                var f = new FakeMoodle(port);
                try { f._listener.Start(); }
                catch (HttpListenerException) when (attempt < 10) { continue; }
                new Thread(f.Loop) { IsBackground = true, Name = "fake-moodle" }.Start();
                return f;
            }
        }

        private void Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); } catch { return; }
                try { Handle(ctx); }
                catch (Exception ex) { Console.WriteLine("fake moodle: " + ex.Message); try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
            }
        }

        private static void Send(HttpListenerContext ctx, string body, string type = "application/json")
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentType = type + "; charset=utf-8";
            ctx.Response.OutputStream.Write(bytes);
            ctx.Response.Close();
        }

        private void Handle(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url!.AbsolutePath;
            if (path == "/webservice/rest/server.php")
            {
                string raw = new StreamReader(ctx.Request.InputStream).ReadToEnd();
                var form = System.Web.HttpUtility.ParseQueryString(raw);
                if (form["wstoken"] != Token || RevokeToken) { Send(ctx, """{"exception":"moodle_exception","errorcode":"invalidtoken","message":"Invalid token - token not found"}"""); return; }
                string fn = form["wsfunction"] ?? "";
                switch (fn)
                {
                    case "core_webservice_get_site_info":
                        Send(ctx, $$"""{"sitename":"مهارة تك","username":"u","fullname":"Test User","userid":7,"siteurl":"{{Site}}"}"""); return;
                    case "core_enrol_get_users_courses":
                        Send(ctx, """[{"id":2288,"shortname":"sql","fullname":"لغة الاستعلام المنظمة SQL","visible":1},{"id":1,"fullname":"Front page"}]"""); return;
                    case "core_course_get_courses_by_field":
                        Send(ctx, """{"courses":[{"id":2288,"fullname":"SQL","contacts":[{"id":5,"fullname":"رامي محمد"}]}],"warnings":[]}"""); return;
                    case "core_course_get_contents":
                        Send(ctx, Contents(Site, Extra)); return;
                    case "tool_mobile_get_content":
                        Interlocked.Increment(ref MobileCalls);
                        _secret = "s" + Random.Shared.Next(100000, 999999) + "+/=";
                        string cm = form["args[0][value]"] ?? "0";
                        Send(ctx, $$"""{"templates":[{"id":"main","html":"<div><iframe width=\"100%\" src=\"{{Site}}/mod/hvp/embed.php?id={{cm}}&user_id=7&secret={{Uri.EscapeDataString(_secret)}}\" frameborder=\"0\"></iframe></div>"}],"javascript":"","otherdata":"","files":[]}""");
                        return;
                    default:
                        Send(ctx, $$"""{"exception":"dml_missing_record_exception","errorcode":"invalidrecord","message":"Can't find data record in database table external_functions. ({{fn}})"}"""); return;
                }
            }
            if (path == "/mod/hvp/embed.php")
            {
                string id = ctx.Request.QueryString["id"] ?? "0";
                bool viaSecret = ctx.Request.QueryString["secret"] is { } sec && sec == _secret && ctx.Request.QueryString["user_id"] == "7";
                bool viaSession = ctx.Request.Cookies["MoodleSession"]?.Value == "sess7";
                if (!viaSecret && !viaSession) { Send(ctx, "<html><body>login required <form action=\"/login/index.php\"></form></body></html>", "text/html"); return; }
                if (viaSecret) { _secret = null; ctx.Response.SetCookie(new Cookie("MoodleSession", "sess7", "/")); }
                else Interlocked.Increment(ref SessionEmbeds);
                string vid = $"mtVideo{long.Parse(id):D4}";
                Send(ctx, "<html><head><script>var H5PIntegration = {\"contents\":{\"cid-" + id + "\":{\"jsonContent\":\"{\\\"interactiveVideo\\\":{\\\"video\\\":{\\\"files\\\":[{\\\"path\\\":\\\"https:\\\\\\/\\\\\\/www.youtube.com\\\\\\/watch?v=" + vid + "\\\",\\\"mime\\\":\\\"video\\\\\\/YouTube\\\"}]}}}\"}}};</script></head><body></body></html>", "text/html");
                return;
            }
            Send(ctx, "<html><body>login required</body></html>", "text/html");
        }

        /// <summary>رد core_course_get_contents بشكل مهارة تك (General فيه إعلانات بس → بيتشال).</summary>
        private static string ExtraModule(string site, bool extra) => !extra ? "" :
            ",{\"id\":105,\"name\":\"CH00_VID06_New\",\"modname\":\"hvp\",\"url\":\"" + site + "/mod/hvp/view.php?id=105\",\"uservisible\":true,\"visible\":1}";

        public static string Contents(string site, bool extra) => $$$"""
            [{"id":1,"name":"General","section":0,"uservisible":true,"modules":[
                {"id":99,"name":"Announcements","modname":"forum","url":"{{{site}}}/mod/forum/view.php?id=99","uservisible":true,"visible":1}]},
             {"id":2,"name":"Overview","section":1,"uservisible":true,"modules":[
                {"id":100,"name":"Welcome to T-SQL","modname":"hvp","url":"{{{site}}}/mod/hvp/view.php?id=100","uservisible":true,"visible":1,"completiondata":{"state":0}},
                {"id":98,"name":"<span lang=\"en\" class=\"multilang\">x</span>","modname":"label","url":"{{{site}}}/x","uservisible":true}]},
             {"id":3,"name":"Introduction to SQL Server","section":2,"uservisible":true,"modules":[
                {"id":101,"name":"CH00_VID02","modname":"hvp","url":"{{{site}}}/mod/hvp/view.php?id=101","uservisible":true,"visible":1,"completiondata":{"state":1}},
                {"id":102,"name":"CH00_VID03 &amp; more","modname":"hvp","url":"{{{site}}}/mod/hvp/view.php?id=102","uservisible":true,"visible":1},
                {"id":103,"name":"Section 1 - Interview Questions","modname":"quiz","url":"{{{site}}}/mod/quiz/view.php?id=103","uservisible":true,"visible":1},
                {"id":104,"name":"Extra video","modname":"url","url":"{{{site}}}/mod/url/view.php?id=104","uservisible":true,"visible":1,
                 "contents":[{"type":"url","filename":"x","fileurl":"https://www.youtube.com/watch?v=urlVideo123"}]}{{{ExtraModule(site, extra)}}}]},
             {"id":4,"name":"Hidden","section":3,"uservisible":false,"modules":[{"id":200,"name":"x","modname":"hvp","url":"{{{site}}}/mod/hvp/view.php?id=200"}]}]
            """;

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }
}
