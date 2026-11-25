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
        if (item == null) { ShowStatus("لا يوجد ما يُتراجع عنه"); return; }
        ApplySnapshot(item.Value.Snapshot);
        ShowStatus($"تراجعت عن: {item.Value.Label}  (Ctrl+Y للإعادة)", 4);
    }

    private void RedoLast()
    {
        var item = _undo.Redo(Storage.Serialize(_library));
        if (item == null) { ShowStatus("لا يوجد ما يُعاد"); return; }
        ApplySnapshot(item.Value.Snapshot);
        ShowStatus($"أُعيدت: {item.Value.Label}", 4);
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
            UpdateTabStripVisible();

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
            FixSplitPanelSize();
            RefitVideo();
            var logLater = new System.Windows.Forms.Timer { Interval = 1200 };
            logLater.Tick += (_, _) => { logLater.Stop(); logLater.Dispose(); if (_miniMode) Log.Info($"mini player (dpi {DeviceDpi}): {VideoAreaOutsideWindow() ?? "video inside window"}"); };
            logLater.Start();
            ShowStatus("المشغل الصغير — P أو Esc أو نقر مزدوج للرجوع");
        }
        else
        {
            _miniMode = false;
            UpdateTabStripVisible();
            SuspendLayout();
            MinimumSize = new Size(900, 560);
            Bounds = _preMiniBounds;
            WindowState = _preMiniState;
            TopMost = _preMiniTopMost || _settings.AlwaysOnTop;
            _menuHost.Visible = _activityHost.Visible = true;
            _statusHost.Visible = StatusBarWanted;
            _split.Panel2Collapsed = !_settings.SidePanelVisible;
            SetChromeless(false);
            SetMiniControls(false);
            ResumeLayout(true);
            ApplySidePanelWidth();
            RefitVideo();
        }
    }

    /// <summary>
    /// بعد ما الشباك يصغر أو يكبر فجأة (المشغل الصغير/ملء الشاشة)، المشغل ساعات بيكمّل يرسم بالمقاس القديم
    /// فيبان جزء من الصورة بس. بنرجّع الصورة «على قد الشباك» ونخلّي المشغل يحس بالمقاس الجديد.
    /// </summary>
    private void RefitVideo()
    {
        void Fit()
        {
            if (IsDisposed) return;
            try
            {
                _mp.Scale = 0;              // 0 = على قد المساحة
                _mp.AspectRatio = null;     // نسبة الفيديو الأصلية
            }
            catch { }
            // هزّة صغيرة لمساحة الفيديو: المشغل بيعيد حساب مقاسه
            var host = _videoView.Parent;
            if (host != null && WindowState == FormWindowState.Normal)
            {
                var s = Size;
                Size = new Size(s.Width + 1, s.Height);
                Size = s;
            }
            _videoView.Invalidate();
            FitVlcWindows();
        }
        BeginInvoke(Fit);
        // ومرة كمان بعد ما الويندوز يخلص رسم الشباك بمقاسه الجديد
        var t = new System.Windows.Forms.Timer { Interval = 300 };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); Fit(); };
        t.Start();
    }

    /// <summary>
    /// المشغل الصغير عرضه ~520 بكسل: شريط التحكم الكامل مش بيكفي فيه وكانت أزراره بتتقص.
    /// نخبّي الحاجات التانوية ونخلّي زرار الرجوع ظاهر ومتعلّم.
    /// </summary>
    private void SetMiniControls(bool mini)
    {
        _speedLabel.Visible = _speedCombo.Visible = _volumeBar.Visible = !mini;
        _btnPlaylist.Visible = _btnStop.Visible = _btnRepeat.Visible = !mini;
        UpdateBookmarkButton();
        _btnMini.Active = mini;
        _btnMini.Text = mini ? Glyphs.FullScreen : Glyphs.MiniPlayer;
        _toolTip.SetToolTip(_btnMini, mini ? "رجوع للمشغل الكامل (P أو Esc أو نقر مزدوج)" : "المشغل الصغير (P)");
    }

    /// <summary>كليك يمين على الفيديو: أدوات المذاكرة الأساسية + التنقل بين الأوضاع.</summary>
    private void ShowVideoMenu(Point at)
    {
        var menu = NewStudyMenu();
        menu.Items.Add(new ToolStripSeparator());
        void Add(string text, string keys, Action act) =>
            menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => act()) { ShortcutKeyDisplayString = keys, ForeColor = Theme.Text });
        Add(_miniMode ? "رجوع للمشغل الكامل" : "المشغل الصغير", "P", ToggleMiniMode);
        Add(_fullscreen ? "خروج من ملء الشاشة" : "ملء الشاشة", _fullscreen ? "Esc" : "F11", ToggleFullscreen);
        Add(TopMost ? "إلغاء «فوق كل النوافذ»" : "اجعله فوق كل النوافذ", "T", ToggleAlwaysOnTop);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_videoView, at);
    }

    /// <summary>زرار «علامة هنا» بيظهر بس مع فيديو عادي (مش بث مباشر، ومش في المشغل الصغير).</summary>
    private void UpdateBookmarkButton() => _btnBookmark.Visible = !_miniMode && _currentLive == null;

    /// <summary>كليك يمين على زرار «علامة هنا».</summary>
    private void ShowStudyMenu(Control owner, Point at, bool above)
    {
        var menu = NewStudyMenu();
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        if (above) menu.Show(owner, at, ToolStripDropDownDirection.AboveRight);
        else menu.Show(owner, at);
    }

    /// <summary>أدوات المذاكرة على الفيديو اللي شغال (البسيطة دايمًا، والمتقدمة في الوضع المتقدم).</summary>
    private ContextMenuStrip NewStudyMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        bool video = _currentVideo != null && IsActive && _currentLive == null;
        void Add(string text, string keys, Action act, bool enabled = true) =>
            menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => act())
                { ShortcutKeyDisplayString = keys, Enabled = enabled, ForeColor = Theme.Text });

        if (!video)
            menu.Items.Add(new ToolStripLabel("شغّل فيديو لاستخدام أدوات المذاكرة") { ForeColor = Theme.Muted });
        Add("🔖  علامة هنا", "B", () => AddBookmark(false), video);
        Add("📝  علامة بملاحظة…", "Shift+B", () => AddBookmark(true), video);
        Add("📷  صورة من الشاشة (لقطة)", "S", TakeScreenshot, video);
        Add(_loopA < 0 ? "🔁  كرّر جزءًا: ابدأ من هنا" : _loopB < 0 ? "🔁  كرّر جزءًا: حتى هنا" : "🔁  أوقف تكرار الجزء", "A", CycleAbLoop, video);
        menu.Items.Add(new ToolStripSeparator());
        Add("📌  علامة مراجعة (تذكّرني بمراجعتها)…", "R", AddReviewBookmark, video);
        Add("📋  انسخ الكلام المكتوب على الشاشة", "O", () => _ = OcrFrameAsync(), video);
        return menu;
    }

    // =====================================================================
    //  معاينة شريط التقديم (صورة من الفيديو)
    // =====================================================================

    private PreviewPopup? _preview;
    private long _previewBucket = -1;
    private Image? _previewImage;
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
        // نفس الصورة اللي معروضة: نحرّك الشباك بس (من غير ما نقرا الصورة من الديسك مع كل حركة ماوس)
        if (bucket == _previewBucket && _preview.Visible && _preview.HasImage)
        {
            _preview.ShowAt(_previewImage, caption, anchor, imgSize);
            return true;
        }
        if (File.Exists(cache))
        {
            _previewBucket = bucket;
            _previewImage = LoadImageUnlocked(cache);
            _preview.ShowAt(_previewImage, caption, anchor, imgSize);
            return true;
        }

        _previewImage = null;
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
                {
                    _previewImage = LoadImageUnlocked(cache);
                    p.ShowAt(_previewImage, caption, anchor, imgSize);
                }
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
                $"{why} يحتاج إلى «أداة الفيديو» المجانية (نحو 65 ميجابايت، تُنزَّل مرة واحدة).\n\n" +
                "ستُنزَّل من مصدرها الرسمي. هل أنزّلها الآن؟", "أداة الفيديو"))
            return false;

        UseWaitCursor = true;
        try
        {
            await Tools.DownloadFfmpegAsync(new Progress<string>(m => ShowStatus(m, 60)), CancellationToken.None);
            ShowStatus("أداة الفيديو جاهزة ✓", 4);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("ffmpeg-download", ex);
            Dialogs.Error(this, "تعذّر تنزيل أداة الفيديو (ffmpeg):\n" + ex.Message + "\n\nيمكنك تنزيله يدويًا من:\n" + Tools.FfmpegUrl +
                                "\nوفك ffmpeg.exe وملفات dll في:\n" + Tools.FfmpegDir);
            return false;
        }
        finally { UseWaitCursor = false; }
    }

    private void RemoveOffline(VideoItem v)
    {
        if (!_settings.OfflineFiles.TryGetValue(v.Path, out var file)) return;
        if (!Dialogs.Confirm(this, $"هل تريد حذف النسخة المنزّلة من «{v.Title}»؟\n(يبقى الفيديو في المكتبة ويعمل من الإنترنت)")) return;
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
        try { Process.Start("explorer.exe", $"\"{dir}\"")?.Dispose(); } catch { }
    }
}
