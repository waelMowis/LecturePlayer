using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// التفريعة الرئيسية: موضوع (مثلًا «البرمجة» أو «اللغات») ← مصدر ← كورسات ← محاضرات ← فيديوهات.
/// الكورس اللي ملوش موضوع بيظهر في أول مستوى زي الأول (الملفات القديمة بتفضل شغالة زي ما هي).
/// </summary>
internal sealed partial class MainForm
{
    /// <summary>الموضوع المحدد في الشجرة، أو موضوع العنصر المحدد (كورس/محاضرة/فيديو).</summary>
    private Topic? SelectedTopicContext() => _tree.SelectedNode?.Tag switch
    {
        Topic t => t,
        Place p => _library.TopicOf(p),
        Course c => _library.TopicOf(c),
        LectureGroup g when _library.FindCourseOf(g) is { } c => _library.TopicOf(c),
        VideoItem v when _library.FindCourseOf(v) is { } c => _library.TopicOf(c),
        _ => null
    };

    private void UpdateTopicNode(TreeNode node, Topic t)
    {
        var courses = _library.CoursesOf(t).ToList();
        int videos = courses.Sum(c => c.VideoCount), watched = courses.Sum(c => c.WatchedCount);
        long duration = courses.Sum(c => c.TotalDurationMs);
        bool done = videos > 0 && watched == videos;
        node.Text = Modern.Bidi(t.Name);
        node.NodeFont = _topicNodeFont;
        node.ImageKey = node.SelectedImageKey = done ? "topicDone" : "topic";
        bool current = _currentGroup != null && courses.Any(c => c.Groups.Contains(_currentGroup));
        node.ForeColor = current ? Theme.Accent : done ? Theme.Success : Theme.Text;
        node.ToolTipText = $"{t.Name}\n{courses.Count} كورس · {videos} فيديو" + (duration > 0 ? " · " + TimeFormat.Format(duration) : "")
            + (_library.PlacesOf(t).Any() ? "\n🏫 " + string.Join(" · ", _library.PlacesOf(t).Select(p => p.Name)) : "");
        _tree.SetRow(node, new LectureTreeView.RowInfo("",
            courses.Count == 1 ? "كورس" : $"{courses.Count} كورس", done));
    }

    private Topic? AddTopic(string? name = null)
    {
        if (!EnsureLibrary()) return null;
        name ??= Dialogs.Input(this, "موضوع جديد", "اسم الموضوع (مثلًا: البرمجة، اللغات، التصميم):");
        if (name == null) return null;
        Checkpoint("إضافة موضوع");
        var t = new Topic { Name = name };
        t.TouchInfo();
        int index = _tree.SelectedNode?.Tag is Topic sel ? _library.Topics.IndexOf(sel) + 1 : _library.Topics.Count;
        _library.Topics.Insert(Math.Clamp(index, 0, _library.Topics.Count), t);
        MarkDirty();
        RebuildTree();
        SelectItem(t);
        ShowStatus("اسحب الكورسات وارميها على الموضوع، أو كليك يمين على الكورس ← انقل لـ…", 6);
        return t;
    }

    /// <summary>حذف الموضوع بس — الكورسات اللي تحته بتطلع برا (مش بتتمسح).</summary>
    private void DeleteTopic(Topic t)
    {
        var courses = _library.CoursesOf(t).ToList();
        int places = _library.PlacesOf(t).Count();
        string msg = courses.Count == 0 && places == 0
            ? $"حذف الموضوع «{t.Name}»؟"
            : $"حذف الموضوع «{t.Name}»؟\n\n" + (places > 0 ? $"الـ {places} مصدر و" : "الـ ") +
              $"{courses.Count} كورس اللي تحته مش هيتمسحوا — هيطلعوا برا أي موضوع.";
        if (!Dialogs.Confirm(this, msg)) return;
        Checkpoint("حذف موضوع");
        foreach (var c in courses) { c.TopicId = null; c.TouchInfo(); }
        // الأماكن اللي جواه بتطلع برا معاه (بكورساتها)
        foreach (var p in _library.PlacesOf(t).ToList()) { p.TopicId = null; p.TouchInfo(); }
        _deletedKeys.Add(LibrarySync.TopicKey(t));
        _library.Topics.Remove(t);
        MarkDirty();
        RebuildTree();
        if (courses.Count > 0) SelectItem(courses[0]);
    }

    private void MoveCourseToTopic(Course c, Topic? t)
    {
        if (c.TopicId == t?.Id && _library.PlaceOf(c) == null) return;
        Checkpoint("نقل كورس لموضوع");
        // يتحط في آخر كورسات الموضوع الجديد
        _library.Courses.Remove(c);
        var last = _library.CoursesOf(t).LastOrDefault();
        int to = last != null ? _library.Courses.IndexOf(last) + 1 : _library.Courses.Count;
        _library.Courses.Insert(Math.Clamp(to, 0, _library.Courses.Count), c);
        _library.MoveCourse(c, t, null);
        if (t != null) t.Expanded = true;
        MarkDirty();
        RebuildTree();
        SelectItem(c);
    }

    /// <summary>تحريك كورس لفوق/لتحت جوه موضوعه بس.</summary>
    private bool SwapCourseInTopic(Course c, int direction)
    {
        var siblings = SiblingCourses(c);
        int i = siblings.IndexOf(c), j = i + direction;
        if (i < 0 || j < 0 || j >= siblings.Count) return false;
        var other = siblings[j];
        int a = _library.Courses.IndexOf(c), b = _library.Courses.IndexOf(other);
        (_library.Courses[a], _library.Courses[b]) = (_library.Courses[b], _library.Courses[a]);
        return true;
    }

    /// <summary>ترتيب المواضيع بالسحب.</summary>
    private void DropTopic(Topic topic, object? target)
    {
        Topic? anchor = target switch
        {
            Topic t => t,
            Place p => _library.TopicOf(p),
            Course c => _library.TopicOf(c),
            LectureGroup g when _library.FindCourseOf(g) is { } c => _library.TopicOf(c),
            VideoItem v when _library.FindCourseOf(v) is { } c => _library.TopicOf(c),
            _ => null
        };
        if (anchor == topic) return;
        Checkpoint("سحب وإفلات");
        int from = _library.Topics.IndexOf(topic);
        int originalAnchor = anchor != null ? _library.Topics.IndexOf(anchor) : -1;
        _library.Topics.Remove(topic);
        int to = anchor == null ? _library.Topics.Count : _library.Topics.IndexOf(anchor) + (from < originalAnchor ? 1 : 0);
        _library.Topics.Insert(Math.Clamp(to, 0, _library.Topics.Count), topic);
        MarkDirty();
        RebuildTree();
        SelectItem(topic);
    }

    /// <summary>كمّل الموضوع: آخر كورس اشتغلت عليه فيه ولسه مخلصش (أو أول كورس مخلصش).</summary>
    private void PlayTopic(Topic t)
    {
        var courses = _library.CoursesOf(t).Where(c => c.VideoCount > 0).ToList();
        var c = courses.Where(x => !x.IsCompleted).OrderByDescending(x => x.LastActivityUtc).FirstOrDefault() ?? courses.FirstOrDefault();
        if (c == null) { ShowStatus("الموضوع ده لسه مفيهوش كورسات بفيديوهات"); return; }
        PlayCourse(c);
    }

    private void BuildTopicMenu(Topic t, ToolStripItemCollection items)
    {
        items.Add(MenuItem("▶  كمّل الموضوع (آخر كورس كنت فيه)", () => PlayTopic(t)));
        AddRefreshLinksMenuItem(t, items);
        items.Add(new ToolStripSeparator());
        items.Add(MenuItem("📁  إضافة كورس من فولدر هنا…", () =>
        {
            string? path = PickFolder(this, "اختار فولدر الكورس (أو الفولدر اللي فيه كذا كورس)");
            if (path != null) ShowAddFolderDialog(path, t, null, useSelection: false);
        }));
        items.Add(MenuItem("🔗  إضافة من يوتيوب هنا…", () => AddUrlsDialog(null, t)));
        items.Add(MenuItem("🏫  مصدر جديد هنا…", () => AddPlace(t)));
        items.Add(MenuItem("إعادة تسمية…", RenameSelected, "F2"));
        items.Add(FoldMenu(t));
        var more = new List<ToolStripItem>
        {
            MenuItem("كورس جديد فاضي هنا…", () => AddCourse(topicFor: t)),
            MenuItem("ترتيب الكورسات بالاسم", () =>
            {
                Checkpoint("ترتيب الكورسات");
                var mine = _library.DirectCoursesOf(t).OrderBy(c => c.Name, NaturalComparer.Instance).ToList();
                var slots = _library.Courses.Select((c, i) => (c, i)).Where(x => mine.Contains(x.c)).Select(x => x.i).ToList();
                for (int k = 0; k < slots.Count; k++) _library.Courses[slots[k]] = mine[k];
                MarkDirty();
                RebuildTree();
            }),
            MenuItem("تعليم كل فيديوهات الموضوع كمُشاهَدة", ToggleWatchedSelected, "W"),
            MenuItem("↺ تصفير التقدم في الموضوع كله…", () =>
                ResetWatched(_library.CoursesOf(t).SelectMany(c => c.AllVideos), $"موضوع «{t.Name}»")),
            new ToolStripSeparator(),
            MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"),
            MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"),
            MenuItem("موضوع جديد بعده…", () => AddTopic())
        };
        if (_library.Topics.Count > 1) more.Add(ConvertTopicToPlaceMenu(t));
        items.Add(SubMenuOf("المزيد", more.ToArray()));
        items.Add(new ToolStripSeparator());
        items.Add(MenuItem("حذف الموضوع (الكورسات بتفضل)", DeleteSelected, "Delete"));
    }
}
