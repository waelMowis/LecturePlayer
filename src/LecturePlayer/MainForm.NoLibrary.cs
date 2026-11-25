using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// البرنامج من غير مكتبة مفتوحة: بيفتح فاضي (إلا لو المستخدم اختار «افتح آخر مكتبة» من الإعدادات)،
/// وبيختار المكتبة اللي هيشتغل عليها من «مكتباتك» أو «فتح مكتبة». وأي إضافة وإنت من غير مكتبة
/// بتسألك تفتح مكتبة الأول، عشان مفيش حاجة تتضاف وتضيع من غير ما تتحفظ.
/// وعند القفل: المكتبة بتتحفظ على آخر حالة، والكاش المؤقت بتاع البرنامج بيتمسح.
/// </summary>
internal sealed partial class MainForm
{
    /// <summary>فيه مكتبة مفتوحة (ليها ملف بتتحفظ فيه)؟</summary>
    private bool HasLibrary => !string.IsNullOrEmpty(_libraryPath);

    /// <summary>من غير مكتبة: الشجرة فاضية، والعنوان «افتح مكتبة ▾»، والرئيسية بتعرض مكتباتك.</summary>
    private void StartWithoutLibrary()
    {
        if (HasLibrary)
        {
            SaveCurrentPosition();
            CommitNotes();
            FlushSession();
            SaveLibrary();
        }
        StopPlaybackCore(clearCurrent: true);
        _library = new CourseLibrary { Name = "" };
        _libraryPath = "";
        _journal = null;
        _dirty = false;
        _positionDirty = false;
        _deletedKeys.Clear();
        _libraryStampUtc = DateTime.MinValue;
        _undo.Clear();
        _statusFile.Text = "";
        _statusFile.ToolTipText = "";
        RestoreTabs();
        RebuildTree();
        UpdateLibraryHeader();
        UpdateTitle();
        UpdateStudyStatus();
        RefreshHome();
        Log.Info("started without a library");
    }

    /// <summary>
    /// قبل أي إضافة: لو مفيش مكتبة مفتوحة، نسأل المستخدم يفتح واحدة أو يعمل جديدة.
    /// بيرجّع true لو فيه مكتبة مفتوحة بعد كده.
    /// </summary>
    private bool EnsureLibrary()
    {
        if (HasLibrary) return true;
        ShowLibraryChooser("لإضافة دورات تحتاج إلى مكتبة تُحفظ فيها. افتح مكتبة من مكتباتك، أو أنشئ مكتبة جديدة:");
        return HasLibrary;
    }

    /// <summary>شاشة صغيرة: مكتباتك الأخيرة (باسمها ومكانها)، وفتح من ملف، ومكتبة جديدة.</summary>
    /// <summary>للفحص التلقائي بس: شاشة اختيار المكتبة تتقفل على طول من غير اختيار.</summary>
    internal static bool ChooserAutoCancelForSmoke;

    private void ShowLibraryChooser(string message)
    {
        if (ChooserAutoCancelForSmoke) return;
        using var kit = new FormKit("افتح مكتبة", 520);
        kit.Note(message, 40);
        var list = kit.Full(new ListBox { IntegralHeight = false, RightToLeft = RightToLeft.Yes }, 170);
        var items = RecentLibraries(10).Where(x => !x.Current).ToList();
        foreach (var e in items) list.Items.Add(e.Text);
        if (items.Count == 0) list.Items.Add("(لا توجد مكتبات بعد — أنشئ مكتبة جديدة أو افتح ملفًا)");
        int firstAvailable = items.FindIndex(x => x.Exists);
        if (firstAvailable >= 0) list.SelectedIndex = firstAvailable;

        string? chosen = null;
        Action? after = null;
        kit.Buttons("افتح", "إلغاء",
            ("📂  من ملف…", () => { after = OpenLibraryDialog; kit.Form.DialogResult = DialogResult.Cancel; kit.Form.Close(); }),
            ("➕  مكتبة جديدة…", () => { after = NewLibraryDialog; kit.Form.DialogResult = DialogResult.Cancel; kit.Form.Close(); }));
        void Pick()
        {
            int i = list.SelectedIndex;
            if (i >= 0 && i < items.Count && items[i].Exists) chosen = items[i].Path;
        }
        list.DoubleClick += (_, _) => { Pick(); if (chosen != null) { kit.Form.DialogResult = DialogResult.OK; kit.Form.Close(); } };
        list.SelectedIndexChanged += (_, _) => kit.Ok!.Enabled = list.SelectedIndex >= 0 && list.SelectedIndex < items.Count && items[list.SelectedIndex].Exists;
        kit.Ok!.Enabled = firstAvailable >= 0;

        if (kit.Show(this) == DialogResult.OK) Pick();
        if (chosen != null) SwitchLibrary(chosen);
        else after?.Invoke();
    }

    /// <summary>
    /// أمان: لو حاجة اتغيّرت وإحنا من غير مكتبة (مثلًا قناة بث اتضافت)، نعرض نحفظها في مكتبة جديدة
    /// بدل ما تضيع وقت القفل.
    /// </summary>
    private void WarnUnsavedWithoutLibrary()
    {
        if (HasLibrary || _closing) return;
        if (_library.Courses.Count == 0 && _library.Topics.Count == 0 && _library.Live.Count == 0) return;
        ShowBanner("ما أضفته غير محفوظ في أي مكتبة — احفظه في مكتبة حتى لا يضيع عند الإغلاق",
            BannerKind.Warning, "💾  احفظه في مكتبة جديدة", SaveUnsavedAsNewLibrary, seconds: 0, key: "unsaved-no-library");
    }

    private void SaveUnsavedAsNewLibrary()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "اسم ومكان المكتبة",
            Filter = Storage.LibraryFilter,
            DefaultExt = Storage.LibraryExtension,
            FileName = "مكتبة جديدة" + Storage.LibraryExtension,
            InitialDirectory = Storage.AppDataDir,
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _library.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
            Storage.SaveLibrary(dlg.FileName, _library);
        }
        catch (Exception ex) { Dialogs.Error(this, "تعذّر حفظ المكتبة:\n" + ex.Message); return; }
        HideBanner("unsaved-no-library");
        LoadLibrary(dlg.FileName);
    }

    /// <summary>
    /// عند القفل: البرنامج بيمسح الكاش المؤقت بتاعه — صور المعاينة على شريط التقدم، وصور قراية النص من الفيديو،
    /// وملفات الصوت المؤقتة بتاعة تحويل الكلام لنص، ونسخ التحديث القديمة — وبيفضّي الكاش اللي في الذاكرة.
    /// مش بيلمس: المكتبات، وسجل المذاكرة، والنسخ الاحتياطية، والفيديوهات المتنزلة للمشاهدة من غير نت، والأدوات.
    /// </summary>
    private void ClearCachesOnExit()
    {
        FileCache.Clear();
        LibraryFiles.ClearNames();
        int deleted = 0;
        void DeleteDir(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                deleted += Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count();
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) { Log.Error("clear cache " + dir, ex); }
        }
        DeleteDir(Path.Combine(Storage.AppDataDir, "cache"));
        string temp = Path.GetTempPath();
        DeleteDir(Path.Combine(temp, "LecturePlayer-subs"));
        DeleteDir(Path.Combine(temp, "LecturePlayer-whisper"));
        // نسخ التحديث: إلا لو فيه تحديث هيتسطّب دلوقتي بعد القفل
        if (_pendingInstaller == null) DeleteDir(Path.Combine(temp, "Masar-Update"));
        Log.Info($"caches cleared on exit ({deleted} files)");
    }

    /// <summary>فحص تلقائي: البداية من غير مكتبة، والحماية من الإضافة من غير مكتبة، وفتح مكتبة بعدها، ومسح الكاش.</summary>
    internal void RunNoLibraryChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("no library: " + what); }
        Check(!new AppSettings().OpenLastLibraryOnStart, "default should start empty");

        StartWithoutLibrary();
        Check(!HasLibrary && _library.Courses.Count == 0 && _tree.Nodes.Count == 0, "empty state");
        void PumpFor(int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { Application.DoEvents(); Thread.Sleep(15); } }
        MeasureResources("idle, no library", 4000, PumpFor);
        Check(_courseLabel.Text.Contains("افتح مكتبة") && Text == AppTitle, "header/title: " + _courseLabel.Text + " / " + Text);
        RefreshHome();
        Check(_homeCards.Controls.Count == RecentLibraries(8).Count && _homeTitle.Text.Length > 0, "home lists libraries");

        // أي إضافة من غير مكتبة ← بتسأل تفتح مكتبة (هنا بتتلغي) ومفيش حاجة بتتضاف
        ChooserAutoCancelForSmoke = true;
        try
        {
            Check(AddTopic("x") == null && AddCourse("y") == null && _library.Topics.Count == 0 && _library.Courses.Count == 0, "add blocked");
            AddPlaceFromMenu();
            Check(_library.Places.Count == 0, "place blocked");
        }
        finally { ChooserAutoCancelForSmoke = false; }
        SaveLibrary(); // من غير مكتبة: مش بيعمل حاجة ومش بيقع
        Check(!HasLibrary, "save without library");

        // فتح مكتبة بعدها
        string dir = Path.Combine(Path.GetTempPath(), "masar-smoke-nolib");
        Directory.CreateDirectory(dir);
        string libPath = Path.Combine(dir, "Study.lpl");
        Storage.SaveLibrary(libPath, new CourseLibrary { Name = "Study", Courses = { new Course { Name = "C" } } });
        SwitchLibrary(libPath);
        Check(HasLibrary && _library.Name == "Study" && _courseLabel.Text.Contains("Study") && _tree.Nodes.Count == 1, "open after empty");
        // وقفلها يرجّع للحالة الفاضية من غير ما يقع، وبعد ما يحفظ
        _library.Courses.Add(new Course { Name = "D" });
        MarkDirty();
        StartWithoutLibrary();
        Check(!HasLibrary && Storage.LoadLibrary(libPath).Courses.Count == 2, "close library saves");

        // مسح الكاش عند القفل: صور المعاينة والمؤقت بيتمسحوا، والمتنزّل للمشاهدة من غير نت لأ
        string thumb = Path.Combine(Storage.AppDataDir, "cache", "thumbs", "x", "1.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(thumb)!);
        File.WriteAllText(thumb, "x");
        string wh = Path.Combine(Path.GetTempPath(), "LecturePlayer-whisper", "abc", "a.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(wh)!);
        File.WriteAllText(wh, "x");
        string offline = Path.Combine(Storage.AppDataDir, "offline", "smoke-keep.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(offline)!);
        File.WriteAllText(offline, "x");
        ClearCachesOnExit();
        Check(!File.Exists(thumb) && !File.Exists(wh), "cache not cleared");
        Check(File.Exists(offline), "offline download deleted!");
        File.Delete(offline);
        Directory.Delete(dir, true);
        Console.WriteLine("SMOKE no library OK");
    }
}
