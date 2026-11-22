namespace LecturePlayer;

/// <summary>
/// فحص إن البرنامج بيفتح من غير ما يقع: بيبني الشباك الرئيسي، ويعرضه، ويجرّب الوضع البسيط والمتقدم،
/// وشريط التنبيهات، ومنيو المذاكرة، ومبدّل المكتبات، والجولة — وبعدين يقفل.
/// بيتشغّل في GitHub Actions قبل النشر: LecturePlayer.exe --smoke-test
/// </summary>
internal static class SmokeTest
{
    public static bool Active { get; private set; }

    public static int Run()
    {
        Active = true;
        Exception? failure = null;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => { failure ??= e.Exception; Application.ExitThread(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine("SMOKE FAIL (unhandled): " + e.ExceptionObject);
            Environment.Exit(1);
        };

        try
        {
            ProxySelfTest().GetAwaiter().GetResult();
            Console.WriteLine("SMOKE proxy OK");
            FolderLinkSelfTest();
            Console.WriteLine("SMOKE folder link OK");
            HierarchySelfTest();
            Console.WriteLine("SMOKE hierarchy OK");
            LibVLCSharp.Shared.Core.Initialize();
            FormatsSelfTest();
            using var form = new MainForm(null, Array.Empty<string>());
            form.Shown += (_, _) =>
            {
                var t = new System.Windows.Forms.Timer { Interval = 1500 };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    try { form.RunSmokeChecks(); }
                    catch (Exception ex) { failure ??= ex; }
                    form.Close();
                };
                t.Start();
            };
            Application.Run(form);
            if (failure == null) failure = StartupLayoutCheck();
        }
        catch (Exception ex) { failure ??= ex; }

        if (failure != null)
        {
            Console.Error.WriteLine("SMOKE FAIL: " + failure);
            return 1;
        }
        Console.WriteLine("SMOKE OK");
        return 0;
    }

    /// <summary>بيدوّر على فولدر في المستودع (tests/formats، installer) طالع من فولدر التشغيل أو البرنامج.</summary>
    private static string? RepoPath(string relative)
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var d = new DirectoryInfo(start);
            for (int i = 0; d != null && i < 8; i++, d = d.Parent)
            {
                string p = Path.Combine(d.FullName, relative);
                if (Directory.Exists(p) || File.Exists(p)) return p;
            }
        }
        return null;
    }

    /// <summary>
    /// كل صيغة في MediaFiles ليها ملف حقيقي في tests/formats، وكل ملف بيتشغّل بـ VLC بتاع البرنامج:
    /// بيتفتح، وكل التراكات (صورة/صوت) بتتفك من غير «no suitable decoder»، والفيديو ليه صورة بمقاس.
    /// (العرض والصوت «وهميين» عشان الجهاز مايحتاجش شاشة/سماعات — بس الفك حقيقي.)
    /// وكمان: المثبّت بيسجّل نفس الصيغ بالظبط في «فتح بواسطة».
    /// </summary>
    private static void FormatsSelfTest()
    {
        string? dir = RepoPath(Path.Combine("tests", "formats"));
        if (dir == null) { Console.WriteLine("SMOKE formats skipped (no tests/formats)"); return; }

        var files = Directory.GetFiles(dir).Where(f => !f.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList();
        var problems = new List<string>();

        // 1) القوايم: كل امتداد عنده ملف، وكل ملف امتداده في القايمة
        var covered = files.Select(Path.GetExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string e in LecturePlayer.Data.MediaFiles.Extensions.Where(e => !covered.Contains(e)))
            problems.Add($"{e}: in MediaFiles but no sample file");
        foreach (string f in files.Where(f => !LecturePlayer.Data.MediaFiles.IsMedia(f)))
            problems.Add($"{Path.GetFileName(f)}: sample not recognized by MediaFiles");

        // 2) التشغيل الفعلي
        using var lib = new LibVLCSharp.Shared.LibVLC("--vout=dummy", "--aout=adummy", "--no-video-title-show", "--verbose=1");
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        lib.Log += (_, e) => log.Enqueue(e.Message);
        int ok = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (string f in files)
        {
            string name = Path.GetFileName(f);
            bool audioOnly = LecturePlayer.Data.MediaFiles.IsAudio(f);
            while (log.TryDequeue(out _)) { }
            using var media = new LibVLCSharp.Shared.Media(lib, f, LibVLCSharp.Shared.FromType.FromPath);
            using var mp = new LibVLCSharp.Shared.MediaPlayer(media);
            var ended = new ManualResetEventSlim();
            bool error = false;
            mp.EncounteredError += (_, _) => { error = true; ended.Set(); };
            mp.EndReached += (_, _) => ended.Set();
            if (!mp.Play()) { problems.Add($"{name}: Play() returned false"); continue; }
            // نسيبه يخلص (الملفات ثانية واحدة) عشان عدادات الفك تتحدّث
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!ended.IsSet && DateTime.UtcNow < until) Thread.Sleep(40);
            uint w = 0, h = 0;
            try { mp.Size(0, ref w, ref h); } catch { }
            var stats = media.Statistics;
            var tracks = media.Tracks;
            bool fileHasAudio = tracks.Any(t => t.TrackType == LibVLCSharp.Shared.TrackType.Audio);
            bool videoDecoded = stats.DecodedVideo > 0 || w > 0
                                || tracks.Any(t => t.TrackType == LibVLCSharp.Shared.TrackType.Video && t.Data.Video.Width > 0);
            bool audioDecoded = stats.DecodedAudio > 0;
            mp.Stop();
            string[] bad = log.Where(m => m.Contains("no suitable decoder", StringComparison.OrdinalIgnoreCase)
                                          || m.Contains("could not identify", StringComparison.OrdinalIgnoreCase)
                                          || m.Contains("Codec not supported", StringComparison.OrdinalIgnoreCase)
                                          || m.Contains("cannot decode", StringComparison.OrdinalIgnoreCase)
                                          || m.Contains("no suitable demux", StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
            string why = error ? "player error" : bad.Length > 0 ? string.Join(" | ", bad.Take(2))
                : !audioOnly && !videoDecoded ? $"video not decoded (decoded {stats.DecodedVideo}, size {w}x{h})"
                : fileHasAudio && !audioDecoded && !name.StartsWith("dv-") ? $"audio not decoded (decoded {stats.DecodedAudio})"
                : audioOnly && !fileHasAudio ? "no audio track" : "";
            Console.WriteLine($"SMOKE format {name}: {(why.Length == 0 ? "OK" : "FAIL " + why)} — video frames {stats.DecodedVideo} ({w}x{h}), audio blocks {stats.DecodedAudio}");
            if (why.Length > 0) problems.Add($"{name}: {why}");
            else ok++;
        }
        Console.WriteLine($"SMOKE formats: {ok}/{files.Count} played in {sw.Elapsed.TotalSeconds:0.0}s, {LecturePlayer.Data.MediaFiles.VideoExtensions.Count} video + {LecturePlayer.Data.MediaFiles.AudioExtensions.Count} audio extensions");

        // 3) المثبّت ← نفس القوايم
        string? iss = RepoPath(Path.Combine("installer", "LecturePlayer.iss"));
        if (iss != null)
        {
            string text = File.ReadAllText(iss);
            var progs = System.Text.RegularExpressions.Regex.Matches(text,
                    @"Classes\\(\.[a-z0-9]+)\\OpenWithProgids""; ValueType: string; ValueName: ""LecturePlayer\.(Video|Audio)""")
                .Select(m => (Ext: m.Groups[1].Value, Kind: m.Groups[2].Value)).ToList();
            foreach (string e in LecturePlayer.Data.MediaFiles.VideoExtensions.Where(e => !progs.Contains((e, "Video"))))
                problems.Add($"installer: {e} not registered as video");
            foreach (string e in LecturePlayer.Data.MediaFiles.AudioExtensions.Where(e => !progs.Contains((e, "Audio"))))
                problems.Add($"installer: {e} not registered as audio");
            foreach (var (e, _) in progs.Where(p => !LecturePlayer.Data.MediaFiles.IsMedia("x" + p.Ext)))
                problems.Add($"installer: {e} registered but not in MediaFiles");
        }

        if (problems.Count > 0)
            throw new Exception("formats:\n  " + string.Join("\n  ", problems));
        Console.WriteLine("SMOKE formats OK");
    }

    /// <summary>
    /// نفس فتح المستخدم بالظبط (21/11): المكتبة مستخبية، والشباك متكبّر، ومكانه المحفوظ على شاشة تانية مش موجودة،
    /// والمكتبة بتتفتح من ملف. الرئيسية لازم تاخد الشباك كله. بيتجرب مرة من غير الإصلاح (للسجل) ومرة بيه.
    /// </summary>
    private static Exception? StartupLayoutCheck()
    {
        var saved = LecturePlayer.Data.Storage.LoadSettings();
        string savedJson = System.Text.Json.JsonSerializer.Serialize(saved);
        string dir = Path.Combine(Path.GetTempPath(), "masar-smoke-startup");
        Directory.CreateDirectory(dir);
        string lib = Path.Combine(dir, "WAEL.lpl");
        var l = new LecturePlayer.Data.CourseLibrary { Name = "WAEL" };
        var c = new LecturePlayer.Data.Course { Name = "C# Basics" };
        var g = new LecturePlayer.Data.LectureGroup { Name = "Session 04" };
        g.Videos.Add(new LecturePlayer.Data.VideoItem { Path = Path.Combine(dir, "Part 06.mp4"), Title = "Part 06", LastPositionMs = 60000, DurationMs = 600000 });
        g.Videos.Add(new LecturePlayer.Data.VideoItem { Path = Path.Combine(dir, "Part 07.mp4"), Title = "Part 07" });
        c.Groups.Add(g);
        l.Courses.Add(c);
        l.LastGroupId = g.Id;
        l.LastVideoPath = g.Videos[0].Path;
        LecturePlayer.Data.Storage.SaveLibrary(lib, l);

        Exception? result = null;
        try
        {
            foreach (bool withFix in new[] { false, true })
            {
                var s = System.Text.Json.JsonSerializer.Deserialize<LecturePlayer.Data.AppSettings>(savedJson)!;
                s.SidePanelVisible = false;
                s.SidePanelWidth = 431;
                s.Maximized = true;
                s.WindowX = -1262; s.WindowY = -18; s.WindowWidth = 900; s.WindowHeight = 560;
                s.OpenLastLibraryOnStart = false;
                LecturePlayer.Data.Storage.SaveSettings(s);

                MainForm.DisableSplitFixForSmoke = !withFix;
                string report = "";
                bool ok = false;
                using (var form = new MainForm(lib, Array.Empty<string>()))
                {
                    form.Shown += (_, _) =>
                    {
                        var t = new System.Windows.Forms.Timer { Interval = 2500 };
                        t.Tick += (_, _) => { t.Stop(); report = form.StartupLayoutReport(out ok); form.Close(); };
                        t.Start();
                    };
                    Application.Run(form);
                }
                Console.WriteLine($"SMOKE startup layout {(withFix ? "with fix" : "WITHOUT fix")}: {(ok ? "OK" : "BROKEN")} — {report}");
                if (withFix && !ok) result = new Exception("startup layout broken: " + report);
            }
        }
        catch (Exception ex) { result = ex; }
        finally
        {
            MainForm.DisableSplitFixForSmoke = false;
            LecturePlayer.Data.Storage.SaveSettings(System.Text.Json.JsonSerializer.Deserialize<LecturePlayer.Data.AppSettings>(savedJson)!);
        }
        return result;
    }

    // =====================================================================
    //  فحص الوسيط (StreamProxy) من غير نت: سيرفر وهمي على الجهاز بيمثّل يوتيوب
    // =====================================================================

    private static async Task ProxySelfTest()
    {
        const int size = 25 * 1024 * 1024 + 12345;
        byte At(long i) => (byte)((i * 31 + (i >> 11)) & 0xFF);
        var upstream = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        upstream.Start();
        int port = ((System.Net.IPEndPoint)upstream.LocalEndpoint).Port;
        int requests = 0, refreshed = 0;
        using var stop = new CancellationTokenSource();

        // السيرفر الوهمي: بيدعم Range، والرابط القديم (/old) بيترفض 403 بعد أول 3 طلبات زي يوتيوب
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                System.Net.Sockets.TcpClient c;
                try { c = await upstream.AcceptTcpClientAsync(stop.Token); } catch { break; }
                _ = Task.Run(async () =>
                {
                    using (c)
                    {
                        var ns = c.GetStream();
                        while (true)
                        {
                            var head = new List<byte>();
                            var one = new byte[1];
                            while (true)
                            {
                                if (await ns.ReadAsync(one) == 0) return;
                                head.Add(one[0]);
                                int n = head.Count;
                                if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break;
                            }
                            string text = System.Text.Encoding.ASCII.GetString(head.ToArray());
                            string path = text.Split(' ')[1];
                            if (!text.Contains("X-Test: yes")) { await Send(ns, "400 Bad Request", ""); continue; }
                            int count = Interlocked.Increment(ref requests);
                            if (path.StartsWith("/old") && count > 3) { await Send(ns, "403 Forbidden", ""); continue; }
                            long from = 0, to = size - 1;
                            var m = System.Text.RegularExpressions.Regex.Match(text, @"Range:\s*bytes=(\d+)-(\d*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (m.Success) { from = long.Parse(m.Groups[1].Value); if (m.Groups[2].Value != "") to = Math.Min(size - 1, long.Parse(m.Groups[2].Value)); }
                            var body = new byte[to - from + 1];
                            for (long i = 0; i < body.Length; i++) body[i] = At(from + i);
                            string hdr = $"HTTP/1.1 206 Partial Content\r\nContent-Type: video/mp4\r\nContent-Range: bytes {from}-{to}/{size}\r\nContent-Length: {body.Length}\r\n\r\n";
                            await ns.WriteAsync(System.Text.Encoding.ASCII.GetBytes(hdr));
                            await ns.WriteAsync(body);
                        }
                    }
                });
            }
        });

        static async Task Send(System.Net.Sockets.NetworkStream ns, string status, string body)
        {
            var b = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\n\r\n{body}");
            await ns.WriteAsync(b);
        }

        try
        {
            var headers = new Dictionary<string, string> { ["X-Test"] = "yes", ["User-Agent"] = "smoke" };
            using var proxy = new LecturePlayer.Data.StreamProxy();
            string local = proxy.Register($"http://127.0.0.1:{port}/old", headers, _ =>
            {
                Interlocked.Increment(ref refreshed);
                return Task.FromResult<(string, IReadOnlyDictionary<string, string>)?>(($"http://127.0.0.1:{port}/new", headers));
            }, out var source);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            // 1) طلب من نص الملف لآخره (بيعدّي على أكتر من جزء، والرابط القديم بيترفض في النص)
            long start = 3_000_000;
            var req = new HttpRequestMessage(HttpMethod.Get, local);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, null);
            using var resp = await http.SendAsync(req);
            if ((int)resp.StatusCode != 206) throw new Exception("proxy: expected 206, got " + (int)resp.StatusCode);
            var data = await resp.Content.ReadAsByteArrayAsync();
            if (data.Length != size - start) throw new Exception($"proxy: length {data.Length} != {size - start}");
            for (int i = 0; i < data.Length; i += 997)
                if (data[i] != At(start + i)) throw new Exception("proxy: wrong byte at " + (start + i));
            if (refreshed == 0) throw new Exception("proxy: url was not refreshed after 403");
            // 2) طلب من غير Range: 200 بالحجم الكامل
            using var full = await http.GetAsync(local, HttpCompletionOption.ResponseHeadersRead);
            if ((int)full.StatusCode != 200 || full.Content.Headers.ContentLength != size)
                throw new Exception("proxy: full request wrong: " + (int)full.StatusCode + " len " + full.Content.Headers.ContentLength);
            // 3) وقت الإتاحة بتاع يوتيوب (available_at) بيتقري من ناتج أداة يوتيوب
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 20;
            var parsed = LecturePlayer.Data.YtDlp.ParseStream(
                "{\"duration\":10,\"http_headers\":{\"User-Agent\":\"x\"},\"requested_formats\":[" +
                "{\"format_id\":\"136\",\"url\":\"https://a/v?ip=1.2.3.4\",\"protocol\":\"https\",\"vcodec\":\"avc1\",\"acodec\":\"none\",\"available_at\":" + ts + "}," +
                "{\"format_id\":\"251\",\"url\":\"https://a/a\",\"protocol\":\"https\",\"vcodec\":\"none\",\"acodec\":\"opus\"}]}");
            if (parsed.AvailableAt?.ToUnixTimeSeconds() != ts) throw new Exception("available_at not parsed");
            if (parsed.Parts?.Count != 2 || parsed.AudioUrl != "https://a/a") throw new Exception("parts not parsed");
            // البث: القايمة الرئيسية (manifest_url) بتيجي جوه كل صيغة مش في الأول
            var live = LecturePlayer.Data.YtDlp.ParseStream(
                "{\"is_live\":true,\"requested_formats\":[" +
                "{\"format_id\":\"301\",\"url\":\"https://a/v.m3u8\",\"protocol\":\"m3u8_native\",\"vcodec\":\"avc1\",\"acodec\":\"none\",\"manifest_url\":\"https://a/master.m3u8\"}," +
                "{\"format_id\":\"234\",\"url\":\"https://a/a.m3u8\",\"protocol\":\"m3u8_native\",\"vcodec\":\"none\",\"acodec\":\"mp4a\",\"manifest_url\":\"https://a/master.m3u8\"}]}");
            if (live.ManifestUrl != "https://a/master.m3u8" || live.AudioUrl == null) throw new Exception("live manifest not parsed");
            if (LecturePlayer.Data.StreamProxy.FamilyFromUrl(new Uri("https://a/v?ip=1.2.3.4")) != System.Net.Sockets.AddressFamily.InterNetwork) throw new Exception("ip family");
            // 4) وضع «الأنبوب»: عملية بتكتب على stdout والوسيط بيمرره
            string tmp = Path.Combine(Path.GetTempPath(), "masar-smoke-pipe.bin");
            var payload = new byte[3 * 1024 * 1024 + 7];
            for (int i = 0; i < payload.Length; i++) payload[i] = At(i);
            File.WriteAllBytes(tmp, payload);
            string pipeUrl = proxy.RegisterPipe(() =>
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("type"); psi.ArgumentList.Add(tmp);
                return System.Diagnostics.Process.Start(psi)!;
            }, "video/mp4");
            var piped = await http.GetByteArrayAsync(pipeUrl);
            if (piped.Length != payload.Length || !piped.AsSpan().SequenceEqual(payload)) throw new Exception($"pipe: got {piped.Length} bytes");
            proxy.StopPipes();
            File.Delete(tmp);
            // 5) تحويل الكلام لنص: من غير «لفّة» التكرار
            if (!LecturePlayer.Data.Transcriber.WhisperArgs("m", "w", "o", "auto", 2).Contains("-mc")) throw new Exception("whisper args: -mc missing");
            var loop = new LecturePlayer.Data.Transcript();
            loop.Cues.Add(new LecturePlayer.Data.Cue(0, 1000, "أهلا"));
            for (int i = 1; i <= 5; i++) loop.Cues.Add(new LecturePlayer.Data.Cue(i * 10000, i * 10000 + 2000, "نحن نتحدث عن \"يوزر دفايند فونكشون\""));
            loop.Cues.Add(new LecturePlayer.Data.Cue(90000, 91000, "خلاص"));
            if (LecturePlayer.Data.Transcriber.RemoveRepeats(loop).Cues.Count != 3) throw new Exception("RemoveRepeats");
            // 6) فحص «أكّد إنك مش روبوت» بيتعرف
            if (!LecturePlayer.Data.YtDlp.IsBotCheck("ERROR: [youtube] Fbl-YpQgnrg: Sign in to confirm you’re not a bot. Use --cookies-from-browser"))
                throw new Exception("bot check not detected");
            if (LecturePlayer.Data.YtDlp.IsBotCheck("ERROR: Sign in to confirm your age")) throw new Exception("age check detected as bot");
            // 7) تحليل Range
            if (LecturePlayer.Data.StreamProxy.ParseRange("bytes=10-") != (10, null)) throw new Exception("ParseRange");
        }
        finally
        {
            stop.Cancel();
            upstream.Stop();
        }
    }

    /// <summary>ربط فولدر كورس بنفس ترتيب كورس المحاضرة (Eng X / 01 C# Basics / Session 04 / Videos …).</summary>
    private static void FolderLinkSelfTest()
    {
        string root = Path.Combine(Path.GetTempPath(), "masar-smoke-link", "Eng Doaa Amin Elfawal");
        if (Directory.Exists(Path.GetDirectoryName(root)!)) Directory.Delete(Path.GetDirectoryName(root)!, true);
        void Touch(string rel) { string f = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, "x"); }
        Touch(@"00 Orientation\Videos\Welcome.mp4");
        Touch(@"01 C# Basics\Session 01\Videos\Part 02 Types.mp4");
        Touch(@"01 C# Basics\Session 01\Videos\Part 01 Intro.mp4");
        Touch(@"01 C# Basics\Session 01\Presentation\Session01.pptx");
        Touch(@"01 C# Basics\Session 01\Demo Link.gdoc");
        Touch(@"01 C# Basics\Session 02\Videos\Part 01 Arrays.mp4");

        var lib = new LecturePlayer.Data.CourseLibrary();
        // فيديو كان متضاف بإيدك قبل كده (ومعاه تقدّم وعلامة)
        var manual = LecturePlayer.Data.VideoItem.FromFile(Path.Combine(root, @"01 C# Basics\Session 02\Videos\Part 01 Arrays.mp4"));
        manual.LastPositionMs = 5000;
        manual.Bookmarks.Add(new LecturePlayer.Data.Bookmark { TimeMs = 1000, Note = "مهم" });
        var manualCourse = new LecturePlayer.Data.Course { Name = "LEC4" };
        manualCourse.Groups.Add(new LecturePlayer.Data.LectureGroup { Name = "LEC4", Videos = { manual } });
        lib.Courses.Add(manualCourse);

        var topic = new LecturePlayer.Data.Topic { Name = "Eng Doaa Amin Elfawal" };
        lib.Topics.Add(topic);
        var snap = LecturePlayer.Data.FolderLink.Scan(root) ?? throw new Exception("folder link: scan null");
        var r = LecturePlayer.Data.FolderLink.Apply(lib, topic, snap, initial: true);
        string got = $"courses {r.NewCourses}, lectures {r.NewGroups}, new {r.NewVideos.Count}, moved {r.Moved}, attachments {r.NewAttachments}";
        Console.WriteLine("SMOKE folder link initial: " + got);
        if (r.NewCourses != 2 || r.NewGroups != 3 || r.NewVideos.Count != 3 || r.Moved != 1 || r.NewAttachments != 2)
            throw new Exception("folder link initial: " + got);
        if (lib.Courses.Contains(manualCourse)) throw new Exception("folder link: empty manual course not removed");
        var basics = lib.Courses.Single(c => c.Name == "01 C# Basics");
        var s1 = basics.Groups.Single(g => g.Name == "Session 01");
        if (s1.Videos[0].Title != "Part 01 Intro") throw new Exception("folder link: wrong order " + s1.Videos[0].Title);
        var s2 = basics.Groups.Single(g => g.Name == "Session 02");
        if (s2.Videos.Single() != manual || manual.LastPositionMs != 5000 || manual.Bookmarks.Count != 1) throw new Exception("folder link: progress not kept");
        if (lib.Courses.Single(c => c.Name == "00 Orientation").Groups.Single().Videos.Count != 1) throw new Exception("folder link: Videos folder of a course");

        // المحاضر رفع حاجات جديدة
        Touch(@"01 C# Basics\Session 01\Videos\Part 03 Loops.mp4");
        Touch(@"01 C# Basics\Session 03\Videos\Part 01 Methods.mp4");
        var r2 = LecturePlayer.Data.FolderLink.Apply(lib, topic, LecturePlayer.Data.FolderLink.Scan(root)!, initial: false);
        Console.WriteLine($"SMOKE folder link update: lectures +{r2.NewGroups}, videos +{r2.NewVideos.Count}");
        if (r2.NewGroups != 1 || r2.NewVideos.Count != 2 || !r2.NewVideos.All(v => v.IsNew) || r2.GroupsWithNew.Count != 2)
            throw new Exception("folder link update wrong");
        // مفيش جديد: مفيش تغيير
        if (LecturePlayer.Data.FolderLink.Apply(lib, topic, LecturePlayer.Data.FolderLink.Scan(root)!, initial: false).Changed)
            throw new Exception("folder link: changed with nothing new");
        Directory.Delete(Path.GetDirectoryName(root)!, true);
    }

    /// <summary>الهيكل: موضوع ← مكان ← كورس ← محاضرة ← فيديو، والتوافق مع القوائم القديمة والدمج بين الأجهزة.</summary>
    private static void HierarchySelfTest()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("hierarchy: " + what); }
        string libPath = Path.Combine(Path.GetTempPath(), "masar-smoke-hier.lpl");

        // 1) قائمة قديمة (من غير أماكن) بتفتح زي ما هي
        const string oldJson = """
        {"Version":2,"Name":"old","Topics":[{"Id":"t1","Name":"Backend"}],
         "Courses":[{"Id":"c1","Name":"C# Basics","TopicId":"t1","Groups":[{"Id":"g1","Name":"Session 01","Videos":[]}]},
                    {"Id":"c2","Name":"Loose"}]}
        """;
        var lib = LecturePlayer.Data.Storage.FromJson(oldJson, libPath);
        var backend = lib.Topics.Single();
        var basics = lib.Courses.Single(c => c.Id == "c1");
        Check(lib.Places.Count == 0, "old library got places");
        Check(lib.DirectCoursesOf(backend).Single() == basics, "old course not directly under topic");
        Check(lib.DirectCoursesOf(null).Single().Id == "c2", "loose course");
        Check(!LecturePlayer.Data.Storage.Serialize(lib).Contains("PlaceId"), "old course wrote PlaceId");

        // 2) مكان جوه الموضوع، والكورس يتنقل له
        var route = new LecturePlayer.Data.Place { Name = "ROUTE", TopicId = backend.Id };
        lib.Places.Add(route);
        lib.MoveCourse(basics, null, route);
        Check(basics.TopicId == backend.Id && lib.PlaceOf(basics) == route, "move course into place");
        Check(!lib.DirectCoursesOf(backend).Any() && lib.CoursesInPlace(route).Single() == basics, "place listing");
        Check(lib.CoursesOf(backend).Single() == basics, "topic still counts course in place");
        Check(lib.PathOf(basics) == "Backend › ROUTE › C# Basics", "path " + lib.PathOf(basics));

        // 3) الحفظ والفتح تاني
        var reloaded = LecturePlayer.Data.Storage.FromJson(LecturePlayer.Data.Storage.Serialize(lib), libPath);
        var rb = reloaded.Courses.Single(c => c.Id == "c1");
        Check(reloaded.PlaceOf(rb)?.Name == "ROUTE", "place lost on save");

        // 4) نقل المكان لموضوع تاني: كورساته معاه
        var dotnet = new LecturePlayer.Data.Topic { Name = ".NET" };
        lib.Topics.Add(dotnet);
        lib.MovePlace(route, dotnet);
        Check(basics.TopicId == dotnet.Id && lib.PlaceOf(basics) == route && !lib.PlacesOf(backend).Any(), "move place");

        // 5) نسخة قديمة نقلت الكورس لموضوع تاني: يرجع تحت موضوعه على طول
        basics.TopicId = backend.Id;
        Check(lib.PlaceOf(basics) == null && lib.DirectCoursesOf(backend).Contains(basics), "stale place id");
        lib.MoveCourse(basics, null, route);

        // 6) الدمج بين جهازين: مكان جديد + نقل كورس على الجهاز التاني
        var other = LecturePlayer.Data.Storage.FromJson(LecturePlayer.Data.Storage.Serialize(lib), libPath);
        var udemy = new LecturePlayer.Data.Place { Name = "Udemy", TopicId = backend.Id };
        udemy.TouchInfo();
        other.Places.Add(udemy);
        var loose = other.Courses.Single(c => c.Id == "c2");
        System.Threading.Thread.Sleep(5);
        other.MoveCourse(loose, null, udemy);
        var mr = LecturePlayer.Data.LibrarySync.Merge(lib, other);
        var localLoose = lib.Courses.Single(c => c.Id == "c2");
        Check(mr.AddedPlaces == 1 && lib.PlaceOf(localLoose)?.Name == "Udemy", "merge place/move");
        // مكان اتمسح هنا ميرجعش
        var deleted = new HashSet<string> { LecturePlayer.Data.LibrarySync.PlaceKey(udemy) };
        lib.RemovePlace(lib.Places.Single(p => p.Id == udemy.Id));
        LecturePlayer.Data.LibrarySync.Merge(lib, other, deleted);
        Check(lib.Places.All(p => p.Id != udemy.Id), "deleted place came back");

        // 7) حذف مكان: الكورسات بتفضل تحت الموضوع
        lib.RemovePlace(route);
        Check(lib.Courses.Contains(basics) && lib.PlaceOf(basics) == null && basics.TopicId == dotnet.Id, "delete place keeps courses");
        Check(lib.AllGroups.Count() == 1, "groups lost");

        PlaceLinkSelfTest();
        SubSourceSelfTest();
        FollowRulesSelfTest();
        LibraryFilesSelfTest();
    }

    /// <summary>ملف المكتبة: الاسم من جوه الملف، المكان بكلام مفهوم، نسخ الملفات التابعة، وفولدرات النسخ الاحتياطية.</summary>
    private static void LibraryFilesSelfTest()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("library files: " + what); }
        string dir = Path.Combine(Path.GetTempPath(), "masar-smoke-libfiles");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        string a = Path.Combine(dir, "A", "lib.lpl"), b = Path.Combine(dir, "B", "lib.lpl");

        var lib = new LecturePlayer.Data.CourseLibrary { Name = "مكتبة الشغل" };
        var c = new LecturePlayer.Data.Course { Name = "C" };
        var v = new LecturePlayer.Data.VideoItem { Path = Path.Combine(dir, "v.mp4"), Title = "v" };
        c.Groups.Add(new LecturePlayer.Data.LectureGroup { Name = "G", Videos = { v } });
        lib.Courses.Add(c);
        for (int i = 0; i < 300; i++) lib.Courses.Add(new LecturePlayer.Data.Course { Name = "filler " + i }); // ملف كبير
        LecturePlayer.Data.Storage.SaveLibrary(a, lib);
        Check(LecturePlayer.Data.LibraryFiles.PeekName(a) == "مكتبة الشغل", "peek name " + LecturePlayer.Data.LibraryFiles.PeekName(a));
        Check(LecturePlayer.Data.LibraryFiles.PeekName(Path.Combine(dir, "missing.lpl")) == "missing", "peek missing");
        File.WriteAllText(Path.Combine(dir, "broken.lpl"), "{ not json");
        Check(LecturePlayer.Data.LibraryFiles.PeekName(Path.Combine(dir, "broken.lpl")) == "broken", "peek broken");

        Check(LecturePlayer.Data.LibraryFiles.LocationLabel(@"G:\My Drive\WAEL.lpl") == "Google Drive", "loc gdrive");
        Check(LecturePlayer.Data.LibraryFiles.LocationLabel(@"G:\.shortcut-targets-by-id\x\WAEL.lpl") == "Google Drive", "loc shortcut");
        Check(LecturePlayer.Data.LibraryFiles.LocationLabel(@"D:\Dropbox\WAEL.lpl") == "Dropbox", "loc dropbox");
        Check(LecturePlayer.Data.LibraryFiles.LocationLabel(LecturePlayer.Data.Storage.DefaultLibraryPath) == "على الجهاز", "loc local");

        // سجل المذاكرة والنصوص بيتنسخوا، ومش بيكتبوا فوق ملف موجود
        File.WriteAllText(Path.Combine(dir, "A", "lib.sessions.PC1.jsonl"), "{}");
        File.WriteAllText(Path.Combine(dir, "A", "lib.sessions.PC2.jsonl"), "{}");
        string tDir = LecturePlayer.Data.TranscriptStore.Dir(a);
        Directory.CreateDirectory(tDir);
        string key = LecturePlayer.Data.TranscriptStore.Key(v);
        File.WriteAllText(Path.Combine(tDir, key + ".vtt"), "WEBVTT");
        File.WriteAllText(Path.Combine(tDir, key + ".summary.md"), "s");
        File.WriteAllText(Path.Combine(tDir, "ffffffffffffffff.vtt"), "other library's video");
        string target = Path.Combine(dir, "B", "new name.lpl");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(Path.Combine(dir, "B", "new name.sessions.PC2.jsonl"), "keep");
        int n = LecturePlayer.Data.LibraryFiles.CopyCompanions(a, target, lib.AllVideos);
        Check(File.Exists(Path.Combine(dir, "B", "new name.sessions.PC1.jsonl")), "journal copied");
        Check(File.ReadAllText(Path.Combine(dir, "B", "new name.sessions.PC2.jsonl")) == "keep", "journal overwritten");
        string tB = LecturePlayer.Data.TranscriptStore.Dir(target);
        Check(File.Exists(Path.Combine(tB, key + ".vtt")) && File.Exists(Path.Combine(tB, key + ".summary.md")), "transcripts copied");
        Check(!File.Exists(Path.Combine(tB, "ffffffffffffffff.vtt")), "copied another library's transcript");
        Check(n == 3, "companions " + n);

        // نفس اسم الملف في مكانين ← فولدر نسخ احتياطية مختلف
        Check(LecturePlayer.Data.Storage.BackupDir(a) != LecturePlayer.Data.Storage.BackupDir(b), "backup dirs collide");
        Directory.Delete(dir, true);
    }

    /// <summary>
    /// قواعد الفولدر المتابَع بعد أول ربط: اللي المستخدم مسحه مايرجعش، واللي نقله أو رتّبه بإيده يفضل زي ما هو،
    /// والجديد بس هو اللي بيتضاف. وكمان ذاكرة «الملف موجود؟».
    /// </summary>
    private static void FollowRulesSelfTest()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("follow rules: " + what); }
        string root = Path.Combine(Path.GetTempPath(), "masar-smoke-follow", "Teacher");
        if (Directory.Exists(Path.GetDirectoryName(root)!)) Directory.Delete(Path.GetDirectoryName(root)!, true);
        void Touch(string rel) { string f = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, "x"); }
        Touch(@"Course A\Lesson 1\Part 1.mp4");
        Touch(@"Course A\Lesson 1\Part 2.mp4");
        Touch(@"Course A\Lesson 1\Part 3.mp4");
        Touch(@"Course A\Lesson 2\Part 1.mp4");
        Touch(@"Course A\Lesson 2\notes.pdf");
        Touch(@"Course B\Lesson 1\Part 1.mp4");

        var lib = new LecturePlayer.Data.CourseLibrary();
        var link = new LecturePlayer.Data.LinkedFolder { Path = root };
        lib.Links.Add(link);
        LecturePlayer.Data.FolderLink.Snapshot Snap() => LecturePlayer.Data.FolderLink.Scan(root)!;
        LecturePlayer.Data.FolderLink.Apply(lib, link, Snap(), initial: true);
        var a = lib.Courses.Single(c => c.Name == "Course A");
        var l1 = a.Groups.Single(g => g.Name == "Lesson 1");
        var l2 = a.Groups.Single(g => g.Name == "Lesson 2");
        Check(lib.Courses.Count == 2 && l1.Videos.Count == 3, "initial");

        // 1) ترتيب يدوي: Part 3 الأول ← المزامنة متغيّروش
        var p3 = l1.Videos[2];
        l1.Videos.Remove(p3); l1.Videos.Insert(0, p3);
        Check(!LecturePlayer.Data.FolderLink.Apply(lib, link, Snap(), initial: false).Changed && l1.Videos[0] == p3, "manual order kept");

        // 2) نقل فيديو بإيدك لمحاضرة تانية ← يفضل هناك
        var p2 = l1.Videos.Single(v => v.Title == "Part 2");
        l1.Videos.Remove(p2); l2.Videos.Add(p2);
        var r = LecturePlayer.Data.FolderLink.Apply(lib, link, Snap(), initial: false);
        Check(!r.Changed && l2.Videos.Contains(p2) && !l1.Videos.Contains(p2) && lib.AllVideos.Count() == 5, "manual video move kept");

        // 3) نقل محاضرة بإيدك لكورس تاني ← تفضل هناك ومتتكررش
        var b = lib.Courses.Single(c => c.Name == "Course B");
        a.Groups.Remove(l2); b.Groups.Add(l2);
        r = LecturePlayer.Data.FolderLink.Apply(lib, link, Snap(), initial: false);
        Check(!r.Changed && b.Groups.Contains(l2) && a.Groups.Count == 1, "manual lecture move kept");
        // والجديد فيها بيوصلها مكان ما هي
        Touch(@"Course A\Lesson 2\Part 2.mp4");
        r = LecturePlayer.Data.FolderLink.Apply(lib, link, Snap(), initial: false);
        Check(r.NewVideos.Count == 1 && l2.Videos.Count == 3 && r.GroupsWithNew.Single().Course == b, "new video goes to moved lecture");

        // 4) حذف فيديو ومحاضرة وكورس ومرفق ← مايرجعوش
        var p1 = l1.Videos.Single(v => v.Title == "Part 1");
        l1.Videos.Remove(p1); link.Ignored.Add(p1.Path);
        var att = l2.Attachments.Single();
        l2.Attachments.Remove(att); link.Ignored.Add(att.Target);
        // زي حذف المحاضرة من البرنامج: الفولدر وكل فيديو فيها (منهم Part 2 اللي اتنقل لها من Lesson 1)
        link.Ignored.Add(l2.SourceFolder!);
        foreach (var v in l2.Videos) link.Ignored.Add(v.Path);
        b.Groups.Remove(l2);
        lib.Courses.Remove(b); link.Ignored.Add(b.SourceFolder!);
        r = LecturePlayer.Data.FolderLink.Apply(lib, link, Snap(), initial: false);
        Check(!r.Changed && lib.Courses.Count == 1 && a.Groups.Count == 1 && l1.Videos.Count == 1, $"deleted came back: courses {lib.Courses.Count}, new {r.NewVideos.Count}, groups {r.NewGroups}");
        // جديد في محاضرة موجودة لسه بيتضاف
        Touch(@"Course A\Lesson 1\Part 4.mp4");
        r = LecturePlayer.Data.FolderLink.Apply(lib, link, Snap(), initial: false);
        Check(r.NewVideos.Count == 1 && r.NewVideos[0].IsNew, "new still added after ignores");
        // الحفظ والفتح بيحتفظ باللي اتمسح
        var back = LecturePlayer.Data.Storage.FromJson(LecturePlayer.Data.Storage.Serialize(lib), Path.Combine(root, "x.lpl"));
        Check(back.Links.Single().Ignored.Count == link.Ignored.Count && link.Ignored.Count >= 6, "ignored lost on save");

        // 5) ذاكرة «الملف موجود؟»
        string f = Path.Combine(root, "cache.mp4");
        File.WriteAllText(f, "x");
        Check(LecturePlayer.Data.FileCache.Exists(f), "cache: exists");
        File.Delete(f);
        Check(LecturePlayer.Data.FileCache.Exists(f), "cache: remembered");
        LecturePlayer.Data.FileCache.Forget(f);
        Check(!LecturePlayer.Data.FileCache.Exists(f), "cache: forget");
        Directory.Delete(Path.GetDirectoryName(root)!, true);
    }

    /// <summary>مصدر فرعي: منصة ← مدرّس ← كورسات (مستويين بس)، والنقل والحذف والحفظ والتوافق.</summary>
    private static void SubSourceSelfTest()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("sub source: " + what); }
        var lib = new LecturePlayer.Data.CourseLibrary();
        var prog = new LecturePlayer.Data.Topic { Name = "Programming" };
        var other = new LecturePlayer.Data.Topic { Name = "Other" };
        lib.Topics.Add(prog); lib.Topics.Add(other);
        var platform = new LecturePlayer.Data.Place { Name = "Platform", TopicId = prog.Id };
        var t1 = new LecturePlayer.Data.Place { Name = "Teacher 1", TopicId = prog.Id };
        var t2 = new LecturePlayer.Data.Place { Name = "Teacher 2", TopicId = prog.Id };
        lib.Places.AddRange(new[] { platform, t1, t2 });
        lib.NestPlace(t1, platform);
        lib.NestPlace(t2, platform);
        var c1 = new LecturePlayer.Data.Course { Name = "C1" };
        var c2 = new LecturePlayer.Data.Course { Name = "C2" };
        var c3 = new LecturePlayer.Data.Course { Name = "C3" };
        lib.Courses.AddRange(new[] { c1, c2, c3 });
        lib.MoveCourse(c1, null, t1);
        lib.MoveCourse(c2, null, t2);
        lib.MoveCourse(c3, null, platform);

        Check(lib.TopPlacesOf(prog).Single() == platform && lib.ChildrenOf(platform).Count() == 2, "nesting");
        Check(lib.OrderedPlacesOf(prog).Select(x => x.Place.Name + x.Depth).SequenceEqual(new[] { "Platform0", "Teacher 11", "Teacher 21" }), "order");
        Check(lib.AllCoursesUnder(platform).Count() == 3 && lib.CoursesInPlace(platform).Single() == c3, "courses under");
        Check(lib.PathOf(c1) == "Programming › Platform › Teacher 1 › C1", "path " + lib.PathOf(c1));
        Check(!lib.CanNest(t2, t1) && !lib.CanNest(platform, t1) && !lib.CanNest(platform, platform), "two levels only");

        // حفظ وفتح
        string libPath = Path.Combine(Path.GetTempPath(), "masar-smoke-sub.lpl");
        var back = LecturePlayer.Data.Storage.FromJson(LecturePlayer.Data.Storage.Serialize(lib), libPath);
        var bc1 = back.Courses.Single(c => c.Name == "C1");
        Check(back.PathOf(bc1) == "Programming › Platform › Teacher 1 › C1", "save/load");

        // نقل المنصة لموضوع تاني: المدرّسين والكورسات معاها
        lib.MovePlace(platform, other);
        Check(lib.TopicOf(t1) == other && lib.ParentOf(t1) == platform && c1.TopicId == other.Id && lib.PlaceOf(c1) == t1, "move family");
        lib.MovePlace(platform, prog);

        // طلّع مدرّس برا المنصة، ورجّعه
        lib.NestPlace(t2, null);
        Check(lib.ParentOf(t2) == null && lib.TopPlacesOf(prog).Count() == 2 && lib.PlaceOf(c2) == t2, "un-nest");
        lib.NestPlace(t2, platform);

        // مدرّس في موضوع تاني يتحط جوه المنصة ← بياخد موضوعها
        var t3 = new LecturePlayer.Data.Place { Name = "Teacher 3", TopicId = other.Id };
        lib.Places.Add(t3);
        var c4 = new LecturePlayer.Data.Course { Name = "C4" };
        lib.Courses.Add(c4);
        lib.MoveCourse(c4, null, t3);
        lib.NestPlace(t3, platform);
        Check(t3.TopicId == prog.Id && c4.TopicId == prog.Id && lib.PlaceOf(c4) == t3, "nest across topics");

        // حذف مدرّس: كورساته تروح للمنصة. حذف المنصة: المدرّسين يبقوا أساسيين بكورساتهم
        lib.RemovePlace(t3);
        Check(lib.PlaceOf(c4) == platform, "delete sub → courses to parent");
        lib.RemovePlace(platform);
        Check(lib.ParentOf(t1) == null && lib.PlaceOf(c1) == t1 && lib.PlaceOf(c4) == null && c4.TopicId == prog.Id, "delete parent");

        // نسخة قديمة/بيانات بايظة: الأكبر مش موجود ← المصدر يبان أساسي
        var orphan = new LecturePlayer.Data.Place { Name = "Orphan", TopicId = prog.Id, ParentId = "missing" };
        lib.Places.Add(orphan);
        Check(lib.TopPlacesOf(prog).Contains(orphan), "orphan shows at top");
        // ملف من غير ParentId (نسخة قبل كده) بيفتح عادي
        Check(!LecturePlayer.Data.Storage.Serialize(new LecturePlayer.Data.CourseLibrary { Places = { new LecturePlayer.Data.Place { Name = "x" } } }).Contains("ParentId"), "ParentId written when null");
    }

    /// <summary>
    /// ربط فولدرات الدرايف جوه مكان (Backend › ROUTE)، وتحويل الربط القديم (موضوع «Eng Doaa…») لمكان
    /// من غير ما الكورسات تتكرر أو التقدّم يضيع.
    /// </summary>
    private static void PlaceLinkSelfTest()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("place link: " + what); }
        string drive = Path.Combine(Path.GetTempPath(), "masar-smoke-place");
        if (Directory.Exists(drive)) Directory.Delete(drive, true);
        void Touch(string rel) { string f = Path.Combine(drive, rel); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, "x"); }
        Touch(@"Eng Doaa Amin Elfawal\00 Orientation\Videos\Welcome.mp4");
        Touch(@"Eng Doaa Amin Elfawal\01 C# Basics\Session 01\Videos\Part 01 Intro.mp4");
        Touch(@"Eng Doaa Amin Elfawal\01 C# Basics\Session 02\Videos\Part 01 Arrays.mp4");
        Touch(@"ROUTE - Asp.Net\01 MVC\Session 01\Videos\Part 01 Intro.mp4");
        string doaa = Path.Combine(drive, "Eng Doaa Amin Elfawal"), asp = Path.Combine(drive, "ROUTE - Asp.Net");

        // (أ) الربط القديم: موضوع «Eng Doaa…» مربوط بالفولدر، وفيه تقدّم
        var lib = new LecturePlayer.Data.CourseLibrary();
        var old = new LecturePlayer.Data.Topic { Name = "Eng Doaa Amin Elfawal", SourceFolder = doaa };
        lib.Topics.Add(old);
        lib.MigrateLinks(); // زي فتح ملف من 4.1.30: الربط بيتنقل لقايمة الفولدرات المتابَعة
        Check(lib.Links.Single().TopicId == old.Id && old.SourceFolder == null, "migrate topic link");
        LecturePlayer.Data.FolderLink.Apply(lib, old, LecturePlayer.Data.FolderLink.Scan(doaa)!, initial: true);
        var intro = lib.AllVideos.Single(v => v.Title == "Part 01 Intro");
        intro.LastPositionMs = 7000;
        Check(lib.CoursesOf(old).Count() == 2, "old link courses");

        // تحويله لمكان ROUTE جوه Backend
        var backend = new LecturePlayer.Data.Topic { Name = "Backend" };
        lib.Topics.Add(backend);
        var route = new LecturePlayer.Data.Place { Name = "ROUTE", TopicId = backend.Id };
        lib.Places.Add(route);
        int moved = MainForm.ConvertTopicToPlaceCore(lib, old, route);
        Check(moved == 2 && !lib.Topics.Contains(old), "convert moved " + moved);
        Check(lib.CoursesInPlace(route).Select(c => c.Name).SequenceEqual(new[] { "00 Orientation", "01 C# Basics" }), "convert order");
        Check(lib.Links.Single().PlaceId == route.Id && intro.LastPositionMs == 7000, "convert kept link/progress");
        Check(lib.PathOf(lib.Courses.Single(c => c.Name == "01 C# Basics")) == "Backend › ROUTE › 01 C# Basics", "convert path");

        // المزامنة بعد التحويل: مفيش تكرار، والجديد يتحط في مكانه
        Check(!LecturePlayer.Data.FolderLink.Apply(lib, lib.Links[0], LecturePlayer.Data.FolderLink.Scan(doaa)!, initial: false).Changed, "resync changed");
        Touch(@"Eng Doaa Amin Elfawal\01 C# Basics\Session 03\Videos\Part 01 Methods.mp4");
        Touch(@"Eng Doaa Amin Elfawal\02 OOP\Session 01\Videos\Part 01 Classes.mp4");
        var r = LecturePlayer.Data.FolderLink.Apply(lib, route, LecturePlayer.Data.FolderLink.Scan(doaa)!, initial: false);
        Check(r.NewCourses == 1 && r.NewGroups == 2 && r.NewVideos.Count == 2, "place update");
        Check(lib.CoursesInPlace(route).Count() == 3 && lib.Courses.Count == 3, "place update courses");

        // (ب) فولدر تاني في نفس المكان
        lib.Links.Add(new LecturePlayer.Data.LinkedFolder { Path = asp, TopicId = backend.Id, PlaceId = route.Id });
        var r2 = LecturePlayer.Data.FolderLink.Apply(lib, route, LecturePlayer.Data.FolderLink.Scan(asp)!, initial: true);
        Check(r2.NewCourses == 1 && lib.CoursesInPlace(route).Count() == 4, "second folder");

        // (ج) كورس مربوط اتنقل بإيدك لمكان تاني: المزامنة متكررهوش
        var udemy = new LecturePlayer.Data.Place { Name = "Udemy", TopicId = backend.Id };
        lib.Places.Add(udemy);
        var mvc = lib.Courses.Single(c => c.Name == "01 MVC");
        lib.MoveCourse(mvc, null, udemy);
        LecturePlayer.Data.FolderLink.Apply(lib, route, LecturePlayer.Data.FolderLink.Scan(asp)!, initial: false);
        Check(lib.Courses.Count(c => c.Name == "01 MVC") == 1 && lib.PlaceOf(mvc) == udemy, "moved linked course duplicated");

        // (د) الحفظ والفتح تاني
        var back = LecturePlayer.Data.Storage.FromJson(LecturePlayer.Data.Storage.Serialize(lib), Path.Combine(drive, "x.lpl"));
        Check(back.Links.Count == 2 && back.Links.All(l => l.PlaceId == route.Id), "links lost on save");
        Check(!LecturePlayer.Data.Storage.Serialize(back).Contains("SourceFolders"), "legacy field written");

        // (هـ) نوع الفولدر: فولدر المدرّس فيه كذا كورس، وفولدر الكورس فيه محاضرات
        Check(LecturePlayer.Data.FolderLink.LooksLikeManyCourses(doaa), "instructor folder = many courses");
        string basicsDir = Path.Combine(doaa, "01 C# Basics");
        Check(!LecturePlayer.Data.FolderLink.LooksLikeManyCourses(basicsDir), "course folder = one course");
        var one = LecturePlayer.Data.FolderLink.ScanSingle(basicsDir)!;
        Check(one.Courses.Count == 1 && one.Courses[0].Sessions.Count == 3, "single course scan " + one.Courses.Count);

        // (و) نفس الملف من مسارين للدرايف (My Drive و shortcut-targets): يتعرف بتقدّمه ومايتكررش
        string myDrive = Path.Combine(drive, "My Drive", "Eng Nada Soliman"), shortcut = Path.Combine(drive, ".shortcut-targets-by-id", "abc", "Eng Nada Soliman");
        string rel = Path.Combine("01 Intro", "Session 01", "Videos", "Part 01.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(myDrive, rel))!);
        File.WriteAllText(Path.Combine(myDrive, rel), "same video");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(shortcut, rel))!);
        File.Copy(Path.Combine(myDrive, rel), Path.Combine(shortcut, rel));
        File.SetLastWriteTimeUtc(Path.Combine(shortcut, rel), File.GetLastWriteTimeUtc(Path.Combine(myDrive, rel)));
        var lib2 = new LecturePlayer.Data.CourseLibrary();
        var manual = LecturePlayer.Data.VideoItem.FromFile(Path.Combine(myDrive, rel));
        manual.Watched = true;
        var manualCourse = new LecturePlayer.Data.Course { Name = "NADA" };
        manualCourse.Groups.Add(new LecturePlayer.Data.LectureGroup { Name = "LEC1", Videos = { manual } });
        lib2.Courses.Add(manualCourse);
        var r3 = LecturePlayer.Data.FolderLink.Apply(lib2, (string?)null, null, LecturePlayer.Data.FolderLink.Scan(shortcut)!, initial: true);
        Check(r3.Moved == 1 && r3.NewVideos.Count == 0 && lib2.AllVideos.Count() == 1 && manual.Watched, $"same file other path: moved {r3.Moved}, new {r3.NewVideos.Count}");
        Check(LecturePlayer.Data.FolderLink.Norm(manual.Path) == LecturePlayer.Data.FolderLink.Norm(Path.Combine(shortcut, rel)), "path updated to working one");
        // ملف تاني بنفس الاسم بس محتوى مختلف (نسخة تانية): مايتلخبطش معاه
        string other = Path.Combine(drive, "Copy", "Eng Nada Soliman");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(other, rel))!);
        File.WriteAllText(Path.Combine(other, rel), "a different, longer video file");
        var r4 = LecturePlayer.Data.FolderLink.Apply(lib2, (string?)null, null, LecturePlayer.Data.FolderLink.Scan(other)!, initial: true);
        Check(r4.NewVideos.Count == 1 && lib2.AllVideos.Count() == 2 && lib2.Courses.Count == 2, $"different copy merged: courses {lib2.Courses.Count}");
        Check(lib2.Courses.Count(c => LecturePlayer.Data.FolderLink.Norm(c.SourceFolder!).Contains(".shortcut-targets-by-id")) == 1, "original course lost its folder");
        Directory.Delete(drive, true);
    }
}
