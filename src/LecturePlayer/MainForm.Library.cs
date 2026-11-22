using System.Diagnostics;
using LecturePlayer.Data;
using LecturePlayer.UI;
using LibVLCSharp.Shared;

namespace LecturePlayer;

/// <summary>
/// إدارة المكتبة: كورسات ← محاضرات ← فيديوهات.
/// الشجرة، السحب والإفلات، الاستيراد، الحفظ.
/// </summary>
internal sealed partial class MainForm
{
    private CourseLibrary _library = new();
    private string _libraryPath = "";
    private bool _dirty;
    private bool _rebuilding;

    // ---------- المزامنة بين الأجهزة ----------
    private DateTime _libraryStampUtc;                       // وقت آخر نسخة من الملف قريناها أو كتبناها
    private readonly HashSet<string> _deletedKeys = new();   // اللي اتمسح هنا في الجلسة دي (ميرجعش من الجهاز التاني)

    private readonly Dictionary<VideoItem, TreeNode> _videoNodes = new();
    private readonly Dictionary<LectureGroup, TreeNode> _groupNodes = new();
    private readonly Dictionary<Course, TreeNode> _courseNodes = new();
    private readonly Dictionary<Topic, TreeNode> _topicNodes = new();

    private readonly Queue<VideoItem> _probeQueue = new();
    private bool _probing;

    // =====================================================================
    //  ملف القائمة
    // =====================================================================

    private string? _waitingLibraryPath;
    private DateTime _waitingLibrarySinceUtc;

    /// <summary>
    /// بيتنده من التايمر: لو آخر قائمة كانت مش موجودة وقت الفتح (درايف سحابي لسه بيفتح)، نفتحها أول ما تظهر.
    /// </summary>
    private void CheckWaitingLibrary()
    {
        if (_waitingLibraryPath is not { } path) return;
        if (File.Exists(path))
        {
            _waitingLibraryPath = null;
            Log.Info("waited library appeared: " + path);
            SwitchLibrary(path);
            ShowBanner("اتفتحت مكتبتك «" + Path.GetFileNameWithoutExtension(path) + "» ✓", BannerKind.Success, seconds: 6, key: "waiting-library");
            return;
        }
        if (DateTime.UtcNow - _waitingLibrarySinceUtc < TimeSpan.FromMinutes(2)) return;

        _waitingLibraryPath = null;
        HideBanner("waiting-library");
        if (Dialogs.Confirm(this,
                "مقدرتش ألاقي آخر مكتبة كنت فاتحها:\n" + path +
                "\n\nلو الملف في Google Drive أو OneDrive، اتأكد إن البرنامج بتاعهم شغال.\nتحب تدوّر عليها بنفسك؟", "ملف المكتبة"))
            OpenLibraryDialog();
    }

    /// <param name="remember">false = فتح مؤقت، مايتسجلش كـ «آخر قائمة» (عشان مانضيّعش القائمة الأصلية)</param>
    private void LoadLibrary(string path, bool remember = true)
    {
        CourseLibrary? lib = null;
        if (File.Exists(path))
        {
            try { lib = Storage.LoadLibrary(path, _settings.PathMappings); }
            catch (Exception ex)
            {
                Dialogs.Error(this, "مقدرتش أفتح ملف المكتبة:\n" + path + "\n\n" + ex.Message);
                if (!string.Equals(path, Storage.DefaultLibraryPath, StringComparison.OrdinalIgnoreCase))
                {
                    // مؤقتًا: من غير مانسجّل «مكتبتي» كآخر قائمة، فالقائمة الأصلية تفتح تاني المرة الجاية
                    LoadLibrary(Storage.DefaultLibraryPath, remember: false);
                    return;
                }
            }
        }

        StopPlaybackCore(clearCurrent: true);

        _library = lib ?? new CourseLibrary
        {
            Name = string.Equals(path, Storage.DefaultLibraryPath, StringComparison.OrdinalIgnoreCase)
                ? "مكتبتي"
                : Path.GetFileNameWithoutExtension(path)
        };
        if (string.IsNullOrWhiteSpace(_library.Name)) _library.Name = Path.GetFileNameWithoutExtension(path);
        // إصلاح لمرة واحدة: «حفظ نسخة باسم» قبل 4.1.37 كان بيسيب الاسم الافتراضي «مكتبتي» جوه الملف الجديد،
        // فمكتبتين بيبانوا بنفس الاسم. المكتبة دي تاخد اسم ملفها.
        bool healedName = false;
        if (lib != null && _library.Name == "مكتبتي" && !string.Equals(path, Storage.DefaultLibraryPath, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(path) is var fileName && fileName != "مكتبتي")
        {
            Log.Info($"library name healed: «مكتبتي» -> «{fileName}» ({path})");
            _library.Name = fileName;
            healedName = true;
        }
        _libraryPath = path;
        HideBanner("unsaved-no-library");
        InitJournal(path);
        _dirty = lib == null || healedName;
        _deletedKeys.Clear();
        _undo.Clear();
        _libraryStampUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (lib != null) Storage.BackupIfDue(path);
        Log.Info($"library loaded: {path} ({_library.Courses.Count} courses)");

        if (remember)
        {
            if (_waitingLibraryPath != null && !string.Equals(_waitingLibraryPath, path, StringComparison.OrdinalIgnoreCase))
                HideBanner("waiting-library");
            _waitingLibraryPath = null;
            _settings.LastLibraryPath = path;
            AddRecent(path);
            try { Storage.SaveSettings(_settings); } catch { } // من غير مقاس الشباك (ممكن نكون لسه بنفتح)
        }

        _searchBox.Clear();
        RefreshFilterCombo();
        RebuildTree();
        SelectLastPlayed();
        UpdateLibraryHeader();
        UpdateStatusBar();
        UpdateTitle();
        _statusFile.Text = Path.GetFileName(path);
        _statusFile.ToolTipText = path;

        QueueProbe(_library.AllVideos);
        if (_dirty) SaveLibrary();
        RefreshNotesPane();
        RefreshHome();
    }

    private void SwitchLibrary(string path)
    {
        if (string.Equals(path, _libraryPath, StringComparison.OrdinalIgnoreCase)) return;
        SaveCurrentPosition();
        SaveLibrary();
        LoadLibrary(path);
    }

    private void NewLibraryDialog()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "اسم ومكان المكتبة الجديدة",
            Filter = Storage.LibraryFilter,
            DefaultExt = Storage.LibraryExtension,
            FileName = "مكتبة جديدة" + Storage.LibraryExtension,
            InitialDirectory = Storage.AppDataDir, // فولدر Data جنب البرنامج
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        SaveCurrentPosition();
        SaveLibrary();
        try
        {
            Storage.SaveLibrary(dlg.FileName, new CourseLibrary { Name = Path.GetFileNameWithoutExtension(dlg.FileName) });
        }
        catch (Exception ex) { Dialogs.Error(this, ex.Message); return; }
        LoadLibrary(dlg.FileName);
        ShowStatus("المكتبة الجديدة جاهزة — ضيف فولدر فيديوهات أو اسحبه وارميه هنا", 6);
    }

    private void OpenLibraryDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "فتح مكتبة",
            Filter = Storage.LibraryFilter,
            InitialDirectory = HasLibrary ? Path.GetDirectoryName(_libraryPath) : Storage.AppDataDir
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) SwitchLibrary(dlg.FileName);
    }

    private void SaveLibraryAsDialog()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "حفظ نسخة من المكتبة باسم تاني",
            Filter = Storage.LibraryFilter,
            DefaultExt = Storage.LibraryExtension,
            FileName = _library.Name + Storage.LibraryExtension,
            InitialDirectory = Path.GetDirectoryName(_libraryPath)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        RelocateLibrary(dlg.FileName, move: false);
    }

    /// <summary>
    /// المزامنة بين الأجهزة: المكتبة تتنقل لفولدر بيتزامن (OneDrive أو Google Drive)، وعلى الجهاز التاني تتفتح من «فتح مكتبة».
    /// </summary>
    private void MoveLibraryToCloudDialog()
    {
        string? oneDrive = new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(d => !string.IsNullOrEmpty(d) && Directory.Exists(d));
        string? gDrive = new[] { @"G:\My Drive", @"H:\My Drive", @"I:\My Drive" }.FirstOrDefault(Directory.Exists);
        string? cloud = gDrive ?? oneDrive;

        if (!Dialogs.Confirm(this,
                "عشان تكمّل مذاكرتك على أكتر من جهاز:\n\n" +
                "1. المكتبة هتتنقل لفولدر بيتزامن (Google Drive أو OneDrive)، ومعاها سجل مذاكرتك.\n" +
                "2. على الجهاز التاني: «📚 اسم المكتبة ▾» ← «فتح مكتبة من ملف»، واختار نفس الملف.\n" +
                "3. البرنامج بيدمج التقدم والعلامات والملاحظات من الجهازين لوحده.\n\n" +
                "الفيديوهات نفسها مش بتتنقل. لو مكانها مختلف على الجهاز التاني، حدد مكان أي فيديو مرة واحدة، " +
                "والبرنامج هيفتكر المكان ده للجهاز ده بس.\n\n" +
                (cloud != null ? "لقيت فولدر بيتزامن هنا:\n" + cloud : "ملقتش Google Drive ولا OneDrive على الجهاز ده، اختار أي فولدر بيتزامن.") +
                "\n\nنكمّل؟", "مزامنة بين الأجهزة"))
            return;

        string initial = cloud ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        using var dlg = new SaveFileDialog
        {
            Title = "مكان المكتبة في فولدر المزامنة",
            Filter = Storage.LibraryFilter,
            DefaultExt = Storage.LibraryExtension,
            FileName = _library.Name + Storage.LibraryExtension,
            InitialDirectory = initial
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        RelocateLibrary(dlg.FileName, move: true);
    }

    /// <summary>
    /// «حفظ نسخة باسم» (move = false) و«نقل لفولدر المزامنة» (move = true) بيعدّوا من هنا:
    /// • المكتبة تتكتب في الملف الجديد باسم الملف الجديد (عشان متبقاش مكتبتين بنفس الاسم).
    /// • سجل المذاكرة والنصوص والملخصات بيتنسخوا معاها.
    /// • البرنامج يكمّل على الملف الجديد (المفتوح دلوقتي)، والعنوان والقوايم بتتحدّث.
    /// • في النقل: القديمة بتتشال من «مكتباتك» عشان متلخبطش (الملف نفسه مش بيتمسح).
    /// </summary>
    private void RelocateLibrary(string target, bool move)
    {
        target = Path.GetFullPath(target);
        if (string.Equals(target, Path.GetFullPath(_libraryPath), StringComparison.OrdinalIgnoreCase)) { SaveLibrary(); ShowStatus("اتحفظ ✓"); return; }

        string oldPath = _libraryPath;
        string oldName = _library.Name;
        string newName = Path.GetFileNameWithoutExtension(target);
        int companions;
        try
        {
            SaveCurrentPosition();
            CommitNotes();
            FlushSession();
            SaveLibrary(); // القديمة تتحفظ بآخر حالة الأول
            _library.Name = newName;
            Storage.SaveLibrary(target, _library);
            companions = LibraryFiles.CopyCompanions(oldPath, target, _library.AllVideos);
        }
        catch (Exception ex)
        {
            _library.Name = oldName;
            Log.Error(move ? "move library" : "save-as", ex);
            Dialogs.Error(this, "مقدرتش أحفظ المكتبة هنا:\n" + target + "\n\n" + ex.Message);
            return;
        }

        _libraryPath = target;
        _libraryStampUtc = File.GetLastWriteTimeUtc(target);
        _dirty = false;
        _deletedKeys.Clear();
        InitJournal(target); // سجل المذاكرة يكمّل في المكان الجديد
        LibraryFiles.ForgetName(target);
        if (move) _settings.RecentFiles.RemoveAll(f => string.Equals(f, oldPath, StringComparison.OrdinalIgnoreCase));
        _settings.LastLibraryPath = target;
        AddRecent(target);
        SaveSettings();
        Storage.BackupIfDue(target);
        _statusFile.Text = Path.GetFileName(target);
        _statusFile.ToolTipText = target;
        UpdateLibraryHeader();
        UpdateTitle();
        Log.Info($"library {(move ? "moved" : "saved as")}: {oldPath} -> {target} (name «{newName}», {companions} companion files)");
        ShowBanner(move
                ? $"المكتبة «{newName}» بقت في {LibraryFiles.LocationLabel(target)} ✓ — افتحها من الجهاز التاني من «فتح مكتبة من ملف»."
                : $"اتعملت نسخة باسم «{newName}» في {LibraryFiles.LocationLabel(target)}، وإنت شغال عليها دلوقتي ✓ — النسخة القديمة «{oldName}» لسه موجودة في «مكتباتك».",
            BannerKind.Success, seconds: 10, key: "library-relocated");
    }

    private void RestoreBackupDialog()
    {
        static bool HasBackups(string d) => Directory.Exists(d) && Directory.GetFiles(d, "*" + Storage.LibraryExtension).Length > 0;
        string dir = Storage.BackupDir(_libraryPath);
        if (!HasBackups(dir)) dir = Storage.LegacyBackupDir(_libraryPath); // نسخ قبل 4.1.37
        if (!HasBackups(dir))
        {
            Dialogs.Info(this, "لسه مفيش نسخ احتياطية للمكتبة دي.\nالبرنامج بياخد نسخة كل 6 ساعات وبيحتفظ بآخر 10.");
            return;
        }

        using var dlg = new OpenFileDialog
        {
            Title = "اختار النسخة اللي عايز ترجعلها",
            Filter = Storage.LibraryFilter,
            InitialDirectory = dir
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (!Dialogs.Confirm(this, $"ترجع المكتبة لنسخة «{Path.GetFileNameWithoutExtension(dlg.FileName)}»؟\n" +
                                   "الحالة الحالية هتتحفظ كنسخة احتياطية الأول، فتقدر ترجعلها.")) return;

        try
        {
            SaveCurrentPosition();
            CommitNotes();
            SaveLibrary();
            Storage.BackupIfDue(_libraryPath, force: true);
            File.Copy(dlg.FileName, _libraryPath, overwrite: true);
            Log.Info("backup restored: " + dlg.FileName);
            LoadLibrary(_libraryPath);
            ShowStatus("رجعت للنسخة الاحتياطية ✓", 5);
        }
        catch (Exception ex)
        {
            Log.Error("restore", ex);
            Dialogs.Error(this, "مقدرتش أرجّع النسخة:\n" + ex.Message);
        }
    }

    private void SaveLibrary()
    {
        if (!TrySaveLibrary(out string? error))
            ShowBanner("مقدرتش أحفظ المكتبة: " + error + " — هحاول تاني لوحدي", BannerKind.Error, seconds: 15, key: "save-error");
    }

    /// <summary>بيحفظ المكتبة (بعد ما يدمج أي تغيير من جهاز تاني). false + السبب لو فشل.</summary>
    private bool TrySaveLibrary(out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(_libraryPath)) return true;
        try
        {
            MergeExternalChanges(); // لو الملف اتغير من جهاز تاني، ندمج الأول عشان منمسحش شغله
            Storage.SaveLibrary(_libraryPath, _library);
            _libraryStampUtc = File.GetLastWriteTimeUtc(_libraryPath);
            _dirty = false;
            _positionDirty = false;
            HideBanner("save-error");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("save", ex);
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// لو ملف القائمة اتغير على الديسك من جهاز تاني (OneDrive)، ندمجه مع اللي عندنا.
    /// بيتنده قبل كل حفظ، ولما البرنامج ياخد الفوكس، وكل 30 ثانية.
    /// </summary>
    private bool MergeExternalChanges()
    {
        if (string.IsNullOrEmpty(_libraryPath) || !File.Exists(_libraryPath)) return false;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(_libraryPath); }
        catch { return false; }
        if (stamp <= _libraryStampUtc.AddSeconds(1)) return false;

        CourseLibrary remote;
        try { remote = Storage.LoadLibrary(_libraryPath, _settings.PathMappings); }
        catch (Exception ex)
        {
            // ممكن OneDrive لسه بيكتب الملف — نجرب المرة الجاية
            Log.Error("merge-read", ex);
            return false;
        }

        var result = LibrarySync.Merge(_library, remote, _deletedKeys);
        _libraryStampUtc = stamp;
        if (result.Total == 0) return false;

        Log.Info($"merged from other device: {result.UpdatedVideos} updated, {result.AddedCourses} courses, {result.AddedGroups} lectures, {result.AddedVideos} videos, {result.UpdatedInfo} info");
        _dirty = true;
        if (result.UpdatedInfo > 0 || result.AddedCourses > 0) RefreshFilterCombo();
        RebuildTree();
        RefreshNotesPane();
        RefreshHome();
        ShowBanner($"اتجابت تغييرات عملتها على جهاز تاني ({result.Total}) ✓", BannerKind.Success, seconds: 6);
        return true;
    }

    private void MarkDirty()
    {
        _dirty = true;
        if (!HasLibrary) WarnUnsavedWithoutLibrary();
    }

    /// <summary>
    /// تغيير مكان التشغيل بس (كل 5 ثواني وإنت بتتفرج): بيتحفظ كل دقيقة بدل كل 10 ثواني،
    /// عشان منكتبش ملف المكتبة على Google Drive/Dropbox طول ما الفيديو شغال. أي تغيير حقيقي بيتحفظ في ثواني زي ما هو.
    /// </summary>
    private void MarkPositionDirty() => _positionDirty = true;
    private bool _positionDirty;

    private void AddRecent(string path)
    {
        _settings.RecentFiles.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, path);
        if (_settings.RecentFiles.Count > 10) _settings.RecentFiles.RemoveRange(10, _settings.RecentFiles.Count - 10);
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
            else if (Directory.Exists(Path.GetDirectoryName(path)))
                Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(path)}\"")?.Dispose();
        }
        catch { }
    }

    /// <summary>
    /// الضغط على اسم المكتبة: قايمة صغيرة فيها مكتباتك الأخيرة (تبدّل بضغطة)، ومكتبة جديدة، وفتح، وتغيير الاسم.
    /// </summary>
    private void ShowLibrarySwitcher()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        ToolStripMenuItem Add(string text, Action click, bool enabled = true, string? shortcut = null)
        {
            var i = new ToolStripMenuItem(text) { ForeColor = Theme.Text, Enabled = enabled, ShortcutKeyDisplayString = shortcut };
            i.Click += (_, _) => click();
            menu.Items.Add(i);
            return i;
        }

        menu.Items.Add(new ToolStripLabel("مكتباتك") { ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) });
        bool anyMissing = false;
        foreach (var e in RecentLibraries(8))
        {
            string path = e.Path;
            var item = Add((e.Current ? "✓  " : "      ") + e.Text, () => { if (!e.Current) SwitchLibrary(path); }, e.Exists);
            item.ToolTipText = path;
            if (e.Current) item.Font = new Font(item.Font, FontStyle.Bold);
            anyMissing |= !e.Exists;
        }
        if (anyMissing) Add("🧹  شيل المكتبات اللي مش موجودة من القايمة", RemoveMissingRecent);
        menu.Items.Add(new ToolStripSeparator());
        Add("➕  مكتبة جديدة…", NewLibraryDialog, shortcut: "Ctrl+N");
        Add("📂  فتح مكتبة من ملف…", OpenLibraryDialog, shortcut: "Ctrl+O");
        menu.Items.Add(new ToolStripSeparator());
        Add("✏️  تغيير اسم المكتبة دي…", RenameLibrary, HasLibrary);
        Add("💾  حفظ نسخة باسم تاني…", SaveLibraryAsDialog, HasLibrary, "Ctrl+Shift+S");
        Add("📍  فتح مكان الملف", () => OpenInExplorer(_libraryPath), HasLibrary);
        Add("✖  قفل المكتبة (البرنامج يفضل مفتوح)", StartWithoutLibrary, HasLibrary);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_courseLabel, new Point(_courseLabel.Width, _courseLabel.Height), ToolStripDropDownDirection.BelowLeft);
    }

    /// <summary>سطر في قايمة المكتبات: الاسم الحقيقي (من جوه الملف) ومكانها، وتحذير لو مش موجودة.</summary>
    private sealed record RecentLibrary(string Path, string Text, bool Current, bool Exists);

    /// <summary>
    /// المكتبات الأخيرة بأساميها الحقيقية + مكانها («على الجهاز» / «Google Drive»…)، عشان مكتبتين بنفس الاسم
    /// (أو نفس اسم الملف) يبانوا مختلفين. المفتوحة دايمًا في الأول.
    /// </summary>
    private List<RecentLibrary> RecentLibraries(int max)
    {
        var files = _settings.RecentFiles.Take(max).ToList();
        if (!string.IsNullOrEmpty(_libraryPath) && !files.Any(f => string.Equals(f, _libraryPath, StringComparison.OrdinalIgnoreCase)))
            files.Insert(0, _libraryPath);
        var list = new List<RecentLibrary>();
        foreach (string f in files)
        {
            bool current = string.Equals(f, _libraryPath, StringComparison.OrdinalIgnoreCase);
            bool exists = current || File.Exists(f);
            string name = current ? _library.Name : exists ? LibraryFiles.PeekName(f) : Path.GetFileNameWithoutExtension(f);
            string text = Modern.Bidi(name) + "   ·   " + LibraryFiles.LocationLabel(f) + (exists ? "" : "  (مش متاحة دلوقتي)");
            list.Add(new RecentLibrary(f, text, current, exists));
        }
        return list;
    }

    private void RemoveMissingRecent()
    {
        int n = _settings.RecentFiles.RemoveAll(f => !string.Equals(f, _libraryPath, StringComparison.OrdinalIgnoreCase) && !File.Exists(f));
        SaveSettings();
        ShowStatus(n == 0 ? "مفيش حاجة تتشال" : $"اتشال {n} من القايمة (الملفات نفسها متمسحتش) ✓", 4);
    }

    private void RenameLibrary()
    {
        string? name = Dialogs.Input(this, "اسم المكتبة",
            "اكتب اسم المكتبة (ده اللي بيظهر فوق الشجرة وفي «مكتباتك»؛ اسم الملف نفسه مش بيتغيّر):", _library.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        _library.Name = name.Trim();
        MarkDirty();
        SaveLibrary();
        LibraryFiles.ForgetName(_libraryPath);
        UpdateLibraryHeader();
        UpdateTitle();
    }

    private void UpdateLibraryHeader()
    {
        // «📚 الاسم ▾»: واضح إن ده اسم المكتبة المفتوحة وإنه بيتضغط
        if (!HasLibrary)
        {
            _courseLabel.Text = "📚  افتح مكتبة  ▾";
            _toolTip.SetToolTip(_courseLabel, "مفيش مكتبة مفتوحة — دوس هنا واختار مكتبة من مكتباتك، أو اعمل مكتبة جديدة");
            return;
        }
        _courseLabel.Text = "📚  " + Modern.Bidi(_library.Name) + "  ▾";
        int videos = _library.VideoCount, watched = _library.WatchedCount;
        long total = _library.TotalDurationMs;
        string duration = total > 0 ? "  ·  " + TimeFormat.Format(total) : "";
        int percent = videos == 0 ? 0 : (int)Math.Round(watched * 100.0 / videos);
        string stats = (_library.Topics.Count > 0 ? $"{_library.Topics.Count} موضوع  ·  " : "") +
            $"{_library.Courses.Count} كورس  ·  {videos} فيديو{duration}  ·  {percent}% خلص";
        _toolTip.SetToolTip(_courseLabel, "المكتبة المفتوحة: " + _library.Name + "  ·  " + LibraryFiles.LocationLabel(_libraryPath) + "\n" + stats + "\n" + _libraryPath +
                                          "\n\nاضغط عشان تبدّل لمكتبة تانية أو تعمل واحدة جديدة");
        _toolTip.SetToolTip(_libraryProgress, stats);
        // السطر ده بيظهر بس لما فيه فلتر شغال
        bool filtered = _activeFilterText.Length > 0;
        _courseInfoLabel.Text = filtered ? $"🔎 فلتر شغال: {Modern.Bidi(_activeFilterText)}" : "";
        if (_infoRow != null && _infoRow.Visible != filtered) _infoRow.Visible = filtered;
        _libraryProgress.Value = videos == 0 ? 0 : (double)watched / videos;
    }

    private void UpdateStatusBar()
    {
        var g = _currentGroup ?? SelectedGroup();
        var c = g != null ? _library.FindCourseOf(g) : SelectedCourse();
        if (g == null && c == null && _tree.SelectedNode?.Tag is Place pl)
        {
            var pc = _library.AllCoursesUnder(pl).ToList();
            _statusGroup.Text = $"{_library.TopicOf(pl)?.Name} › {pl.Name}:  {pc.Count} كورس  ·  {pc.Sum(x => x.WatchedCount)} من {pc.Sum(x => x.VideoCount)} فيديو خلصوا";
            return;
        }
        if (g == null && c == null && _tree.SelectedNode?.Tag is Topic t)
        {
            var tc = _library.CoursesOf(t).ToList();
            _statusGroup.Text = $"{t.Name}:  {tc.Count} كورس  ·  {tc.Sum(x => x.WatchedCount)} من {tc.Sum(x => x.VideoCount)} فيديو خلصوا";
            return;
        }
        if (g != null)
            _statusGroup.Text = $"{c?.Name} › {g.Name}:  {g.WatchedCount} من {g.Videos.Count} خلصوا" +
                                (g.TotalDurationMs > 0 ? "  ·  " + TimeFormat.Format(g.TotalDurationMs) : "");
        else if (c != null)
            _statusGroup.Text = $"{c.Name}:  {c.WatchedCount} من {c.VideoCount} فيديو خلصوا";
        else
            _statusGroup.Text = "";
    }

    // =====================================================================
    //  الشجرة (3 مستويات)
    // =====================================================================

    private void RebuildTree()
    {
        _liveForm?.RefreshList(); // بعد تراجع أو مزامنة
        string filter = _searchBox.Text.Trim();
        object? selectedTag = _tree.SelectedNode?.Tag;
        int scrollTopIndex = _tree.TopNode != null ? GetVisibleIndex(_tree.TopNode) : 0;

        _rebuilding = true;
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            _tree.ClearRows();
            _videoNodes.Clear();
            _groupNodes.Clear();
            _courseNodes.Clear();
            _topicNodes.Clear();
            _placeNodes.Clear();

            var toExpand = new List<TreeNode>();
            bool filtering = filter.Length > 0 || _settings.CourseFilter != CourseFilter.All;

            TreeNode? BuildCourse(Course c, bool forceMatch)
            {
                if (!CourseFilter.Matches(c, _settings.CourseFilter)) return null;
                bool courseMatches = forceMatch || Matches(c.Name, filter) || c.Tags.Any(t => Matches("#" + t, filter));
                var cn = new TreeNode { Tag = c };
                var groupsToExpand = new List<TreeNode>();

                foreach (var g in c.Groups)
                {
                    bool groupMatches = courseMatches || Matches(g.Name, filter);
                    var videos = groupMatches ? g.Videos : g.Videos.Where(v => Matches(v.Title, filter)).ToList();
                    if (!groupMatches && videos.Count == 0) continue;

                    var gn = new TreeNode { Tag = g };
                    _groupNodes[g] = gn;
                    UpdateGroupNode(gn, g);
                    foreach (var v in videos)
                    {
                        var vn = new TreeNode { Tag = v };
                        _videoNodes[v] = vn;
                        UpdateVideoNode(vn, v);
                        gn.Nodes.Add(vn);
                    }
                    cn.Nodes.Add(gn);
                    if ((filter.Length > 0 && !forceMatch) || g.Expanded) groupsToExpand.Add(gn);
                }

                if (!courseMatches && cn.Nodes.Count == 0) return null;
                _courseNodes[c] = cn;
                UpdateCourseNode(cn, c);
                if ((filter.Length > 0 && !forceMatch) || c.Expanded) toExpand.Add(cn);
                toExpand.AddRange(groupsToExpand);
                return cn;
            }

            // مصدر (منصة، أكاديمية، قناة…) وتحته كورساته
            TreeNode? BuildPlace(Place p, bool forceMatch)
            {
                bool placeMatches = forceMatch || (filter.Length > 0 && Matches(p.Name, filter));
                var pn = new TreeNode { Tag = p };
                // المصادر الفرعية الأول (مثلًا المدرّسين جوه المنصة)، وبعدها الكورسات اللي فيه على طول
                foreach (var child in _library.ChildrenOf(p))
                    if (BuildPlace(child, placeMatches) is { } cpn) pn.Nodes.Add(cpn);
                foreach (var c in _library.CoursesInPlace(p))
                    if (BuildCourse(c, placeMatches) is { } cn) pn.Nodes.Add(cn);
                if (pn.Nodes.Count == 0 && filtering && !placeMatches) return null;
                _placeNodes[p] = pn;
                UpdatePlaceNode(pn, p);
                if ((filter.Length > 0 && !forceMatch) || p.Expanded) toExpand.Add(pn);
                return pn;
            }

            // المواضيع (التفريعة الرئيسية): جوه كل واحد أماكنه، وبعدها الكورسات اللي تحته على طول
            foreach (var t in _library.Topics)
            {
                bool topicMatches = filter.Length > 0 && Matches(t.Name, filter);
                var tn = new TreeNode { Tag = t };
                foreach (var p in _library.TopPlacesOf(t))
                    if (BuildPlace(p, topicMatches) is { } pn) tn.Nodes.Add(pn);
                foreach (var c in _library.DirectCoursesOf(t))
                    if (BuildCourse(c, topicMatches) is { } cn) tn.Nodes.Add(cn);
                if (tn.Nodes.Count == 0 && filtering && !topicMatches) continue;
                _topicNodes[t] = tn;
                UpdateTopicNode(tn, t);
                _tree.Nodes.Add(tn);
                if (filter.Length > 0 || t.Expanded) toExpand.Insert(0, tn);
            }

            // الأماكن والكورسات اللي برا أي موضوع
            foreach (var p in _library.TopPlacesOf(null))
                if (BuildPlace(p, false) is { } pn) _tree.Nodes.Add(pn);
            foreach (var c in _library.DirectCoursesOf(null))
                if (BuildCourse(c, false) is { } cn) _tree.Nodes.Add(cn);

            foreach (var n in toExpand) n.Expand();

            // نرجّع التحديد، بس من غير ما نفتح حاجة اتقفلت: ويندوز بيفتح كل اللي فوق العنصر المحدد عشان يبان،
            // فلو العنصر جوه موضوع/مصدر/كورس مقفول، التحديد بيروح للحاجة المقفولة نفسها (أعلى واحدة) بدل ما تتفتح لوحدها.
            if (selectedTag != null && NodeOf(selectedTag) is { } sel) _tree.SelectedNode = VisibleAncestorOrSelf(sel);
        }
        finally
        {
            _tree.EndUpdate();
            _rebuilding = false;
        }

        RestoreScroll(scrollTopIndex);
        UpdateLibraryHeader();
        UpdateStatusBar();
        RefreshHome();
    }

    /// <summary>العنصر نفسه لو كل اللي فوقه مفتوح، وإلا أعلى عنصر مقفول فوقه (اللي باين في الشجرة).</summary>
    private static TreeNode VisibleAncestorOrSelf(TreeNode node)
    {
        var result = node;
        for (var a = node.Parent; a != null; a = a.Parent)
            if (!a.IsExpanded) result = a;
        return result;
    }

    private static bool Matches(string text, string filter) =>
        filter.Length == 0 || text.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private int GetVisibleIndex(TreeNode node)
    {
        int i = 0;
        for (TreeNode? n = _tree.Nodes.Count > 0 ? _tree.Nodes[0] : null; n != null; n = n.NextVisibleNode, i++)
            if (n == node) return i;
        return 0;
    }

    private void RestoreScroll(int index)
    {
        TreeNode? n = _tree.Nodes.Count > 0 ? _tree.Nodes[0] : null;
        for (int i = 0; i < index && n?.NextVisibleNode != null; i++) n = n.NextVisibleNode;
        if (n != null)
        {
            try { _tree.TopNode = n; } catch { }
        }
    }

    private void UpdateCourseNode(TreeNode node, Course c)
    {
        node.Text = CourseMarks(c) + Modern.Bidi(c.Name);
        // رقم واحد بس: خلصت كام من كام (المدة في التلميح)
        _tree.SetRow(node, new LectureTreeView.RowInfo(_library.LinkOf(c) != null ? "🔄" : "", $"{c.WatchedCount}/{c.VideoCount}", c.IsCompleted));
        node.ImageKey = node.SelectedImageKey = NodeImageKey("course", c.IconKey, c.ColorHex, c.IsCompleted);
        bool current = _currentGroup != null && c.Groups.Contains(_currentGroup);
        node.ForeColor = current ? Theme.Accent : c.IsCompleted ? Theme.Success
            : c.Status is CourseStatus.Paused or CourseStatus.Planned ? Theme.Muted : CustomNameColor(c.ColorHex) ?? Theme.Text;
        node.NodeFont = null; // خط الشجرة (Bold)
        node.ToolTipText = CourseTooltip(c);
    }

    private void UpdateGroupNode(TreeNode node, LectureGroup g)
    {
        string clip = g.Attachments.Count > 0 ? $"   📎{g.Attachments.Count}" : "";
        node.Text = Modern.Bidi(g.Name) + clip;
        int fresh = g.NewCount;
        string meta = g.IsPlaylist ? "🔄" : "";
        _tree.SetRow(node, fresh > 0
            ? new LectureTreeView.RowInfo(meta, $"{fresh} جديد", Accent: true)
            : new LectureTreeView.RowInfo(meta, $"{g.WatchedCount}/{g.Videos.Count}", g.IsCompleted));
        node.ImageKey = node.SelectedImageKey = g.IsCompleted ? "groupDone" : "group";
        node.ForeColor = g == _currentGroup ? Theme.Accent : g.IsCompleted ? Theme.Success : Theme.Text;
        node.NodeFont = _groupNodeFont;
        node.ToolTipText = g.Name + (g.TotalDurationMs > 0 ? $"\n{g.Videos.Count} فيديو · المدة {TimeFormat.Format(g.TotalDurationMs)}" : "")
            + (g.Attachments.Count == 0 ? "" : "\n" + string.Join("\n", g.Attachments.Select(a => (a.IsLink ? "🔗 " : "📄 ") + a.Name)));
    }

    private void UpdateVideoNode(TreeNode node, VideoItem v)
    {
        bool exists = IsAvailable(v);
        bool playing = v == _currentVideo;

        // من غير أرقام في الشجرة: شريط تقدّم رفيع لو بدأته ومخلصتوش، والمدة في التلميح
        double progress = v.DurationMs > 0 && v.LastPositionMs > 0 && !v.Watched
            ? Math.Clamp((double)v.LastPositionMs / v.DurationMs, 0.02, 0.98) : -1;
        string time = v.DurationMs <= 0 ? ""
            : v.LastPositionMs > 0 && !v.Watched
                ? $"المدة {TimeFormat.Format(v.DurationMs)} — وقفت عند {TimeFormat.Format(v.LastPositionMs)}"
                : $"المدة {TimeFormat.Format(v.DurationMs)}";

        node.Text = v.Title;
        _tree.SetRow(node, new LectureTreeView.RowInfo("", v.IsNew ? "جديد" : null, Accent: v.IsNew, Progress: progress));
        node.NodeFont = playing ? _groupNodeFont : _regularNodeFont;
        node.ImageKey = node.SelectedImageKey =
            !exists ? "missing" : playing ? "playing" : v.Watched ? "watched" : v.LastPositionMs > 0 ? "partial"
            : v.IsOnline ? "online" : "video";
        node.ForeColor = !exists ? Theme.Danger : playing ? Theme.Accent : v.Watched ? Theme.Muted : Theme.Text;
        node.ToolTipText = v.IsOnline ? (IsDownloaded(v) ? "متنزّل للمشاهدة من غير نت:\n" : "من النت:\n") + v.Path
            : exists ? v.LocalPath
            : "الملف مش موجود على الجهاز ده:\n" + v.Path;
        if (time.Length > 0) node.ToolTipText = time + "\n" + node.ToolTipText;
        if (v.Bookmarks.Count > 0 || !string.IsNullOrWhiteSpace(v.Notes))
            node.ToolTipText += $"\n\n{v.Bookmarks.Count} علامة" + (string.IsNullOrWhiteSpace(v.Notes) ? "" : " · فيه ملاحظات");
    }

    private void RefreshVideo(VideoItem? v)
    {
        if (v != null && _videoNodes.TryGetValue(v, out var node)) UpdateVideoNode(node, v);
    }

    /// <summary>بيحدّث المحاضرة والكورس اللي هي فيه والعنوان وشريط الحالة.</summary>
    private void RefreshGroup(LectureGroup? g)
    {
        if (g != null)
        {
            if (_groupNodes.TryGetValue(g, out var node)) UpdateGroupNode(node, g);
            RefreshCourse(_library.FindCourseOf(g));
        }
        UpdateLibraryHeader();
        UpdateStatusBar();
    }

    private void RefreshCourse(Course? c)
    {
        if (c != null && _courseNodes.TryGetValue(c, out var node)) UpdateCourseNode(node, c);
        if (c != null && _library.PlaceOf(c) is { } p && _placeNodes.TryGetValue(p, out var pn))
        {
            UpdatePlaceNode(pn, p);
            if (_library.ParentOf(p) is { } pp && _placeNodes.TryGetValue(pp, out var ppn)) UpdatePlaceNode(ppn, pp);
        }
        if (c != null && _library.TopicOf(c) is { } t && _topicNodes.TryGetValue(t, out var tn)) UpdateTopicNode(tn, t);
    }

    private void OnNodeExpandChanged(TreeNode? node, bool expanded)
    {
        if (_rebuilding || node == null) return;
        switch (node.Tag)
        {
            case Topic t: t.Expanded = expanded; break;
            case Place p: p.Expanded = expanded; break;
            case Course c: c.Expanded = expanded; break;
            case LectureGroup g: g.Expanded = expanded; break;
            default: return;
        }
        MarkDirty();
    }

    private void SelectLastPlayed()
    {
        var g = _library.AllGroups.FirstOrDefault(x => x.Id == _library.LastGroupId);
        var v = g?.Videos.FirstOrDefault(x => string.Equals(x.Path, _library.LastVideoPath, StringComparison.OrdinalIgnoreCase));
        TreeNode? node = (v != null ? NodeOf(v) : null) ?? (g != null ? NodeOf(g) : null);
        if (node == null) return;
        _tree.SelectedNode = node;
        node.EnsureVisible();
        if (v != null)
        {
            var group = _library.FindGroupOf(v);
            ShowBanner($"آخر حاجة كنت بتتفرج عليها: {v.Title}", BannerKind.Info, "▶  كمّل",
                () => { if (group != null) PlayVideo(group, v, v.LastPositionMs); }, seconds: 12, key: "resume");
        }
    }

    private TreeNode? NodeOf(object item) => item switch
    {
        VideoItem v when _videoNodes.TryGetValue(v, out var n) => n,
        LectureGroup g when _groupNodes.TryGetValue(g, out var n) => n,
        Course c when _courseNodes.TryGetValue(c, out var n) => n,
        Topic t when _topicNodes.TryGetValue(t, out var n) => n,
        Place p when _placeNodes.TryGetValue(p, out var n) => n,
        _ => null
    };

    private void SelectItem(object item)
    {
        var node = NodeOf(item);
        if (node == null) return;
        _tree.SelectedNode = node;
        node.EnsureVisible();
    }

    private LectureGroup? SelectedGroup() => _tree.SelectedNode?.Tag switch
    {
        LectureGroup g => g,
        VideoItem v => _library.FindGroupOf(v),
        _ => null
    };

    private Course? SelectedCourse() => _tree.SelectedNode?.Tag switch
    {
        Course c => c,
        LectureGroup g => _library.FindCourseOf(g),
        VideoItem v => _library.FindCourseOf(v),
        _ => null
    };

    private VideoItem? SelectedVideo() => _tree.SelectedNode?.Tag as VideoItem;

    // =====================================================================
    //  الكورسات
    // =====================================================================

    private Course? AddCourse(string? name = null, Course? after = null, Topic? topicFor = null, bool atRoot = false, Place? placeFor = null)
    {
        if (!EnsureLibrary()) return null;
        name ??= Dialogs.Input(this, "كورس جديد", placeFor != null ? $"اسم الكورس (في «{placeFor.Name}»):" : "اسم الكورس:", $"كورس {_library.Courses.Count + 1}");
        if (name == null) return null;

        Checkpoint("إضافة كورس");
        Place? place = atRoot || topicFor != null ? null
            : placeFor ?? (after != null ? _library.PlaceOf(after) : SelectedPlaceContext());
        var topic = atRoot ? null : place != null ? _library.TopicOf(place)
            : topicFor ?? (after != null ? _library.TopicOf(after) : SelectedTopicContext());
        var c = new Course { Name = name, TopicId = topic?.Id, PlaceId = place?.Id };
        var siblings = place != null ? _library.CoursesInPlace(place) : _library.DirectCoursesOf(topic);
        after ??= SelectedCourse() is { } sc && siblings.Contains(sc) ? sc : siblings.LastOrDefault();
        after ??= place != null ? _library.CoursesOf(topic).LastOrDefault() : null;
        int index = after != null ? _library.Courses.IndexOf(after) + 1 : _library.Courses.Count;
        _library.Courses.Insert(Math.Clamp(index, 0, _library.Courses.Count), c);
        MarkDirty();
        RebuildTree();
        SelectItem(c);
        return c;
    }

    /// <summary>الكورس اللي هيتضاف فيه: المحدد، أو آخر كورس، أو يعمل واحد جديد.</summary>
    private Course? TargetCourseOrCreate()
    {
        var c = SelectedCourse() ?? _library.Courses.LastOrDefault();
        return c ?? AddCourse();
    }

    private void SortCourses()
    {
        Checkpoint("ترتيب الكورسات");
        _library.Courses = _library.Courses.OrderBy(c => c.Name, NaturalComparer.Instance).ToList();
        MarkDirty();
        RebuildTree();
    }

    private void MoveGroupToCourse(LectureGroup g, Course target, int? index = null)
    {
        var source = _library.FindCourseOf(g);
        if (source == null) return;
        Checkpoint("نقل محاضرة لكورس تاني");
        source.Groups.Remove(g);
        target.Groups.Insert(Math.Clamp(index ?? target.Groups.Count, 0, target.Groups.Count), g);
        target.Expanded = true;
        MarkDirty();
        RebuildTree();
        SelectItem(g);
    }

    // =====================================================================
    //  المحاضرات
    // =====================================================================

    private LectureGroup? AddGroup(string? name = null, LectureGroup? after = null, Course? course = null)
    {
        if (!EnsureLibrary()) return null;
        after ??= course == null ? SelectedGroup() : null;
        course ??= after != null ? _library.FindCourseOf(after) : TargetCourseOrCreate();
        if (course == null) return null;

        name ??= Dialogs.Input(this, "محاضرة جديدة", $"اسم المحاضرة (في كورس «{course.Name}»):", $"محاضرة {course.Groups.Count + 1}");
        if (name == null) return null;

        Checkpoint("إضافة محاضرة");
        var g = new LectureGroup { Name = name };
        int index = after != null && course.Groups.Contains(after) ? course.Groups.IndexOf(after) + 1 : course.Groups.Count;
        course.Groups.Insert(index, g);
        course.Expanded = true;
        MarkDirty();
        RebuildTree();
        SelectItem(g);
        return g;
    }

    private void RenameSelected()
    {
        switch (_tree.SelectedNode?.Tag)
        {
            case Topic t:
            {
                string? name = Dialogs.Input(this, "إعادة تسمية", "اسم الموضوع:", t.Name);
                if (name == null) return;
                Checkpoint("إعادة تسمية");
                t.Name = name;
                t.TouchInfo();
                break;
            }
            case Place p:
                RenamePlace(p);
                break;
            case Course c:
            {
                string? name = Dialogs.Input(this, "إعادة تسمية", "اسم الكورس:", c.Name);
                if (name == null) return;
                Checkpoint("إعادة تسمية");
                c.Name = name;
                break;
            }
            case LectureGroup g:
            {
                string? name = Dialogs.Input(this, "إعادة تسمية", "اسم المحاضرة:", g.Name);
                if (name == null) return;
                Checkpoint("إعادة تسمية");
                g.Name = name;
                break;
            }
            case VideoItem v:
            {
                string? name = Dialogs.Input(this, "إعادة تسمية", "الاسم اللي هيظهر في المكتبة (الملف نفسه مش هيتغير):", v.Title);
                if (name == null) return;
                Checkpoint("إعادة تسمية");
                v.Title = name;
                v.Touch();
                break;
            }
            default: return;
        }
        MarkDirty();
        RebuildTree();
        UpdateTitle();
        UpdateNowPlaying();
    }

    private void DeleteSelected()
    {
        switch (_tree.SelectedNode?.Tag)
        {
            case Topic t:
                DeleteTopic(t);
                return;

            case Place p:
                DeletePlace(p);
                return;

            case Course c:
                if (!Dialogs.Confirm(this, $"حذف الكورس «{c.Name}» من المكتبة؟\n" +
                                           $"({c.Groups.Count} محاضرة · {c.VideoCount} فيديو — الملفات نفسها مش هتتمسح من الجهاز)" +
                                           FollowedDeleteNote(c.SourceFolder)))
                    return;
                if (_currentGroup != null && c.Groups.Contains(_currentGroup)) StopPlaybackCore(clearCurrent: true);
                Checkpoint("حذف كورس");
                IgnoreInFollowed(c.SourceFolder);
                foreach (var cv in c.AllVideos) IgnoreInFollowed(cv.Path); // حتى اللي اتنقل له من فولدر تاني
                _deletedKeys.Add(LibrarySync.CourseKey(c));
                _library.Courses.Remove(c);
                break;

            case LectureGroup g:
                if (!Dialogs.Confirm(this, $"حذف المحاضرة «{g.Name}» من المكتبة؟\n({g.Videos.Count} فيديو — الملفات نفسها مش هتتمسح من الجهاز)" +
                                           FollowedDeleteNote(g.SourceFolder)))
                    return;
                if (g == _currentGroup) StopPlaybackCore(clearCurrent: true);
                var course = _library.FindCourseOf(g);
                Checkpoint("حذف محاضرة");
                // محاضرة باسم الكورس (فيديوهات على طول في فولدره) مسارها نفس مسار الكورس: نتجاهل فيديوهاتها بس
                if (!(g.SourceFolder != null && course?.SourceFolder != null && FolderLink.Norm(g.SourceFolder) == FolderLink.Norm(course.SourceFolder)))
                    IgnoreInFollowed(g.SourceFolder);
                foreach (var gv in g.Videos) IgnoreInFollowed(gv.Path); // وكل فيديو فيها (حتى اللي اتنقل لها من محاضرة تانية)
                _deletedKeys.Add(LibrarySync.GroupKey(g));
                course?.Groups.Remove(g);
                MarkDirty();
                RebuildTree();
                if (course != null) SelectItem(course);
                return;

            case VideoItem v:
                var owner = _library.FindGroupOf(v);
                if (owner == null) return;
                if (v == _currentVideo) StopPlaybackCore(clearCurrent: true);
                int i = owner.Videos.IndexOf(v);
                Checkpoint("حذف فيديو");
                IgnoreInFollowed(v.Path);
                _deletedKeys.Add(LibrarySync.VideoKey(v));
                owner.Videos.Remove(v);
                MarkDirty();
                RebuildTree();
                if (owner.Videos.Count > 0) SelectItem(owner.Videos[Math.Min(i, owner.Videos.Count - 1)]);
                else SelectItem(owner);
                return;

            default: return;
        }
        MarkDirty();
        RebuildTree();
    }

    private void ToggleWatchedSelected()
    {
        switch (_tree.SelectedNode?.Tag)
        {
            case VideoItem v:
                SetWatched(v, !v.Watched);
                break;
            case LectureGroup g:
            {
                bool all = !g.IsCompleted;
                foreach (var v in g.Videos) SetWatched(v, all, refresh: false);
                RebuildTree();
                break;
            }
            case Course c:
            {
                bool all = !c.IsCompleted;
                foreach (var v in c.AllVideos) SetWatched(v, all, refresh: false);
                RebuildTree();
                break;
            }
            case Topic t:
            {
                var vids = _library.CoursesOf(t).SelectMany(c => c.AllVideos).ToList();
                bool all = vids.Any(v => !v.Watched);
                if (all && !Dialogs.Confirm(this, $"تعليم كل فيديوهات «{t.Name}» ({vids.Count}) كمُشاهَدة؟")) return;
                foreach (var v in vids) SetWatched(v, all, refresh: false);
                RebuildTree();
                break;
            }
            case Place p:
            {
                var vids = _library.AllCoursesUnder(p).SelectMany(c => c.AllVideos).ToList();
                bool all = vids.Any(v => !v.Watched);
                if (all && !Dialogs.Confirm(this, $"تعليم كل فيديوهات «{p.Name}» ({vids.Count}) كمُشاهَدة؟")) return;
                foreach (var v in vids) SetWatched(v, all, refresh: false);
                RebuildTree();
                break;
            }
        }
    }

    private void SetWatched(VideoItem v, bool watched, bool refresh = true)
    {
        v.Watched = watched;
        if (!watched || v != _currentVideo) v.LastPositionMs = 0;
        v.Touch();
        MarkDirty();
        if (!refresh) return;
        RefreshVideo(v);
        RefreshGroup(_library.FindGroupOf(v));
    }

    private void ResetWatched(IEnumerable<VideoItem> videos, string what)
    {
        var list = videos.ToList();
        if (!Dialogs.Confirm(this, $"تصفير التقدم لـ {what}؟\n\n" +
                                   $"• {list.Count(v => v.Watched)} فيديو متعلّم «اتشاف» هيرجع «لسه»\n" +
                                   $"• {list.Count(v => v.LastPositionMs > 0)} فيديو هيبدأ من الأول بدل ما يكمّل\n\n" +
                                   "العلامات والملاحظات مش هتتمسح. وتقدر ترجع بـ Ctrl+Z.", "تصفير التقدم")) return;
        Checkpoint("تصفير التقدم");
        foreach (var v in list)
        {
            v.Watched = false;
            v.LastPositionMs = 0;
            v.Touch();
        }
        MarkDirty();
        SaveLibrary();
        RebuildTree();
        RefreshHome();
        ShowStatus($"اتصفّر التقدم لـ {what} ✓ (Ctrl+Z للتراجع)", 5);
    }

    private void MoveSelected(int direction)
    {
        object? item = _tree.SelectedNode?.Tag;
        Checkpoint("تحريك");
        switch (item)
        {
            case Topic t:
                if (!Swap(_library.Topics, t, direction)) return;
                break;
            case Place p:
            {
                var parentOfP = _library.ParentOf(p);
                var siblings = _library.PlacesOf(_library.TopicOf(p)).Where(x => _library.ParentOf(x) == parentOfP).ToList();
                int i = siblings.IndexOf(p), j = i + direction;
                if (i < 0 || j < 0 || j >= siblings.Count) return;
                int a = _library.Places.IndexOf(p), b = _library.Places.IndexOf(siblings[j]);
                (_library.Places[a], _library.Places[b]) = (_library.Places[b], _library.Places[a]);
                break;
            }
            case Course c:
                if (!SwapCourseInTopic(c, direction)) return;
                break;
            case LectureGroup g:
                if (_library.FindCourseOf(g) is not { } course || !Swap(course.Groups, g, direction)) return;
                break;
            case VideoItem v:
                if (_library.FindGroupOf(v) is not { } owner || !Swap(owner.Videos, v, direction)) return;
                break;
            default: return;
        }
        MarkDirty();
        RebuildTree();
        SelectItem(item);
    }

    private static bool Swap<T>(List<T> list, T item, int direction)
    {
        int i = list.IndexOf(item), j = i + direction;
        if (i < 0 || j < 0 || j >= list.Count) return false;
        (list[i], list[j]) = (list[j], list[i]);
        return true;
    }

    private void MoveVideoToGroup(VideoItem v, LectureGroup target)
    {
        var owner = _library.FindGroupOf(v);
        if (owner == null || owner == target) return;
        Checkpoint("نقل فيديو");
        owner.Videos.Remove(v);
        target.Videos.Add(v);
        target.Expanded = true;
        if (_library.FindCourseOf(target) is { } c) c.Expanded = true;
        if (v == _currentVideo) _currentGroup = target;
        MarkDirty();
        RebuildTree();
        SelectItem(v);
    }

    private void SortGroupVideos(LectureGroup? g)
    {
        if (g == null) { Dialogs.Info(this, "حدد محاضرة الأول."); return; }
        Checkpoint("ترتيب الفيديوهات");
        g.Videos = g.Videos.OrderBy(v => Path.GetFileName(v.Path), NaturalComparer.Instance).ToList();
        MarkDirty();
        RebuildTree();
    }

    private void SortGroups(Course? c)
    {
        if (c == null) { Dialogs.Info(this, "حدد كورس الأول."); return; }
        Checkpoint("ترتيب المحاضرات");
        c.Groups = c.Groups.OrderBy(g => g.Name, NaturalComparer.Instance).ToList();
        MarkDirty();
        RebuildTree();
    }

    // ---------- التقسيم ----------

    private void AutoSplitDialog(LectureGroup? g)
    {
        if (g == null) { Dialogs.Info(this, "حدد المحاضرة اللي عايز تقسمها الأول."); return; }
        var course = _library.FindCourseOf(g);
        if (course == null) return;

        var chunks = MediaFiles.SplitByPartReset(g.Videos);
        if (chunks.Count < 2)
        {
            Dialogs.Info(this, "ملقتش مكان الترقيم بيرجع فيه من الأول (زي Part 12 وبعدها Part 01)، فمفيش تقسيم تلقائي.\n\n" +
                               "تقدر تقسم يدوي: كليك يمين على أول فيديو في المحاضرة الجديدة ← «ابدأ محاضرة جديدة من هنا».");
            return;
        }

        string? prefix = Dialogs.Input(this, "تقسيم تلقائي",
            $"هتتقسم لـ {chunks.Count} محاضرات. اكتب بداية الاسم (هيتكتب بعده الرقم):", "محاضرة");
        if (prefix == null) return;

        int index = course.Groups.IndexOf(g);
        var newGroups = chunks.Select((videos, i) => new LectureGroup
        {
            Name = $"{prefix} {i + 1}",
            Videos = videos,
            Expanded = i == 0
        }).ToList();

        Checkpoint("تقسيم تلقائي");
        course.Groups.RemoveAt(index);
        course.Groups.InsertRange(index, newGroups);
        if (_currentGroup == g && _currentVideo != null) _currentGroup = _library.FindGroupOf(_currentVideo);

        MarkDirty();
        RebuildTree();
        SelectItem(newGroups[0]);
        ShowStatus($"اتقسمت لـ {newGroups.Count} محاضرات ✓");
    }

    private void SplitFromHere(VideoItem v)
    {
        var owner = _library.FindGroupOf(v);
        var course = owner != null ? _library.FindCourseOf(owner) : null;
        if (owner == null || course == null) return;
        int i = owner.Videos.IndexOf(v);
        if (i <= 0) { Dialogs.Info(this, "ده أول فيديو في المحاضرة أصلًا."); return; }

        string? name = Dialogs.Input(this, "محاضرة جديدة", "اسم المحاضرة الجديدة (هتبدأ من الفيديو ده):", $"محاضرة {course.Groups.Count + 1}");
        if (name == null) return;

        Checkpoint("تقسيم محاضرة");
        var moved = owner.Videos.Skip(i).ToList();
        owner.Videos.RemoveRange(i, moved.Count);
        var g = new LectureGroup { Name = name, Videos = moved };
        course.Groups.Insert(course.Groups.IndexOf(owner) + 1, g);
        if (_currentVideo != null && moved.Contains(_currentVideo)) _currentGroup = g;

        MarkDirty();
        RebuildTree();
        SelectItem(g);
    }

    // =====================================================================
    //  إضافة فيديوهات واستيراد فولدرات
    // =====================================================================

    private void AddVideosDialog(LectureGroup? target)
    {
        if (!EnsureLibrary()) return;
        using var dlg = new OpenFileDialog
        {
            Title = target == null ? "اختار فيديوهات المحاضرة الجديدة" : $"إضافة فيديوهات لـ «{target.Name}»",
            Filter = MediaFiles.DialogFilter,
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.FileNames.Length == 0) return;

        if (target == null)
        {
            var course = TargetCourseOrCreate();
            if (course == null) return;
            string folderName = new DirectoryInfo(Path.GetDirectoryName(dlg.FileNames[0])!).Name;
            target = AddGroup(folderName, course: course);
            if (target == null) return;
        }
        AddFiles(target, dlg.FileNames);
    }

    private void AddFiles(LectureGroup target, IEnumerable<string> files)
    {
        var existing = new HashSet<string>(target.Videos.Select(v => v.Path), StringComparer.OrdinalIgnoreCase);
        var added = files
            .Where(f => MediaFiles.IsMedia(f) && File.Exists(f))
            .Select(Path.GetFullPath)
            .Where(f => existing.Add(f))
            .OrderBy(Path.GetFileName, NaturalComparer.Instance)
            .Select(VideoItem.FromFile)
            .ToList();

        if (added.Count == 0) { ShowStatus("مفيش فيديوهات جديدة تتضاف"); return; }

        Checkpoint("إضافة فيديوهات");
        target.Videos.AddRange(added);
        target.Expanded = true;
        if (_library.FindCourseOf(target) is { } c) c.Expanded = true;
        MarkDirty();
        RebuildTree();
        SelectItem(added[0]);
        QueueProbe(added);
        ShowStatus($"اتضاف {added.Count} فيديو لـ «{target.Name}»");
    }

    private static string? PickFolder(IWin32Window owner, string description)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedPath : null;
    }

    /// <summary>فولدر كورس واحد ← كورس جديد (كل فولدر فرعي فيه فيديوهات = محاضرة).</summary>
    /// <summary>فولدر محاضرات يتضاف لكورس موجود.</summary>
    private void ImportIntoCourseDialog(Course course)
    {
        string? path = PickFolder(this, $"اختار فولدر محاضرات يتضاف لكورس «{course.Name}»");
        if (path != null) ImportFolder(path, into: course);
    }

    /// <param name="into">null = كورس جديد باسم الفولدر</param>
    private void ImportFolder(string root, Course? into)
    {
        root = Path.GetFullPath(root);
        List<LectureGroup> groups;
        Cursor = Cursors.WaitCursor;
        try { groups = MediaFiles.ScanFolder(root); }
        finally { Cursor = Cursors.Default; }

        if (groups.Count == 0)
        {
            Dialogs.Info(this, "ملقتش أي فيديوهات في الفولدر ده أو الفولدرات اللي جواه.");
            return;
        }

        // الفولدر شكله فيه أكتر من كورس (Course A\Lecture 1, Course B\Lecture 1 ...)
        if (into == null && MediaFiles.LooksLikeMultiCourse(groups))
        {
            int courseCount = MediaFiles.ScanMultiCourse(root).Count;
            if (Dialogs.Confirm(this,
                    $"الفولدر «{new DirectoryInfo(root).Name}» شكله فيه {courseCount} كورسات جواه.\n\n" +
                    "• نعم: كل فولدر جواه يبقى كورس لوحده، والفولدرات اللي جوه كل كورس تبقى محاضرات\n" +
                    "• لا: كله يبقى كورس واحد"))
            {
                ImportMultiCourse(root);
                return;
            }
        }

        Course course;
        Checkpoint("استيراد فولدر");
        if (into == null)
        {
            course = new Course { Name = new DirectoryInfo(root).Name, Groups = groups };
            PutInSelectedContext(course);
            _library.Courses.Add(course);
        }
        else
        {
            course = into;
            course.Groups.AddRange(groups);
            course.Expanded = true;
        }

        MarkDirty();
        RebuildTree();
        SelectItem(groups[0]);
        QueueProbe(groups.SelectMany(g => g.Videos));
        ShowStatus($"«{course.Name}»: اتضاف {groups.Count} محاضرة ({groups.Sum(g => g.Videos.Count)} فيديو) ✓", 5);

        // فولدر واحد مليان فيديوهات والترقيم بيرجع لـ Part 01 (زي اللي في PotPlayer) ← اعرض التقسيم
        if (groups.Count == 1)
        {
            int parts = MediaFiles.SplitByPartReset(groups[0].Videos).Count;
            if (parts > 1 && Dialogs.Confirm(this,
                    $"الفيديوهات دي كلها في فولدر واحد، بس الترقيم بيرجع لـ Part 01 أكتر من مرة.\n" +
                    $"تحب أقسمها تلقائيًا لـ {parts} محاضرات؟"))
            {
                SelectItem(groups[0]);
                AutoSplitDialog(groups[0]);
            }
        }
    }

    private void ImportMultiCourse(string root)
    {
        List<Course> courses;
        Cursor = Cursors.WaitCursor;
        try { courses = MediaFiles.ScanMultiCourse(root); }
        finally { Cursor = Cursors.Default; }

        if (courses.Count == 0)
        {
            Dialogs.Info(this, "ملقتش أي فيديوهات في الفولدر ده.");
            return;
        }

        Checkpoint("استيراد كورسات");
        string topicName = new DirectoryInfo(root).Name;
        if (Dialogs.Confirm(this, $"تحط الـ {courses.Count} كورس تحت موضوع جديد اسمه «{topicName}»؟\n\n(«لأ» = يتضافوا من غير موضوع، أو تحت الموضوع المحدد)", "موضوع"))
        {
            var t = new Topic { Name = topicName };
            t.TouchInfo();
            _library.Topics.Add(t);
            foreach (var c in courses) c.TopicId = t.Id;
        }
        else
            foreach (var c in courses) PutInSelectedContext(c);
        _library.Courses.AddRange(courses);
        MarkDirty();
        RebuildTree();
        SelectItem(courses[0]);
        QueueProbe(courses.SelectMany(c => c.AllVideos));
        ShowStatus($"اتضاف {courses.Count} كورس ({courses.Sum(c => c.Groups.Count)} محاضرة · {courses.Sum(c => c.VideoCount)} فيديو) ✓", 6);
    }

    // =====================================================================
    //  ملفات اتنقلت من مكانها
    // =====================================================================

    private void RelocateMissing(VideoItem v)
    {
        using var dlg = new OpenFileDialog
        {
            Title = $"فين الملف «{Path.GetFileName(v.Path)}» دلوقتي؟",
            Filter = MediaFiles.DialogFilter,
            FileName = Path.GetFileName(v.Path)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string oldDir = Path.GetDirectoryName(v.Path) ?? "";
        string newDir = Path.GetDirectoryName(dlg.FileName) ?? "";
        v.LocalPath = dlg.FileName;
        FileCache.Clear(); // مكان جديد: الفيديوهات التانية ممكن تكون اتنقلت معاه
        int fixedCount = 1;

        // المسار المكتوب في القائمة (Path) مش بيتغير عشان الأجهزة التانية؛
        // بنسجّل تحويل خاص بالجهاز ده، وبنطبّقه على باقي الملفات اللي كانت في نفس الفولدر
        if (oldDir.Length > 0 && !string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase))
        {
            _settings.PathMappings.RemoveAll(m => string.Equals(m.From, oldDir, StringComparison.OrdinalIgnoreCase));
            _settings.PathMappings.Insert(0, new PathMapping { From = oldDir, To = newDir });

            foreach (var other in _library.AllVideos)
            {
                if (other == v || IsAvailable(other)) continue;
                if (!other.Path.StartsWith(oldDir, StringComparison.OrdinalIgnoreCase)) continue;
                string candidate = newDir + other.Path.Substring(oldDir.Length);
                if (File.Exists(candidate)) { other.LocalPath = candidate; fixedCount++; }
            }
            Log.Info($"path mapping added: {oldDir} -> {newDir}");
        }

        MarkDirty();
        RebuildTree();
        ShowStatus(fixedCount > 1 ? $"اتصلّح مكان {fixedCount} ملف ✓" : "اتصلّح مكان الملف ✓");
    }

    // =====================================================================
    //  قراءة مدة الفيديوهات في الخلفية
    // =====================================================================

    /// <summary>فيديوهات فشلت قراية مدتها مرة: مش بنعيد المحاولة تاني في نفس التشغيلة.</summary>
    private readonly HashSet<string> _probeFailed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// مدة الفيديوهات اللي لسه مش معروفة، في الخلفية. مش بيقرا فيديوهات الدرايف السحابي (Google Drive/OneDrive/Dropbox):
    /// القراية هناك بتخلّي الدرايف ينزّل جزء من كل ملف (نت ومعالج وذاكرة مع كل فتح للبرنامج) — مدتها بتتعرف لما تشتغل.
    /// </summary>
    private void QueueProbe(IEnumerable<VideoItem> videos)
    {
        foreach (var v in videos)
            if (v.DurationMs <= 0 && !v.IsOnline && !LibraryFiles.IsCloudPath(v.LocalPath)
                && !_probeFailed.Contains(v.LocalPath) && FileCache.Exists(v.LocalPath))
                _probeQueue.Enqueue(v);
        if (!_probing && _probeQueue.Count > 0) _ = ProbeLoopAsync();
    }

    private async Task ProbeLoopAsync()
    {
        _probing = true;
        try
        {
            int sinceRefresh = 0;
            var touchedGroups = new HashSet<LectureGroup>();
            while (_probeQueue.Count > 0 && !_closing)
            {
                // وإنت بتتفرج: نستنى (القراية بتنافس الفيديو الشغال على الديسك والمعالج)
                while (IsActive && _mp.State == VLCState.Playing && !_closing) await Task.Delay(2000);
                var v = _probeQueue.Dequeue();
                if (v.DurationMs > 0) continue;
                try
                {
                    using var media = new Media(_libVLC, v.LocalPath, FromType.FromPath);
                    await media.Parse(MediaParseOptions.ParseLocal, 5000);
                    if (_closing) return;
                    if (media.Duration <= 0) _probeFailed.Add(v.LocalPath);
                    if (media.Duration > 0)
                    {
                        v.DurationMs = media.Duration;
                        MarkDirty();
                        RefreshVideo(v);
                        if (_library.FindGroupOf(v) is { } g) touchedGroups.Add(g);
                    }
                }
                catch { _probeFailed.Add(v.LocalPath); /* ملف بايظ أو مش مدعوم — هتتعرف مدته لما يشتغل */ }
                await Task.Delay(30); // نسيب نَفَس للواجهة

                if (++sinceRefresh >= 10 || _probeQueue.Count == 0)
                {
                    foreach (var g in touchedGroups) RefreshGroup(g);
                    touchedGroups.Clear();
                    sinceRefresh = 0;
                }
            }
        }
        finally { _probing = false; }
    }

    // =====================================================================
    //  القوائم (كليك يمين)
    // =====================================================================

    private void ShowNodeMenu(TreeNode node, Point location)
    {
        ClearNodeMenu();
        var items = _nodeMenu.Items;

        switch (node.Tag)
        {
            case Topic t:
                BuildTopicMenu(t, items);
                break;

            case Place p:
                BuildPlaceMenu(p, items);
                break;

            case Course c:
                items.Add(MenuItem("▶  تشغيل / استكمال الكورس", () => PlayCourse(c)));
                if (_library.LinkOf(c) != null) items.Add(MenuItem("🔄  هات الجديد من الفولدر", () => _ = SyncLinkedFoldersAsync(c, manual: true)));
                items.Add(new ToolStripSeparator());
                items.Add(SubMenuOf("➕  إضافة للكورس ده",
                    MenuItem("محاضرة جديدة فاضية…", () => AddGroup(course: c)),
                    MenuItem("فولدر محاضرات من الجهاز…", () => ImportIntoCourseDialog(c)),
                    MenuItem("رابط يوتيوب أو قائمة تشغيل…", () => AddUrlsDialog(null), "Ctrl+U")));
                items.Add(MenuItem("إعادة تسمية…", RenameSelected, "F2"));
                items.Add(AppearanceMenuItem(c));
                items.Add(ExportMenuItem(c));
                if (c.AllVideos.Any(v => v.IsDownloadable))
                    items.Add(MenuItem("⬇  تنزيل الكورس كله (فيديو أو MP3)…", () => DownloadCourse(c)));
                if (c.UdemyId != null)
                    items.Add(MenuItem("🎓  حدّث من يوديمي (الدروس الجديدة واللي خلّصته)", () => UdemyImport(c)));
                items.Add(MoveCourseMenu(c));
                items.Add(FoldMenu(c));
                if (AdvancedMode) items.Add(MenuItem("بيانات الكورس (الحالة، الموعد، الوسوم)…", () => EditCourseInfo(c), "F4"));
                {
                    var more = new List<ToolStripItem>();
                    if (_library.LinkOf(c) is { } link)
                    {
                        more.Add(MenuItem("📂  فتح الفولدر", () => OpenFolder(c.SourceFolder ?? link.Path)));
                        more.Add(MenuItem("بطّل متابعة الفولدر (الكورسات بتفضل)", () => StopFollowing(link)));
                        more.Add(new ToolStripSeparator());
                    }
                    more.Add(MenuItem(c.IsCompleted ? "إلغاء علامة المشاهدة للكورس كله" : "تعليم الكورس كله كمُشاهَد", ToggleWatchedSelected, "W"));
                    more.Add(MenuItem("↺ تصفير التقدم في الكورس…", () => ResetWatched(c.AllVideos, $"كورس «{c.Name}»")));
                    more.Add(MenuItem("ترتيب المحاضرات بالاسم", () => SortGroups(c)));
                    more.Add(MenuItem("حفظ ملاحظات الكورس في ملف…", () => ExportNotesDialog(c), "Ctrl+E"));
                    more.Add(new ToolStripSeparator());
                    more.Add(MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"));
                    more.Add(MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"));
                    more.Add(MenuItem("كورس جديد فاضي بعده…", () => AddCourse(after: c)));
                    items.Add(SubMenuOf("المزيد", more.ToArray()));
                }
                items.Add(new ToolStripSeparator());
                items.Add(MenuItem("حذف الكورس من المكتبة", DeleteSelected, "Delete"));
                break;

            case LectureGroup g:
            {
                var course = _library.FindCourseOf(g);
                items.Add(MenuItem("▶  تشغيل المحاضرة", () => PlayGroup(g)));
                items.Add(MenuItem("تشغيل من أول فيديو", () => { if (g.Videos.Count > 0) PlayVideo(g, g.Videos[0], 0); }));
                if (g.Attachments.Count > 0) items.Add(AttachmentsMenu(g));
                if (g.IsPlaylist) items.Add(MenuItem("🔄  هات الجديد من يوتيوب", () => _ = SyncPlaylistsAsync(new[] { g }, quiet: false)));
                if (g.NewCount > 0) items.Add(MenuItem($"شيل علامة «جديد» ({g.NewCount})", () => ClearNew(g)));
                items.Add(new ToolStripSeparator());
                items.Add(SubMenuOf("➕  إضافة للمحاضرة دي",
                    MenuItem("فيديوهات من الجهاز…", () => AddVideosDialog(g)),
                    MenuItem("رابط يوتيوب…", () => AddUrlsDialog(null), "Ctrl+U"),
                    MenuItem("مرفقات (PDF، سلايدات، كود)…", () => AddAttachmentFiles(g))));
                items.Add(MenuItem("إعادة تسمية…", RenameSelected, "F2"));

                var others = _library.Courses.Where(x => x != course).ToList();
                var moveTo = new ToolStripMenuItem("نقل لكورس تاني") { ForeColor = Theme.Text };
                foreach (var target in others)
                {
                    var t = target;
                    moveTo.DropDownItems.Add(MenuItem(t.Name, () => MoveGroupToCourse(g, t)));
                }
                if (others.Count > 0) moveTo.DropDownItems.Add(new ToolStripSeparator());
                moveTo.DropDownItems.Add(MenuItem("كورس جديد…", () =>
                {
                    var nc = AddCourse(after: course);
                    if (nc != null) MoveGroupToCourse(g, nc);
                }));
                items.Add(moveTo);

                var more = new List<ToolStripItem>
                {
                    MenuItem(g.IsCompleted ? "إلغاء علامة المشاهدة للكل" : "تعليم الكل كمُشاهَد", ToggleWatchedSelected, "W"),
                    MenuItem("↺ تصفير التقدم في المحاضرة…", () => ResetWatched(g.Videos, $"محاضرة «{g.Name}»")),
                    MenuItem("ترتيب الفيديوهات بالاسم", () => SortGroupVideos(g))
                };
                if (AdvancedMode) more.Add(MenuItem("تقسيم تلقائي (عند Part 01)…", () => AutoSplitDialog(g)));
                if (g.Videos.Any(x => x.IsDownloadable))
                    more.Add(MenuItem("⬇  تنزيل فيديوهات يوتيوب (فيديو أو MP3)…", () => DownloadFromLibrary(g.Videos, g.Name)));
                if (g.IsUdemySection) { }
                else if (g.SourceUrl != null)
                    more.Add(MenuItem("فك الربط بقائمة يوتيوب", () => { Checkpoint("فك الربط"); g.SourceUrl = null; g.InfoUpdatedUtc = DateTime.UtcNow; MarkDirty(); RefreshGroup(g); }));
                else more.Add(MenuItem("ربط بقائمة تشغيل يوتيوب (عشان الجديد يتضاف)…", () => LinkPlaylist(g)));
                more.Add(new ToolStripSeparator());
                more.Add(MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"));
                more.Add(MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"));
                more.Add(MenuItem("محاضرة جديدة فاضية بعدها…", () => AddGroup(after: g)));
                items.Add(SubMenuOf("المزيد", more.ToArray()));

                items.Add(new ToolStripSeparator());
                items.Add(MenuItem("حذف المحاضرة من المكتبة", DeleteSelected, "Delete"));
                break;
            }

            case VideoItem v:
            {
                var owner = _library.FindGroupOf(v);
                bool exists = IsAvailable(v);

                if (exists)
                {
                    items.Add(MenuItem("▶  تشغيل", () => PlayNode(node, fromStart: false), "Enter"));
                    if (v.LastPositionMs > 0) items.Add(MenuItem("تشغيل من البداية", () => PlayNode(node, fromStart: true)));
                }
                else
                {
                    items.Add(MenuItem("الملف مش موجود — حدد مكانه الجديد…", () => RelocateMissing(v)));
                }
                items.Add(MenuItem(v.Watched ? "تعليم كغير مُشاهَد" : "تعليم كمُشاهَد", () => SetWatched(v, !v.Watched), "W"));
                items.Add(new ToolStripSeparator());
                items.Add(MenuItem("إعادة تسمية…", RenameSelected, "F2"));
                items.Add(BuildMoveVideoMenu(v, owner));

                var more = new List<ToolStripItem>();
                if (v.Watched || v.LastPositionMs > 0)
                    more.Add(MenuItem("↺ تصفير التقدم (يبدأ من الأول)", () => ResetWatched(new[] { v }, $"«{v.Title}»")));
                more.Add(MenuItem("ابدأ محاضرة جديدة من هنا…", () => SplitFromHere(v)));
                more.Add(MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"));
                more.Add(MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"));
                more.Add(new ToolStripSeparator());
                if (v.IsOnline)
                {
                    if (IsDownloaded(v)) more.Add(MenuItem("مسح النسخة المتنزلة", () => RemoveOffline(v)));
                    else if (!v.IsUdemy) more.Add(MenuItem("⬇  تنزيل (فيديو أو MP3)…", () => DownloadFromLibrary(new[] { v }, v.Title)));
                    more.Add(MenuItem("فتح في المتصفح", () => OpenInBrowser(v.Path)));
                    more.Add(MenuItem("نسخ الرابط", () => { try { Clipboard.SetText(v.Path); } catch { } }));
                }
                else if (exists) more.Add(MenuItem("فتح مكان الملف", () => OpenInExplorer(v.LocalPath)));
                items.Add(SubMenuOf("المزيد", more.ToArray()));
                items.Add(new ToolStripSeparator());
                items.Add(MenuItem("إزالة من المكتبة", DeleteSelected, "Delete"));
                break;
            }
        }

        _nodeMenu.Show(_tree, location);
    }

    /// <summary>«نقل إلى محاضرة»: محاضرات نفس الكورس الأول، وبعدها كل كورس تاني في قايمة فرعية.</summary>
    private ToolStripMenuItem BuildMoveVideoMenu(VideoItem v, LectureGroup? owner)
    {
        var moveTo = new ToolStripMenuItem("نقل إلى محاضرة") { ForeColor = Theme.Text };
        var ownCourse = owner != null ? _library.FindCourseOf(owner) : null;

        if (ownCourse != null)
        {
            foreach (var target in ownCourse.Groups.Where(x => x != owner))
            {
                var t = target;
                moveTo.DropDownItems.Add(MenuItem(t.Name, () => MoveVideoToGroup(v, t)));
            }
            moveTo.DropDownItems.Add(MenuItem("محاضرة جديدة…", () =>
            {
                var ng = AddGroup(after: owner);
                if (ng != null) MoveVideoToGroup(v, ng);
            }));
        }

        var otherCourses = _library.Courses.Where(c => c != ownCourse && c.Groups.Count > 0).ToList();
        if (otherCourses.Count > 0) moveTo.DropDownItems.Add(new ToolStripSeparator());
        foreach (var course in otherCourses)
        {
            var sub = new ToolStripMenuItem(course.Name) { ForeColor = Theme.Text };
            foreach (var target in course.Groups)
            {
                var t = target;
                sub.DropDownItems.Add(MenuItem(t.Name, () => MoveVideoToGroup(v, t)));
            }
            moveTo.DropDownItems.Add(sub);
        }
        return moveTo;
    }

    private void ShowEmptyAreaMenu(Point location)
    {
        ClearNodeMenu();
        _nodeMenu.Items.Add(MenuItem("📁  إضافة كورس من فولدر…", () =>
        {
            string? path = PickFolder(this, "اختار فولدر الكورس (أو الفولدر اللي فيه كذا كورس)");
            if (path != null) ShowAddFolderDialog(path, useSelection: false);
        }, "Ctrl+I"));
        _nodeMenu.Items.Add(MenuItem("🔗  إضافة من يوتيوب…", () => AddUrlsDialog(null), "Ctrl+U"));
        _nodeMenu.Items.Add(new ToolStripSeparator());
        _nodeMenu.Items.Add(MenuItem("موضوع جديد…", () => AddTopic(), "Ctrl+Shift+N"));
        _nodeMenu.Items.Add(new ToolStripSeparator());
        _nodeMenu.Items.Add(MenuItem("فتح الكل", () => SetAllExpanded(true)));
        _nodeMenu.Items.Add(MenuItem("قفل الكل", () => SetAllExpanded(false)));
        _nodeMenu.Show(_tree, location);
    }

    private void ClearNodeMenu()
    {
        var old = _nodeMenu.Items.Cast<ToolStripItem>().ToList();
        _nodeMenu.Items.Clear();
        foreach (var item in old) item.Dispose();
    }

    /// <summary>قايمة فرعية (زي «المزيد» أو «إضافة»): بتلم الأوامر اللي مش بتتستخدم كل يوم.</summary>
    private static ToolStripMenuItem SubMenuOf(string text, params ToolStripItem[] items)
    {
        var m = new ToolStripMenuItem(text) { ForeColor = Theme.Text };
        // من غير فواصل في الأول أو الآخر أو ورا بعض
        var list = items.ToList();
        while (list.Count > 0 && list[^1] is ToolStripSeparator) list.RemoveAt(list.Count - 1);
        for (int i = 0; i < list.Count; i++)
            if (list[i] is ToolStripSeparator && (i == 0 || list[i - 1] is ToolStripSeparator)) continue;
            else m.DropDownItems.Add(list[i]);
        return m;
    }

    private static ToolStripMenuItem MenuItem(string text, Action onClick, string? shortcut = null)
    {
        var item = new ToolStripMenuItem(text) { ForeColor = Theme.Text };
        if (shortcut != null) item.ShortcutKeyDisplayString = shortcut;
        item.Click += (_, _) => onClick();
        return item;
    }

    // =====================================================================
    //  السحب والإفلات
    // =====================================================================

    private void OnTreeItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Button == MouseButtons.Left && e.Item is TreeNode node && _searchBox.Text.Length == 0)
            _tree.DoDragDrop(node, DragDropEffects.Move);
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data == null) { e.Effect = DragDropEffects.None; return; }

        Point p = _tree.PointToClient(new Point(e.X, e.Y));
        TreeNode? target = _tree.GetNodeAt(p);

        // سكرول تلقائي لما تقرب من فوق أو تحت
        int edge = LogicalToDeviceUnits(24);
        if (p.Y < edge) target?.PrevVisibleNode?.EnsureVisible();
        else if (p.Y > _tree.ClientSize.Height - edge) target?.NextVisibleNode?.EnsureVisible();

        if (e.Data.GetDataPresent(DataFormats.FileDrop) || TryGetDroppedUrls(e.Data).Count > 0)
        {
            e.Effect = DragDropEffects.Copy;
            if (target != null) _tree.SelectedNode = target;
        }
        else if (e.Data.GetDataPresent(typeof(TreeNode)))
        {
            e.Effect = DragDropEffects.Move;
            if (target != null) _tree.SelectedNode = target;
        }
        else e.Effect = DragDropEffects.None;
    }

    private void OnTreeDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data == null) return;
        Point p = _tree.PointToClient(new Point(e.X, e.Y));
        TreeNode? target = _tree.GetNodeAt(p);

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            OnExternalDrop(e, target?.Tag);
            return;
        }

        var droppedUrls = TryGetDroppedUrls(e.Data);
        if (droppedUrls.Count > 0)
        {
            _ = AddUrlsAsync(droppedUrls, target?.Tag, playFirst: false);
            return;
        }

        if (e.Data.GetData(typeof(TreeNode)) is not TreeNode dragged || dragged == target) return;

        // فيديو أو محاضرة اتسابوا على موضوع ← آخر كورس في الموضوع ده
        object? targetTag = target?.Tag;
        if (targetTag is Topic tt && dragged.Tag is VideoItem or LectureGroup)
        {
            targetTag = _library.CoursesOf(tt).LastOrDefault();
            if (targetTag == null) { ShowStatus("الموضوع ده فاضي — ضيف فيه كورس الأول"); return; }
        }
        if (targetTag is Place tp && dragged.Tag is VideoItem or LectureGroup)
        {
            targetTag = _library.CoursesInPlace(tp).LastOrDefault() ?? _library.AllCoursesUnder(tp).LastOrDefault();
            if (targetTag == null) { ShowStatus("المصدر ده فاضي — ضيف فيه كورس الأول"); return; }
        }

        switch (dragged.Tag)
        {
            case VideoItem video: DropVideo(video, targetTag); break;
            case LectureGroup group: DropGroup(group, targetTag); break;
            case Course course: DropCourse(course, target?.Tag); break;
            case Topic topic: DropTopic(topic, target?.Tag); break;
            case Place place: DropPlace(place, target?.Tag); break;
        }
    }

    private void DropVideo(VideoItem video, object? target)
    {
        var source = _library.FindGroupOf(video);
        if (source == null) return;
        Checkpoint("سحب وإفلات");
        int sourceIndex = source.Videos.IndexOf(video);

        LectureGroup? destination;
        int insertAt;

        switch (target)
        {
            case VideoItem tv:            // على فيديو = قبله (لو طالع لفوق) أو بعده (لو نازل لتحت)
            {
                destination = _library.FindGroupOf(tv);
                if (destination == null) return;
                int originalTargetIndex = destination.Videos.IndexOf(tv);
                source.Videos.Remove(video);
                int targetIndex = destination.Videos.IndexOf(tv);
                insertAt = destination == source && sourceIndex < originalTargetIndex ? targetIndex + 1 : targetIndex;
                break;
            }
            case LectureGroup g:          // على محاضرة = في آخرها
                destination = g;
                source.Videos.Remove(video);
                insertAt = g.Videos.Count;
                break;

            case Course c:                // على كورس = آخر محاضرة فيه (أو محاضرة جديدة لو فاضي)
                destination = c.Groups.LastOrDefault();
                if (destination == null)
                {
                    destination = new LectureGroup { Name = "محاضرة 1" };
                    c.Groups.Add(destination);
                }
                source.Videos.Remove(video);
                insertAt = destination.Videos.Count;
                break;

            default:
                return;
        }

        destination.Videos.Insert(Math.Clamp(insertAt, 0, destination.Videos.Count), video);
        destination.Expanded = true;
        if (_library.FindCourseOf(destination) is { } dc) dc.Expanded = true;
        if (video == _currentVideo) _currentGroup = destination;

        MarkDirty();
        RebuildTree();
        SelectItem(video);
    }

    private void DropGroup(LectureGroup group, object? target)
    {
        var sourceCourse = _library.FindCourseOf(group);
        if (sourceCourse == null) return;

        Course destination;
        LectureGroup? anchor = null;
        switch (target)
        {
            case Course c: destination = c; break;                                  // على كورس = في آخره
            case LectureGroup g: anchor = g; destination = _library.FindCourseOf(g)!; break;
            case VideoItem v: anchor = _library.FindGroupOf(v); destination = _library.FindCourseOf(v)!; break;
            default: destination = _library.Courses[^1]; break;                     // مكان فاضي = آخر كورس
        }
        if (anchor == group) return;

        Checkpoint("سحب وإفلات");
        int from = sourceCourse.Groups.IndexOf(group);
        int originalAnchor = anchor != null ? destination.Groups.IndexOf(anchor) : -1;
        sourceCourse.Groups.Remove(group);

        int to;
        if (anchor == null) to = destination.Groups.Count;
        else
        {
            to = destination.Groups.IndexOf(anchor);
            if (destination == sourceCourse && from < originalAnchor) to++; // نازل لتحت ← بعد اللي اتساب عليه
        }
        destination.Groups.Insert(Math.Clamp(to, 0, destination.Groups.Count), group);
        destination.Expanded = true;

        MarkDirty();
        RebuildTree();
        SelectItem(group);
    }

    /// <summary>
    /// كورس اتساب على موضوع ← آخر الكورسات اللي تحته على طول. على مكان ← آخر كورسات المكان.
    /// على كورس تاني ← ياخد موضوعه ومكانه ويتحط جنبه. في مكان فاضي ← يطلع برا أي موضوع في الآخر.
    /// </summary>
    private void DropCourse(Course course, object? target)
    {
        Topic? topic;
        Place? place = null;
        Course? anchor = null;
        switch (target)
        {
            case Topic t: topic = t; break;
            case Place p: place = p; topic = _library.TopicOf(p); break;
            case Course c: anchor = c; break;
            case LectureGroup g: anchor = _library.FindCourseOf(g); break;
            case VideoItem v: anchor = _library.FindCourseOf(v); break;
            default: topic = null; break;
        }
        if (anchor == course) return;
        if (anchor != null) { topic = _library.TopicOf(anchor); place = _library.PlaceOf(anchor); }
        else topic = place != null ? _library.TopicOf(place) : target is Topic tt ? tt : null;

        Checkpoint("سحب وإفلات");
        if (anchor == null)
        {
            PlaceCourseAtEnd(course, topic, place);
        }
        else
        {
            int from = _library.Courses.IndexOf(course);
            int originalAnchor = _library.Courses.IndexOf(anchor);
            _library.Courses.Remove(course);
            if (course.TopicId != topic?.Id || course.PlaceId != place?.Id) _library.MoveCourse(course, topic, place);
            int to = _library.Courses.IndexOf(anchor) + (from < originalAnchor ? 1 : 0);
            _library.Courses.Insert(Math.Clamp(to, 0, _library.Courses.Count), course);
        }

        MarkDirty();
        RebuildTree();
        SelectItem(course);
    }

    private void OnExternalDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true || TryGetDroppedUrls(e.Data).Count > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    /// <summary>
    /// ملفات أو فولدرات جاية من الويندوز.
    /// فولدر على مكان فاضي = كورس جديد · فولدر على كورس/محاضرة = محاضرات جديدة في الكورس ده.
    /// ملفات على محاضرة = تتضاف لها · ملفات على كورس = محاضرة جديدة فيه.
    /// </summary>
    private void OnExternalDrop(DragEventArgs e, object? targetTag)
    {
        // ملف مكتبة (.lpl) اتساب على البرنامج ← تتفتح
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] dropped &&
            dropped.FirstOrDefault(p => File.Exists(p) && p.EndsWith(Storage.LibraryExtension, StringComparison.OrdinalIgnoreCase)) is { } libFile)
        {
            SwitchLibrary(libFile);
            return;
        }
        if (!EnsureLibrary()) return;
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            var urls = TryGetDroppedUrls(e.Data);
            if (urls.Count > 0) _ = AddUrlsAsync(urls, targetTag, playFirst: targetTag == null);
            return;
        }

        Course? targetCourse = targetTag switch
        {
            Course c => c,
            LectureGroup g => _library.FindCourseOf(g),
            VideoItem v => _library.FindCourseOf(v),
            _ => null
        };
        LectureGroup? targetGroup = targetTag switch
        {
            LectureGroup g => g,
            VideoItem v => _library.FindGroupOf(v),
            _ => null
        };

        foreach (string dir in paths.Where(Directory.Exists))
        {
            if (targetCourse != null) ImportFolder(dir, into: targetCourse);       // على كورس ← محاضرات جديدة فيه
            else ShowAddFolderDialog(dir, targetTag as Topic, targetTag as Place,    // على موضوع/مصدر/فاضي ← شاشة الإضافة
                useSelection: false);
        }

        var files = paths.Where(p => File.Exists(p) && MediaFiles.IsMedia(p)).ToList();
        if (files.Count == 0) return;

        if (targetGroup == null)
        {
            string name = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(files[0]))!).Name;
            if (targetCourse == null)
            {
                targetCourse = new Course { Name = name };
                PutInSelectedContext(targetCourse);
                _library.Courses.Add(targetCourse);
            }
            targetGroup = new LectureGroup { Name = name };
            targetCourse.Groups.Add(targetGroup);
        }
        AddFiles(targetGroup, files);
    }
}
