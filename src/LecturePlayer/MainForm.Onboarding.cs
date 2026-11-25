using LibVLCSharp.Shared;
using System.Diagnostics;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// مساعدة المستخدم الجديد: الوضع البسيط/المتقدم، والجولة التعريفية.
/// </summary>
internal sealed partial class MainForm
{
    // =====================================================================
    //  الوضع البسيط والمتقدم
    // =====================================================================

    private readonly List<GlyphButton> _advancedActivity = new();
    private ToolStripMenuItem _advancedItem = null!;

    private bool AdvancedMode => _settings.AdvancedMode == true;

    /// <summary>بيعلّم عنصر في المنيو إنه من أدوات الوضع المتقدم (بيستخبى في البسيط).</summary>
    private static T Adv<T>(T item) where T : ToolStripItem
    {
        item.Tag = AdvTag;
        return item;
    }
    private const string AdvTag = "adv";

    /// <summary>أول تشغيل بعد الميزة دي: المستخدم الجديد بيبدأ بسيط، واللي كان بيستخدم البرنامج قبل كده بيفضل متقدم.</summary>
    private void InitAdvancedMode()
    {
        if (_settings.AdvancedMode == null)
        {
            bool existingUser = !string.IsNullOrEmpty(_settings.LastRunVersion);
            _settings.AdvancedMode = existingUser;
            if (existingUser) _settings.TourShown = true; // مش محتاج جولة
            try { Storage.SaveSettings(_settings); } catch { }
        }
        ApplyMode();
    }

    private void SetAdvancedMode(bool on)
    {
        _settings.AdvancedMode = on;
        ApplyMode();
        SaveSettings();
        ShowBanner(on
                ? "الوضع المتقدم: شريط الحالة في الأسفل ومعلومات إضافية — والقوائم كما هي"
                : "الوضع البسيط: شاشة أهدأ (دون الأزرار الإضافية وشريط الحالة) — كل الأوامر ما زالت في القوائم (عرض ← الوضع المتقدم)",
            BannerKind.Info, seconds: 8);
    }

    private void ApplyMode()
    {
        bool adv = AdvancedMode;
        if (_statusHost.Visible != StatusBarWanted) _statusHost.Visible = StatusBarWanted;
        foreach (var b in _advancedActivity) b.Visible = adv;
        if (_advancedItem != null) _advancedItem.Checked = adv;
    }

    private static int CountHidden(ToolStripItemCollection items) =>
        items.OfType<ToolStripItem>().Sum(i => (i.Available ? 0 : 1) + (i is ToolStripDropDownItem dd ? CountHidden(dd.DropDownItems) : 0));

    private static void ApplyMode(ToolStripItem item, bool adv)
    {
        // Available مش Visible: Visible بيرجع false لأي عنصر في منيو مقفولة.
        if (Equals(item.Tag, AdvTag)) item.Available = adv;
        if (item is ToolStripDropDownItem dd)
        {
            foreach (ToolStripItem child in dd.DropDownItems) ApplyMode(child, adv);
            TidySeparators(dd.DropDownItems);
        }
    }

    /// <summary>بعد إخفاء عناصر: مفيش فاصل في الأول ولا في الآخر ولا فاصلين ورا بعض.</summary>
    private static void TidySeparators(ToolStripItemCollection items)
    {
        ToolStripItem? lastShown = null;
        foreach (ToolStripItem i in items)
        {
            if (i is ToolStripSeparator sep)
            {
                sep.Available = lastShown != null && lastShown is not ToolStripSeparator;
                if (sep.Available) lastShown = sep;
                continue;
            }
            if (i.Available) lastShown = i;
        }
        if (lastShown is ToolStripSeparator tail) tail.Available = false;
    }

    // =====================================================================
    //  الجولة التعريفية
    // =====================================================================

    private void StartTourOrGuide() => StartTour();

    /// <summary>الجولة السريعة (3 خطوات). بتظهر لوحدها أول مرة، وموجودة في منيو مساعدة.</summary>
    private void StartTour()
    {
        var choice = TourForm.Run(this);
        if (!_settings.TourShown) { _settings.TourShown = true; SaveSettings(); }
        AskRoleIfNeeded();
        switch (choice)
        {
            case TourChoice.AddFolder: ImportCourseDialog(); break;
            case TourChoice.AddUrl: AddUrlsDialog(null); break;
            case TourChoice.Live: ShowLiveWindow(); break;
            default:
                ShowBanner("إن احتجت إلى أي شيء: F1 لدليل الاستخدام، أو مساعدة ← جولة سريعة", BannerKind.Info, seconds: 10);
                break;
        }
    }

    /// <summary>أول تشغيل للبرنامج خالص: نعرض الجولة (إلا لو اتفتح بملف فيديو، ساعتها منقاطعش).</summary>
    private bool ShouldShowFirstRunTour(bool openedWithFiles) => !_settings.TourShown && !openedWithFiles && !SmokeTest.Active;

    // =====================================================================
    //  فحص التشغيل الآلي (SmokeTest)
    // =====================================================================

    /// <summary>بيجرّب الحاجات اللي بتتبني وقت التشغيل بس (مش وقت الـ compile).</summary>
    internal void RunSmokeChecks()
    {
        bool before = AdvancedMode;
        foreach (bool adv in new[] { false, true, before })
        {
            _settings.AdvancedMode = adv;
            ApplyMode();
            var tops = _menu.Items.Cast<ToolStripItem>().Where(i => i.Available).Select(i => i.Text).ToList();
            Console.WriteLine($"SMOKE menus ({(adv ? "advanced" : "simple")}): {tops.Count}");
            // نفس القوايم ونفس الأوامر في الوضعين (الفرق في عناصر الشاشة بس)
            if (!tops.SequenceEqual(new[] { "ملف", "تحرير", "المكتبة", "التشغيل", "عرض", "الأدوات", "مساعدة" }))
                throw new Exception("top menus: " + string.Join(" | ", tops));
            int hidden = CountHidden(_menu.Items);
            if (hidden > 0) throw new Exception($"{hidden} menu commands hidden in {(adv ? "advanced" : "simple")} mode");
            if (_statusHost.Visible != adv) throw new Exception($"status bar visible={_statusHost.Visible} in {(adv ? "advanced" : "simple")} mode");
            if (!adv)
            {
                ShowStatus("رسالة فحص", 0.3);
                if (_nowPlayingLabel.Text != "رسالة فحص") throw new Exception("toast not shown in simple mode");
                var until = DateTime.UtcNow.AddMilliseconds(900);
                while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(30); }
                ExpireToast();
                if (_nowPlayingLabel.Text == "رسالة فحص") throw new Exception("toast did not expire");
            }
        }
        ShowBanner("فحص", BannerKind.Warning, "زر", () => { }, seconds: 0, key: "smoke");
        HideBanner("smoke");
        foreach (var kind in Enum.GetValues<BannerKind>()) ShowBanner("فحص", kind, seconds: 1);
        HideBanner();
        using (var m = NewStudyMenu()) { m.Show(this, Point.Empty); m.Close(); }
        RefreshHome();
        TourForm.SmokeTest(this);

        RunHierarchyUiChecks();
        RunLibraryFileChecks();
        RunNoLibraryChecks();
        RunFoldChecks();
        RunAppearanceChecks();
        RunSplitLayoutChecks();
        RunUpdateReopenChecks();
        RunExportChecks();
        RunDownloadChecks();
        RunUdemyChecks();
        RunMoodleChecks();
        RunItLegendChecks();
        RunTopicSeparatorCheck();
        RunMenuChecks();
        RunBookChecks();
        RunBookLinkChecks();
        RunBookSmartChecks();
        RunNotionChecks();
        RunInkChecks();
        RunBoardChecks();
        RunPresentationChecks();
        RunTabChecks();
        RunSessionExportChecks();
        RunRecordingChecks();
        RunStressAndPlaybackChecks();

        // تقرير الأعطال: تسجيل، وقراية، وبناء التقرير، وفتح الشاشة بتاعته
        CrashReport.Record("smoke", new InvalidOperationException("smoke test"), fatal: false);
        string? crash = CrashReport.TakeLast();
        if (crash == null || !crash.Contains("smoke test")) throw new Exception("crash marker not written/read");
        if (CrashReport.TakeLast() != null) throw new Exception("crash marker not cleared");
        using (var cf = BuildCrashReportForm(crash)) { cf.Show(this); Application.DoEvents(); cf.Close(); }
    }

    // =====================================================================
    //  تقرير الأعطال
    // =====================================================================

    /// <summary>لو البرنامج وقع المرة اللي فاتت: شريط تنبيه وزرار يعرض التقرير.</summary>
    private void CheckLastCrash()
    {
        if (SmokeTest.Active) return;
        if (CrashReport.TakeLast() is not { } crash) return;
        Log.Info("previous run ended with an error; offering report");
        ShowBanner("حدث عطل في «مسار» في المرة السابقة وسُجّل سببه — بياناتك سليمة", BannerKind.Warning,
            "اعرض التقرير", () => ShowCrashReport(crash), seconds: 30, key: "crash");
    }

    private void ShowCrashReport(string crash)
    {
        using var f = BuildCrashReportForm(crash);
        f.ShowDialog(this);
    }

    private Form BuildCrashReportForm(string crash)
    {
        string report = CrashReport.BuildReport(crash);
        var f = new Form
        {
            Text = "تقرير العطل", StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, ShowIcon = false,
            MinimizeBox = false, MaximizeBox = false, RightToLeft = RightToLeft.Yes, BackColor = Theme.Window, ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10f), AutoScaleDimensions = new SizeF(96F, 96F), AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(720, 480)
        };
        f.HandleCreated += (_, _) => NativeMethods.UseDarkTitleBar(f);
        var note = new Label
        {
            Dock = DockStyle.Top, Height = 54, Padding = new Padding(12, 10, 12, 0), RightToLeft = RightToLeft.Yes,
            Text = "هذا تقرير العطل الذي حدث. انسخه وأرسله إلى المطوّر لإصلاحه. يحتوي التقرير على أسماء ملفات من جهازك (مثل مكان المكتبة)، فراجعه قبل إرساله لأي أحد."
        };
        var box = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            Text = report, BackColor = Theme.Input, ForeColor = Theme.Text, Font = new Font("Consolas", 9f), RightToLeft = RightToLeft.No,
            BorderStyle = BorderStyle.None
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Window };
        var copy = new PillButton("📋  انسخ التقرير", PillStyle.Primary);
        copy.Click += (_, _) => { try { Clipboard.SetText(report); copy.Text = "نُسخ ✓"; copy.AutoSizeToText(); } catch { } };
        var folder = new PillButton("فتح مجلد السجل");
        folder.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(Log.LogDir) { UseShellExecute = true })?.Dispose(); } catch { } };
        var close = new PillButton("إغلاق", PillStyle.Subtle) { DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { copy, folder, close });
        f.CancelButton = close;
        f.Controls.Add(box);
        f.Controls.Add(note);
        f.Controls.Add(buttons);
        return f;
    }

    /// <summary>
    /// فحص الأداء والتشغيل الفعلي: مكتبة كبيرة (آلاف الفيديوهات) بتتفتح وتتبني وتتدوّر فيها في وقت معقول،
    /// وملف صوت حقيقي بيشتغل فعلًا في VLC.
    /// </summary>
    /// <summary>قياس استهلاك البرنامج: المعالج (% من نواة واحدة)، والذاكرة، وسرعة حجز الذاكرة في .NET.</summary>
    /// <returns>صحيان تايمر الواجهة في الثانية (كل صحيان بيمنع المعالج ينزل لحالة توفير الطاقة).</returns>
    private double MeasureResources(string label, int ms, Action<int> pump)
    {
        int ticks0 = _tick;
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        p.Refresh();
        var cpu0 = p.TotalProcessorTime;
        long alloc0 = GC.GetTotalAllocatedBytes();
        int gc0 = GC.CollectionCount(0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        pump(ms);
        sw.Stop();
        p.Refresh();
        double cpu = (p.TotalProcessorTime - cpu0).TotalMilliseconds / sw.Elapsed.TotalMilliseconds * 100;
        double allocMBs = (GC.GetTotalAllocatedBytes() - alloc0) / 1048576.0 / sw.Elapsed.TotalSeconds;
        double wakeups = (_tick - ticks0) / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"SMOKE resources [{label}]: cpu {cpu:F1}% of one core, ui wakeups {wakeups:F1}/s (timer {_uiTimer.Interval} ms), working set {p.WorkingSet64 / 1048576} MB, " +
                          $"private {p.PrivateMemorySize64 / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB, " +
                          $"alloc {allocMBs:F2} MB/s, gen0 GCs {GC.CollectionCount(0) - gc0}");
        return wakeups;
    }

    /// <summary>
    /// توفير البطارية: التايمر بيهدى لما مفيش حاجة بتتحرك قدامك، والموجات بتقف والشباك متصغّر،
    /// والمواعيد الدورية بتفضل صح مهما كانت سرعة التايمر. (المفروض فيه صوت شغال لما بتتنده.)
    /// </summary>
    private void RunBatteryChecks(Action<int> pump)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("battery: " + what); }
        var shown = WindowState;
        try
        {
            // شغال والشباك ظاهر: سريع (شريط الوقت لازم يتحرك ناعم)
            Check(_mp.State == VLCState.Playing, "precondition: audio should be playing, got " + _mp.State);
            pump(400);
            Check(_uiTimer.Interval == UiFastMs, $"playing visible should be {UiFastMs} ms, got {_uiTimer.Interval}");

            // شغال والشباك متصغّر: ثانية (حفظ المكان كفاية عليه)
            WindowState = FormWindowState.Minimized;
            pump(1500);
            Check(DesiredUiInterval() == UiIdleMs && _uiTimer.Interval == UiIdleMs, $"playing minimized should be {UiIdleMs} ms, got {_uiTimer.Interval}");
            double w = MeasureResources("audio playing, window minimized", 4000, pump);
            Check(w < 1.6, $"minimized playback woke the UI {w:F1}/s");

            // متوقف والشباك متصغّر: ثانيتين
            _mp.SetPause(true);
            pump(1500);
            Check(DesiredUiInterval() == UiBackgroundMs, "paused minimized should be " + UiBackgroundMs + ", got " + DesiredUiInterval());
            w = MeasureResources("paused, window minimized", 4000, pump);
            Check(w < 0.9, $"paused minimized woke the UI {w:F1}/s");

            // الموجات (راديو/بث): بتقف والشباك متصغّر وترجع لما يرجع
            bool radioWas = _radioVisual.Visible;
            _radioVisual.Visible = true;
            pump(100);
            Check(!_radioVisual.AnimationRunning, "radio animation should stop while minimized");
            WindowState = shown == FormWindowState.Minimized ? FormWindowState.Normal : shown;
            pump(300);
            Check(_radioVisual.AnimationRunning, "radio animation should resume after restore");
            _radioVisual.Visible = radioWas;

            // الرجوع: الواجهة تصحى فورًا
            _mp.SetPause(false);
            pump(800);
            Check(_uiTimer.Interval == UiFastMs, "restored + playing should be fast again, got " + _uiTimer.Interval);

            // المواعيد بالوقت مش بعدد الدقات
            long due = 0;
            Check(Due(ref due, 1000) && !Due(ref due, 1000), "Due() should fire once then wait");
            Console.WriteLine("SMOKE battery OK");
        }
        finally
        {
            if (WindowState == FormWindowState.Minimized) WindowState = shown == FormWindowState.Minimized ? FormWindowState.Normal : shown;
            if (_mp.State == VLCState.Paused) _mp.SetPause(false);
        }
    }

    private void RunStressAndPlaybackChecks()
    {
        string dir = Path.Combine(Path.GetTempPath(), "masar-smoke");
        Directory.CreateDirectory(dir);
        string wav = Path.Combine(dir, "tone.wav");
        WriteToneWav(wav, seconds: 40);

        // مكتبة كبيرة: 10 كورسات × 20 محاضرة × 25 فيديو = 5000 فيديو
        var lib = new CourseLibrary { Name = "فحص" };
        for (int c = 0; c < 10; c++)
        {
            var course = new Course { Name = $"Course {c}" };
            for (int g = 0; g < 20; g++)
            {
                var group = new LectureGroup { Name = $"Lecture {c}-{g}" };
                for (int v = 0; v < 25; v++)
                    group.Videos.Add(new VideoItem { Path = Path.Combine(dir, $"missing-{c}-{g}-{v}.mp4"), Title = $"Video {c}-{g}-{v}", DurationMs = 600_000 });
                course.Groups.Add(group);
            }
            lib.Courses.Add(course);
        }
        var real = new VideoItem { Path = wav, Title = "Tone", DurationMs = 40000 };
        lib.Courses[0].Groups[0].Videos.Insert(0, real);
        string libPath = Path.Combine(dir, "stress.lpl");
        Storage.SaveLibrary(libPath, lib);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        LoadLibrary(libPath, remember: false);
        Application.DoEvents();
        long loadMs = sw.ElapsedMilliseconds;

        sw.Restart();
        _searchBox.Text = "Video 9-19-24";
        RebuildTree();
        Application.DoEvents();
        long searchMs = sw.ElapsedMilliseconds;
        _searchBox.Text = "";
        RebuildTree();

        sw.Restart();
        SaveLibrary();
        long saveMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"SMOKE perf: load+tree {loadMs} ms, search {searchMs} ms, save {saveMs} ms (5001 videos)");
        if (loadMs > 15000) throw new Exception($"perf: loading a 5000-video library took {loadMs} ms");
        if (searchMs > 5000) throw new Exception($"perf: search took {searchMs} ms");
        if (saveMs > 5000) throw new Exception($"perf: save took {saveMs} ms");
        void PumpFor(int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { Application.DoEvents(); Thread.Sleep(15); } }
        MeasureResources("idle, 5000-video library, 0-5s", 5000, PumpFor);
        MeasureResources("idle, 5000-video library, 5-10s", 5000, PumpFor);
        double idleWakeups = MeasureResources("idle, 5000-video library, 10-15s", 5000, PumpFor);
        if (idleWakeups > 1.6) throw new Exception($"battery: idle UI timer woke {idleWakeups:F1}/s (should be ~1/s)");

        // تشغيل فعلي في VLC
        var group0 = _library.Courses[0].Groups[0];
        var tone = group0.Videos[0];
        PlayVideo(group0, tone, 0);
        var until = DateTime.UtcNow.AddSeconds(25);
        VLCState state = VLCState.NothingSpecial;
        long time = 0;
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(50);
            state = _mp.State;
            time = _mp.Time;
            if (state == VLCState.Error) break;
            if (state == VLCState.Playing && time > 500) break;
        }
        Console.WriteLine($"SMOKE playback: state {state}, time {time} ms");
        MeasureResources("audio playing, vlc log ON", 2500, PumpFor);
        SetVlcLogCapture(false);
        MeasureResources("audio playing, vlc log OFF", 2500, PumpFor);
        SetVlcLogCapture(true);
        if (state == VLCState.Error) throw new Exception("playback: VLC error playing a local audio file");
        RunBatteryChecks(PumpFor);
        if (state != VLCState.Playing || time <= 0) throw new Exception($"playback: never started (state {state}, time {time})");
        // التكرار: ملف صوت قصير (ثانيتين) لازم يتعاد لوحده لما يخلص
        string shortWav = Path.Combine(dir, "short.wav");
        WriteToneWav(shortWav, seconds: 2);
        var shortItem = new VideoItem { Path = shortWav, Title = "Short", DurationMs = 2000 };
        group0.Videos.Insert(1, shortItem);
        int oldRepeat = _settings.RepeatMode;
        _settings.RepeatMode = 1;
        PlayVideo(group0, shortItem, 0);
        bool sawEnd = false, repeated = false;
        long maxTime = 0;
        var ru = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < ru)
        {
            Application.DoEvents(); Thread.Sleep(30);
            long tnow = _mp.Time;
            if (_mp.State == VLCState.Ended || (maxTime > 1200 && tnow < 600)) sawEnd = true;
            maxTime = Math.Max(maxTime, tnow);
            // اتشغّل من الأول تاني: الوقت رجع صغير بعد ما كان قرب الآخر والملف شغال
            if (sawEnd && _mp.State == VLCState.Playing && _currentVideo == shortItem && tnow > 0 && tnow < 1500) { repeated = true; break; }
        }
        _settings.RepeatMode = oldRepeat;
        Console.WriteLine($"SMOKE repeat: ended {sawEnd}, replayed {repeated}");
        if (!repeated) throw new Exception("repeat: the video did not play again after it ended");
        StopPlayback();
        Application.DoEvents();

        // استخراج الصوت لتحويل الكلام لنص بالمشغل نفسه (من غير ffmpeg)
        string extracted = Path.Combine(dir, "extracted.wav");
        var swx = System.Diagnostics.Stopwatch.StartNew();
        Task.Run(() => VlcAudio.ExtractWavAsync(wav, extracted, CancellationToken.None)).GetAwaiter().GetResult();
        long bytes = new FileInfo(extracted).Length;
        Console.WriteLine($"SMOKE vlc audio extract: {bytes} bytes in {swx.ElapsedMilliseconds} ms");
        // 8 ثواني × 16000 × 2 بايت ≈ 256 كيلو
        if (bytes < 200_000) throw new Exception($"vlc audio extract too small: {bytes}");

        // المشغل الصغير وملء الشاشة بفيديو فيه صورة فعلًا (صورة PNG بيعرضها VLC كفيديو):
        // شباك VLC لازم يغطي مساحة الفيديو بالظبط بعد كل تغيير مقاس
        string png = Path.Combine(dir, "frame.png");
        using (var bmp = new Bitmap(1920, 1080))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.DarkSlateBlue);
            g.FillEllipse(Brushes.Gold, 760, 340, 400, 400);
            bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        }
        var picture = new VideoItem { Path = png, Title = "Frame", DurationMs = 10000 };
        group0.Videos.Insert(1, picture);
        void Pump(int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { Application.DoEvents(); Thread.Sleep(20); } }
        // شباك كبير الأول (زي جهاز المستخدم) عشان المشغل الصغير يبقى أصغر منه بكتير
        WindowState = FormWindowState.Normal;
        var wa = Screen.FromControl(this).WorkingArea;
        Bounds = new Rectangle(wa.Left, wa.Top, wa.Width, wa.Height);
        Pump(300);
        PlayVideo(group0, picture, 0);
        var vu = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < vu && VlcWindowsMisfit() == "no vlc window") Pump(100);
        Pump(500);
        Console.WriteLine($"SMOKE video window (full): {VlcWindowsMisfit() ?? "fits"} :: {DescribeVideoWindows()}");
        MeasureResources("video playing", 4000, Pump);

        // تسريب ذاكرة؟ 40 مرة تبديل بين فيديو وصوت، ونقيس قبل وبعد
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            long priv0 = self.PrivateMemorySize64, managed0 = GC.GetTotalMemory(true);
            int handles0 = self.HandleCount, threads0 = self.Threads.Count;
            for (int i = 0; i < 40; i++)
            {
                PlayVideo(group0, i % 2 == 0 ? tone : picture, 0);
                Pump(250);
            }
            PlayVideo(group0, picture, 0);
            Pump(1500);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            self.Refresh();
            long privGrow = (self.PrivateMemorySize64 - priv0) / 1048576, managedGrow = (GC.GetTotalMemory(true) - managed0) / 1048576;
            int handleGrow = self.HandleCount - handles0, threadGrow = self.Threads.Count - threads0;
            Console.WriteLine($"SMOKE leak (40 switches): private {privGrow:+#;-#;0} MB, .NET {managedGrow:+#;-#;0} MB, handles {handleGrow:+#;-#;0}, threads {threadGrow:+#;-#;0}");
            if (managedGrow > 50 || handleGrow > 400 || threadGrow > 20)
                throw new Exception($"leak: 40 video switches grew .NET memory {managedGrow} MB, handles {handleGrow}, threads {threadGrow}");
        }
        MeasureResources("video playing after switches", 3000, Pump);

        // من غير الإصلاح (عشان نتأكد إن ده فعلًا سبب المشكلة)
        DisableVideoFitForTest = true;
        ToggleMiniMode(); Pump(800);
        Console.WriteLine($"SMOKE mini WITHOUT fix: {VlcWindowsMisfit() ?? "fits"} :: {DescribeVideoWindows()}");
        ToggleMiniMode(); Pump(800);
        DisableVideoFitForTest = false;
        FitVlcWindows(); Pump(300);

        // بالإصلاح
        foreach (var (name, toggle) in new (string, Action)[] { ("mini", ToggleMiniMode), ("back from mini", ToggleMiniMode), ("fullscreen", ToggleFullscreen), ("back from fullscreen", ToggleFullscreen) })
        {
            toggle();
            Pump(800);
            string? misfit = VlcWindowsMisfit();
            Console.WriteLine($"SMOKE {name}: {misfit ?? "fits"} :: {DescribeVideoWindows()}");
            Console.WriteLine($"SMOKE layout {name}: {VideoAreaOutsideWindow() ?? "inside"} :: {DescribeLayoutChain()}");
            if (VideoAreaOutsideWindow() is { } outside) throw new Exception($"video area outside the window after {name}: {outside}");
            if (misfit != null && misfit != "no vlc window") throw new Exception($"video window does not fit after {name}: {misfit}");
        }
        if (_miniMode || _fullscreen) throw new Exception("mini/fullscreen did not toggle back");

        // من شباك مكبّر للآخر (Maximized) للمشغل الصغير — زي ما المستخدم بيستخدمه غالبًا
        WindowState = FormWindowState.Maximized; Pump(800);
        DisableVideoFitForTest = true;
        ToggleMiniMode(); Pump(1000);
        Console.WriteLine($"SMOKE maximized->mini WITHOUT fix: {VlcWindowsMisfit() ?? "fits"} :: {DescribeVideoWindows()}");
        Console.WriteLine($"SMOKE layout maximized->mini: {VideoAreaOutsideWindow() ?? "inside"} :: {DescribeLayoutChain()}");
        DisableVideoFitForTest = false;
        HealVideoFit(); Pump(500);
        string? mm = VlcWindowsMisfit();
        Console.WriteLine($"SMOKE maximized->mini: {mm ?? "fits"} :: {DescribeVideoWindows()}");
        if (mm != null && mm != "no vlc window") throw new Exception("video window does not fit after maximized->mini: " + mm);
        Console.WriteLine($"SMOKE layout maximized->mini (fixed): {VideoAreaOutsideWindow() ?? "inside"} :: {DescribeLayoutChain()}");
        if (VideoAreaOutsideWindow() is { } outside2) throw new Exception("video area outside the window after maximized->mini: " + outside2);
        ToggleMiniMode(); Pump(800);
        WindowState = FormWindowState.Normal; Pump(300);
        StopPlayback();
        Application.DoEvents();
    }

    /// <summary>ملف صوت WAV بسيط (نغمة 440 هرتز) للفحص.</summary>
    private static void WriteToneWav(string path, int seconds)
    {
        const int rate = 22050;
        int samples = rate * seconds;
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write("RIFF"u8.ToArray()); bw.Write(36 + samples * 2); bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8.ToArray()); bw.Write(samples * 2);
        for (int i = 0; i < samples; i++) bw.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
    }
}
