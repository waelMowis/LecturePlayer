using System.Net;
using System.Text;
using System.Text.Json;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// فحص منصة الإبداع (كلاسيرا) التلقائي: قراءة ردود المنصة (بنفس شكل جلسة حقيقية)، والروابط، والدمج،
/// والاستيراد من النافذة (من ردود الصفحة، بلا توكن)، والتشغيل بصفحة المنصة: فيديو، وملف يُعدّ «اطّلعت عليه»،
/// واختبار تُقرأ محاولاته ودرجته، ومتابعة الانتقال من الصفحة، وعنصر جديد يظهر وحده — قدام موقع وهمي على الجهاز.
/// </summary>
internal sealed partial class MainForm
{
    internal void RunClasseraChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("classera: " + what); }

        // ---------- الروابط ----------
        string courseUrl = FakeClassera.CourseUrl(Classera.Site);
        Check(Classera.CourseLectureOf(courseUrl) == FakeClassera.Lecture && Classera.CourseTitleOf(courseUrl) == "حساب المعلم | AY26", "course url: " + Classera.CourseLectureOf(courseUrl));
        Check(Classera.CourseLectureOf(courseUrl.Replace("elearning.classera.com", "evil.com")) == null, "other host is not classera");
        Check(Classera.CourseLectureOf("C:\\videos\\course-details?c=x.mp4") == null && !Classera.IsItem("D:\\a.mp4"), "local files are not classera");
        var src = Classera.ParseLessons(FakeClassera.Lessons(extra: false));
        Check(src is { LectureId: FakeClassera.Lecture, CourseId: FakeClassera.CourseId } && src.Lessons.Count == 4, "lessons: " + src?.Lessons.Count);
        Check(src!.Lessons[0].Items.Count == 0 && src.Lessons[1].Title == "المصادر المرفقة" && src.Lessons[1].Items.Count == 2, "lesson titles prefer arabic + attachments");
        Check(src.Lessons[2].Items is [{ IsAssignment: true, Type: "Exam" }, { IsAssignment: true, Type: "Assessment" }], "assignments in order");
        Check(src.Lessons[3].Items.All(i => i.IsVideo) && src.Lessons[3].Items.Count == 2, "youtube items are videos");
        var yt = src.Lessons[3].Items[0];
        string itemUrl = Classera.ItemUrl(yt, src.CourseId, src.LectureId);
        Check(Classera.IsItem(itemUrl) && Classera.ItemIdOf(itemUrl) == yt.Id && Classera.LectureOfPage(itemUrl) == FakeClassera.Lecture && !Classera.IsAssignmentUrl(itemUrl), "item url roundtrip: " + itemUrl);
        string examUrl = Classera.ItemUrl(src.Lessons[2].Items[0], src.CourseId, src.LectureId);
        Check(Classera.IsAssignmentUrl(examUrl) && examUrl.Contains("/solve-exam?type=exams") && Classera.ItemIdOf(examUrl) == "80749", "exam url");
        // نفس صيغة رابط الصفحة: {"id","course_id","lecture_id"} بنصوص
        var c = System.Web.HttpUtility.ParseQueryString(new Uri(itemUrl).Query)["c"]!;
        Check(Encoding.UTF8.GetString(Convert.FromBase64String(c)) == $"{{\"id\":\"{yt.Id}\",\"course_id\":\"{FakeClassera.CourseId}\",\"lecture_id\":\"{FakeClassera.Lecture}\"}}", "item c param matches the site's format");
        Check(new VideoItem { Path = itemUrl } is { IsClassera: true, IsWebLesson: true, IsOnline: true, IsDownloadable: false, IsUdemy: false, IsItLegend: false }, "item flags");
        Check(new LectureGroup { SourceUrl = "cls-lesson:1" } is { IsClasseraSection: true, IsPlaylist: false }, "classera lesson is not a playlist");
        var prog = Classera.ParseProgress(FakeClassera.Progress());
        Check(prog is { Percent: 25, Passed: false } && prog.Completed.SetEquals(new[] { "600369", "601498" }), "progress: " + prog?.Percent);
        var enrolled = Classera.ParseEnrolled(FakeClassera.Enrolled());
        Check(enrolled is [{ LectureId: FakeClassera.Lecture, CourseId: FakeClassera.CourseId, Title: "حساب المعلم | AY26" }], "enrolled list (arabic title from either field)");
        var info = Classera.ParseAssignmentInfo(FakeClassera.AssignmentInfo());
        Check(info is { Id: "80749", Submissions: 1, MaxSubmissions: 3, LastGrade: "8", MaxMark: "10" }, "assignment info");
        Check(Classera.AssignmentText(info!) == "✓ قدّمته (1 من 3) · الدرجة 8 من 10", "assignment text: " + Classera.AssignmentText(info!));
        Check(Classera.AssignmentText(info! with { Submissions = 0 }).Contains("لم تقدّمه"), "assignment not yet submitted");
        Check(Classera.KindOf("https://svaapi.classera.com/Lessons/lessons_contents?payload%5B0%5D=x") == Classera.Api.Lessons &&
              Classera.KindOf("https://svaapi.classera.com//api/v1/Attachments/update_attachment_progress/") == Classera.Api.None &&
              Classera.KindOf("https://evil.com/Lessons/lessons_contents") == Classera.Api.None, "api kinds");

        // ---------- الدمج ----------
        var course = new Course { Name = "حساب المعلم" };
        var r1 = Classera.Apply(course, src, prog, courseUrl, firstImport: true);
        Check(course.Groups.Count == 3 && course.VideoCount == 6 && r1 is { Total: 6, Done: 2, MarkedDone: 2 } && course.AllVideos.All(v => !v.IsNew), "first import: " + r1);
        Check(course.Groups[0].Videos[0].Title.StartsWith("🌐") && course.Groups[0].Videos[1].Title.StartsWith("📄") &&
              course.Groups[1].Videos[0].Title.StartsWith("📝") && course.Groups[1].Videos[1].Title.StartsWith("📋") && !course.Groups[2].Videos[0].Title.StartsWith("📎"), "icons by kind");
        Check(course.Groups[0].Videos[0].PlatformInfo == "موقع — يُفتح في صفحة المنصة" && course.Groups[2].Videos[0].PlatformInfo == null, "platform info for non-video items");
        Check(Classera.CourseLectureOf(course.SourceUrl) == FakeClassera.Lecture, "course keeps its page url");
        var keep = course.Groups[2].Videos[0];
        keep.Notes = "ملاحظتي";
        keep.LastPositionMs = 90_000;
        var r2 = Classera.Apply(course, Classera.ParseLessons(FakeClassera.Lessons(extra: true))!, null, courseUrl, firstImport: false);
        Check(r2.AddedItems == 1 && ReferenceEquals(course.Groups[2].Videos[0], keep) && keep is { Notes: "ملاحظتي", LastPositionMs: 90_000 }, "refresh keeps items: " + r2);
        Check(course.Groups[2].Videos[2] is { IsNew: true } nv && nv.Title.Contains("الجلسة 3"), "new item in order");
        Console.WriteLine("SMOKE classera: ok");
    }

    /// <summary>النافذة + التشغيل قدام منصة وهمية (من جوه فحص المشغّل عشان إطار المشغّل متجهز).</summary>
    private void RunClasseraFlowCheck(Func<Func<bool>, int, bool> until)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("classera flow: " + what); }
        using var server = FakeClassera.Start();
        Classera.TestHost = server.Host;
        int seen = ClasseraSeenMs;
        ClasseraSeenMs = 1500;
        Course? course = null;
        try
        {
            string url = FakeClassera.CourseUrl(server.Site);
            // 1) النافذة: الصفحة تعرض الدورة ← «مسار» يقرأ المنهج والتقدّم من ردودها (وضع التحديث يستورد وحده)
            using (var form = new ClasseraForm(url))
            {
                form.Show(this);
                Check(until(() => form.Result != null || form.IsDisposed, 30_000), "window read the course");
                var res = form.Result;
                Check(res is { Title: "حساب المعلم | AY26", Progress.Completed.Count: 2 } rr && rr.Course.Lessons.Count == 4 && Classera.CourseLectureOf(rr.CourseUrl) == FakeClassera.Lecture, "window result: " + res?.Title);
                ApplyClassera(res!, null, "دورات " + Classera.Platform, null);
            }
            Check(server.ApiCalls > 0, "the page itself called the platform api");
            course = FindClasseraCourse(FakeClassera.Lecture);
            Check(course is { VideoCount: 6 } && course.AllVideos.Count(v => v.Watched) == 2, "course imported with done items");
            Check(_library.Places.FirstOrDefault(p => p.Id == course!.PlaceId) is { Name: Classera.Platform }, "placed under the platform");

            // 2) فيديو يوتيوب في صفحة المنصة: «مسار» يلاقيه ويتحكم فيه ويحفظ المكان
            var videos = course!.Groups[2];
            var v1 = videos.Videos[1];
            PlayVideo(videos, v1, 0);
            Check(until(() => _udemyPane is { State: { HasVideo: true, Playing: true } }, 30_000), "item video plays in the page: " + _udemyPane?.State);
            Check(UdemyActive && _currentVideo == v1, "masar shows the platform page");
            TogglePlay();
            Check(until(() => _udemyPane!.State is { Paused: true }, 5000), "pause from masar");
            SaveCurrentPosition();
            Check(v1.LastPositionMs > 0, "position saved");

            // 3) عنصر جديد في المنهج: الصفحة تجيب المنهج فيظهر وحده
            Check(until(() => course.VideoCount == 7, 15_000), "new item appears from the page's own response: " + course.VideoCount);

            // 4) ملف (ليس فيديو): يُعدّ «اطّلعت عليه» بعد فتحه، والمنصة هي الحكم عند المزامنة
            var res2 = course.Groups[0];
            var doc = res2.Videos[1];
            Check(!doc.Watched && doc.Title.StartsWith("📄"), "document not done yet");
            PlayVideo(res2, doc, 0);
            Check(until(() => doc.Watched, 15_000), "opened document counts as seen");

            // 5) من الصفحة: «المحتويات التالية» ← «مسار» يمشي معك
            var next = res2.Videos[0];
            string href = Classera.ItemUrl(new ClasseraItem("600369", false, "website", "أدلة الاستخدام", 1), FakeClassera.CourseId, FakeClassera.Lecture) + "&sec_title=User%20Manual&type=website";
            until(() => false, 3200); // بعد مهلة فتح الصفحة
            _udemyPane!.Web.CoreWebView2.ExecuteScriptAsync($"location.href = {JsonSerializer.Serialize(href)}");
            Check(until(() => _currentVideo == next, 15_000), "follows the item opened from the page: " + _currentVideo?.Title);

            // 6) اختبار: المحاولات والدرجة من رد الصفحة، ويُعدّ منجزًا
            var exams = course.Groups[1];
            var exam = exams.Videos[0];
            PlayVideo(exams, exam, 0);
            Check(until(() => exam.Watched && exam.PlatformInfo == "✓ قدّمته (1 من 3) · الدرجة 8 من 10", 15_000), "exam result read: " + exam.PlatformInfo);
            SaveSmokeShot(this, "classera-exam");
            Console.WriteLine("SMOKE classera flow: ok");
        }
        finally
        {
            StopPlaybackCore(clearCurrent: true);
            if (course != null) _library.Courses.Remove(course);
            foreach (var t in _library.Topics.Where(t => t.Name == "دورات " + Classera.Platform).ToList())
            {
                _library.Places.RemoveAll(p => p.TopicId == t.Id);
                _library.Topics.Remove(t);
            }
            RebuildTree();
            Classera.TestHost = null;
            ClasseraSeenMs = seen;
        }
    }

    /// <summary>منصة وهمية: رحلتي التعليمية، وصفحة الدورة، وصفحة عنصر (فيديو في إطار من موقع آخر أو ملف)، وصفحة اختبار، وسيرفر /svaapi/.</summary>
    private sealed class FakeClassera : IDisposable
    {
        public const string Lecture = "127654", CourseId = "122361";
        private readonly HttpListener _listener = new();
        public string Host { get; }
        public string Site => "http://" + Host;
        public int ApiCalls;

        private FakeClassera(int port)
        {
            Host = "localhost:" + port;
            _listener.Prefixes.Add(Site + "/");
        }

        public static FakeClassera Start()
        {
            for (int a = 0; ; a++)
            {
                var f = new FakeClassera(Random.Shared.Next(56000, 60000));
                try { f._listener.Start(); }
                catch (HttpListenerException) when (a < 10) { continue; }
                new Thread(f.Loop) { IsBackground = true }.Start();
                return f;
            }
        }

        /// <summary>رابط صفحة الدورة كما تفتحه المنصة: c = {"id": رقم الدورة, "course_id": معرّف من 10 حروف}.</summary>
        public static string CourseUrl(string site) =>
            site + "/course-details?title=" + Uri.EscapeDataString("حساب المعلم | AY26") + "&absent_progress=0&c=" +
            Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"id\":\"{Lecture}\",\"course_id\":\"Q2xhc3MxMj\"}}")));

        private static string Att(string id, string title, string type, int order) =>
            $$"""{"id":"{{id}}","lecture_id":"{{Lecture}}","course_id":"{{CourseId}}","user_id":"1","lesson_id":"1","vimeo_link":null,"youtube_link":{{(type == "Youtube" ? "\"https://www.youtube.com/watch?v=Q7ebmQo7SRQ\"" : "null")}},"title":"{{title}}","sec_title":"x","type":"{{type}}","order":"{{order}}","deleted":false,"duration":"0.000","hide_from_lessons_list":false,"is_published":true,"exclude_from_progress":false}""";

        private static string Asg(string id, string title, string type, int order) =>
            $$"""{"id":"{{id}}","title":"{{title}}","sec_title":"x","type":"{{type}}","mark":"10","order":"{{order}}","deleted":false,"course_id":"{{CourseId}}","lecture_id":"{{Lecture}}","hide_from_lessons_list":false}""";

        public static string Lessons(bool extra) => "{\"success\":true,\"categorized\":[" +
            $$"""{"id":"88464","lecture_id":"{{Lecture}}","course_id":"{{CourseId}}","title":"الجلسات المباشرة نظام كلاسيرا","title_ara":"Live Sessions","lesson_order":"1.00","deleted":false},""" +
            $$"""{"id":"88465","lecture_id":"{{Lecture}}","course_id":"{{CourseId}}","title":"Resources","title_ara":"المصادر المرفقة","lesson_order":"1.50","deleted":false,"attachments":[""" +
                Att("600369", "أدلة الاستخدام", "website", 1) + "," + Att("600375", "دليل المصادر المؤرشفة", "Document", 2) + "]}," +
            $$"""{"id":"88466","lecture_id":"{{Lecture}}","course_id":"{{CourseId}}","title":"تقييم التدريب","title_ara":"Training Evaluation","lesson_order":"2.00","deleted":false,"assignments":[""" +
                Asg("80750", "رأيك يهمنا! شاركنا تجربة التدريب", "Assessment", 2) + "," + Asg("80749", "اختبر قدراتك", "Exam", 1) + "]}," +
            $$"""{"id":"88467","lecture_id":"{{Lecture}}","course_id":"{{CourseId}}","title":"الجلسات المؤرشفة","title_ara":"Archived Sessions","lesson_order":"2.50","deleted":false,"attachments":[""" +
                Att("601498", "AR Live – الجلسة 1", "Youtube", 1) + "," + Att("601499", "AR Live – الجلسة 2", "Youtube", 2) +
                (extra ? "," + Att("601883", "AR Live – الجلسة 3", "Youtube", 3) : "") + "]}" +
            "],\"uncategorized\":[]}";

        public static string Progress() =>
            """{"user_percentage":0.25,"user_completed":[{"ContentUserRecord":{"reference_id":"600369","modified":"2026-11-15 10:00:00"}},{"ContentUserRecord":{"reference_id":"601498","modified":"2026-11-16 10:00:00"}}],"optional_user_completed":[],"passed":"0"}""";

        public static string Enrolled() =>
            "{\"success\":true,\"message\":\"\",\"data\":{\"courses\":[{\"SvaCourse\":{\"id\":" + CourseId +
            ",\"title_ar\":\"Teachers Account | AY26\",\"title_en\":\"حساب المعلم | AY26\",\"date_from\":\"2025-08-13T01:00:00+03:00\",\"date_to\":\"2027-12-31T01:00:00+03:00\"}," +
            "\"SvaLecture\":{\"id\":\"" + Lecture + "\",\"course_id\":" + CourseId + "},\"Level\":{},\"assistants\":[],\"is_assistant\":false}]}}";

        public static string AssignmentInfo() =>
            """{"assignment_info":{"id":"80749","title":"اختبر قدراتك","student_submissions":1,"multi_submissions":"3","number_of_submissions":1,"last_submission_grade":"8","maxMark":"10","out_of_attempts":false,"type":"Exam"}}""";

        private const string Api = """
            <script>
              const api = (p, post) => fetch('/svaapi/' + p + '?payload%5B0%5D=ZW5jcnlwdGVk', { method: post ? 'POST' : 'GET' }).then(r => r.text());
              const ctx = () => JSON.parse(atob(new URLSearchParams(location.search).get('c')));
            </script>
            """;

        private static string Page(string path, string query)
        {
            string head = "<!doctype html><html><head><meta charset=\"utf-8\"><title>منصة الإبداع</title></head><body style=\"margin:0;background:#fff;color:#123;font:15px Segoe UI\">" +
                          "<header style=\"height:60px;border-bottom:1px solid #ddd\">منصة الإبداع في التعليم الإلكتروني</header>" + Api;
            return path switch
            {
                "/course-details" => head + """
                    <main><h2>حساب المعلم | AY26</h2><p>12.5%</p></main>
                    <script>api('api/v2/trainee/courses'); api('svaLecture/get_progress_percentage', true); api('Lessons/lessons_contents');</script></body></html>
                    """,
                "/attachments-details" => head + """
                    <main style="display:flex"><section id="p" style="flex:1;height:70vh"></section><aside style="width:280px">المحتويات التالية</aside></main>
                    <script>
                      const id = ctx().id;
                      document.getElementById('p').innerHTML = id.startsWith('6014') || id.startsWith('6018')
                        ? '<iframe src="https://player.masar.test/embed.html" allow="autoplay; encrypted-media; fullscreen" allowfullscreen style="width:100%;height:100%;border:0"></iframe>'
                        : '<div style="padding:30px">ملف PDF: ' + decodeURIComponent(new URLSearchParams(location.search).get('title')) + '</div>';
                      api('Attachments/view_attachment'); api('Lessons/lessons_contents');
                    </script></body></html>
                    """,
                "/solve-exam" => head + """
                    <main style="padding:30px"><h2>اختبر قدراتك</h2><p>عنوان الاختبار · المدة 30 · العلامة 10 · التعليمات: 3 محاولات</p><button>البدء</button></main>
                    <script>api('Assignments/student_assignment_info', true); api('Lessons/lessons_contents');</script></body></html>
                    """,
                _ => head + "<main>رحلتي التعليمية</main><script>api('api/v2/trainee/courses');</script></body></html>"
            };
        }

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
                    if (path.StartsWith("/svaapi/"))
                    {
                        Interlocked.Increment(ref ApiCalls);
                        body = path switch
                        {
                            // صفحة الدورة: المنهج كما كان عند الاستيراد، وصفحات العناصر: بعنصر جديد أُضيف بعدها
                            _ when path.EndsWith("/Lessons/lessons_contents") => Lessons(extra: ctx.Request.UrlReferrer?.AbsolutePath != "/course-details"),
                            _ when path.EndsWith("/svaLecture/get_progress_percentage") => Progress(),
                            _ when path.EndsWith("/trainee/courses") => Enrolled(),
                            _ when path.EndsWith("/Assignments/student_assignment_info") => AssignmentInfo(),
                            _ => """{"attachment_details":[]}"""
                        };
                    }
                    else { body = Page(path, ctx.Request.Url.Query); type = "text/html"; }
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = type + "; charset=utf-8";
                    ctx.Response.OutputStream.Write(bytes);
                    ctx.Response.Close();
                }
                catch (Exception ex) { Console.WriteLine("fake classera: " + ex.Message); try { ctx.Response.Close(); } catch { } }
            }
        }

        public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
    }
}
