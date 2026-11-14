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
        ApplyRateSilently(1f);
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
            }
            if (request != _playRequest || _closing || _currentLive != ch) return;

            var options = new List<string> { ":network-caching=4000", ":http-reconnect" };
            if (audio != null) options.Add(":input-slave=" + audio);
            if (userAgent != null) options.Add(":http-user-agent=" + userAgent);
            if (referer != null) options.Add(":http-referrer=" + referer);

            var media = new Media(_libVLC, url, FromType.FromLocation, options.ToArray());
            media.MetaChanged += (_, e) =>
            {
                if (e.MetadataType is not (MetadataType.NowPlaying or MetadataType.Title)) return;
                string? now = null;
                try { now = media.Meta(MetadataType.NowPlaying); } catch { }
                Ui(() => { if (_currentLive == ch && _currentMedia == media) { _liveNowPlaying = Clean(now); UpdateNowPlaying(); } });
            };
            var old = _currentMedia;
            _currentMedia = media;
            _mp.Play(media);
            old?.Dispose();

            _timeLabel.Text = "● مباشر";
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

    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > 120 ? s[..120] + "…" : s;
    }

    /// <summary>البث وقف (النت قطع، أو السيرفر قفل): نحاول تاني كذا مرة، وبعدين نجرّب yt-dlp لو مجرّبناهوش.</summary>
    private bool TryRecoverLive()
    {
        var ch = _currentLive;
        if (ch == null) return false;

        if (_liveRetries < 3)
        {
            _liveRetries++;
            int attempt = _liveRetries;
            ShowStatus($"البث وقف… بحاول أرجع ({attempt}/3)", 10);
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
