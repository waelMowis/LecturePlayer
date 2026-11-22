using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// المستوى التاني في الشجرة: «مصدر» جوه الموضوع (منصة، أكاديمية، قناة، مدرّس). في الكود اسمه Place.
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
        var courses = _library.AllCoursesUnder(p).ToList();
        int videos = courses.Sum(c => c.VideoCount), watched = courses.Sum(c => c.WatchedCount);
        long duration = courses.Sum(c => c.TotalDurationMs);
        bool done = videos > 0 && watched == videos;
        var links = LinksFor(p);
        bool linked = links.Count > 0;
        node.Text = Modern.Bidi(p.Name);
        node.NodeFont = _groupNodeFont;
        node.ImageKey = node.SelectedImageKey = NodeImageKey("place", p.IconKey, p.ColorHex, done);
        bool current = _currentGroup != null && courses.Any(c => c.Groups.Contains(_currentGroup));
        node.ForeColor = current ? Theme.Accent : done ? Theme.Success : CustomNameColor(p.ColorHex) ?? Theme.Text;
        int doneCourses = courses.Count(c => c.IsCompleted);
        node.ToolTipText = $"🏫 {p.Name}\n{courses.Count} كورس (خلصت {doneCourses}) · {watched} من {videos} فيديو"
            + (duration > 0 ? " · " + TimeFormat.Format(duration) : "")
            + (linked ? "\n🔄 فولدرات متابَعة (الجديد بيتضاف لوحده):\n" + string.Join("\n", links.Select(l => l.Path)) : "");
        _tree.SetRow(node, new LectureTreeView.RowInfo(linked ? "🔄" : "", $"{doneCourses}/{courses.Count}", done));
    }

    /// <summary>«مكان جديد» من قايمة ＋ إضافة: جوه الموضوع المحدد.</summary>
    private void AddPlaceFromMenu()
    {
        if (!EnsureLibrary()) return;
        var topic = SelectedTopicContext();
        if (topic == null && _library.Topics.Count == 0)
        {
            Dialogs.Info(this, "المصدر (المنصة أو الأكاديمية أو القناة أو المدرّس) بيبقى جوه موضوع.\nاعمل الموضوع الأول، وبعدها المصدر جواه.");
            topic = AddTopic();
            if (topic == null) return;
        }
        else if (topic == null)
        {
            ShowStatus("حدد الموضوع اللي المصدر هيبقى جواه في الشجرة الأول (أو كليك يمين على الموضوع ← مصدر جديد)", 6);
            return;
        }
        AddPlace(topic);
    }

    /// <param name="parent">مصدر أكبر (لو ده مصدر فرعي، زي مدرّس جوه منصة).</param>
    private Place? AddPlace(Topic? topic, string? name = null, Place? parent = null)
    {
        if (parent != null) topic = _library.TopicOf(parent);
        name ??= Dialogs.Input(this, parent != null ? "مصدر فرعي جديد" : "مصدر جديد",
            parent != null ? $"اسم المصدر الفرعي جوه «{parent.Name}» (مثلًا اسم المدرّس أو القناة):"
            : topic != null ? $"اسم المصدر جوه «{topic.Name}» (المنصة أو الأكاديمية أو القناة أو المدرّس):" : "اسم المصدر (المنصة أو الأكاديمية أو القناة أو المدرّس):");
        if (string.IsNullOrWhiteSpace(name)) return null;
        Checkpoint("إضافة مصدر");
        var p = new Place { Name = name.Trim(), TopicId = topic?.Id, ParentId = parent?.Id };
        p.TouchInfo();
        if (parent != null) parent.Expanded = true;
        // بعد آخر مصدر من نفس المستوى
        var after = _library.PlacesOf(topic).LastOrDefault(x => _library.ParentOf(x) == parent && x != p);
        int index = after != null ? _library.Places.IndexOf(after) + 1 : _library.Places.Count;
        _library.Places.Insert(Math.Clamp(index, 0, _library.Places.Count), p);
        if (topic != null) topic.Expanded = true;
        MarkDirty();
        RebuildTree();
        SelectItem(p);
        ShowStatus("اسحب الكورسات وارميها على المصدر، أو كليك يمين على الكورس ← انقل لـ…", 6);
        return p;
    }

    /// <summary>حذف المكان بس — الكورسات اللي فيه بتفضل تحت الموضوع على طول.</summary>
    private void DeletePlace(Place p)
    {
        var courses = _library.CoursesInPlace(p).ToList();
        var children = _library.ChildrenOf(p).ToList();
        var topic = _library.TopicOf(p);
        var parent = _library.ParentOf(p);
        string where = parent != null ? $"في «{parent.Name}»" : topic != null ? $"تحت «{topic.Name}» على طول" : "برا أي مصدر";
        string msg = $"حذف المصدر «{p.Name}»؟";
        if (courses.Count > 0) msg += $"\n\nالـ {courses.Count} كورس اللي فيه مش هيتمسحوا — هيفضلوا {where}، بتقدّمهم وعلاماتهم.";
        if (children.Count > 0) msg += $"\n\nالمصادر اللي جواه ({string.Join("، ", children.Select(x => x.Name))}) هتفضل بكورساتها، وتبقى مصادر أساسية.";
        if (LinksFor(p).Count > 0) msg += "\n\nالفولدرات المتابَعة هتفضل متابَعة، والجديد هيتحط تحت الموضوع.";
        if (!Dialogs.Confirm(this, msg)) return;
        Checkpoint("حذف مصدر");
        _deletedKeys.Add(LibrarySync.PlaceKey(p));
        _library.RemovePlace(p);
        MarkDirty();
        RebuildTree();
        if (courses.Count > 0) SelectItem(courses[0]);
        else if (topic != null) SelectItem(topic);
    }

    private void RenamePlace(Place p)
    {
        string? name = Dialogs.Input(this, "إعادة تسمية", "اسم المصدر:", p.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        Checkpoint("إعادة تسمية");
        p.Name = name.Trim();
        p.TouchInfo();
    }

    /// <summary>يحط المصدر جوه مصدر تاني (أو يطلّعه لو null)، بكورساته.</summary>
    private void NestPlaceInto(Place p, Place? parent)
    {
        if (parent != null && !_library.CanNest(p, parent))
        {
            Dialogs.Info(this, _library.ChildrenOf(p).Any()
                ? $"«{p.Name}» جواه مصادر فرعية، والمصادر مستويين بس.\nطلّع المصادر اللي جواه الأول، أو انقل كورساتها."
                : $"«{parent.Name}» نفسه مصدر فرعي، والمصادر مستويين بس.");
            return;
        }
        if (_library.ParentOf(p) == parent) return;
        Checkpoint("نقل مصدر");
        _library.NestPlace(p, parent);
        if (parent != null) parent.Expanded = true;
        MarkDirty();
        RebuildTree();
        SelectItem(p);
    }

    private void MovePlaceToTopic(Place p, Topic? t)
    {
        if (p.TopicId == t?.Id && _library.ParentOf(p) == null) return;
        Checkpoint("نقل مصدر لموضوع");
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
        var courses = _library.AllCoursesUnder(p).Where(c => c.VideoCount > 0).ToList();
        var c = courses.Where(x => !x.IsCompleted).OrderByDescending(x => x.LastActivityUtc).FirstOrDefault() ?? courses.FirstOrDefault();
        if (c == null) { ShowStatus("المصدر ده لسه مفيهوش كورسات بفيديوهات"); return; }
        PlayCourse(c);
    }

    private void BuildPlaceMenu(Place p, ToolStripItemCollection items)
    {
        items.Add(MenuItem("▶  كمّل آخر كورس في المصدر ده", () => PlayPlace(p)));
        AddRefreshLinksMenuItem(p, items);
        items.Add(new ToolStripSeparator());
        items.Add(MenuItem("📁  إضافة كورس من فولدر هنا…", () =>
        {
            string? path = PickFolder(this, $"اختار فولدر الكورس (أو الفولدر اللي فيه كذا كورس) من «{p.Name}»");
            if (path != null) ShowAddFolderDialog(path, null, p, useSelection: false);
        }));
        items.Add(MenuItem("🔗  إضافة من يوتيوب هنا…", () => AddUrlsDialog(null, p)));
        if (_library.ParentOf(p) == null)
            items.Add(MenuItem("🏫  مصدر فرعي جديد هنا (مدرّس، قناة…)…", () => AddPlace(null, parent: p)));
        items.Add(MenuItem("إعادة تسمية…", RenameSelected, "F2"));
        items.Add(AppearanceMenuItem(p));
        items.Add(ExportMenuItem(p));
        items.Add(MovePlaceToTopicMenu(p));
        items.Add(FoldMenu(p));
        items.Add(SubMenuOf("المزيد",
            MenuItem("كورس جديد فاضي هنا…", () => AddCourse(placeFor: p)),
            MenuItem("ترتيب الكورسات بالاسم", () =>
            {
                Checkpoint("ترتيب الكورسات");
                var mine = _library.CoursesInPlace(p).OrderBy(c => c.Name, NaturalComparer.Instance).ToList();
                var slots = _library.Courses.Select((c, i) => (c, i)).Where(x => mine.Contains(x.c)).Select(x => x.i).ToList();
                for (int k = 0; k < slots.Count; k++) _library.Courses[slots[k]] = mine[k];
                MarkDirty();
                RebuildTree();
            }),
            MenuItem("تعليم كل فيديوهات المصدر كمُشاهَدة", ToggleWatchedSelected, "W"),
            MenuItem("↺ تصفير التقدم في المصدر كله…", () =>
                ResetWatched(_library.AllCoursesUnder(p).SelectMany(c => c.AllVideos), $"«{p.Name}»")),
            new ToolStripSeparator(),
            MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"),
            MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"),
            MenuItem("مصدر جديد بعده…", () => AddPlace(_library.TopicOf(p), parent: _library.ParentOf(p)))));
        items.Add(new ToolStripSeparator());
        items.Add(MenuItem("حذف المصدر (الكورسات بتفضل)", DeleteSelected, "Delete"));
    }

    private ToolStripMenuItem MovePlaceToTopicMenu(Place p)
    {
        var current = _library.TopicOf(p);
        var menu = new ToolStripMenuItem("انقل لـ…") { ForeColor = Theme.Text };
        // جوه مصدر تاني (يبقى فرعي)، أو يطلع مستوى لفوق
        var parents = _library.Places.Where(x => _library.CanNest(p, x) && _library.ParentOf(p) != x).ToList();
        foreach (var x in parents)
        {
            var target = x;
            string where = _library.TopicOf(x) is { } xt && xt != current ? $"  ‹{xt.Name}›" : "";
            menu.DropDownItems.Add(MenuItem("🏫  جوه «" + Modern.Bidi(x.Name) + "»" + where, () => NestPlaceInto(p, target)));
        }
        if (_library.ParentOf(p) is { } pp)
            menu.DropDownItems.Add(MenuItem($"طلّعه برا «{pp.Name}» (مصدر أساسي)", () => NestPlaceInto(p, null)));
        if (menu.DropDownItems.Count > 0) menu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var t in _library.Topics)
        {
            var target = t;
            var item = MenuItem("📚  " + Modern.Bidi(t.Name), () => MovePlaceToTopic(p, target));
            item.Checked = t == current && _library.ParentOf(p) == null;
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
            var places = _library.OrderedPlacesOf(t).ToList();
            if (places.Count == 0)
            {
                var item = MenuItem("📚  " + Modern.Bidi(t.Name), () => MoveCourseTo(c, topic, null));
                item.Checked = curTopic == t && curPlace == null;
                menu.DropDownItems.Add(item);
                continue;
            }
            var sub = new ToolStripMenuItem("📚  " + Modern.Bidi(t.Name)) { ForeColor = Theme.Text, Checked = curTopic == t };
            foreach (var (p, depth) in places)
            {
                var place = p;
                var pi = MenuItem((depth > 0 ? "        ↳ " : "🏫  ") + Modern.Bidi(p.Name), () => MoveCourseTo(c, topic, place));
                pi.Checked = curPlace == p;
                sub.DropDownItems.Add(pi);
            }
            sub.DropDownItems.Add(new ToolStripSeparator());
            var direct = MenuItem($"تحت «{t.Name}» على طول (من غير مصدر)", () => MoveCourseTo(c, topic, null));
            direct.Checked = curTopic == t && curPlace == null;
            sub.DropDownItems.Add(direct);
            sub.DropDownItems.Add(MenuItem("مصدر جديد هنا…", () =>
            {
                var np = AddPlace(topic);
                if (np != null) MoveCourseTo(c, topic, np);
            }));
            menu.DropDownItems.Add(sub);
        }

        // أماكن برا أي موضوع (بتحصل لو الموضوع بتاعها اتمسح)
        foreach (var (p, depth) in _library.OrderedPlacesOf(null))
        {
            var place = p;
            var pi = MenuItem((depth > 0 ? "        ↳ " : "🏫  ") + Modern.Bidi(p.Name), () => MoveCourseTo(c, null, place));
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
    /// مصدر اتساب على موضوع ← مصدر أساسي في الموضوع ده. على مصدر تاني ← يبقى فرعي جواه
    /// (لو ينفع: مستويين بس)، وإلا يتحط جنبه. على كورس ← كأنه اتساب على مصدر الكورس.
    /// </summary>
    private void DropPlace(Place place, object? target)
    {
        var course = target switch
        {
            Course c => c,
            LectureGroup g => _library.FindCourseOf(g),
            VideoItem v => _library.FindCourseOf(v),
            _ => null
        };
        Place? onto = target as Place ?? (course != null ? _library.PlaceOf(course) : null);
        Topic? topic = target is Topic t ? t : onto != null ? _library.TopicOf(onto) : course != null ? _library.TopicOf(course) : null;
        if (onto == place || (onto != null && (_library.ParentOf(place) == onto || _library.ParentOf(onto) == place))) return;

        Place? parent = null;
        Place? anchor = null; // يتحط بعده في الترتيب
        if (onto != null)
        {
            if (_library.CanNest(place, onto)) { parent = onto; anchor = _library.ChildrenOf(onto).LastOrDefault() ?? onto; }
            else if (_library.ParentOf(onto) is { } op && _library.CanNest(place, op)) { parent = op; anchor = onto; }
            else anchor = _library.ParentOf(onto) ?? onto;
        }

        Checkpoint("سحب وإفلات");
        if (place.TopicId != topic?.Id) _library.MovePlace(place, topic);
        if (_library.ParentOf(place) != parent) _library.NestPlace(place, parent);
        _library.Places.Remove(place);
        if (anchor == null) anchor = _library.PlacesOf(topic).LastOrDefault(x => x != place);
        int to = anchor != null ? _library.Places.IndexOf(anchor) + 1 : _library.Places.Count;
        _library.Places.Insert(Math.Clamp(to, 0, _library.Places.Count), place);
        if (topic != null) topic.Expanded = true;
        if (parent != null) parent.Expanded = true;
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

        // شاشة «إضافة كورس» بجد: فولدر مدرّس فيه كورسين ← جوه مصدر، ومتابَع
        string drive = Path.Combine(dir, "add-folder");
        if (Directory.Exists(drive)) Directory.Delete(drive, true);
        void Touch(string rel) { string f = Path.Combine(drive, rel); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, "x"); }
        Touch(@"Teacher\Course A\Lesson 1\Videos\Part 1.mp4");
        Touch(@"Teacher\Course A\Lesson 1\Slides\deck.pdf");
        Touch(@"Teacher\Course B\Lesson 1\Part 1.mp4");
        var udemy = _library.Places.Single();
        var topicB = _library.Topics.Single(t => t.Name == "Backend");
        AddFolderForm.AutoAcceptForSmoke = true;
        try { ShowAddFolderDialog(Path.Combine(drive, "Teacher"), null, udemy, useSelection: false); }
        finally { AddFolderForm.AutoAcceptForSmoke = false; }
        // فولدر المدرّس جوه المنصة ← مصدر فرعي باسمه (العلامة بتتعلّم لوحدها) وكورساته جواه
        var teacher = _library.ChildrenOf(udemy).SingleOrDefault(x => x.Name == "Teacher");
        Check(teacher != null && _library.CoursesInPlace(teacher!).Count(c => c.Name is "Course A" or "Course B") == 2, "add dialog: teacher sub-source");
        Check(_library.Links.Count == 1 && _library.Links[0].PlaceId == teacher!.Id && !_library.Links[0].SingleCourse, "add dialog: followed");
        Check(_library.PathOf(_library.Courses.Single(c => c.Name == "Course A")) == "Backend › Udemy › Teacher › Course A", "add dialog: path");
        // نفس اللي حصل عند المستخدم: الكورسات اتضافت في المنصة على طول، وبعدين يضيف الفولدر تاني كمصدر فرعي ← تتنقل جواه ومتتكررش
        foreach (var cc in _library.Courses.Where(c => c.Name is "Course A" or "Course B").ToList()) _library.MoveCourse(cc, null, udemy);
        _library.Links[0].PlaceId = udemy.Id;
        _library.RemovePlace(teacher);
        RebuildTree();
        AddFolderForm.AutoAcceptForSmoke = true;
        try { ShowAddFolderDialog(Path.Combine(drive, "Teacher"), null, udemy, useSelection: false); }
        finally { AddFolderForm.AutoAcceptForSmoke = false; }
        teacher = _library.ChildrenOf(udemy).SingleOrDefault(x => x.Name == "Teacher");
        Check(teacher != null && _library.CoursesInPlace(teacher!).Count() == 2 && _library.Courses.Count(c => c.Name == "Course A") == 1
              && _library.Links.Single().PlaceId == teacher!.Id, "add again as sub-source moves courses");
        Check(NodeOf(udemy)!.Nodes[0].Tag == teacher && NodeOf(teacher!)!.Nodes.Count == 2, "tree: sub-source under source");
        // سحب مصدر على مصدر ← يبقى فرعي جواه، وعلى موضوع ← يرجع أساسي
        var udemy2 = AddPlace(topicB, "Second");
        DropPlace(udemy2!, udemy);
        Check(_library.ParentOf(udemy2!) == udemy, "drop source on source nests");
        DropPlace(udemy2!, teacher);
        Check(_library.ParentOf(udemy2!) == udemy, "drop on sub-source → sibling");
        DropPlace(udemy2!, topicB);
        Check(_library.ParentOf(udemy2!) == null, "drop on topic → top level");
        DropPlace(udemy, udemy2!);
        Check(_library.ParentOf(udemy) == null, "source with children can't nest");
        _library.RemovePlace(udemy2!);
        RebuildTree();
        var courseA = _library.Courses.Single(c => c.Name == "Course A");
        Check(courseA.Groups.Single().Attachments.Count == 1 && _library.LinkOf(courseA) != null, "add dialog: attachments/link");
        // نفس الفولدر تاني ← مايتكررش
        AddFolderForm.AutoAcceptForSmoke = true;
        try { ShowAddFolderDialog(Path.Combine(drive, "Teacher"), topicB, null, useSelection: false); }
        finally { AddFolderForm.AutoAcceptForSmoke = false; }
        Check(_library.Courses.Count(c => c.Name == "Course A") == 1 && _library.Links.Count == 1, "add dialog: same folder duplicated");
        Check(_library.TopicOf(courseA) == topicB && _library.PlaceOf(courseA) == null, "add dialog: same folder moved to new destination");
        _library.RemovePlace(teacher!);
        // كورس واحد (فولدر جواه محاضرات)
        Touch(@"Single\Week 1\a.mp4");
        Touch(@"Single\Week 2\b.mp4");
        AddFolderForm.AutoAcceptForSmoke = true;
        try { ShowAddFolderDialog(Path.Combine(drive, "Single"), topicB, null, useSelection: false); }
        finally { AddFolderForm.AutoAcceptForSmoke = false; }
        var single = _library.Courses.SingleOrDefault(c => c.Name == "Single");
        Check(single != null && single.Groups.Count == 2 && _library.Links.Any(l => l.SingleCourse), "add dialog: single course");
        // القوايم الجديدة بتتبني من غير أخطاء
        {
            _tree.SelectedNode = NodeOf(courseA);
            ShowNodeMenu(NodeOf(courseA)!, Point.Empty);
            _nodeMenu.Close();
            ShowNodeMenu(NodeOf(courseA.Groups[0])!, Point.Empty);
            _nodeMenu.Close();
            ShowNodeMenu(NodeOf(courseA.Groups[0].Videos[0])!, Point.Empty);
            _nodeMenu.Close();
            ShowNodeMenu(NodeOf(udemy)!, Point.Empty);
            _nodeMenu.Close();
            ShowNodeMenu(NodeOf(topicB)!, Point.Empty);
            _nodeMenu.Close();
            ShowEmptyAreaMenu(Point.Empty);
            _nodeMenu.Close();
        }
        Directory.Delete(drive, true);

        // روابط يوتيوب: بتتحط في المكان المختار (نفس شكل المستخدم: YOUTUBE ← ISLAMIC ← سورة البقرة)، مش في آخر كورس في المكتبة
        var yt = new Topic { Name = "YOUTUBE" };
        _library.Topics.Add(yt);
        var islamic = new Place { Name = "ISLAMIC", TopicId = yt.Id };
        var baqara = new Place { Name = "سورة البقرة", TopicId = yt.Id, ParentId = islamic.Id };
        _library.Places.Add(islamic);
        _library.Places.Add(baqara);
        RebuildTree();
        var pl = new OnlineListing("Playlist X", true, new List<OnlineEntry> { new("https://www.youtube.com/watch?v=aaaaaaaaaaa", "A", 60) });
        var one = new OnlineListing(null, false, new List<OnlineEntry> { new("https://www.youtube.com/watch?v=bbbbbbbbbbb", "B", 60) });
        int before = _library.Courses.Count;
        var st = new UrlTarget(this, baqara);
        var (pc, pg) = st.Place(pl, "https://www.youtube.com/playlist?list=PLx");
        Check(_library.PlaceOf(pc) == baqara && pc.Name == "Playlist X" && pg.SourceUrl != null, "url: playlist → course named after it");
        // فيديوهات لوحدها + اسم كورس جديد ← كورس واحد بالاسم ده ومحاضرة «الفيديوهات»
        var named = new UrlTarget(this, baqara, "سورة البقرة");
        var (nc1, ng1) = named.Place(one, "https://www.youtube.com/watch?v=bbbbbbbbbbb");
        var (nc2, ng2) = named.Place(one, "https://www.youtube.com/watch?v=ccccccccccc");
        var (nc3, ng3) = named.Place(pl, "https://www.youtube.com/playlist?list=PLy");
        Check(nc1 == nc2 && nc2 == nc3 && nc1.Name == "سورة البقرة" && _library.PlaceOf(nc1) == baqara, "url: named course holds everything");
        Check(ng1 == ng2 && ng1.Name == UrlTarget.SinglesLecture && ng3.Name == "Playlist X" && ng3.SourceUrl != null && nc1.Groups.Count == 2, "url: lectures in named course");
        Check(!_library.Courses.Any(c => c.Name == "فيديوهات من يوتيوب") && !_library.AllGroups.Any(g => g.Name == "فيديوهات من النت"), "url: no meaningless names");
        // فيديو لوحده من غير اسم (مثلًا اتسحب على موضوع) ← الكورس باسم الفيديو
        var (tc, tg) = new UrlTarget(this, yt).Place(one, "u");
        Check(_library.TopicOf(tc) == yt && _library.PlaceOf(tc) == null && tc.Name == "B" && tg.Name == UrlTarget.SinglesLecture, "url: single without name → named after video");
        var (rc, _) = new UrlTarget(this, null).Place(pl, "u");
        Check(_library.TopicOf(rc) == null && rc.Name == "Playlist X", "url: nothing selected → new course at root");
        // كورس موجود ← القائمة محاضرة جديدة، والفيديو في «الفيديوهات» (مش في أول محاضرة في الكورس)
        var (ucc, ucg) = new UrlTarget(this, pc).Place(pl, "u2");
        Check(ucc == pc && pc.Groups.Count == 2 && ucg.SourceUrl == "u2", "url: course → new lecture in it");
        var (vc, vg) = new UrlTarget(this, pc).Place(one, "u3");
        Check(vc == pc && vg.Name == UrlTarget.SinglesLecture && vg != pc.Groups[0] && pc.Groups.Count == 3, "url: single into existing course → «الفيديوهات»");
        Check(_library.Courses.Count == before + 4, "url: courses created " + (_library.Courses.Count - before));

        // شاشة «إضافة من يوتيوب» نفسها: كورس جديد باسم، وكورس موجود من القايمة، ومحاضرة محددة
        try
        {
            UrlForm.AutoFillForSmoke = ("https://www.youtube.com/watch?v=aaaaaaaaaaa", "سورة الكهف", 0);
            var r1 = UrlForm.Show(this, "", "«سورة البقرة»", _library.CoursesInPlace(baqara).ToList(), null, null);
            Check(r1 != null && r1.Course == null && r1.NewCourseName == "سورة الكهف" && r1.Urls.Count == 1, "url form: new named course");
            UrlForm.AutoFillForSmoke = ("https://www.youtube.com/watch?v=aaaaaaaaaaa", null, 1);
            var list = _library.CoursesInPlace(baqara).ToList();
            var r2 = UrlForm.Show(this, "", "«سورة البقرة»", list, null, null);
            Check(r2 != null && r2.Course == list[0] && r2.NewCourseName == null, "url form: existing course");
            UrlForm.AutoFillForSmoke = ("https://www.youtube.com/playlist?list=PLz", null, 0);
            var r3 = UrlForm.Show(this, "", "«سورة البقرة»", list, null, null);
            Check(r3 != null && r3.Course == null && r3.NewCourseName == null, "url form: playlist without name allowed");
            UrlForm.AutoFillForSmoke = ("https://www.youtube.com/watch?v=aaaaaaaaaaa", null, 0);
            var r4 = UrlForm.Show(this, "", "x", list, pc, pc.Groups[0]);
            Check(r4 != null && r4.Course == null, "url form: lecture target");
        }
        finally { UrlForm.AutoFillForSmoke = null; }
        // الأمر موجود في قايمة الموضوع والمصدر
        RebuildTree();
        ShowNodeMenu(NodeOf(baqara)!, Point.Empty);
        bool inPlaceMenu = _nodeMenu.Items.OfType<ToolStripMenuItem>().Any(i => i.Text.Contains("يوتيوب"));
        _nodeMenu.Close();
        ShowNodeMenu(NodeOf(yt)!, Point.Empty);
        bool inTopicMenu = _nodeMenu.Items.OfType<ToolStripMenuItem>().Any(i => i.Text.Contains("يوتيوب"));
        _nodeMenu.Close();
        Check(inPlaceMenu && inTopicMenu, "youtube item in topic/source menus");

        // الطي والتوسيع: YOUTUBE ← ISLAMIC ← سورة البقرة ← Playlist X ← محاضرة
        TreeNode N(object o) => NodeOf(o) ?? throw new Exception("hierarchy ui: fold: node missing for " + o);
        // المحاضرة لازم يبقى فيها فيديو عشان تتفتح في الشجرة (عقدة فاضية مالهاش فتح وقفل)
        if (pc.Groups[0].Videos.Count == 0) pc.Groups[0].Videos.Add(VideoItem.FromUrl(new OnlineEntry("https://www.youtube.com/watch?v=ddddddddddd", "D", 60)));
        ExpandToLevel(0);
        Check(!N(yt).IsExpanded && !_library.Places.Any(x => x.Expanded) && !_library.Courses.Any(x => x.Expanded), "fold: topics only");
        ExpandToLevel(1);
        Check(N(yt).IsExpanded && !N(islamic).IsExpanded, "fold: up to sources");
        ExpandToLevel(2);
        Check(N(islamic).IsExpanded && N(baqara).IsExpanded && !N(pc).IsExpanded, "fold: up to courses");
        ExpandToLevel(3);
        Check(N(pc).IsExpanded && !N(pc.Groups[0]).IsExpanded, "fold: up to lectures");
        ExpandToLevel(4);
        Check(N(pc.Groups[0]).IsExpanded, "fold: all open");
        SetSubtreeExpanded(islamic, false);
        Check(N(islamic).IsExpanded && !N(baqara).IsExpanded && !pc.Expanded, "fold: close inside source");
        SetSubtreeExpanded(islamic, true);
        Check(N(baqara).IsExpanded && N(pc).IsExpanded && N(pc.Groups[0]).IsExpanded, "fold: open inside source");
        SetSubtreeExpanded(pc, false);
        Check(N(pc).IsExpanded && !N(pc.Groups[0]).IsExpanded, "fold: close inside course");
        ShowNodeMenu(N(islamic), Point.Empty);
        bool foldInMenu = _nodeMenu.Items.OfType<ToolStripMenuItem>().Any(i => i.Text == "طي وتوسيع");
        _nodeMenu.Close();
        Check(foldInMenu, "fold item in source menu");
        Console.WriteLine("SMOKE hierarchy ui OK");
    }
}
