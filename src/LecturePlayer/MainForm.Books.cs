using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>فصل (أو جزء من فصل) في شجرة الكتب.</summary>
internal sealed record BookSection(Book Book, string Title, int From, int To, int Level);
/// <summary>علامة صفحة في شجرة الكتب.</summary>
internal sealed record BookMarkRef(Book Book, PageMark Mark);

/// <summary>
/// «الكتب»: رف منفصل عن الكورسات (رفوف ← كتب ← فصول)، وقارئ PDF جوه مساحة الفيديو بيتابع الصفحة اللي وقفت عندها
/// والصفحات اللي اتقرت فعلًا (قعدت قدامك المدة المحددة)، ووقت القراية بيدخل سجل المذاكرة.
/// القوايم بنفس قاعدة الشجرة: ▶ فتح ← ➕ إضافة ← 🔄 هات الجديد ← ✏ تعديل ← ↔ نقل ← ✔ التقدّم ← 📤 المصدر ← 🗑 حذف.
/// </summary>
internal sealed partial class MainForm
{
    private ReaderPane? _reader;
    private bool _readerShown;
    private Book? _currentBook;
    private int _readerGen;
    private bool _readerActive;
    private DateTime _readerActiveUtc;
    private int _pagesSession;
    private DateTime _pagesSessionStartUtc;

    private readonly Card _shelfCard = new();
    private readonly LectureTreeView _shelfTree = new();
    private readonly TextBox _shelfSearch = new();
    private readonly Label _shelfEmpty = new();
    private GlyphButton? _activityBooks;
    private bool _shelfMode;
    private readonly Dictionary<object, TreeNode> _shelfNodes = new();
    private readonly Queue<Book> _bookProbeQueue = new();
    private bool _bookProbing;

    /// <summary>كتاب مفتوح في القارئ دلوقتي.</summary>
    private bool ReaderShown => _readerShown && _reader != null && _currentBook != null;

    /// <summary>للفحص: الأسامي اللي المستخدم كان هيكتبها في الشبابيك.</summary>
    internal static Func<string, string?>? SmokeAskText;

    // =====================================================================
    //  اللوحة الجانبية: «الكتب»
    // =====================================================================

    private void BuildShelfPanel()
    {
        _shelfCard.Dock = DockStyle.Fill;
        _shelfCard.Visible = false;

        var titleRow = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(35), BackColor = Theme.Panel, Padding = new Padding(8, 6, 14, 4) };
        titleRow.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, Text = "الكتب", ForeColor = Theme.Text, RightToLeft = RightToLeft.Yes,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight
        });
        var addMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        addMenu.Opening += (_, e) =>
        {
            foreach (var old in addMenu.Items.Cast<ToolStripItem>().ToList()) old.Dispose();
            addMenu.Items.Clear();
            BuildShelfAddMenu(_shelfTree.SelectedNode?.Tag, addMenu.Items);
            e.Cancel = false;
        };
        addMenu.Items.Add(new ToolStripMenuItem("…"));
        var addButton = new PillButton("＋  إضافة", PillStyle.Ghost) { Dock = DockStyle.Left, Width = LogicalToDeviceUnits(82), Margin = Padding.Empty, TabStop = false };
        addButton.Click += (_, _) => addMenu.Show(addButton, new Point(0, addButton.Height + 2));
        _toolTip.SetToolTip(addButton, "رف جديد، أو كتب (PDF / EPUB) أو عروض PowerPoint من ملفات أو فولدر — بتتحط في الرف اللي واقف عليه");
        titleRow.Controls.Add(addButton);
        var searchAll = new GlyphButton(Glyphs.Search, 11f) { Size = new Size(LogicalToDeviceUnits(26), LogicalToDeviceUnits(24)), Dock = DockStyle.Left };
        searchAll.Click += (_, _) => ShowBookSearch();
        _toolTip.SetToolTip(searchAll, "دوّر على كلمة جوه كل الكتب");
        titleRow.Controls.Add(searchAll);

        var searchRow = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(38), Padding = new Padding(10, 2, 14, 8), BackColor = Theme.Panel };
        _shelfSearch.RightToLeft = RightToLeft.Yes;
        _shelfSearch.PlaceholderText = "دوّر على كتاب…";
        _shelfSearch.Font = new Font("Segoe UI", 9.5f);
        _shelfSearch.TextChanged += (_, _) => RebuildShelf();
        searchRow.Controls.Add(new RoundedHost(_shelfSearch, searchIcon: true, padding: 7) { Dock = DockStyle.Fill });

        _shelfTree.Dock = DockStyle.Fill;
        _shelfTree.BorderStyle = BorderStyle.None;
        _shelfTree.BackColor = Theme.Panel;
        _shelfTree.ForeColor = Theme.Text;
        _shelfTree.Font = _boldNodeFont;
        _shelfTree.FullRowSelect = true;
        _shelfTree.HideSelection = false;
        _shelfTree.ShowLines = false;
        _shelfTree.ShowRootLines = true;
        _shelfTree.ShowPlusMinus = true;
        _shelfTree.ShowNodeToolTips = true;
        _shelfTree.AllowDrop = true;
        _shelfTree.Indent = LogicalToDeviceUnits(18);
        _shelfTree.ItemHeight = LogicalToDeviceUnits(26);
        _shelfTree.ImageList = Theme.CreateTreeIcons(LogicalToDeviceUnits(16));
        _shelfTree.HandleCreated += (_, _) => NativeMethods.UseDarkExplorerTheme(_shelfTree);
        _shelfTree.NodeMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            _shelfTree.SelectedNode = e.Node;
            ShowShelfMenu(e.Node, e.Location);
        };
        _shelfTree.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && _shelfTree.GetNodeAt(e.Location) == null) ShowShelfMenu(null, e.Location);
        };
        _shelfTree.NodeMouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenShelfNode(e.Node); };
        _shelfTree.AfterExpand += (_, e) => RememberShelfExpanded(e.Node, true);
        _shelfTree.AfterCollapse += (_, e) => RememberShelfExpanded(e.Node, false);
        _shelfTree.ItemDrag += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && e.Item is TreeNode { Tag: Shelf or Book } n && _shelfSearch.Text.Length == 0)
                _shelfTree.DoDragDrop(n, DragDropEffects.Move);
        };
        _shelfTree.DragOver += OnShelfDragOver;
        _shelfTree.DragDrop += OnShelfDragDrop;

        _shelfEmpty.Dock = DockStyle.Fill;
        _shelfEmpty.Visible = false;
        _shelfEmpty.ForeColor = Theme.Muted;
        _shelfEmpty.BackColor = Theme.Panel;
        _shelfEmpty.TextAlign = ContentAlignment.MiddleCenter;
        _shelfEmpty.RightToLeft = RightToLeft.Yes;
        _shelfEmpty.Cursor = Cursors.Hand;
        _shelfEmpty.AllowDrop = true;
        _shelfEmpty.Text = "رف الكتب فاضي\n\nاضغط هنا عشان تضيف كتب (PDF)\nمن ملفات أو فولدر على الجهاز،\nأو رابط Google Drive / Dropbox / OneDrive\n\nأو اسحب الملفات وارميها هنا";
        _shelfEmpty.Click += (_, _) => addMenu.Show(_shelfEmpty, new Point(_shelfEmpty.Width / 2, _shelfEmpty.Height / 2));
        _shelfEmpty.DragEnter += (_, e) => e.Effect = BookFilesIn(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        _shelfEmpty.DragDrop += (_, e) => DropBookFiles(BookFilesIn(e.Data), null);

        _shelfCard.Controls.Add(_shelfTree);
        _shelfCard.Controls.Add(_shelfEmpty);
        _shelfCard.Controls.Add(searchRow);
        _shelfCard.Controls.Add(titleRow);
        _sidePanel.Controls.Add(_shelfCard);
        _shelfCard.BringToFront();
        if (_settings.SideView == "books") ShowShelf(true);
    }

    private void UpdateActivityIndicators()
    {
        bool visible = !_split.Panel2Collapsed;
        if (_activityExplorer is { } ex) ex.Active = visible && !_shelfMode;
        if (_activityBooks != null) _activityBooks.Active = visible && _shelfMode;
    }

    /// <summary>زرار «مكتبتي»: من «الكتب» بيرجع للكورسات، وإلا بيخفي/يظهر اللوحة.</summary>
    private void OnExplorerActivity()
    {
        if (_fullscreen) return;
        if (_shelfMode && !_split.Panel2Collapsed) { ShowShelf(false); return; }
        if (_shelfMode) ShowShelf(false);
        ToggleSidePanel();
    }

    /// <summary>زرار «الكتب» (Ctrl+Shift+B): يظهر الرف، ولو ظاهر يخفي اللوحة.</summary>
    private void ToggleShelf()
    {
        if (_fullscreen) return;
        if (_split.Panel2Collapsed) { ShowShelf(true); ToggleSidePanel(); return; }
        if (_shelfMode) { ToggleSidePanel(); return; }
        ShowShelf(true);
    }

    private void ShowShelf(bool on)
    {
        _shelfMode = on;
        _settings.SideView = on ? "books" : "library";
        _shelfCard.Visible = on;
        _treeCard.Visible = !on;
        _notesPanel.Visible = !on;
        _notesSplitter.Visible = !on && !_notesCollapsed;
        if (on)
        {
            _shelfCard.BringToFront();
            RebuildShelf();
        }
        UpdateActivityIndicators();
    }

    // =====================================================================
    //  الشجرة
    // =====================================================================

    private void RebuildShelf()
    {
        if (_shelfTree.IsDisposed) return;
        // الكتاب المفتوح ممكن يكون اتبدل بنسخة تانية (تراجع/مزامنة)
        if (_currentBook != null && !_library.Books.Contains(_currentBook))
        {
            var same = _library.Books.FirstOrDefault(b => b.Id == _currentBook.Id);
            if (same != null) { same.LocalPath = _currentBook.LocalPath; _currentBook = same; }
            else CloseReader(showHome: true);
        }
        string filter = _shelfSearch.Text.Trim();
        bool filtering = filter.Length > 0;
        object? selected = _shelfTree.SelectedNode?.Tag;
        int top = _shelfTree.TopNode?.Index ?? 0;

        _shelfTree.BeginUpdate();
        try
        {
            _shelfTree.Nodes.Clear();
            _shelfTree.ClearRows();
            _shelfNodes.Clear();
            var expand = new List<TreeNode>();

            bool BookMatches(Book b) => Matches(b.Title, filter) || b.Author != null && Matches(b.Author, filter);

            TreeNode BuildBook(Book b)
            {
                var n = new TreeNode { Tag = b };
                _shelfNodes[b] = n;
                UpdateBookNode(n, b);
                foreach (var (title, from, to, parts) in b.Sections())
                {
                    var sn = new TreeNode { Tag = new BookSection(b, title, from, to, 0) };
                    UpdateSectionNode(sn, (BookSection)sn.Tag);
                    foreach (var (pt, pf, pto) in parts)
                    {
                        var pn = new TreeNode { Tag = new BookSection(b, pt, pf, pto, 1) };
                        UpdateSectionNode(pn, (BookSection)pn.Tag);
                        sn.Nodes.Add(pn);
                    }
                    n.Nodes.Add(sn);
                }
                // العلامات والتظليلات بترتيب الصفحات
                var notes = b.Marks.Select(m => (m.Page, Tag: (object)new BookMarkRef(b, m)))
                    .Concat(b.Highlights.Select(h => (h.Page, Tag: (object)new BookHighlightRef(b, h))))
                    .OrderBy(x => x.Page).ToList();
                foreach (var (_, tag) in notes)
                {
                    var mn = new TreeNode { Tag = tag };
                    if (tag is BookMarkRef mr) UpdateMarkNode(mn, b, mr.Mark); else UpdateHighlightNode(mn, b, ((BookHighlightRef)tag).Highlight);
                    n.Nodes.Add(mn);
                }
                if (b.Expanded && n.Nodes.Count > 0) expand.Add(n);
                return n;
            }

            TreeNode? BuildShelfNode(Shelf s, int depth)
            {
                if (depth > 12) return null;
                bool selfMatch = !filtering || Matches(s.Name, filter);
                var n = new TreeNode { Tag = s };
                foreach (var child in _library.ShelvesIn(s))
                    if (BuildShelfNode(child, depth + 1) is { } cn) n.Nodes.Add(cn);
                foreach (var b in _library.BooksOn(s))
                    if (selfMatch || BookMatches(b)) n.Nodes.Add(BuildBook(b));
                if (filtering && !selfMatch && n.Nodes.Count == 0) return null;
                _shelfNodes[s] = n;
                UpdateShelfNode(n, s);
                if (s.Expanded || filtering) expand.Add(n);
                return n;
            }

            foreach (var s in _library.ShelvesIn(null))
                if (BuildShelfNode(s, 0) is { } n) _shelfTree.Nodes.Add(n);
            foreach (var b in _library.BooksOn(null))
                if (!filtering || BookMatches(b)) _shelfTree.Nodes.Add(BuildBook(b));
            foreach (var n in expand) n.Expand();

            if (selected != null && ShelfNodeOf(selected) is { } sel) _shelfTree.SelectedNode = sel;
            if (_shelfTree.Nodes.Count > 0 && top < _shelfTree.Nodes.Count) _shelfTree.TopNode = _shelfTree.Nodes[Math.Min(top, _shelfTree.Nodes.Count - 1)];
        }
        finally { _shelfTree.EndUpdate(); }
        _shelfTree.Invalidate(); // بعد EndUpdate الصفوف الجديدة مش دايمًا بتترسم لوحدها
        bool empty = _library.Books.Count == 0 && _library.Shelves.Count == 0;
        _shelfEmpty.Visible = empty && !filtering;
        _shelfTree.Visible = !_shelfEmpty.Visible;
    }

    private TreeNode? ShelfNodeOf(object tag)
    {
        if (_shelfNodes.TryGetValue(tag, out var n)) return n;
        IEnumerable<TreeNode> All(TreeNodeCollection c) => c.Cast<TreeNode>().SelectMany(x => All(x.Nodes).Prepend(x));
        return All(_shelfTree.Nodes).FirstOrDefault(x => Equals(x.Tag, tag));
    }

    private string ShelfImageKey(string kind, string glyph, string? icon, string? colorHex, Color fallback, bool done)
    {
        string g = IconGlyphs.Exists(icon) ? icon! : glyph;
        var c = IconGlyphs.ParseHex(colorHex) ?? fallback;
        string key = $"b|{kind}|{g}|{IconGlyphs.ToHex(c)}|{(done ? 1 : 0)}";
        var il = _shelfTree.ImageList;
        if (il != null && !il.Images.ContainsKey(key)) il.Images.Add(key, Theme.NodeIcon(il.ImageSize.Width, kind, g, c, done));
        return key;
    }

    private void UpdateShelfNode(TreeNode n, Shelf s)
    {
        var books = _library.AllBooksUnder(s).ToList();
        int done = books.Count(b => b.IsDone);
        bool all = books.Count > 0 && done == books.Count;
        n.Text = Modern.Bidi(s.Name);
        _shelfTree.SetRow(n, new LectureTreeView.RowInfo(s.SourceFolder != null ? "🔄" : "", books.Count > 0 ? $"{done}/{books.Count}" : null, all));
        n.ImageKey = n.SelectedImageKey = ShelfImageKey("shelf", "library", s.IconKey, s.ColorHex, Theme.PlaceColor, all);
        n.ForeColor = all ? Theme.Success : CustomNameColor(s.ColorHex) ?? Theme.Text;
        n.NodeFont = _topicNodeFont;
        n.ToolTipText = s.Name + $"\n{books.Count} كتاب · خلصت {done}" + (s.SourceFolder != null ? " · مربوط بفولدر" : "");
    }

    private void UpdateBookNode(TreeNode n, Book b)
    {
        bool current = b == _currentBook && ReaderShown;
        n.Text = Modern.Bidi(b.Title);
        string meta = b.IsBoard ? $"{Math.Max(1, b.PageCount)} ص" + InkMeta(b) : (b.IsDone && b.PageCount > 0 ? $"{b.PageCount} ص" : b.PageCount > 0 ? $"{Math.Max(1, b.LastPage)}/{b.PageCount}" : b.IsOnline ? "🌐" : "جديد") + InkMeta(b);
        if (b.IsBoard) { _shelfTree.SetRow(n, new LectureTreeView.RowInfo(meta, null, false, current, -1)); n.ImageKey = n.SelectedImageKey = ShelfImageKey("book", "whiteboard", b.IconKey, b.ColorHex, Theme.CourseColor, false); n.ForeColor = current ? Theme.Accent : CustomNameColor(b.ColorHex) ?? Theme.Text; n.NodeFont = null; n.Text = Modern.Bidi(b.Title); n.ToolTipText = $"{b.Title}\nسبورة · {Math.Max(1, b.PageCount)} صفحة"; return; }
        _shelfTree.SetRow(n, new LectureTreeView.RowInfo(meta, null, b.IsDone, current, b.IsStarted && !b.IsDone ? b.Fraction : -1));
        bool missing = !b.IsOnline && !FileCache.Exists(b.LocalPath);
        n.ImageKey = n.SelectedImageKey = missing ? "missing" : ShelfImageKey("book", b.IsPresentation ? "slide_text" : "book_open", b.IconKey, b.ColorHex, Theme.CourseColor, b.IsDone);
        n.ForeColor = current ? Theme.Accent : b.IsDone ? Theme.Success : missing ? Theme.Muted : CustomNameColor(b.ColorHex) ?? Theme.Text;
        n.NodeFont = null;
        n.ToolTipText = BookTooltip(b, missing);
    }

    /// <summary>تلميح قصير (سطرين): الاسم والتقدّم — التفاصيل (المعدّل، المسار) في كليك يمين ← 📂 الملف / بيانات الكتاب.</summary>
    private string BookTooltip(Book b, bool missing)
    {
        string unit = b.IsPresentation ? "شريحة" : "صفحة";
        var lines = new List<string> { b.Title };
        if (missing) lines.Add("⚠ الملف مش موجود — كليك يمين ← 📂 الملف");
        else if (b.PageCount > 0)
            lines.Add($"{(int)Math.Round(b.Fraction * 100)}% · {(b.IsPresentation ? "آخر شريحة" : "آخر صفحة")} {Math.Max(1, b.LastPage)} من {b.PageCount} {unit}" +
                      (b.LastOpenedUtc is { } lo ? " · " + RelativeDay(lo) : ""));
        if (b.InkCount > 0) lines[^1] += " · 🖊 عليه شرح";
        return string.Join("\n", lines);
    }

    private void UpdateSectionNode(TreeNode n, BookSection s)
    {
        double f = s.Book.FractionOf(s.From, s.To);
        bool done = f >= 0.95;
        bool here = s.Book == _currentBook && ReaderShown && s.Book.LastPage >= s.From && s.Book.LastPage <= s.To;
        n.Text = Modern.Bidi(s.Title);
        _shelfTree.SetRow(n, new LectureTreeView.RowInfo($"ص {s.From}", null, done, here, f > 0 && !done ? f : -1));
        n.ImageKey = n.SelectedImageKey = ShelfImageKey("section", "document_text", null, null, done ? Theme.Success : Theme.Muted, false);
        n.ForeColor = here ? Theme.Accent : done ? Theme.Success : Theme.Text;
        n.NodeFont = _regularNodeFont;
        n.ToolTipText = $"{s.Title}\nصفحات {s.From}–{s.To} · قريت {(int)Math.Round(f * 100)}%";
    }

    private void UpdateMarkNode(TreeNode n, Book b, PageMark m)
    {
        n.Text = string.IsNullOrWhiteSpace(m.Note) ? $"علامة — صفحة {m.Page}" : Modern.Bidi(m.Note.Split('\n')[0]);
        _shelfTree.SetRow(n, new LectureTreeView.RowInfo($"ص {m.Page}"));
        n.ImageKey = n.SelectedImageKey = ShelfImageKey("mark", "bookmark", null, null, Theme.Accent, false);
        n.ForeColor = Theme.Text;
        n.NodeFont = _regularNodeFont;
        n.ToolTipText = $"صفحة {m.Page}" + (string.IsNullOrWhiteSpace(m.Note) ? "" : "\n" + m.Note);
    }

    /// <summary>تحديث صفوف كتاب (وفصوله) من غير ما الشجرة تتبني من الأول.</summary>
    private void RefreshBookRows(Book b)
    {
        if (!_shelfNodes.TryGetValue(b, out var n)) return;
        UpdateBookNode(n, b);
        foreach (TreeNode sn in n.Nodes)
        {
            if (sn.Tag is BookSection s) UpdateSectionNode(sn, s);
            foreach (TreeNode pn in sn.Nodes) if (pn.Tag is BookSection ps) UpdateSectionNode(pn, ps);
        }
        for (var p = n.Parent; p != null; p = p.Parent) if (p.Tag is Shelf sh) UpdateShelfNode(p, sh);
    }

    private void RememberShelfExpanded(TreeNode? n, bool expanded)
    {
        if (_shelfTree.Nodes.Count == 0 || _shelfSearch.Text.Length > 0) return;
        switch (n?.Tag)
        {
            case Shelf s when s.Expanded != expanded: s.Expanded = expanded; MarkPositionDirty(); break;
            case Book b when b.Expanded != expanded: b.Expanded = expanded; MarkPositionDirty(); break;
        }
    }

    private Shelf? SelectedShelfContext() => _shelfTree.SelectedNode?.Tag switch
    {
        Shelf s => s,
        Book b => _library.ShelfOf(b),
        BookSection s => _library.ShelfOf(s.Book),
        BookMarkRef m => _library.ShelfOf(m.Book),
        BookHighlightRef h => _library.ShelfOf(h.Book),
        _ => null
    };

    private void SelectShelfItem(object item)
    {
        if (ShelfNodeOf(item) is not { } n) return;
        _shelfTree.SelectedNode = n;
        n.EnsureVisible();
    }

    private void OpenShelfNode(TreeNode? n)
    {
        switch (n?.Tag)
        {
            case Book b: OpenBook(b); break;
            case BookSection s: OpenBook(s.Book, s.From); break;
            case BookMarkRef m: OpenBook(m.Book, m.Mark.Page); break;
            case BookHighlightRef h: OpenBook(h.Book, h.Highlight.Page); break;
            case Shelf s when n.Nodes.Count > 0: if (n.IsExpanded) n.Collapse(); else n.Expand(); break;
        }
    }

    // =====================================================================
    //  القوايم
    // =====================================================================

    private readonly ContextMenuStrip _shelfMenu = new() { RightToLeft = RightToLeft.Yes, ShowImageMargin = false };

    private void ShowShelfMenu(TreeNode? node, Point location)
    {
        _shelfMenu.Renderer ??= new DarkRenderer();
        if (_shelfMenu.Renderer is not DarkRenderer) _shelfMenu.Renderer = new DarkRenderer();
        foreach (var old in _shelfMenu.Items.Cast<ToolStripItem>().ToList()) old.Dispose();
        _shelfMenu.Items.Clear();
        BuildShelfNodeMenu(node?.Tag, _shelfMenu.Items);
        _shelfMenu.Show(_shelfTree, location);
    }

    internal void BuildShelfNodeMenu(object? tag, ToolStripItemCollection items)
    {
        switch (tag)
        {
            case Shelf s: BuildShelfMenu(s, items); break;
            case Book b: BuildBookMenu(b, items); break;
            case BookSection s: BuildSectionMenu(s, items); break;
            case BookMarkRef m: BuildMarkMenu(m, items); break;
            case BookHighlightRef h: BuildHighlightMenu(h, items); break;
            default: BuildShelfAddMenu(null, items); break;
        }
    }

    /// <summary>«＋ إضافة»: حسب اللي واقف عليه — في الرف ده (أو الرف بتاع الكتاب).</summary>
    internal void BuildShelfAddMenu(object? tag, ToolStripItemCollection items)
    {
        var shelf = tag switch
        {
            Shelf s => s,
            Book b => _library.ShelfOf(b),
            BookSection s => _library.ShelfOf(s.Book),
            BookMarkRef m => _library.ShelfOf(m.Book),
            BookHighlightRef h => _library.ShelfOf(h.Book),
            _ => null
        };
        string where = shelf != null ? $" في «{shelf.Name}»" : "";
        items.Add(MenuItem("➕  كتاب (PDF / EPUB) — ملف واحد أو أكتر…" + where, () => AddBooksDialog(shelf)));
        items.Add(MenuItem("➕  فولدر كتب كامل (يبقى رف)…" + where, () => AddBooksFromFolderDialog(shelf)));
        items.Add(MenuItem("➕  كتاب من رابط (Google Drive / Dropbox / OneDrive)…" + where, () => AddBookLinksDialog(shelf)));
        items.Add(MenuItem("➕  عرض PowerPoint من ملف…" + where, () => AddPresentationsDialog(shelf)));
        items.Add(Sep());
        items.Add(MenuItem(shelf != null ? $"➕  رف جوه «{shelf.Name}»…" : "➕  رف جديد…", () => AddShelfDialog(shelf)));
        if (shelf != null) items.Add(MenuItem("➕  رف جديد (أساسي)…", () => AddShelfDialog(null)));
        items.Add(Sep());
        items.Add(MenuItem("➕  سبورة بيضاء جديدة (صفحات ترسم وتشرح عليها)…", () => NewBoardDialog(null, shelf)));
        items.Add(MenuItem("➕  رف متشارك من حد (ملف ZIP من «مسار»)…", () => ReceiveSharedShelfDialog(shelf)));
    }

    private void BuildShelfMenu(Shelf s, ToolStripItemCollection items)
    {
        var last = _library.AllBooksUnder(s).Where(b => b.LastOpenedUtc != null).OrderByDescending(b => b.LastOpenedUtc).FirstOrDefault()
                   ?? _library.AllBooksUnder(s).FirstOrDefault(b => !b.IsDone);
        if (last != null) items.Add(MenuItem($"▶  كمّل القراية: {last.Title}", () => OpenBook(last)));
        items.Add(Sep());
        items.Add(MenuItem("➕  كتاب (PDF / EPUB) — ملف واحد أو أكتر…", () => AddBooksDialog(s)));
        items.Add(MenuItem("➕  فولدر كتب كامل (يبقى رف)…", () => AddBooksFromFolderDialog(s)));
        items.Add(MenuItem("➕  كتاب من رابط (Google Drive / Dropbox / OneDrive)…", () => AddBookLinksDialog(s)));
        items.Add(MenuItem("➕  رف جوه الرف ده…", () => AddShelfDialog(s)));
        items.Add(MenuItem("➕  رف متشارك من حد (ZIP) جوه الرف ده…", () => ReceiveSharedShelfDialog(s)));
        items.Add(MenuItem("➕  سبورة جديدة في الرف ده…", () => NewBoardDialog(null, s)));
        items.Add(MenuItem("➕  عرض PowerPoint في الرف ده…", () => AddPresentationsDialog(s)));
        if (s.SourceFolder != null)
        {
            items.Add(Sep());
            items.Add(MenuItem("🔄  هات الكتب الجديدة من الفولدر", () => RefreshShelfFolder(s, announce: true)));
        }
        items.Add(Sep());
        items.Add(MenuItem("✏  إعادة تسمية…", () => RenameShelfItem(s), "F2"));
        items.Add(AppearanceMenuItem(s));
        items.Add(Sep());
        items.Add(ShelfMoveMenu(s));
        items.Add(SubMenuOf("↕  ترتيب",
            MenuItem("تحريك لفوق", () => MoveShelfItem(s, -1), "Alt+↑"),
            MenuItem("تحريك لتحت", () => MoveShelfItem(s, +1), "Alt+↓"),
            Sep(),
            MenuItem("ترتيب الكتب بالاسم", () => SortShelfBooks(s))));
        var books = _library.AllBooksUnder(s).ToList();
        if (books.Count > 0)
            items.Add(SubMenuOf("✔  التقدّم",
                MenuItem("تعليم كل الكتب «اتقرت»", () => SetBooksDone(books, true)),
                MenuItem("↺  تصفير التقدّم في الرف…", () => { if (Confirm("تصفّر الصفحات المقروءة في كل كتب الرف؟")) SetBooksDone(books, false); })));
        if (books.Count > 0) items.Add(MenuItem("📤  شارك الرف (ملف ZIP تبعته لحد)…", () => ShareShelfDialog(s)));
        if (s.SourceFolder != null)
            items.Add(SubMenuOf("📂  المصدر",
                MenuItem("📂  فتح الفولدر", () => OpenFolder(s.SourceFolder)),
                MenuItem("بطّل متابعة الفولدر (الكتب بتفضل)", () => { Checkpoint("بطّل متابعة الفولدر"); s.SourceFolder = null; s.TouchInfo(); MarkDirty(); RebuildShelf(); })));
        items.Add(Sep());
        items.Add(MenuItem("🗑  حذف الرف…", () => DeleteShelfItem(s), "Delete"));
    }

    private void BuildBookMenu(Book b, ToolStripItemCollection items)
    {
        if (b.IsBoard) { BuildBoardMenu(b, items); return; }
        items.Add(MenuItem(b.IsPresentation ? (b.LastPage > 1 ? $"▶  اعرض من شريحة {b.LastPage}" : "▶  اعرض (بالأنيميشن)") : b.IsStarted && b.LastPage > 1 && !b.IsDone ? $"▶  كمّل من صفحة {b.LastPage}" : "▶  افتح الكتاب", () => OpenBook(b), "Enter"));
        if (b.LastPage > 1) items.Add(MenuItem("⏮  افتحه من الأول", () => OpenBook(b, 1)));
        if (BookFiles.FormatOf(BookFile(b)) == "pdf")
            items.Add(MenuItem("🖊  اشرح عليه (أقلام وألوان وأشكال وملصقات)" + (b.InkCount > 0 ? $" — عليه {b.InkCount}" : ""), () => OpenBookForInk(b), "Ctrl+Shift+D"));
        AddAnnotatedMenu(b, items, 0);
        if (b.IsPresentation && AnnotatedCopyOf(b) is { } pcopy && File.Exists(pcopy.LocalPath))
            items.Add(MenuItem("🖊  افتح النسخة المشروحة (بالكتابة)", () => OpenPresentation(pcopy)));
        if (b.IsOnline)
        {
            items.Add(Sep());
            items.Add(MenuItem(File.Exists(BookFile(b)) ? "🔄  هات أحدث نسخة من المصدر" : "🔄  نزّل الكتاب على الجهاز", () => _ = RefreshOnlineBookAsync(b)));
        }
        items.Add(Sep());
        items.Add(MenuItem("✏  إعادة تسمية…", () => RenameShelfItem(b), "F2"));
        items.Add(AppearanceMenuItem(b));
        items.Add(MenuItem("📋  بيانات الكتاب (المؤلف)…", () => EditBookInfo(b)));
        if (BookFiles.FormatOf(BookFile(b)) == "pdf")
            items.Add(MenuItem(b.WorkCopyOf != null ? "📑  محرر الصفحات…" : "📑  محرر الصفحات (على نسخة عمل)…" + (WorkCopyOf(b) != null ? " — موجودة" : ""), () => OpenPageEditor(b)));
        items.Add(Sep());
        items.Add(ShelfMoveMenu(b));
        items.Add(SubMenuOf("↕  ترتيب",
            MenuItem("تحريك لفوق", () => MoveShelfItem(b, -1), "Alt+↑"),
            MenuItem("تحريك لتحت", () => MoveShelfItem(b, +1), "Alt+↓")));
        items.Add(SubMenuOf("✔  التقدّم",
            MenuItem(b.IsDone ? "رجّعه «مش مقروء»" : "تعليمه «اتقرا»", () => SetBooksDone(new[] { b }, !b.IsDone), "W"),
            MenuItem("↺  تصفير التقدّم…", () => { if (Confirm($"تصفّر الصفحات المقروءة في «{b.Title}»؟")) ResetBook(b); })));
        if (b.PageCount > 0)
        {
            int pg = Math.Max(1, b.LastPage);
            items.Add(SubMenuOf("✨  الذكاء الاصطناعي", BookAiItems(b, pg, PartOf(b, pg)).ToArray()));
        }
        AddAnnotatedMenu(b, items, 5);
        AddAnnotatedMenu(b, items, 6);
        if (b.IsPresentation) items.Add(MenuItem("📤  حوّل العرض لفيديو MP4 (بالأنيميشن وعلامة لكل شريحة)…", () => ConvertPresentationToVideo(b)));
        items.Add(MenuItem("📝  حفظ التظليلات والملاحظات (Markdown)…", () => ExportBookNotes(b)));
        if (BookFiles.FormatOf(BookFile(b)) == "pdf")
            items.Add(MenuItem("📝  حوّل الصفحات المتصوّرة لنص (OCR) — للبحث والذكاء", () => _ = OcrBookAsync(b)));
        var src = new List<ToolStripItem>();
        if (b.IsOnline) src.Add(MenuItem("🌐  افتح الرابط في المتصفح", () => OpenInBrowser(b.Path)));
        if (File.Exists(BookFile(b)))
        {
            src.Add(MenuItem("📂  افتح مكان الملف" + (b.IsOnline ? " (النسخة المتنزلة)" : ""), () => OpenInExplorer(BookFile(b))));
            src.Add(MenuItem("📂  افتحه في البرنامج العادي للـ PDF", () => OpenExternally(b)));
        }
        if (!b.IsOnline && !FileCache.Exists(b.LocalPath)) src.Add(MenuItem("📂  حدّد مكان الملف…", () => RelocateBook(b)));
        items.Add(SubMenuOf("📂  الملف", src.ToArray()));
        items.Add(Sep());
        items.Add(MenuItem("🗑  شيل الكتاب من الرف (الملف مش بيتمسح)", () => DeleteShelfItem(b), "Delete"));
    }

    private void BuildSectionMenu(BookSection s, ToolStripItemCollection items)
    {
        items.Add(MenuItem($"▶  افتح عند «{s.Title}» (صفحة {s.From})", () => OpenBook(s.Book, s.From), "Enter"));
        items.Add(Sep());
        bool done = s.Book.FractionOf(s.From, s.To) >= 0.95;
        items.Add(MenuItem(done ? "✔  رجّع الفصل «مش مقروء»" : "✔  علّم الفصل «اتقرا»", () => SetSectionRead(s, !done), "W"));
        items.Add(MenuItem($"✨  لخّص «{s.Title}»", () => _ = SummarizeBookPartAsync(s.Book, s.Title, s.From, s.To)));
        items.Add(MenuItem($"✨  اختبرني في «{s.Title}»", () => _ = QuizBookPartAsync(s.Book, s.Title, s.From, s.To)));
    }

    private void BuildMarkMenu(BookMarkRef m, ToolStripItemCollection items)
    {
        items.Add(MenuItem($"▶  روح لصفحة {m.Mark.Page}", () => OpenBook(m.Book, m.Mark.Page), "Enter"));
        items.Add(Sep());
        items.Add(MenuItem("✏  تعديل الملاحظة…", () => EditPageMark(m), "F2"));
        items.Add(Sep());
        items.Add(MenuItem("🗑  حذف العلامة", () => DeleteShelfItem(m), "Delete"));
    }

    private ToolStripMenuItem ShelfMoveMenu(object item)
    {
        var self = item as Shelf;
        var current = item switch { Book b => _library.ShelfOf(b), Shelf s => _library.ParentOf(s), _ => null };
        var list = new List<ToolStripItem>();
        if (current != null) list.Add(MenuItem(self != null ? "برا (رف أساسي)" : "برا الرفوف", () => MoveToShelf(item, null)));
        foreach (var s in _library.Shelves)
        {
            if (s == current || self != null && !_library.CanNest(self, s)) continue;
            var path = new List<string> { s.Name };
            for (var p = _library.ParentOf(s); p != null && path.Count < 6; p = _library.ParentOf(p)) path.Insert(0, p.Name);
            var target = s;
            list.Add(MenuItem(string.Join(" › ", path), () => MoveToShelf(item, target)));
        }
        if (list.Count == 0) list.Add(new ToolStripMenuItem("مفيش رفوف تانية") { Enabled = false, ForeColor = Theme.Muted });
        return SubMenuOf("↔  انقل لرف…", list.ToArray());
    }

    private bool Confirm(string text) => SmokeAskText != null || Dialogs.Confirm(this, text);

    private string? AskText(string title, string prompt, string initial)
    {
        if (SmokeAskText != null) return SmokeAskText(initial);
        string? s = Dialogs.Input(this, title, prompt, initial);
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    // =====================================================================
    //  إضافة
    // =====================================================================

    private void AddShelfDialog(Shelf? parent)
    {
        if (!EnsureLibrary()) return;
        string? name = AskText("رف جديد", parent != null ? $"اسم الرف (جوه «{parent.Name}»):" : "اسم الرف:", "");
        if (name == null) return;
        var s = AddShelf(name, parent);
        SelectShelfItem(s);
    }

    internal Shelf AddShelf(string name, Shelf? parent, string? folder = null, bool checkpoint = true)
    {
        if (checkpoint) Checkpoint("رف جديد");
        var s = new Shelf { Name = name, ParentId = parent?.Id, SourceFolder = folder };
        s.TouchInfo();
        _library.Shelves.Add(s);
        if (parent != null && !parent.Expanded) parent.Expanded = true;
        MarkDirty();
        if (!_shelfMode) ShowShelf(true);
        RebuildShelf();
        return s;
    }

    private void AddBooksDialog(Shelf? shelf)
    {
        if (!EnsureLibrary()) return;
        using var dlg = new OpenFileDialog
        {
            Title = "اختار كتاب أو أكتر (PDF / EPUB / PowerPoint)" + (shelf != null ? $" — هيتحط في «{shelf.Name}»" : ""),
            Filter = "كتب وعروض (PDF و EPUB و PowerPoint)|*.pdf;*.epub;*.pptx;*.ppsx;*.pptm;*.ppt;*.pps|كل الملفات|*.*", Multiselect = true, RestoreDirectory = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var added = AddBooks(dlg.FileNames.Where(BookFiles.IsBookFile), shelf);
        if (added.Count > 0)
        {
            // يظهر في الرف على طول (حتى لو كنت في «مكتبتي»)، وكتاب واحد بيتفتح
            if (!_shelfMode || _split.Panel2Collapsed) ToggleShelf();
            RebuildShelf();
            SelectShelfItem(added[0]);
            if (added.Count == 1) { OpenBook(added[0]); return; }
        }
        AnnounceAdded(added.Count, dlg.FileNames.Length);
    }

    private void AddBooksFromFolderDialog(Shelf? parent)
    {
        if (!EnsureLibrary()) return;
        string? dir = PickFolder(this, "اختار فولدر الكتب (كل فولدر جواه بيبقى رف)" + (parent != null ? $" — جوه «{parent.Name}»" : ""));
        if (dir == null) return;
        if (!BookFiles.HasBooks(dir)) { Dialogs.Info(this, "مفيش كتب (PDF أو EPUB) في الفولدر ده ولا في الفولدرات اللي جواه."); return; }
        var s = AddBooksFromFolder(dir, parent);
        SelectShelfItem(s);
        ShowStatus($"اتضاف رف «{s.Name}» فيه {_library.AllBooksUnder(s).Count()} كتاب — وأي كتاب جديد في الفولدر هيتضاف لما تحدّث الرف", 6);
    }

    private void AnnounceAdded(int added, int picked)
    {
        if (added == 0) ShowStatus(picked > 0 ? "الكتب دي موجودة على الرف أصلًا" : "مفيش كتب اتضافت", 4);
        else ShowStatus(added == 1 ? "اتضاف كتاب ✓ — دبل كليك عشان تقراه" : $"اتضاف {added} كتاب ✓", 4);
    }

    /// <summary>بيضيف ملفات كتب للرف (الموجود أصلًا بيتساب). بيرجّع اللي اتضاف.</summary>
    internal List<Book> AddBooks(IEnumerable<string> files, Shelf? shelf, bool checkpoint = true)
    {
        var list = files.Select(f => Path.GetFullPath(f)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => !_library.Books.Any(b => string.Equals(b.LocalPath, f, StringComparison.OrdinalIgnoreCase) || string.Equals(b.Path, f, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var added = new List<Book>();
        if (list.Count == 0) return added;
        if (checkpoint) Checkpoint(list.Count == 1 ? "إضافة كتاب" : "إضافة كتب");
        foreach (string f in list)
        {
            var b = new Book { Title = BookFiles.TitleFromFile(f), Path = f, LocalPath = f, ShelfId = shelf?.Id };
            b.TouchInfo();
            _library.Books.Add(b);
            added.Add(b);
        }
        if (shelf != null) shelf.Expanded = true;
        MarkDirty();
        if (!_shelfMode) ShowShelf(true);
        RebuildShelf();
        SelectShelfItem(added[0]);
        QueueProbe(added);
        return added;
    }

    /// <summary>فولدر = رف (والفولدرات اللي جواه رفوف فرعية)، ومربوط عشان الجديد يتضاف.</summary>
    internal Shelf AddBooksFromFolder(string dir, Shelf? parent)
    {
        Checkpoint("كتب من فولدر");
        dir = Path.GetFullPath(dir);
        var shelf = _library.Shelves.FirstOrDefault(s => s.SourceFolder != null && FolderLink.Norm(s.SourceFolder) == FolderLink.Norm(dir))
                    ?? AddShelf(Path.GetFileName(dir.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : dir, parent, dir, checkpoint: false);
        FillShelfFromFolder(shelf, dir, 0);
        MarkDirty();
        RebuildShelf();
        return shelf;
    }

    /// <summary>بيضيف الكتب الجديدة والفولدرات الجديدة (رفوف فرعية). بيرجّع عدد الكتب الجديدة.</summary>
    private int FillShelfFromFolder(Shelf shelf, string dir, int depth)
    {
        int n = 0;
        var known = _library.Books.Select(b => FolderLink.Norm(string.IsNullOrEmpty(b.LocalPath) ? b.Path : b.LocalPath)).ToHashSet();
        var fresh = BookFiles.FilesIn(dir).Where(f => !known.Contains(FolderLink.Norm(f))).ToList();
        foreach (string f in fresh)
        {
            var b = new Book { Title = BookFiles.TitleFromFile(f), Path = f, LocalPath = f, ShelfId = shelf.Id };
            b.TouchInfo();
            _library.Books.Add(b);
            QueueProbe(new[] { b });
            n++;
        }
        if (depth >= 8) return n;
        foreach (string sub in BookFiles.SubfoldersWithBooks(dir))
        {
            var child = _library.ShelvesIn(shelf).FirstOrDefault(s => s.SourceFolder != null && FolderLink.Norm(s.SourceFolder) == FolderLink.Norm(sub));
            if (child == null)
            {
                child = new Shelf { Name = Path.GetFileName(sub), ParentId = shelf.Id, SourceFolder = sub, Expanded = false };
                child.TouchInfo();
                _library.Shelves.Add(child);
            }
            n += FillShelfFromFolder(child, sub, depth + 1);
        }
        return n;
    }

    internal int RefreshShelfFolder(Shelf s, bool announce)
    {
        if (s.SourceFolder == null) return 0;
        if (!Directory.Exists(s.SourceFolder))
        {
            if (announce) ShowBanner("فولدر الرف مش موجود دلوقتي: " + s.SourceFolder + " — لو على الدرايف اتأكد إن برنامجه شغال", BannerKind.Warning, seconds: 8);
            return 0;
        }
        Checkpoint("الكتب الجديدة من الفولدر");
        int n = FillShelfFromFolder(s, s.SourceFolder, 0);
        if (n > 0) { MarkDirty(); RebuildShelf(); }
        if (announce) ShowStatus(n == 0 ? "مفيش كتب جديدة في الفولدر" : $"اتضاف {n} كتاب جديد ✓", 4);
        return n;
    }

    private static List<string> BookFilesIn(IDataObject? data)
    {
        if (data?.GetData(DataFormats.FileDrop) is not string[] paths) return new();
        var list = new List<string>();
        foreach (string p in paths)
        {
            if (File.Exists(p) && BookFiles.IsBookFile(p)) list.Add(p);
            else if (Directory.Exists(p)) list.Add(p);
        }
        return list;
    }

    private void DropBookFiles(List<string> paths, Shelf? shelf)
    {
        if (paths.Count == 0 || !EnsureLibrary()) return;
        var dirs = paths.Where(Directory.Exists).ToList();
        var files = paths.Where(File.Exists).ToList();
        int before = _library.Books.Count;
        if (files.Count > 0) AddBooks(files, shelf);
        foreach (string d in dirs) if (BookFiles.HasBooks(d)) AddBooksFromFolder(d, shelf);
        AnnounceAdded(_library.Books.Count - before, paths.Count);
    }

    private void OnShelfDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data == null) { e.Effect = DragDropEffects.None; return; }
        if (BookFilesIn(e.Data).Count > 0) { e.Effect = DragDropEffects.Copy; return; }
        if (e.Data.GetData(typeof(TreeNode)) is TreeNode src && src.TreeView == _shelfTree)
        {
            var target = _shelfTree.GetNodeAt(_shelfTree.PointToClient(new Point(e.X, e.Y)));
            var dest = target?.Tag switch { Shelf s => s, Book b => _library.ShelfOf(b), _ => null };
            bool ok = src.Tag switch
            {
                Book => true,
                Shelf s => _library.CanNest(s, dest) && dest != s,
                _ => false
            };
            e.Effect = ok ? DragDropEffects.Move : DragDropEffects.None;
            return;
        }
        e.Effect = DragDropEffects.None;
    }

    private void OnShelfDragDrop(object? sender, DragEventArgs e)
    {
        var target = _shelfTree.GetNodeAt(_shelfTree.PointToClient(new Point(e.X, e.Y)));
        var dest = target?.Tag switch { Shelf s => s, Book b => _library.ShelfOf(b), BookSection s => _library.ShelfOf(s.Book), BookMarkRef m => _library.ShelfOf(m.Book), _ => null };
        var files = BookFilesIn(e.Data);
        if (files.Count > 0) { DropBookFiles(files, dest); return; }
        if (e.Data?.GetData(typeof(TreeNode)) is TreeNode src && src.Tag is Shelf or Book)
        {
            // كتاب على كتاب: يتحط قبله في نفس الرف
            if (src.Tag is Book moving && target?.Tag is Book before && before != moving)
            {
                Checkpoint("نقل كتاب");
                _library.Books.Remove(moving);
                _library.Books.Insert(_library.Books.IndexOf(before), moving);
                if (moving.ShelfId != before.ShelfId) { moving.ShelfId = before.ShelfId; moving.TouchInfo(); }
                MarkDirty();
                RebuildShelf();
                SelectShelfItem(moving);
                return;
            }
            MoveToShelf(src.Tag, dest);
        }
    }

    // =====================================================================
    //  تعديل ونقل وحذف
    // =====================================================================

    private void RenameShelfItem(object item)
    {
        switch (item)
        {
            case Shelf s:
            {
                string? name = AskText("إعادة تسمية", "اسم الرف:", s.Name);
                if (name == null || name == s.Name) return;
                Checkpoint("إعادة تسمية");
                s.Name = name; s.TouchInfo();
                break;
            }
            case Book b:
            {
                string? name = AskText("إعادة تسمية", "اسم الكتاب:", b.Title);
                if (name == null || name == b.Title) return;
                Checkpoint("إعادة تسمية");
                b.Title = name; b.TouchInfo();
                break;
            }
            case BookMarkRef m: EditPageMark(m); return;
            default: return;
        }
        MarkDirty();
        RebuildShelf();
    }

    private void EditBookInfo(Book b)
    {
        string? author = AskText("بيانات الكتاب", "المؤلف:", b.Author ?? "");
        if (author == null || author == b.Author) return;
        Checkpoint("بيانات الكتاب");
        b.Author = author; b.TouchInfo();
        MarkDirty();
        RebuildShelf();
    }

    internal void MoveToShelf(object item, Shelf? dest)
    {
        switch (item)
        {
            case Book b when b.ShelfId != dest?.Id:
                Checkpoint("نقل كتاب");
                b.ShelfId = dest?.Id; b.TouchInfo();
                break;
            case Shelf s when s.ParentId != dest?.Id && _library.CanNest(s, dest) && s != dest:
                Checkpoint("نقل رف");
                s.ParentId = dest?.Id; s.TouchInfo();
                break;
            default: return;
        }
        if (dest != null) dest.Expanded = true;
        MarkDirty();
        RebuildShelf();
        SelectShelfItem(item);
    }

    /// <summary>تحريك لفوق/لتحت جوه نفس الرف.</summary>
    internal void MoveShelfItem(object item, int delta)
    {
        if (item is Book b)
        {
            var siblings = _library.BooksOn(_library.ShelfOf(b)).ToList();
            int i = siblings.IndexOf(b), j = i + delta;
            if (i < 0 || j < 0 || j >= siblings.Count) return;
            Checkpoint("ترتيب");
            var other = siblings[j];
            int bi = _library.Books.IndexOf(b), oi = _library.Books.IndexOf(other);
            (_library.Books[bi], _library.Books[oi]) = (_library.Books[oi], _library.Books[bi]);
        }
        else if (item is Shelf s)
        {
            var siblings = _library.ShelvesIn(_library.ParentOf(s)).ToList();
            int i = siblings.IndexOf(s), j = i + delta;
            if (i < 0 || j < 0 || j >= siblings.Count) return;
            Checkpoint("ترتيب");
            var other = siblings[j];
            int si = _library.Shelves.IndexOf(s), oi = _library.Shelves.IndexOf(other);
            (_library.Shelves[si], _library.Shelves[oi]) = (_library.Shelves[oi], _library.Shelves[si]);
        }
        else return;
        MarkDirty();
        RebuildShelf();
        SelectShelfItem(item);
    }

    private void SortShelfBooks(Shelf s)
    {
        var mine = _library.BooksOn(s).ToList();
        if (mine.Count < 2) return;
        Checkpoint("ترتيب الكتب");
        var sorted = mine.OrderBy(b => b.Title, NaturalComparer.Instance).ToList();
        var slots = mine.Select(b => _library.Books.IndexOf(b)).OrderBy(i => i).ToList();
        for (int i = 0; i < slots.Count; i++) _library.Books[slots[i]] = sorted[i];
        MarkDirty();
        RebuildShelf();
    }

    internal void DeleteShelfItem(object item)
    {
        switch (item)
        {
            case Shelf s:
            {
                var shelves = _library.ShelfTree(s).ToList();
                var books = _library.AllBooksUnder(s).ToList();
                string text = books.Count == 0 && shelves.Count == 1
                    ? $"تحذف الرف «{s.Name}»؟"
                    : $"تحذف الرف «{s.Name}» وكل اللي جواه ({books.Count} كتاب{(shelves.Count > 1 ? $"، {shelves.Count - 1} رف" : "")})؟\nالملفات نفسها مش هتتمسح من الجهاز.";
                if (!Confirm(text)) return;
                Checkpoint("حذف رف");
                if (_currentBook != null && books.Contains(_currentBook)) CloseReader(showHome: true);
                foreach (var b in books) { _library.Books.Remove(b); _deletedKeys.Add(LibrarySync.BookKey(b)); }
                foreach (var x in shelves) { _library.Shelves.Remove(x); _deletedKeys.Add(LibrarySync.ShelfKey(x)); }
                break;
            }
            case Book b:
                if (!Confirm($"تشيل «{b.Title}» من الرف؟\n(الملف نفسه مش هيتمسح، بس التقدّم والعلامات بتوعه هيروحوا)")) return;
                Checkpoint("شيل كتاب");
                if (b == _currentBook) CloseReader(showHome: true);
                _library.Books.Remove(b);
                _deletedKeys.Add(LibrarySync.BookKey(b));
                break;
            case BookMarkRef m:
                Checkpoint("حذف علامة");
                m.Book.Marks.Remove(m.Mark);
                m.Book.TouchProgress();
                if (m.Book == _currentBook) _ = _reader?.SetMarksAsync(m.Book.Marks.Select(x => x.Page));
                break;
            default: return;
        }
        MarkDirty();
        RebuildShelf();
    }

    internal void SetBooksDone(IEnumerable<Book> books, bool done)
    {
        var list = books.ToList();
        if (list.Count == 0) return;
        Checkpoint(done ? "تعليم «اتقرا»" : "تصفير التقدّم");
        foreach (var b in list)
        {
            if (done) b.MarkedDone = true;
            else { b.MarkedDone = false; b.ReadPages = ""; }
            b.TouchProgress();
            if (b == _currentBook) _ = _reader?.SetReadAsync(b.Read.Pages);
        }
        MarkDirty();
        RebuildShelf();
    }

    private void ResetBook(Book b)
    {
        Checkpoint("تصفير التقدّم");
        b.MarkedDone = false; b.ReadPages = ""; b.LastPage = 1; b.LastTop = null; b.LastLeft = null;
        b.TouchProgress();
        if (b == _currentBook) _ = _reader?.SetReadAsync(Array.Empty<int>());
        MarkDirty();
        RebuildShelf();
    }

    internal void SetSectionRead(BookSection s, bool read)
    {
        Checkpoint(read ? "تعليم الفصل «اتقرا»" : "الفصل «مش مقروء»");
        var set = s.Book.Read;
        var pages = set.Pages.ToHashSet();
        for (int p = s.From; p <= s.To; p++) { if (read) pages.Add(p); else pages.Remove(p); }
        var fresh = new PageSet();
        foreach (int p in pages) fresh.Add(p);
        s.Book.ReadPages = fresh.ToString();
        if (!read) s.Book.MarkedDone = false;
        s.Book.TouchProgress();
        if (s.Book == _currentBook) _ = _reader?.SetReadAsync(fresh.Pages);
        MarkDirty();
        RebuildShelf();
    }

    internal PageMark AddPageMark(Book b, int page, string note = "")
    {
        var existing = b.Marks.FirstOrDefault(m => m.Page == page);
        if (existing != null) { ShowStatus($"فيه علامة على صفحة {page} أصلًا", 3); return existing; }
        var m = new PageMark { Page = page, Note = note };
        b.Marks.Add(m);
        b.TouchProgress();
        MarkDirty();
        if (b == _currentBook) _ = _reader?.SetMarksAsync(b.Marks.Select(x => x.Page));
        if (_shelfMode) RebuildShelf();
        ShowStatus($"🔖 علامة على صفحة {page} — بتلاقيها تحت الكتاب في «الكتب»", 4);
        return m;
    }

    private void EditPageMark(BookMarkRef m)
    {
        string? note = AskText("ملاحظة العلامة", $"ملاحظة على صفحة {m.Mark.Page}:", m.Mark.Note);
        if (note == null || note == m.Mark.Note) return;
        Checkpoint("تعديل علامة");
        m.Mark.Note = note;
        m.Book.TouchProgress();
        MarkDirty();
        RebuildShelf();
    }

    private void OpenExternally(Book b)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(BookFile(b)) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Dialogs.Error(this, "مقدرتش أفتحه: " + ex.Message); }
    }

    private bool RelocateBook(Book b)
    {
        using var dlg = new OpenFileDialog { Title = $"فين «{b.Title}»؟", Filter = "كتب (PDF و EPUB)|*.pdf;*.epub|كل الملفات|*.*", FileName = Path.GetFileName(b.Path) };
        try { if (Directory.Exists(Path.GetDirectoryName(b.Path))) dlg.InitialDirectory = Path.GetDirectoryName(b.Path); } catch { }
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        Checkpoint("مكان الكتاب");
        b.Path = b.LocalPath = dlg.FileName;
        b.TouchInfo();
        FileCache.Forget(dlg.FileName);
        MarkDirty();
        RebuildShelf();
        return true;
    }

    // =====================================================================
    //  بيانات الكتب الجديدة (عدد الصفحات والفهرس) في الخلفية
    // =====================================================================

    private void QueueProbe(IEnumerable<Book> books)
    {
        // العروض بتتقري على طول (من غير القارئ)
        foreach (var b in books.Where(x => x.IsPresentation && x.PageCount <= 0).ToList())
            if (ApplyPresentationInfo(b)) RefreshBookRowsOrTree(b);
        foreach (var b in books.Where(x => !x.IsPresentation))
            if (b.PageCount <= 0 && !b.IsBoard && (b.IsOnline ? File.Exists(BookFile(b)) : !LibraryFiles.IsCloudPath(b.LocalPath)) && !_bookProbeQueue.Contains(b))
                _bookProbeQueue.Enqueue(b);
        if (!_bookProbing && _bookProbeQueue.Count > 0 && ReaderPane.Available) _ = BookProbeLoopAsync();
    }

    private async Task BookProbeLoopAsync()
    {
        _bookProbing = true;
        var pane = EnsureReader();
        try
        {
            // WebView2 مش بيتجهّز وهو مستخبي: يبقى «ظاهر» ورا الشاشة الرئيسية لحد ما يخلص (المستخدم مش شايفه)
            if (!ReaderShown) { pane.Visible = true; pane.SendToBack(); }
            while (_bookProbeQueue.Count > 0 && !IsDisposed)
            {
                var b = _bookProbeQueue.Dequeue();
                if (!_library.Books.Contains(b) || b.PageCount > 0 || !File.Exists(BookFile(b))) continue;
                var info = await pane.ProbeAsync(BookFile(b));
                if (info == null || IsDisposed || !_library.Books.Contains(b)) continue;
                ApplyDocInfo(b, info);
                RefreshBookRowsOrTree(b);
            }
        }
        catch (Exception ex) { Log.Error("book probe", ex); }
        finally
        {
            _bookProbing = false;
            if (!ReaderShown) pane.Visible = false;
        }
    }

    private void RefreshBookRowsOrTree(Book b)
    {
        // الفصول بتغيّر عدد الصفوف: نبني الشجرة؛ غير كده نحدّث الصف بس
        if (_shelfNodes.TryGetValue(b, out var n) && n.Nodes.Count != b.Sections().Count + b.Marks.Count + b.Highlights.Count) RebuildShelf();
        else RefreshBookRows(b);
    }

    private void ApplyDocInfo(Book b, ReaderPane.DocInfo info)
    {
        bool changed = false;
        if (info.Pages > 0 && b.PageCount != info.Pages) { b.PageCount = info.Pages; changed = true; }
        if (info.Outline.Count > 0 && !SameOutline(b.Chapters, info.Outline)) { b.Chapters = info.Outline; changed = true; }
        if (!string.IsNullOrEmpty(info.Fingerprint) && b.Fingerprint != info.Fingerprint) { b.Fingerprint = info.Fingerprint; changed = true; }
        if (string.IsNullOrWhiteSpace(b.Author) && !string.IsNullOrWhiteSpace(info.Author)) { b.Author = info.Author; changed = true; }
        if (changed) { b.TouchInfo(); MarkDirty(); }
    }

    private static bool SameOutline(List<BookChapter> a, List<BookChapter> b) =>
        a.Count == b.Count && a.Zip(b).All(x => x.First.Title == x.Second.Title && x.First.Page == x.Second.Page && x.First.Level == x.Second.Level);

    // =====================================================================
    //  القارئ
    // =====================================================================

    private ReaderPane EnsureReader()
    {
        if (_reader != null) return _reader;
        var pane = new ReaderPane { Dock = DockStyle.Fill };
        _videoArea.Controls.Add(pane);
        pane.Loaded += OnReaderLoaded;
        pane.StateChanged += OnReaderState;
        pane.PagesRead += OnPagesRead;
        pane.Tick += (gen, active) => { if (gen != _readerGen) return; _readerActive = active; _readerActiveUtc = DateTime.UtcNow; };
        pane.Command += OnReaderCommand;
        pane.HighlightRequested += OnHighlightRequested;
        pane.HighlightEdited += OnHighlightEdited;
        pane.InkChanged += OnInkChanged;
        pane.InkPrefsChanged += OnInkPrefsChanged;
        pane.PickRequested += OnReaderPick;
        pane.PagesRemapped += OnPagesRemapped;
        pane.BoardChanged += OnBoardChanged;
        pane.Failed += (gen, code, message) =>
        {
            if (gen != _readerGen || IsDisposed) return;
            Log.Info($"reader failed: {code} {message}");
            ShowBanner("مقدرتش أفتح الكتاب" + (string.IsNullOrWhiteSpace(message) ? "" : ": " + message), BannerKind.Warning, seconds: 10, key: "reader");
        };
        pane.KeyForwarded += key =>
        {
            if (IsDisposed) return;
            var msg = new Message();
            ProcessCmdKey(ref msg, key);
        };
        _reader = pane;
        return pane;
    }

    /// <summary>بيفتح الكتاب في القارئ (page = صفحة معينة، وإلا من آخر مكان).</summary>
    internal async void OpenBook(Book b, int? page = null)
    {
        if (!EnsureLibrary()) return;
        if (b.IsPresentation) { OpenPresentation(b, page); return; }
        if (!ReaderPane.Available)
        {
            ShowBanner(UdemyWeb.RuntimeVersion == null
                    ? "قارئ الكتب محتاج «Microsoft Edge WebView2» (موجود في ويندوز 10/11 عادةً)"
                    : "ملفات قارئ الكتب مش موجودة جنب البرنامج — نزّل آخر إصدار تاني",
                BannerKind.Warning, UdemyWeb.RuntimeVersion == null ? "تنزيله" : null,
                UdemyWeb.RuntimeVersion == null ? () => OpenInBrowser(WebView2Download) : null, seconds: 0, key: "reader");
            return;
        }
        if (b.IsOnline)
        {
            // من رابط: من النسخة المتنزلة على الجهاز (ولو مش متنزلة: ننزّلها الأول)
            if (!File.Exists(BookFile(b))) { _ = FetchBookAsync(b, open: true); return; }
        }
        else if (!File.Exists(b.LocalPath))
        {
            FileCache.Forget(b.LocalPath);
            if (!File.Exists(b.LocalPath))
            {
                if (SmokeAskText == null && Dialogs.Confirm(this, $"ملف الكتاب مش موجود:\n{b.Path}\n\nلو على Google Drive أو Dropbox اتأكد إن برنامجهم شغال.\nتحب تحدد مكانه الجديد؟") && RelocateBook(b))
                    OpenBook(b, page);
                return;
            }
        }

        // نفس الكتاب مفتوح: نروح للصفحة بس
        if (ReaderShown && _currentBook == b)
        {
            if (page != null) await _reader!.GoToAsync(page.Value);
            else await _reader!.FocusAsync();
            return;
        }

        if (_currentVideo != null || _currentLive != null || UdemyActive) StopPlaybackCore(clearCurrent: true);
        if (ReaderShown) FlushReading();
        if (_fullscreen && _miniMode) ToggleMiniMode();

        var pane = EnsureReader();
        _currentBook = b;
        _readerShown = true;
        if (page != null) { b.LastPage = page.Value; b.LastTop = null; b.LastLeft = null; b.LastCfi = null; }
        b.LastOpenedUtc = DateTime.UtcNow;
        _library.LastBookId = b.Id;
        _readerActive = true;
        _readerActiveUtc = DateTime.UtcNow;
        _pagesSession = 0;
        _pagesSessionStartUtc = DateTime.UtcNow;
        MarkPositionDirty();

        _welcome.Visible = false;
        _videoView.Visible = false;
        pane.Visible = true;
        pane.BringToFront();
        ControlChromeVisible = false;
        HideBanner("reader");
        _nowPlayingLabel.Text = "";
        UpdateTitle();
        if (_shelfMode) RebuildShelf();
        try
        {
            var ink = BeginInkSession(b);
            _readerGen = await pane.OpenAsync(BookFile(b), b.Title, b, Math.Max(1, _settings.ReadSeconds), _settings.ReaderTheme, b.Read.Pages, ink, _settings.InkPrefs);
            BindInkSession(_readerGen);
            if (b.IsOnline) _ = CheckBookUpdateAsync(b);
            ShowStatus(b.LastPage > 1 && page == null ? $"«{b.Title}» — كمّل من صفحة {b.LastPage}" : $"«{b.Title}»", 4);
        }
        catch (Exception ex)
        {
            Log.Error("reader open", ex);
            if (_currentBook == b) ShowBanner("مقدرتش أفتح قارئ الكتب: " + ex.Message, BannerKind.Warning, seconds: 0, key: "reader");
        }
    }

    /// <summary>بيقفل القارئ (عشان فيديو اتشغل أو رجعت للرئيسية). مكانك متسجّل أول بأول.</summary>
    private void CloseReader(bool showHome)
    {
        if (!_readerShown) return;
        FlushReading();
        var b = _currentBook;
        _readerShown = false;
        _currentBook = null;
        _readerGen = -1;
        if (_reader != null)
        {
            _reader.Visible = false;
            _ = _reader.CloseBookAsync();
        }
        if (b != null) QueueAnnotated(b);
        _videoView.Visible = true;
        if (!_fullscreen) ControlChromeVisible = true;
        if (showHome) { _welcome.Visible = true; RefreshHome(); }
        if (b != null && _shelfMode) RefreshBookRows(b);
        UpdateTitle();
    }

    /// <summary>بيكتب الصفحات اللي اتقرت في الجلسة دي في سجل المذاكرة (صفحات في اليوم).</summary>
    private void FlushReading()
    {
        FlushSession();
        if (_pagesSession > 0 && _currentBook != null)
        {
            LogJournal(new JournalEntry
            {
                Kind = JournalEntry.Pages, StartUtc = _pagesSessionStartUtc, EndUtc = DateTime.UtcNow,
                CourseId = "book:" + _currentBook.Id, VideoPath = _currentBook.Path, Score = _pagesSession, Total = _currentBook.PageCount
            });
        }
        _pagesSession = 0;
        _pagesSessionStartUtc = DateTime.UtcNow;
    }

    private void OnReaderLoaded(int gen, ReaderPane.DocInfo info)
    {
        if (gen != _readerGen || _currentBook is not { } b || IsDisposed) return;
        if (_inkOnOpen == b) { _inkOnOpen = null; _ = _reader?.SetInkModeAsync(true); }
        if (_peOnOpen == b) { _peOnOpen = null; _ = _reader?.OpenPageEditorAsync(); }
        if (_boardExportOnOpen == b) { _boardExportOnOpen = null; _ = _reader?.BoardExportAsync(_boardExportArgs.Which, _boardExportArgs.Send); }
        int sections = b.Sections().Count;
        ApplyDocInfo(b, info);
        if (b.LastPage > b.PageCount && b.PageCount > 0) b.LastPage = b.PageCount;
        if (_shelfMode) { if (b.Sections().Count != sections) RebuildShelf(); else RefreshBookRows(b); }
    }

    private void OnReaderState(int gen, ReaderPane.State st)
    {
        if (gen != _readerGen || _currentBook is not { } b) return;
        int oldPage = b.LastPage;
        b.LastPage = st.Page;
        b.LastTop = st.Top is { } t && double.IsFinite(t) ? Math.Round(t, 1) : null;
        b.LastLeft = st.Left is { } l && double.IsFinite(l) ? Math.Round(l, 1) : null;
        b.Zoom = st.Scale == "page-width" ? null : st.Scale;
        b.Spread = st.Spread;
        b.RightToLeft = st.Rtl;
        if (st.Cfi != null) b.LastCfi = st.Cfi;
        if (st.Pages > 0 && b.PageCount != st.Pages) b.PageCount = st.Pages;
        _settings.ReaderTheme = st.Theme;
        b.TouchProgress();
        MarkPositionDirty();
        if (oldPage != st.Page && _shelfMode) RefreshBookRows(b);
        if (oldPage != st.Page && Recording) RecordingMark((b.IsBoard ? "🧑‍🏫 " : "📖 ") + b.Title + " — صفحة " + st.Page);
    }

    private void OnPagesRead(int gen, int[] pages)
    {
        if (gen != _readerGen || _currentBook is not { } b) return;
        bool wasDone = b.IsDone;
        var set = b.Read;
        int added = pages.Count(set.Add);
        if (added == 0) return;
        b.ReadPages = set.ToString();
        b.TouchProgress();
        _pagesSession += added;
        MarkPositionDirty();
        if (_shelfMode) RefreshBookRows(b);
        if (!wasDone && b.IsDone) ShowBanner($"خلّصت «{b.Title}» 🎉", BannerKind.Success, seconds: 8);
    }

    private void OnReaderCommand(int gen, string name, int page)
    {
        if (gen != _readerGen || _currentBook is not { } b) return;
        switch (name)
        {
            case "mark": AddPageMark(b, page > 0 ? page : Math.Max(1, b.LastPage)); break;
            case "fullscreen": ToggleFullscreen(); break;
            case "close": if (_fullscreen) ToggleFullscreen(); CloseReader(showHome: true); break;
            case "ai": ShowReaderAiMenu(); break;
            case "present-on": OnReaderPresent(true); break;
            case "present-off": OnReaderPresent(false); break;
            case "ink-save": SaveAllInk(); ShowStatus("الشرح اتحفظ", 3); break;
            case "ink-off": SaveAllInk(); break;
            case "pe-open": OpenPageEditor(b); break;
            case var bc when bc.StartsWith("board-", StringComparison.Ordinal): OnBoardCommand(bc); break;
            case "pe-saved": ReloadAfterPageEdit(b); break;
        }
    }

    /// <summary>مفاتيح «الكتب» (البحث والشجرة) والقارئ لما يكون مفتوح والفوكس برّه. true = اتنفذ.</summary>
    private bool HandleBookKeys(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Shift | Keys.B)) { ToggleShelf(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.P)) { AddPresentationsDialog(SelectedShelfContext()); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.D) && ReaderShown) { ToggleInkMode(); return true; }
        if (keyData == (Keys.Control | Keys.W) && ReaderShown) { CloseReader(showHome: true); return true; }
        if (_shelfTree.Focused)
        {
            var tag = _shelfTree.SelectedNode?.Tag;
            switch (keyData)
            {
                case Keys.Enter: OpenShelfNode(_shelfTree.SelectedNode); return true;
                case Keys.F2 when tag is BookHighlightRef hr: EditHighlightNote(hr); return true;
                case Keys.Delete when tag is BookHighlightRef hr: RemoveHighlight(hr.Book, hr.Highlight); return true;
                case Keys.F2 when tag is Shelf or Book or BookMarkRef: RenameShelfItem(tag is BookMarkRef m ? m : tag); return true;
                case Keys.Delete when tag is Shelf or Book or BookMarkRef: DeleteShelfItem(tag); return true;
                case Keys.Insert: ShowShelfMenu(null, new Point(4, (_shelfTree.SelectedNode?.Bounds.Bottom ?? 4))); return true;
                case Keys.W when tag is Book bk: SetBooksDone(new[] { bk }, !bk.IsDone); return true;
                case Keys.W when tag is BookSection s: SetSectionRead(s, s.Book.FractionOf(s.From, s.To) < 0.95); return true;
                case Keys.Alt | Keys.Up when tag is Shelf or Book: MoveShelfItem(tag, -1); return true;
                case Keys.Alt | Keys.Down when tag is Shelf or Book: MoveShelfItem(tag, +1); return true;
                case Keys.Control | Keys.F: _shelfSearch.Focus(); return true;
            }
        }
        if (ReaderShown)
        {
            switch (keyData)
            {
                case Keys.PageDown: _ = _reader!.StepAsync(1); return true;
                case Keys.PageUp: _ = _reader!.StepAsync(-1); return true;
                case Keys.B: AddPageMark(_currentBook!, Math.Max(1, _currentBook!.LastPage)); return true;
                case Keys.P: ShowStatus("المشغل الصغير للفيديو بس", 2); return true;
                case Keys.Space when !_shelfTree.Focused: _ = _reader!.FocusAsync(); return true;
            }
        }
        return false;
    }

    // =====================================================================
    //  الرئيسية: «كمّل القراية»
    // =====================================================================

    private IEnumerable<Book> HomeBooks() => _library.Books
        .Where(b => b.LastOpenedUtc != null && !b.IsDone)
        .OrderByDescending(b => b.LastOpenedUtc)
        .Take(3);

    private Control BookCard(Book b)
    {
        int percent = (int)Math.Round(b.Fraction * 100);
        var card = new Card
        {
            Fill = Theme.PanelLight, Size = new Size(LogicalToDeviceUnits(330), LogicalToDeviceUnits(124)),
            Margin = new Padding(LogicalToDeviceUnits(8)), Padding = new Padding(14, 10, 14, 8),
            BackColor = Theme.PanelLight, Cursor = Cursors.Hand, RightToLeft = RightToLeft.Yes
        };
        var name = new Label
        {
            Dock = DockStyle.Top, Height = 28, AutoEllipsis = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Theme.Text, Text = "📖  " + Modern.Bidi(b.Title), TextAlign = ContentAlignment.MiddleRight
        };
        string shelf = _library.ShelfOf(b) is { } s ? Modern.Bidi(s.Name) + "  ·  " : "";
        var info = new Label
        {
            Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight,
            Text = shelf + (b.PageCount > 0 ? $"صفحة {Math.Max(1, b.LastPage)} من {b.PageCount}  ·  {percent}%" : "لسه بادئ") +
                   (b.LastOpenedUtc is { } lo ? "  ·  " + RelativeDay(lo) : "")
        };
        var f = BookForecast(b);
        var next = new Label
        {
            Dock = DockStyle.Top, Height = 24, ForeColor = Theme.Text, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight,
            Text = CurrentSectionTitle(b) is { } sec ? "إنت في: " + sec : f.Finish is { } d ? $"هتخلّصه حوالي {d:d MMM}" : ""
        };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 6, BackColor = Theme.Back };
        bar.Paint += (_, e) =>
        {
            int w = (int)(bar.Width * percent / 100.0);
            using var br = new SolidBrush(Theme.Success);
            e.Graphics.FillRectangle(br, bar.Width - w, 0, w, bar.Height);
        };
        card.Controls.Add(bar);
        card.Controls.Add(next);
        card.Controls.Add(info);
        card.Controls.Add(name);
        foreach (Control ctl in new Control[] { card, name, info, next, bar })
        {
            ctl.Click += (_, _) => OpenBook(b);
            ctl.Cursor = Cursors.Hand;
        }
        card.MouseEnter += (_, _) => card.Fill = card.BackColor = Modern.Blend(Theme.PanelLight, Color.White, 0.06);
        card.MouseLeave += (_, _) => { if (!card.ClientRectangle.Contains(card.PointToClient(Cursor.Position))) card.Fill = card.BackColor = Theme.PanelLight; };
        _toolTip.SetToolTip(card, BookTooltip(b, false));
        return card;
    }

    private static string? CurrentSectionTitle(Book b)
    {
        int p = Math.Max(1, b.LastPage);
        var s = b.Sections().LastOrDefault(x => x.From <= p);
        if (s.Title == null) return null;
        var part = s.Parts.LastOrDefault(x => x.From <= p);
        return part.Title != null ? s.Title + " › " + part.Title : s.Title;
    }

    /// <summary>معدّل القراية (آخر أسبوعين) وميعاد الخلصان المتوقع.</summary>
    private (double PagesPerDay, DateOnly? Finish) BookForecast(Book b)
    {
        string id = "book:" + b.Id;
        var since = DateTime.UtcNow.AddDays(-14);
        var entries = JournalEntries.Where(e => e.Kind == JournalEntry.Pages && e.CourseId == id && e.StartUtc >= since).ToList();
        int pages = entries.Sum(e => e.Score) + (b == _currentBook ? _pagesSession : 0);
        if (pages <= 0) return (0, null);
        var first = entries.Select(e => e.StartUtc).DefaultIfEmpty(DateTime.UtcNow).Min();
        double days = Math.Max(1, Math.Ceiling((DateTime.UtcNow - first).TotalDays));
        double rate = pages / days;
        int left = Math.Max(0, b.PageCount - b.ReadCount);
        if (b.PageCount <= 0 || left == 0 || rate <= 0) return (rate, null);
        return (rate, DateOnly.FromDateTime(DateTime.Now.AddDays(Math.Ceiling(left / rate))));
    }
}
