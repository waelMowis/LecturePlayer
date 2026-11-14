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
        _libraryPath = path;
        InitJournal(path);
        _dirty = lib == null;
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
            InitialDirectory = Path.GetDirectoryName(_libraryPath)
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) SwitchLibrary(dlg.FileName);
    }

    private void SaveLibraryAsDialog()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "حفظ المكتبة باسم",
            Filter = Storage.LibraryFilter,
            DefaultExt = Storage.LibraryExtension,
            FileName = _library.Name + Storage.LibraryExtension,
            InitialDirectory = Path.GetDirectoryName(_libraryPath)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string target = dlg.FileName;
        if (string.Equals(target, _libraryPath, StringComparison.OrdinalIgnoreCase)) { SaveLibrary(); ShowStatus("اتحفظ ✓"); return; }

        try
        {
            // الحفظ باسم بيكتب القائمة الحالية زي ما هي. (قبل كده كان بيدمجها مع الملف اللي اخترته لو كان موجود،
            // فلو حفظت فوق قائمة تانية، الاتنين كانوا بيتخلطوا في بعض)
            SaveCurrentPosition();
            CommitNotes();
            MergeExternalChanges();
            Storage.SaveLibrary(target, _library);
        }
        catch (Exception ex)
        {
            Log.Error("save-as", ex);
            Dialogs.Error(this, "مقدرتش أحفظ المكتبة هنا:\n" + target + "\n\n" + ex.Message);
            return;
        }

        _libraryPath = target;
        _libraryStampUtc = File.GetLastWriteTimeUtc(target);
        _dirty = false;
        _deletedKeys.Clear();
        InitJournal(target); // سجل المذاكرة يمشي مع القائمة الجديدة
        _settings.LastLibraryPath = target;
        AddRecent(target);
        SaveSettings();
        _statusFile.Text = Path.GetFileName(target);
        _statusFile.ToolTipText = target;
        UpdateTitle();
        Log.Info("library saved as: " + target);
        ShowStatus("اتحفظت باسم " + Path.GetFileName(target) + " ✓", 5);
    }

    /// <summary>
    /// المزامنة بين الأجهزة: نحط ملف القائمة في OneDrive، وعلى الجهاز التاني نفتحه من «فتح قائمة».
    /// </summary>
    private void MoveLibraryToCloudDialog()
    {
        string? oneDrive = new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(d => !string.IsNullOrEmpty(d) && Directory.Exists(d));

        if (!Dialogs.Confirm(this,
                "عشان تكمّل مذاكرتك على أكتر من جهاز:\n\n" +
                "1. ملف المكتبة هيتحفظ في فولدر بيتزامن (OneDrive أو Google Drive).\n" +
                "2. على الجهاز التاني: ملف ← فتح مكتبة، واختار نفس الملف.\n" +
                "3. البرنامج بيدمج التقدم والعلامات والملاحظات من الجهازين لوحده.\n\n" +
                "الفيديوهات نفسها مش بتتنقل. لو مكانها مختلف على الجهاز التاني، حدد مكان أي فيديو مرة واحدة، " +
                "والبرنامج هيفتكر المكان ده للجهاز ده بس.\n\n" +
                (oneDrive != null ? "لقيت OneDrive هنا:\n" + oneDrive : "ملقتش OneDrive على الجهاز ده، اختار أي فولدر بيتزامن.") +
                "\n\nنكمّل؟", "مزامنة بين الأجهزة"))
            return;

        string initial = oneDrive != null ? Path.Combine(oneDrive, "مشغل المحاضرات") : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        try { Directory.CreateDirectory(initial); } catch { }

        using var dlg = new SaveFileDialog
        {
            Title = "حفظ المكتبة في فولدر المزامنة",
            Filter = Storage.LibraryFilter,
            DefaultExt = Storage.LibraryExtension,
            FileName = _library.Name + Storage.LibraryExtension,
            InitialDirectory = initial
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _libraryPath = dlg.FileName;
        _settings.LastLibraryPath = _libraryPath;
        AddRecent(_libraryPath);
        _libraryStampUtc = DateTime.MinValue;
        SaveLibrary();
        _statusFile.Text = Path.GetFileName(_libraryPath);
        _statusFile.ToolTipText = _libraryPath;
        Log.Info("library moved to sync folder: " + _libraryPath);
        ShowStatus("المكتبة بقت في فولدر المزامنة ✓ — افتحها من الجهاز التاني", 8);
    }

    private void RestoreBackupDialog()
    {
        string dir = Storage.BackupDir(_libraryPath);
        if (!Directory.Exists(dir) || Directory.GetFiles(dir, "*" + Storage.LibraryExtension).Length == 0)
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
        if (string.IsNullOrEmpty(_libraryPath)) return;
        try
        {
            MergeExternalChanges(); // لو الملف اتغير من جهاز تاني، ندمج الأول عشان منمسحش شغله
            Storage.SaveLibrary(_libraryPath, _library);
            _libraryStampUtc = File.GetLastWriteTimeUtc(_libraryPath);
            _dirty = false;
            _positionDirty = false;
            HideBanner("save-error");
        }
        catch (Exception ex)
        {
            Log.Error("save", ex);
            ShowBanner("مقدرتش أحفظ المكتبة: " + ex.Message + " — هحاول تاني لوحدي", BannerKind.Error, seconds: 15, key: "save-error");
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

    private void MarkDirty() => _dirty = true;

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
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(Path.GetDirectoryName(path)))
                Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(path)}\"");
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
        var recent = _settings.RecentFiles.Take(8).ToList();
        if (!recent.Any(f => string.Equals(f, _libraryPath, StringComparison.OrdinalIgnoreCase))) recent.Insert(0, _libraryPath);
        foreach (string f in recent)
        {
            string path = f;
            bool current = string.Equals(f, _libraryPath, StringComparison.OrdinalIgnoreCase);
            bool exists = current || File.Exists(f);
            string name = current ? _library.Name : Path.GetFileNameWithoutExtension(f);
            var item = Add((current ? "✓  " : "      ") + name + (exists ? "" : "  (مش متاحة دلوقتي)"),
                () => { if (!current) SwitchLibrary(path); }, exists);
            item.ToolTipText = f;
            if (current) item.Font = new Font(item.Font, FontStyle.Bold);
        }
        menu.Items.Add(new ToolStripSeparator());
        Add("➕  مكتبة جديدة…", NewLibraryDialog, shortcut: "Ctrl+N");
        Add("📂  فتح مكتبة من ملف…", OpenLibraryDialog, shortcut: "Ctrl+O");
        menu.Items.Add(new ToolStripSeparator());
        Add("✏️  تغيير اسم المكتبة دي…", RenameLibrary);
        Add("💾  حفظ نسخة باسم تاني…", SaveLibraryAsDialog, shortcut: "Ctrl+Shift+S");
        Add("📍  فتح مكان الملف", () => OpenInExplorer(_libraryPath));
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_courseLabel, new Point(_courseLabel.Width, _courseLabel.Height), ToolStripDropDownDirection.BelowLeft);
    }

    private void RenameLibrary()
    {
        string? name = Dialogs.Input(this, "اسم المكتبة", "اكتب اسم المكتبة:", _library.Name);
        if (name == null) return;
        _library.Name = name;
        MarkDirty();
        UpdateLibraryHeader();
        UpdateTitle();
    }

    private void UpdateLibraryHeader()
    {
        // «📚 الاسم ▾»: واضح إن ده اسم المكتبة المفتوحة وإنه بيتضغط
        _courseLabel.Text = "📚  " + Modern.Bidi(_library.Name) + "  ▾";
        _toolTip.SetToolTip(_courseLabel, "المكتبة المفتوحة: " + _library.Name + "\n" + _libraryPath +
                                          "\n\nاضغط عشان تبدّل لمكتبة تانية أو تعمل واحدة جديدة");
        int videos = _library.VideoCount, watched = _library.WatchedCount;
        long total = _library.TotalDurationMs;
        string duration = total > 0 ? "  ·  " + TimeFormat.Format(total) : "";
        int percent = videos == 0 ? 0 : (int)Math.Round(watched * 100.0 / videos);
        _courseInfoLabel.Text = (_library.Topics.Count > 0 ? $"{_library.Topics.Count} موضوع  ·  " : "") +
            $"{_library.Courses.Count} كورس  ·  {videos} فيديو{duration}  ·  {percent}% خلص" +
            (_activeFilterText.Length > 0 ? $"  ·  فلتر: {Modern.Bidi(_activeFilterText)}" : "");
        _libraryProgress.Value = videos == 0 ? 0 : (double)watched / videos;
    }

    private void UpdateStatusBar()
    {
        var g = _currentGroup ?? SelectedGroup();
        var c = g != null ? _library.FindCourseOf(g) : SelectedCourse();
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

            // المواضيع (التفريعة الرئيسية) وتحت كل واحد كورساته
            foreach (var t in _library.Topics)
            {
                bool topicMatches = filter.Length > 0 && Matches(t.Name, filter);
                var tn = new TreeNode { Tag = t };
                foreach (var c in _library.CoursesOf(t))
                    if (BuildCourse(c, topicMatches) is { } cn) tn.Nodes.Add(cn);
                if (tn.Nodes.Count == 0 && filtering && !topicMatches) continue;
                _topicNodes[t] = tn;
                UpdateTopicNode(tn, t);
                _tree.Nodes.Add(tn);
                if (filter.Length > 0 || t.Expanded) toExpand.Insert(0, tn);
            }

            // الكورسات اللي برا أي موضوع
            foreach (var c in _library.CoursesOf(null))
                if (BuildCourse(c, false) is { } cn) _tree.Nodes.Add(cn);

            foreach (var n in toExpand) n.Expand();

            if (selectedTag != null && NodeOf(selectedTag) is { } sel) _tree.SelectedNode = sel;
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
        _tree.SetRow(node, new LectureTreeView.RowInfo("", $"{c.WatchedCount}/{c.VideoCount}", c.IsCompleted));
        node.ImageKey = node.SelectedImageKey = c.IsCompleted ? "courseDone" : "course";
        bool current = _currentGroup != null && c.Groups.Contains(_currentGroup);
        node.ForeColor = current ? Theme.Accent : c.IsCompleted ? Theme.Success
            : c.Status is CourseStatus.Paused or CourseStatus.Planned ? Theme.Muted : Theme.Text;
        node.NodeFont = null; // خط الشجرة (Bold)
        node.ToolTipText = CourseTooltip(c);
    }

    private void UpdateGroupNode(TreeNode node, LectureGroup g)
    {
        string clip = g.Attachments.Count > 0 ? $"   📎{g.Attachments.Count}" : "";
        node.Text = Modern.Bidi(g.Name) + clip;
        int fresh = g.NewCount;
        string meta = g.SourceUrl != null ? "🔄" : "";
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
        if (c != null && _library.TopicOf(c) is { } t && _topicNodes.TryGetValue(t, out var tn)) UpdateTopicNode(tn, t);
    }

    private void OnNodeExpandChanged(TreeNode? node, bool expanded)
    {
        if (_rebuilding || node == null) return;
        switch (node.Tag)
        {
            case Topic t: t.Expanded = expanded; break;
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

    private void SetAllExpanded(bool expanded)
    {
        foreach (var t in _library.Topics) t.Expanded = expanded;
        foreach (var c in _library.Courses)
        {
            c.Expanded = expanded;
            foreach (var g in c.Groups) g.Expanded = expanded;
        }
        MarkDirty();
        RebuildTree();
    }

    // =====================================================================
    //  الكورسات
    // =====================================================================

    private Course? AddCourse(string? name = null, Course? after = null, Topic? topicFor = null, bool atRoot = false)
    {
        name ??= Dialogs.Input(this, "كورس جديد", "اسم الكورس:", $"كورس {_library.Courses.Count + 1}");
        if (name == null) return null;

        Checkpoint("إضافة كورس");
        var topic = atRoot ? null : topicFor ?? (after != null ? _library.TopicOf(after) : SelectedTopicContext());
        var c = new Course { Name = name, TopicId = topic?.Id };
        after ??= SelectedCourse() is { } sc && _library.TopicOf(sc) == topic ? sc : _library.CoursesOf(topic).LastOrDefault();
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

            case Course c:
                if (!Dialogs.Confirm(this, $"حذف الكورس «{c.Name}» من المكتبة؟\n" +
                                           $"({c.Groups.Count} محاضرة · {c.VideoCount} فيديو — الملفات نفسها مش هتتمسح من الجهاز)"))
                    return;
                if (_currentGroup != null && c.Groups.Contains(_currentGroup)) StopPlaybackCore(clearCurrent: true);
                Checkpoint("حذف كورس");
                _deletedKeys.Add(LibrarySync.CourseKey(c));
                _library.Courses.Remove(c);
                break;

            case LectureGroup g:
                if (!Dialogs.Confirm(this, $"حذف المحاضرة «{g.Name}» من المكتبة؟\n({g.Videos.Count} فيديو — الملفات نفسها مش هتتمسح من الجهاز)"))
                    return;
                if (g == _currentGroup) StopPlaybackCore(clearCurrent: true);
                var course = _library.FindCourseOf(g);
                Checkpoint("حذف محاضرة");
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
    private void ImportCourseDialog()
    {
        string? path = PickFolder(this, "اختار فولدر الكورس (كل فولدر فرعي فيه فيديوهات هيبقى محاضرة)");
        if (path != null) ImportFolder(path, into: null);
    }

    /// <summary>فولدر فيه كذا كورس ← كل فولدر مباشر جواه = كورس.</summary>
    private void ImportMultiCourseDialog()
    {
        string? path = PickFolder(this, "اختار الفولدر اللي فيه الكورسات (كل فولدر جواه = كورس)");
        if (path != null) ImportMultiCourse(path);
    }

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
            course = new Course { Name = new DirectoryInfo(root).Name, Groups = groups, TopicId = SelectedTopicContext()?.Id };
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
        else if (SelectedTopicContext() is { } sel)
            foreach (var c in courses) c.TopicId = sel.Id;
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

    private void QueueProbe(IEnumerable<VideoItem> videos)
    {
        foreach (var v in videos)
            if (v.DurationMs <= 0 && !v.IsOnline && File.Exists(v.LocalPath)) _probeQueue.Enqueue(v);
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
                var v = _probeQueue.Dequeue();
                if (v.DurationMs > 0) continue;
                try
                {
                    using var media = new Media(_libVLC, v.LocalPath, FromType.FromPath);
                    await media.Parse(MediaParseOptions.ParseLocal, 5000);
                    if (_closing) return;
                    if (media.Duration > 0)
                    {
                        v.DurationMs = media.Duration;
                        MarkDirty();
                        RefreshVideo(v);
                        if (_library.FindGroupOf(v) is { } g) touchedGroups.Add(g);
                    }
                }
                catch { /* ملف بايظ أو مش مدعوم — هتتعرف مدته لما يشتغل */ }

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

            case Course c:
                items.Add(MenuItem("▶  تشغيل / استكمال الكورس", () => PlayCourse(c)));
                if (AdvancedMode) items.Add(MenuItem("بيانات الكورس (الحالة، الموعد، الوسوم)…", () => EditCourseInfo(c), "F4"));
                items.Add(new ToolStripSeparator());
                items.Add(MenuItem("محاضرة جديدة في الكورس ده…", () => AddGroup(course: c)));
                items.Add(MenuItem("إضافة فولدر محاضرات للكورس ده…", () => ImportIntoCourseDialog(c)));
                items.Add(MenuItem("إضافة قائمة تشغيل يوتيوب للكورس ده…", () => AddUrlsDialog(null), "Ctrl+U"));
                items.Add(MenuItem("إعادة تسمية…", RenameSelected, "F2"));
                items.Add(MenuItem("ترتيب المحاضرات بالاسم", () => SortGroups(c)));
                items.Add(MenuItem("حفظ ملاحظات الكورس في ملف…", () => ExportNotesDialog(c), "Ctrl+E"));
                items.Add(new ToolStripSeparator());
                items.Add(MenuItem(c.IsCompleted ? "إلغاء علامة المشاهدة للكورس كله" : "تعليم الكورس كله كمُشاهَد", ToggleWatchedSelected, "W"));
                items.Add(MenuItem("↺ تصفير التقدم في الكورس…", () => ResetWatched(c.AllVideos, $"كورس «{c.Name}»")));
                items.Add(MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"));
                items.Add(MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"));
                items.Add(new ToolStripSeparator());
                items.Add(MoveCourseToTopicMenu(c));
                items.Add(MenuItem("كورس جديد بعده…", () => AddCourse(after: c)));
                items.Add(MenuItem("حذف الكورس من المكتبة", DeleteSelected, "Delete"));
                break;

            case LectureGroup g:
            {
                var course = _library.FindCourseOf(g);
                items.Add(MenuItem("▶  تشغيل المحاضرة", () => PlayGroup(g)));
                items.Add(MenuItem("تشغيل من أول فيديو", () => { if (g.Videos.Count > 0) PlayVideo(g, g.Videos[0], 0); }));
                items.Add(AttachmentsMenu(g));
                if (g.SourceUrl != null)
                {
                    items.Add(MenuItem("🔄 تحديث من يوتيوب (هات الجديد)", () => _ = SyncPlaylistsAsync(new[] { g }, quiet: false)));
                    items.Add(MenuItem("فك الربط بقائمة يوتيوب", () => { Checkpoint("فك الربط"); g.SourceUrl = null; g.InfoUpdatedUtc = DateTime.UtcNow; MarkDirty(); RefreshGroup(g); }));
                }
                else items.Add(MenuItem("ربط بقائمة تشغيل يوتيوب (للتحديث)…", () => LinkPlaylist(g)));
                if (g.NewCount > 0) items.Add(MenuItem($"شيل علامة «جديد» ({g.NewCount})", () => ClearNew(g)));
                items.Add(new ToolStripSeparator());
                items.Add(MenuItem("إضافة فيديوهات…", () => AddVideosDialog(g)));
                items.Add(MenuItem("إضافة رابط يوتيوب للمحاضرة دي…", () => AddUrlsDialog(null), "Ctrl+U"));
                items.Add(MenuItem("إعادة تسمية…", RenameSelected, "F2"));
                items.Add(MenuItem("محاضرة جديدة بعدها…", () => AddGroup(after: g)));
                items.Add(new ToolStripSeparator());
                if (AdvancedMode) items.Add(MenuItem("تقسيم تلقائي (عند Part 01)…", () => AutoSplitDialog(g)));
                items.Add(MenuItem("ترتيب الفيديوهات بالاسم", () => SortGroupVideos(g)));
                if (g.Videos.Any(x => x.IsOnline))
                    items.Add(MenuItem("تنزيل فيديوهات يوتيوب في المحاضرة دي (من غير نت)", () => _ = DownloadOfflineAsync(g.Videos)));
                items.Add(new ToolStripSeparator());
                items.Add(MenuItem(g.IsCompleted ? "إلغاء علامة المشاهدة للكل" : "تعليم الكل كمُشاهَد", ToggleWatchedSelected, "W"));
                items.Add(MenuItem("↺ تصفير التقدم في المحاضرة (من الأول)…", () => ResetWatched(g.Videos, $"محاضرة «{g.Name}»")));
                items.Add(MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"));
                items.Add(MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"));

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
                items.Add(new ToolStripSeparator());
                items.Add(MenuItem(v.Watched ? "تعليم كغير مُشاهَد" : "تعليم كمُشاهَد", () => SetWatched(v, !v.Watched), "W"));
                if (v.Watched || v.LastPositionMs > 0)
                    items.Add(MenuItem("↺ تصفير التقدم (يبدأ من الأول)", () => ResetWatched(new[] { v }, $"«{v.Title}»")));
                items.Add(new ToolStripSeparator());
                items.Add(MenuItem("ابدأ محاضرة جديدة من هنا…", () => SplitFromHere(v)));
                items.Add(BuildMoveVideoMenu(v, owner));
                items.Add(MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"));
                items.Add(MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"));
                items.Add(MenuItem("إعادة تسمية…", RenameSelected, "F2"));
                items.Add(new ToolStripSeparator());
                if (v.IsOnline)
                {
                    if (IsDownloaded(v)) items.Add(MenuItem("مسح النسخة المتنزلة", () => RemoveOffline(v)));
                    else items.Add(MenuItem("تنزيل للمشاهدة من غير نت", () => _ = DownloadOfflineAsync(new[] { v })));
                    items.Add(MenuItem("فتح في المتصفح", () => OpenInBrowser(v.Path)));
                    items.Add(MenuItem("نسخ الرابط", () => { try { Clipboard.SetText(v.Path); } catch { } }));
                }
                else if (exists) items.Add(MenuItem("فتح مكان الملف", () => OpenInExplorer(v.LocalPath)));
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
        _nodeMenu.Items.Add(MenuItem("موضوع جديد (تفريعة رئيسية)…", () => AddTopic(), "Ctrl+Shift+N"));
        _nodeMenu.Items.Add(MenuItem("كورس جديد (برا أي موضوع)…", () => AddCourse(atRoot: true), "Ctrl+Shift+G"));
        _nodeMenu.Items.Add(MenuItem("استيراد فولدر كورس…", ImportCourseDialog, "Ctrl+I"));
        if (AdvancedMode) _nodeMenu.Items.Add(MenuItem("استيراد فولدر فيه أكتر من كورس…", ImportMultiCourseDialog, "Ctrl+Shift+I"));
        _nodeMenu.Items.Add(MenuItem("إضافة رابط يوتيوب / قائمة تشغيل…", () => AddUrlsDialog(null), "Ctrl+U"));
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

        switch (dragged.Tag)
        {
            case VideoItem video: DropVideo(video, targetTag); break;
            case LectureGroup group: DropGroup(group, targetTag); break;
            case Course course: DropCourse(course, target?.Tag); break;
            case Topic topic: DropTopic(topic, target?.Tag); break;
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
    /// كورس اتساب على موضوع ← يبقى في آخر الموضوع ده. على كورس تاني ← ياخد موضوعه ومكانه.
    /// في مكان فاضي ← يطلع برا أي موضوع في الآخر.
    /// </summary>
    private void DropCourse(Course course, object? target)
    {
        Topic? topic;
        Course? anchor;
        switch (target)
        {
            case Topic t: topic = t; anchor = null; break;
            case Course c: anchor = c; topic = _library.TopicOf(c); break;
            case LectureGroup g: anchor = _library.FindCourseOf(g); topic = anchor != null ? _library.TopicOf(anchor) : null; break;
            case VideoItem v: anchor = _library.FindCourseOf(v); topic = anchor != null ? _library.TopicOf(anchor) : null; break;
            default: topic = null; anchor = null; break;
        }
        if (anchor == course) return;

        Checkpoint("سحب وإفلات");
        int from = _library.Courses.IndexOf(course);
        int originalAnchor = anchor != null ? _library.Courses.IndexOf(anchor) : -1;
        _library.Courses.Remove(course);
        if (course.TopicId != topic?.Id) { course.TopicId = topic?.Id; course.TouchInfo(); }

        int to;
        if (anchor != null) to = _library.Courses.IndexOf(anchor) + (from < originalAnchor ? 1 : 0);
        else
        {
            var last = _library.CoursesOf(topic).LastOrDefault();
            to = last != null ? _library.Courses.IndexOf(last) + 1 : _library.Courses.Count;
        }
        _library.Courses.Insert(Math.Clamp(to, 0, _library.Courses.Count), course);
        if (topic != null) topic.Expanded = true;

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
            ImportFolder(dir, into: targetCourse);

        var files = paths.Where(p => File.Exists(p) && MediaFiles.IsMedia(p)).ToList();
        if (files.Count == 0) return;

        if (targetGroup == null)
        {
            string name = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(files[0]))!).Name;
            if (targetCourse == null)
            {
                targetCourse = new Course { Name = name, TopicId = SelectedTopicContext()?.Id };
                _library.Courses.Add(targetCourse);
            }
            targetGroup = new LectureGroup { Name = name };
            targetCourse.Groups.Add(targetGroup);
        }
        AddFiles(targetGroup, files);
    }
}
