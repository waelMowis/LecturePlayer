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
            // 5) تحليل Range
            if (LecturePlayer.Data.StreamProxy.ParseRange("bytes=10-") != (10, null)) throw new Exception("ParseRange");
        }
        finally
        {
            stop.Cancel();
            upstream.Stop();
        }
    }
}
