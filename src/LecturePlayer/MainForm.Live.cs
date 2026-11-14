using LecturePlayer.Data;
using LecturePlayer.UI;
using LibVLCSharp.Shared;

namespace LecturePlayer;

/// <summary>
/// البث المباشر (راديو وتليفزيون) جوه نفس المشغل:
/// مالوش تقدم ولا «اتشاف» ولا بيتحسب في وقت المذاكرة، وبيعيد الاتصال لوحده لو النت قطع.
/// </summary>
internal sealed partial class MainForm
{
    private LiveChannel? _currentLive;
    private string? _liveNowPlaying;
    private int _liveRetries;
    private bool _liveTriedResolver;
    private LiveForm? _liveForm;
    private DateTime? _sleepAtUtc;
    private readonly RadioVisual _radioVisual = new();
    private bool _liveReconnecting;
    private static readonly HttpClient LogoHttp = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>
    /// شاشة الراديو مكان الفيديو: دايمًا للراديو، وللتليفزيون وهو بيتصل أو لو البث مفيهوش صورة.
    /// بتتنادى من التايمر (4 مرات في الثانية).
    /// </summary>
    private const int MaxLiveRetries = 5;
    private long _liveLastTime = -1;
    private DateTime _liveLastProgressUtc = DateTime.UtcNow;
    private DateTime? _livePlayingSinceUtc;

    private void ResetLiveWatchdog()
    {
        _liveLastTime = -1;
        _liveLastProgressUtc = DateTime.UtcNow;
        _livePlayingSinceUtc = null;
    }

    /// <summary>
    /// مراقب البث: لو الصورة/الصوت وقفوا من غير ما VLC يقول إن فيه خطأ (البث «اتجمّد»)، نعيد الاتصال برابط جديد.
    /// ولو البث فضل شغال كويس دقيقة ونص، عدّاد المحاولات بيرجع صفر (القطع اللي بعد كده يتعامل كأول مرة).
    /// </summary>
    private void LiveWatchdog()
    {
        if (_currentLive == null || _liveReconnecting) { _liveLastProgressUtc = DateTime.UtcNow; return; }
        var state = _mp.State;
        var now = DateTime.UtcNow;
        if (state is not (VLCState.Playing or VLCState.Buffering or VLCState.Opening))
        {
            // واقف بإرادة المستخدم أو لسه بيجهّز الرابط: مفيش مراقبة
            _liveLastProgressUtc = now;
            _livePlayingSinceUtc = null;
            return;
        }

        long t = _mp.Time;
        if (t != _liveLastTime)
        {
            _liveLastTime = t;
            _liveLastProgressUtc = now;
            if (state == VLCState.Playing) _livePlayingSinceUtc ??= now;
            if (_livePlayingSinceUtc is { } since && now - since > TimeSpan.FromSeconds(90)) _liveRetries = 0;
            return;
        }

        var limit = state == VLCState.Opening ? TimeSpan.FromSeconds(45) : TimeSpan.FromSeconds(20);
        if (now - _liveLastProgressUtc > limit)
        {
            Log.Info($"live stalled ({state}) for {(now - _liveLastProgressUtc).TotalSeconds:0}s: {_currentLive.Url}");
            ResetLiveWatchdog();
            TryRecoverLive();
        }
    }

    private void UpdateRadioVisual()
    {
        LiveWatchdog();
        var ch = _currentLive;
        bool show = false;
        if (ch != null)
        {
            var state = _mp.State;
            bool playing = state == VLCState.Playing;
            show = ch.Kind == LiveKind.Radio || !playing || _mp.VideoTrackCount <= 0;
            _radioVisual.SetStatus(_liveReconnecting ? RadioVisual.Status.Reconnecting
                : playing ? RadioVisual.Status.Playing
                : state == VLCState.Paused ? RadioVisual.Status.Paused
                : RadioVisual.Status.Connecting);
            if (playing) _liveReconnecting = false;
        }
        if (_radioVisual.Visible != show) _radioVisual.Visible = show;
    }

    private async Task LoadLogoAsync(LiveChannel ch)
    {
        if (string.IsNullOrWhiteSpace(ch.Logo) || !Uri.TryCreate(ch.Logo, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
        try
        {
            using var resp = await LogoHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength > 2_000_000) return;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length > 2_000_000) return;
            Image img;
            try { img = Image.FromStream(new MemoryStream(bytes)); } catch { return; } // svg أو صيغة مش مدعومة: نسيب الأيقونة
            if (_currentLive == ch && !_closing) _radioVisual.SetLogo(img); else img.Dispose();
        }
        catch { /* اللوجو مش مهم */ }
    }

    private void ShowLiveWindow()
    {
        if (_liveForm == null || _liveForm.IsDisposed)
        {
            _liveForm = new LiveForm(new LiveForm.Host(
                Library: () => _library,
                Play: PlayLive,
                Checkpoint: Checkpoint,
                Forget: c => _deletedKeys.Add(LibrarySync.LiveKey(c)),
                Changed: () => { MarkDirty(); SaveLibrary(); }));
            _liveForm.Show(this);
        }
        else
        {
            if (_liveForm.WindowState == FormWindowState.Minimized) _liveForm.WindowState = FormWindowState.Normal;
            _liveForm.Activate();
        }
    }

    internal void PlayLive(LiveChannel ch)
    {
        if (FocusActive) { ShowStatus("وضع التركيز شغال 🎯"); return; }

        // نقفل الفيديو الحالي (بعد ما نحفظ مكانه) ونشغّل البث مكانه
        StopPlaybackCore(clearCurrent: true);
        _welcome.Visible = false;
        _currentLive = ch;
        _liveNowPlaying = null;
        _liveRetries = 0;
        _liveTriedResolver = false;
        _liveReconnecting = false;
        ApplyRateSilently(1f);
        string subtitle = string.Join("  ·  ", new[] { ch.Group, FirstTags(ch.Tags) }.Where(s => !string.IsNullOrWhiteSpace(s)));
        _radioVisual.Start(ch.Name, subtitle, ch.Kind == LiveKind.Tv);
        _radioVisual.Visible = true;
        _ = LoadLogoAsync(ch);
        int request = ++_playRequest;
        _ = StartLiveAsync(ch, request, useResolver: !M3u.IsDirectStream(ch.Url));
        UpdateNowPlaying();
        UpdateTitle();
        if (ch.StationUuid != null) _ = RadioBrowser.ClickAsync(ch.StationUuid);
    }

    private async Task StartLiveAsync(LiveChannel ch, int request, bool useResolver)
    {
        string url = ch.Url;
        string? userAgent = null, referer = null, audio = null;
        try
        {
            if (useResolver)
            {
                // يوتيوب لايف وصفحات البث: yt-dlp بيجيب رابط البث نفسه
                _liveTriedResolver = true;
                if (!await EnsureYtDlpAsync()) return;
                ShowStatus("بجهّز البث…", 60);
                var stream = await ResolveWithAutoUpdateAsync(url, YtDlp.StreamMode.Muxed, CancellationToken.None,
                    () => request == _playRequest && !_closing);
                if (stream == null || request != _playRequest || _closing || _currentLive != ch) return;
                url = stream.VideoUrl; audio = stream.AudioUrl; userAgent = stream.UserAgent; referer = stream.Referer;
                // البث المباشر: القايمة الرئيسية بكل الجودات بدل جودة واحدة ثابتة،
                // فـ VLC بينزل لجودة أقل لوحده لما النت يضعف بدل ما الصورة تقف
                if (stream.IsLive && stream.ManifestUrl != null && audio == null) url = stream.ManifestUrl;
            }
            if (request != _playRequest || _closing || _currentLive != ch) return;

            var options = new List<string>
            {
                ":network-caching=8000", ":http-reconnect",
                // بث HLS: مخزون أكبر وتأخير أكبر شوية عن اللحظة الحالية = تقطيع أقل
                ":adaptive-livedelay=20000", ":adaptive-maxbuffer=60000"
            };
            if (_settings.OnlineMaxHeight > 0) options.Add(":adaptive-maxheight=" + _settings.OnlineMaxHeight);
            if (audio != null) options.Add(":input-slave=" + audio);
            if (userAgent != null) options.Add(":http-user-agent=" + userAgent);
            if (referer != null) options.Add(":http-referrer=" + referer);

            var media = new Media(_libVLC, url, FromType.FromLocation, options.ToArray());
            media.MetaChanged += (_, e) =>
            {
                if (e.MetadataType is not (MetadataType.NowPlaying or MetadataType.Title)) return;
                string? now = null;
                try { now = media.Meta(MetadataType.NowPlaying); } catch { }
                Ui(() =>
                {
                    if (_currentLive != ch || _currentMedia != media) return;
                    _liveNowPlaying = Clean(now);
                    _radioVisual.SetNowPlaying(_liveNowPlaying);
                    UpdateNowPlaying();
                });
            };
            var old = _currentMedia;
            _currentMedia = media;
            _mp.Play(media);
            old?.Dispose();

            _timeLabel.Text = "● مباشر";
            ResetLiveWatchdog();
            _liveReconnecting = false; // من هنا المراقب بيتابع المحاولة دي كمان (لو علّقت وهي بتفتح)
            _seekBar.Value = 0;
            ShowStatus(ch.Kind == LiveKind.Radio ? $"📻 {ch.Name}" : $"📺 {ch.Name}", 4);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (request != _playRequest || _closing) return;
            Log.Error($"live {ch.Url}", ex);
            ShowStatus("البث ده مش راضي يشتغل", 6);
            Dialogs.Error(this, $"مقدرتش أشغّل «{ch.Name}»:\n\n{YtDlp.Explain(ex.Message).Message}");
        }
    }

    private static string? FirstTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return null;
        var parts = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(3);
        return string.Join("، ", parts);
    }

    /// <summary>اللي المحطة بتبعته كاسم «اللي بيتذاع» — نتجاهل الفاضي والعلامات لوحدها زي «-».</summary>
    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().Trim('-', '|', '.', ' ');
        if (!s.Any(char.IsLetterOrDigit)) return null;
        return s.Length > 120 ? s[..120] + "…" : s;
    }

    /// <summary>البث وقف (النت قطع، أو السيرفر قفل): نحاول تاني كذا مرة، وبعدين نجرّب yt-dlp لو مجرّبناهوش.</summary>
    private bool TryRecoverLive()
    {
        var ch = _currentLive;
        if (ch == null) return false;

        if (_liveRetries < MaxLiveRetries)
        {
            _liveRetries++;
            _liveReconnecting = true;
            int attempt = _liveRetries;
            ShowStatus($"البث وقف… بحاول أرجع ({attempt}/{MaxLiveRetries})", 10);
            int request = ++_playRequest;
            _ = Task.Delay(TimeSpan.FromSeconds(2 * attempt)).ContinueWith(_ => Ui(() =>
            {
                if (_currentLive == ch && request == _playRequest)
                    _ = StartLiveAsync(ch, request, useResolver: !M3u.IsDirectStream(ch.Url));
            }));
            return true;
        }
        if (!_liveTriedResolver)
        {
            // رابط صفحة مش رابط بث: yt-dlp يمكن يعرف يطلّع البث منها
            _liveTriedResolver = true;
            _ = StartLiveAsync(ch, ++_playRequest, useResolver: true);
            return true;
        }
        ShowStatus("البث ده مش متاح دلوقتي", 8);
        Dialogs.Error(this, $"«{ch.Name}» مش متاح دلوقتي.\n\nممكن المحطة/القناة قافلة البث، أو الرابط اتغيّر، أو النت مش ثابت.");
        return true;
    }

    private void ToggleLive()
    {
        var ch = _currentLive!;
        switch (_mp.State)
        {
            case VLCState.Playing: _mp.SetPause(true); break;
            case VLCState.Paused: _mp.SetPause(false); break;
            case VLCState.Opening:
            case VLCState.Buffering: break;
            default:
                _liveRetries = 0;
                _ = StartLiveAsync(ch, ++_playRequest, useResolver: !M3u.IsDirectStream(ch.Url));
                break;
        }
        UpdatePlayButton();
    }

    private string LiveLabel()
    {
        var ch = _currentLive!;
        string icon = ch.Kind == LiveKind.Radio ? "📻" : "📺";
        return _liveNowPlaying != null ? $"{icon} {ch.Name}   ›   🎵 {_liveNowPlaying}" : $"{icon} {ch.Name}   ›   مباشر";
    }

    // =====================================================================
    //  مؤقت النوم
    // =====================================================================

    private void SetSleepTimer(int minutes)
    {
        _sleepAtUtc = minutes > 0 ? DateTime.UtcNow.AddMinutes(minutes) : null;
        ShowStatus(minutes > 0 ? $"😴 هيقفل بعد {minutes} دقيقة" : "اتلغى مؤقت النوم", 5);
    }

    private void SleepTick()
    {
        if (_sleepAtUtc is not { } at || DateTime.UtcNow < at) return;
        _sleepAtUtc = null;
        if (_mp.IsPlaying) _mp.SetPause(true);
        UpdatePlayButton();
        ShowStatus("😴 مؤقت النوم وقّف التشغيل", 30);
    }

    private ToolStripMenuItem BuildLiveMenu()
    {
        var menu = SubMenu("بث مباشر");
        var sleep = SubMenu("مؤقت النوم");
        foreach (int m in new[] { 15, 30, 45, 60, 90 })
            sleep.DropDownItems.Add(Item($"بعد {m} دقيقة", () => SetSleepTimer(m)));
        sleep.DropDownItems.Add(new ToolStripSeparator());
        sleep.DropDownItems.Add(Item("إلغاء", () => SetSleepTimer(0)));

        menu.DropDownOpening += (_, _) =>
        {
            menu.DropDownItems.Clear();
            menu.DropDownItems.Add(Item("📻 راديو وتليفزيون…", ShowLiveWindow, display: "Ctrl+Shift+L"));
            var favs = _library.Live.Where(l => l.Favorite).Take(20).ToList();
            if (favs.Count > 0)
            {
                menu.DropDownItems.Add(new ToolStripSeparator());
                foreach (var f in favs)
                {
                    var it = Item((f.Kind == LiveKind.Radio ? "📻 " : "📺 ") + f.Name, () => PlayLive(f));
                    it.Checked = _currentLive == f;
                    menu.DropDownItems.Add(it);
                }
            }
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(sleep);
            sleep.Text = _sleepAtUtc is { } at ? $"مؤقت النوم ({Math.Max(1, (int)Math.Ceiling((at - DateTime.UtcNow).TotalMinutes))} د)" : "مؤقت النوم";
            var stop = Item("إيقاف البث", GoHome);
            stop.Enabled = _currentLive != null;
            menu.DropDownItems.Add(stop);
        };
        menu.DropDownItems.Add(new ToolStripMenuItem("…")); // عشان السهم يظهر قبل أول فتح
        return menu;
    }
}
