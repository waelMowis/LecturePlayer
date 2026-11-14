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
    private readonly Label _timeLabel = new();
    private readonly Label _nowPlayingLabel = new();
    private readonly SelectBox _speedCombo = new();
    private readonly LectureTreeView _tree = new();
    private readonly TextBox _searchBox = new();
    private readonly Label _courseLabel = new();
    private readonly Label _courseInfoLabel = new();
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

        string path = openPath ?? _settings.LastLibraryPath ?? "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) path = Storage.DefaultLibraryPath;
        LoadLibrary(path);

        _uiTimer.Tick += (_, _) => OnUiTick();
        _uiTimer.Start();
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
        _toolTip.SetToolTip(_btnBack, "رجوع 10 ثواني (Ctrl+←  = 30 ثانية)");
        _toolTip.SetToolTip(_btnPlay, "تشغيل / إيقاف مؤقت (Space)");
        _toolTip.SetToolTip(_btnFwd, "تقديم 10 ثواني (Ctrl+→  = 30 ثانية)");
        _toolTip.SetToolTip(_btnNext, "الفيديو التالي (PageDown)");
        _toolTip.SetToolTip(_btnStop, "إيقاف");
        _toolTip.SetToolTip(_btnMute, "كتم الصوت (M)");
        _toolTip.SetToolTip(_btnFullscreen, "ملء الشاشة (F11 أو دبل كليك)");
        _toolTip.SetToolTip(_btnPlaylist, "إظهار / إخفاء قايمة المحاضرات (F6)");
        _toolTip.SetToolTip(_btnMini, "المشغل الصغير (P)");

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
        _timeLabel.Width = 170;
        _timeLabel.TextAlign = ContentAlignment.MiddleRight;
        _timeLabel.ForeColor = Theme.Text;
        _timeLabel.Font = new Font("Consolas", 10.5f);
        _timeLabel.Text = "00:00 / 00:00";
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
        left.Controls.AddRange(new Control[] { _btnPrev, _btnBack, _btnPlay, _btnFwd, _btnNext, _btnStop });
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

        right.Controls.AddRange(new Control[] { speedLabel, _speedCombo, _btnMute, _volumeBar, _btnPlaylist, _btnMini, _btnFullscreen });

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
        set { _controlCard.Visible = value; _controlGap.Visible = value && !_videoCard.Flat; }
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

    /// <summary>شريط الأيقونات على طرف الشاشة (زي المحررات الحديثة): القايمة، البحث، المراجعة، الإحصائيات…</summary>
    private void BuildActivityBar()
    {
        _activity.Dock = DockStyle.Fill;
        _activity.Width = LogicalToDeviceUnits(46);
        _activity.BackColor = Theme.Panel;
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel,
            Padding = new Padding(0, 4, 0, 0)
        };
        GlyphButton Act(string glyph, string tip, Action click, Control parent)
        {
            var b = new GlyphButton(glyph, 16f)
            {
                Size = new Size(LogicalToDeviceUnits(46), LogicalToDeviceUnits(44)), Margin = Padding.Empty,
                ShowIndicator = true, IndicatorOnRight = true
            };
            b.Click += (_, _) => click();
            _toolTip.SetToolTip(b, tip);
            parent.Controls.Add(b);
            return b;
        }
        _activityExplorer = Act(Glyphs.Library, "قايمة الكورسات (F6)", ToggleSidePanel, top);
        _activityExplorer.Active = true;
        Act(Glyphs.Search, "بحث في كلام الفيديوهات (Ctrl+Shift+F)", ShowTranscriptSearch, top);
        Act(Glyphs.History, "مراجعة النهارده (F7)", ShowReview, top);
        Act(Glyphs.Radio, "بث مباشر: راديو وتليفزيون (Ctrl+Shift+L)", ShowLiveWindow, top);
        Act(Glyphs.Chart, "إحصائيات المذاكرة (F8)", ShowStats, top);
        Act(Glyphs.Chat, "اسأل الكورس (Ctrl+K)", AskCourse, top);
        Act(Glyphs.Code, "معمل الكود (Ctrl+L)", () => OpenCodeLab(""), top);
        Act(Glyphs.Timer, "وضع التركيز", StartFocusDialog, top);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = LogicalToDeviceUnits(46), BackColor = Theme.Panel };
        bottom.Height = LogicalToDeviceUnits(92);
        var settings = Act(Glyphs.Settings, "الإعدادات (Ctrl+,)", SettingsDialog, bottom);
        settings.Dock = DockStyle.Bottom;
        var help = Act(Glyphs.Help, "دليل الاستخدام (F1)", () => HelpGuide.Open(this), bottom);
        help.Dock = DockStyle.Bottom;
        _activity.Controls.Add(top);
        _activity.Controls.Add(bottom);
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
        void MoreItem(string text, Action click) { var i = new ToolStripMenuItem(text) { ForeColor = Theme.Text }; i.Click += (_, _) => click(); moreMenu.Items.Add(i); }
        MoreItem("استيراد فولدر فيه أكتر من كورس…", ImportMultiCourseDialog);
        MoreItem("تقسيم المحاضرة المحددة (عند Part 01)…", () => AutoSplitDialog(SelectedGroup()));
        MoreItem("بيانات الكورس المحدد…  (F4)", () => EditCourseInfo(SelectedCourse()));
        moreMenu.Items.Add(new ToolStripSeparator());
        MoreItem("ترتيب الكورسات بالاسم", SortCourses);
        MoreItem("فتح الكل", () => SetAllExpanded(true));
        MoreItem("قفل الكل", () => SetAllExpanded(false));
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
        _toolTip.SetToolTip(_courseLabel, "اضغط لتغيير اسم القائمة");
        sectionRow.Controls.Add(_courseLabel);
        Icon(Glyphs.Library, "موضوع جديد — تفريعة رئيسية زي «C#» أو «قواعد البيانات» (Ctrl+Shift+N)", () => AddTopic(), sectionRow);
        Icon(Glyphs.NewFolder, "كورس جديد في الموضوع المحدد (Ctrl+Shift+G)", () => AddCourse(), sectionRow);
        Icon(Glyphs.Add, "محاضرة جديدة في الكورس المحدد (Ctrl+G)", () => AddGroup(), sectionRow);
        Icon(Glyphs.Video, "إضافة فيديوهات للمحاضرة المحددة (Ctrl+Shift+A)", () => AddVideosDialog(SelectedGroup()), sectionRow);
        Icon(Glyphs.OpenFolder, "استيراد فولدر كورس (Ctrl+I)", ImportCourseDialog, sectionRow);
        Icon(Glyphs.Link, "رابط يوتيوب أو قائمة تشغيل (Ctrl+U)", () => AddUrlsDialog(null), sectionRow);

        var infoRow = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(26), BackColor = Theme.Panel, Padding = new Padding(14, 0, 14, 6) };
        _courseInfoLabel.Dock = DockStyle.Fill;
        _courseInfoLabel.ForeColor = Theme.Muted;
        _courseInfoLabel.Font = new Font("Segoe UI", 8.5f);
        _courseInfoLabel.RightToLeft = RightToLeft.Yes;
        _courseInfoLabel.TextAlign = ContentAlignment.MiddleRight;
        _libraryProgress.Dock = DockStyle.Bottom;
        _libraryProgress.Height = LogicalToDeviceUnits(4);
        infoRow.Controls.Add(_courseInfoLabel);
        infoRow.Controls.Add(_libraryProgress);

        // ---------- البحث والفلتر ----------
        var searchRow = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(38), Padding = new Padding(10, 2, 14, 8), BackColor = Theme.Panel };
        _searchBox.RightToLeft = RightToLeft.Yes;
        _searchBox.PlaceholderText = "بحث… (Ctrl+F)";
        _searchBox.Font = new Font("Segoe UI", 9.5f);
        var searchHost = new RoundedHost(_searchBox, searchIcon: true, padding: 7) { Dock = DockStyle.Fill };
        BuildFilterButton(searchRow);
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

    private void BuildMenus()
    {
        var file = SubMenu("ملف",
            Item("قائمة جديدة…", NewLibraryDialog, Keys.Control | Keys.N),
            Item("فتح قائمة…", OpenLibraryDialog, Keys.Control | Keys.O),
            Item("حفظ", () => { SaveLibrary(); ShowStatus("اتحفظ ✓"); }, Keys.Control | Keys.S),
            Item("حفظ باسم…", SaveLibraryAsDialog, Keys.Control | Keys.Shift | Keys.S),
            Item("نقل القائمة لـ OneDrive (مزامنة بين الأجهزة)…", MoveLibraryToCloudDialog),
            Item("استرجاع نسخة احتياطية…", RestoreBackupDialog),
            new ToolStripSeparator(),
            Item("تصدير ملاحظات الكورس المحدد (Markdown)…", () => ExportNotesDialog(null), Keys.Control | Keys.E),
            new ToolStripSeparator(),
            _recentMenu = SubMenu("القوائم الأخيرة"),
            new ToolStripSeparator(),
            Item("فتح مكان ملف القائمة", () => OpenInExplorer(_libraryPath)),
            Item("فتح فولدر البيانات (القوائم والإعدادات)", () =>
            {
                Directory.CreateDirectory(Storage.AppDataDir);
                try { System.Diagnostics.Process.Start("explorer.exe", $"\"{Storage.AppDataDir}\""); } catch { }
            }),
            new ToolStripSeparator(),
            Item("خروج", Close, Keys.Alt | Keys.F4));
        _recentMenu.DropDownOpening += (_, _) => BuildRecentMenu();
        _recentMenu.DropDownItems.Add(new ToolStripMenuItem("…"));

        var groups = SubMenu("الكورسات والمحاضرات",
            Item("موضوع جديد (تفريعة رئيسية)…", () => AddTopic(), Keys.Control | Keys.Shift | Keys.N),
            Item("كورس جديد…", () => AddCourse(), Keys.Control | Keys.Shift | Keys.G),
            Item("محاضرة جديدة في الكورس المحدد…", () => AddGroup(), Keys.Control | Keys.G),
            Item("إضافة فيديوهات للمحاضرة المحددة…", () => AddVideosDialog(SelectedGroup()), Keys.Control | Keys.Shift | Keys.A),
            new ToolStripSeparator(),
            Item("استيراد فولدر كورس…", ImportCourseDialog, Keys.Control | Keys.I),
            Item("استيراد فولدر فيه أكتر من كورس…", ImportMultiCourseDialog, Keys.Control | Keys.Shift | Keys.I),
            Item("إضافة فولدر محاضرات للكورس المحدد…", () =>
            {
                if (SelectedCourse() is { } c) ImportIntoCourseDialog(c);
                else Dialogs.Info(this, "حدد كورس الأول.");
            }),
            new ToolStripSeparator(),
            Item("تقسيم تلقائي للمحاضرة المحددة (عند Part 01)…", () => AutoSplitDialog(SelectedGroup())),
            Item("ترتيب فيديوهات المحاضرة المحددة بالاسم", () => SortGroupVideos(SelectedGroup())),
            Item("ترتيب محاضرات الكورس المحدد بالاسم", () => SortGroups(SelectedCourse())),
            Item("ترتيب الكورسات بالاسم", SortCourses),
            new ToolStripSeparator(),
            Item("بيانات الكورس المحدد…", () => EditCourseInfo(SelectedCourse()), display: "F4"),
            new ToolStripSeparator(),
            Item("فتح الكل", () => SetAllExpanded(true)),
            Item("قفل الكل", () => SetAllExpanded(false)),
            new ToolStripSeparator(),
            Item("تصفير المشاهدة للقائمة كلها…", () => ResetWatched(_library.AllVideos, "كل الكورسات")));

        _autoNextItem = Item("تشغيل الفيديو التالي تلقائيًا", () =>
        {
            _settings.AutoPlayNext = !_settings.AutoPlayNext;
            _autoNextItem.Checked = _settings.AutoPlayNext;
        });
        _continueGroupsItem = Item("بعد آخر فيديو في المحاضرة، كمّل على المحاضرة اللي بعدها (في نفس الكورس)", () =>
        {
            _settings.ContinueToNextGroup = !_settings.ContinueToNextGroup;
            _continueGroupsItem.Checked = _settings.ContinueToNextGroup;
        });
        _resumeItem = Item("استكمال الفيديو من آخر مكان وقفت عنده", () =>
        {
            _settings.ResumePosition = !_settings.ResumePosition;
            _resumeItem.Checked = _settings.ResumePosition;
        });

        var play = SubMenu("التشغيل",
            Item("تشغيل / إيقاف مؤقت", TogglePlay, display: "Space"),
            Item("كمّل الكورس المحدد من آخر مكان", () => { if (SelectedCourse() is { } c) PlayCourse(c); }),
            Item("الفيديو التالي", () => PlayNext(false), display: "PageDown"),
            Item("الفيديو السابق", PlayPrevious, display: "PageUp"),
            Item("إيقاف", StopPlayback),
            new ToolStripSeparator(),
            Item("أسرع", () => ChangeRate(+1), display: "C"),
            Item("أبطأ", () => ChangeRate(-1), display: "X"),
            Item("السرعة العادية", () => SetRate(1f), display: "Z"),
            new ToolStripSeparator(),
            _autoNextItem, _continueGroupsItem, _resumeItem,
            new ToolStripSeparator(),
            Item("ملء الشاشة", ToggleFullscreen, display: "F11"),
            Item("إظهار / إخفاء القايمة", ToggleSidePanel, display: "F6"),
            _alwaysOnTopItem = Item("دايمًا فوق الشبابيك التانية", ToggleAlwaysOnTop, display: "T"),
            Item("الشاشة الرئيسية", GoHome));

        var tracks = SubMenu("الترجمة والصوت");
        tracks.DropDownOpening += (_, _) => BuildTracksMenu(tracks);
        tracks.DropDownItems.Add(new ToolStripMenuItem("…"));
        var normalize = Item("توحيد مستوى الصوت (بعد إعادة تشغيل البرنامج)", () => { });
        normalize.Checked = _settings.NormalizeAudio;
        normalize.Click += (_, _) =>
        {
            _settings.NormalizeAudio = !_settings.NormalizeAudio;
            normalize.Checked = _settings.NormalizeAudio;
            ShowStatus("هيتطبق المرة الجاية اللي تفتح فيها البرنامج", 5);
        };

        var study = SubMenu("مذاكرة",
            Item("علامة عند اللحظة دي", () => AddBookmark(false), display: "B"),
            Item("علامة بملاحظة…", () => AddBookmark(true), display: "Shift+B"),
            Item("تكرار جزء (A–B)", CycleAbLoop, display: "A"),
            Item("لقطة شاشة", TakeScreenshot, display: "S"),
            Item("فتح فولدر اللقطات", OpenSnapshotsFolder),
            new ToolStripSeparator(),
            Item("تصدير ملاحظات الكورس (Markdown)…", () => ExportNotesDialog(null), display: "Ctrl+E"),
            new ToolStripSeparator(),
            Item("إحصائيات المذاكرة", ShowStats, Keys.F8),
            Item("بومودورو (تشغيل / إيقاف)", TogglePomodoro, Keys.F9),
            Item("بيانات الكورس المحدد (الموعد والوسوم)…", () => EditCourseInfo(SelectedCourse()), Keys.F4),
            Item("الإعدادات…", SettingsDialog, Keys.Control | Keys.Oemcomma, "Ctrl+,"),
            new ToolStripSeparator(),
            tracks,
            normalize);

        var seekPreview = Item("صورة معاينة على شريط التقديم", () => { });
        seekPreview.Checked = _settings.SeekPreview;
        seekPreview.Click += (_, _) =>
        {
            _settings.SeekPreview = !_settings.SeekPreview;
            seekPreview.Checked = _settings.SeekPreview;
            if (_settings.SeekPreview && Tools.FindFfmpeg() == null) _ = EnsureFfmpegAsync("صورة المعاينة");
        };
        var tools = SubMenu("أدوات",
            Item("تراجع", UndoLast, display: "Ctrl+Z"),
            Item("إعادة", RedoLast, display: "Ctrl+Y"),
            new ToolStripSeparator(),
            Item("المشغل الصغير", ToggleMiniMode, display: "P"),
            seekPreview,
            new ToolStripSeparator(),
            Item("تنزيل ffmpeg (المعاينة والتنزيل والتفريغ)…", () => _ = EnsureFfmpegAsync("المعاينة وتنزيل يوتيوب والتفريغ")),
            Item("إلغاء التنزيل الشغال", CancelDownload),
            new ToolStripSeparator(),
            Item("فتح فولدر الأدوات", () => OpenDataFolder("tools")),
            Item("فتح فولدر الفيديوهات المتنزلة", () => OpenDataFolder("offline")),
            Item("فتح فولدر سجل الأخطاء", () => OpenDataFolder("logs")));
        tools.DropDownOpening += (_, _) =>
        {
            ((ToolStripMenuItem)tools.DropDownItems[0]).Text = _undo.CanUndo ? $"تراجع: {_undo.UndoLabel}" : "تراجع";
            ((ToolStripMenuItem)tools.DropDownItems[0]).Enabled = _undo.CanUndo;
            ((ToolStripMenuItem)tools.DropDownItems[1]).Text = _undo.CanRedo ? $"إعادة: {_undo.RedoLabel}" : "إعادة";
            ((ToolStripMenuItem)tools.DropDownItems[1]).Enabled = _undo.CanRedo;
        };

        var help = SubMenu("مساعدة",
            Item("التحقق من التحديثات", () => _ = CheckForUpdatesAsync(manual: true)),
            Item("مستودع التحديثات على GitHub…", SetUpdateRepoDialog),
            new ToolStripSeparator(),
            Item("دليل الاستخدام", () => HelpGuide.Open(this), display: "F1"),
            Item("اختصارات الكيبورد", ShowShortcuts),
            Item("عن البرنامج", () => AboutDialog.Show(this, UpdateChecker.CurrentVersion.ToString(3),
                "محرك التشغيل: VLC (LibVLCSharp)\nفيديوهات النت: yt-dlp + Deno\nمنصة التشغيل: .NET " + Environment.Version.ToString(2) +
                "\n\nملف القائمة الحالي:\n" + _libraryPath +
                "\n\nفولدر البيانات" + (Storage.IsPortable ? " (جنب البرنامج)" : " (%AppData% — مفيش صلاحية كتابة جنب البرنامج)") +
                ":\n" + Storage.AppDataDir)));

        _menu.Items.AddRange(new ToolStripItem[] { file, groups, play, study, BuildSmartMenu(), BuildBoldMenu(), BuildOnlineMenu(), BuildLiveMenu(), tools, help });
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
        var files = _settings.RecentFiles.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem("(مفيش)") { Enabled = false });
            return;
        }
        foreach (string f in files)
        {
            string path = f;
            var item = Item(Path.GetFileNameWithoutExtension(f) + "    —    " + f, () => SwitchLibrary(path));
            item.Checked = string.Equals(f, _libraryPath, StringComparison.OrdinalIgnoreCase);
            _recentMenu.DropDownItems.Add(item);
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

        _seekBar.Scrubbing += (_, v) =>
        {
            long len = CurrentLength();
            if (len > 0) _timeLabel.Text = $"{TimeFormat.Format((long)(v * len))} / {TimeFormat.Format(len)}";
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

        _courseLabel.Click += (_, _) => RenameLibrary();

        _searchBox.TextChanged += (_, _) => RebuildTree();

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

        _split.SplitterMoved += (_, _) =>
        {
            if (!_applyingLayout && !_split.Panel2Collapsed)
                _settings.SidePanelWidth = DeviceToLogicalUnits(_split.Panel2.Width);
        };
        Shown += (_, _) =>
        {
            ApplySidePanelWidth();
            BeginInvoke(new Action(CheckLeftoverFocusBlock));
            if (_pendingExternal is { } files) { _pendingExternal = null; BeginInvoke(() => OpenExternalFiles(files)); }
            AfterStartupUpdateCheck();
        };
        DpiChanged += (_, _) => BeginInvoke(new Action(ApplySidePanelWidth));

        HandleCreated += (_, _) => NativeMethods.UseDarkTitleBar(this);

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
        _activityExplorer.Active = _settings.SidePanelVisible;

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
            case Keys.Shift | Keys.S: ToggleSkipSilence(); return true;
            case Keys.Control | Keys.Z: UndoLast(); return true;
            case Keys.Control | Keys.Y: RedoLast(); return true;
            case Keys.C: ChangeRate(+1); return true;
            case Keys.X: ChangeRate(-1); return true;
            case Keys.Z: SetRate(1f); return true;

            case Keys.Control | Keys.V when tree: PasteUrls(); return true;
            case Keys.F2 when tree: RenameSelected(); return true;
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
            "  → / ←  ←  تقديم / رجوع 5 ثواني\n" +
            "  Ctrl + → / ←  ←  30 ثانية\n" +
            "  ↑ / ↓  ←  الصوت (أو عجلة الماوس على الفيديو)\n" +
            "  M  ←  كتم الصوت (والصوت لحد 200%)\n" +
            "  B / Shift+B  ←  علامة / علامة بملاحظة\n" +
            "  A  ←  تكرار جزء: بداية، نهاية، إلغاء\n" +
            "  S  ←  لقطة شاشة\n" +
            "  T  ←  دايمًا فوق الشبابيك التانية\n" +
            "  P  ←  المشغل الصغير (فوق كل الشبابيك)\n" +
            "  Ctrl + Z / Ctrl + Y  ←  تراجع / إعادة لتعديلات القايمة\n" +
            "  Ctrl + E  ←  تصدير ملاحظات الكورس\n" +
            "  C / X / Z  ←  أسرع / أبطأ / سرعة عادية\n" +
            "  PageDown / PageUp  ←  الفيديو التالي / السابق\n" +
            "  F11 أو F أو دبل كليك  ←  ملء الشاشة (Esc للخروج)\n" +
            "  F6  ←  إظهار / إخفاء القايمة\n" +
            "  O  ←  نسخ الكود/النص اللي على الشاشة (OCR)\n" +
            "  Shift+S  ←  تخطي السكوت\n\n" +
            "المذاكرة والذكاء\n" +
            "  R  ←  علامة مراجعة متباعدة   ·   F7  ←  مراجعة النهارده\n" +
            "  F4  ←  بيانات الكورس (الموعد والوسوم)   ·   F8  ←  الإحصائيات   ·   F9  ←  بومودورو\n" +
            "  Ctrl+T  ←  نص الفيديو   ·   Ctrl+Shift+F  ←  بحث في كلام الفيديوهات\n" +
            "  Ctrl+Q  ←  اختبرني   ·   Ctrl+K  ←  اسأل الكورس   ·   Ctrl+L  ←  معمل الكود\n" +
            "  Ctrl+,  ←  الإعدادات\n\n" +
            "القايمة (لما تكون واقف عليها)\n" +
            "  Enter  ←  تشغيل الفيديو / المحاضرة / الكورس\n" +
            "  F2  ←  إعادة تسمية\n" +
            "  W  ←  تعليم كمُشاهَد / غير مُشاهَد\n" +
            "  Alt + ↑ / ↓  ←  تحريك لفوق / لتحت\n" +
            "  Delete  ←  حذف\n" +
            "  Ctrl + F  ←  بحث\n" +
            "  Ctrl + U  ←  إضافة رابط يوتيوب / قائمة تشغيل\n" +
            "  Ctrl + V  ←  لصق رابط من الحافظة\n" +
            "  السحب والإفلات  ←  نقل فيديو بين المحاضرات، أو محاضرة بين الكورسات،\n" +
            "                         أو ترتيب الكورسات، أو سحب ملفات/فولدرات من الويندوز",
            "اختصارات الكيبورد  ·  الدليل الكامل: F1");
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

            SuspendLayout();
            _menuHost.Visible = _statusHost.Visible = _activityHost.Visible = false;
            _split.Panel2Collapsed = true;
            SetChromeless(true);
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
            ResumeLayout(true);
            _lastMouseActivity = DateTime.Now;
            ShowStatus("Esc للخروج من ملء الشاشة");
        }
        else
        {
            _fullscreen = false;
            SuspendLayout();
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = _prevBounds;
            WindowState = _prevWindowState;
            _menuHost.Visible = _statusHost.Visible = _activityHost.Visible = true;
            _split.Panel2Collapsed = !_settings.SidePanelVisible;
            SetChromeless(false);
            ControlChromeVisible = true;
            ResumeLayout(true);
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
        bool show = nearBottom || recentlyMoved || !_mp.IsPlaying;
        if (ControlChromeVisible != show) ControlChromeVisible = show;
    }

    private void ToggleSidePanel()
    {
        if (_fullscreen) return;
        _settings.SidePanelVisible = _split.Panel2Collapsed;
        _split.Panel2Collapsed = !_settings.SidePanelVisible;
        if (_settings.SidePanelVisible) ApplySidePanelWidth();
        _activityExplorer.Active = _settings.SidePanelVisible;
    }

    // =====================================================================
    //  رسالة مؤقتة في شريط الحالة
    // =====================================================================

    private void ShowStatus(string message, double seconds = 3)
    {
        _statusMessage.Text = message;
        _statusMessageUntil = DateTime.Now.AddSeconds(seconds);
    }

    private void UpdateTitle()
    {
        if (_currentLive != null) { Text = $"{_currentLive.Name}  —  مباشر  —  {AppTitle}"; return; }
        Text = _currentVideo != null
            ? $"{_currentVideo.Title}  —  {_currentGroup?.Name}  —  {AppTitle}"
            : $"{_library.Name}  —  {AppTitle}";
    }

    private void UpdateNowPlaying()
    {
        if (_currentLive != null) { _nowPlayingLabel.Text = LiveLabel(); return; }
        if (_currentVideo == null || _currentGroup == null) { _nowPlayingLabel.Text = ""; return; }
        var course = _library.FindCourseOf(_currentGroup);
        _nowPlayingLabel.Text = $"{course?.Name}   ›   {_currentGroup.Name}   ›   {_currentVideo.Title}";
    }

    // =====================================================================
    //  القفل
    // =====================================================================

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_shutdownDone) { base.OnFormClosing(e); return; }
        if (!_closing && FocusActive && e.CloseReason == CloseReason.UserClosing && !ConfirmLeaveFocus()) { e.Cancel = true; return; }
        if (!_closing && _focusBlocked) EndFocus(early: true);

        // VLC لازم يقف في Thread تاني عشان الواجهة متعلقش
        e.Cancel = true;
        if (_closing) return;
        _closing = true;

        SaveCurrentPosition();
        CommitNotes();
        FlushSession();
        SaveLibrary();
        SaveSettings();
        _uiTimer.Stop();
        Log.Info("app closed");
        Hide();

        try { await Task.Run(() => _mp.Stop()).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch { /* لو VLC علّق مش هنستنى أكتر من كده */ }

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
