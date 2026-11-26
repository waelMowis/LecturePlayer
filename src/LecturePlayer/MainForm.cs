using LecturePlayer.Data;
using LecturePlayer.UI;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using Timer = System.Windows.Forms.Timer;

namespace LecturePlayer;

/// <summary>
/// الشاشة الرئيسية: الفيديو على الشمال، وعلى اليمين شجرة من 3 مستويات:
/// كورس ← محاضرات ← فيديوهات (كل مستوى بيتفتح ويتقفل).
/// الملف ده فيه بناء الواجهة والاختصارات وملء الشاشة.
/// باقي الشغل في MainForm.Library.cs و MainForm.Playback.cs
/// </summary>
internal sealed partial class MainForm : Form
{
    private const string AppTitle = "مسار";
    private static readonly float[] Rates = { 0.5f, 0.75f, 1f, 1.1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f };

    // ---------- VLC ----------
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mp;

    // ---------- البيانات ----------
    private readonly AppSettings _settings;

    // ---------- الواجهة ----------
    private readonly MenuStrip _menu = new();
    private readonly Panel _sidePanel = new();
    private readonly SplitContainer _split = new();
    private readonly Panel _videoArea = new();
    private readonly VideoView _videoView = new();
    private readonly Panel _welcome = new();
    private readonly Panel _controlBar = new();
    private readonly SeekBar _seekBar = new();
    private readonly SeekBar _volumeBar = new();
    private readonly TimeReadout _timeLabel = new();
    private readonly Label _nowPlayingLabel = new();
    private readonly SelectBox _speedCombo = new();
    private readonly LectureTreeView _tree = new();
    private readonly TextBox _searchBox = new();
    private readonly Label _courseLabel = new();
    private readonly Label _courseInfoLabel = new();
    private Panel _infoRow = null!;
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusGroup = new();
    private readonly ToolStripStatusLabel _statusMessage = new();
    private readonly ToolStripStatusLabel _statusFile = new();
    private readonly ContextMenuStrip _nodeMenu = new();
    private readonly ToolTip _toolTip = new();
    private readonly Timer _uiTimer = new() { Interval = 250 };

    private readonly GlyphButton _btnPrev = MakeButton(Glyphs.Previous);
    private readonly GlyphButton _btnBack = MakeButton(Glyphs.Rewind);
    private readonly GlyphButton _btnPlay = MakeButton(Glyphs.Play);
    private readonly GlyphButton _btnFwd = MakeButton(Glyphs.Forward);
    private readonly GlyphButton _btnNext = MakeButton(Glyphs.Next);
    private readonly GlyphButton _btnStop = MakeButton(Glyphs.Stop);
    private readonly GlyphButton _btnMute = MakeButton(Glyphs.Volume);
    private readonly GlyphButton _btnFullscreen = MakeButton(Glyphs.FullScreen);
    private readonly GlyphButton _btnPlaylist = MakeButton(Glyphs.List);
    private readonly GlyphButton _btnMini = MakeButton(Glyphs.MiniPlayer);
    private readonly PillButton _btnBookmark = new("🔖  علامة هنا");
    private Label _speedLabel = null!;

    private ToolStripMenuItem _recentMenu = null!;
    private ToolStripMenuItem _autoNextItem = null!;
    private ToolStripMenuItem _continueGroupsItem = null!;
    private ToolStripMenuItem _resumeItem = null!;
    private ToolStripMenuItem? _alwaysOnTopItem;

    private Font _regularNodeFont = null!;
    private Font _boldNodeFont = null!;
    private Font _groupNodeFont = null!;
    private Font _topicNodeFont = null!;

    // ---------- ملء الشاشة ----------
    private bool _fullscreen;
    private FormWindowState _prevWindowState;
    private Rectangle _prevBounds;
    private Point _lastCursor;
    private DateTime _lastMouseActivity = DateTime.Now;

    private int _tick;
    private DateTime _statusMessageUntil;
    private readonly Queue<string> _vlcLog = new();
    private bool _vlcLogCapture;

    private void OnVlcLog(object? sender, LogEventArgs e)
    {
        if (e.Level < LogLevel.Warning) return;
        lock (_vlcLog)
        {
            _vlcLog.Enqueue($"{e.Level} {e.Module}: {e.Message}");
            while (_vlcLog.Count > 30) _vlcLog.Dequeue();
        }
    }

    /// <summary>تسجيل تحذيرات VLC (عشان نعرف سبب فشل فيديو). للقياس: بنقدر نقفله.</summary>
    internal void SetVlcLogCapture(bool on)
    {
        if (on == _vlcLogCapture) return;
        if (on) _libVLC.Log += OnVlcLog; else _libVLC.Log -= OnVlcLog;
        _vlcLogCapture = on;
    }

    /// <summary>بيكتب آخر رسايل VLC في السجل (بيتنده لما فيديو يفشل).</summary>
    private void DumpVlcLog(string why)
    {
        string[] lines;
        lock (_vlcLog) { lines = _vlcLog.ToArray(); _vlcLog.Clear(); }
        if (lines.Length > 0) Log.Info($"vlc log ({why}):\n    " + string.Join("\n    ", lines.TakeLast(15)));
    }

    private bool _closing;
    private bool _shutdownDone;

    public MainForm(string? openPath, IReadOnlyList<string>? openFiles = null)
    {
        _pendingExternal = openFiles is { Count: > 0 } ? openFiles : null;
        _settings = Storage.LoadSettings();
        YtDlp.CookiesBrowser = _settings.OnlineCookiesBrowser;

        var vlcOptions = new List<string> { "--no-video-title-show" };
        if (_settings.NormalizeAudio) vlcOptions.Add("--audio-filter=normvol"); // توحيد مستوى الصوت
        _libVLC = new LibVLC(vlcOptions.ToArray());
        // آخر تحذيرات/أخطاء VLC بتتحفظ في الذاكرة، وبتتكتب في السجل لما فيديو يفشل (عشان نعرف السبب الحقيقي)
        SetVlcLogCapture(true);
        StreamProxy.Trace = Log.Info;
        // لو تسجيل الدخول من متصفح حل «أكّد إنك مش روبوت» لوحده: نحفظه عشان المرات الجاية
        YtDlp.CookiesBrowserFound += browser => BeginInvoke(() =>
        {
            _settings.OnlineCookiesBrowser = browser;
            SaveSettings();
            ShowBanner($"طلب يوتيوب التأكد من أنك لست روبوتًا — استُخدم تسجيل دخولك من {browser} ونجح ✓", BannerKind.Success, seconds: 10);
        });
        Log.Info("app started" + (_settings.NormalizeAudio ? " (normvol)" : ""));
        _mp = new MediaPlayer(_libVLC)
        {
            EnableHardwareDecoding = true,
            EnableMouseInput = false, // عشان الدبل كليك والماوس يوصلوا للبرنامج مش لـ VLC
            EnableKeyInput = false
        };

        BuildUi();
        BuildMenus();
        BuildTray();
        WireEvents();
        WirePlayerEvents();
        ApplySettings();
        InitAdvancedMode();

        // البرنامج بيفتح فاضي وتختار المكتبة (إلا لو فتحت ملف مكتبة بالدبل كليك، أو اخترت «افتح آخر مكتبة» من الإعدادات)
        string path = openPath ?? TakeReopenAfterUpdate() ?? (_settings.OpenLastLibraryOnStart ? _settings.LastLibraryPath ?? "" : "");
        if (string.IsNullOrWhiteSpace(path)) StartWithoutLibrary();
        else if (!File.Exists(path))
        {
            // آخر قائمة مش موجودة دلوقتي — غالبًا في Google Drive / OneDrive واللي لسه مااشتغلش بعد تشغيل الجهاز.
            // نفتح «مكتبتي» مؤقتًا من غير مانغيّر «آخر قائمة»، ونفضل مستنيين الملف يظهر ونفتحه لوحده.
            _waitingLibraryPath = path;
            _waitingLibrarySinceUtc = DateTime.UtcNow;
            Log.Info("last library not available yet, waiting: " + path);
            StartWithoutLibrary();
            ShowBanner("أنتظر مكتبتك «" + Path.GetFileNameWithoutExtension(path) + "» تظهر — غالبًا Google Drive أو OneDrive ما زال يُفتح. سأفتحها تلقائيًا فور وصولها.",
                BannerKind.Warning, "سأختارها بنفسي", OpenLibraryDialog, seconds: 0, key: "waiting-library");
        }
        else LoadLibrary(path);

        _uiTimer.Tick += (_, _) => OnUiTick();
        _uiTimer.Start();
    }

    /// <summary>
    /// أول تشغيل بعد تحديث تلقائي: المكتبة اللي كانت مفتوحة (مرة واحدة، ولو التحديث من أقل من ساعة —
    /// لو المثبّت فشل وفتحت البرنامج بعدها بيوم، ده تشغيل عادي).
    /// </summary>
    private string? TakeReopenAfterUpdate()
    {
        string? path = _settings.ReopenAfterUpdate;
        if (path == null) return null;
        bool fresh = DateTime.UtcNow - _settings.ReopenAfterUpdateUtc < TimeSpan.FromHours(1);
        _settings.ReopenAfterUpdate = null;
        Storage.SaveSettings(_settings);
        if (!fresh || string.IsNullOrWhiteSpace(path)) return null;
        Log.Info("reopening library after update: " + path);
        return path;
    }

    /// <summary>فحص تلقائي: بعد التحديث المكتبة بترجع مرة واحدة بس، ولو التحديث قديم مابترجعش.</summary>
    internal void RunUpdateReopenChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("reopen after update: " + what); }
        _settings.ReopenAfterUpdate = @"G:\My Drive\WAEL.lpl";
        _settings.ReopenAfterUpdateUtc = DateTime.UtcNow.AddMinutes(-2);
        Check(TakeReopenAfterUpdate() == @"G:\My Drive\WAEL.lpl", "fresh update reopens");
        Check(_settings.ReopenAfterUpdate == null && TakeReopenAfterUpdate() == null, "only once");
        _settings.ReopenAfterUpdate = @"G:\My Drive\WAEL.lpl";
        _settings.ReopenAfterUpdateUtc = DateTime.UtcNow.AddHours(-5);
        Check(TakeReopenAfterUpdate() == null && _settings.ReopenAfterUpdate == null, "stale ignored and cleared");
        Console.WriteLine("SMOKE reopen after update OK");

        // التحقق من التحديثات والنت بطيء (22/11): 3 ضغطات ورا بعض ← تحقق واحد، ومفيش رسالة بتوقف الشغل
        int fetches = 0;
        try
        {
            UpdateChecker.TestFetch = async (_, ct) =>
            {
                Interlocked.Increment(ref fetches);
                await Task.Delay(1500, ct);
                throw new UpdateChecker.UpdateCheckException(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 20 seconds elapsing."));
            };
            int started = UpdateChecksStarted;
            _ = CheckForUpdatesAsync(manual: true);
            Application.DoEvents();
            _ = CheckForUpdatesAsync(manual: true);
            _ = CheckForUpdatesAsync(manual: true);
            var until = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < until && _updateCheck is { IsCompleted: false }) { Application.DoEvents(); Thread.Sleep(20); }
            Application.DoEvents();
            Check(fetches == 1 && UpdateChecksStarted - started == 1, $"one check for 3 clicks, got {fetches}");
            Check(_bannerKey == "update-check", "failure shown as a bar with «حاول مرة أخرى», got banner " + _bannerKey);

            // ونت كويس: «إنت على آخر إصدار» في شريط مش رسالة
            UpdateChecker.TestFetch = (_, _) => Task.FromResult<UpdateChecker.Release?>(new UpdateChecker.Release(UpdateChecker.CurrentVersion, "v" + UpdateChecker.CurrentVersion.ToString(3), "u", "", null));
            var t = CheckForUpdatesAsync(manual: true);
            while (!t.IsCompleted) { Application.DoEvents(); Thread.Sleep(20); }
            Check(_bannerKey == "update-check", "up to date shown as a bar");
            HideBanner("update-check");

            // تحويل github.com ← الإصدار ورابط المثبّت
            var redirect = UpdateChecker.ParseLatestRedirect("waelMowis/LecturePlayer", "https://github.com/waelMowis/LecturePlayer/releases/tag/v4.1.46");
            Check(redirect?.Version == new Version(4, 1, 46) && redirect.SetupUrl == "https://github.com/waelMowis/LecturePlayer/releases/download/v4.1.46/Masar-Setup-4.1.46.exe"
                  && redirect.SetupSha256 == null, "redirect parse: " + redirect);
            Check(UpdateChecker.ParseLatestRedirect("o/r", "https://github.com/o/r/releases") == null, "non-tag redirect");
        }
        finally { UpdateChecker.TestFetch = null; }
        Console.WriteLine("SMOKE update check OK (3 clicks → 1 check, no blocking dialog)");
    }

    private static Icon? TryExeIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return null; }
    }

    private static GlyphButton MakeButton(string symbol) =>
        new(symbol, 12f) { Size = new Size(40, 36), Margin = new Padding(2, 4, 2, 4) };

    // =====================================================================
    //  بناء الواجهة
    // =====================================================================

    private void BuildUi()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = AppTitle;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.75f);
        // أيقونة البرنامج (من جوه الـ exe)، ولو مش موجودة لأي سبب نجيبها من ملف البرنامج نفسه
        Icon = Theme.AppIcon ?? TryExeIcon() ?? SystemIcons.Application;
        MinimumSize = new Size(900, 560);
        Size = new Size(1360, 800);
        StartPosition = FormStartPosition.CenterScreen;

        _regularNodeFont = new Font("Segoe UI", 9.75f, FontStyle.Regular);
        _boldNodeFont = new Font("Segoe UI Semibold", 9.75f, FontStyle.Regular);
        _groupNodeFont = new Font("Segoe UI", 9.75f, FontStyle.Regular);
        _topicNodeFont = new Font("Segoe UI", 10f, FontStyle.Bold);

        // ---------- منطقة الفيديو ----------
        _videoArea.Dock = DockStyle.Fill;
        _videoArea.BackColor = Color.Black;

        _videoView.Dock = DockStyle.Fill;
        _videoView.BackColor = Color.Black;
        _videoView.MediaPlayer = _mp;

        BuildWelcome();
        BuildControlBar();

        _videoArea.Controls.Add(_videoView);
        _videoArea.Controls.Add(_welcome);
        _videoArea.Controls.Add(_radioVisual);
        _radioVisual.Dock = DockStyle.Fill;
        _radioVisual.Visible = false;
        _welcome.BringToFront();
        _radioVisual.BringToFront();
        _radioVisual.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) { if (_miniMode) ToggleMiniMode(); else ToggleFullscreen(); } };
        _radioVisual.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowVideoMenu(_videoView.PointToClient(_radioVisual.PointToScreen(e.Location))); };
        _radioVisual.MouseWheel += (_, e) => SetVolume(_settings.Volume + (e.Delta > 0 ? 5 : -5));
        BuildBanner();
        InitRepeat();
        InitVideoFit();

        // ---------- القايمة الجانبية ----------
        BuildSidePanel();
        BuildActivityBar();

        // الفيديو على الشمال والقايمة على اليمين جوه SplitContainer:
        // كل جزء ليه مساحته الخاصة، فالقايمة عمرها ما تدخل تحت الفيديو.
        // (الحد الأدنى لكل جزء بيتظبط بعد ما الشاشة تظهر — شوف ApplySidePanelWidth)
        _split.Dock = DockStyle.Fill;
        _split.Orientation = Orientation.Vertical;
        _split.FixedPanel = FixedPanel.Panel2;   // تكبير الشاشة بيكبّر الفيديو، والقايمة تفضل بعرضها
        _split.SplitterWidth = 6;
        _split.BackColor = Theme.Window;         // الفاصل = فراغ بين الكارتين
        _split.RightToLeft = RightToLeft.No;
        _split.TabStop = false;
        _split.Panel1.BackColor = Theme.Window;
        _split.Panel2.BackColor = Theme.Window;
        _videoCard.Dock = DockStyle.Fill;
        _videoArea.BackColor = Theme.Panel;
        _videoCard.Controls.Add(_videoArea);
        BuildTabStrip();
        // شريط التحكم كارت لوحده تحت كارت الفيديو، وبينهم فراغ
        _controlCard.Dock = DockStyle.Bottom;
        _controlBar.Dock = DockStyle.Fill;
        _controlCard.Controls.Add(_controlBar);
        _controlCard.Height = _controlBar.Height + _controlCard.Padding.Vertical;
        _controlGap.Dock = DockStyle.Bottom;
        _controlGap.Height = 6;
        _controlGap.BackColor = Theme.Window;
        _split.Panel1.Controls.Add(_videoCard);
        _split.Panel1.Controls.Add(_controlGap);
        _split.Panel1.Controls.Add(_controlCard);
        _split.Panel2.Controls.Add(_sidePanel);
        _workspace.Dock = DockStyle.Fill;
        _workspace.BackColor = Theme.Window;
        _workspace.Padding = new Padding(6, 2, 0, 2);
        _workspace.Controls.Add(_split);

        // ---------- شريط الحالة ----------
        _status.Renderer = new DarkRenderer();
        _status.BackColor = Theme.Panel;
        _status.Dock = DockStyle.Fill;
        _status.SizingGrip = false;
        _status.RightToLeft = RightToLeft.Yes;
        _statusGroup.ForeColor = Theme.Text;
        _statusMessage.Spring = true;
        _statusMessage.ForeColor = Theme.Accent;
        _statusMessage.TextAlign = ContentAlignment.MiddleCenter;
        _statusFile.ForeColor = Theme.Muted;
        _status.Items.AddRange(new ToolStripItem[] { _statusGroup, _statusMessage, _statusFile });
        SetupDownloadsIndicator();

        // ---------- المنيو ----------
        _menu.Renderer = new DarkRenderer();
        _menu.BackColor = Theme.Panel;
        _menu.Dock = DockStyle.Fill;
        _menu.Padding = new Padding(4, 1, 4, 1);
        _menu.ForeColor = Theme.Text;
        _menu.RightToLeft = RightToLeft.Yes;
        MainMenuStrip = _menu;

        // ترتيب الإضافة مهم للـ Dock: اللي بيتضاف الأخير بياخد مكانه الأول
        Controls.Add(_workspace);
        _activityHost.Dock = DockStyle.Right;
        _activityHost.BackColor = Theme.Window;
        _activityHost.Padding = new Padding(4, 2, 6, 2);
        _activityHost.Width = _activity.Width + _activityCard.Padding.Horizontal + _activityHost.Padding.Horizontal;
        _activityCard.Dock = DockStyle.Fill;
        _activityCard.Controls.Add(_activity);
        _activityHost.Controls.Add(_activityCard);
        Controls.Add(_activityHost);
        // شريط الحالة كارت تحت (زي شريط القوائم فوق)
        _statusCard.Dock = DockStyle.Fill;
        _statusCard.Controls.Add(_status);
        _statusHost.Dock = DockStyle.Bottom;
        _statusHost.BackColor = Theme.Window;
        _statusHost.Padding = new Padding(6, 2, 6, 4);
        _statusHost.Height = Math.Max(_status.PreferredSize.Height, LogicalToDeviceUnits(24)) + _statusCard.Padding.Vertical + _statusHost.Padding.Vertical;
        _statusHost.Controls.Add(_statusCard);
        Controls.Add(_statusHost);
        // شريط القوائم كارت لوحده فوق، بنفس شكل باقي اللوحات
        _menuCard.Dock = DockStyle.Fill;
        _menuCard.Controls.Add(_menu);
        _menuHost.Dock = DockStyle.Top;
        _menuHost.BackColor = Theme.Window;
        _menuHost.Padding = new Padding(6, 4, 6, 2);
        _menuHost.Height = Math.Max(_menu.PreferredSize.Height, LogicalToDeviceUnits(26)) + _menuCard.Padding.Vertical + _menuHost.Padding.Vertical;
        _menuHost.Controls.Add(_menuCard);
        Controls.Add(_menuHost);

        _nodeMenu.Renderer = new DarkRenderer();
        _nodeMenu.RightToLeft = RightToLeft.Yes;

        _toolTip.SetToolTip(_btnPrev, "الفيديو السابق (PageUp)");
        _toolTip.SetToolTip(_btnBack, "رجوع 10 ثوانٍ (Ctrl+←  = 30 ثانية)");
        _toolTip.SetToolTip(_btnPlay, "تشغيل / إيقاف مؤقت (Space)");
        _toolTip.SetToolTip(_btnFwd, "تقديم 10 ثوانٍ (Ctrl+→  = 30 ثانية)");
        _toolTip.SetToolTip(_btnNext, "الفيديو التالي (PageDown)");
        _toolTip.SetToolTip(_btnStop, "إيقاف");
        _toolTip.SetToolTip(_btnMute, "كتم الصوت (M)");
        _toolTip.SetToolTip(_btnFullscreen, "ملء الشاشة (F11 أو نقر مزدوج)");
        _toolTip.SetToolTip(_btnPlaylist, "إظهار / إخفاء المكتبة (F6)");
        _toolTip.SetToolTip(_btnMini, "المشغل الصغير (P)");
        _toolTip.SetToolTip(_btnBookmark, "ضع علامة عند هذه اللحظة لتعود إليها لاحقًا (B)\nانقر بالزر الأيمن: علامة بملاحظة، علامة مراجعة، لقطة شاشة…");

        ResumeLayout(false);
        PerformLayout();
    }

    private void BuildWelcome()
    {
        _welcome.Dock = DockStyle.Fill;
        _welcome.BackColor = Theme.Panel;
        _welcome.AllowDrop = true;
        BuildHome(); // الشاشة الرئيسية: «كمّل اللي بدأته»
        _welcome.DragEnter += OnExternalDragEnter;
        _welcome.DragDrop += (_, e) => OnExternalDrop(e, null);
    }

    private void BuildControlBar()
    {
        _controlBar.Dock = DockStyle.Bottom;
        _controlBar.Height = 84;
        _controlBar.BackColor = Theme.Panel;
        _controlBar.Padding = new Padding(10, 6, 10, 4);
        _controlBar.RightToLeft = RightToLeft.No;

        // الصف الأول: شريط التقديم + الوقت
        var seekRow = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Theme.Panel };
        _seekBar.Dock = DockStyle.Fill;
        _timeLabel.Dock = DockStyle.Right;
        _timeLabel.Width = 190;
        _timeLabel.Font = new Font("Consolas", 10.5f);
        _toolTip.SetToolTip(_timeLabel, "اضغط هنا للتبديل بين «المتبقي» و«المدة كاملة»");
        _timeLabel.ModeChanged += (_, _) => { _settings.TimeShowsTotal = _timeLabel.ShowTotal; SaveSettings(); };
        seekRow.Controls.Add(_seekBar);
        seekRow.Controls.Add(_timeLabel);

        // الصف التاني: الأزرار
        var row = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Theme.Panel
        };
        _btnBookmark.Size = new Size(118, 32);
        _btnBookmark.Margin = new Padding(14, 4, 4, 4);
        _btnBookmark.TabStop = false;
        left.Controls.AddRange(new Control[] { _btnPrev, _btnBack, _btnPlay, _btnFwd, _btnNext, _btnStop, _btnBookmark });
        _btnPlay.Size = new Size(50, 36);
        _btnPlay.Primary = true;

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Theme.Panel
        };
        var speedLabel = _speedLabel = new Label
        {
            Text = "السرعة",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(6, 13, 2, 4)
        };
        _speedCombo.Width = 80;
        _speedCombo.Height = 32;
        _speedCombo.Margin = new Padding(2, 6, 10, 4);
        _speedCombo.TabStop = false;
        foreach (float r in Rates) _speedCombo.Items.Add(RateText(r));

        _volumeBar.Width = 110;
        _volumeBar.Height = 24;
        _volumeBar.Margin = new Padding(0, 10, 8, 4);

        right.Controls.AddRange(new Control[] { speedLabel, _speedCombo, _btnRepeat, _btnMute, _volumeBar, _btnPlaylist, _btnMini, _btnFullscreen });
        SetupQualityChip(right);

        _nowPlayingLabel.Dock = DockStyle.Fill;
        _nowPlayingLabel.TextAlign = ContentAlignment.MiddleCenter;
        _nowPlayingLabel.AutoEllipsis = true;
        _nowPlayingLabel.ForeColor = Theme.Text;
        _nowPlayingLabel.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _nowPlayingLabel.RightToLeft = RightToLeft.Yes;

        row.Controls.Add(_nowPlayingLabel);
        row.Controls.Add(right);
        row.Controls.Add(left);

        _controlBar.Controls.Add(row);
        _controlBar.Controls.Add(seekRow);
    }

    private readonly ProgressLine _libraryProgress = new();
    private readonly Panel _activity = new();
    private readonly Panel _workspace = new();
    private readonly Card _videoCard = new();
    private readonly Card _treeCard = new();
    private readonly Card _controlCard = new();
    private readonly Card _menuCard = new();
    private readonly Card _statusCard = new();
    private readonly Panel _statusHost = new();
    private readonly Panel _menuHost = new();
    private readonly Panel _controlGap = new();
    private readonly Card _activityCard = new();
    private readonly Panel _activityHost = new();

    /// <summary>شريط التحكم (كارت تحت الفيديو): بيختفي في ملء الشاشة لما الماوس يقف.</summary>
    private bool ControlChromeVisible
    {
        get => _controlCard.Visible;
        set { bool v = value && !ReaderShown; _controlCard.Visible = v; _controlGap.Visible = v && !_videoCard.Flat; } // القارئ ليه شريطه
    }

    /// <summary>ملء الشاشة والمشغل الصغير: من غير كروت ولا فراغات.</summary>
    private void SetChromeless(bool on)
    {
        _workspace.Padding = on ? Padding.Empty : new Padding(6, 2, 0, 2);
        _videoCard.Flat = on;
        _controlCard.Flat = on;
        _controlGap.Visible = !on && _controlCard.Visible;
        _controlCard.Height = _controlBar.Height + _controlCard.Padding.Vertical;
        _videoArea.BackColor = Theme.Panel;
    }
    private GlyphButton _activityExplorer = null!;
    private GlyphButton _activityHome = null!;
    private GlyphButton _activityBoard = null!, _activityRecord = null!, _activityTools = null!;
    private GlyphButton _activityClasses = null!, _activityReview = null!, _activityStats = null!;
    private Action? _fitRail;
    private readonly Dictionary<GlyphButton, string?> _railCaptions = new();
    private bool _railCompact;

    /// <summary>شريط الأيقونات على طرف الشاشة (زي المحررات الحديثة): القايمة، البحث، المراجعة، الإحصائيات…</summary>
    private void BuildActivityBar()
    {
        _activity.Dock = DockStyle.Fill;
        _activity.Width = LogicalToDeviceUnits(64);
        _activity.BackColor = Theme.Panel;
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel,
            Padding = new Padding(0, 4, 0, 0)
        };
        GlyphButton Act(string glyph, string caption, string tip, Action click, Control parent)
        {
            var b = new GlyphButton(glyph, 15f)
            {
                Size = new Size(LogicalToDeviceUnits(64), LogicalToDeviceUnits(56)), Margin = Padding.Empty,
                ShowIndicator = true, IndicatorOnRight = true, Caption = caption
            };
            b.Click += (_, _) => click();
            _toolTip.SetToolTip(b, tip);
            parent.Controls.Add(b);
            return b;
        }
        // الشريط الجانبي المرتّب: الرئيسية · المكتبة (الدورات والكتب في مكان واحد) · سبورة · تسجيل · الأدوات (كل الباقي)
        _activityHome = Act(Glyphs.Home, "الرئيسية", "الشاشة الرئيسية: أكمل ما بدأته، وأضف محتوى (Alt+Home)", GoHome, top);
        _activityExplorer = Act(Glyphs.Library, "المكتبة", "المكتبة: الدورات والفيديوهات، والكتب والعروض والسبورات — اضغط لإخفائها أو إظهارها (F6)", OnExplorerActivity, top);
        UpdateActivityIndicators();
        _activityClasses = Act(Glyphs.People, "الحصص", "حصص اليوم: ابدأ حصة مجموعة من حيث توقفت، وأنهِها بالواجب والتصدير — والمجموعات وجدولها", () => ShowClassesActivityMenu(_activityClasses), top);
        _activityBoard = Act(Glyphs.Board, "سبورة", "السبورة البيضاء: سبورة جديدة أو افتح إحدى سبوراتك", () => ShowBoardActivityMenu(_activityBoard), top);
        _activityRecord = Act(Glyphs.Record, "تسجيل", "سجّل شرح: الشاشة والكاميرا والصوت وأقلام على الشاشة (Ctrl+Shift+F9 من أي برنامج)", OnRecordActivity, top);
        _activityReview = Act(Glyphs.History, "المراجعة", "مراجعة اليوم: العلامات التي حان وقت مراجعتها (F7)", ShowReview, top);
        _activityStats = Act(Glyphs.Chart, "الإحصائيات", "إحصائيات المذاكرة: وقتك وتقدّمك وأيامك المتتالية (F8)", ShowStats, top);
        _activityTools = Act(Glyphs.Tools, "الأدوات", "كل الأدوات: عرض PowerPoint، أقلام الشاشة، المراجعة، الإحصائيات، التركيز، البث المباشر، التنزيل… — وابحث عن أي أمر (Ctrl+Shift+Space)", () => ShowToolsMenu(_activityTools), top);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = LogicalToDeviceUnits(112), BackColor = Theme.Panel };
        var settings = Act(Glyphs.Settings, "الإعدادات", "الإعدادات (Ctrl+,)", SettingsDialog, bottom);
        settings.Dock = DockStyle.Bottom;
        var help = Act(Glyphs.Help, "مساعدة", "دليل الاستخدام (F1) — والجولة السريعة من قائمة مساعدة", () => HelpGuide.Open(this), bottom);
        help.Dock = DockStyle.Bottom;
        _activity.Controls.Add(top);
        _activity.Controls.Add(bottom);
        // الشباك صغير؟ الزراير تصغر عشان آخر زرار مايتغطاش بالإعدادات (والكلام تحت الأيقونة يختفي لو المكان ضيق قوي)
        void FitRail()
        {
            var buttons = top.Controls.OfType<GlyphButton>().Where(b => b.Visible).ToList();
            if (buttons.Count == 0) return;
            int full = LogicalToDeviceUnits(56), min = LogicalToDeviceUnits(36);
            int h = Math.Clamp((top.ClientSize.Height - top.Padding.Vertical) / buttons.Count, min, full);
            _railCompact = h < LogicalToDeviceUnits(46);
            foreach (var b in buttons)
            {
                if (b.Height != h) b.Height = h;
                if (!_railCaptions.ContainsKey(b)) _railCaptions[b] = b.Caption;
                b.Caption = _railCompact ? null : _railCaptions[b];
                b.Invalidate();
            }
        }
        top.Resize += (_, _) => FitRail();
        top.VisibleChanged += (_, _) => FitRail();
        _fitRail = FitRail;
        ApplyRoleRail();
    }

    private void BuildSidePanel()
    {
        _sidePanel.Dock = DockStyle.Fill;
        _sidePanel.BackColor = Theme.Window;
        _treeCard.Dock = DockStyle.Fill;
        GlyphButton Icon(string glyph, string tip, Action click, Control parent)
        {
            var b = new GlyphButton(glyph, 11f) { Size = new Size(LogicalToDeviceUnits(26), LogicalToDeviceUnits(24)), Dock = DockStyle.Left };
            b.Click += (_, _) => click();
            _toolTip.SetToolTip(b, tip);
            parent.Controls.Add(b); // بعد الـ Fill ← بيترص الأول فمش بيتغطى (ولا بيغطي الاسم)
            return b;
        }

        // ---------- العنوان ----------
        var titleRow = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(35), BackColor = Theme.Panel, Padding = new Padding(8, 6, 14, 4) };
        var title = new Label
        {
            Dock = DockStyle.Fill, Text = "المكتبة", ForeColor = Theme.Text, RightToLeft = RightToLeft.Yes,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight
        };
        titleRow.Controls.Add(title);
        var moreMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        ToolStripMenuItem MoreItem(string text, Action click) { var i = new ToolStripMenuItem(text) { ForeColor = Theme.Text }; i.Click += (_, _) => click(); moreMenu.Items.Add(i); return i; }
        // «···»: عرض الشجرة وترتيبها (أوامر الحاجة المحددة في كليك يمين وفي قايمة «المكتبة»)
        MoreItem("ترتيب الدورات بالاسم", SortCourses);
        var sepAll = MoreItem("➖  فواصل بين كل الموضوعات", () => ToggleAllTopicSeparators());
        moreMenu.Opening += (_, _) => sepAll.Checked = _settings.TopicSeparators;
        var more = Icon(Glyphs.More, "المزيد", () => { }, titleRow);
        more.Click += (_, _) => moreMenu.Show(more, new Point(0, more.Height + 2));

        // ---------- اسم المكتبة + أزرار الإضافة ----------
        var sectionRow = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(28), BackColor = Theme.Panel, Padding = new Padding(8, 2, 14, 2) };
        _courseLabel.Dock = DockStyle.Fill;
        _courseLabel.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _courseLabel.ForeColor = Theme.Text;
        _courseLabel.RightToLeft = RightToLeft.Yes;
        _courseLabel.TextAlign = ContentAlignment.MiddleRight;
        _courseLabel.AutoEllipsis = true;
        _courseLabel.Cursor = Cursors.Hand;
        _toolTip.SetToolTip(_courseLabel, "اسم المكتبة المفتوحة — اضغط للتبديل إلى مكتبة أخرى أو إنشاء مكتبة جديدة");
        sectionRow.Controls.Add(_courseLabel);
        // زرار واحد «＋ إضافة» بدل 6 أيقونات صغيرة من غير أسامي
        var addMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        // «＋ إضافة» بيتبني كل مرة حسب اللي واقف عليه: موضوع جديد الأول، وبعدها اللي تحت الحاجة المحددة بالترتيب
        addMenu.Opening += (_, e) =>
        {
            foreach (var old in addMenu.Items.Cast<ToolStripItem>().ToList()) old.Dispose();
            addMenu.Items.Clear();
            BuildAddMenu(_tree.SelectedNode?.Tag, addMenu.Items);
            e.Cancel = false;
        };
        addMenu.Items.Add(new ToolStripMenuItem("…"));
        var addButton = new PillButton("＋  إضافة", PillStyle.Ghost)
        {
            Dock = DockStyle.Left, Width = LogicalToDeviceUnits(82), Margin = Padding.Empty, TabStop = false
        };
        addButton.Click += (_, _) => addMenu.Show(addButton, new Point(0, addButton.Height + 2));
        _toolTip.SetToolTip(addButton, "إضافة حسب العنصر المحدد: موضوع ← مصدر ← دورة ← محاضرة ← فيديوهات (أو Insert)");
        sectionRow.Controls.Add(addButton);

        // سطر الأرقام اتشال: الإحصائيات بقت في تلميح اسم المكتبة، والسطر ده بيظهر بس لما فيه فلتر شغال (عشان متتلخبطش ليه حاجات مش ظاهرة)
        var infoRow = _infoRow = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(24), BackColor = Theme.Panel, Padding = new Padding(14, 0, 14, 4), Visible = false };
        _courseInfoLabel.Dock = DockStyle.Fill;
        _courseInfoLabel.ForeColor = Theme.Muted;
        _courseInfoLabel.Font = new Font("Segoe UI", 8.5f);
        _courseInfoLabel.RightToLeft = RightToLeft.Yes;
        _courseInfoLabel.TextAlign = ContentAlignment.MiddleRight;
        _libraryProgress.Dock = DockStyle.Bottom;
        _libraryProgress.Height = LogicalToDeviceUnits(3);
        infoRow.Controls.Add(_courseInfoLabel);
        // شريط التقدّم الكلي رفيع تحت اسم المكتبة (من غير أرقام)
        sectionRow.Controls.Add(_libraryProgress);

        // ---------- البحث والفلتر ----------
        var searchRow = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(38), Padding = new Padding(10, 2, 14, 8), BackColor = Theme.Panel };
        _searchBox.RightToLeft = RightToLeft.Yes;
        _searchBox.PlaceholderText = "بحث… (Ctrl+F)";
        _searchBox.Font = new Font("Segoe UI", 9.5f);
        var searchHost = new RoundedHost(_searchBox, searchIcon: true, padding: 7) { Dock = DockStyle.Fill };
        BuildFilterButton(searchRow);
        BuildFoldButton(searchRow);
        searchRow.Controls.Add(searchHost);
        searchHost.BringToFront(); // الـ Fill لازم يترص في الآخر عشان ميغطيش الفلتر

        // ---------- الشجرة ----------
        _tree.Dock = DockStyle.Fill;
        _tree.BorderStyle = BorderStyle.None;
        _tree.BackColor = Theme.Panel;
        _tree.ForeColor = Theme.Text;
        _tree.Font = _boldNodeFont;
        _tree.FullRowSelect = true;
        _tree.HideSelection = false;
        _tree.ShowLines = false;
        _tree.ShowRootLines = true;
        _tree.ShowPlusMinus = true;
        _tree.ShowNodeToolTips = true;
        _tree.AllowDrop = true;
        _tree.Indent = LogicalToDeviceUnits(18);
        _tree.ItemHeight = LogicalToDeviceUnits(26);
        _tree.ImageList = Theme.CreateTreeIcons(LogicalToDeviceUnits(16));
        _tree.HandleCreated += (_, _) => NativeMethods.UseDarkExplorerTheme(_tree);

        BuildNotesPane();
        // كارت الشجرة فوق، وكارت العلامات والملاحظات تحت، وبينهم فراغ بيتسحب
        _treeCard.Controls.Add(_tree);
        _treeCard.Controls.Add(searchRow);
        _treeCard.Controls.Add(infoRow);
        _treeCard.Controls.Add(sectionRow);
        _treeCard.Controls.Add(titleRow);
        _sidePanel.Controls.Add(_treeCard);
        _sidePanel.Controls.Add(_notesSplitter);
        _sidePanel.Controls.Add(_notesPanel);
        SetNotesCollapsed(!_settings.NotesExpanded);
        BuildShelfPanel();
    }

    private static ToolStripButton ToolButton(string text, string tip, Action onClick)
    {
        var b = new ToolStripButton(text) { ToolTipText = tip, ForeColor = Theme.Text, Margin = new Padding(2) };
        b.Click += (_, _) => onClick();
        return b;
    }

    // =====================================================================
    //  المنيو
    // =====================================================================

    /// <summary>
    /// شريط القوايم: 7 قوايم ثابتة — ملف · تحرير · المكتبة · التشغيل · عرض · الأدوات · مساعدة
    /// («الأدوات» فيها الشرح والمذاكرة والذكاء والإنترنت).
    /// كل أمر ليه مكان واحد حسب معناه، والوضع المتقدم بيغيّر عناصر الشاشة بس (مش الأوامر).
    /// </summary>
    private void BuildMenus()
    {
        // ---------------- ملف: المكتبة كملف ----------------
        var file = SubMenu("ملف",
            Item("مكتبة جديدة…", NewLibraryDialog, Keys.Control | Keys.N),
            Item("فتح مكتبة…", OpenLibraryDialog, Keys.Control | Keys.O),
            _recentMenu = SubMenu("المكتبات الأخيرة"),
            new ToolStripSeparator(),
            Item("حفظ", () => { SaveLibrary(); ShowStatus("حُفظ ✓"); }, Keys.Control | Keys.S),
            Item("حفظ المكتبة باسم…", SaveLibraryAsDialog, Keys.Control | Keys.Shift | Keys.S),
            new ToolStripSeparator(),
            Item("نقل المكتبة إلى OneDrive / Google Drive (مزامنة بين الأجهزة)…", MoveLibraryToCloudDialog),
            Item("استرجاع نسخة احتياطية…", RestoreBackupDialog),
            new ToolStripSeparator(),
            Item("فتح مكان ملف المكتبة", () => { if (HasLibrary) OpenInExplorer(_libraryPath); }),
            Item("إغلاق المكتبة (يبقى البرنامج مفتوحًا)", () => { if (HasLibrary) StartWithoutLibrary(); }),
            new ToolStripSeparator(),
            Item("خروج", Close, Keys.Alt | Keys.F4));
        _recentMenu.DropDownOpening += (_, _) => BuildRecentMenu();
        _recentMenu.DropDownItems.Add(new ToolStripMenuItem("…"));

        // ---------------- تعديل ----------------
        var undo = Item("تراجع", UndoLast, display: "Ctrl+Z");
        var redo = Item("إعادة", RedoLast, display: "Ctrl+Y");
        var edit = SubMenu("تحرير",
            undo, redo,
            new ToolStripSeparator(),
            Item("إعادة تسمية المحدد…", RenameSelected, display: "F2"),
            Item("حذف المحدد…", DeleteSelected, display: "Delete"),
            new ToolStripSeparator(),
            Item("بحث في المكتبة", () => { if (_split.Panel2Collapsed) ToggleSidePanel(); _searchBox.Focus(); }, display: "Ctrl+F"),
            new ToolStripSeparator(),
            Item("الإعدادات…", SettingsDialog, Keys.Control | Keys.Oemcomma, "Ctrl+,"));
        edit.DropDownOpening += (_, _) =>
        {
            undo.Text = _undo.CanUndo ? $"تراجع: {_undo.UndoLabel}" : "تراجع";
            undo.Enabled = _undo.CanUndo;
            redo.Text = _undo.CanRedo ? $"إعادة: {_undo.RedoLabel}" : "إعادة";
            redo.Enabled = _undo.CanRedo;
        };

        // ---------------- المكتبة: الإضافة بترتيب الشجرة، وبعدها التحديث والتنظيم ----------------
        var addHere = SubMenu("➕  إضافة (حسب المحدد في الشجرة)");
        addHere.DropDownOpening += (_, _) =>
        {
            foreach (var old in addHere.DropDownItems.Cast<ToolStripItem>().ToList()) old.Dispose();
            addHere.DropDownItems.Clear();
            BuildAddMenu(_tree.SelectedNode?.Tag, addHere.DropDownItems);
        };
        addHere.DropDownItems.Add(new ToolStripMenuItem("…"));
        var sepAll = Item("فواصل بين كل الموضوعات", ToggleAllTopicSeparators);
        var library = SubMenu("المكتبة",
            addHere,
            new ToolStripSeparator(),
            Item("موضوع جديد…", () => AddTopic(), Keys.Control | Keys.Shift | Keys.N),
            Item("مصدر جديد في الموضوع المحدد…", AddPlaceFromMenu),
            Item("دورة من مجلد…", ImportCourseDialog, Keys.Control | Keys.I),
            Item("دورة من يوتيوب…", () => AddUrlsDialog(null), display: "Ctrl+U"),
            SubMenu("دورة من منصة",
                Item("🎓  يوديمي (بحسابك)…", () => UdemyImport()),
                Item("🎓  مهارة تك أو أي موقع Moodle…", () => MoodleImport()),
                Item("🎓  IT Legend…", () => ItLegendImport())),
            Item("دورة فارغة…", () => AddCourse(), Keys.Control | Keys.Shift | Keys.G),
            Item("محاضرة فارغة في الدورة المحددة…", () => AddGroup(), Keys.Control | Keys.G),
            Item("فيديوهات للمحاضرة المحددة…", () => AddVideosDialog(SelectedGroup()), Keys.Control | Keys.Shift | Keys.A),
            new ToolStripSeparator(),
            Item("📖  كتاب (PDF / EPUB) — ملف واحد أو أكثر…", () => AddBooksDialog(SelectedShelfContext())),
            Item("📁  مجلد كتب كامل (يصبح رفًا)…", () => AddBooksFromFolderDialog(SelectedShelfContext())),
            Item("🔗  كتاب من رابط (Google Drive / Dropbox / OneDrive)…", () => AddBookLinksDialog(SelectedShelfContext())),
            Item("📽  عرض PowerPoint…", () => AddPresentationsDialog(SelectedShelfContext()), display: "Ctrl+Shift+P"),
            Item("🧑‍🏫  سبورة جديدة…", () => NewBoardDialog(SelectedGroup())),
            Item("رف جديد…", () => AddShelfDialog(null)),
            Item("🔍  بحث داخل كل الكتب…", () => ShowBookSearch()),
            new ToolStripSeparator(),
            Item("🔄  اجلب الجديد من كل المجلدات المتابَعة", () => _ = SyncLinkedFoldersAsync(null, manual: true)),
            Item("🔄  اجلب الجديد من كل قوائم يوتيوب", () => _ = SyncPlaylistsAsync(null, quiet: false), Keys.Control | Keys.R),
            new ToolStripSeparator(),
            Item("بيانات الدورة المحددة…", () => EditCourseInfo(SelectedCourse()), Keys.F4),
            Item("تقسيم تلقائي للمحاضرة المحددة (عند Part 01)…", () => AutoSplitDialog(SelectedGroup())),
            SubMenu("ترتيب بالاسم",
                Item("فيديوهات المحاضرة المحددة", () => SortGroupVideos(SelectedGroup())),
                Item("محاضرات الدورة المحددة", () => SortGroups(SelectedCourse())),
                Item("الكورسات", SortCourses)),
            new ToolStripSeparator(),
            sepAll,
            Item("فتح الكل", () => SetAllExpanded(true), display: "Ctrl+Shift+]"),
            Item("طي الكل", () => SetAllExpanded(false), display: "Ctrl+Shift+["),
            new ToolStripSeparator(),
            Item("حفظ ملاحظات الدورة المحددة في ملف…", () => ExportNotesDialog(null), Keys.Control | Keys.E),
            Item("تصفير المشاهدة للمكتبة كلها…", () => ResetWatched(_library.AllVideos, "كل الدورات")));
        library.DropDownOpening += (_, _) => sepAll.Checked = _settings.TopicSeparators;

        // ---------------- التشغيل: كل حاجة بتأثر على الفيديو اللي شغال ----------------
        _autoNextItem = Item("تشغيل الفيديو التالي تلقائيًا", () =>
        {
            _settings.AutoPlayNext = !_settings.AutoPlayNext;
            _autoNextItem.Checked = _settings.AutoPlayNext;
        });
        _continueGroupsItem = Item("بعد آخر فيديو في المحاضرة، أكمل المحاضرة التالية (في الدورة نفسها)", () =>
        {
            _settings.ContinueToNextGroup = !_settings.ContinueToNextGroup;
            _continueGroupsItem.Checked = _settings.ContinueToNextGroup;
        });
        _resumeItem = Item("استكمال الفيديو من آخر موضع توقفت عنده", () =>
        {
            _settings.ResumePosition = !_settings.ResumePosition;
            _resumeItem.Checked = _settings.ResumePosition;
        });
        var tracks = SubMenu("الترجمة والصوت");
        tracks.DropDownOpening += (_, _) => BuildTracksMenu(tracks);
        tracks.DropDownItems.Add(new ToolStripMenuItem("…"));
        var normalize = Item("توحيد مستوى الصوت (بعد إعادة تشغيل البرنامج)", () => { });
        normalize.Checked = _settings.NormalizeAudio;
        normalize.Click += (_, _) =>
        {
            _settings.NormalizeAudio = !_settings.NormalizeAudio;
            normalize.Checked = _settings.NormalizeAudio;
            ShowStatus("سيُطبَّق في المرة القادمة التي تفتح فيها البرنامج", 5);
        };
        var skip = Item("تخطي الصمت تلقائيًا", ToggleSkipSilence, display: "Shift+S");
        var sleep = BuildSleepMenu();
        var play = SubMenu("التشغيل",
            Item("تشغيل / إيقاف مؤقت", TogglePlay, display: "Space"),
            Item("أكمل الدورة المحددة من آخر موضع", () => { if (SelectedCourse() is { } c) PlayCourse(c); }),
            Item("الفيديو التالي", () => PlayNext(false), display: "PageDown"),
            Item("الفيديو السابق", PlayPrevious, display: "PageUp"),
            Item("إيقاف", StopPlayback),
            new ToolStripSeparator(),
            Item("أسرع", () => ChangeRate(+1), display: "C"),
            Item("أبطأ", () => ChangeRate(-1), display: "X"),
            Item("السرعة العادية", () => SetRate(1f), display: "Z"),
            new ToolStripSeparator(),
            tracks, normalize, skip,
            new ToolStripSeparator(),
            _autoNextItem, _continueGroupsItem, _resumeItem,
            SubMenu("التكرار بعد انتهاء الفيديو", BuildRepeatMenuItems()),
            new ToolStripSeparator(),
            sleep);
        play.DropDownOpening += (_, _) =>
        {
            skip.Checked = _settings.SkipSilence;
            sleep.Text = _sleepAtUtc is { } at ? $"مؤقت النوم ({Math.Max(1, (int)Math.Ceiling((at - DateTime.UtcNow).TotalMinutes))} د)" : "مؤقت النوم";
        };

        // ---------------- عرض: شكل الشباك ----------------
        var seekPreview = Item("صورة معاينة على شريط التقديم", () => { });
        seekPreview.Checked = _settings.SeekPreview;
        seekPreview.Click += (_, _) =>
        {
            _settings.SeekPreview = !_settings.SeekPreview;
            seekPreview.Checked = _settings.SeekPreview;
            if (_settings.SeekPreview && Tools.FindFfmpeg() == null) _ = EnsureFfmpegAsync("صورة المعاينة");
        };
        var view = SubMenu("عرض",
            Item("ملء الشاشة", ToggleFullscreen, display: "F11"),
            Item("إظهار / إخفاء المكتبة", ToggleSidePanel, display: "F6"),
            Item("المكتبة: الكتب والعروض والسبورات", () => { if (_split.Panel2Collapsed) ToggleSidePanel(); ShowShelf(true); }, display: "Ctrl+Shift+B"),
            Item("المكتبة: الدورات والفيديوهات", () => { if (_split.Panel2Collapsed) ToggleSidePanel(); ShowShelf(false); }),
            Item("المشغل الصغير", ToggleMiniMode, display: "P"),
            _alwaysOnTopItem = Item("دائمًا فوق النوافذ الأخرى", ToggleAlwaysOnTop, display: "T"),
            Item("الشاشة الرئيسية", GoHome, display: "Alt+Home"),
            BuildTabsMenu(),
            new ToolStripSeparator(),
            seekPreview,
            new ToolStripSeparator(),
            _advancedItem = Item("الوضع المتقدم (شريط الحالة)", () => SetAdvancedMode(!AdvancedMode)));

        // ---------------- مذاكرة: العلامات والمراجعة والتركيز والإحصائيات ----------------
        var study = SubMenu("المذاكرة",
            Item("علامة عند هذه اللحظة", () => AddBookmark(false), display: "B"),
            Item("علامة بملاحظة…", () => AddBookmark(true), display: "Shift+B"),
            Item("علامة مراجعة (لتذكيرك بمراجعتها)…", AddReviewBookmark, display: "R"),
            Item("لقطة شاشة", TakeScreenshot, display: "S"),
            Item("كرّر جزءًا معيّنًا (بداية ونهاية)", CycleAbLoop, display: "A"),
            new ToolStripSeparator(),
            Item("مراجعة اليوم…", ShowReview, Keys.F7),
            Item("الأجزاء الصعبة في هذا الفيديو", ShowDifficultSpots),
            Item("«ما فاتك» في هذه الدورة", () => { var c = CurrentOrSelectedCourse(); if (c != null) ShowRecap(c, force: true); }),
            new ToolStripSeparator(),
            Item("بومودورو (تشغيل / إيقاف)", TogglePomodoro, Keys.F9),
            Item("وضع التركيز الصارم…", StartFocusDialog),
            Item("إنهاء وضع التركيز", () => EndFocus(early: true)),
            new ToolStripSeparator(),
            Item("إحصائيات المذاكرة", ShowStats, Keys.F8),
            Item("صفحة إنجازاتي (تُفتح في المتصفح)…", ExportAchievements));

        // ---------------- مساعدة ----------------
        var help = SubMenu("مساعدة",
            Item("دليل الاستخدام", () => HelpGuide.Open(this), display: "F1"),
            Item("جولة سريعة (3 خطوات)", StartTour),
            Item("اختصارات لوحة المفاتيح", ShowShortcuts),
            new ToolStripSeparator(),
            Item("التحقق من التحديثات", () => _ = CheckForUpdatesAsync(manual: true)),
            Item("مستودع التحديثات على GitHub…", SetUpdateRepoDialog),
            new ToolStripSeparator(),
            SubMenu("فتح مجلد",
                Item("البيانات (المكتبات والإعدادات)", () =>
                {
                    Directory.CreateDirectory(Storage.AppDataDir);
                    try { System.Diagnostics.Process.Start("explorer.exe", $"\"{Storage.AppDataDir}\""); } catch { }
                }),
                Item("الفيديوهات المنزّلة", () => OpenDataFolder("offline")),
                Item("اللقطات", OpenSnapshotsFolder),
                Item("سجل الأخطاء", () => OpenDataFolder("logs")),
                Item("الأدوات", () => OpenDataFolder("tools"))),
            new ToolStripSeparator(),
            Item("عن البرنامج", () => AboutDialog.Show(this, UpdateChecker.CurrentVersion.ToString(3) + (Application.ProductVersion.Contains("-test") ? " (نسخة تجريبية)" : ""),
                "محرك التشغيل: VLC (LibVLCSharp)\nفيديوهات النت: yt-dlp + Deno\nمنصة التشغيل: .NET " + Environment.Version.ToString(2) +
                "\n\nملف المكتبة الحالية:\n" + _libraryPath +
                "\n\nمجلد البيانات" + (Storage.IsPortable ? " (بجانب البرنامج)" : " (%AppData% — لا توجد صلاحية كتابة بجانب البرنامج)") +
                ":\n" + Storage.AppDataDir)));

        // ---------------- الأدوات: الشرح فوق، وبعدها المذاكرة والذكاء والإنترنت (كانت 3 قوايم منفصلة) ----------------
        var smart = BuildSmartMenu(); smart.Text = "الذكاء الاصطناعي";
        var online = BuildOnlineMenu(); online.Text = "الإنترنت والمنصات";
        var tools = SubMenu("الأدوات",
            Item("🔎  ابحث عن أمر…", ShowCommandPalette, display: "Ctrl+Shift+Space"),
            new ToolStripSeparator());
        foreach (var it in TeachingToolItems()) tools.DropDownItems.Add(it);
        // معمل الكود هنا بالاختصار الحقيقي (Ctrl+L)
        var lab = tools.DropDownItems.OfType<ToolStripMenuItem>().Last();
        lab.ShortcutKeyDisplayString = null; lab.ShortcutKeys = Keys.Control | Keys.L;
        tools.DropDownItems.Add(new ToolStripSeparator());
        var sessions = SubMenu("📚  الجلسات والحصص");
        sessions.DropDownOpening += (_, _) => { sessions.DropDownItems.Clear(); sessions.DropDownItems.AddRange(SessionMenuItems().ToArray()); };
        sessions.DropDownItems.AddRange(SessionMenuItems().ToArray());
        tools.DropDownItems.Add(sessions);
        tools.DropDownItems.Add(study);
        tools.DropDownItems.Add(smart);
        tools.DropDownItems.Add(online);

        // 7 قوايم بدل 9: ملف · تحرير · المكتبة · التشغيل · عرض · الأدوات · مساعدة
        _menu.Items.AddRange(new ToolStripItem[] { file, edit, library, play, view, tools, help });
    }

    private static ToolStripMenuItem SubMenu(string text, params ToolStripItem[] items)
    {
        var m = new ToolStripMenuItem(text) { ForeColor = Theme.Text };
        m.DropDownItems.AddRange(items);
        return m;
    }

    private static ToolStripMenuItem Item(string text, Action onClick, Keys keys = Keys.None, string? display = null)
    {
        var item = new ToolStripMenuItem(text) { ForeColor = Theme.Text };
        if (keys != Keys.None) item.ShortcutKeys = keys;
        if (display != null) item.ShortcutKeyDisplayString = display;
        item.Click += (_, _) => onClick();
        return item;
    }

    private void BuildRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();
        var list = RecentLibraries(10);
        if (list.Count == 0)
        {
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem("(لا يوجد)") { Enabled = false });
            return;
        }
        foreach (var e in list)
        {
            string path = e.Path;
            // الملفات اللي مش متاحة دلوقتي (درايف سحابي مش شغال مثلًا) بتظهر بس متعطلة، عشان المستخدم يعرف إنها لسه موجودة
            var item = Item(e.Text, () => SwitchLibrary(path));
            item.Enabled = e.Exists;
            item.Checked = e.Current;
            item.ToolTipText = path;
            _recentMenu.DropDownItems.Add(item);
        }
        if (list.Any(x => !x.Exists))
        {
            _recentMenu.DropDownItems.Add(new ToolStripSeparator());
            _recentMenu.DropDownItems.Add(Item("🧹  أزل المكتبات غير الموجودة من القائمة", RemoveMissingRecent));
        }
    }

    // =====================================================================
    //  الأحداث
    // =====================================================================

    private void WireEvents()
    {
        _btnPlay.Click += (_, _) => TogglePlay();
        _btnStop.Click += (_, _) => StopPlayback();
        _btnNext.Click += (_, _) => PlayNext(false);
        _btnPrev.Click += (_, _) => PlayPrevious();
        _btnFwd.Click += (_, _) => SeekBy(10_000);
        _btnBack.Click += (_, _) => SeekBy(-10_000);
        _btnMute.Click += (_, _) => ToggleMute();
        _btnFullscreen.Click += (_, _) => ToggleFullscreen();
        _btnPlaylist.Click += (_, _) => ToggleSidePanel();
        _btnMini.Click += (_, _) => ToggleMiniMode();
        _btnBookmark.Click += (_, e) => { if (e is MouseEventArgs { Button: not MouseButtons.Left }) return; AddBookmark(false); }; // كليك يمين = المنيو بس
        _btnBookmark.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowStudyMenu(_btnBookmark, new Point(0, -4), above: true); };

        _seekBar.Scrubbing += (_, v) =>
        {
            long len = CurrentLength();
            if (len > 0) _timeLabel.Set((long)(v * len), len);
        };
        _seekBar.ValueCommitted += (_, v) => SeekToFraction(v);
        string lastHover = "";
        _seekBar.HoverChanged += (_, v) =>
        {
            long len = CurrentLength();
            if (v < 0 || len <= 0 || _seekBar.IsDragging) { _toolTip.Hide(_seekBar); HideSeekPreview(); lastHover = ""; return; }
            string text = TimeFormat.Format((long)(v * len));
            if (BookmarkNear(v) is { } note) text += "  —  " + note;
            if (ShowSeekPreview(v, text)) { _toolTip.Hide(_seekBar); lastHover = ""; return; } // صورة معاينة (ffmpeg)
            if (text == lastHover) return;
            lastHover = text;
            var p = _seekBar.PointToClient(Cursor.Position);
            _toolTip.Show(text, _seekBar, p.X - 18, -LogicalToDeviceUnits(30), 1500);
        };

        // الشريط من 0 لـ 200% (النص التاني = تضخيم)
        _volumeBar.Scrubbing += (_, v) => SetVolume((int)Math.Round(v * MaxVolume));
        _volumeBar.ValueCommitted += (_, v) => SetVolume((int)Math.Round(v * MaxVolume));

        _speedCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_speedCombo.SelectedIndex >= 0) SetRate(Rates[_speedCombo.SelectedIndex]);
        };

        _courseLabel.Click += (_, _) => ShowLibrarySwitcher();

        // البحث: نستنى لحد ما المستخدم يقف عن الكتابة شوية بدل ما نبني الشجرة كلها مع كل حرف
        var searchDelay = new Timer { Interval = 220 };
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); RebuildTree(); };
        _searchBox.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };

        _tree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && e.Node.Tag is VideoItem) PlayNode(e.Node, fromStart: false);
        };
        _tree.NodeMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            _tree.SelectedNode = e.Node;
            ShowNodeMenu(e.Node, e.Location);
        };
        _tree.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && _tree.GetNodeAt(e.Location) == null)
                ShowEmptyAreaMenu(e.Location);
        };
        _tree.AfterExpand += (_, e) => OnNodeExpandChanged(e.Node, true);
        _tree.AfterCollapse += (_, e) => OnNodeExpandChanged(e.Node, false);
        _tree.ItemDrag += OnTreeItemDrag;
        _tree.DragEnter += OnTreeDragOver;
        _tree.DragOver += OnTreeDragOver;
        _tree.DragDrop += OnTreeDragDrop;

        // في المشغل الصغير الدبل كليك بيرجّع للمشغل الكامل بدل ملء الشاشة
        _videoView.MouseDoubleClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (_miniMode) ToggleMiniMode(); else ToggleFullscreen();
        };
        _videoView.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowVideoMenu(e.Location); };
        _videoView.MouseClick += (_, _) => _videoView.Focus();
        _videoView.MouseWheel += (_, e) => SetVolume(_settings.Volume + (e.Delta > 0 ? 5 : -5));

        // القايمة مستخبية: SplitContainer مش بيكبّر جزء الفيديو/الرئيسية مع الشباك (مثلًا تكبير الشاشة وقت الفتح)،
        // فكان بيفضل بعرضه القديم والباقي فاضي. أي تغيير في المقاس ← نظبطه، ومرة كمان بعد ما الويندوز يخلص.
        _split.SizeChanged += (_, _) =>
        {
            if (!_split.Panel2Collapsed || _splitAutoFixDisabledForTest) return;
            FixSplitPanelSize();
            if (IsHandleCreated) BeginInvoke(FixSplitPanelSize);
        };
        _split.SplitterMoved += (_, _) =>
        {
            if (!_applyingLayout && !_split.Panel2Collapsed)
                _settings.SidePanelWidth = DeviceToLogicalUnits(_split.Panel2.Width);
        };
        Shown += (_, _) =>
        {
            try { RegisterGlobalHotkeys(); } catch (Exception ex) { Log.Error("hotkeys", ex); }
            ApplySidePanelWidth();
            if (!_splitAutoFixDisabledForTest) FixSplitPanelSize();
            BeginInvoke(new Action(CheckLeftoverFocusBlock));
            bool withFiles = _pendingExternal != null;
            if (_pendingExternal is { } files) { _pendingExternal = null; BeginInvoke(() => OpenExternalFiles(files)); }
            bool tour = ShouldShowFirstRunTour(withFiles);
            if (tour) BeginInvoke(new Action(StartTour));
            else if (!withFiles && !SmokeTest.Active && _settings.Role == null) BeginInvoke(new Action(AskRoleIfNeeded));
            AfterStartupUpdateCheck(firstRunTour: tour);
            CheckLastCrash();
            BeginInvoke(new Action(OfferResumeDownloads));
        };
        DpiChanged += (_, _) => BeginInvoke(new Action(ApplySidePanelWidth));

        HandleCreated += (_, _) => NativeMethods.UseDarkTitleBar(this);
        Resize += (_, _) => AdaptUiTimer(); // الرجوع من التصغير: الواجهة تصحى فورًا

        // الملاحظات تتبع الفيديو المتحدد لما مفيش حاجة شغالة
        _tree.AfterSelect += (_, _) => { if (_currentVideo == null) RefreshNotesPane(); };
        // جهاز تاني عدّل القائمة (OneDrive)؟ ندمج أول ما ترجع للبرنامج
        Activated += (_, _) => { if (!_closing) MergeExternalChanges(); };
    }

    private int DeviceToLogicalUnits(int value) => (int)Math.Round(value * 96.0 / DeviceDpi);

    private bool _applyingLayout;

    /// <summary>
    /// عرض القايمة: بيتحسب من الإعدادات وبيتقص عشان يفضل للفيديو مساحة كفاية،
    /// والحد الأدنى للجزئين بيتحط هنا (بعد ما الشاشة تاخد مقاسها الحقيقي).
    /// </summary>
    private void ApplySidePanelWidth()
    {
        if (_split.Width <= 0) return;
        // القايمة مستخبية: مفيش عرض نظبطه. تغيير SplitterDistance هنا كان بيرص محتوى جزء الفيديو/الرئيسية
        // على العرض ده (320) وبعدين SplitContainer يمد الجزء من غير مايعيد رص اللي جواه ← الرئيسية تفضل ضيقة.
        // العرض المحفوظ بيتطبق لما القايمة تظهر (ToggleSidePanel / الرجوع من ملء الشاشة).
        if (_split.Panel2Collapsed && !_splitAutoFixDisabledForTest) { FixSplitPanelSize(); return; }
        _applyingLayout = true;
        try
        {
            int videoMin = LogicalToDeviceUnits(320);
            int sideMin = LogicalToDeviceUnits(240);
            int available = _split.Width - _split.SplitterWidth;

            // لو الشاشة صغيرة جدًا نقلل الحدود الدنيا بدل ما SplitContainer يرمي Exception
            if (videoMin + sideMin > available)
            {
                videoMin = Math.Max(0, available / 2);
                sideMin = Math.Max(0, available - videoMin);
            }
            _split.Panel1MinSize = 0;
            _split.Panel2MinSize = 0;

            int side = LogicalToDeviceUnits(Math.Clamp(_settings.SidePanelWidth, 240, 900));
            side = Math.Clamp(side, sideMin, Math.Max(sideMin, available - videoMin));
            _split.SplitterDistance = Math.Clamp(available - side, 0, Math.Max(0, available));

            _split.Panel1MinSize = videoMin;
            _split.Panel2MinSize = sideMin;
        }
        catch (InvalidOperationException) { /* مقاس مؤقت أثناء التصغير */ }
        finally { _applyingLayout = false; }
    }

    private void ApplySettings()
    {
        _timeLabel.ShowTotal = _settings.TimeShowsTotal;
        _autoNextItem.Checked = _settings.AutoPlayNext;
        _continueGroupsItem.Checked = _settings.ContinueToNextGroup;
        _resumeItem.Checked = _settings.ResumePosition;

        _settings.Volume = Math.Clamp(_settings.Volume, 0, MaxVolume);
        _volumeBar.Value = _settings.Volume / (double)MaxVolume;
        TopMost = _settings.AlwaysOnTop;
        if (_alwaysOnTopItem != null) _alwaysOnTopItem.Checked = _settings.AlwaysOnTop;
        _mp.Volume = _settings.Volume;

        int rateIndex = Array.FindIndex(Rates, r => Math.Abs(r - _settings.Rate) < 0.01f);
        if (rateIndex < 0) { rateIndex = Array.IndexOf(Rates, 1f); _settings.Rate = 1f; }
        _activeRate = _settings.Rate;
        _speedCombo.SelectedIndex = rateIndex;

        _split.Panel2Collapsed = !_settings.SidePanelVisible;
        UpdateActivityIndicators();

        if (_settings.WindowWidth > 0 && _settings.WindowHeight > 0)
        {
            var rect = new Rectangle(_settings.WindowX, _settings.WindowY, _settings.WindowWidth, _settings.WindowHeight);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = rect;
            }
        }
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void SaveSettings()
    {
        Rectangle b = _fullscreen ? _prevBounds : (WindowState == FormWindowState.Normal ? Bounds : RestoreBounds);
        _settings.WindowX = b.X;
        _settings.WindowY = b.Y;
        _settings.WindowWidth = b.Width;
        _settings.WindowHeight = b.Height;
        _settings.Maximized = _fullscreen ? _prevWindowState == FormWindowState.Maximized : WindowState == FormWindowState.Maximized;
        Storage.SaveSettings(_settings);
    }

    // =====================================================================
    //  الكيبورد
    // =====================================================================

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_shelfSearch.Focused)
        {
            if (keyData == Keys.Escape) { _shelfSearch.Clear(); _shelfTree.Focus(); return true; }
            if (keyData == Keys.Down) { _shelfTree.Focus(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        if (HandleTabKeys(keyData)) return true;
        if (HandleBookKeys(keyData)) return true;
        if (_searchBox.Focused)
        {
            if (keyData == Keys.Escape) { _searchBox.Clear(); _tree.Focus(); return true; }
            if (keyData == Keys.Down) { _tree.Focus(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        if (_notesBox.Focused)
        {
            // وإنت بتكتب ملاحظات: الحروف والمسافة للكتابة، مش للتحكم
            if (keyData == Keys.Escape) { CommitNotes(); _tree.Focus(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        if (_bookmarkList.Focused)
        {
            switch (keyData)
            {
                case Keys.Enter: JumpToSelectedBookmark(); return true;
                case Keys.Delete: DeleteSelectedBookmark(); return true;
                case Keys.F2: EditSelectedBookmark(); return true;
                case Keys.R: ToggleReviewForSelectedBookmark(); return true;
            }
        }

        bool tree = _tree.Focused;
        switch (keyData)
        {
            case Keys.Space: TogglePlay(); return true;
            case Keys.Enter when tree: PlaySelected(); return true;
            case Keys.Control | Keys.Shift | Keys.OemCloseBrackets: ExpandToLevel(4); return true;   // فتح الكل
            case Keys.Control | Keys.Shift | Keys.OemOpenBrackets: ExpandToLevel(0); return true;    // قفل الكل

            case Keys.Right when !tree: SeekBy(5_000); return true;
            case Keys.Left when !tree: SeekBy(-5_000); return true;
            case Keys.Control | Keys.Right: SeekBy(30_000); return true;
            case Keys.Control | Keys.Left: SeekBy(-30_000); return true;
            case Keys.Up when !tree: SetVolume(_settings.Volume + 5); return true;
            case Keys.Down when !tree: SetVolume(_settings.Volume - 5); return true;

            case Keys.PageDown: PlayNext(false); return true;
            case Keys.PageUp: PlayPrevious(); return true;

            case Keys.F11:
            case Keys.Alt | Keys.Enter:
                ToggleFullscreen(); return true;
            case Keys.F when !tree: ToggleFullscreen(); return true;
            case Keys.Escape when _fullscreen: ToggleFullscreen(); return true;
            case Keys.Escape when _miniMode: ToggleMiniMode(); return true;
            case Keys.F6: ToggleSidePanel(); return true;
            case Keys.Alt | Keys.Home: GoHome(); return true;
            case Keys.F1: HelpGuide.Open(this); return true;
            case Keys.Control | Keys.Shift | Keys.L: ShowLiveWindow(); return true;
            case Keys.Control | Keys.F: if (_split.Panel2Collapsed) ToggleSidePanel(); _searchBox.Focus(); return true;

            case Keys.M: ToggleMute(); return true;
            case Keys.B: AddBookmark(false); return true;
            case Keys.Shift | Keys.B: AddBookmark(true); return true;
            case Keys.A: CycleAbLoop(); return true;
            case Keys.S: TakeScreenshot(); return true;
            case Keys.T: ToggleAlwaysOnTop(); return true;
            case Keys.P: ToggleMiniMode(); return true;
            case Keys.O: _ = OcrFrameAsync(); return true;
            case Keys.R: AddReviewBookmark(); return true;
            case Keys.L: SetRepeatMode(RepeatMode == 1 ? 0 : 1, announce: true); return true;
            case Keys.Shift | Keys.S: ToggleSkipSilence(); return true;
            case Keys.Control | Keys.Z: UndoLast(); return true;
            case Keys.Control | Keys.Y: RedoLast(); return true;
            case Keys.C: ChangeRate(+1); return true;
            case Keys.X: ChangeRate(-1); return true;
            case Keys.Z: SetRate(1f); return true;

            case Keys.Control | Keys.V when tree: PasteUrls(); return true;
            case Keys.F2 when tree: RenameSelected(); return true;
            case Keys.Insert when tree: ShowAddMenuAtSelection(); return true;
            case Keys.Delete when tree: DeleteSelected(); return true;
            case Keys.W when tree: ToggleWatchedSelected(); return true;
            case Keys.Alt | Keys.Up when tree: MoveSelected(-1); return true;
            case Keys.Alt | Keys.Down when tree: MoveSelected(+1); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ShowShortcuts()
    {
        Dialogs.Info(this,
            "التشغيل\n" +
            "  Space  ←  تشغيل / إيقاف مؤقت\n" +
            "  → / ←  ←  تقديم / رجوع 5 ثوانٍ\n" +
            "  Ctrl + → / ←  ←  30 ثانية\n" +
            "  ↑ / ↓  ←  الصوت (أو عجلة الفأرة على الفيديو)\n" +
            "  M  ←  كتم الصوت (والصوت حتى 200%)\n" +
            "  B / Shift+B  ←  علامة / علامة بملاحظة\n" +
            "  A  ←  تكرار جزء: بداية، نهاية، إلغاء\n" +
            "  S  ←  لقطة شاشة\n" +
            "  T  ←  دائمًا فوق النوافذ الأخرى\n" +
            "  P  ←  المشغل الصغير (فوق كل النوافذ)\n" +
            "  Ctrl + Z / Ctrl + Y  ←  تراجع / إعادة لتعديلات المكتبة\n" +
            "  Ctrl + E  ←  تصدير ملاحظات الدورة\n" +
            "  C / X / Z  ←  أسرع / أبطأ / سرعة عادية\n" +
            "  PageDown / PageUp  ←  الفيديو التالي / السابق\n" +
            "  F11 أو F أو نقر مزدوج  ←  ملء الشاشة (Esc للخروج)\n" +
            "  F6  ←  إظهار / إخفاء المكتبة\n" +
            "  O  ←  نسخ الكلام المكتوب على الشاشة\n" +
            "  Shift+S  ←  تخطي الصمت\n\n" +
            "التبويبات\n" +
            "  Ctrl + Tab / Ctrl + Shift + Tab  ←  التبويب التالي / السابق\n" +
            "  Ctrl + 1 … 9  ←  انتقل إلى تبويب بعينه   ·   Ctrl + W  ←  إغلاق التبويب\n" +
            "  Ctrl + Shift + Space  ←  ابحث عن أي أمر\n\n" +
            "المذاكرة والذكاء\n" +
            "  R  ←  علامة مراجعة متباعدة   ·   F7  ←  مراجعة اليوم\n" +
            "  F4  ←  بيانات الدورة (الموعد والوسوم)   ·   F8  ←  الإحصائيات   ·   F9  ←  بومودورو\n" +
            "  Ctrl+T  ←  نص الفيديو   ·   Ctrl+Shift+F  ←  بحث في كلام الفيديوهات\n" +
            "  Ctrl+Q  ←  اختبرني   ·   Ctrl+K  ←  اسأل الدورة   ·   Ctrl+L  ←  معمل الكود\n" +
            "  Ctrl+,  ←  الإعدادات\n\n" +
            "المكتبة (عند تحديدها)\n" +
            "  Enter  ←  تشغيل الفيديو / المحاضرة / الدورة\n" +
            "  F2  ←  إعادة تسمية\n" +
            "  W  ←  تعليم كمُشاهَد / غير مُشاهَد\n" +
            "  Alt + ↑ / ↓  ←  تحريك لأعلى / لأسفل\n" +
            "  Delete  ←  حذف\n" +
            "  Ctrl + F  ←  بحث\n" +
            "  Ctrl + U  ←  إضافة رابط يوتيوب / قائمة تشغيل\n" +
            "  Ctrl + V  ←  لصق رابط من الحافظة\n" +
            "  السحب والإفلات  ←  نقل فيديو بين المحاضرات، أو محاضرة بين الدورات،\n" +
            "                         أو ترتيب الدورات، أو سحب ملفات/مجلدات من ويندوز",
            "اختصارات لوحة المفاتيح  ·  الدليل الكامل: F1");
    }

    // =====================================================================
    //  ملء الشاشة وإخفاء القايمة
    // =====================================================================

    private void ToggleFullscreen()
    {
        if (_fullscreen && FocusActive && !ConfirmLeaveFocus()) return;
        // من المشغل الصغير لملء الشاشة: نرجع للعادي الأول عشان الرجوع من ملء الشاشة يبقى سليم
        if (!_fullscreen && _miniMode) ToggleMiniMode();
        if (!_fullscreen)
        {
            _prevWindowState = WindowState;
            _prevBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _fullscreen = true;
            UpdateTabStripVisible();

            SuspendLayout();
            _menuHost.Visible = _statusHost.Visible = _activityHost.Visible = false;
            _split.Panel2Collapsed = true;
            SetChromeless(true);
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
            ResumeLayout(true);
            RefitVideo();
            _lastMouseActivity = DateTime.Now;
            ShowStatus("Esc للخروج من ملء الشاشة");
        }
        else
        {
            _fullscreen = false;
            UpdateTabStripVisible();
            _udemyPane?.ExitPageFullscreen();
            SuspendLayout();
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = _prevBounds;
            WindowState = _prevWindowState;
            _menuHost.Visible = _activityHost.Visible = true;
            _statusHost.Visible = StatusBarWanted;
            _split.Panel2Collapsed = !_settings.SidePanelVisible;
            SetChromeless(false);
            ControlChromeVisible = true;
            ResumeLayout(true);
            RefitVideo();
            ApplySidePanelWidth();
        }
    }

    /// <summary>في ملء الشاشة: شريط التحكم يظهر لما الماوس يتحرك أو يقرب من تحت، ويختفي بعد ثانيتين.</summary>
    private void UpdateFullscreenChrome()
    {
        Point p = Cursor.Position;
        if (p != _lastCursor) { _lastCursor = p; _lastMouseActivity = DateTime.Now; }

        Rectangle screen = Screen.FromControl(this).Bounds;
        bool nearBottom = p.Y >= screen.Bottom - _controlBar.Height - LogicalToDeviceUnits(20);
        bool recentlyMoved = (DateTime.Now - _lastMouseActivity).TotalSeconds < 2.0;
        bool show = (nearBottom || recentlyMoved || !_mp.IsPlaying) && !ReaderShown;
        if (ControlChromeVisible != show) ControlChromeVisible = show;
    }

    private void ToggleSidePanel()
    {
        if (_fullscreen) return;
        _settings.SidePanelVisible = _split.Panel2Collapsed;
        _split.Panel2Collapsed = !_settings.SidePanelVisible;
        if (_settings.SidePanelVisible) ApplySidePanelWidth(); else FixSplitPanelSize();
        UpdateActivityIndicators();
    }

    // =====================================================================
    //  رسالة مؤقتة في شريط الحالة
    // =====================================================================

    private void ShowStatus(string message, double seconds = 3)
    {
        _statusMessage.Text = message;
        _statusMessageUntil = DateTime.Now.AddSeconds(seconds);
        // الوضع البسيط من غير شريط حالة: الرسالة بتظهر لثواني مكان اسم الفيديو تحت الشاشة وبعدين الاسم يرجع
        if (!AdvancedMode && !string.IsNullOrEmpty(message))
        {
            _toastUntil = _statusMessageUntil;
            _nowPlayingLabel.ForeColor = Theme.Accent;
            _nowPlayingLabel.Text = message;
        }
    }

    private DateTime _toastUntil = DateTime.MinValue;

    /// <summary>بيتنده كل ربع ثانية: يرجّع اسم الفيديو بعد ما الرسالة تخلص.</summary>
    private void ExpireToast()
    {
        if (_toastUntil == DateTime.MinValue || DateTime.Now < _toastUntil) return;
        _toastUntil = DateTime.MinValue;
        _nowPlayingLabel.ForeColor = Theme.Text;
        UpdateNowPlaying();
    }

    /// <summary>شريط الحالة تحت: في الوضع المتقدم بس (ومش في المشغل الصغير أو ملء الشاشة).</summary>
    private bool StatusBarWanted => AdvancedMode && !_miniMode && !_fullscreen;

    private void UpdateTitle()
    {
        Text = _currentLive != null ? $"{_currentLive.Name}  —  مباشر  —  {AppTitle}"
            : PresentationShown ? $"{_presBook!.Title}  —  {AppTitle}"
            : ReaderShown ? $"{_currentBook!.Title}  —  {AppTitle}"
            : _currentVideo != null ? $"{_currentVideo.Title}  —  {_currentGroup?.Name}  —  {AppTitle}"
            : HasLibrary ? $"{_library.Name}  —  {AppTitle}" : AppTitle;
        SyncTabs();
    }

    private void UpdateNowPlaying()
    {
        if (DateTime.Now < _toastUntil) return; // فيه رسالة معروضة؛ الاسم هيرجع لما تخلص
        if (_currentLive != null) { _nowPlayingLabel.Text = LiveLabel(); return; }
        if (_currentVideo == null || _currentGroup == null) { _nowPlayingLabel.Text = ""; return; }
        var course = _library.FindCourseOf(_currentGroup);
        // الاسم بس؛ الكورس والمحاضرة بيظهروا لما تقف بالماوس
        _nowPlayingLabel.Text = _currentVideo.Title;
        _toolTip.SetToolTip(_nowPlayingLabel, $"{course?.Name}  ›  {_currentGroup.Name}  ›  {_currentVideo.Title}");
    }

    // =====================================================================
    //  القفل
    // =====================================================================

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_shutdownDone) { base.OnFormClosing(e); return; }
        if (!_closing && FocusActive && e.CloseReason == CloseReason.UserClosing && !ConfirmLeaveFocus()) { e.Cancel = true; return; }
        if (!_closing && _focusBlocked) EndFocus(early: true);
        // تنزيلات شغالة: نسأل، ولو قفلت بتقف وتكمّل المرة الجاية
        if (!_closing && !StopDownloadsForClose()) { e.Cancel = true; return; }

        // VLC لازم يقف في Thread تاني عشان الواجهة متعلقش
        e.Cancel = true;
        if (_closing) return;
        _closing = true;

        if (Recording) { try { await StopRecordingAsync(save: true, quick: true); } catch (Exception ex) { Log.Error("recording close", ex); } }
        try { _screenInk?.Close(); _hotkeys?.Dispose(); _hotkeys = null; } catch { }
        try { FinishSuspendedPresentation(); } catch (Exception ex) { Log.Error("presentation close", ex); }
        await FlushInkForCloseAsync();
        SaveCurrentPosition();
        CommitNotes();
        if (ReaderShown) FlushReading();
        FlushSession();
        SaveTabsForClose();
        // المكتبة لازم تتحفظ على آخر حالة: لو الحفظ فشل (الدرايف مش متاح مثلًا) نسأل بدل ما التغييرات تضيع
        while (HasLibrary && !TrySaveLibrary(out string? error))
        {
            var answer = MessageBox.Show(this,
                "تعذّر حفظ المكتبة قبل الإغلاق:\n" + error + "\n\n" +
                "إعادة المحاولة: أحاول مرة أخرى (مثلًا بعد تشغيل Drive)\nتجاهل: أغلق دون آخر التغييرات\nإلغاء: لا تغلق",
                "حفظ المكتبة", MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            if (answer == DialogResult.Retry) continue;
            if (answer == DialogResult.Ignore) { Log.Info("closed without saving the last changes"); break; }
            _closing = false;   // «إلغاء»: البرنامج يفضل مفتوح
            _pendingInstaller = null;           // والتحديث مايتثبتش في نص الشغل
            _settings.ReopenAfterUpdate = null;
            return;
        }
        SaveSettings();
        _uiTimer.Stop();
        Log.Info("app closed");
        Hide();

        try { await Task.Run(() => _mp.Stop()).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch { /* لو VLC علّق مش هنستنى أكتر من كده */ }
        ClearCachesOnExit();

        _shutdownDone = true;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        LaunchPendingInstaller();
        try
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _videoView.MediaPlayer = null;
            _mp.Dispose();
            _currentMedia?.Dispose();
            _libVLC.Dispose();
        }
        catch { }
    }

    private static string RateText(float r) => r.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
}
