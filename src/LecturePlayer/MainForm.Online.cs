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
            Adv(whole),
            new ToolStripSeparator(),
            Adv(Item("تنزيل أدوات يوتيوب…", () => _ = DownloadToolsAsync(askFirst: true, includeDeno: true))),
            Item("تحديث أداة يوتيوب (لو اليوتيوب وقف يشتغل)", () => _ = UpdateYtDlpAsync()),
            Adv(Item("فتح فولدر الأدوات", () =>
            {
                Directory.CreateDirectory(YtDlp.ToolsDir);
                try { Process.Start("explorer.exe", $"\"{YtDlp.ToolsDir}\""); } catch { }
            })));
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
                "تشغيل فيديوهات اليوتيوب محتاج أدوات مجانية بتتنزل مرة واحدة:\n\n" +
                "• أداة يوتيوب  (حوالي 18 ميجا) ← بتجيب الفيديو من اليوتيوب\n" +
                (includeDeno ? "• أداة مساعدة  (حوالي 40 ميجا) ← يوتيوب بقى محتاجها عشان التشغيل يشتغل صح\n" : "") +
                "\nهيتنزلوا من مصدرهم الرسمي. أنزّلهم دلوقتي؟",
                "أدوات اليوتيوب"))
            return false;

        _downloadingTools = true;
        UseWaitCursor = true;
        try
        {
            var progress = new Progress<string>(m => ShowStatus(m, 30));
            await YtDlp.DownloadToolsAsync(includeDeno, progress, CancellationToken.None);
            HideBanner("tools");
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
        ShowStatus("بحدّث أداة يوتيوب…", 60);
        UseWaitCursor = true;
        try
        {
            string result = await YtDlp.UpdateAsync(CancellationToken.None);
            if (YtDlp.FindDeno() == null &&
                Dialogs.Confirm(this, result + "\n\nالأداة المساعدة مش موجودة، ومن غيرها اليوتيوب ممكن ميشتغلش كويس. أنزّلها؟"))
            {
                await DownloadToolsAsync(askFirst: false, includeDeno: true);
                return;
            }
            Dialogs.Info(this, result, "تحديث أداة يوتيوب");
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
                    ShowStatus("بحدّث أداة يوتيوب وأجرّب تاني…", 120);
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
            ShowStatus("الفيديوهات دي موجودة في المكتبة أصلًا");
        }
        else ShowStatus("");

        if (errors.Count > 0)
            Dialogs.Error(this, "الروابط دي مقدرتش أضيفها:\n\n" + string.Join("\n\n", errors.Take(8)) +
                                "\n\nلو اليوتيوب كله وقف فجأة: منيو إنترنت ← تحديث أداة يوتيوب");

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
            if (request == _playRequest) ShowBanner("فيديوهات النت محتاجة «أدوات يوتيوب» (بتتنزل مرة واحدة)", BannerKind.Warning, "نزّلها",
                () => _ = DownloadToolsAsync(askFirst: false, includeDeno: true), seconds: 0, key: "tools");
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
            if (!stream.IsLive && ProxyUrls(v, stream) is { } proxied)
                PlayStream(proxied.Video, proxied.Audio, null, null, startMs);
            else
            {
                Log.Info("online stream (direct): " + string.Join(" + ", (stream.Parts ?? Array.Empty<StreamPart>()).Select(p => p.Description)));
                PlayStream(stream.VideoUrl, stream.AudioUrl, stream.UserAgent, stream.Referer, startMs);
            }
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

    /// <summary>
    /// روابط يوتيوب العادية (https) بتعدّي على الوسيط المحلي: نفس هيدرز أداة يوتيوب، وأجزاء صغيرة،
    /// ولو الرابط اترفض في النص بيتجدد لوحده. البث (m3u8) بيروح لـ VLC مباشرة.
    /// </summary>
    private (string Video, string? Audio)? ProxyUrls(VideoItem v, OnlineStream stream)
    {
        var parts = stream.Parts;
        if (parts == null || parts.Count == 0 || parts.Count > 2) return null;
        if (parts.Any(p => !p.Protocol.StartsWith("http", StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            var proxy = StreamProxy.Shared;
            string pageUrl = v.Path;
            string Register(StreamPart part)
            {
                string fmt = part.FormatId;
                return proxy.Register(part.Url, part.Headers, async ct =>
                {
                    if (string.IsNullOrEmpty(fmt)) return null;
                    var fresh = await YtDlp.ResolveFormatAsync(pageUrl, fmt, null, ct);
                    var p = fresh.Parts?.FirstOrDefault(x => x.FormatId == fmt) ?? fresh.Parts?.FirstOrDefault();
                    return p == null ? null : (p.Url, p.Headers);
                }, out _);
            }
            var videoPart = parts.FirstOrDefault(p => p.HasVideo) ?? parts[0];
            var audioPart = parts.FirstOrDefault(p => p != videoPart);
            Log.Info("online stream (proxy): " + string.Join(" + ", parts.Select(p => p.Description)));
            return (Register(videoPart), audioPart == null ? null : Register(audioPart));
        }
        catch (Exception ex)
        {
            Log.Error("stream proxy", ex);
            return null;
        }
    }

    private bool _ytdlpAutoUpdated;      // مرة واحدة بس في كل تشغيل للبرنامج
    private VideoItem? _onlineRetryFor;  // الفيديو اللي جربناله الطريقة التانية (ملف واحد)
    private VideoItem? _directTriedFor;  // الفيديو اللي جربناه مباشرة في VLC

    /// <summary>
    /// يجيب رابط التشغيل، ولو الخطأ شكله من النوع اللي بيتحل بتحديث yt-dlp (يوتيوب بيغيّر حاجات كتير)
    /// يحدّث الأداة لوحده ويجرّب تاني مرة واحدة.
    /// </summary>
    private async Task<OnlineStream?> ResolveWithAutoUpdateAsync(string url, YtDlp.StreamMode mode, CancellationToken ct, Func<bool> stillWanted, int? maxHeight = null)
    {
        YtDlp.CookiesBrowser = _settings.OnlineCookiesBrowser;
        try
        {
            return await YtDlp.ResolveAsync(url, maxHeight ?? _settings.OnlineMaxHeight, ct, mode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !_ytdlpAutoUpdated && YtDlp.MightBeFixedByUpdate(ex.Message))
        {
            _ytdlpAutoUpdated = true;
            Log.Error("online-resolve (هيتحدّث yt-dlp ويتجرّب تاني)", ex);
            ShowStatus("يوتيوب غيّر حاجة… بحدّث أداة يوتيوب وأجرّب تاني", 120);
            try { Log.Info("yt-dlp auto-update: " + await YtDlp.UpdateAsync(ct)); }
            catch (Exception uex) when (uex is not OperationCanceledException) { Log.Error("yt-dlp auto-update", uex); }
            if (!stillWanted()) return null;
            return await YtDlp.ResolveAsync(url, maxHeight ?? _settings.OnlineMaxHeight, ct, mode);
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

        // المحاولات بالترتيب (كل فيديو بيبدأ من الأول لما المستخدم يشغّله):
        // 1) ملف واحد (صورة وصوت مع بعض) من نفس النقطة
        // 2) ملف واحد من الأول، وبعد ما يشتغل نروح للنقطة (VLC ساعات بيفشل لو بدأ من نص فيديو طويل)
        // 3) أعلى جودة من الأول وبعدين نروح للنقطة
        if (_onlineRetryFor != v)
        {
            _onlineRetryFor = v;
            _onlineRetryStage = 0;
            _onlineRetryAtMs = v.DurationMs > 0 ? (long)(_seekBar.Value * v.DurationMs) : 0;
            if (_onlineRetryAtMs <= 0) _onlineRetryAtMs = Math.Max(0, v.LastPositionMs);
        }
        long at = _onlineRetryAtMs;
        _onlineRetryStage++;
        int request;
        switch (_onlineRetryStage)
        {
            case 1:
                Log.Info($"vlc error on online video, retrying muxed from {at}ms: {v.Path}");
                request = ++_playRequest;
                _ = PlayOnlineAsync(request, v, at, YtDlp.StreamMode.Muxed);
                return true;
            case 2:
                Log.Info($"vlc error again, retrying muxed from start then seek to {at}ms: {v.Path}");
                _seekAfterPlayingMs = at;
                request = ++_playRequest;
                _ = PlayOnlineAsync(request, v, 0, YtDlp.StreamMode.Muxed);
                return true;
            case 3:
                Log.Info($"vlc error again, retrying best from start then seek to {at}ms: {v.Path}");
                _seekAfterPlayingMs = at;
                request = ++_playRequest;
                _ = PlayOnlineAsync(request, v, 0, YtDlp.StreamMode.Best);
                return true;
        }
        _seekAfterPlayingMs = -1;
        Log.Info($"online video failed after {_onlineRetryStage - 1} retries: {v.Path}");
        ShowOnlineFailure(v, "مشغل الفيديو مقدرش يفتح الرابط اللي الموقع رجّعه.");
        return true;
    }

    private int _onlineRetryStage;
    private long _onlineRetryAtMs;
    /// <summary>نقطة نروحلها أول ما الفيديو يشتغل فعلًا (بدل :start-time اللي ساعات بيوقّع VLC مع روابط النت).</summary>
    private long _seekAfterPlayingMs = -1;

    /// <summary>بتتنده من OnPlaying.</summary>
    private void ApplyDeferredSeek()
    {
        if (_seekAfterPlayingMs <= 0) return;
        long t = _seekAfterPlayingMs;
        _seekAfterPlayingMs = -1;
        try { _mp.Time = t; ShowStatus($"استكمال من {TimeFormat.Format(t)}", 3); } catch { }
    }

    private void ShowOnlineFailure(VideoItem v, string raw)
    {
        var (message, recoverable) = YtDlp.Explain(raw);
        ShowStatus("الفيديو ده مش راضي يشتغل", 6);
        if (recoverable)
        {
            string size = EstimateDownloadSize(v);
            if (Dialogs.Confirm(this,
                    $"مقدرتش أشغّل «{v.Title}» من النت بعد كذا محاولة:\n\n{message}\n\n" +
                    "• «أيوه»: أنزّله على الجهاز وأشغّله منه (أضمن طريقة، ومن غير نت بعد كده)." + size + "\n" +
                    "   تقدر تمسحه بعدين: كليك يمين على الفيديو ← مسح النسخة المتنزلة.\n" +
                    "• «لأ»: أفتحه في المتصفح بدل كده.", "فيديو من النت"))
                _ = DownloadThenPlayAsync(v);
            else
                OpenInBrowser(YtDlp.NormalizeUrl(v.Path));
        }
        else if (Dialogs.Confirm(this, $"مقدرتش أشغّل «{v.Title}»:\n\n{message}\n\nتحب تفتحه في المتصفح؟", "فيديو من النت"))
            OpenInBrowser(YtDlp.NormalizeUrl(v.Path));
    }

    /// <summary>حجم تقريبي للتنزيل حسب المدة والجودة (عشان المستخدم يعرف هياخد قد إيه من الجهاز).</summary>
    private string EstimateDownloadSize(VideoItem v)
    {
        if (v.DurationMs <= 0) return "";
        int h = _settings.OnlineMaxHeight;
        // ميجا في الساعة تقريبًا (فيديو + صوت) لفيديوهات المحاضرات/التلاوات
        double mbPerHour = h is > 0 and <= 360 ? 300 : h is > 0 and <= 480 ? 450 : h is > 0 and <= 720 ? 800 : 1500;
        double mb = mbPerHour * v.DurationMs / 3_600_000.0;
        string text = mb >= 1024 ? $"{mb / 1024:0.#} جيجا" : $"{Math.Max(1, mb):0} ميجا";
        return $"\n   الحجم التقريبي: {text} (تقدر تقلله من منيو إنترنت ← الجودة).";
    }

    private async Task DownloadThenPlayAsync(VideoItem v)
    {
        await DownloadOfflineAsync(new[] { v });
        if (IsDownloaded(v) && _library.FindGroupOf(v) is { } g && !_closing) PlayVideo(g, v);
    }
}
