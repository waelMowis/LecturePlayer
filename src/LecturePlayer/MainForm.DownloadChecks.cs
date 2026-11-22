using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>فحص التنزيل التلقائي (قبل كل إصدار).</summary>
internal sealed partial class MainForm
{
    internal void RunDownloadChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("download: " + what); }

        // ---------- 1) الأوامر ----------
        var mp4 = YtDlp.BuildDownloadArgs(new DownloadOptions(DownloadKind.Mp4, 720, Subtitles: true), "out", "%(title)s.%(ext)s", "ff");
        string a = string.Join(" ", mp4);
        Check(a.Contains("-S res:720,vcodec:h264,acodec:m4a") && a.Contains("--merge-output-format mp4") && a.Contains("--embed-subs")
              && a.Contains("--embed-thumbnail") && a.Contains("--embed-chapters") && a.Contains("--ffmpeg-location ff") && a.Contains("--continue")
              && a.Contains("--print after_move:filepath --no-quiet"), "mp4 args (progress needs --no-quiet with --print): " + a);
        var mp3 = string.Join(" ", YtDlp.BuildDownloadArgs(new DownloadOptions(DownloadKind.Mp3, Mp3Kbps: 192, Subtitles: true, EmbedCover: false), "o", "t", null));
        Check(mp3.Contains("-x --audio-format mp3 --audio-quality 192K") && !mp3.Contains("--embed-subs") && !mp3.Contains("--embed-thumbnail"), "mp3 args: " + mp3);
        var m4a = string.Join(" ", YtDlp.BuildDownloadArgs(new DownloadOptions(DownloadKind.M4a), "o", "t", null));
        Check(m4a.Contains("ba[ext=m4a]/ba/b") && m4a.Contains("--audio-format m4a"), "m4a args: " + m4a);
        var best = string.Join(" ", YtDlp.BuildDownloadArgs(new DownloadOptions(DownloadKind.Mp4, 0), "o", "t", null));
        Check(best.Contains("-S vcodec:h264,acodec:m4a") && !best.Contains("res:"), "best quality args: " + best);

        // ---------- 2) قراية التقدم (صورة + صوت ورا بعض، والترجمة مش جزء) ----------
        var r = new YtDlp.ProgressReader();
        Check(r.Read("[info] abc: Downloading subtitles: ar") == null, "subs info line");
        r.Read("[info] abc: Downloading 1 format(s): 137+140");
        r.Read("[download] Destination: C:\\x\\v.ar.vtt");
        var p = r.Read("[download] Destination: C:\\x\\v.f137.mp4");
        Check(p != null && p.Percent == 0 && p.Stage.Contains("الصورة"), "video part start: " + p);
        p = r.Read(YtDlp.ProgressTag + " 500|1000|NA|2048.5|10");
        Check(p != null && Math.Abs(p.Percent - 25) < 0.1 && p.EtaSeconds == 10 && p.TotalBytes == 1000, "half of video = 25%: " + p);
        r.Read("[download] Destination: C:\\x\\v.f140.m4a");
        p = r.Read(YtDlp.ProgressTag + " 50|NA|100|NA|NA");
        Check(p != null && Math.Abs(p.Percent - 75) < 0.1 && p.Stage.Contains("الصوت"), "half of audio = 75%: " + p);
        p = r.Read("[Merger] Merging formats into \"v.mp4\"");
        Check(p != null && p.Stage.Contains("يدمج") || p != null && p.Stage.Contains("بيدمج"), "merge stage: " + p);
        var r1 = new YtDlp.ProgressReader();
        p = r1.Read(YtDlp.ProgressTag + " 30|60|NA|NA|NA");
        Check(p != null && Math.Abs(p.Percent - 50) < 0.1, "single part: " + p);
        Check(r1.Read("[ExtractAudio] Destination: a.mp3")?.Stage.Contains("الصوت") == true, "extract stage");

        // ---------- 3) الأحجام من الصيغ ----------
        string json = """
        {"title":"T","channel":"C","duration":600,"webpage_url":"u","formats":[
         {"format_id":"137","vcodec":"avc1.640028","acodec":"none","height":1080,"filesize":50000000,"tbr":660},
         {"format_id":"136","vcodec":"avc1.4d401f","acodec":"none","height":720,"filesize":20000000,"tbr":260},
         {"format_id":"247","vcodec":"vp9","acodec":"none","height":720,"filesize":15000000,"tbr":200},
         {"format_id":"140","vcodec":"none","acodec":"mp4a.40.2","filesize":5000000,"tbr":130},
         {"format_id":"251","vcodec":"none","acodec":"opus","filesize":4000000,"tbr":120},
         {"format_id":"sb0","vcodec":"none","acodec":"none","protocol":"mhtml"}]}
        """;
        var info = YtDlp.ParseInfo(json, "u");
        Check(info.Title == "T" && info.Channel == "C" && info.DurationMs == 600_000, "info fields");
        Check(info.Heights.SequenceEqual(new[] { 720, 1080 }), "heights " + string.Join(",", info.Heights));
        Check(YtDlp.EstimateBytes(info, 0, new DownloadOptions(DownloadKind.Mp4, 720)) == 25_000_000, "720 = avc1 720 + m4a");
        Check(YtDlp.EstimateBytes(info, 0, new DownloadOptions(DownloadKind.Mp4, 0)) == 55_000_000, "best = 1080 + m4a");
        Check(YtDlp.EstimateBytes(info, 0, new DownloadOptions(DownloadKind.Mp4, 480)) > 0, "lower than available still estimates");
        Check(YtDlp.EstimateBytes(info, 0, new DownloadOptions(DownloadKind.Mp3, Mp3Kbps: 192)) == 14_400_000, "mp3 192 for 10 min");
        Check(YtDlp.EstimateBytes(info, 0, new DownloadOptions(DownloadKind.M4a)) == 5_000_000, "m4a = original audio");
        Check(YtDlp.EstimateBytes(null, 3_600_000, new DownloadOptions(DownloadKind.Mp4, 720)) > 500_000_000, "no info: estimate from duration");
        Console.WriteLine("SMOKE download units OK");

        // ---------- 4) تنزيل حقيقي (على جهاز الفحص بس: محتاج ينزّل الأدوات من النت) ----------
        if (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("SMOKE download integration skipped (not CI)");
            return;
        }
        RunDownloadIntegration(Check);
    }

    private void RunDownloadIntegration(Action<bool, string> Check)
    {
        void Pump(int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { Application.DoEvents(); Thread.Sleep(20); } }
        bool WaitFor(Func<bool> cond, int seconds)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < until) { if (cond()) return true; Pump(100); }
            return cond();
        }
        T Run<T>(Func<Task<T>> f)
        {
            var t = f();
            while (!t.IsCompleted) Pump(50);
            return t.GetAwaiter().GetResult();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        // الأدوات (نفس اللي البرنامج بينزّله للمستخدم)
        if (YtDlp.FindYtDlp() == null) Run(async () => { await YtDlp.DownloadToolsAsync(false, new Progress<string>(), CancellationToken.None); return 0; });
        if (Tools.FindFfmpeg() == null) Run(async () => { await Tools.DownloadFfmpegAsync(new Progress<string>(), CancellationToken.None); return 0; });
        Check(YtDlp.FindYtDlp() != null && Tools.FindFfmpeg() != null, "tools");
        var enc = Run(() => ProcessRunner.RunAsync(Tools.FindFfmpeg()!, new[] { "-hide_banner", "-encoders" }, TimeSpan.FromSeconds(30)));
        bool lame = enc.StdOut.Contains("libmp3lame");
        Console.WriteLine($"SMOKE download tools ready in {sw.Elapsed.TotalSeconds:0}s, ffmpeg has libmp3lame: {lame}");
        Check(lame, "the ffmpeg build the app downloads has no MP3 encoder (libmp3lame)");

        string formats = SmokeTest.RepoPath(Path.Combine("tests", "formats")) ?? throw new Exception("download: tests/formats missing");
        string root = Path.Combine(Path.GetTempPath(), "masar-smoke-dl");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        string wav = Path.Combine(root, "lecture.wav");
        WriteToneWav(wav, seconds: 40);   // ~3.5 ميجا

        using var server = new TestHttpServer();
        server.Serve("lecture.mp4", Path.Combine(formats, "h264-aac.mp4"), "video/mp4");
        server.Serve("long.wav", wav, "audio/wav", bytesPerSecond: 350_000); // بطيء عشان نلحق نوقفه
        var saved = (_settings.DownloadKind, _settings.DownloadMaxHeight, _settings.DownloadMp3Kbps, _settings.DownloadFolder, _settings.DownloadNoticeShown);
        try
        {
            // ---- أ) من الشاشة: رابط ← MP3 192 في فولدر ----
            string outA = Path.Combine(root, "a");
            DownloadForm.SmokePick = (DownloadKind.Mp3, 192, outA);
            DownloadForm.SmokeShot = f => SaveSmokeShot(f, "download-dialog");
            OpenDownloadDialog(server.Url("lecture.mp4"));
            Check(WaitFor(() => _downloadJobs.Count == 1, 60), "dialog → queue (info: " + DownloadForm.SmokeInfoText + ")");
            var jobA = _downloadJobs[0];
            Check(WaitFor(() => !jobA.Active, 90), "mp3 job finished: " + jobA.Status);
            Check(jobA.Status == DownloadStatus.Done && jobA.File != null && jobA.File.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                  && Path.GetDirectoryName(jobA.File) == outA, $"mp3 done in chosen folder: {jobA.Status} {jobA.File} {jobA.Error}");
            var dA = SmokeTest.DecodeCheck(jobA.File!);
            Check(dA.AudioBlocks > 0 && dA.VideoFrames == 0, $"mp3 decodes as audio: {dA}");
            Check(_settings.DownloadKind == DownloadKind.Mp3 && _settings.DownloadMp3Kbps == 192 && _settings.DownloadNoticeShown, "choices remembered");
            Console.WriteLine($"SMOKE download mp3 OK ({new FileInfo(jobA.File!).Length / 1024} KB, audio blocks {dA.AudioBlocks})");

            // ---- ب) MP4 + M4A مع بعض (اتنين في نفس الوقت) ----
            _settings.DownloadParallel = 2;
            var item = new DownloadForm.Item(server.Url("lecture.mp4"), "Lecture 1", 1000, null);
            var jobsB = EnqueuePlan(new DownloadForm.Plan(new DownloadOptions(DownloadKind.Mp4, 720), new() { item }, false, Path.Combine(root, "b"), null, 0));
            var jobsC = EnqueuePlan(new DownloadForm.Plan(new DownloadOptions(DownloadKind.M4a), new() { item }, false, Path.Combine(root, "c"), null, 0));
            Check(WaitFor(() => _downloadJobs.Count(j => j.Status == DownloadStatus.Running) == 2, 15) || jobsB[0].Status == DownloadStatus.Done, "two at once");
            Check(WaitFor(() => !jobsB[0].Active && !jobsC[0].Active, 90), "mp4/m4a finished");
            Check(jobsB[0].Status == DownloadStatus.Done && jobsB[0].File!.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase), $"mp4 done: {jobsB[0].Error}");
            Check(jobsC[0].Status == DownloadStatus.Done && jobsC[0].File!.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase), $"m4a done: {jobsC[0].Error}");
            var dB = SmokeTest.DecodeCheck(jobsB[0].File!);
            Check(dB.VideoFrames > 0 && dB.AudioBlocks > 0, $"mp4 decodes picture + sound: {dB}");
            var dC = SmokeTest.DecodeCheck(jobsC[0].File!);
            Check(dC.AudioBlocks > 0 && dC.VideoFrames == 0, $"m4a decodes as audio: {dC}");
            _settings.DownloadParallel = 1;
            Console.WriteLine("SMOKE download mp4 + m4a OK");

            // ---- ج) قايمة تشغيل لفولدر: ترقيم وفولدر فرعي ----
            var plItems = new List<DownloadForm.Item> { item, new(server.Url("lecture.mp4") + "?2", "Lecture 2", 1000, null) };
            var jobsPl = EnqueuePlan(new DownloadForm.Plan(new DownloadOptions(DownloadKind.Mp3, Mp3Kbps: 128), plItems, false, Path.Combine(root, "pl"), "My: Playlist?", 0));
            Check(WaitFor(() => jobsPl.All(j => !j.Active), 90) && jobsPl.All(j => j.Status == DownloadStatus.Done), "playlist done: " + string.Join(" | ", jobsPl.Select(j => j.Error)));
            Check(jobsPl.All(j => Path.GetDirectoryName(j.File) == Path.Combine(root, "pl", NotesExporter.SafeFileName("My: Playlist?"))), "playlist subfolder: " + jobsPl[0].File);
            Check(Path.GetFileName(jobsPl[0].File!).StartsWith("01 - ") && Path.GetFileName(jobsPl[1].File!).StartsWith("02 - "), "numbered: " + Path.GetFileName(jobsPl[1].File!));
            Console.WriteLine("SMOKE download playlist OK");

            // ---- د) يقف بسبب قفل البرنامج ← يتحفظ ← يكمّل من مكان ماوقف (Range) ----
            var jobD = EnqueuePlan(new DownloadForm.Plan(new DownloadOptions(DownloadKind.Mp3, Mp3Kbps: 128), new() { new(server.Url("long.wav"), "Long lecture", 40_000, null) }, false, Path.Combine(root, "d"), null, 0))[0];
            Check(WaitFor(() => jobD.Progress?.Percent >= 25, 40), "resume: download started (" + jobD.Progress?.Percent + "%)");
            double before = jobD.Progress!.Percent;
            jobD.StoppedForClose = true;          // نفس اللي بيحصل عند قفل البرنامج
            jobD.Cts?.Cancel();
            Check(WaitFor(() => jobD.Status == DownloadStatus.Paused, 20), "stopped for close → paused: " + jobD.Status);
            SavePendingDownloads();
            Check(_settings.PendingDownloads.Count == 1 && _settings.PendingDownloads[0].Url == jobD.Spec.Url, "pending saved for next run");
            Check(Directory.GetFiles(Path.Combine(root, "d"), "*.part").Length == 1, "partial file kept for resume");
            lock (server.Ranges) server.Ranges.Clear();
            RetryDownload(jobD);
            Check(WaitFor(() => !jobD.Active, 60), "resumed download finished");
            Check(jobD.Status == DownloadStatus.Done, "resume done: " + jobD.Error);
            string[] ranges; lock (server.Ranges) ranges = server.Ranges.ToArray();
            long resumedFrom = ranges.Select(x => long.TryParse(x.Split('=')[1].Split('-')[0], out long v) ? v : 0).DefaultIfEmpty(0).Max();
            Check(resumedFrom > 200_000, $"resumed from the middle (Range {string.Join(",", ranges)}) after {before:0}%");
            var dD = SmokeTest.DecodeCheck(jobD.File!);
            Check(dD.AudioBlocks > 0 && dD.DurationMs > 38_000, $"resumed file is whole: {dD}");
            Check(_settings.PendingDownloads.Count == 0, "pending cleared after done");
            Console.WriteLine($"SMOKE download resume OK (stopped at {before:0}%, resumed from byte {resumedFrom})");

            // ---- هـ) إلغاء من المستخدم: الملفات المؤقتة بتتمسح ----
            var jobE = EnqueuePlan(new DownloadForm.Plan(new DownloadOptions(DownloadKind.M4a), new() { new(server.Url("long.wav"), "Cancel me", 40_000, null) }, false, Path.Combine(root, "e"), null, 0))[0];
            Check(WaitFor(() => jobE.Progress?.Percent >= 15, 40), "cancel: started");
            CancelDownload(jobE);
            Check(WaitFor(() => jobE.Status == DownloadStatus.Canceled, 20), "canceled: " + jobE.Status);
            Pump(500);
            var left = Directory.Exists(Path.Combine(root, "e")) ? Directory.GetFiles(Path.Combine(root, "e")) : Array.Empty<string>();
            Check(left.Length == 0, "cancel left files: " + string.Join(", ", left.Select(Path.GetFileName)));
            Console.WriteLine("SMOKE download cancel OK");

            // ---- و) جوه المكتبة: الفيديو بيتربط بالملف ويشتغل منه ----
            var lib = new CourseLibrary { Name = "dl" };
            var course = new Course { Name = "Online course" };
            var g = new LectureGroup { Name = "L" };
            var online = new VideoItem { Path = server.Url("lecture.mp4") + "?lib", Title = "Online lecture", DurationMs = 1000 };
            g.Videos.Add(online);
            course.Groups.Add(g);
            lib.Courses.Add(course);
            string libPath = Path.Combine(root, "dl.lpl");
            Storage.SaveLibrary(libPath, lib);
            LoadLibrary(libPath, remember: false);
            var v = _library.AllVideos.First();
            var jobsF = EnqueuePlan(new DownloadForm.Plan(new DownloadOptions(DownloadKind.Mp4, 720), new() { new(v.Path, v.Title, 1000, v.Path) }, true, root, null, 0));
            Check(WaitFor(() => !jobsF[0].Active, 60) && jobsF[0].Status == DownloadStatus.Done, "library download: " + jobsF[0].Error);
            Check(_settings.OfflineFiles.TryGetValue(v.Path, out var off) && off == jobsF[0].File && off.StartsWith(Path.Combine(Storage.AppDataDir, "offline")), "linked offline file: " + off);
            Check(IsDownloaded(v), "video counts as downloaded");
            _settings.OfflineFiles.Remove(v.Path);
            try { File.Delete(jobsF[0].File!); } catch { }
            Console.WriteLine("SMOKE download into library OK");

            // ---- ز) اللوحة ----
            ShowDownloadsPanel();
            Pump(400);
            Check(_downloadsPanel != null && _downloadsPanel.RowCount == _downloadJobs.Count, $"panel rows {_downloadsPanel?.RowCount} vs {_downloadJobs.Count}");
            SaveSmokeShot(_downloadsPanel!, "downloads-panel");
            ClearFinishedDownloads();
            Pump(200);
            Check(_downloadJobs.Count == 0 && _downloadsPanel.RowCount == 0, "clear finished");
            _downloadsPanel.Close();
            Check(!_statusDownloads.Visible, "status indicator hidden when nothing is downloading");
            Console.WriteLine($"SMOKE download integration OK in {sw.Elapsed.TotalSeconds:0}s");
        }
        finally
        {
            DownloadForm.SmokePick = null;
            DownloadForm.SmokeShot = null;
            foreach (var j in _downloadJobs.Where(j => j.Active).ToList()) j.Cts?.Cancel();
            _downloadJobs.Clear();
            _settings.PendingDownloads.Clear();
            (_settings.DownloadKind, _settings.DownloadMaxHeight, _settings.DownloadMp3Kbps, _settings.DownloadFolder, _settings.DownloadNoticeShown) = saved;
            _settings.DownloadParallel = 1;
        }
    }
}
