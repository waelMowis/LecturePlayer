using System.Globalization;
using System.Runtime.InteropServices;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// تسجيل الشرح: الشاشة (كلها/شاشة/جزء/شباك/«مسار») + المايك + صوت الجهاز + الكاميرا العايمة (زي كامتازيا)،
/// مع أقلام على الشاشة، لمعة الماوس، إيقاف مؤقت، اختصارات من أي مكان، وعلامات تلقائية (صفحة/شريحة/مشهد).
/// بعد الإيقاف: قص البداية والنهاية وأجزاء من النص، وتغيير مكان الكاميرا (لو متسجلة لوحدها)، والحفظ كدرس.
/// </summary>
internal partial class MainForm
{
    internal sealed record RecChoice(string Area, int Monitor, Rectangle? Region, IntPtr Window, string? Mic, bool SystemAudio, string? Camera,
        RecordingEdit.Scene Scene, CameraWindow.Shape Shape, bool CamFile, bool CursorFx, bool Countdown, string Title, LectureGroup? Target);

    private sealed class RecSession
    {
        public required ScreenRecorder Rec;
        public required RecChoice Choice;
        public required string Dir;
        public required Rectangle Area;
        public AudioRecorder? Mic, Sys;
        public CameraFeed? Cam;
        public CameraWindow? CamWin;
        public CursorFx? Fx;
        public required RecordBar Bar;
        public System.Windows.Forms.Timer? Tick;
        public RecordingEdit.Scene Scene;
        public string? Encoder;
    }

    private RecSession? _rec;
    private ScreenInk? _screenInk;
    internal RecChoice? SmokeRecChoice;
    internal string? SmokeCameraInput, SmokeScreenInput;
    internal bool SmokeKeepRecording;
    internal VideoItem? LastRecording { get; private set; }
    internal string? LastRecordingScreen, LastRecordingCamera;
    private bool Recording => _rec != null;

    private string RecordingsDir => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_libraryPath)) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "تسجيلاتي");

    // ---------------- اختصارات من أي مكان ----------------
    private HotkeyWindow? _hotkeys;
    internal List<string> HotkeyConflicts { get; private set; } = new();
    private static string KeyText(Keys k) => (k.HasFlag(Keys.Control) ? "Ctrl+" : "") + (k.HasFlag(Keys.Shift) ? "Shift+" : "") + (k.HasFlag(Keys.Alt) ? "Alt+" : "") + (k & Keys.KeyCode);
    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        private readonly Dictionary<int, Action> _map = new();
        public HotkeyWindow() { CreateHandle(new CreateParams()); }
        public bool Add(int id, Keys key, Action a)
        {
            uint mods = 0x4000; // NOREPEAT
            if (key.HasFlag(Keys.Control)) mods |= 2;
            if (key.HasFlag(Keys.Shift)) mods |= 4;
            if (key.HasFlag(Keys.Alt)) mods |= 1;
            if (!RegisterHotKey(Handle, id, mods, (uint)(key & Keys.KeyCode))) return false;
            _map[id] = a; return true;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && _map.TryGetValue(m.WParam.ToInt32(), out var a)) { try { a(); } catch (Exception ex) { Log.Error("hotkey", ex); } }
            base.WndProc(ref m);
        }
        public void Dispose() { foreach (var id in _map.Keys) UnregisterHotKey(Handle, id); _map.Clear(); DestroyHandle(); }
    }

    /// <summary>بيتسجلوا أول ما البرنامج يفتح: تسجيل (Ctrl+Shift+F9) وأقلام الشاشة (Ctrl+Shift+F8) من أي برنامج.</summary>
    private void RegisterGlobalHotkeys()
    {
        if (_hotkeys != null) return;
        _hotkeys = new HotkeyWindow();
        var failed = new List<string>();
        void Reg(int id, Keys k, string name, Action a) { if (!_hotkeys.Add(id, k, a)) failed.Add($"{name} ({KeyText(k)})"); }
        Reg(901, Keys.Control | Keys.Shift | Keys.F9, "بداية/نهاية التسجيل", () => { if (Recording) _ = StopRecordingAsync(save: true); else StartRecordingDialog(SelectedGroup()); });
        Reg(902, Keys.Control | Keys.Shift | Keys.F8, "أقلام على الشاشة", ToggleScreenInk);
        Reg(903, Keys.Control | Keys.Shift | Keys.F10, "إيقاف مؤقت للتسجيل", () => { if (Recording) _ = TogglePauseAsync(); });
        Reg(904, Keys.Control | Keys.Shift | Keys.F7, "الكاميرا", () => { if (_rec?.CamWin is { } w) w.Visible = !w.Visible; });
        HotkeyConflicts = failed;
        if (failed.Count > 0)
        {
            // اختصار محجوز لبرنامج تاني: نقول بدل ما يبقى مش شغال وخلاص
            Log.Info("global hotkeys taken by another app: " + string.Join(", ", failed));
            ShowBanner("الاختصارات دي محجوزة لبرنامج تاني فمش هتشتغل من برّه «مسار»: " + string.Join("، ", failed) +
                " — الأوامر نفسها موجودة في «مذاكرة» وفي زرار ⏺ في الشريط الجانبي.", BannerKind.Warning, seconds: 20, key: "hotkeys");
        }
    }

    // ---------------- زراير الشريط الجانبي ----------------
    private void OnRecordActivity()
    {
        if (Recording) { _ = StopRecordingAsync(save: true); return; }
        StartRecordingDialog(SelectedGroup());
    }

    /// <summary>زرار التسجيل في الشريط الجانبي: أحمر وبيقول «إنهاء» وانت بتسجل.</summary>
    private void UpdateRecordActivity()
    {
        if (_activityRecord == null) return;
        bool on = Recording;
        _activityRecord.Caption = on ? "إنهاء" : "تسجيل";
        _activityRecord.Glyph = on ? Glyphs.Stop : Glyphs.Record;
        _activityRecord.ForeColor = on ? Color.FromArgb(240, 70, 70) : Theme.Muted;
        _activityRecord.Active = on;
        _toolTip.SetToolTip(_activityRecord, on ? "إنهاء التسجيل وحفظه (Ctrl+Shift+F9)" : "سجّل شرح: الشاشة والكاميرا والصوت وأقلام على الشاشة (Ctrl+Shift+F9 من أي برنامج)");
        _activityRecord.Invalidate();
    }

    private void ShowBoardActivityMenu(Control anchor)
    {
        if (!EnsureLibrary()) return;
        var m = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        void Add(string text, Action a) { var i = new ToolStripMenuItem(text) { ForeColor = Theme.Text }; i.Click += (_, _) => a(); m.Items.Add(i); }
        var g = SelectedGroup();
        Add("➕  سبورة جديدة…", () => NewBoardDialog());
        if (g != null) Add($"➕  سبورة للمحاضرة «{(g.Name.Length > 30 ? g.Name[..30] + "…" : g.Name)}»…", () => OpenLectureBoard(g));
        var recent = _library.Books.Where(b => b.IsBoard).OrderByDescending(b => b.LastOpenedUtc ?? b.AddedUtc).Take(8).ToList();
        if (recent.Count > 0)
        {
            m.Items.Add(new ToolStripSeparator());
            foreach (var b in recent) { var bb = b; Add("🧑‍🏫  " + (b.Title.Length > 40 ? b.Title[..40] + "…" : b.Title), () => OpenBook(bb)); }
        }
        m.Items.Add(new ToolStripSeparator());
        Add("📂  كل السبورات (رف «سبوراتي»)", ShowBoardsShelf);
        Add("🖊  أقلام على الشاشة (Ctrl+Shift+F8)", ToggleScreenInk);
        var at = anchor.PointToScreen(Point.Empty);
        m.Show(new Point(at.X - m.PreferredSize.Width, at.Y));   // جنب الزرار ناحية الشاشة (الشريط على اليمين)
        LastBoardMenu = m;
    }
    internal ContextMenuStrip? LastBoardMenu;

    private void ShowBoardsShelf()
    {
        var shelf = BoardsShelf();
        RebuildTree();
        if (_split.Panel2Collapsed) ToggleSidePanel();
        ShowShelf(true);
        if (ShelfNodeOf(shelf) is { } n) { n.Expand(); SelectShelfItem(shelf); }
    }

    // ---------------- أقلام الشاشة ----------------
    internal void ToggleScreenInk()
    {
        if (_screenInk is { IsDisposed: false }) { _screenInk.Close(); _screenInk = null; return; }
        var screen = _rec != null ? Screen.FromRectangle(_rec.Area) : Screen.FromControl(this);
        _screenInk = new ScreenInk(screen);
        _screenInk.FormClosed += (_, _) => _screenInk = null;
        _screenInk.Show();
        ShowStatus("🖊 أقلام على الشاشة — 🖱 يرجّعك تشتغل على البرامج والرسم ظاهر، ✕ تقفلها (Ctrl+Shift+F8)", 6);
    }

    // ---------------- البداية ----------------
    internal async void StartRecordingDialog(LectureGroup? target)
    {
        if (Recording) return;
        if (!EnsureLibrary()) return;
        var c = SmokeRecChoice;
        if (c == null)
        {
            if (!await EnsureFfmpegAsync("تسجيل الشاشة")) return;
            var cams = await ScreenRecorder.CamerasAsync(Tools.FindFfmpeg()!);
            c = RecordSetupDialog(cams, AudioRecorder.Microphones(), target);
            if (c == null) return;
        }
        await StartRecordingAsync(c);
    }

    internal RecChoice? RecordSetupDialog(List<string> cams, List<AudioRecorder.Device> mics, LectureGroup? target)
    {
        using var kit = new FormKit("سجّل شرح", 640);
        var area = kit.Row("أسجّل إيه", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++) area.Items.Add(screens.Length == 1 ? "الشاشة كلها" : $"الشاشة {i + 1}" + (screens[i].Primary ? " (الأساسية)" : "") + $" — {screens[i].Bounds.Width}×{screens[i].Bounds.Height}");
        area.Items.Add("جزء أحدده بالماوس…");
        area.Items.Add("شباك «مسار» بس");
        var wins = TopWindows();
        foreach (var (h, t) in wins) area.Items.Add("شباك: " + (t.Length > 60 ? t[..60] + "…" : t));
        area.SelectedIndex = Array.IndexOf(screens, Screen.FromControl(this));
        var mic = kit.Row("المايك", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
        mic.Items.Add("من غير مايك");
        foreach (var m in mics) mic.Items.Add(m.Name);
        string? def = AudioRecorder.DefaultMicrophone();
        mic.SelectedIndex = mics.Count == 0 ? 0 : 1 + Math.Max(0, mics.FindIndex(m => m.Id == def));
        var sys = kit.Row("صوت الجهاز", new ToggleSwitch { Text = "سجّل الصوت اللي طالع من الجهاز (فيديو/برنامج)", Checked = false });
        var cam = kit.Row("الكاميرا", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
        cam.Items.Add("من غير كاميرا");
        foreach (var n in cams) cam.Items.Add(n);
        cam.SelectedIndex = 0;
        var scene = kit.Row("المشهد", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
        scene.Items.AddRange(new object[] { "الشاشة + الكاميرا صغيرة (زي ما هي على الشاشة)", "الكاميرا كبيرة + الشاشة صغيرة", "الكاميرا بس", "الشاشة بس (الكاميرا في ملف لوحده)" });
        scene.SelectedIndex = 0;
        var shape = kit.Row("شكل الكاميرا", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
        shape.Items.AddRange(new object[] { "دايرة", "مستطيل مدوّر", "مستطيل" });
        shape.SelectedIndex = 0;
        var camFile = kit.Row("ملف الكاميرا", new ToggleSwitch { Text = "احفظ الكاميرا في ملف لوحده (تقدر تغير مكانها والمشهد بعد التسجيل)", Checked = true });
        var fx = kit.Row("الماوس", new ToggleSwitch { Text = "لمعة حوالين الماوس ودايرة عند الكليك", Checked = true });
        var cd = kit.Row("عد تنازلي", new ToggleSwitch { Text = "3، 2، 1 قبل ما يبدأ", Checked = true });
        var title = kit.Row("الاسم", FormKit.Style(new TextBox { Text = target != null ? "شرح — " + target.Name : "شرح " + DateTime.Now.ToString("d MMMM HH:mm"), RightToLeft = RightToLeft.Yes }));
        kit.Note("وانت بتسجل: Ctrl+Shift+F9 إنهاء، Ctrl+Shift+F10 إيقاف مؤقت، Ctrl+Shift+F8 أقلام على الشاشة، Ctrl+Shift+F7 الكاميرا — شريط التحكم مابيظهرش في الفيديو.", 44, Theme.Muted);
        kit.Buttons("ابدأ التسجيل");
        if (SmokeShowDialog(kit, "record-setup")) return null;
        if (kit.Show(this) != DialogResult.OK) return null;
        int ai = area.SelectedIndex;
        string kind = ai < screens.Length ? "screen" : ai == screens.Length ? "region" : ai == screens.Length + 1 ? "masar" : "window";
        Rectangle? region = null;
        if (kind == "region")
        {
            using var rp = new RegionPicker();
            if (rp.ShowDialog(this) != DialogResult.OK || rp.Result == null) return null;
            region = rp.Result;
        }
        return new RecChoice(kind, Math.Min(ai, screens.Length - 1), region, kind == "window" ? wins[ai - screens.Length - 2].Handle : IntPtr.Zero,
            mic.SelectedIndex > 0 ? mics[mic.SelectedIndex - 1].Id : null, sys.Checked, cam.SelectedIndex > 0 ? cams[cam.SelectedIndex - 1] : null,
            (RecordingEdit.Scene)scene.SelectedIndex, (CameraWindow.Shape)shape.SelectedIndex, camFile.Checked, fx.Checked, cd.Checked, title.Text.Trim(), target);
    }

    internal async Task<bool> StartRecordingAsync(RecChoice c)
    {
        if (Recording) return false;
        if (Tools.FindFfmpeg() == null && !await EnsureFfmpegAsync("تسجيل الشاشة")) return false;
        string ff = Tools.FindFfmpeg()!;
        var screens = Screen.AllScreens;
        Rectangle area = c.Area switch
        {
            "region" when c.Region is { } r => r,
            "masar" => Bounds,
            "window" => WindowRect(c.Window),
            _ => screens[Math.Clamp(c.Monitor, 0, screens.Length - 1)].Bounds
        };
        area.Intersect(SystemInformation.VirtualScreen);
        if (area.Width < 64 || area.Height < 64) { ShowStatus("المساحة صغيرة قوي", 4); return false; }
        var scr = Screen.FromRectangle(area);
        int mon = Array.IndexOf(screens, scr);
        string dir = Path.Combine(RecordingsDir, ".work", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        // أكتر من شاشة: ترتيب ddagrab ممكن يختلف عن ويندوز — نستخدم gdigrab (أكيد) في الحالة دي
        string? force = SmokeScreenInput ?? (screens.Length > 1 ? $"-f gdigrab -framerate 30 -draw_mouse 1 -offset_x {area.X} -offset_y {area.Y} -video_size {area.Width & ~1}x{area.Height & ~1} -i desktop" : null);
        var rec = new ScreenRecorder(ff, new ScreenRecorder.Options(new ScreenRecorder.Area(area.X, area.Y, area.Width, area.Height, Math.Max(0, mon), scr.Bounds.X, scr.Bounds.Y), 30, dir, ForceInput: force));
        var bar = new RecordBar(c.Camera != null);
        var s = new RecSession { Rec = rec, Choice = c, Dir = dir, Area = area, Bar = bar, Scene = c.Scene };
        try
        {
            s.Encoder = await ScreenRecorder.PickEncoderAsync(ff);
            // الكاميرا (والمعاينة بتبدأ قبل العد عشان تظبط مكانك)
            if (c.Camera != null)
            {
                s.CamWin = new CameraWindow { CamShape = c.Shape };
                s.CamWin.Show();
                s.CamWin.PlaceDefault(scr, right: true);
                s.CamWin.Render();
                if (c.Scene != RecordingEdit.Scene.ScreenWithCam) LayeredWindow.ExcludeFromCapture(s.CamWin); // المشهد هيتركّب بعدين
                s.CamWin.HideRequested += () => s.CamWin.Visible = false;
                s.Cam = new CameraFeed(ff) { TestInput = SmokeCameraInput };
                s.Cam.Frame += f => s.CamWin?.SetFrame(f);
                s.Cam.Failed += m => BeginInvoke(() => ShowStatus("📷 الكاميرا: " + m, 6));
                s.Cam.Start(c.Camera, s.CamWin.Mirror, null, "");
            }
            if (c.Mic != null) { s.Mic = new AudioRecorder(Path.Combine(dir, "mic.wav"), c.Mic == "default" ? null : c.Mic, loopback: false); if (!s.Mic.Open()) { ShowStatus("🎙 المايك مااشتغلش: " + s.Mic.Error, 6); s.Mic = null; } }
            if (c.SystemAudio) { s.Sys = new AudioRecorder(Path.Combine(dir, "system.wav"), null, loopback: true); if (!s.Sys.Open()) { ShowStatus("🔊 صوت الجهاز مااشتغلش: " + s.Sys.Error, 6); s.Sys = null; } }
            Directory.CreateDirectory(dir);
            if (c.Countdown) await new Countdown(area).RunAsync(3);
            rec.SegmentStarted += () => { s.Mic?.Begin(); s.Sys?.Begin(); };
            rec.Failed += m => BeginInvoke(() => { ShowBanner("⏺ " + m, BannerKind.Warning, seconds: 0); _ = StopRecordingAsync(save: true); });
            // ملف الكاميرا بيبدأ مع الصورة
            if (s.Cam != null && (c.CamFile || c.Scene != RecordingEdit.Scene.ScreenWithCam))
                s.Cam.Start(c.Camera!, s.CamWin!.Mirror, Path.Combine(dir, $"cam{s.Cam.Files.Count + 1:00}.mp4"), RecordingEdit.EncoderArgs(s.Encoder));
            await rec.StartAsync();
            _rec = s;
            if (c.CursorFx) { s.Fx = new CursorFx(); s.Fx.Show(); }
            bar.Show();
            LayeredWindow.ExcludeFromCapture(bar);
            bar.Location = new Point(scr.WorkingArea.Left + (scr.WorkingArea.Width - bar.Width) / 2, scr.WorkingArea.Top + 6);
            bar.Command += cmd => OnRecordBar(cmd);
            s.Tick = new System.Windows.Forms.Timer { Interval = 250 };
            s.Tick.Tick += (_, _) => bar.SetTime(rec.Elapsed, rec.Paused);
            s.Tick.Start();
            RegisterGlobalHotkeys();
            Log.Info($"recording: {area} via {rec.Source}/{rec.Encoder}, mic={(s.Mic != null)}, system={(s.Sys != null)}, cam={c.Camera ?? "-"} scene={c.Scene}");
            RecordingMark("البداية");
            UpdateRecordActivity();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("recording start", ex);
            CleanupRecording(s, delete: true);
            ShowBanner("مقدرتش أبدأ التسجيل: " + ex.Message, BannerKind.Warning, seconds: 0);
            return false;
        }
    }

    private void OnRecordBar(string cmd)
    {
        var s = _rec; if (s == null) return;
        switch (cmd)
        {
            case "pause": _ = TogglePauseAsync(); break;
            case "stop": _ = StopRecordingAsync(save: true); break;
            case "cancel": if (Confirm("تلغي التسجيل وتمسحه؟")) _ = StopRecordingAsync(save: false); break;
            case "mark": RecordingMark("علامة " + (s.Rec.Marks.Count + 1)); ShowStatus("🔖 اتعلّمت", 2); break;
            case "ink": ToggleScreenInk(); break;
            case "cam": if (s.CamWin != null) s.CamWin.Visible = !s.CamWin.Visible; break;
            case "scene":
                var m = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
                string[] names = { "الشاشة + الكاميرا صغيرة", "الكاميرا كبيرة + الشاشة صغيرة", "الكاميرا بس", "الشاشة بس" };
                for (int i = 0; i < names.Length; i++)
                {
                    int k = i;
                    var it = new ToolStripMenuItem(names[i]) { Checked = (int)s.Scene == i, Enabled = s.Cam?.Files.Count > 0 || i == 0 || i == 3 };
                    it.Click += (_, _) => { s.Scene = (RecordingEdit.Scene)k; RecordingMark("مشهد: " + names[k]); };
                    m.Items.Add(it);
                }
                m.Show(Cursor.Position);
                break;
        }
    }

    private async Task TogglePauseAsync()
    {
        var s = _rec; if (s == null) return;
        if (!s.Rec.Paused)
        {
            await s.Rec.PauseAsync();
            s.Mic?.Pause(); s.Sys?.Pause();
            if (s.Cam is { } cam && cam.Files.Count > 0) cam.Start(s.Choice.Camera!, s.CamWin!.Mirror, null, ""); // معاينة بس وقت الإيقاف
            ShowStatus("⏸ متوقف — Ctrl+Shift+F10 يكمّل", 4);
        }
        else
        {
            if (s.Cam is { } cam && cam.Files.Count > 0) cam.Start(s.Choice.Camera!, s.CamWin!.Mirror, Path.Combine(s.Dir, $"cam{cam.Files.Count + 1:00}.mp4"), RecordingEdit.EncoderArgs(s.Encoder!));
            await s.Rec.ResumeAsync();
        }
    }

    /// <summary>علامة في التسجيل (بتتنادى من القارئ والعروض والسبورة كمان).</summary>
    internal void RecordingMark(string note) { if (_rec is { Rec.Paused: false } s && s.Rec.Running) s.Rec.AddMark(note); }

    // ---------------- النهاية ----------------
    internal async Task<VideoItem?> StopRecordingAsync(bool save, bool quick = false)
    {
        var s = _rec; if (s == null) return null;
        _rec = null;
        UpdateRecordActivity();
        s.Tick?.Stop();
        s.Bar.Hide();
        string? screen = null, camFile = null;
        try
        {
            screen = await s.Rec.StopAsync();
            s.Mic?.Stop(); s.Sys?.Stop();
            s.Cam?.Stop();
            if (s.Cam != null)
            {
                var lens = new List<double>();
                foreach (var seg in s.Rec.Segments) lens.Add(File.Exists(seg) ? await ScreenRecorder.DurationAsync(Tools.FindFfmpeg()!, seg) : 0);
                camFile = await s.Cam.JoinAsync(s.Dir, s.Rec.SegmentStarts, RecordingEdit.EncoderArgs(s.Encoder!), lens);
                Log.Info("camera sync offsets (s): " + string.Join(", ", s.Cam.Offsets.Select(o => o.ToString("0.000", CultureInfo.InvariantCulture))));
            }
            LastRecordingScreen = screen; LastRecordingCamera = camFile;
        }
        catch (Exception ex)
        {
            Log.Error("recording stop", ex);
            ShowBanner("التسجيل ماتحفظش: " + ex.Message, BannerKind.Warning, seconds: 0);
            CleanupRecording(s, delete: false);
            return null;
        }
        var marks = s.Rec.Marks.ToList();
        string log = s.Rec.LastLog;
        CleanupRecording(s, delete: false);
        if (!save) { try { Directory.Delete(s.Dir, true); } catch { } ShowStatus("اتلغى التسجيل", 4); return null; }
        string ff = Tools.FindFfmpeg()!;
        var audio = new List<(string, double)>();
        if (s.Mic is { HasData: true }) audio.Add((s.Mic.File, 0));
        if (s.Sys is { HasData: true }) audio.Add((s.Sys.File, 0));
        try
        {
            ShowStatus("⏺ بجهّز الفيديو…", 60);
            string composed = Path.Combine(s.Dir, "final.mp4");
            bool camInScreen = s.Choice.Scene == RecordingEdit.Scene.ScreenWithCam;   // الفقاعة كانت ظاهرة في الشاشة ولا مستخبية
            await RecordingEdit.ComposeAsync(ff, screen!, camFile, s.Scene, RecordingEdit.Corner.BottomRight, s.Scene == RecordingEdit.Scene.ScreenWithCam ? 0.28 : 0.32, audio, RecordingEdit.EncoderArgs(s.Encoder!), composed, camInScreen);
            double dur = await ScreenRecorder.DurationAsync(ff, composed);
            // تعديل بعد التسجيل: قص ومكان الكاميرا والمشهد
            var edit = SmokeRecordEdit ?? (quick ? new RecordEdit(s.Choice.Title, 0, 0, new(), s.Scene, RecordingEdit.Corner.BottomRight) : PostRecordDialog(s.Choice.Title, dur, camFile != null, s.Scene));
            if (edit == null) { ShowStatus("التسجيل لسه في «تسجيلاتي/.work» لو احتجته", 6); return null; }
            if (camFile != null && (edit.Scene != s.Scene || edit.Corner != RecordingEdit.Corner.BottomRight))
            {
                string re = Path.Combine(s.Dir, "final2.mp4");
                await RecordingEdit.ComposeAsync(ff, screen!, camFile, edit.Scene, edit.Corner, edit.Scene == RecordingEdit.Scene.ScreenWithCam ? 0.28 : 0.32, audio, RecordingEdit.EncoderArgs(s.Encoder!), re, camInScreen);
                composed = re;
            }
            var keep = RecordingEdit.Keep(dur, edit.Start, edit.End, edit.Cuts);
            if (keep.Count == 0) keep = new() { (0, dur) };   // القص شال كل حاجة: نسيبه زي ما هو
            if (keep.Count != 1 || keep[0].From > 0.05 || keep[0].To < dur - 0.05)
            {
                string tr = Path.Combine(s.Dir, "trimmed.mp4");
                await RecordingEdit.TrimAsync(ff, composed, keep, audio.Count > 0, RecordingEdit.EncoderArgs(s.Encoder!), tr);
                composed = tr;
                marks = RecordingEdit.ShiftMarks(marks, keep);
            }
            Directory.CreateDirectory(RecordingsDir);
            string safe = new string((edit.Title.Length > 0 ? edit.Title : s.Choice.Title).Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch).ToArray()).Trim();
            if (safe.Length > 80) safe = safe[..80];
            string final = Path.Combine(RecordingsDir, (safe.Length > 0 ? safe : "شرح") + ".mp4");
            for (int i = 2; File.Exists(final); i++) final = Path.Combine(RecordingsDir, $"{safe} ({i}).mp4");
            File.Move(composed, final);
            if (camFile != null && s.Choice.CamFile) try { File.Copy(camFile, Path.ChangeExtension(final, null) + " — كاميرا.mp4", true); } catch { }
            var v = AddRecordingToLibrary(final, edit.Title.Length > 0 ? edit.Title : s.Choice.Title, s.Choice.Target, marks);
            if (!SmokeKeepRecording) try { Directory.Delete(s.Dir, true); } catch { }
            return v;
        }
        catch (Exception ex)
        {
            Log.Error("recording finish", ex);
            Log.Info("ffmpeg log: " + log);
            ShowBanner("مقدرتش أجهّز الفيديو: " + ex.Message + " — الملفات الأصلية في «تسجيلاتي/.work»", BannerKind.Warning, "افتح", () => OpenFolder(s.Dir), seconds: 0);
            return null;
        }
    }

    private void CleanupRecording(RecSession s, bool delete)
    {
        try { s.Rec.Dispose(); } catch { }
        try { s.Mic?.Dispose(); } catch { }
        try { s.Sys?.Dispose(); } catch { }
        try { s.Cam?.Dispose(); } catch { }
        try { s.CamWin?.Close(); } catch { }
        try { s.Fx?.Close(); } catch { }
        try { s.Bar.Close(); } catch { }
        s.Tick?.Dispose();
        if (delete) try { Directory.Delete(s.Dir, true); } catch { }
    }

    /// <summary>الفيديو بيتضاف للمحاضرة المختارة، أو لكورس «تسجيلاتي» في محاضرة باسم اليوم — بعلامات.</summary>
    private VideoItem AddRecordingToLibrary(string file, string title, LectureGroup? target, List<ScreenRecorder.Mark> marks)
    {
        Checkpoint("تسجيل شرح");
        var g = target;
        if (g == null || !_library.AllGroups.Contains(g))
        {
            var course = _library.Courses.FirstOrDefault(c => c.Name == "تسجيلاتي");
            if (course == null) { course = new Course { Name = "تسجيلاتي" }; _library.Courses.Add(course); }
            string day = DateTime.Now.ToString("yyyy-MM-dd");
            g = course.Groups.FirstOrDefault(x => x.Name == day);
            if (g == null) { g = new LectureGroup { Name = day }; course.Groups.Add(g); }
            course.Expanded = true;
        }
        var v = VideoItem.FromFile(file);
        v.Title = title;
        foreach (var m in marks) v.Bookmarks.Add(new Bookmark { TimeMs = m.Ms, Note = m.Note });
        g.Videos.Add(v);
        g.Expanded = true;
        MarkDirty();
        RebuildTree();
        QueueProbe(new[] { v });
        LastRecording = v;
        var gg = g;
        ShowBanner($"⏺ اتحفظ «{title}» ({v.Bookmarks.Count} علامة)", BannerKind.Success, "شغّله", () => PlayVideo(gg, v), seconds: 15);
        return v;
    }

    // ---------------- التعديل بعد التسجيل ----------------
    internal sealed record RecordEdit(string Title, double Start, double End, List<(double From, double To)> Cuts, RecordingEdit.Scene Scene, RecordingEdit.Corner Corner);
    internal RecordEdit? SmokeRecordEdit;

    private RecordEdit? PostRecordDialog(string title, double duration, bool camera, RecordingEdit.Scene scene)
    {
        using var kit = new FormKit("التسجيل خلص — " + FormatSeconds(duration), 600);
        var name = kit.Row("الاسم", FormKit.Style(new TextBox { Text = title, RightToLeft = RightToLeft.Yes }));
        var start = kit.Row("ابدأ من", FormKit.Style(new TextBox { Text = "0:00", RightToLeft = RightToLeft.No }));
        var end = kit.Row("لحد", FormKit.Style(new TextBox { Text = FormatSeconds(duration), RightToLeft = RightToLeft.No }));
        var cuts = kit.Row("أجزاء تتشال", FormKit.Style(new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, RightToLeft = RightToLeft.No, PlaceholderText = "كل سطر: 1:20-1:45" }), 70);
        ComboBox? sc = null, corner = null;
        if (camera)
        {
            sc = kit.Row("المشهد", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
            sc.Items.AddRange(new object[] { "الشاشة + الكاميرا صغيرة", "الكاميرا كبيرة + الشاشة صغيرة", "الكاميرا بس", "الشاشة بس" });
            sc.SelectedIndex = (int)scene;
            corner = kit.Row("مكان الصغير", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
            corner.Items.AddRange(new object[] { "تحت يمين", "تحت شمال", "فوق يمين", "فوق شمال" });
            corner.SelectedIndex = 0;
        }
        kit.Note("القص بيتعمل في نسخة جديدة — والعلامات بتتظبط لوحدها.", 26, Theme.Muted);
        kit.Buttons("احفظ", "سيبه في الفولدر");
        if (kit.Show(this) != DialogResult.OK) return null;
        var list = new List<(double, double)>();
        foreach (var line in cuts.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('-', 2);
            if (parts.Length == 2 && ParseClock(parts[0]) is { } a && ParseClock(parts[1]) is { } b && b > a) list.Add((a, b));
        }
        double st = Math.Clamp(ParseClock(start.Text) ?? 0, 0, duration), en = ParseClock(end.Text) ?? duration;
        if (en >= duration - 0.5 || en <= st) en = 0;   // 0 = لحد الآخر
        return new RecordEdit(name.Text.Trim(), st, en, list,
            sc != null ? (RecordingEdit.Scene)sc.SelectedIndex : scene, corner != null ? (RecordingEdit.Corner)corner.SelectedIndex : RecordingEdit.Corner.BottomRight);
    }

    internal static double? ParseClock(string s)
    {
        s = s.Trim();
        var p = s.Split(':');
        try
        {
            double v = 0;
            foreach (var x in p) v = v * 60 + double.Parse(x, CultureInfo.InvariantCulture);
            return v;
        }
        catch { return null; }
    }

    private static string FormatSeconds(double s) => TimeSpan.FromSeconds(s) is var t && t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : TimeSpan.FromSeconds(s).ToString(@"m\:ss");

    // ---------------- الشبابيك ----------------
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);

    private List<(IntPtr Handle, string Title)> TopWindows()
    {
        var list = new List<(IntPtr, string)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h) || h == Handle) return true;
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(h, sb, 256);
            string t = sb.ToString().Trim();
            if (t.Length > 0 && t != "Program Manager" && WindowRect(h).Width > 200) list.Add((h, t));
            return true;
        }, IntPtr.Zero);
        return list.Take(25).ToList();
    }

    private static Rectangle WindowRect(IntPtr h)
    {
        if (DwmGetWindowAttribute(h, 9 /*EXTENDED_FRAME_BOUNDS*/, out var r, Marshal.SizeOf<RECT>()) != 0) GetWindowRect(h, out r);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }
}
