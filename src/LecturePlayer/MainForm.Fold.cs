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
}
