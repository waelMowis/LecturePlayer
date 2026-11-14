using System.Diagnostics;
using System.Globalization;
using LecturePlayer.Data;
using LecturePlayer.UI;
using LibVLCSharp.Shared;

namespace LecturePlayer;

/// <summary>
/// فيديوهات من النت (يوتيوب وغيره) عن طريق yt-dlp:
/// إضافة رابط فيديو أو قائمة تشغيل، وتشغيلها جوه المشغل زي أي فيديو.
/// </summary>
internal sealed partial class MainForm
{
    private static readonly (int Height, string Label)[] OnlineQualities =
    {
        (480, "480p (نت ضعيف)"), (720, "720p"), (1080, "1080p"), (0, "أعلى جودة متاحة")
    };

    private bool _importingUrls;
    private bool _downloadingTools;
    private int _playRequest;
    private CancellationTokenSource? _resolveCts;
    private VideoItem? _resolvingVideo;

    private static bool IsAvailable(VideoItem v) => v.IsOnline || File.Exists(v.LocalPath);

    // =====================================================================
    //  المنيو
    // =====================================================================

    private ToolStripMenuItem BuildOnlineMenu()
    {
        var quality = SubMenu("جودة فيديوهات النت");
        foreach (var (height, label) in OnlineQualities)
        {
            int h = height;
            var item = Item(label, () => SetOnlineQuality(h));
            quality.DropDownItems.Add(item);
        }
        quality.DropDownOpening += (_, _) =>
        {
            for (int i = 0; i < OnlineQualities.Length; i++)
                ((ToolStripMenuItem)quality.DropDownItems[i]).Checked = OnlineQualities[i].Height == _settings.OnlineMaxHeight;
        };

        var whole = Item("لو الرابط فيه فيديو وقائمة تشغيل: هات القائمة كلها", () => { });
        whole.Click += (_, _) => { _settings.WholePlaylist = !_settings.WholePlaylist; whole.Checked = _settings.WholePlaylist; };
        whole.Checked = _settings.WholePlaylist;

        return SubMenu("إنترنت",
            Item("إضافة رابط فيديو أو قائمة تشغيل…", () => AddUrlsDialog(null), Keys.Control | Keys.U),
            Item("لصق رابط من الحافظة", PasteUrls, display: "Ctrl+V"),
            new ToolStripSeparator(),
            Item("🔄 تحديث قوائم يوتيوب (هات الفيديوهات الجديدة)", () => _ = SyncPlaylistsAsync(null, quiet: false), Keys.Control | Keys.R),
            new ToolStripSeparator(),
            quality,
            whole,
            new ToolStripSeparator(),
            Item("تنزيل أدوات يوتيوب (yt-dlp + Deno)…", () => _ = DownloadToolsAsync(askFirst: true, includeDeno: true)),
            Item("تحديث yt-dlp (لو اليوتيوب وقف يشتغل)", () => _ = UpdateYtDlpAsync()),
            Item("فتح فولدر الأدوات", () =>
            {
                Directory.CreateDirectory(YtDlp.ToolsDir);
                try { Process.Start("explorer.exe", $"\"{YtDlp.ToolsDir}\""); } catch { }
            }));
    }

    private void SetOnlineQuality(int height)
    {
        _settings.OnlineMaxHeight = height;
        string label = OnlineQualities.First(q => q.Height == height).Label;
        ShowStatus($"جودة فيديوهات النت: {label} (هتطبّق من الفيديو الجاي)", 4);
    }

    // =====================================================================
    //  الأدوات (yt-dlp + Deno)
    // =====================================================================

    /// <summary>بيتأكد إن yt-dlp موجود، ولو مش موجود يعرض تنزيله.</summary>
    private async Task<bool> EnsureYtDlpAsync()
    {
        if (YtDlp.FindYtDlp() != null) return true;
        return await DownloadToolsAsync(askFirst: true, includeDeno: YtDlp.FindDeno() == null);
    }

    private async Task<bool> DownloadToolsAsync(bool askFirst, bool includeDeno)
    {
        if (_downloadingTools) { ShowStatus("التنزيل شغال بالفعل…"); return false; }

        if (askFirst && !Dialogs.Confirm(this,
                "تشغيل فيديوهات اليوتيوب محتاج أداتين مجانيتين (نفس اللي PotPlayer بيستخدمهم):\n\n" +
                "• yt-dlp  (حوالي 18 ميجا) ← بيجيب الفيديو من اليوتيوب\n" +
                (includeDeno ? "• Deno  (حوالي 40 ميجا) ← يوتيوب بقى محتاجه عشان yt-dlp يشتغل صح\n" : "") +
                "\nهيتنزلوا من GitHub الرسمي في:\n" + YtDlp.ToolsDir + "\n\nأنزّلهم دلوقتي؟",
                "أدوات اليوتيوب"))
            return false;

        _downloadingTools = true;
        UseWaitCursor = true;
        try
        {
            var progress = new Progress<string>(m => ShowStatus(m, 30));
            await YtDlp.DownloadToolsAsync(includeDeno, progress, CancellationToken.None);
            ShowStatus("أدوات اليوتيوب جاهزة ✓", 5);
            return true;
        }
        catch (Exception ex)
        {
            Dialogs.Error(this, "مقدرتش أنزّل الأدوات:\n" + ex.Message +
                                "\n\nتقدر تنزّلهم بإيدك وتحطهم في:\n" + YtDlp.ToolsDir +
                                "\n\n" + YtDlp.YtDlpDownloadUrl + "\n" + YtDlp.DenoDownloadUrl);
            return YtDlp.FindYtDlp() != null;
        }
        finally
        {
            _downloadingTools = false;
            UseWaitCursor = false;
        }
    }

    private async Task UpdateYtDlpAsync()
    {
        if (YtDlp.FindYtDlp() == null) { await EnsureYtDlpAsync(); return; }
        ShowStatus("بحدّث yt-dlp…", 60);
        UseWaitCursor = true;
        try
        {
            string result = await YtDlp.UpdateAsync(CancellationToken.None);
            if (YtDlp.FindDeno() == null &&
                Dialogs.Confirm(this, result + "\n\nDeno مش موجود، ومن غيره اليوتيوب ممكن ميشتغلش كويس. أنزّله؟"))
            {
                await DownloadToolsAsync(askFirst: false, includeDeno: true);
                return;
            }
            Dialogs.Info(this, result, "تحديث yt-dlp");
        }
        catch (Exception ex) { Dialogs.Error(this, ex.Message); }
        finally
        {
            UseWaitCursor = false;
            ShowStatus("");
        }
    }

    // =====================================================================
    //  إضافة روابط
    // =====================================================================

    private void AddUrlsDialog(string? prefill)
    {
        if (prefill == null)
        {
            try
            {
                if (Clipboard.ContainsText() && MediaFiles.ExtractUrls(Clipboard.GetText()).Count > 0)
                    prefill = Clipboard.GetText();
            }
            catch { /* الحافظة مشغولة */ }
        }

        var target = SelectedGroup() is { } g ? $"محاضرة «{g.Name}»"
                   : SelectedCourse() is { } c ? $"كورس «{c.Name}»"
                   : "كورس جديد";
        var result = Dialogs.Urls(this, prefill ?? "", target);
        if (result == null) return;
        _ = AddUrlsAsync(result.Value.Urls, _tree.SelectedNode?.Tag, result.Value.PlayFirst);
    }

    private void PasteUrls()
    {
        List<string> urls;
        try { urls = Clipboard.ContainsText() ? MediaFiles.ExtractUrls(Clipboard.GetText()) : new List<string>(); }
        catch { urls = new List<string>(); }

        if (urls.Count == 0) { AddUrlsDialog(null); return; }
        _ = AddUrlsAsync(urls, _tree.SelectedNode?.Tag, playFirst: false);
    }

    /// <summary>
    /// قائمة تشغيل ← محاضرة جديدة باسمها في الكورس المحدد.
    /// فيديو لوحده ← المحاضرة المحددة (أو محاضرة «فيديوهات من النت»).
    /// </summary>
    private async Task AddUrlsAsync(IReadOnlyList<string> urls, object? targetTag, bool playFirst)
    {
        if (urls.Count == 0) return;
        if (_importingUrls) { ShowStatus("استنى، لسه بجيب الروابط اللي قبل كده…"); return; }
        if (!await EnsureYtDlpAsync()) return;

        _importingUrls = true;
        UseWaitCursor = true;

        Course? course = targetTag switch
        {
            Course c => c,
            LectureGroup g => _library.FindCourseOf(g),
            VideoItem v => _library.FindCourseOf(v),
            _ => null
        } ?? _library.Courses.LastOrDefault();
        LectureGroup? singlesGroup = targetTag switch
        {
            LectureGroup g => g,
            VideoItem v => _library.FindGroupOf(v),
            _ => null
        };

        Checkpoint("إضافة روابط");
        var errors = new List<string>();
        int addedVideos = 0, addedPlaylists = 0;
        LectureGroup? firstGroup = null;
        VideoItem? firstVideo = null;

        try
        {
            for (int i = 0; i < urls.Count; i++)
            {
                ShowStatus(urls.Count > 1 ? $"بجيب بيانات الروابط… ({i + 1} من {urls.Count})" : "بجيب بيانات الرابط…", 120);

                OnlineListing listing;
                YtDlp.CookiesBrowser = _settings.OnlineCookiesBrowser;
                try { listing = await YtDlp.ListAsync(urls[i], _settings.WholePlaylist, CancellationToken.None); }
                catch (Exception ex) when (!_ytdlpAutoUpdated && YtDlp.MightBeFixedByUpdate(ex.Message))
                {
                    // يوتيوب غيّر حاجة: نحدّث yt-dlp مرة ونجرّب تاني
                    _ytdlpAutoUpdated = true;
                    Log.Error("list (هيتحدّث yt-dlp ويتجرّب تاني)", ex);
                    ShowStatus("بحدّث yt-dlp وأجرّب تاني…", 120);
                    try { Log.Info("yt-dlp auto-update: " + await YtDlp.UpdateAsync(CancellationToken.None)); } catch (Exception uex) { Log.Error("yt-dlp auto-update", uex); }
                    try { listing = await YtDlp.ListAsync(urls[i], _settings.WholePlaylist, CancellationToken.None); }
                    catch (Exception ex2)
                    {
                        Log.Error($"list {urls[i]}", ex2);
                        errors.Add(urls[i] + "\n   " + YtDlp.Explain(ex2.Message).Message.Replace("\n", "\n   "));
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"list {urls[i]}", ex);
                    errors.Add(urls[i] + "\n   " + YtDlp.Explain(ex.Message).Message.Replace("\n", "\n   "));
                    continue;
                }
                if (_closing) return;
                if (listing.Entries.Count == 0) { errors.Add(urls[i] + "\n   مفيش فيديوهات متاحة"); continue; }

                if (course == null)
                {
                    course = new Course { Name = "من الإنترنت", TopicId = SelectedTopicContext()?.Id };
                    _library.Courses.Add(course);
                }

                LectureGroup destination;
                if (listing.IsPlaylist)
                {
                    destination = new LectureGroup
                    {
                        Name = listing.Title ?? "قائمة تشغيل",
                        SourceUrl = urls[i],             // عشان «تحديث» يجيب الجديد بعدين
                        LastSyncedUtc = DateTime.UtcNow,
                        InfoUpdatedUtc = DateTime.UtcNow
                    };
                    course.Groups.Add(destination);
                    addedPlaylists++;
                }
                else
                {
                    if (singlesGroup == null)
                    {
                        singlesGroup = new LectureGroup { Name = "فيديوهات من النت" };
                        course.Groups.Add(singlesGroup);
                    }
                    destination = singlesGroup;
                }

                var existing = new HashSet<string>(destination.Videos.Select(v => v.Path), StringComparer.OrdinalIgnoreCase);
                foreach (var entry in listing.Entries)
                {
                    if (!existing.Add(entry.Url)) continue;
                    var video = VideoItem.FromUrl(entry);
                    destination.Videos.Add(video);
                    addedVideos++;
                    if (firstVideo == null) { firstVideo = video; firstGroup = destination; }
                }
                destination.Expanded = true;
                course.Expanded = true;
            }
        }
        finally
        {
            _importingUrls = false;
            UseWaitCursor = false;
        }

        if (addedVideos > 0)
        {
            MarkDirty();
            RebuildTree();
            if (firstVideo != null) SelectItem(firstVideo);
            ShowStatus(addedPlaylists > 0
                ? $"اتضاف {addedVideos} فيديو ({addedPlaylists} قائمة تشغيل) ✓"
                : $"اتضاف {addedVideos} فيديو ✓", 5);
        }
        else if (errors.Count == 0)
        {
            ShowStatus("الفيديوهات دي موجودة في القايمة أصلًا");
        }
        else ShowStatus("");

        if (errors.Count > 0)
            Dialogs.Error(this, "الروابط دي مقدرتش أضيفها:\n\n" + string.Join("\n\n", errors.Take(8)) +
                                "\n\nلو اليوتيوب كله وقف فجأة: منيو إنترنت ← تحديث yt-dlp");

        if (playFirst && firstGroup != null && firstVideo != null) PlayVideo(firstGroup, firstVideo);
    }

    /// <summary>رابط اتسحب من المتصفح (من شريط العنوان أو لينك في صفحة).</summary>
    private static List<string> TryGetDroppedUrls(IDataObject? data)
    {
        if (data == null || data.GetDataPresent(DataFormats.FileDrop)) return new List<string>();
        try
        {
            foreach (string format in new[] { "UniformResourceLocatorW", DataFormats.UnicodeText, DataFormats.Text })
            {
                if (!data.GetDataPresent(format)) continue;
                object? raw = data.GetData(format);
                string? text = raw switch
                {
                    string s => s,
                    MemoryStream ms => System.Text.Encoding.Unicode.GetString(ms.ToArray()).TrimEnd('\0'),
                    _ => null
                };
                var urls = MediaFiles.ExtractUrls(text);
                if (urls.Count > 0) return urls;
            }
        }
        catch { }
        return new List<string>();
    }

    private static void OpenInBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // =====================================================================
    //  تشغيل فيديو من النت
    // =====================================================================

    /// <summary>بنجيب رابط التشغيل وقت التشغيل بس (روابط اليوتيوب بتنتهي صلاحيتها بعد ساعات).</summary>
    private async Task PlayOnlineAsync(int request, VideoItem v, long startMs, YtDlp.StreamMode mode = YtDlp.StreamMode.Best)
    {
        _resolveCts?.Cancel();
        var cts = _resolveCts = new CancellationTokenSource();

        if (!await EnsureYtDlpAsync())
        {
            if (request == _playRequest) ShowStatus("فيديوهات النت محتاجة أداة yt-dlp", 6);
            return;
        }

        _resolvingVideo = v;
        ShowStatus(mode == YtDlp.StreamMode.Muxed ? "بجرّب أشغّل الفيديو بطريقة تانية…" : "بجهّز الفيديو من النت…", 90);
        try
        {
            var stream = await ResolveWithAutoUpdateAsync(v.Path, mode, cts.Token, () => request == _playRequest && !_closing);
            if (stream == null || request != _playRequest || _closing) return; // المستخدم اختار فيديو تاني في الوقت ده

            if (stream.DurationMs > 0 && Math.Abs(v.DurationMs - stream.DurationMs) > 1000)
            {
                v.DurationMs = stream.DurationMs;
                MarkDirty();
            }
            PlayStream(stream.VideoUrl, stream.AudioUrl, stream.UserAgent, stream.Referer, startMs);
            RefreshVideo(v);
            RefreshGroup(_currentGroup);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (request != _playRequest || _closing) return;
            Log.Error($"online-play {v.Path}", ex);

            // صفحة yt-dlp مش عارفها: يمكن رابط مباشر لملف فيديو، نسيبه لـ VLC يجرّب
            if (YtDlp.IsUnsupportedUrl(ex.Message) && _directTriedFor != v)
            {
                _directTriedFor = v;
                ShowStatus("بجرّب أشغّل الرابط مباشرة…", 15);
                PlayStream(YtDlp.NormalizeUrl(v.Path), null, null, null, startMs);
                return;
            }
            ShowOnlineFailure(v, ex.Message);
        }
        finally
        {
            if (_resolvingVideo == v) _resolvingVideo = null;
        }
    }

    private bool _ytdlpAutoUpdated;      // مرة واحدة بس في كل تشغيل للبرنامج
    private VideoItem? _onlineRetryFor;  // الفيديو اللي جربناله الطريقة التانية (ملف واحد)
    private VideoItem? _directTriedFor;  // الفيديو اللي جربناه مباشرة في VLC

    /// <summary>
    /// يجيب رابط التشغيل، ولو الخطأ شكله من النوع اللي بيتحل بتحديث yt-dlp (يوتيوب بيغيّر حاجات كتير)
    /// يحدّث الأداة لوحده ويجرّب تاني مرة واحدة.
    /// </summary>
    private async Task<OnlineStream?> ResolveWithAutoUpdateAsync(string url, YtDlp.StreamMode mode, CancellationToken ct, Func<bool> stillWanted)
    {
        YtDlp.CookiesBrowser = _settings.OnlineCookiesBrowser;
        try
        {
            return await YtDlp.ResolveAsync(url, _settings.OnlineMaxHeight, ct, mode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !_ytdlpAutoUpdated && YtDlp.MightBeFixedByUpdate(ex.Message))
        {
            _ytdlpAutoUpdated = true;
            Log.Error("online-resolve (هيتحدّث yt-dlp ويتجرّب تاني)", ex);
            ShowStatus("يوتيوب غيّر حاجة… بحدّث yt-dlp وأجرّب تاني", 120);
            try { Log.Info("yt-dlp auto-update: " + await YtDlp.UpdateAsync(ct)); }
            catch (Exception uex) when (uex is not OperationCanceledException) { Log.Error("yt-dlp auto-update", uex); }
            if (!stillWanted()) return null;
            return await YtDlp.ResolveAsync(url, _settings.OnlineMaxHeight, ct, mode);
        }
    }

    private void PlayStream(string videoUrl, string? audioUrl, string? userAgent, string? referer, long startMs)
    {
        var options = new List<string> { ":network-caching=3000", ":http-reconnect" };
        if (startMs > 0) options.Add(":start-time=" + (startMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
        if (audioUrl != null) options.Add(":input-slave=" + audioUrl);
        if (userAgent != null) options.Add(":http-user-agent=" + userAgent);
        if (referer != null) options.Add(":http-referrer=" + referer);

        var media = new Media(_libVLC, videoUrl, FromType.FromLocation, options.ToArray());
        var oldMedia = _currentMedia;
        _currentMedia = media;
        _mp.Play(media);
        oldMedia?.Dispose();
        ShowStatus(startMs > 0 ? $"استكمال من {TimeFormat.Format(startMs)}" : "", 3);
    }

    /// <summary>
    /// VLC مقدرش يشغّل الرابط (صيغة مش مناسبة، أو الرابط انتهت صلاحيته بعد وقفة طويلة):
    /// نجيب رابط جديد بطريقة «ملف واحد» ونكمّل من نفس النقطة، ولو فشلت كمان نعرض الحلول.
    /// </summary>
    private bool TryRecoverOnlineError()
    {
        var v = _currentVideo;
        if (v == null || !v.IsOnline || PlayableLocalFile(v) != null) return false;

        long at = v.DurationMs > 0 ? (long)(_seekBar.Value * v.DurationMs) : 0;
        if (_onlineRetryFor != v)
        {
            _onlineRetryFor = v;
            Log.Info($"vlc error on online video, retrying muxed: {v.Path}");
            int request = ++_playRequest;
            _ = PlayOnlineAsync(request, v, at, YtDlp.StreamMode.Muxed);
            return true;
        }
        ShowOnlineFailure(v, "مشغل الفيديو مقدرش يفتح الرابط اللي الموقع رجّعه.");
        return true;
    }

    private void ShowOnlineFailure(VideoItem v, string raw)
    {
        var (message, recoverable) = YtDlp.Explain(raw);
        ShowStatus("الفيديو ده مش راضي يشتغل", 6);
        if (recoverable)
        {
            if (Dialogs.Confirm(this,
                    $"مقدرتش أشغّل «{v.Title}» من النت:\n\n{message}\n\n" +
                    "تحب أنزّله على الجهاز وأشغّله منه؟ (أضمن طريقة، وبعدها يشتغل من غير نت)", "فيديو من النت"))
                _ = DownloadThenPlayAsync(v);
        }
        else if (Dialogs.Confirm(this, $"مقدرتش أشغّل «{v.Title}»:\n\n{message}\n\nتحب تفتحه في المتصفح؟", "فيديو من النت"))
            OpenInBrowser(YtDlp.NormalizeUrl(v.Path));
    }

    private async Task DownloadThenPlayAsync(VideoItem v)
    {
        await DownloadOfflineAsync(new[] { v });
        if (IsDownloaded(v) && _library.FindGroupOf(v) is { } g && !_closing) PlayVideo(g, v);
    }
}
