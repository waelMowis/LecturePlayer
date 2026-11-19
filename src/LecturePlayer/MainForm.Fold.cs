using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// الطي والتوسيع: زرار جنب البحث (فتح الكل، قفل الكل، «اعرض لحد» مستوى معيّن)،
/// و«افتح/اقفل كل اللي جواه» في الكليك يمين على الموضوع أو المصدر أو الكورس.
/// الحالة بتتحفظ في المكتبة (كل عنصر فاكر هو مفتوح ولا مقفول).
/// </summary>
internal sealed partial class MainForm
{
    /// <summary>المستويات: 0 المواضيع بس، 1 لحد المصادر، 2 لحد الكورسات، 3 لحد المحاضرات، 4 كله مفتوح (الفيديوهات ظاهرة).</summary>
    private void ExpandToLevel(int level)
    {
        foreach (var t in _library.Topics) t.Expanded = level >= 1;
        foreach (var p in _library.Places) p.Expanded = level >= 2;
        foreach (var c in _library.Courses)
        {
            c.Expanded = level >= 3;
            foreach (var g in c.Groups) g.Expanded = level >= 4;
        }
        MarkDirty();
        RebuildTree();
        ScrollTreeToTop();
    }

    private void SetAllExpanded(bool expanded) => ExpandToLevel(expanded ? 4 : 0);

    private void ScrollTreeToTop()
    {
        if (_tree.Nodes.Count == 0) return;
        try { _tree.TopNode = _tree.Nodes[0]; } catch { }
    }

    /// <summary>
    /// expanded = true: العنصر وكل اللي جواه يتفتح لحد الفيديوهات.
    /// expanded = false: العنصر يفضل مفتوح، واللي جواه يتقفل (تشوف قايمة اللي جواه بس).
    /// </summary>
    private void SetSubtreeExpanded(object item, bool expanded)
    {
        IEnumerable<Place> places;
        IEnumerable<Course> courses;
        switch (item)
        {
            case Topic t:
                t.Expanded = true;
                places = _library.PlacesOf(t).ToList();
                courses = _library.CoursesOf(t).ToList();
                break;
            case Place p:
                p.Expanded = true;
                places = _library.ChildrenOf(p).ToList();
                courses = _library.AllCoursesUnder(p).ToList();
                break;
            case Course c:
                c.Expanded = true;
                places = Enumerable.Empty<Place>();
                courses = Array.Empty<Course>();
                foreach (var g in c.Groups) g.Expanded = expanded;
                break;
            default: return;
        }
        foreach (var p in places) p.Expanded = expanded;
        foreach (var c in courses)
        {
            c.Expanded = expanded;
            foreach (var g in c.Groups) g.Expanded = expanded;
        }
        MarkDirty();
        RebuildTree();
        SelectItem(item);
    }

    /// <summary>«افتح كل اللي جواه» و«اقفل كل اللي جواه» لقايمة الكليك يمين.</summary>
    private ToolStripMenuItem FoldMenu(object item) => SubMenuOf("طي وتوسيع",
        MenuItem("⊞  افتح كل اللي جواه", () => SetSubtreeExpanded(item, true)),
        MenuItem("⊟  اقفل كل اللي جواه", () => SetSubtreeExpanded(item, false)));

    private readonly ContextMenuStrip _foldMenu = new() { RightToLeft = RightToLeft.Yes, ShowImageMargin = false };

    /// <summary>زرار الطي والتوسيع جنب البحث.</summary>
    private void BuildFoldButton(Panel row)
    {
        _foldMenu.Renderer = new DarkRenderer();
        void Add(string text, Action click, string? keys = null)
        {
            var i = new ToolStripMenuItem(text) { ForeColor = Theme.Text, ShortcutKeyDisplayString = keys };
            i.Click += (_, _) => click();
            _foldMenu.Items.Add(i);
        }
        Add("⊞  فتح الكل", () => ExpandToLevel(4), "Ctrl+Shift+]");
        Add("⊟  قفل الكل", () => ExpandToLevel(0), "Ctrl+Shift+[");
        _foldMenu.Items.Add(new ToolStripSeparator());
        _foldMenu.Items.Add(new ToolStripLabel("اعرض لحد:") { ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) });
        Add("      المواضيع بس", () => ExpandToLevel(0));
        Add("      المصادر", () => ExpandToLevel(1));
        Add("      الكورسات", () => ExpandToLevel(2));
        Add("      المحاضرات", () => ExpandToLevel(3));

        var button = new GlyphButton(Glyphs.Fold, 11f) { Dock = DockStyle.Left, Width = LogicalToDeviceUnits(30) };
        _toolTip.SetToolTip(button, "طي وتوسيع: فتح الكل، قفل الكل، أو اعرض لحد مستوى معيّن");
        button.Click += (_, _) => _foldMenu.Show(button, new Point(0, button.Height + 2));
        row.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 2, BackColor = Theme.Panel });
        row.Controls.Add(button);
    }

    /// <summary>
    /// فحص تلقائي لإدارة المكتبات على الواجهة: «حفظ نسخة باسم» بيدّي الاسم الجديد وبينقل سجل المذاكرة،
    /// والقديمة بتفضل باسمها، والقايمة بتفرّق بينهم، و«النقل» بيشيل القديمة من القايمة، وإصلاح الاسم القديم.
    /// </summary>
    internal void RunLibraryFileChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("library ui: " + what); }
        string dir = Path.Combine(Path.GetTempPath(), "masar-smoke-libui");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(Path.Combine(dir, "Drive"));
        string original = Path.Combine(dir, "مكتبتي.lpl");
        var lib = new CourseLibrary { Name = "مكتبتي" };
        lib.Courses.Add(new Course { Name = "C1" });
        Storage.SaveLibrary(original, lib);
        File.WriteAllText(Path.Combine(dir, "مكتبتي.sessions.PC1.jsonl"), "");
        var savedRecent = _settings.RecentFiles.ToList();
        string? savedLast = _settings.LastLibraryPath;
        try
        {
            LoadLibrary(original);
            // حفظ نسخة باسم WAEL على «الدرايف»
            string copy = Path.Combine(dir, "Drive", "WAEL.lpl");
            RelocateLibrary(copy, move: false);
            Check(_libraryPath == Path.GetFullPath(copy) && _library.Name == "WAEL", "save-as: switched with new name");
            Check(_courseLabel.Text.Contains("WAEL"), "save-as: header " + _courseLabel.Text);
            Check(LibraryFiles.PeekName(copy) == "WAEL" && LibraryFiles.PeekName(original) == "مكتبتي", "save-as: names in files");
            Check(File.Exists(Path.Combine(dir, "Drive", "WAEL.sessions.PC1.jsonl")), "save-as: journal copied");
            var recent = RecentLibraries(8).Where(x => Path.GetFullPath(x.Path).StartsWith(dir, StringComparison.OrdinalIgnoreCase)).ToList();
            Check(recent.Count == 2 && recent.Count(x => x.Text.Contains("WAEL")) == 1 && recent.Count(x => x.Text.Contains("مكتبتي")) == 1,
                "save-as: both listed with own names: " + string.Join(" | ", recent.Select(x => x.Text)));
            // نفس الاسم في مكانين مختلفين (مكتبتي على الجهاز ومكتبتي هنا) بيبانوا مختلفين بالمكان
            var all = RecentLibraries(8);
            Check(all.Select(x => x.Text).Distinct().Count() == all.Count, "two entries look identical: " + string.Join(" | ", all.Select(x => x.Text)));
            Check(recent.Single(x => x.Current).Text.Contains("WAEL"), "save-as: current is WAEL");

            // فتح القديمة تاني ← باسمها، والرجوع للجديدة
            SwitchLibrary(original);
            Check(_library.Name == "مكتبتي" && _courseLabel.Text.Contains("مكتبتي"), "reopen old");
            SwitchLibrary(copy);
            Check(_library.Name == "WAEL", "reopen new");

            // نقل لفولدر تاني ← القديمة بتتشال من القايمة
            string moved = Path.Combine(dir, "Drive", "Moved.lpl");
            RelocateLibrary(moved, move: true);
            Check(_library.Name == "Moved" && !_settings.RecentFiles.Any(f => string.Equals(f, Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase)), "move: old removed from list");

            // تغيير الاسم بيظهر في القايمة والعنوان
            _library.Name = "اسم جديد";
            UpdateLibraryHeader();
            Check(_courseLabel.Text.Contains("اسم جديد") && RecentLibraries(8).Single(x => x.Current).Text.Contains("اسم جديد"), "rename shows");

            // إصلاح لمرة واحدة: ملف اسمه X وجواه «مكتبتي» (من حفظ باسم قديم)
            string old = Path.Combine(dir, "Drive", "Old WAEL.lpl");
            Storage.SaveLibrary(old, new CourseLibrary { Name = "مكتبتي" });
            LoadLibrary(old, remember: false);
            Check(_library.Name == "Old WAEL" && LibraryFiles.PeekName(old) == "Old WAEL", "heal old save-as name");
        }
        finally
        {
            _settings.RecentFiles = savedRecent;
            _settings.LastLibraryPath = savedLast;
            SaveSettings();
        }
        Console.WriteLine("SMOKE library files ui OK");
    }

    /// <summary>
    /// فتح الكل / قفل الكل / «اعرض لحد» مع أكتر من موضوع، والتحديد جوه موضوع تاني (زي آخر فيديو اتشغل):
    /// اللي على الشاشة لازم يطابق اللي البرنامج فاكره، ومفيش موضوع يفتح لوحده.
    /// </summary>
    internal void RunFoldChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("fold: " + what); }
        string dir = Path.Combine(Path.GetTempPath(), "masar-smoke-fold");
        Directory.CreateDirectory(dir);
        var lib = new CourseLibrary { Name = "fold" };
        VideoItem? deep = null;
        for (int ti = 1; ti <= 3; ti++)
        {
            var t = new Topic { Name = "Topic " + ti };
            lib.Topics.Add(t);
            var p = new Place { Name = "Source " + ti, TopicId = t.Id };
            var sub = new Place { Name = "Sub " + ti, TopicId = t.Id, ParentId = p.Id };
            lib.Places.Add(p);
            lib.Places.Add(sub);
            foreach (var (place, name) in new[] { (p, "In source " + ti), (sub, "In sub " + ti), ((Place?)null, "Direct " + ti) })
            {
                var c = new Course { Name = name, TopicId = t.Id, PlaceId = place?.Id };
                for (int gi = 1; gi <= 2; gi++)
                {
                    var g = new LectureGroup { Name = "L" + gi };
                    g.Videos.Add(new VideoItem { Path = Path.Combine(dir, $"{name}-{gi}.mp4"), Title = "v" + gi });
                    c.Groups.Add(g);
                }
                lib.Courses.Add(c);
                if (ti == 2 && place == sub) deep = c.Groups[1].Videos[0];
            }
        }
        string path = Path.Combine(dir, "fold.lpl");
        Storage.SaveLibrary(path, lib);
        LoadLibrary(path, remember: false);
        string deepPath = deep!.Path;
        deep = _library.AllVideos.Single(v => v.Path == deepPath); // نفس الفيديو في المكتبة اللي اتفتحت

        bool Model(TreeNode n) => n.Tag switch
        {
            Topic t => t.Expanded, Place p => p.Expanded, Course c => c.Expanded, LectureGroup g => g.Expanded, _ => false
        };
        IEnumerable<TreeNode> All(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes) { yield return n; foreach (var x in All(n.Nodes)) yield return x; }
        }
        void Consistent(string when)
        {
            foreach (var n in All(_tree.Nodes).Where(n => n.Nodes.Count > 0))
                Check(n.IsExpanded == Model(n), $"{when}: «{n.Tag}» on screen {(n.IsExpanded ? "open" : "closed")} but remembered {(Model(n) ? "open" : "closed")}");
            var sel = _tree.SelectedNode;
            for (var a = sel?.Parent; a != null; a = a.Parent) Check(a.IsExpanded, $"{when}: selection hidden inside a closed node");
        }

        // التحديد جوه الموضوع التاني (مصدر فرعي ← كورس ← محاضرة ← فيديو)
        _tree.SelectedNode = NodeOf(deep) ?? throw new Exception("fold: deep video not in tree");
        Check(_tree.SelectedNode.Tag == deep, "start selection");
        for (int level = 4; level >= 0; level--)
        {
            ExpandToLevel(level);
            Consistent("level " + level);
        }
        ExpandToLevel(0);
        Check(All(_tree.Nodes).Count(n => n.Parent == null && n.IsExpanded) == 0, "collapse all left a topic open");
        Check(_tree.SelectedNode?.Tag is Topic { Name: "Topic 2" }, "selection should move to its topic: " + _tree.SelectedNode?.Tag);
        ExpandToLevel(4);
        Consistent("expand all");
        Check(All(_tree.Nodes).Where(n => n.Nodes.Count > 0).All(n => n.IsExpanded), "expand all left something closed");
        // اختصارات الكيبورد بتعمل نفس الحاجة
        var msg = new Message();
        _tree.Focus();
        ProcessCmdKey(ref msg, Keys.Control | Keys.Shift | Keys.OemOpenBrackets);
        Check(_library.Topics.All(t => !t.Expanded) && _tree.Nodes.Cast<TreeNode>().All(n => !n.IsExpanded), "Ctrl+Shift+[");
        Directory.Delete(dir, true);
        Console.WriteLine("SMOKE fold OK");
    }
}
