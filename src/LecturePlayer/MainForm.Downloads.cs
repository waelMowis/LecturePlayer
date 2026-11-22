using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

internal enum DownloadStatus { Queued, Running, Paused, Done, Failed, Canceled }

/// <summary>تنزيل واحد في القايمة.</summary>
internal sealed class DownloadJob
{
    public DownloadJob(PendingDownload spec) => Spec = spec;
    public PendingDownload Spec { get; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public DownloadProgress? Progress { get; set; }
    public string? Stage { get; set; }
    public string? File { get; set; }
    public string? Error { get; set; }
    internal CancellationTokenSource? Cts { get; set; }
    /// <summary>الإيقاف بسبب قفل البرنامج (يكمّل المرة الجاية) مش إلغاء من المستخدم.</summary>
    internal bool StoppedForClose { get; set; }
    /// <summary>ملفات التنزيل المؤقتة (بتتمسح لو المستخدم لغى).</summary>
    internal readonly List<string> Partials = new();
    internal TaskCompletionSource<bool>? Finished { get; set; }

    public bool Active => Status is DownloadStatus.Queued or DownloadStatus.Running;
    public DownloadOptions Options => new(Spec.Kind, Spec.MaxHeight, Spec.Mp3Kbps, Spec.EmbedCover, Spec.EmbedChapters, Spec.Subtitles);

    public event Action? Changed;
    private long _lastNotify;
    public void Notify() { _lastNotify = Environment.TickCount64; Changed?.Invoke(); }
    /// <summary>للتقدم: مش أكتر من 4 مرات في الثانية (الواجهة مش محتاجة أكتر، والبطارية تشكر).</summary>
    public void NotifyThrottled() { if (Environment.TickCount64 - _lastNotify >= 250) Notify(); }
}

/// <summary>
/// التنزيل من يوتيوب: قايمة انتظار (واحد ورا التاني، أو اتنين لو المستخدم عايز)، تقدم وسرعة ووقت فاضل،
/// إلغاء/إعادة، ويكمّل بعد قفل البرنامج من مكان ماوقف.
/// </summary>
internal sealed partial class MainForm
{
    private readonly List<DownloadJob> _downloadJobs = new();
    private readonly ToolStripStatusLabel _statusDownloads = new() { Visible = false, IsLink = false };
    private DownloadsPanel? _downloadsPanel;

    internal IReadOnlyList<DownloadJob> DownloadJobs => _downloadJobs;
    /// <summary>تنزيل اتضاف أو اتشال (اللوحة بتعيد رسم القايمة).</summary>
    internal event Action? DownloadListChanged;

    // =====================================================================
    //  نقط البداية
    // =====================================================================

    /// <summary>«تنزيل من يوتيوب…» (Ctrl+D): الرابط من الحافظة لو فيها رابط.</summary>
    internal async void OpenDownloadDialog(string? url)
    {
        if (!await EnsureYtDlpAsync() || !await EnsureFfmpegAsync("تنزيل يوتيوب (دمج الصورة والصوت والتحويل لـ MP3)")) return;
        if (url == null)
        {
            try
            {
                string clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
                if (clip.Length < 500 && !clip.Contains('\n') && MediaFiles.IsUrl(YtDlp.NormalizeUrl(clip))) url = clip;
            }
            catch { /* الحافظة مشغولة */ }
        }
        var plan = DownloadForm.Show(this, _settings, url ?? "", null, null);
        if (plan != null) EnqueuePlan(plan);
    }

    /// <summary>كليك يمين على فيديو/محاضرة يوتيوب في المكتبة.</summary>
    private async void DownloadFromLibrary(IReadOnlyList<VideoItem> videos, string name)
    {
        var online = videos.Where(v => v.IsOnline).ToList();
        if (online.Count == 0) { ShowStatus("مفيش فيديوهات يوتيوب هنا"); return; }
        if (!await EnsureYtDlpAsync() || !await EnsureFfmpegAsync("تنزيل يوتيوب (دمج الصورة والصوت والتحويل لـ MP3)")) return;
        var items = online.Select(v => new DownloadForm.Item(v.Path, v.Title, v.DurationMs, v.Path)).ToList();
        var plan = DownloadForm.Show(this, _settings, null, items, name);
        if (plan != null) EnqueuePlan(plan);
    }

    /// <summary>
    /// تنزيل كورس كامل: كل فيديوهات يوتيوب في كل محاضراته بترتيبها. اللي متنزل قبل كده (جوه المكتبة) بيتخطّى،
    /// وفي فولدر: فولدر باسم الكورس وجواه فولدر لكل محاضرة.
    /// </summary>
    internal async void DownloadCourse(Course c)
    {
        var items = CourseDownloadItems(c, out int already, out int local);
        if (items.Count == 0)
        {
            ShowStatus(already > 0 ? $"كل فيديوهات «{c.Name}» متنزلة قبل كده ✓" : "مفيش فيديوهات يوتيوب في الكورس ده");
            return;
        }
        if (!await EnsureYtDlpAsync() || !await EnsureFfmpegAsync("تنزيل يوتيوب (دمج الصورة والصوت والتحويل لـ MP3)")) return;
        string label = c.Name + (already > 0 ? $" — {already} متنزلين قبل كده اتخطّوا" : "") + (local > 0 ? $" — {local} ملف على جهازك مش محتاجين تنزيل" : "");
        var plan = DownloadForm.Show(this, _settings, null, items, label);
        if (plan != null) EnqueuePlan(plan with { GroupName = c.Name });
    }

    /// <summary>فيديوهات يوتيوب الكورس بترتيبها، كل واحد بمحاضرته ورقمه فيها (من غير اللي متنزل).</summary>
    internal List<DownloadForm.Item> CourseDownloadItems(Course c, out int alreadyDownloaded, out int localFiles)
    {
        var items = new List<DownloadForm.Item>();
        alreadyDownloaded = 0;
        localFiles = 0;
        for (int gi = 0; gi < c.Groups.Count; gi++)
        {
            var g = c.Groups[gi];
            string lecture = $"{gi + 1:00} - {g.Name}";
            for (int vi = 0; vi < g.Videos.Count; vi++)
            {
                var v = g.Videos[vi];
                if (!v.IsOnline) { localFiles++; continue; }
                if (IsDownloaded(v)) { alreadyDownloaded++; continue; }
                items.Add(new DownloadForm.Item(v.Path, v.Title, v.DurationMs, v.Path, lecture, vi + 1));
            }
        }
        return items;
    }

    /// <summary>الفيديو مارضيش يشتغل أونلاين: ينزل (بالاختيارات المعتادة) ويشتغل من الجهاز.</summary>
    private async Task DownloadThenPlayAsync(VideoItem v)
    {
        if (!await EnsureYtDlpAsync() || !await EnsureFfmpegAsync("دمج الصورة والصوت")) return;
        var opts = new DownloadOptions(DownloadKind.Mp4, _settings.OnlineMaxHeight, _settings.DownloadMp3Kbps, _settings.DownloadEmbedCover, _settings.DownloadChapters, false);
        var job = Enqueue(new[] { LibrarySpec(v, opts, YtDlp.EstimateBytes(null, v.DurationMs, opts)) }).FirstOrDefault();
        if (job == null) return;
        job.Finished = new TaskCompletionSource<bool>();
        ShowDownloadsPanel();
        bool ok = await job.Finished.Task;
        if (ok && IsDownloaded(v) && _library.FindGroupOf(v) is { } g && !_closing) PlayVideo(g, v);
    }

    // =====================================================================
    //  القايمة
    // =====================================================================

    private PendingDownload LibrarySpec(VideoItem v, DownloadOptions o, long estimate)
    {
        var course = _library.FindCourseOf(v);
        return Spec(v.Path, v.Title, o, Path.Combine(Storage.AppDataDir, "offline", NotesExporter.SafeFileName(course?.Name ?? "فيديوهات")),
            "%(title).120B [%(id)s].%(ext)s", v.Path, estimate);
    }

    private static PendingDownload Spec(string url, string title, DownloadOptions o, string outDir, string template, string? libraryUrl, long estimate) => new()
    {
        Url = url, Title = title, Kind = o.Kind, MaxHeight = o.MaxHeight, Mp3Kbps = o.Mp3Kbps,
        EmbedCover = o.EmbedCover, EmbedChapters = o.EmbedChapters, Subtitles = o.Subtitles,
        OutDir = outDir, Template = template, LibraryUrl = libraryUrl, EstimatedBytes = estimate
    };

    /// <summary>اختيارات شاشة التنزيل ← تنزيلات في القايمة.</summary>
    internal List<DownloadJob> EnqueuePlan(DownloadForm.Plan plan)
    {
        var specs = new List<PendingDownload>();
        bool playlist = plan.GroupName != null && plan.Items.Count > 1;
        string folder = playlist ? Path.Combine(plan.Folder, NotesExporter.SafeFileName(plan.GroupName!)) : plan.Folder;
        long each = plan.Items.Count == 1 ? plan.EstimatedBytes : 0;
        for (int i = 0; i < plan.Items.Count; i++)
        {
            var it = plan.Items[i];
            long est = each > 0 ? each : YtDlp.EstimateBytes(null, it.DurationMs, plan.Options);
            if (plan.IntoLibrary && it.LibraryUrl != null && _library.AllVideos.FirstOrDefault(v => v.Path == it.LibraryUrl) is { } video)
                specs.Add(LibrarySpec(video, plan.Options, est));
            else if (it.SubFolder != null && plan.GroupName != null)
                // كورس كامل: نفس شكله في المكتبة — فولدر الكورس، وجواه فولدر لكل محاضرة، والفيديوهات مترقمة
                specs.Add(Spec(it.Url, it.Title, plan.Options, Path.Combine(plan.Folder, NotesExporter.SafeFileName(plan.GroupName), NotesExporter.SafeFileName(it.SubFolder)),
                    $"{(it.Order > 0 ? it.Order : i + 1):00} - %(title).140B.%(ext)s", null, est));
            else
                specs.Add(Spec(it.Url, it.Title, plan.Options, folder,
                    playlist ? $"{(it.Order > 0 ? it.Order : i + 1):00} - %(title).140B.%(ext)s" : "%(title).150B.%(ext)s", null, est));
        }
        Storage.SaveSettings(_settings); // الاختيارات اللي اتفتكرت
        var jobs = Enqueue(specs);
        ShowDownloadsPanel();
        return jobs;
    }

    internal List<DownloadJob> Enqueue(IEnumerable<PendingDownload> specs, bool paused = false)
    {
        var added = new List<DownloadJob>();
        foreach (var spec in specs)
        {
            // نفس الفيديو بنفس الاختيارات بيتنزل أصلًا ← مانكررهوش
            if (_downloadJobs.Any(j => (j.Active || j.Status == DownloadStatus.Paused) && j.Spec.Url == spec.Url && j.Spec.Kind == spec.Kind && j.Spec.OutDir == spec.OutDir))
                continue;
            var job = new DownloadJob(spec) { Status = paused ? DownloadStatus.Paused : DownloadStatus.Queued, Stage = paused ? "متوقف — دوس «كمّل»" : "في الانتظار" };
            _downloadJobs.Add(job);
            added.Add(job);
        }
        if (added.Count > 0)
        {
            SavePendingDownloads();
            DownloadListChanged?.Invoke();
            PumpDownloads();
            UpdateDownloadsIndicator();
        }
        return added;
    }

    /// <summary>بيبدأ اللي في الانتظار على قد العدد المسموح.</summary>
    private void PumpDownloads()
    {
        if (_closing) return;
        int parallel = Math.Clamp(_settings.DownloadParallel, 1, 2);
        while (_downloadJobs.Count(j => j.Status == DownloadStatus.Running) < parallel
               && _downloadJobs.FirstOrDefault(j => j.Status == DownloadStatus.Queued) is { } next)
            _ = RunDownloadAsync(next);
    }

    private async Task RunDownloadAsync(DownloadJob job)
    {
        job.Status = DownloadStatus.Running;
        job.Error = null;
        job.Stage = "بيبدأ…";
        job.StoppedForClose = false;
        var cts = job.Cts = new CancellationTokenSource();
        job.Notify();
        UpdateDownloadsIndicator();
        var spec = job.Spec;
        bool ok = false;
        try
        {
            long free = DownloadForm.FreeSpace(spec.OutDir);
            if (free >= 0 && spec.EstimatedBytes > 0 && free < spec.EstimatedBytes + 200L * 1024 * 1024)
                throw new InvalidOperationException($"المساحة مش كفاية: محتاج حوالي {YtDlp.FormatSize(spec.EstimatedBytes)} والفاضي {YtDlp.FormatSize(free)}.");

            var progress = new Progress<DownloadProgress>(p =>
            {
                if (job.Status != DownloadStatus.Running) return;
                job.Progress = p;
                job.Stage = p.Stage;
                job.NotifyThrottled();
                UpdateDownloadsIndicator();
            });
            string file = await YtDlp.DownloadAsync(spec.Url, spec.OutDir, spec.Template, job.Options, progress, cts.Token,
                onUpdate: m => Ui(() => { job.Stage = m; job.Notify(); }),
                onLine: l =>
                {
                    const string dest = "[download] Destination:";
                    if (l.StartsWith(dest, StringComparison.Ordinal)) lock (job.Partials) job.Partials.Add(l[dest.Length..].Trim());
                });
            job.File = file;
            job.Status = DownloadStatus.Done;
            job.Stage = "خلص ✓";
            ok = true;
            Log.Info($"download done: {spec.Url} -> {file}");
            if (spec.LibraryUrl != null)
            {
                _settings.OfflineFiles[spec.LibraryUrl] = file;
                if (_library.AllVideos.FirstOrDefault(v => v.Path == spec.LibraryUrl) is { } v) RefreshVideo(v);
            }
        }
        catch (OperationCanceledException)
        {
            if (job.StoppedForClose)
            {
                job.Status = DownloadStatus.Paused; // يكمّل المرة الجاية
            }
            else
            {
                job.Status = DownloadStatus.Canceled;
                job.Stage = "اتلغى";
                DeletePartials(job);
            }
        }
        catch (Exception ex)
        {
            job.Status = DownloadStatus.Failed;
            job.Error = ex.Message;
            job.Stage = "فشل";
            Log.Error("download", ex);
        }
        finally
        {
            job.Cts = null;
            cts.Dispose();
            if (!_closing)
            {
                SavePendingDownloads();
                job.Notify();
                UpdateDownloadsIndicator();
                job.Finished?.TrySetResult(ok);
                PumpDownloads();
                AnnounceIfAllDone();
            }
        }
    }

    private void AnnounceIfAllDone()
    {
        if (_downloadJobs.Any(j => j.Active)) return;
        var done = _downloadJobs.Where(j => j.Status == DownloadStatus.Done && !_announced.Contains(j)).ToList();
        var failed = _downloadJobs.Where(j => j.Status == DownloadStatus.Failed && !_announced.Contains(j)).ToList();
        if (done.Count + failed.Count == 0) return;
        foreach (var j in done.Concat(failed)) _announced.Add(j);
        string? folder = done.Select(j => Path.GetDirectoryName(j.File)).FirstOrDefault(d => d != null && !d.StartsWith(Storage.AppDataDir, StringComparison.OrdinalIgnoreCase));
        string text = failed.Count == 0 ? $"اتنزّل {done.Count} ✓" : $"اتنزّل {done.Count}، وفشل {failed.Count} — التفاصيل في «التنزيلات»";
        if (folder != null) ShowBanner(text, failed.Count == 0 ? BannerKind.Success : BannerKind.Warning, "افتح الفولدر", () => OpenFolder(folder), 12, "downloads");
        else ShowBanner(text, failed.Count == 0 ? BannerKind.Success : BannerKind.Warning, "التنزيلات", ShowDownloadsPanel, 12, "downloads");
    }
    private readonly HashSet<DownloadJob> _announced = new();

    internal void CancelDownload(DownloadJob job)
    {
        if (job.Status == DownloadStatus.Running) job.Cts?.Cancel();
        else if (job.Status is DownloadStatus.Queued or DownloadStatus.Paused)
        {
            job.Status = DownloadStatus.Canceled;
            job.Stage = "اتلغى";
            DeletePartials(job);
            SavePendingDownloads();
            job.Notify();
            UpdateDownloadsIndicator();
        }
    }

    /// <summary>«حاول تاني» / «كمّل»: بيكمّل من مكان ماوقف (الملف المؤقت لسه موجود).</summary>
    internal void RetryDownload(DownloadJob job)
    {
        if (job.Active) return;
        job.Status = DownloadStatus.Queued;
        job.Stage = "في الانتظار";
        job.Error = null;
        job.Notify();
        SavePendingDownloads();
        PumpDownloads();
        UpdateDownloadsIndicator();
    }

    internal void ResumeAllDownloads()
    {
        HideBanner("downloads-resume");
        foreach (var j in _downloadJobs.Where(j => j.Status == DownloadStatus.Paused).ToList()) RetryDownload(j);
    }

    internal void RemoveDownload(DownloadJob job)
    {
        if (job.Active) return;
        if (job.Status == DownloadStatus.Paused) DeletePartials(job);
        _downloadJobs.Remove(job);
        SavePendingDownloads();
        DownloadListChanged?.Invoke();
        UpdateDownloadsIndicator();
    }

    internal void ClearFinishedDownloads()
    {
        _downloadJobs.RemoveAll(j => j.Status is DownloadStatus.Done or DownloadStatus.Canceled or DownloadStatus.Failed);
        DownloadListChanged?.Invoke();
        UpdateDownloadsIndicator();
    }

    private static void DeletePartials(DownloadJob job)
    {
        List<string> files;
        lock (job.Partials) files = job.Partials.ToList();
        foreach (string f in files)
        {
            try
            {
                string? dir = Path.GetDirectoryName(f);
                if (dir == null || !Directory.Exists(dir)) continue;
                foreach (string p in Directory.GetFiles(dir, Path.GetFileName(f) + ".part*")) File.Delete(p);
                foreach (string p in Directory.GetFiles(dir, Path.GetFileName(f) + ".ytdl")) File.Delete(p);
                // جزء (صورة أو صوت) خلص لوحده بس الدمج ماحصلش: «اسم.f137.mp4»
                if (File.Exists(f) && System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"\.f\d+[^.]*\.\w+$")) File.Delete(f);
            }
            catch (Exception ex) { Log.Info("download cleanup: " + ex.Message); }
        }
    }

    /// <summary>اللي ماخلصش بيتحفظ في الإعدادات عشان يكمّل المرة الجاية.</summary>
    private void SavePendingDownloads()
    {
        _settings.PendingDownloads = _downloadJobs.Where(j => j.Active || j.Status == DownloadStatus.Paused).Select(j => j.Spec).ToList();
        Storage.SaveSettings(_settings);
    }

    /// <summary>بعد الفتح: تنزيلات ماخلصتش المرة اللي فاتت ← في القايمة متوقفة + شريط «كمّل».</summary>
    private void OfferResumeDownloads()
    {
        var pending = _settings.PendingDownloads.ToList();
        if (pending.Count == 0) return;
        Enqueue(pending, paused: true);
        ShowBanner($"فيه {pending.Count} تنزيل ماخلصش المرة اللي فاتت — هيكمّل من مكان ماوقف.", BannerKind.Info,
            "كمّل التنزيل", () => _ = ResumeWithToolsAsync(), 30, "downloads-resume");
    }

    private async Task ResumeWithToolsAsync()
    {
        if (!await EnsureYtDlpAsync() || !await EnsureFfmpegAsync("تنزيل يوتيوب")) return;
        ResumeAllDownloads();
        ShowDownloadsPanel();
    }

    /// <summary>
    /// قبل القفل: لو فيه تنزيل شغال نسأل. لو قفلت: بيقف ويتحفظ ويكمّل المرة الجاية من مكان ماوقف.
    /// false = المستخدم عايز يستنى.
    /// </summary>
    private bool StopDownloadsForClose()
    {
        int active = _downloadJobs.Count(j => j.Active);
        if (active > 0 && !SmokeTest.Active &&
            !Dialogs.Confirm(this, $"فيه {active} تنزيل شغال.\nلو قفلت البرنامج هيقف، والمرة الجاية يكمّل من مكان ماوقف.\n\nأقفل؟", "التنزيلات"))
            return false;
        SavePendingDownloads();
        foreach (var j in _downloadJobs.Where(j => j.Status == DownloadStatus.Running))
        {
            j.StoppedForClose = true;
            j.Cts?.Cancel();
        }
        return true;
    }

    // =====================================================================
    //  شريط الحالة واللوحة
    // =====================================================================

    private void SetupDownloadsIndicator()
    {
        _statusDownloads.ForeColor = Theme.Accent;
        _statusDownloads.Click += (_, _) => ShowDownloadsPanel();
        _statusDownloads.ToolTipText = "التنزيلات";
        _status.Items.Insert(_status.Items.IndexOf(_statusFile), _statusDownloads);
    }

    private long _lastIndicator;
    private void UpdateDownloadsIndicator()
    {
        long now = Environment.TickCount64;
        var running = _downloadJobs.Where(j => j.Status == DownloadStatus.Running).ToList();
        int queued = _downloadJobs.Count(j => j.Status == DownloadStatus.Queued);
        int paused = _downloadJobs.Count(j => j.Status == DownloadStatus.Paused);
        bool show = running.Count + queued + paused > 0;
        if (show && running.Count > 0 && now - _lastIndicator < 500 && _statusDownloads.Visible) return;
        _lastIndicator = now;
        _statusDownloads.Visible = show;
        if (!show) return;
        string pct = running.Count > 0 ? $" {running.Average(j => j.Progress?.Percent ?? 0):0}%" : "";
        _statusDownloads.Text = $"⬇ {running.Count + queued}{pct}" + (paused > 0 ? $" (متوقف {paused})" : "");
    }

    internal void ShowDownloadsPanel()
    {
        if (_downloadsPanel is { IsDisposed: false })
        {
            if (_downloadsPanel.WindowState == FormWindowState.Minimized) _downloadsPanel.WindowState = FormWindowState.Normal;
            _downloadsPanel.Activate();
            return;
        }
        _downloadsPanel = new DownloadsPanel(this, _settings);
        _downloadsPanel.Show(this);
    }

    internal void RevealFile(string file)
    {
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\"")?.Dispose(); }
        catch (Exception ex) { Log.Info("reveal: " + ex.Message); }
    }

    /// <summary>«شغّل» من التنزيلات: لو الفيديو من المكتبة يشتغل في مكانه، وإلا يتفتح كملف.</summary>
    internal void PlayDownloaded(DownloadJob job)
    {
        if (job.File == null || !File.Exists(job.File)) { ShowStatus("الملف مش موجود"); return; }
        if (job.Spec.LibraryUrl != null && _library.AllVideos.FirstOrDefault(v => v.Path == job.Spec.LibraryUrl) is { } v && _library.FindGroupOf(v) is { } g)
        {
            PlayVideo(g, v);
            return;
        }
        OpenExternalFiles(new[] { job.File });
    }
}
