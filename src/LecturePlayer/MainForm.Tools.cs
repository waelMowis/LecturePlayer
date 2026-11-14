using System.Diagnostics;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// المرحلة 1 (الإصدار 1.5):
/// التراجع والإعادة، المشغل الصغير، معاينة شريط التقديم، تنزيل فيديوهات يوتيوب للمشاهدة من غير نت،
/// تنزيل ffmpeg، والتحقق من التحديثات.
/// </summary>
internal sealed partial class MainForm
{
    // =====================================================================
    //  التراجع والإعادة (Ctrl+Z / Ctrl+Y)
    // =====================================================================

    private readonly UndoStack _undo = new(30);

    /// <summary>قبل أي تعديل في هيكل القايمة.</summary>
    private void Checkpoint(string label)
    {
        CommitNotes();
        _undo.Push(label, Storage.Serialize(_library));
    }

    private void UndoLast()
    {
        var item = _undo.Undo(Storage.Serialize(_library));
        if (item == null) { ShowStatus("مفيش حاجة ترجع فيها"); return; }
        ApplySnapshot(item.Value.Snapshot);
        ShowStatus($"تراجعت عن: {item.Value.Label}  (Ctrl+Y للإعادة)", 4);
    }

    private void RedoLast()
    {
        var item = _undo.Redo(Storage.Serialize(_library));
        if (item == null) { ShowStatus("مفيش حاجة تتعاد"); return; }
        ApplySnapshot(item.Value.Snapshot);
        ShowStatus($"اتعادت: {item.Value.Label}", 4);
    }

    private void ApplySnapshot(string json)
    {
        CommitNotes();
        var restored = Storage.FromJson(json, _libraryPath, _settings.PathMappings);
        LibrarySync.CopyNewerStates(restored, _library); // التقدم الأحدث ميضيعش مع التراجع

        // الفيديو الشغال: نلاقي نسخته في القايمة الراجعة
        string? playingKey = _currentVideo != null ? LibrarySync.VideoKey(_currentVideo) : null;
        string? playingGroupId = _currentGroup?.Id;
        _library = restored;

        foreach (string key in _library.Courses.Select(LibrarySync.CourseKey)
                     .Concat(_library.AllGroups.Select(LibrarySync.GroupKey))
                     .Concat(_library.AllVideos.Select(LibrarySync.VideoKey)))
            _deletedKeys.Remove(key); // اللي رجع بالتراجع مبقاش ممسوح

        if (playingKey != null)
        {
            var g = _library.AllGroups.FirstOrDefault(x => x.Id == playingGroupId)
                    ?? _library.AllGroups.FirstOrDefault(x => x.Videos.Any(v => LibrarySync.VideoKey(v) == playingKey));
            var v = g?.Videos.FirstOrDefault(x => LibrarySync.VideoKey(x) == playingKey);
            if (g != null && v != null) { _currentGroup = g; _currentVideo = v; }
            else StopPlaybackCore(clearCurrent: true);
        }

        MarkDirty();
        RebuildTree();
        RefreshNotesPane();
        UpdateNowPlaying();
        UpdateTitle();
    }

    // =====================================================================
    //  المشغل الصغير (P)
    // =====================================================================

    private bool _miniMode;
    private Rectangle _preMiniBounds;
    private FormWindowState _preMiniState;
    private bool _preMiniTopMost;

    private void ToggleMiniMode()
    {
        if (_fullscreen) ToggleFullscreen();

        if (!_miniMode)
        {
            _preMiniState = WindowState;
            _preMiniBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _preMiniTopMost = TopMost;
            _miniMode = true;

            SuspendLayout();
            _menuHost.Visible = _statusHost.Visible = _activityHost.Visible = false;
            _split.Panel2Collapsed = true;
            SetChromeless(true);
            SetMiniControls(true);
            WindowState = FormWindowState.Normal;
            MinimumSize = new Size(LogicalToDeviceUnits(320), LogicalToDeviceUnits(220));
            var area = Screen.FromControl(this).WorkingArea;
            var size = new Size(LogicalToDeviceUnits(520), LogicalToDeviceUnits(340));
            Bounds = new Rectangle(area.Right - size.Width - 16, area.Bottom - size.Height - 16, size.Width, size.Height);
            TopMost = true;
            ResumeLayout(true);
            ShowStatus("المشغل الصغير — P أو Esc أو دبل كليك للرجوع");
        }
        else
        {
            _miniMode = false;
            SuspendLayout();
            MinimumSize = new Size(900, 560);
            Bounds = _preMiniBounds;
            WindowState = _preMiniState;
            TopMost = _preMiniTopMost || _settings.AlwaysOnTop;
            _menuHost.Visible = _statusHost.Visible = _activityHost.Visible = true;
            _split.Panel2Collapsed = !_settings.SidePanelVisible;
            SetChromeless(false);
            SetMiniControls(false);
            ResumeLayout(true);
            ApplySidePanelWidth();
        }
    }

    /// <summary>
    /// المشغل الصغير عرضه ~520 بكسل: شريط التحكم الكامل مش بيكفي فيه وكانت أزراره بتتقص.
    /// نخبّي الحاجات التانوية ونخلّي زرار الرجوع ظاهر ومتعلّم.
    /// </summary>
    private void SetMiniControls(bool mini)
    {
        _speedLabel.Visible = _speedCombo.Visible = _volumeBar.Visible = !mini;
        _btnPlaylist.Visible = _btnStop.Visible = !mini;
        _btnMini.Active = mini;
        _btnMini.Text = mini ? Glyphs.FullScreen : Glyphs.MiniPlayer;
        _toolTip.SetToolTip(_btnMini, mini ? "رجوع للمشغل الكامل (P أو Esc أو دبل كليك)" : "المشغل الصغير (P)");
    }

    /// <summary>كليك يمين على الفيديو: طرق واضحة للتنقل بين الأوضاع.</summary>
    private void ShowVideoMenu(Point at)
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        void Add(string text, string keys, Action act) =>
            menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => act()) { ShortcutKeyDisplayString = keys });
        Add(_miniMode ? "رجوع للمشغل الكامل" : "المشغل الصغير", "P", ToggleMiniMode);
        Add(_fullscreen ? "خروج من ملء الشاشة" : "ملء الشاشة", _fullscreen ? "Esc" : "F11", ToggleFullscreen);
        menu.Items.Add(new ToolStripSeparator());
        Add(TopMost ? "إلغاء «فوق كل الشبابيك»" : "فوق كل الشبابيك", "T", ToggleAlwaysOnTop);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_videoView, at);
    }

    // =====================================================================
    //  معاينة شريط التقديم (صورة من الفيديو)
    // =====================================================================

    private PreviewPopup? _preview;
    private long _previewBucket = -1;
    private CancellationTokenSource? _previewCts;

    /// <summary>بيتنده مع حركة الماوس على الشريط. بيرجّع true لو عرض معاينة.</summary>
    private bool ShowSeekPreview(double fraction, string caption)
    {
        var v = _currentVideo;
        long len = CurrentLength();
        string? file = v == null ? null : PlayableLocalFile(v);
        if (!_settings.SeekPreview || file == null || len <= 0 || Tools.FindFfmpeg() == null) return false;

        long t = (long)(fraction * len);
        long bucket = t / 5000; // صورة لكل 5 ثواني (كاش)
        var anchor = _seekBar.PointToScreen(new Point(_seekBar.PointToClient(Cursor.Position).X, 0));
        var imgSize = new Size(LogicalToDeviceUnits(192), LogicalToDeviceUnits(108));
        _preview ??= new PreviewPopup();

        string cache = Path.Combine(Storage.AppDataDir, "cache", "thumbs", StableHash(file), bucket + ".jpg");
        if (File.Exists(cache))
        {
            _previewBucket = bucket;
            _preview.ShowAt(LoadImageUnlocked(cache), caption, anchor, imgSize);
            return true;
        }

        _preview.ShowAt(null, caption, anchor, imgSize);
        if (bucket == _previewBucket) return true;
        _previewBucket = bucket;
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            await Task.Delay(120, cts.Token);
            bool ok = await Tools.ExtractFrameAsync(file, bucket * 5000, cache, 320, cts.Token);
            if (!ok || cts.IsCancellationRequested) return;
            Ui(() =>
            {
                if (_previewBucket == bucket && _preview is { Visible: true } p)
                    p.ShowAt(LoadImageUnlocked(cache), caption, anchor, imgSize);
            });
        }, cts.Token);
        return true;
    }

    private void HideSeekPreview()
    {
        _previewCts?.Cancel();
        _previewBucket = -1;
        _preview?.Hide();
    }

    /// <summary>بنقرا الصورة في الذاكرة عشان الملف ميفضلش مقفول.</summary>
    private static Image? LoadImageUnlocked(string path)
    {
        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            return Image.FromStream(ms);
        }
        catch { return null; }
    }

    public static string StableHash(string text)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToUpperInvariant()));
        return Convert.ToHexString(bytes)[..16];
    }

    // =====================================================================
    //  فيديوهات يوتيوب للمشاهدة من غير نت
    // =====================================================================

    private CancellationTokenSource? _downloadCts;

    /// <summary>الملف المحلي اللي يتشغّل: فيديو عادي = مكانه، فيديو نت متنزّل = الملف المتنزّل.</summary>
    private string? PlayableLocalFile(VideoItem v)
    {
        if (!v.IsOnline) return File.Exists(v.LocalPath) ? v.LocalPath : null;
        return _settings.OfflineFiles.TryGetValue(v.Path, out var f) && File.Exists(f) ? f : null;
    }

    private bool IsDownloaded(VideoItem v) => v.IsOnline && PlayableLocalFile(v) != null;

    private async Task<bool> EnsureFfmpegAsync(string why)
    {
        if (Tools.FindFfmpeg() != null) return true;
        if (!Dialogs.Confirm(this,
                $"{why} محتاج أداة ffmpeg المجانية (حوالي 65 ميجا).\n\n" +
                "هتتنزل من GitHub الرسمي في:\n" + Tools.FfmpegDir + "\n\nأنزّلها دلوقتي؟", "ffmpeg"))
            return false;

        UseWaitCursor = true;
        try
        {
            await Tools.DownloadFfmpegAsync(new Progress<string>(m => ShowStatus(m, 60)), CancellationToken.None);
            ShowStatus("ffmpeg جاهز ✓", 4);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("ffmpeg-download", ex);
            Dialogs.Error(this, "مقدرتش أنزّل ffmpeg:\n" + ex.Message + "\n\nتقدر تنزّله بإيدك من:\n" + Tools.FfmpegUrl +
                                "\nوتفك ffmpeg.exe والـ dll في:\n" + Tools.FfmpegDir);
            return false;
        }
        finally { UseWaitCursor = false; }
    }

    private async Task DownloadOfflineAsync(IReadOnlyList<VideoItem> videos)
    {
        var todo = videos.Where(v => v.IsOnline && !IsDownloaded(v)).ToList();
        if (todo.Count == 0) { ShowStatus("الفيديوهات دي متنزلة أصلًا"); return; }
        if (_downloadCts != null) { ShowStatus("فيه تنزيل شغال — استنى لما يخلص أو ألغيه"); return; }
        if (!await EnsureYtDlpAsync() || !await EnsureFfmpegAsync("دمج الصورة والصوت")) return;

        var cts = _downloadCts = new CancellationTokenSource();
        int done = 0, failed = 0;
        try
        {
            foreach (var v in todo)
            {
                var course = _library.FindCourseOf(v);
                string dir = Path.Combine(Storage.AppDataDir, "offline", NotesExporter.SafeFileName(course?.Name ?? "فيديوهات"));
                int index = done + failed + 1;
                var progress = new Progress<int>(p => ShowStatus($"بنزّل ({index}/{todo.Count}) {v.Title} — {p}%  (أدوات ← إلغاء التنزيل)", 30));
                try
                {
                    string file = await YtDlp.DownloadVideoAsync(v.Path, dir, _settings.OnlineMaxHeight, progress, cts.Token);
                    _settings.OfflineFiles[v.Path] = file;
                    done++;
                    RefreshVideo(v);
                    Log.Info($"offline: {v.Path} -> {file}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error("offline-download", ex);
                }
            }
            ShowStatus(failed == 0 ? $"اتنزّل {done} فيديو للمشاهدة من غير نت ✓" : $"اتنزّل {done}، وفشل {failed} (التفاصيل في السجل)", 8);
        }
        catch (OperationCanceledException) { ShowStatus("اتلغى التنزيل"); }
        finally
        {
            _downloadCts = null;
            Storage.SaveSettings(_settings);
        }
    }

    private void CancelDownload()
    {
        if (_downloadCts == null) { ShowStatus("مفيش تنزيل شغال"); return; }
        _downloadCts.Cancel();
    }

    private void RemoveOffline(VideoItem v)
    {
        if (!_settings.OfflineFiles.TryGetValue(v.Path, out var file)) return;
        if (!Dialogs.Confirm(this, $"مسح النسخة المتنزلة من «{v.Title}»؟\n(الفيديو هيفضل في القايمة ويتشغّل من النت)")) return;
        try { if (File.Exists(file) && v != _currentVideo) File.Delete(file); } catch (Exception ex) { Log.Error("offline-delete", ex); }
        _settings.OfflineFiles.Remove(v.Path);
        RefreshVideo(v);
    }

    // =====================================================================
    //  التحديثات
    // =====================================================================


    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private void OpenDataFolder(string sub)
    {
        string dir = Path.Combine(Storage.AppDataDir, sub);
        Directory.CreateDirectory(dir);
        try { Process.Start("explorer.exe", $"\"{dir}\""); } catch { }
    }
}
