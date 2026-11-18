using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// المستوى التاني في الشجرة: «مكان» جوه الموضوع (ROUTE، Udemy…).
/// الهيكل: موضوع ← مكان ← كورس ← محاضرة ← فيديو. المكان اختياري: الكورس ممكن يبقى تحت الموضوع على طول.
/// </summary>
internal sealed partial class MainForm
{
    private readonly Dictionary<Place, TreeNode> _placeNodes = new();

    /// <summary>المكان المحدد في الشجرة، أو مكان العنصر المحدد (كورس/محاضرة/فيديو).</summary>
    private Place? SelectedPlaceContext() => _tree.SelectedNode?.Tag switch
    {
        Place p => p,
        Course c => _library.PlaceOf(c),
        LectureGroup g when _library.FindCourseOf(g) is { } c => _library.PlaceOf(c),
        VideoItem v when _library.FindCourseOf(v) is { } c => _library.PlaceOf(c),
        _ => null
    };

    /// <summary>كورس جديد بياخد موضوع ومكان العنصر المحدد في الشجرة.</summary>
    private void PutInSelectedContext(Course c)
    {
        if (SelectedPlaceContext() is { } p) { c.TopicId = p.TopicId; c.PlaceId = p.Id; }
        else { c.TopicId = SelectedTopicContext()?.Id; c.PlaceId = null; }
    }

    /// <summary>كورسات نفس الحاوية (المكان، أو الموضوع على طول) — للترتيب والتحريك.</summary>
    private List<Course> SiblingCourses(Course c) =>
        _library.PlaceOf(c) is { } p ? _library.CoursesInPlace(p).ToList() : _library.DirectCoursesOf(_library.TopicOf(c)).ToList();

    /// <summary>يحط الكورس في آخر الحاوية (موضوع/مكان) في ترتيب المكتبة.</summary>
    private void PlaceCourseAtEnd(Course c, Topic? topic, Place? place)
    {
        _library.Courses.Remove(c);
        _library.MoveCourse(c, topic, place);
        var last = (place != null ? _library.CoursesInPlace(place) : _library.DirectCoursesOf(topic)).LastOrDefault()
                   ?? (place != null ? _library.CoursesOf(_library.TopicOf(place)).LastOrDefault() : null);
        int to = last != null ? _library.Courses.IndexOf(last) + 1 : _library.Courses.Count;
        _library.Courses.Insert(Math.Clamp(to, 0, _library.Courses.Count), c);
        if (place != null) place.Expanded = true;
        var t = place != null ? _library.TopicOf(place) : topic;
        if (t != null) t.Expanded = true;
    }

    private void UpdatePlaceNode(TreeNode node, Place p)
    {
        var courses = _library.CoursesInPlace(p).ToList();
        int videos = courses.Sum(c => c.VideoCount), watched = courses.Sum(c => c.WatchedCount);
        long duration = courses.Sum(c => c.TotalDurationMs);
        bool done = videos > 0 && watched == videos;
        bool linked = p.SourceFolders.Count > 0;
        node.Text = Modern.Bidi(p.Name);
        node.NodeFont = _groupNodeFont;
        node.ImageKey = node.SelectedImageKey = done ? "placeDone" : "place";
        bool current = _currentGroup != null && courses.Any(c => c.Groups.Contains(_currentGroup));
        node.ForeColor = current ? Theme.Accent : done ? Theme.Success : Theme.Text;
        int doneCourses = courses.Count(c => c.IsCompleted);
        node.ToolTipText = $"🏫 {p.Name}\n{courses.Count} كورس (خلصت {doneCourses}) · {watched} من {videos} فيديو"
            + (duration > 0 ? " · " + TimeFormat.Format(duration) : "")
            + (linked ? "\n🔗 فولدرات مربوطة (الجديد بيتضاف لوحده):\n" + string.Join("\n", p.SourceFolders) : "");
        _tree.SetRow(node, new LectureTreeView.RowInfo(linked ? "🔗" : "", $"{doneCourses}/{courses.Count}", done));
    }

    /// <summary>«مكان جديد» من قايمة ＋ إضافة: جوه الموضوع المحدد.</summary>
    private void AddPlaceFromMenu()
    {
        var topic = SelectedTopicContext();
        if (topic == null && _library.Topics.Count == 0)
        {
            Dialogs.Info(this, "المكان (ROUTE، Udemy…) بيبقى جوه موضوع (زي Backend).\nاعمل الموضوع الأول، وبعدها المكان جواه.");
            topic = AddTopic();
            if (topic == null) return;
        }
        else if (topic == null)
        {
            ShowStatus("حدد الموضوع اللي المكان هيبقى جواه في الشجرة الأول (أو كليك يمين على الموضوع ← مكان جديد)", 6);
            return;
        }
        AddPlace(topic);
    }

    private Place? AddPlace(Topic? topic, string? name = null)
    {
        name ??= Dialogs.Input(this, "مكان جديد",
            topic != null ? $"اسم المكان جوه «{topic.Name}» (مثلًا: ROUTE أو Udemy):" : "اسم المكان (مثلًا: ROUTE أو Udemy):");
        if (string.IsNullOrWhiteSpace(name)) return null;
        Checkpoint("إضافة مكان");
        var p = new Place { Name = name.Trim(), TopicId = topic?.Id };
        p.TouchInfo();
        // بعد المكان المحدد لو في نفس الموضوع، وإلا في آخر أماكن الموضوع
        var after = SelectedPlaceContext() is { } sel && sel.TopicId == p.TopicId ? sel : _library.PlacesOf(topic).LastOrDefault();
        int index = after != null ? _library.Places.IndexOf(after) + 1 : _library.Places.Count;
        _library.Places.Insert(Math.Clamp(index, 0, _library.Places.Count), p);
        if (topic != null) topic.Expanded = true;
        MarkDirty();
        RebuildTree();
        SelectItem(p);
        ShowStatus("اسحب الكورسات وارميها على المكان، أو كليك يمين على الكورس ← انقل لـ…", 6);
        return p;
    }

    /// <summary>حذف المكان بس — الكورسات اللي فيه بتفضل تحت الموضوع على طول.</summary>
    private void DeletePlace(Place p)
    {
        var courses = _library.CoursesInPlace(p).ToList();
        var topic = _library.TopicOf(p);
        string where = topic != null ? $"تحت «{topic.Name}» على طول" : "برا أي مكان";
        string msg = courses.Count == 0
            ? $"حذف المكان «{p.Name}»؟"
            : $"حذف المكان «{p.Name}»؟\n\nالـ {courses.Count} كورس اللي فيه مش هيتمسحوا — هيفضلوا {where}، بتقدّمهم وعلاماتهم.";
        if (p.SourceFolders.Count > 0) msg += "\n\nوالبرنامج هيبطّل يتابع الفولدرات المربوطة بيه (الفولدرات نفسها مش هتتمسح).";
        if (!Dialogs.Confirm(this, msg)) return;
        Checkpoint("حذف مكان");
        _deletedKeys.Add(LibrarySync.PlaceKey(p));
        _library.RemovePlace(p);
        MarkDirty();
        RebuildTree();
        if (courses.Count > 0) SelectItem(courses[0]);
        else if (topic != null) SelectItem(topic);
    }

    private void RenamePlace(Place p)
    {
        string? name = Dialogs.Input(this, "إعادة تسمية", "اسم المكان:", p.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        Checkpoint("إعادة تسمية");
        p.Name = name.Trim();
        p.TouchInfo();
    }

    private void MovePlaceToTopic(Place p, Topic? t)
    {
        if (p.TopicId == t?.Id) return;
        Checkpoint("نقل مكان لموضوع");
        _library.Places.Remove(p);
        var last = _library.PlacesOf(t).LastOrDefault();
        int to = last != null ? _library.Places.IndexOf(last) + 1 : _library.Places.Count;
        _library.Places.Insert(Math.Clamp(to, 0, _library.Places.Count), p);
        _library.MovePlace(p, t);
        if (t != null) t.Expanded = true;
        MarkDirty();
        RebuildTree();
        SelectItem(p);
    }

    private void MoveCourseTo(Course c, Topic? topic, Place? place)
    {
        var t = place != null ? _library.TopicOf(place) : topic;
        if (_library.TopicOf(c) == t && _library.PlaceOf(c) == place) return;
        Checkpoint("نقل كورس");
        PlaceCourseAtEnd(c, topic, place);
        MarkDirty();
        RebuildTree();
        SelectItem(c);
        ShowStatus($"اتنقل: {_library.PathOf(c)}", 4);
    }

    /// <summary>كمّل المكان: آخر كورس اشتغلت عليه فيه ولسه مخلصش (أو أول كورس مخلصش).</summary>
    private void PlayPlace(Place p)
    {
        var courses = _library.CoursesInPlace(p).Where(c => c.VideoCount > 0).ToList();
        var c = courses.Where(x => !x.IsCompleted).OrderByDescending(x => x.LastActivityUtc).FirstOrDefault() ?? courses.FirstOrDefault();
        if (c == null) { ShowStatus("المكان ده لسه مفيهوش كورسات بفيديوهات"); return; }
        PlayCourse(c);
    }

    private void BuildPlaceMenu(Place p, ToolStripItemCollection items)
    {
        items.Add(MenuItem("▶  كمّل آخر كورس في المكان ده", () => PlayPlace(p)));
        items.Add(new ToolStripSeparator());
        items.Add(MenuItem("كورس جديد هنا…", () => AddCourse(placeFor: p)));
        AddPlaceFolderLinkMenuItems(p, items);
        items.Add(MenuItem("استيراد فولدر كورس هنا…", () =>
        {
            string? path = PickFolder(this, "اختار فولدر الكورس (كل فولدر فرعي فيه فيديوهات هيبقى محاضرة)");
            if (path == null) return;
            _tree.SelectedNode = NodeOf(p);
            ImportFolder(path, into: null);
        }));
        items.Add(MenuItem("إعادة تسمية…", RenameSelected, "F2"));
        items.Add(MovePlaceToTopicMenu(p));
        items.Add(MenuItem("↺ تصفير التقدم في المكان كله…", () =>
            ResetWatched(_library.CoursesInPlace(p).SelectMany(c => c.AllVideos), $"مكان «{p.Name}»")));
        items.Add(MenuItem("ترتيب الكورسات بالاسم", () =>
        {
            Checkpoint("ترتيب الكورسات");
            var mine = _library.CoursesInPlace(p).OrderBy(c => c.Name, NaturalComparer.Instance).ToList();
            var slots = _library.Courses.Select((c, i) => (c, i)).Where(x => mine.Contains(x.c)).Select(x => x.i).ToList();
            for (int k = 0; k < slots.Count; k++) _library.Courses[slots[k]] = mine[k];
            MarkDirty();
            RebuildTree();
        }));
        items.Add(new ToolStripSeparator());
        items.Add(MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"));
        items.Add(MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"));
        items.Add(MenuItem("مكان جديد بعده…", () => AddPlace(_library.TopicOf(p))));
        items.Add(new ToolStripSeparator());
        items.Add(MenuItem("حذف المكان (الكورسات بتفضل)", DeleteSelected, "Delete"));
    }

    private ToolStripMenuItem MovePlaceToTopicMenu(Place p)
    {
        var current = _library.TopicOf(p);
        var menu = new ToolStripMenuItem("انقل لموضوع تاني") { ForeColor = Theme.Text };
        foreach (var t in _library.Topics)
        {
            var target = t;
            var item = MenuItem(Modern.Bidi(t.Name), () => MovePlaceToTopic(p, target));
            item.Checked = t == current;
            menu.DropDownItems.Add(item);
        }
        if (_library.Topics.Count > 0) menu.DropDownItems.Add(new ToolStripSeparator());
        if (current != null) menu.DropDownItems.Add(MenuItem("برا أي موضوع", () => MovePlaceToTopic(p, null)));
        menu.DropDownItems.Add(MenuItem("موضوع جديد…", () =>
        {
            var t = AddTopic();
            if (t != null) MovePlaceToTopic(p, t);
        }));
        return menu;
    }

    /// <summary>
    /// «انقل لـ…» للكورس: كل موضوع وجواه أماكنه، والكورس يتحط في المكان أو تحت الموضوع على طول.
    /// </summary>
    private ToolStripMenuItem MoveCourseMenu(Course c)
    {
        var curTopic = _library.TopicOf(c);
        var curPlace = _library.PlaceOf(c);
        var menu = new ToolStripMenuItem("انقل لـ…") { ForeColor = Theme.Text };

        foreach (var t in _library.Topics)
        {
            var topic = t;
            var places = _library.PlacesOf(t).ToList();
            if (places.Count == 0)
            {
                var item = MenuItem("📚  " + Modern.Bidi(t.Name), () => MoveCourseTo(c, topic, null));
                item.Checked = curTopic == t && curPlace == null;
                menu.DropDownItems.Add(item);
                continue;
            }
            var sub = new ToolStripMenuItem("📚  " + Modern.Bidi(t.Name)) { ForeColor = Theme.Text, Checked = curTopic == t };
            foreach (var p in places)
            {
                var place = p;
                var pi = MenuItem("🏫  " + Modern.Bidi(p.Name), () => MoveCourseTo(c, topic, place));
                pi.Checked = curPlace == p;
                sub.DropDownItems.Add(pi);
            }
            sub.DropDownItems.Add(new ToolStripSeparator());
            var direct = MenuItem($"تحت «{t.Name}» على طول (من غير مكان)", () => MoveCourseTo(c, topic, null));
            direct.Checked = curTopic == t && curPlace == null;
            sub.DropDownItems.Add(direct);
            sub.DropDownItems.Add(MenuItem("مكان جديد هنا…", () =>
            {
                var np = AddPlace(topic);
                if (np != null) MoveCourseTo(c, topic, np);
            }));
            menu.DropDownItems.Add(sub);
        }

        // أماكن برا أي موضوع (بتحصل لو الموضوع بتاعها اتمسح)
        foreach (var p in _library.PlacesOf(null))
        {
            var place = p;
            var pi = MenuItem("🏫  " + Modern.Bidi(p.Name), () => MoveCourseTo(c, null, place));
            pi.Checked = curPlace == p;
            menu.DropDownItems.Add(pi);
        }

        if (menu.DropDownItems.Count > 0) menu.DropDownItems.Add(new ToolStripSeparator());
        if (curTopic != null || curPlace != null) menu.DropDownItems.Add(MenuItem("برا أي موضوع", () => MoveCourseTo(c, null, null)));
        menu.DropDownItems.Add(MenuItem("موضوع جديد…", () =>
        {
            var t = AddTopic();
            if (t != null) MoveCourseTo(c, t, null);
        }));
        return menu;
    }

    /// <summary>
    /// مكان اتساب على موضوع ← يتنقل له (بكورساته). على مكان تاني ← يترتب جنبه وياخد موضوعه.
    /// على كورس ← ياخد موضوع الكورس.
    /// </summary>
    private void DropPlace(Place place, object? target)
    {
        Topic? topic;
        Place? anchor = null;
        switch (target)
        {
            case Topic t: topic = t; break;
            case Place p: anchor = p; topic = _library.TopicOf(p); break;
            case Course c: anchor = _library.PlaceOf(c); topic = _library.TopicOf(c); break;
            case LectureGroup g when _library.FindCourseOf(g) is { } c: anchor = _library.PlaceOf(c); topic = _library.TopicOf(c); break;
            case VideoItem v when _library.FindCourseOf(v) is { } c: anchor = _library.PlaceOf(c); topic = _library.TopicOf(c); break;
            default: topic = null; break;
        }
        if (anchor == place) return;

        Checkpoint("سحب وإفلات");
        int from = _library.Places.IndexOf(place);
        int originalAnchor = anchor != null ? _library.Places.IndexOf(anchor) : -1;
        _library.Places.Remove(place);
        int to;
        if (anchor != null) to = _library.Places.IndexOf(anchor) + (from < originalAnchor ? 1 : 0);
        else
        {
            var last = _library.PlacesOf(topic).LastOrDefault();
            to = last != null ? _library.Places.IndexOf(last) + 1 : _library.Places.Count;
        }
        _library.Places.Insert(Math.Clamp(to, 0, _library.Places.Count), place);
        if (place.TopicId != topic?.Id) _library.MovePlace(place, topic);
        if (topic != null) topic.Expanded = true;
        MarkDirty();
        RebuildTree();
        SelectItem(place);
    }

    /// <summary>
    /// فحص تلقائي للهيكل على الواجهة نفسها: شكل الشجرة (موضوع ← مكان ← كورس)، القوايم، السحب والإفلات،
    /// البحث باسم المكان، فلتر الشاشة الرئيسية، التحويل، والحفظ والفتح.
    /// </summary>
    internal void RunHierarchyUiChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("hierarchy ui: " + what); }
        string dir = Path.Combine(Path.GetTempPath(), "masar-smoke");
        Directory.CreateDirectory(dir);

        var lib = new CourseLibrary { Name = "هيكل" };
        var backendT = new Topic { Name = "Backend" };
        var oldT = new Topic { Name = "Eng Doaa Amin Elfawal" };
        lib.Topics.Add(backendT);
        lib.Topics.Add(oldT);
        lib.Places.Add(new Place { Name = "ROUTE", TopicId = backendT.Id });
        void Mk(string n, Topic? t, Place? p)
        {
            var c = new Course { Name = n, TopicId = t?.Id, PlaceId = p?.Id };
            c.Groups.Add(new LectureGroup { Name = "Session 01", Videos = { new VideoItem { Path = Path.Combine(dir, n + ".mp4"), Title = n } } });
            lib.Courses.Add(c);
        }
        Mk("C# Basics", backendT, lib.Places[0]);
        Mk("Direct", backendT, null);
        Mk("00 Orientation", oldT, null);
        Mk("Loose", null, null);
        string path = Path.Combine(dir, "hierarchy.lpl");
        Storage.SaveLibrary(path, lib);

        LoadLibrary(path, remember: false);
        Application.DoEvents();
        var backend = _library.Topics.Single(t => t.Name == "Backend");
        var old = _library.Topics.Single(t => t.Name.StartsWith("Eng"));
        var route = _library.Places.Single();
        var basics = _library.Courses.Single(c => c.Name == "C# Basics");
        var direct = _library.Courses.Single(c => c.Name == "Direct");

        // شكل الشجرة
        var tb = _tree.Nodes[0];
        Check(tb.Tag == backend && tb.Nodes.Count == 2, "topic node");
        Check(tb.Nodes[0].Tag == route && tb.Nodes[0].Nodes.Count == 1 && tb.Nodes[0].Nodes[0].Tag == basics, "place node");
        Check(tb.Nodes[1].Tag == direct, "direct course after places");
        Check(_tree.Nodes[^1].Tag is Course { Name: "Loose" }, "loose course at root");
        Check(tb.Nodes[0].ImageKey == "place", "place icon");

        // القوايم بتتبني من غير أخطاء، و«انقل لـ…» فيها المكان
        using (var menu = new ContextMenuStrip())
        {
            BuildTopicMenu(backend, menu.Items);
            BuildPlaceMenu(route, menu.Items);
            menu.Items.Add(ConvertTopicToPlaceMenu(old));
            var move = MoveCourseMenu(direct);
            var sub = move.DropDownItems.OfType<ToolStripMenuItem>().First(i => i.Text.Contains("Backend"));
            Check(sub.DropDownItems.OfType<ToolStripMenuItem>().Any(i => i.Text.Contains("ROUTE")), "move menu lists place");
            move.Dispose();
        }

        // السحب والإفلات: كورس على مكان، ومكان على موضوع ورجوعه
        DropCourse(direct, route);
        Check(_library.PlaceOf(direct) == route && NodeOf(route)!.Nodes.Count == 2, "drop course on place");
        DropPlace(route, old);
        Check(_library.TopicOf(route) == old && direct.TopicId == old.Id && NodeOf(old)!.Nodes[0].Tag == route, "drop place on topic");
        DropPlace(route, backend);
        Check(_library.TopicOf(route) == backend && basics.TopicId == backend.Id, "drop place back");
        _tree.SelectedNode = NodeOf(basics);
        MoveSelected(+1);
        Check(_library.CoursesInPlace(route).First() == direct, "move course inside place");

        // البحث باسم المكان بيعرض المكان وكورساته بس
        _searchBox.Text = "ROUTE";
        RebuildTree();
        Check(_tree.Nodes.Count == 1 && _tree.Nodes[0].Nodes.Count == 1 && _tree.Nodes[0].Nodes[0].Nodes.Count == 2, "search by place");
        _searchBox.Text = "";
        RebuildTree();

        // الشاشة الرئيسية متفلترة على المكان
        _settings.HomeTopicFilter = "p:" + route.Id;
        RefreshHome();
        Check(_library.Courses.Where(InHomeScope).Count() == 2, "home place filter");
        _settings.HomeTopicFilter = "";
        RefreshHome();

        // تحويل الموضوع القديم لمكان
        ConvertTopicToPlaceCore(_library, old, route);
        RebuildTree();
        Check(_tree.Nodes.Cast<TreeNode>().All(n => n.Tag != old) && NodeOf(route)!.Nodes.Count == 3, "convert in tree");

        // حذف المكان: الكورسات تفضل تحت الموضوع
        _library.RemovePlace(route);
        RebuildTree();
        Check(NodeOf(backend)!.Nodes.Count == 3 && _library.Courses.Count == 4, "delete place keeps courses");

        // الحفظ والفتح تاني
        _library.Places.Add(new Place { Name = "Udemy", TopicId = backend.Id });
        _library.MoveCourse(basics, null, _library.Places[0]);
        SaveLibrary();
        LoadLibrary(path, remember: false);
        Application.DoEvents();
        Check(_library.Places.Single().Name == "Udemy" && _library.PlaceOf(_library.Courses.Single(c => c.Name == "C# Basics"))?.Name == "Udemy", "save/load");
        Console.WriteLine("SMOKE hierarchy ui OK");
    }
}
