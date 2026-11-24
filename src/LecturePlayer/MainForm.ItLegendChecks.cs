using System.Net;
using System.Text;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// فحص IT Legend التلقائي: قراية ردود الموقع (بنفس شكل جلسة حقيقية)، والدمج، والاستيراد من الشباك (توكن الصفحة)،
/// واختيار الدرس من منهج الصفحة (حتى لو اسمه متكرر في قسم تاني) ومتابعة اختيارك من الصفحة — قدام موقع وهمي على الجهاز.
/// </summary>
internal sealed partial class MainForm
{
    private const string ItlCourse = "36eee4a7-02df-4b56-b424-8b2d6b0dc346";

    internal void RunItLegendChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("itlegend: " + what); }

        // ---------- الروابط ----------
        Check(ItLegend.CourseIdOf("https://itlegend.net/ar/learn-path/course-player/" + ItlCourse) == ItlCourse, "course url");
        Check(ItLegend.CourseIdOf("https://evil.net/ar/learn-path/course-player/" + ItlCourse) == null, "other host is not itlegend");
        var mods = ItLegend.ParseNodes(FakeItLegend.Nodes(extra: false));
        Check(mods.Count == 2 && mods[0].Title == "Date And Time" && mods[0].Nodes.Count == 3 && mods[1].Nodes.Count == 2, "nodes shape: " + string.Join(" | ", mods.Select(m => m.Title + ":" + m.Nodes.Count)));
        Check(mods[0].Nodes[0] is { Title: "intro", Type: 0, DurationMs: 61_000, Done: true } && mods[0].Nodes[1].DurationMs == 17 * 60_000 + 22_000, "node fields + duration");
        Check(mods[0].Nodes[2] is { Type: 1, Done: true }, "passed exam counts as done");
        string lesson = ItLegend.LessonUrl(ItlCourse, mods[1], mods[1].Nodes[0], null);
        Check(ItLegend.ParseLesson(lesson) is { CourseId: ItlCourse, Title: "intro", ModuleTitle: "Data Structures", ModuleOrder: 2, NextModule: null } l && l.NodeId == mods[1].Nodes[0].Id, "lesson url roundtrip: " + lesson);
        Check(new VideoItem { Path = lesson } is { IsItLegend: true, IsWebLesson: true, IsOnline: true, IsDownloadable: false }, "itlegend lesson flags");
        Check(new LectureGroup { SourceUrl = "itl-module:x" } is { IsItLegendSection: true, IsPlaylist: false }, "itlegend module is not a playlist");
        var src = ItLegend.ParseCourse(FakeItLegend.Player());
        Check(src is { Id: ItlCourse, Title: "C# oop", Instructor: "Ali Shahin", Progress: 98 }, "course: " + src);
        Check(ItLegend.ErrorOf("""{"success":false,"message":"Not enrolled"}""") == "Not enrolled" && ItLegend.ErrorOf("""{"success":true}""") == null, "error parsing");

        // ---------- الدمج ----------
        var course = new Course { Name = "C# oop" };
        var r1 = ItLegend.Apply(course, src!, mods, firstImport: true);
        Check(course.Groups.Count == 2 && course.VideoCount == 5 && r1.MarkedWatched == 4 && course.AllVideos.All(v => !v.IsNew), "first import: " + r1);
        Check(course.Groups[0].Videos[2].Title.StartsWith("❓") && course.Groups[1].Videos[1].Title.StartsWith("📄"), "exam/review icons");
        Check(ItLegend.ParseLesson(course.Groups[0].Videos[0].Path)?.NextModule == "Data Structures", "lesson knows the next module");
        var first = course.Groups[0].Videos[0];
        first.Notes = "ملاحظتي";
        var r2 = ItLegend.Apply(course, src!, ItLegend.ParseNodes(FakeItLegend.Nodes(extra: true)), firstImport: false);
        Check(r2.AddedItems == 1 && ReferenceEquals(course.Groups[0].Videos[0], first) && first.Notes == "ملاحظتي", "refresh keeps items: " + r2);
        Check(course.Groups[0].Videos[2] is { IsNew: true } nv && nv.Title == "1.5-New lesson", "new lesson in order");
    }

    /// <summary>الشباك + المشغّل قدام موقع IT Legend وهمي (من جوه فحص المشغّل عشان إطار المشغّل متجهز).</summary>
    private void RunItLegendFlowCheck(Func<Func<bool>, int, bool> until)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("itlegend flow: " + what); }
        using var server = FakeItLegend.Start();
        ItLegendClient.TestApi = server.Site + "/api";
        ItLegend.TestHost = server.Host;
        Course? course = null;
        try
        {
            // 1) الشباك: الصفحة بتطلب من الـ API بتوكنها ← «مسار» بياخده ويقرا المنهج لوحده (وضع التحديث)
            using (var form = new ItLegendForm(ItlCourse, ItLegend.CourseUrl(ItlCourse)))
            {
                form.Show(this);
                Check(until(() => form.Result != null || form.IsDisposed, 30_000), "window imported the course");
                var res = form.Result;
                Check(res is { } rr && rr.Course.Title == "C# oop" && rr.Modules.Count == 2, "window result");
                Check(server.AuthSeen, "api calls carried the page's bearer");
                ApplyItLegend(res!.Value.Course, res.Value.Modules, null, topicForTest: "كورسات IT Legend");
            }
            course = _library.Courses.FirstOrDefault(c => ItLegend.CourseIdOf(c.SourceUrl) == ItlCourse);
            Check(course is { VideoCount: 5 } && _library.Places.FirstOrDefault(p => p.Id == course.PlaceId) is { Name: "Ali Shahin" }, "course placed");

            // 2) التشغيل: «intro» بتاع القسم التاني (فيه «intro» في القسم الأول كمان، والقسم التاني مقفول)
            var g2 = course!.Groups[1];
            var target = g2.Videos[0];
            string node = ItLegend.ParseLesson(target.Path)!.NodeId;
            target.LastPositionMs = 1500;
            PlayVideo(g2, target);
            Check(until(() => server.Selected.Contains(node), 25_000), "selected the right lesson in the page: " + string.Join(",", server.Selected));
            Check(until(() => _udemyPane is { HoldPending: false, State: { HasVideo: true, Playing: true } }, 20_000), "lesson plays after selection: " + _udemyPane?.State);
            Check(UdemyActive && _currentVideo == target, "masar follows the selected lesson");
            TogglePlay();
            Check(until(() => _udemyPane!.State is { Paused: true }, 5000), "pause from masar");
            TogglePlay();

            // 3) نفس الكورس، درس تاني: من غير تحميل الصفحة
            var g1 = course.Groups[0];
            var second = g1.Videos[1];
            string node2 = ItLegend.ParseLesson(second.Path)!.NodeId;
            int loads = server.PageLoads;
            PlayVideo(g1, second);
            Check(until(() => server.Selected.Contains(node2), 20_000), "second lesson selected");
            Check(server.PageLoads == loads, "same course: no page reload");

            // 4) اختيار من منهج الصفحة نفسها ← «مسار» بيمشي معاك
            until(() => _udemyPane is { HoldPending: false }, 5000);
            var other = g1.Videos[0];
            string node3 = ItLegend.ParseLesson(other.Path)!.NodeId;
            _udemyPane!.Web.CoreWebView2.ExecuteScriptAsync($"pick('{node3}')");
            Check(until(() => _currentVideo == other, 10_000), "follows a lesson picked in the page: " + _currentVideo?.Title);
            Console.WriteLine("SMOKE itlegend flow: ok");
        }
        finally
        {
            StopPlaybackCore(clearCurrent: true);
            if (course != null) _library.Courses.Remove(course);
            foreach (var t in _library.Topics.Where(t => t.Name == "كورسات IT Legend").ToList())
            {
                _library.Places.RemoveAll(p => p.TopicId == t.Id);
                _library.Topics.Remove(t);
            }
            RebuildTree();
            ItLegendClient.TestApi = null;
            ItLegend.TestHost = null;
        }
    }

    /// <summary>موقع IT Legend وهمي: صفحة كورس بمنهج (أقسام بتتفتح وتتقفل) ومشغّل في إطار من موقع تاني، وواجهة api بالتوكن.</summary>
    private sealed class FakeItLegend : IDisposable
    {
        private const string Bearer = "Bearer test.jwt.token-for-masar";
        private readonly HttpListener _listener = new();
        public string Host { get; }
        public string Site => "http://" + Host;
        public volatile bool AuthSeen;
        public int PageLoads;
        public readonly List<string> Selected = new();

        private FakeItLegend(int port)
        {
            Host = "localhost:" + port;
            _listener.Prefixes.Add(Site + "/");
        }

        public static FakeItLegend Start()
        {
            for (int a = 0; ; a++)
            {
                var f = new FakeItLegend(Random.Shared.Next(52000, 56000));
                try { f._listener.Start(); }
                catch (HttpListenerException) when (a < 10) { continue; }
                new Thread(f.Loop) { IsBackground = true }.Start();
                return f;
            }
        }

        public static string Player() => $$"""{"success":true,"data":{"courseId":"{{ItlCourse}}","titleAr":"C# oop ","titleEn":"C# oop ","instructorArName":"Ali Shahin","progressPercentage":98,"completedCourse":true},"message":"ok","errorCode":null,"errors":[]}""";

        private static string N(string id, string title, int type, int order, string? dur, bool? watched, bool? passed) =>
            $$"""{"nodeId":"{{id}}","titleAr":"{{title}}","titleEn":"{{title}}","type":{{type}},"displayOrder":{{order}},"contentId":"c-{{id}}","isFree":false,"duration":{{(dur == null ? "null" : "\"" + dur + "\"")}},"isWatched":{{(watched == null ? "null" : watched.Value ? "true" : "false")}},"isPassed":{{(passed == null ? "null" : passed.Value ? "true" : "false")}},"isAccessible":true}""";

        public const string M1N1 = "f762e2cc-5408-432d-86e5-a295e0c5408c", M1N2 = "85ea30b0-5bba-41cc-852e-520478c70b84", M1EX = "168437d1-1b8e-4034-9e73-40dae24fcdf1",
            M2N1 = "9011140f-79bd-471e-8fdb-9d30b85c7502", M2RV = "868792b2-14ee-4baf-a76a-50cbc847a753", NEW = "aaaaaaaa-1111-2222-3333-444444444444";

        public static string Nodes(bool extra) => "{\"success\":true,\"data\":[" +
            "{\"moduleId\":\"5beb9f34-b6a4-4d06-a01d-004b6fac8afd\",\"moduleTitleAr\":\"Date And Time\",\"moduleOrder\":1,\"nodes\":[" +
                N(M1N1, "intro", 0, 1, "00:01:01", true, null) + "," + N(M1N2, "1-Declare Datetime", 0, 2, "00:17:22", true, null) + "," +
                (extra ? N(NEW, "1.5-New lesson", 0, 3, "00:05:00", false, null) + "," : "") +
                N(M1EX, " Date Time", 1, 10, null, null, true) + "]}," +
            "{\"moduleId\":\"2b624db2-5a9f-42de-96f2-b1437a8147bb\",\"moduleTitleAr\":\"Data Structures\",\"moduleOrder\":2,\"nodes\":[" +
                N(M2N1, "intro", 0, 1, "00:01:49", true, null) + "," + N(M2RV, "مراجعة ليلة الامتحان", 4, 3, null, null, null) + "]}" +
            "],\"message\":\"ok\",\"errorCode\":null,\"errors\":[]}";

        /// <summary>صفحة الكورس: أول ما تفتح بتشغّل أول درس (زي الموقع اللي بيكمّل آخر درس)، والقسم التاني مقفول.</summary>
        private string Page() => $$$"""
            <!doctype html><html><head><meta charset="utf-8"><title>course</title></head>
            <body style="margin:0;background:#1c2330;color:#ddd;font:15px Segoe UI">
            <header style="height:70px">IT Legend header</header>
            <main style="display:flex;gap:10px">
              <aside id="side" style="width:340px">
                <div class="mod"><div class="head" style="cursor:pointer" onclick="toggle(this)"><span>Date And Time</span> <small>1H 21M</small></div>
                  <div class="list">
                    <div class="node" data-id="{{{M1N1}}}" onclick="pick(this.dataset.id)"><span>intro</span> <small>01:01</small></div>
                    <div class="node" data-id="{{{M1N2}}}" onclick="pick(this.dataset.id)"><span>1-Declare Datetime</span> <small>17:22</small></div>
                    <div class="node" data-id="{{{M1EX}}}" onclick="pick(this.dataset.id)"><span>Date Time</span> <small>exam</small></div>
                  </div></div>
                <div class="mod"><div class="head" style="cursor:pointer" onclick="toggle(this)"><span>Data Structures</span> <small>3H 34M</small></div>
                  <div class="list" style="display:none">
                    <div class="node" data-id="{{{M2N1}}}" onclick="pick(this.dataset.id)"><span>intro</span> <small>01:49</small></div>
                    <div class="node" data-id="{{{M2RV}}}" onclick="pick(this.dataset.id)"><span>مراجعة ليلة الامتحان</span></div>
                  </div></div>
              </aside>
              <section style="flex:1"><div id="player" style="height:55vh"></div><div style="height:40vh">روابط الكورس / التعليقات</div></section>
            </main>
            <script>
              const API = '{{{Site}}}/api', TOKEN = '{{{Bearer}}}';
              function toggle(h) { const l = h.nextElementSibling; l.style.display = l.style.display === 'none' ? '' : 'none'; }
              function pick(id) {
                fetch(API + '/CourseVideo/{{{ItlCourse}}}/videos/' + id, { headers: { Authorization: TOKEN, 'X-Platform': 'website' } })
                  .then(r => r.json()).then(() => {
                    document.getElementById('player').innerHTML =
                      '<iframe src="https://player.masar.test/embed.html?n=' + id + '" allow="autoplay; encrypted-media; fullscreen" allowfullscreen style="width:100%;height:100%;border:0"></iframe>';
                  });
              }
              fetch(API + '/MemberCoursePlayer/{{{ItlCourse}}}', { headers: { Authorization: TOKEN, 'X-Platform': 'website' } });
              pick('{{{M1N1}}}');
            </script></body></html>
            """;

        private void Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); } catch { return; }
                try
                {
                    string path = ctx.Request.Url!.AbsolutePath;
                    string body, type = "application/json";
                    int status = 200;
                    if (path.StartsWith("/api/"))
                    {
                        ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                        ctx.Response.AddHeader("Access-Control-Allow-Headers", "Authorization, X-Platform, Content-Type");
                        if (ctx.Request.HttpMethod == "OPTIONS") { ctx.Response.StatusCode = 204; ctx.Response.Close(); continue; }
                        if (ctx.Request.Headers["Authorization"] != Bearer) { status = 401; body = """{"success":false,"message":"Unauthorized"}"""; }
                        else
                        {
                            AuthSeen = true;
                            if (path.StartsWith("/api/MemberCoursePlayer/")) body = Player();
                            else if (path.StartsWith("/api/CourseNode/")) body = Nodes(false);
                            else if (path.Contains("/videos/"))
                            {
                                lock (Selected) Selected.Add(path[(path.LastIndexOf('/') + 1)..]);
                                body = """{"success":true,"data":{"video":{"videoId":"v","titleAr":"x","path":"p"},"progress":{"resumeAt":0}}}""";
                            }
                            else { status = 404; body = """{"success":false,"message":"nf"}"""; }
                        }
                    }
                    else { Interlocked.Increment(ref PageLoads); body = Page(); type = "text/html"; }
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.StatusCode = status;
                    ctx.Response.ContentType = type + "; charset=utf-8";
                    ctx.Response.OutputStream.Write(bytes);
                    ctx.Response.Close();
                }
                catch (Exception ex) { Console.WriteLine("fake itlegend: " + ex.Message); try { ctx.Response.Close(); } catch { } }
            }
        }

        public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
    }
}
