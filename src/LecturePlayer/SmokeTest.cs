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
            LibVLCSharp.Shared.Core.Initialize();
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

        var topic = new LecturePlayer.Data.Topic { Name = "Eng Doaa Amin Elfawal", SourceFolder = root };
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
}
