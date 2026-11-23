using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>فحص يوديمي التلقائي (قبل كل إصدار): قراية الردود، الدمج، واستبعاده من التنزيل، والمشغّل جوه البرنامج على صفحة اختبار.</summary>
internal sealed partial class MainForm
{
    internal void RunUdemyChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("udemy: " + what); }

        // ---------- 1) الروابط ----------
        Check(Udemy.IsLecture("https://www.udemy.com/course/complete-csharp/learn/lecture/101#overview"), "lecture url");
        Check(Udemy.IsLecture("https://acme.udemy.com/course/x/learn/quiz/9"), "business quiz url");
        Check(!Udemy.IsLecture("https://evil.com/course/x/learn/lecture/1") && !Udemy.IsLecture("https://www.udemy.com/course/x/"), "non-lecture urls");
        Check(Udemy.ItemKey("https://www.udemy.com/course/x/learn/quiz/101") == "quiz:101" &&
              Udemy.ItemKey("https://www.udemy.com/course/x/learn/lecture/101") == "lecture:101", "item keys differ by kind");
        Check(new VideoItem { Path = "https://www.udemy.com/course/x/learn/lecture/5" } is { IsUdemy: true, IsOnline: true, IsDownloadable: false }, "udemy not downloadable");
        Check(new VideoItem { Path = "https://www.youtube.com/watch?v=abc" } is { IsUdemy: false, IsDownloadable: true }, "youtube downloadable");

        // ---------- 2) الردود ----------
        Check(Udemy.ParseMe("""{"_class":"user","id":0,"title":"","display_name":""}""") == null, "anonymous me");
        Check(Udemy.ParseMe("""{"_class":"user","id":42,"title":"Wael","display_name":"Wael O."}""") == "Wael O.", "signed-in me");
        var (subs, more) = Udemy.ParseSubscribed("""
            {"count":3,"next":"https://www.udemy.com/api-2.0/users/me/subscribed-courses/?page=2","previous":null,"results":[
              {"_class":"course","id":567828,"title":" Complete C# Masterclass ","url":"/course/complete-csharp-masterclass/",
               "visible_instructors":[{"_class":"user","title":"Denis Panjuta","display_name":"Denis Panjuta"}]},
              {"_class":"course","id":2,"title":"No instructor","url":"/course/no-ins/","visible_instructors":[]},
              {"_class":"course","id":0,"title":"broken"}]}
            """);
        Check(more && subs.Count == 2 && subs[0] is { Id: 567828, Title: "Complete C# Masterclass", Instructor: "Denis Panjuta" } && subs[1].Instructor == null,
            "subscribed: " + string.Join(" | ", subs));
        string curriculum1 = """
            {"count":8,"next":"page2","results":[
              {"_class":"lecture","id":100,"title":"Before any chapter","is_published":true,"asset":{"_class":"asset","asset_type":"Video","time_estimation":30}},
              {"_class":"chapter","id":1,"title":"Intro","object_index":1},
              {"_class":"lecture","id":101,"title":"Welcome","object_index":1,"is_published":true,"asset":{"asset_type":"Video","time_estimation":125,"length":125}},
              {"_class":"lecture","id":102,"title":"Read me","is_published":true,"asset":{"asset_type":"Article","time_estimation":60}},
              {"_class":"quiz","id":101,"title":"Quiz 1","type":"simple-quiz"},
              {"_class":"lecture","id":199,"title":"Hidden","is_published":false,"asset":{"asset_type":"Video"}}]}
            """;
        string curriculum2 = """
            {"count":8,"next":null,"results":[
              {"_class":"chapter","id":2,"title":"Basics"},
              {"_class":"lecture","id":103,"title":"Variables","is_published":true,"asset":{"asset_type":"Video","time_estimation":0,"length":300}}]}
            """;
        var (chapters, hasNext) = Udemy.ParseCurriculum(curriculum1);
        Check(hasNext, "curriculum next");
        Udemy.ParseCurriculum(curriculum2, chapters);
        Check(chapters.Count == 3 && chapters[0].Title == "البداية" && chapters[1].Lectures.Count == 3 && chapters[2].Lectures[0].DurationMs == 300_000,
            "curriculum shape: " + string.Join(" | ", chapters.Select(c => c.Title + ":" + c.Lectures.Count)));
        Check(chapters[1].Lectures[1].Kind == "Article" && chapters[1].Lectures[2].Kind == "Quiz", "kinds");
        var done = Udemy.ParseProgress("""{"completed_lecture_ids":[101,103],"_class":"course"}""");
        Check(done.SetEquals(new long[] { 101, 103 }) && Udemy.ParseProgress("<html>").Count == 0, "progress");

        // ---------- 3) الدمج: أول استيراد، وبعدين تحديث يحافظ على ملاحظاتك ----------
        var src = subs[0];
        var course = new Course { Name = src.Title };
        var r1 = Udemy.Apply(course, src, chapters, done, firstImport: true);
        Check(course.UdemyId == 567828 && course.Instructor == "Denis Panjuta" && course.Groups.Count == 3 && course.VideoCount == 5, "first import: " + course.VideoCount);
        Check(r1.AddedLectures == 0 && r1.MarkedWatched == 2 && course.AllVideos.All(v => !v.IsNew), "first import counters: " + r1);
        var quiz = course.Groups[1].Videos[2];
        var welcome = course.Groups[1].Videos[0];
        Check(quiz.Path.Contains("/learn/quiz/101") && !quiz.Watched && quiz.Title.StartsWith("❓"), "quiz with same id as a lecture stays separate");
        Check(welcome.Watched && welcome.DurationMs == 125_000 && course.Groups[1].Videos[1].Title.StartsWith("📄"), "welcome watched + article icon");
        welcome.Notes = "ملاحظتي";
        welcome.LastPositionMs = 50_000;
        course.Groups[1].Name = "اسمي أنا"; // المستخدم غيّر اسم القسم
        // يوديمي ضاف درس في النص وقسم جديد
        var ch2 = Udemy.ParseCurriculum(curriculum1).Chapters;
        Udemy.ParseCurriculum("""
            {"next":null,"results":[
              {"_class":"lecture","id":150,"title":"New in intro","is_published":true,"asset":{"asset_type":"Video","time_estimation":10}},
              {"_class":"chapter","id":2,"title":"Basics"},
              {"_class":"lecture","id":103,"title":"Variables","is_published":true,"asset":{"asset_type":"Video","length":300}},
              {"_class":"chapter","id":3,"title":"Advanced"},
              {"_class":"lecture","id":104,"title":"Generics","is_published":true,"asset":{"asset_type":"Video","time_estimation":200}}]}
            """, ch2);
        var r2 = Udemy.Apply(course, src, ch2, new HashSet<long> { 102 }, firstImport: false);
        Check(r2.AddedLectures == 2 && r2.AddedSections == 1 && course.Groups.Count == 4, "update counters: " + r2);
        Check(ReferenceEquals(course.Groups[1].Videos[0], welcome) && welcome.Notes == "ملاحظتي" && welcome.LastPositionMs == 50_000 && course.Groups[1].Name == "اسمي أنا",
            "update keeps the same items, notes, position and your section name");
        Check(course.Groups[1].Videos.Last().Title == "New in intro" && course.Groups[1].Videos.Last().IsNew, "new lecture flagged and placed after its predecessor");
        Check(course.Groups[1].Videos[1].Watched && welcome.Watched, "watched never removed, newly completed added");
        Check(course.Groups.All(g => g.IsUdemySection && !g.IsPlaylist), "sections are not youtube playlists");

        // ---------- 4) مش بيتنزل ومش بيتعمله «هات الجديد من يوتيوب» ----------
        _library.Courses.Add(course);
        try
        {
            Check(CourseDownloadItems(course, out _, out int local).Count == 0 && local == 0, "download items exclude udemy");
            Check(!PlaylistSync.Due(_library, DateTime.UtcNow, TimeSpan.Zero).Any(g => course.Groups.Contains(g)), "playlist sync skips udemy sections");
        }
        finally { _library.Courses.Remove(course); }

        // ---------- 5) المشغّل جوه البرنامج (صفحة اختبار بنفس شكل يوديمي) ----------
        RunUdemyPaneCheck();
    }

    private void RunUdemyPaneCheck()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("udemy pane: " + what); }
        void Pump(int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { Application.DoEvents(); Thread.Sleep(15); } }
        bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(15); } return cond(); }

        if (UdemyWeb.RuntimeVersion == null) { Console.WriteLine("SMOKE udemy pane: skipped (no WebView2 runtime)"); return; }
        string? sample = SmokeTest.RepoPath(Path.Combine("tests", "formats", "h264-aac.mp4"));
        if (sample == null) { Console.WriteLine("SMOKE udemy pane: skipped (no sample video)"); return; }

        const string host = "udemy.masar.test";
        string root = Path.Combine(Path.GetTempPath(), "masar-udemy-site");
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        File.Copy(sample, Path.Combine(Directory.CreateDirectory(root).FullName, "v.mp4"), true);
        foreach (long id in new long[] { 101, 102 })
        {
            string dir = Directory.CreateDirectory(Path.Combine(root, "course", "demo", "learn", "lecture", id.ToString())).FullName;
            File.WriteAllText(Path.Combine(dir, "p.html"), $$"""
                <!doctype html><html><head><meta charset="utf-8"><title>Lecture {{id}}</title></head>
                <body style="margin:0;background:#111;color:#eee;font:16px Segoe UI">
                <div class="app--header--xyz" id="hdr" style="height:60px;background:#a435f0">Udemy header (should be hidden)</div>
                <!-- زي يوديمي: عمود فيه transform بيحبس position:fixed، ومشغّل ارتفاعه محدود، والأزرار عنصر جنب الفيديو -->
                <div class="content-col" style="transform:translateZ(0);height:75vh;overflow:hidden">
                  <div class="player" style="position:relative;height:100%">
                    <div class="aspect" style="height:62vh;max-height:62vh"><video src="/v.mp4" autoplay muted loop playsinline style="width:100%;height:100%"></video></div>
                    <div class="controls" style="position:absolute;left:0;right:0;bottom:0;height:40px"><button>play</button><button>next</button></div>
                  </div>
                </div>
                <video src="/v.mp4" muted style="width:20px;height:20px"></video>
                <div class="tabs" style="height:30vh">Overview / Q&A</div>
                <input id="q" placeholder="Q&A">
                </body></html>
                """);
        }

        Udemy.TestOrigin = "https://" + host;
        var course = new Course { Name = "Udemy smoke", UdemyId = 1 };
        var g = new LectureGroup { Name = "Section", SourceUrl = "udemy-chapter:1" };
        var v1 = new VideoItem { Path = $"https://{host}/course/demo/learn/lecture/101/p.html", Title = "L101", LastPositionMs = 1500 };
        var v2 = new VideoItem { Path = $"https://{host}/course/demo/learn/lecture/102/p.html", Title = "L102" };
        g.Videos.AddRange(new[] { v1, v2 });
        course.Groups.Add(g);
        _library.Courses.Add(course);
        RebuildTree();
        try
        {
            Check(v1.IsUdemy && !v1.IsDownloadable, "test origin counts as udemy");
            var pane = EnsureUdemyPane();
            pane.Visible = true;
            var ready = pane.EnsureReadyAsync();
            Check(Until(() => ready.IsCompleted, 60_000) && !ready.IsFaulted, "webview init: " + ready.Exception?.GetBaseException().Message);
            pane.Web.CoreWebView2.SetVirtualHostNameToFolderMapping(host, root, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

            PlayVideo(g, v1, 0);
            Check(UdemyActive && !_videoView.Visible, "pane replaces the VLC view");
            Check(Until(() => pane.State is { HasVideo: true, DurationSec: > 0, Playing: true }, 30_000), "video plays in the pane: " + pane.State);
            Check(PlayingNow, "PlayingNow follows the page");
            Check(Until(() => _btnPlay.Text == Glyphs.Pause, 3000), "play button shows pause while the page plays (timer " + _uiTimer.Interval + " ms)");
            Check(Until(() => _uiTimer.Interval == UiFastMs, 3000), "ui timer goes fast while the page plays: " + _uiTimer.Interval);

            // تنظيف الصفحة (العرض النضيف)
            var hidden = pane.Web.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.getElementById('hdr')).display");
            Check(Until(() => hidden.IsCompleted, 5000) && hidden.Result.Contains("none"), "clean view hides udemy header: " + hidden.Result);
            // المشغّل بيملا مساحة الفيديو كلها (الصفحة كانت حاطة الفيديو 80% من الارتفاع، زي يوديمي)
            string fill = "";
            Check(Until(() =>
            {
                var t = pane.Web.CoreWebView2.ExecuteScriptAsync("(() => { const r = document.querySelector('video').getBoundingClientRect(); const b = document.querySelector('.controls button').getBoundingClientRect(); return Math.round(r.height) + '/' + innerHeight + '/' + Math.round(r.width) + '/' + innerWidth + '/' + (b.bottom > innerHeight * 0.85 && b.bottom <= innerHeight + 1 ? 1 : 0) + '/' + (document.elementFromPoint(innerWidth / 2, innerHeight * 0.7)?.closest('[data-masar-fit]') ? 1 : 0); })()");
                if (!Until(() => t.IsCompleted, 3000)) return false;
                fill = t.Result.Trim('"');
                var n = fill.Split('/').Select(x => int.TryParse(x, out int k) ? k : -1).ToArray();
                // الفيديو بيملا (بالطول أو بالعرض حسب نسبته) والأزرار ظاهرة تحت
                return n.Length == 6 && n[5] == 1 && n[1] > 0 && (Math.Abs(n[0] - n[1]) <= 3 || Math.Abs(n[2] - n[3]) <= 3) && n[0] >= n[1] - 3 && n[4] == 1;
            }, 8000), "player fills the whole pane: " + fill);

            // التحكم من «مسار»
            TogglePlay();
            Check(Until(() => pane.State is { Paused: true }, 5000), "pause from masar: " + pane.State);
            Check(Until(() => !PlayingNow && _btnPlay.Text == Glyphs.Play, 3000), "paused state reflected");
            TogglePlay();
            Check(Until(() => pane.State is { Playing: true }, 5000), "resume from masar");
            SetRate(1.5f);
            Check(Until(() => pane.State is { Rate: > 1.45 and < 1.55 }, 5000), "rate from masar: " + pane.State);
            double dur = pane.State!.DurationSec;
            SeekToFraction(0.5);
            Check(Until(() => pane.State is { } s && Math.Abs(s.TimeSec - dur / 2) < Math.Max(1.0, dur * 0.2), 5000), $"seek from masar: {pane.State} (dur {dur})");
            SaveCurrentPosition();
            Check(v1.LastPositionMs > 0, "position saved from the page: " + v1.LastPositionMs);
            SetRate(1f);

            // المفاتيح جوه الصفحة بتروح لـ «مسار» (المسافة = تشغيل/إيقاف) — إلا وإنت بتكتب في خانة
            pane.Web.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', keyCode: 32, bubbles: true }))");
            Check(Until(() => pane.State is { Paused: true }, 5000), "space inside the page toggles via masar: " + pane.State);
            pane.Web.CoreWebView2.ExecuteScriptAsync("const q = document.getElementById('q'); q.focus(); q.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', keyCode: 32, bubbles: true }))");
            Pump(1200);
            Check(pane.State is { Paused: true }, "typing in a page box is not a shortcut");
            TogglePlay();
            SaveSmokeShot(this, "udemy-pane");

            // يوديمي راح للدرس التالي لوحده ← «مسار» يمشي معاه
            Pump(3200); // بعد مهلة التحويلات الأولى
            pane.Web.CoreWebView2.ExecuteScriptAsync($"location.href = 'https://{host}/course/demo/learn/lecture/102/p.html'");
            Check(Until(() => _currentVideo == v2, 15_000), "follows udemy's next lecture: current=" + _currentVideo?.Title);
            Check(Until(() => pane.State is { HasVideo: true }, 15_000), "second lecture loads");

            // فيديو عادي بعد يوديمي: الشاشة ترجع لـ VLC والصفحة تتقفل
            StopPlayback();
            Check(!UdemyActive && !_udemyShown && _videoView.Visible, "stop hides the pane");
            Console.WriteLine("SMOKE udemy pane: ok (runtime " + UdemyWeb.RuntimeVersion + ")");
        }
        finally
        {
            HideUdemyPane();
            if (_currentVideo == v1 || _currentVideo == v2) StopPlaybackCore(clearCurrent: true);
            _library.Courses.Remove(course);
            RebuildTree();
            Udemy.TestOrigin = null;
        }
    }
}
