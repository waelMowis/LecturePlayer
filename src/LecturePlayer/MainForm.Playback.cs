using System.Globalization;
using LecturePlayer.Data;
using LecturePlayer.UI;
using LibVLCSharp.Shared;

namespace LecturePlayer;

/// <summary>التشغيل: تشغيل فيديو، التالي/السابق داخل المحاضرة، الاستكمال، السرعة والصوت.</summary>
internal sealed partial class MainForm
{
    private LectureGroup? _currentGroup;
    private VideoItem? _currentVideo;
    private Media? _currentMedia;
    private bool _muted;

    private const int MaxVolume = 200;   // أكتر من 100 = تضخيم
    private float _activeRate = 1f;      // سرعة الفيديو الحالي (سرعة الكورس لو ليه سرعة خاصة)
    private bool _applyingRate;

    // =====================================================================
    //  أحداث VLC (بتيجي من Thread تاني ← لازم نرجع للواجهة بـ BeginInvoke
    //  وممنوع ننده أي دالة من دوال المشغل جوه الحدث نفسه)
    // =====================================================================

    private void WirePlayerEvents()
    {
        _mp.Playing += (_, _) => Ui(OnPlaying);
        _mp.Paused += (_, _) => Ui(UpdatePlayButton);
        _mp.Stopped += (_, _) => Ui(UpdatePlayButton);
        _mp.EndReached += (_, _) => Ui(OnEndReached);
        _mp.EncounteredError += (_, _) => Ui(() =>
        {
            // فيديو من النت: نجرّب رابط جديد بطريقة تانية قبل ما نقول إنه فشل
            if (TryRecoverLive()) return;
            if (!TryRecoverOnlineError()) ShowStatus("الفيديو ده مش راضي يشتغل (الملف ممكن يكون بايظ)", 8);
        });
        _mp.LengthChanged += (_, e) =>
        {
            long length = e.Length;
            Ui(() => OnLengthChanged(length));
        };
    }

    private void Ui(Action action)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { } // يشمل ObjectDisposedException (الفورم اتقفل)
    }

    private void OnPlaying()
    {
        _mp.SetRate(_activeRate);
        _mp.Volume = _settings.Volume;
        _mp.Mute = _muted;
        UpdatePlayButton();
        AttachTranscriptSubtitle();
        PrepareSilence();
    }

    private void OnLengthChanged(long length)
    {
        var v = _currentVideo;
        if (v == null || length <= 0 || Math.Abs(v.DurationMs - length) < 1000) return;
        v.DurationMs = length;
        MarkDirty();
        RefreshVideo(v);
        RefreshGroup(_currentGroup);
        UpdateSeekMarkers();
    }

    private void OnEndReached()
    {
        // البث المباشر مالوش آخر: لو «خلص» يبقى الاتصال وقع
        if (_currentLive != null) { TryRecoverLive(); return; }
        // ممكن يكون المستخدم شغّل فيديو تاني قبل ما الحدث يوصل
        if (_mp.State != VLCState.Ended || _currentVideo == null) return;

        var v = _currentVideo;
        if (!v.Watched) LogWatched(v);
        v.Watched = true;
        v.LastPositionMs = 0;
        v.Touch();
        MarkDirty();
        RefreshVideo(v);
        RefreshGroup(_currentGroup);
        _seekBar.Value = 1;

        if (_settings.AutoPlayNext) PlayNext(auto: true);
        else UpdatePlayButton();
    }

    // =====================================================================
    //  تشغيل
    // =====================================================================

    /// <param name="startAtMs">null = استكمال من آخر مكان (لو مفعّل)، 0 = من البداية</param>
    private void PlayVideo(LectureGroup g, VideoItem v, long? startAtMs = null)
    {
        if (!IsAvailable(v))
        {
            if (Dialogs.Confirm(this, $"الملف مش موجود:\n{v.Path}\n\nتحب تحدد مكانه الجديد؟"))
            {
                RelocateMissing(v);
                if (IsAvailable(v)) PlayVideo(g, v, startAtMs);
            }
            return;
        }

        // تشغيل جديد من المستخدم: محاولات الإنقاذ بتاعة فيديوهات النت تبدأ من الأول
        _onlineRetryFor = null;
        _currentLive = null; // لو كان فيه بث مباشر شغال
        _liveNowPlaying = null;
        _directTriedFor = null;

        SaveCurrentPosition();
        CommitNotes();
        ClearAbLoop();
        var previousVideo = _currentVideo;
        var previousGroup = _currentGroup;
        _currentGroup = g;
        _currentVideo = v;
        if (v.IsNew) { v.IsNew = false; v.Touch(); MarkDirty(); }
        _subtitleAttachedFor = null;
        _silenceFor = null;

        // سرعة الكورس (كل كورس بيفتكر سرعته)
        ApplyRateSilently(_library.FindCourseOf(g)?.Rate ?? _settings.Rate);

        long start = startAtMs ?? (
            _settings.ResumePosition && !v.Watched && v.LastPositionMs > 3000 &&
            (v.DurationMs <= 0 || v.LastPositionMs < v.DurationMs - 5000)
                ? v.LastPositionMs
                : 0);

        _welcome.Visible = false;
        int request = ++_playRequest;

        string? localFile = PlayableLocalFile(v); // فيديو نت متنزّل = يتشغل من الجهاز
        if (v.IsOnline && localFile == null)
        {
            // فيديو من النت: نوقف اللي شغال ونجيب رابط التشغيل في الخلفية
            if (_mp.State is not (VLCState.NothingSpecial or VLCState.Stopped)) _mp.Stop();
            _ = PlayOnlineAsync(request, v, start);
        }
        else
        {
            _resolveCts?.Cancel();
            string[] options = start > 0
                ? new[] { ":start-time=" + (start / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) }
                : Array.Empty<string>();

            var media = new Media(_libVLC, localFile ?? v.LocalPath, FromType.FromPath, options);
            var oldMedia = _currentMedia;
            _currentMedia = media;
            _mp.Play(media);
            oldMedia?.Dispose();

            if (start > 0) ShowStatus($"استكمال من {TimeFormat.Format(start)}");
        }

        _library.LastGroupId = g.Id;
        _library.LastVideoPath = v.Path;
        MarkDirty();

        long len = v.DurationMs;
        _seekBar.Value = len > 0 ? (double)start / len : 0;
        _timeLabel.Text = $"{TimeFormat.Format(start)} / {TimeFormat.Format(len)}";

        RefreshVideo(previousVideo);
        if (previousGroup != g) RefreshGroup(previousGroup);
        RefreshVideo(v);
        RefreshGroup(g);

        if (_groupNodes.TryGetValue(g, out var gn) && !gn.IsExpanded) gn.Expand();
        if (_videoNodes.TryGetValue(v, out var vn))
        {
            _tree.SelectedNode = vn;
            vn.EnsureVisible();
        }

        UpdateNowPlaying();
        UpdateTitle();
        RefreshNotesPane();
    }

    private void PlayNode(TreeNode node, bool fromStart)
    {
        if (node.Tag is not VideoItem v) return;
        var g = _library.FindGroupOf(v);
        if (g != null) PlayVideo(g, v, fromStart ? 0 : null);
    }

    /// <summary>تشغيل محاضرة: من أول فيديو لسه متشافش (أو من الأول لو كلها اتشافت).</summary>
    private void PlayGroup(LectureGroup g)
    {
        var v = g.Videos.FirstOrDefault(x => !x.Watched && IsAvailable(x))
                ?? g.Videos.FirstOrDefault(IsAvailable);
        if (v == null)
        {
            ShowStatus(g.Videos.Count == 0 ? "المحاضرة دي فاضية — ضيف لها فيديوهات" : "ملفات المحاضرة دي مش موجودة");
            return;
        }
        PlayVideo(g, v, g.IsCompleted ? 0 : null);
    }

    /// <summary>تشغيل كورس: يكمّل من المحاضرة اللي فيها فيديوهات لسه متشافتش.</summary>
    private void PlayCourse(Course c)
    {
        MaybeShowRecap(c);
        // لو آخر فيديو اتشغل من الكورس ده ولسه مخلصش ← كمّل منه
        var lastGroup = c.Groups.FirstOrDefault(x => x.Id == _library.LastGroupId);
        var lastVideo = lastGroup?.Videos.FirstOrDefault(x =>
            string.Equals(x.Path, _library.LastVideoPath, StringComparison.OrdinalIgnoreCase));
        if (lastGroup != null && lastVideo != null && !lastVideo.Watched && IsAvailable(lastVideo))
        {
            PlayVideo(lastGroup, lastVideo);
            return;
        }

        var g = c.Groups.FirstOrDefault(x => !x.IsCompleted && x.Videos.Any(IsAvailable))
                ?? c.Groups.FirstOrDefault(x => x.Videos.Count > 0);
        if (g == null) { ShowStatus("الكورس ده فاضي — ضيف له محاضرات"); return; }
        PlayGroup(g);
    }

    /// <summary>Enter / Space من غير ما يكون في حاجة شغالة.</summary>
    private void PlaySelected()
    {
        switch (_tree.SelectedNode?.Tag)
        {
            case VideoItem:
                PlayNode(_tree.SelectedNode, fromStart: false);
                return;
            case LectureGroup g:
                PlayGroup(g);
                return;
            case Course c:
                PlayCourse(c);
                return;
            case Topic t:
                PlayTopic(t);
                return;
        }

        var lastGroup = _library.AllGroups.FirstOrDefault(x => x.Id == _library.LastGroupId);
        var lastVideo = lastGroup?.Videos.FirstOrDefault(x => string.Equals(x.Path, _library.LastVideoPath, StringComparison.OrdinalIgnoreCase));
        if (lastGroup != null && lastVideo != null) { PlayVideo(lastGroup, lastVideo); return; }

        var first = _library.AllGroups.FirstOrDefault(x => x.Videos.Count > 0);
        if (first != null) PlayGroup(first);
        else ShowStatus("القايمة فاضية — اسحب فولدر الكورس هنا أو اضغط «استيراد فولدر»", 6);
    }

    private void TogglePlay()
    {
        if (_currentLive != null) { ToggleLive(); return; }
        if (_resolvingVideo != null) { ShowStatus("لسه بجهّز الفيديو من النت…"); return; }
        if (_currentVideo == null || _currentMedia == null) { PlaySelected(); return; }

        switch (_mp.State)
        {
            case VLCState.Playing:
                _mp.SetPause(true);
                break;
            case VLCState.Paused:
                _mp.SetPause(false);
                break;
            case VLCState.Opening:
            case VLCState.Buffering:
                break;
            default: // Stopped / Ended / Error
                var g = _currentGroup ?? _library.FindGroupOf(_currentVideo);
                if (g != null) PlayVideo(g, _currentVideo, _mp.State == VLCState.Ended ? 0 : null);
                break;
        }
        UpdatePlayButton();
    }

    private void StopPlayback() => StopPlaybackCore(clearCurrent: false);

    private void StopPlaybackCore(bool clearCurrent)
    {
        SaveCurrentPosition();
        _playRequest++;            // يلغي أي فيديو نت لسه بيتجهّز
        _resolveCts?.Cancel();
        if (_mp.State != VLCState.NothingSpecial && _mp.State != VLCState.Stopped) _mp.Stop();

        _seekBar.Value = 0;
        _timeLabel.Text = "00:00 / 00:00";
        ClearAbLoop();

        if (clearCurrent)
        {
            _currentLive = null;
            _liveNowPlaying = null;
            var previousVideo = _currentVideo;
            var previousGroup = _currentGroup;
            _currentVideo = null;
            _currentGroup = null;
            _currentMedia?.Dispose();
            _currentMedia = null;
            RefreshVideo(previousVideo);
            RefreshGroup(previousGroup);
            _nowPlayingLabel.Text = "";
            _welcome.Visible = true;
            RefreshNotesPane();
            RefreshHome();
        }

        UpdatePlayButton();
        UpdateTitle();
    }

    private void SaveCurrentPosition()
    {
        var v = _currentVideo;
        if (v == null || _currentMedia == null) return;
        if (_mp.State is not (VLCState.Playing or VLCState.Paused)) return;

        long t = _mp.Time;
        if (t <= 0) return;
        if (Math.Abs(v.LastPositionMs - t) > 1000) v.Touch();
        v.LastPositionMs = t;
        MarkDirty();
        RefreshVideo(v);
    }

    // =====================================================================
    //  التالي / السابق (جوه المحاضرة)
    // =====================================================================

    private void PlayNext(bool auto)
    {
        if (_currentVideo == null || _currentGroup == null)
        {
            if (!auto) PlaySelected();
            return;
        }

        var g = _currentGroup;
        int i = g.Videos.IndexOf(_currentVideo);
        var nextInGroup = g.Videos.Skip(i + 1).FirstOrDefault(IsAvailable);
        if (nextInGroup != null)
        {
            PlayVideo(g, nextInGroup);
            return;
        }

        // خلصت المحاضرة ← المحاضرة اللي بعدها في نفس الكورس (مش بنعدّي لكورس تاني)
        var course = _library.FindCourseOf(g);
        var courseGroups = course?.Groups ?? new List<LectureGroup> { g };
        int gi = courseGroups.IndexOf(g);
        var nextGroup = courseGroups.Skip(gi + 1).FirstOrDefault(x => x.Videos.Any(IsAvailable));

        if (nextGroup != null && (!auto || _settings.ContinueToNextGroup))
        {
            nextGroup.Expanded = true;
            PlayVideo(nextGroup, nextGroup.Videos.First(IsAvailable));
            ShowStatus($"المحاضرة التالية: {nextGroup.Name}", 5);
            return;
        }

        if (auto)
        {
            ShowStatus(nextGroup != null
                ? $"خلصت «{g.Name}» ✓  —  PageDown عشان تبدأ «{nextGroup.Name}»"
                : $"خلصت «{g.Name}» ✓  —  دي آخر محاضرة في كورس «{course?.Name}» 🎉", 15);
            UpdatePlayButton();
        }
        else
        {
            ShowStatus($"ده آخر فيديو في كورس «{course?.Name}»");
        }
    }

    private void PlayPrevious()
    {
        if (_currentVideo == null || _currentGroup == null) { PlaySelected(); return; }

        // لو عدّى أكتر من 5 ثواني ← يرجع لأول الفيديو (زي أي مشغل)
        if (_mp.State is (VLCState.Playing or VLCState.Paused) && _mp.Time > 5000)
        {
            _mp.Time = 0;
            return;
        }

        var g = _currentGroup;
        int i = g.Videos.IndexOf(_currentVideo);
        if (i > 0)
        {
            PlayVideo(g, g.Videos[i - 1], 0);
            return;
        }

        var courseGroups = _library.FindCourseOf(g)?.Groups ?? new List<LectureGroup> { g };
        int gi = courseGroups.IndexOf(g);
        var previousGroup = courseGroups.Take(Math.Max(gi, 0)).LastOrDefault(x => x.Videos.Count > 0);
        if (previousGroup != null) PlayVideo(previousGroup, previousGroup.Videos[^1], 0);
        else ShowStatus("ده أول فيديو في الكورس");
    }

    // =====================================================================
    //  التقديم والصوت والسرعة
    // =====================================================================

    private bool IsActive => _currentMedia != null && _mp.State is (VLCState.Playing or VLCState.Paused);

    private long CurrentLength()
    {
        long len = IsActive ? _mp.Length : 0;
        return len > 0 ? len : _currentVideo?.DurationMs ?? 0;
    }

    private void SeekBy(long deltaMs)
    {
        if (!IsActive || !_mp.IsSeekable) return;
        long len = _mp.Length;
        long t = Math.Max(0, _mp.Time + deltaMs);
        if (len > 0) t = Math.Min(t, len - 500);
        NoteSeek(_mp.Time, t);
        _mp.Time = t;
        if (len > 0) _seekBar.Value = (double)t / len;
        ShowStatus((deltaMs > 0 ? "⏩ +" : "⏪ −") + (Math.Abs(deltaMs) / 1000) + " ثانية", 1);
    }

    private void SeekToFraction(double fraction)
    {
        if (IsActive && _mp.Length > 0)
        {
            long target = (long)(fraction * _mp.Length);
            NoteSeek(_mp.Time, target);
            _mp.Time = target;
            return;
        }

        // المشغل واقف ← نشغّل الفيديو الحالي من المكان ده
        if (_currentVideo != null)
        {
            var g = _currentGroup ?? _library.FindGroupOf(_currentVideo);
            long dur = _currentVideo.DurationMs;
            if (g != null) PlayVideo(g, _currentVideo, dur > 0 ? (long)(fraction * dur) : 0);
        }
    }

    private void SetVolume(int volume)
    {
        volume = Math.Clamp(volume, 0, MaxVolume);
        _settings.Volume = volume;
        _mp.Volume = volume;
        if (_muted && volume > 0) { _muted = false; _mp.Mute = false; }
        if (!_volumeBar.IsDragging) _volumeBar.Value = volume / (double)MaxVolume;
        _volumeBar.FillColor = volume > 100 ? Theme.Folder : Theme.Accent; // لون مختلف للتضخيم
        UpdateMuteButton();
        ShowStatus(volume > 100 ? $"الصوت {volume}% (تضخيم)" : $"الصوت {volume}%", 1.5);
    }

    private void ToggleMute()
    {
        _muted = !_muted;
        _mp.Mute = _muted;
        UpdateMuteButton();
        ShowStatus(_muted ? "الصوت مكتوم" : $"الصوت {_settings.Volume}%", 1.5);
    }

    private void UpdateMuteButton()
    {
        string s = _muted || _settings.Volume == 0 ? Glyphs.Mute : Glyphs.Volume;
        if (_btnMute.Text != s) _btnMute.Text = s;
    }

    /// <summary>
    /// تغيير السرعة من المستخدم: لو فيه كورس شغال بتتحفظ للكورس ده بس،
    /// ولو مفيش بتبقى السرعة العامة.
    /// </summary>
    private void SetRate(float rate)
    {
        if (_applyingRate) return;
        _activeRate = rate;
        _mp.SetRate(rate);

        var course = _currentGroup != null ? _library.FindCourseOf(_currentGroup) : null;
        if (course != null)
        {
            course.Rate = Math.Abs(rate - 1f) < 0.01f ? null : rate;
            MarkDirty();
        }
        else _settings.Rate = rate;

        SelectRateInCombo(rate);
        ShowStatus(course != null ? $"السرعة {RateText(rate)} (لكورس «{course.Name}»)" : $"السرعة {RateText(rate)}", 2);
    }

    /// <summary>تحديد السرعة من غير ما تتسجل كاختيار جديد (وقت فتح فيديو).</summary>
    private void ApplyRateSilently(float rate)
    {
        _activeRate = rate;
        SelectRateInCombo(rate);
    }

    private void SelectRateInCombo(float rate)
    {
        int index = Array.FindIndex(Rates, r => Math.Abs(r - rate) < 0.01f);
        if (index < 0 || _speedCombo.SelectedIndex == index) return;
        _applyingRate = true;
        try { _speedCombo.SelectedIndex = index; }
        finally { _applyingRate = false; }
    }

    private void ChangeRate(int step)
    {
        int index = Array.FindIndex(Rates, r => Math.Abs(r - _activeRate) < 0.01f);
        if (index < 0) index = Array.IndexOf(Rates, 1f);
        SetRate(Rates[Math.Clamp(index + step, 0, Rates.Length - 1)]);
    }

    private void UpdatePlayButton()
    {
        string s = _mp.State == VLCState.Playing ? Glyphs.Pause : Glyphs.Play;
        if (_btnPlay.Text != s) _btnPlay.Text = s;
    }

    // =====================================================================
    //  التايمر (4 مرات في الثانية)
    // =====================================================================

    private void OnUiTick()
    {
        _tick++;
        var v = _currentVideo;

        if (v != null && IsActive)
        {
            long len = _mp.Length, t = _mp.Time;
            if (len > 0 && t >= 0)
            {
                CheckAbLoop(t);
                if (!_seekBar.IsDragging)
                {
                    _seekBar.Value = (double)t / len;
                    _timeLabel.Text = $"{TimeFormat.Format(t)} / {TimeFormat.Format(len)}";
                }

                // كل 5 ثواني: نحفظ مكانك، ولو عدّيت 92% من الفيديو يتعلّم إنه اتشاف
                if (_mp.State == VLCState.Playing && _tick % 20 == 0)
                {
                    v.LastPositionMs = t;
                    v.Touch();
                    if (!v.Watched && t >= len * 0.92)
                    {
                        v.Watched = true;
                        LogWatched(v);
                        RefreshGroup(_currentGroup);
                    }
                    MarkDirty();
                    RefreshVideo(v);
                }
            }
        }

        UpdatePlayButton();
        TrackTick();

        if (_tick % 40 == 0 && _dirty) SaveLibrary(); // حفظ تلقائي كل 10 ثواني لو في تغيير
        else if (_tick % 120 == 0) MergeExternalChanges(); // كل 30 ثانية: هل جهاز تاني عدّل القائمة؟

        if (_statusMessage.Text.Length > 0 && DateTime.Now > _statusMessageUntil) _statusMessage.Text = "";

        if (_fullscreen) UpdateFullscreenChrome();
    }
}
