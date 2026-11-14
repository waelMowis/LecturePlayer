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
                ? "اتفتح الوضع المتقدم: كل الأدوات بقت ظاهرة (الذكاء الاصطناعي، الإحصائيات، المراجعة، التركيز، معمل الكود)"
                : "رجعت للوضع البسيط: الأدوات المتقدمة اتخبت (تقدر ترجّعها من مساعدة ← الوضع المتقدم)",
            BannerKind.Info, seconds: 8);
    }

    private void ApplyMode()
    {
        bool adv = AdvancedMode;
        foreach (ToolStripItem top in _menu.Items) ApplyMode(top, adv);
        foreach (var b in _advancedActivity) b.Visible = adv;
        if (_advancedItem != null) _advancedItem.Checked = adv;
    }

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
        switch (choice)
        {
            case TourChoice.AddFolder: ImportCourseDialog(); break;
            case TourChoice.AddUrl: AddUrlsDialog(null); break;
            case TourChoice.Live: ShowLiveWindow(); break;
            default:
                ShowBanner("لو احتجت أي حاجة: F1 لدليل الاستخدام، أو مساعدة ← جولة سريعة", BannerKind.Info, seconds: 10);
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
        }
        ShowBanner("فحص", BannerKind.Warning, "زرار", () => { }, seconds: 0, key: "smoke");
        HideBanner("smoke");
        foreach (var kind in Enum.GetValues<BannerKind>()) ShowBanner("فحص", kind, seconds: 1);
        HideBanner();
        using (var m = NewStudyMenu()) { m.Show(this, Point.Empty); m.Close(); }
        RefreshHome();
        TourForm.SmokeTest(this);

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
        ShowBanner("«مسار» حصل فيه عطل المرة اللي فاتت واتسجل سببه — بياناتك سليمة", BannerKind.Warning,
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
            Text = "ده تقرير العطل اللي حصل. انسخه وابعته للمطوّر عشان يتصلّح. التقرير فيه أسماء ملفات من جهازك (زي مكان المكتبة)، فراجعه قبل ما تبعته لحد."
        };
        var box = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            Text = report, BackColor = Theme.Input, ForeColor = Theme.Text, Font = new Font("Consolas", 9f), RightToLeft = RightToLeft.No,
            BorderStyle = BorderStyle.None
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Window };
        var copy = new PillButton("📋  انسخ التقرير", PillStyle.Primary);
        copy.Click += (_, _) => { try { Clipboard.SetText(report); copy.Text = "اتنسخ ✓"; copy.AutoSizeToText(); } catch { } };
        var folder = new PillButton("فتح فولدر السجل");
        folder.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(Log.LogDir) { UseShellExecute = true }); } catch { } };
        var close = new PillButton("قفل", PillStyle.Subtle) { DialogResult = DialogResult.Cancel };
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
    private void RunStressAndPlaybackChecks()
    {
        string dir = Path.Combine(Path.GetTempPath(), "masar-smoke");
        Directory.CreateDirectory(dir);
        string wav = Path.Combine(dir, "tone.wav");
        WriteToneWav(wav, seconds: 8);

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
        var real = new VideoItem { Path = wav, Title = "Tone", DurationMs = 8000 };
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
        if (state == VLCState.Error) throw new Exception("playback: VLC error playing a local audio file");
        if (state != VLCState.Playing || time <= 0) throw new Exception($"playback: never started (state {state}, time {time})");
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
        using (var bmp = new Bitmap(640, 360))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.DarkSlateBlue);
            g.FillEllipse(Brushes.Gold, 220, 80, 200, 200);
            bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        }
        var picture = new VideoItem { Path = png, Title = "Frame", DurationMs = 10000 };
        group0.Videos.Insert(1, picture);
        void Pump(int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { Application.DoEvents(); Thread.Sleep(20); } }
        PlayVideo(group0, picture, 0);
        var vu = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < vu && VlcWindowsMisfit() == "no vlc window") Pump(100);
        Pump(500);
        Console.WriteLine($"SMOKE video window (full): {VlcWindowsMisfit() ?? "fits"} :: {DescribeVideoWindows()}");

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
            if (misfit != null && misfit != "no vlc window") throw new Exception($"video window does not fit after {name}: {misfit}");
        }
        if (_miniMode || _fullscreen) throw new Exception("mini/fullscreen did not toggle back");
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
