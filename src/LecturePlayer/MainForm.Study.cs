using System.Diagnostics;
using LecturePlayer.Data;
using LecturePlayer.UI;
using LibVLCSharp.Shared;
using Timer = System.Windows.Forms.Timer;

namespace LecturePlayer;

/// <summary>
/// أدوات المذاكرة (الإصدار 1.4):
/// علامات مرجعية + ملاحظات لكل فيديو، تكرار A–B، لقطة شاشة، تصدير الملاحظات،
/// والشاشة الرئيسية «كمّل اللي بدأته».
/// </summary>
internal sealed partial class MainForm
{
    // ---------- لوحة الملاحظات ----------
    private readonly Card _notesPanel = new();
    private readonly Splitter _notesSplitter = new();
    private readonly Label _notesTitle = new();
    private readonly ListBox _bookmarkList = new();
    private readonly TextBox _notesBox = new();
    private readonly Timer _notesSaveTimer = new() { Interval = 800 };
    private VideoItem? _notesVideo;
    private bool _loadingNotes;

    // ---------- تكرار A–B ----------
    private long _loopA = -1, _loopB = -1;

    private string? _lastSnapshotDir;

    // ---------- الشاشة الرئيسية ----------
    private readonly Label _homeTitle = new();
    /// <summary>أزرار الإجراءات الكبيرة: «ضيف فولدر»، «رابط يوتيوب»، «بث مباشر»، «إزاي أستخدم البرنامج».</summary>
    private readonly FlowLayoutPanel _homeActions = new();
    private bool? _homeActionsBig;
    private readonly FlowLayoutPanel _homeCards = new();
    private readonly Label _homeHint = new();
    private readonly FlowLayoutPanel _homeTopics = new();

    /// <summary>الكورس داخل فلتر الموضوع بتاع الشاشة الرئيسية؟</summary>
    private bool InHomeScope(Course c) => _settings.HomeTopicFilter switch
    {
        "" => true,
        "-" => _library.TopicOf(c) == null,
        var key when key.StartsWith("p:") => _library.PlaceOf(c)?.Id == key[2..],
        var id => c.TopicId == id && _library.Topics.Any(t => t.Id == id)
    };

    /// <summary>أزرار المواضيع فوق الكروت: الكل / كل موضوع / من غير موضوع.</summary>
    private void RefreshHomeTopics()
    {
        if (_settings.HomeTopicFilter is { Length: > 0 } f && f != "-" &&
            (f.StartsWith("p:") ? _library.Places.All(p => p.Id != f[2..]) : _library.Topics.All(t => t.Id != f)))
            _settings.HomeTopicFilter = "";
        var old = _homeTopics.Controls.Cast<Control>().ToList();
        _homeTopics.Controls.Clear();
        foreach (var c in old) c.Dispose();
        _homeTopics.Visible = _library.Topics.Count > 0;
        if (!_homeTopics.Visible) return;

        void Chip(string key, string text)
        {
            var b = new PillButton(Modern.Bidi(text), _settings.HomeTopicFilter == key ? PillStyle.Primary : PillStyle.Ghost)
            {
                Height = LogicalToDeviceUnits(30)
            };
            b.Click += (_, _) => { _settings.HomeTopicFilter = key; RefreshHome(); };
            _homeTopics.Controls.Add(b);
        }
        Chip("", "كل المواضيع");
        foreach (var t in _library.Topics)
        {
            var cs = _library.CoursesOf(t).ToList();
            int v = cs.Sum(c => c.VideoCount), w = cs.Sum(c => c.WatchedCount);
            Chip(t.Id, v > 0 ? $"{t.Name}  {w * 100 / v}%" : t.Name);
        }
        if (_library.CoursesOf(null).Any()) Chip("-", TopicStats.NoTopicName);

        // الموضوع المختار (أو مكان جواه) فيه أماكن ← أزرار للأماكن (ROUTE، Udemy…)
        string sel = _settings.HomeTopicFilter;
        var selTopic = sel.StartsWith("p:")
            ? _library.Places.FirstOrDefault(p => p.Id == sel[2..]) is { } sp ? _library.TopicOf(sp) : null
            : _library.Topics.FirstOrDefault(t => t.Id == sel);
        if (selTopic == null) return;
        foreach (var p in _library.PlacesOf(selTopic))
        {
            var cs = _library.CoursesInPlace(p).ToList();
            int v = cs.Sum(c => c.VideoCount), w = cs.Sum(c => c.WatchedCount);
            Chip("p:" + p.Id, "🏫 " + (v > 0 ? $"{p.Name}  {w * 100 / v}%" : p.Name));
        }
    }

    // =====================================================================
    //  بناء لوحة الملاحظات (تحت الشجرة)
    // =====================================================================

    /// <summary>صف في قايمة العلامات (Bookmark = null ← سطر إرشادي).</summary>
    private sealed record BookmarkRow(Bookmark? Bookmark, string Time, string Note, string Tip)
    {
        public override string ToString() => Note;
    }

    private int _hotBookmarkIndex = -1;
    private bool _notesCollapsed;
    private int _notesExpandedHeight = 260;
    private readonly Label _notesChevron = new();

    private void BuildNotesPane()
    {
        _notesPanel.Dock = DockStyle.Bottom;
        _notesPanel.Height = _notesExpandedHeight;
        _notesPanel.BackColor = Theme.Window;
        _notesPanel.RightToLeft = RightToLeft.Yes;

        _notesSplitter.Dock = DockStyle.Bottom;
        _notesSplitter.Height = 6;
        _notesSplitter.BackColor = Theme.Window;
        _notesSplitter.MinSize = 120;
        _notesSplitter.MinExtra = 160;

        // عنوان القسم: سهم + اسم (بيقفل ويفتح القسم زي أقسام الشريط الجانبي في المحررات)
        var header = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Theme.Panel, Cursor = Cursors.Hand };
        _notesChevron.Dock = DockStyle.Right;
        _notesChevron.Width = 24;
        _notesChevron.Font = Glyphs.Font(8f);
        _notesChevron.Text = Glyphs.Expand;
        _notesChevron.ForeColor = Theme.Text;
        _notesChevron.TextAlign = ContentAlignment.MiddleCenter;
        _notesTitle.Dock = DockStyle.Fill;
        _notesTitle.ForeColor = Theme.Text;
        _notesTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _notesTitle.AutoEllipsis = true;
        _notesTitle.TextAlign = ContentAlignment.MiddleRight;
        _notesTitle.RightToLeft = RightToLeft.Yes;
        _notesTitle.Text = "العلامات والملاحظات";
        header.Controls.Add(_notesTitle);
        header.Controls.Add(_notesChevron);
        foreach (Control c in new Control[] { header, _notesTitle, _notesChevron })
        {
            c.Click += (_, _) => ToggleNotesSection();
            c.MouseEnter += (_, _) => header.BackColor = Theme.PanelLight;
            c.MouseLeave += (_, _) => header.BackColor = Theme.Panel;
        }

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(8, 2, 10, 6) };

        _bookmarkList.Dock = DockStyle.Top;
        _bookmarkList.Height = 112;
        _bookmarkList.DrawMode = DrawMode.OwnerDrawFixed;
        _bookmarkList.ItemHeight = LogicalToDeviceUnits(24);
        _bookmarkList.IntegralHeight = false;
        _bookmarkList.BorderStyle = BorderStyle.None;
        _bookmarkList.BackColor = Theme.Panel;
        _bookmarkList.ForeColor = Theme.Text;
        _bookmarkList.RightToLeft = RightToLeft.No; // الرسم يدوي من اليمين للشمال
        _bookmarkList.Font = new Font("Segoe UI", 9.5f);
        _bookmarkList.HandleCreated += (_, _) => NativeMethods.UseDarkExplorerTheme(_bookmarkList);
        _bookmarkList.DrawItem += DrawBookmarkItem;
        _bookmarkList.DoubleClick += (_, _) => JumpToSelectedBookmark();
        _bookmarkList.SelectedIndexChanged += (_, _) => _bookmarkList.Invalidate();
        _bookmarkList.MouseMove += (_, e) =>
        {
            int i = _bookmarkList.IndexFromPoint(e.Location);
            if (i == _hotBookmarkIndex) return;
            _hotBookmarkIndex = i;
            _bookmarkList.Invalidate();
            string tip = i >= 0 && i < _bookmarkList.Items.Count && _bookmarkList.Items[i] is BookmarkRow r ? r.Tip : "";
            _toolTip.SetToolTip(_bookmarkList, tip);
        };
        _bookmarkList.MouseLeave += (_, _) => { _hotBookmarkIndex = -1; _bookmarkList.Invalidate(); };
        _bookmarkList.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            int i = _bookmarkList.IndexFromPoint(e.Location);
            if (i < 0 || _bookmarkList.Items[i] is not BookmarkRow { Bookmark: not null }) return;
            _bookmarkList.SelectedIndex = i;
            ClearNodeMenu();
            _nodeMenu.Items.Add(MenuItem("روح للعلامة", JumpToSelectedBookmark, "Enter"));
            _nodeMenu.Items.Add(MenuItem("تعديل الملاحظة…", EditSelectedBookmark, "F2"));
            var sel = SelectedBookmark();
            _nodeMenu.Items.Add(MenuItem(sel?.ReviewDueUtc != null ? "شيلها من المراجعة المتباعدة" : "📌 ضيفها للمراجعة المتباعدة", ToggleReviewForSelectedBookmark, "R"));
            _nodeMenu.Items.Add(MenuItem("حذف العلامة", DeleteSelectedBookmark, "Delete"));
            _nodeMenu.Show(_bookmarkList, e.Location);
        };

        var spacer = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Panel };

        _notesBox.Dock = DockStyle.Fill;
        _notesBox.Multiline = true;
        _notesBox.ScrollBars = ScrollBars.Vertical;
        _notesBox.AcceptsReturn = true;
        _notesBox.BorderStyle = BorderStyle.None;
        _notesBox.Font = new Font("Segoe UI", 9.75f);
        _notesBox.HandleCreated += (_, _) => NativeMethods.UseDarkExplorerTheme(_notesBox);
        _notesBox.RightToLeft = RightToLeft.Yes;
        _notesBox.PlaceholderText = "ملاحظاتك على الفيديو ده… (بتتحفظ لوحدها)";
        _notesBox.TextChanged += (_, _) =>
        {
            if (_loadingNotes) return;
            _notesSaveTimer.Stop();
            _notesSaveTimer.Start();
        };
        _notesBox.Leave += (_, _) => CommitNotes();
        _notesSaveTimer.Tick += (_, _) => CommitNotes();

        var notesHost = new RoundedHost(_notesBox, padding: 7) { Dock = DockStyle.Fill };
        body.Controls.Add(notesHost);
        body.Controls.Add(spacer);
        body.Controls.Add(_bookmarkList);
        _notesPanel.Controls.Add(body);
        _notesPanel.Controls.Add(header);
    }

    /// <summary>قفل/فتح قسم العلامات والملاحظات (الشجرة بتاخد المساحة).</summary>
    private void ToggleNotesSection()
    {
        SetNotesCollapsed(!_notesCollapsed);
        _settings.NotesExpanded = !_notesCollapsed;
        SaveSettings();
    }

    /// <summary>بتفتح اللوحة لو كانت مقفولة (لما تحط علامة: عشان تشوفها وتكتب ملاحظة).</summary>
    private void ShowNotesSection()
    {
        if (_notesCollapsed) SetNotesCollapsed(false);
    }

    private void SetNotesCollapsed(bool collapsed)
    {
        if (_notesCollapsed == collapsed) { UpdateNotesTitle(); return; }
        _notesCollapsed = collapsed;
        if (_notesCollapsed)
        {
            _notesExpandedHeight = _notesPanel.Height;
            _notesPanel.Height = 28 + _notesPanel.Padding.Vertical;
        }
        else
        {
            _notesPanel.Height = Math.Max(160, _notesExpandedHeight);
        }
        _notesSplitter.Visible = !_notesCollapsed;
        _notesChevron.Text = _notesCollapsed ? Glyphs.Collapse : Glyphs.Expand;
        UpdateNotesTitle();
    }

    /// <summary>العنوان: عدد العلامات والملاحظات، واسم الفيديو لما اللوحة مفتوحة بس.</summary>
    private void UpdateNotesTitle()
    {
        var v = _notesVideo;
        int count = v == null ? 0 : v.Bookmarks.Count + (string.IsNullOrWhiteSpace(v.Notes) ? 0 : 1);
        string text = "📝  العلامات والملاحظات" + (count > 0 ? $"  ({count})" : "");
        if (!_notesCollapsed && v != null) text += $"  ·  {Modern.Bidi(v.Title)}";
        _notesTitle.Text = text;
    }

    /// <summary>علامة: الوقت أزرق على اليمين (زي اللينك)، نقطة لحالة المراجعة، والملاحظة جنبه.</summary>
    private void DrawBookmarkItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _bookmarkList.Items.Count) return;
        var g = e.Graphics;
        var lb = _bookmarkList;
        var row = new Rectangle(0, e.Bounds.Y, lb.ClientSize.Width, e.Bounds.Height);
        var item = (BookmarkRow)lb.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0 && item.Bookmark != null;
        bool hot = e.Index == _hotBookmarkIndex && item.Bookmark != null;
        using (var b = new SolidBrush(lb.BackColor)) g.FillRectangle(b, row);
        if (selected || hot)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Modern.FillRound(g, selected ? (lb.Focused ? Theme.AccentDark : Theme.SelectionInactive) : Theme.PanelLight,
                new RectangleF(1, row.Y + 1, row.Width - 2, row.Height - 2), 5);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        }

        if (item.Bookmark == null)
        {
            TextRenderer.DrawText(g, item.Note, lb.Font, row, Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            return;
        }

        using var timeFont = new Font("Consolas", 9f);
        var ts = TextRenderer.MeasureText(g, item.Time, timeFont, Size.Empty, TextFormatFlags.NoPadding);
        int x = row.Right - 8;
        var b0 = item.Bookmark;
        Color dot = ReviewScheduler.IsMastered(b0) ? Theme.Success : b0.ReviewDueUtc != null ? Theme.Folder : Color.Empty;
        if (dot != Color.Empty)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var db = new SolidBrush(dot);
            g.FillEllipse(db, x - 7, row.Y + row.Height / 2f - 3.5f, 7, 7);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            x -= 13;
        }
        var tr = new Rectangle(x - ts.Width, row.Y, ts.Width, row.Height);
        TextRenderer.DrawText(g, item.Time, timeFont, tr, Modern.Blend(Theme.Accent, Color.White, 0.3),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var nr = new Rectangle(row.X + 6, row.Y, tr.Left - 12 - row.X, row.Height);
        TextRenderer.DrawText(g, item.Note, lb.Font, nr, item.Note == "—" ? Theme.Muted : Theme.Text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void RefreshNotesPane()
    {
        CommitNotes();
        var v = _currentVideo ?? SelectedVideo();
        _notesVideo = v;

        _loadingNotes = true;
        try
        {
            UpdateNotesTitle();
            _notesBox.Enabled = v != null;
            _notesBox.Text = v?.Notes ?? "";

            _bookmarkList.BeginUpdate();
            _bookmarkList.Items.Clear();
            if (v != null)
            {
                foreach (var b in v.Bookmarks.OrderBy(b => b.TimeMs))
                {
                    string tip = b.ReviewDueUtc is { } due ? $"{b.Note}\nالمراجعة الجاية: {due.ToLocalTime():yyyy/MM/dd}"
                        : ReviewScheduler.IsMastered(b) ? b.Note + "\n✅ اتثبتت بالمراجعة" : b.Note;
                    _bookmarkList.Items.Add(new BookmarkRow(b, TimeFormat.Format(b.TimeMs), string.IsNullOrWhiteSpace(b.Note) ? "—" : b.Note, tip));
                }
                if (v.Bookmarks.Count == 0)
                    _bookmarkList.Items.Add(new BookmarkRow(null, "", "اضغط B لعلامة، أو Shift+B لعلامة بملاحظة، أو R لعلامة مراجعة", ""));
            }
            _bookmarkList.EndUpdate();
        }
        finally { _loadingNotes = false; }

        UpdateSeekMarkers();
    }

    private void CommitNotes()
    {
        _notesSaveTimer.Stop();
        var v = _notesVideo;
        if (v == null || _loadingNotes) return;
        string text = _notesBox.Text;
        if (string.Equals(text, v.Notes ?? "", StringComparison.Ordinal)) return;
        v.Notes = string.IsNullOrWhiteSpace(text) ? null : text;
        v.Touch();
        MarkDirty();
        RefreshVideo(v);
        UpdateNotesTitle();
    }

    private void UpdateSeekMarkers()
    {
        var v = _currentVideo;
        long len = CurrentLength();
        _seekBar.Markers = v != null && len > 0
            ? v.Bookmarks.Select(b => (double)b.TimeMs / len).ToList()
            : Array.Empty<double>();
        _seekBar.LoopRange = _loopA >= 0 && _loopB > _loopA && len > 0
            ? ((double)_loopA / len, (double)_loopB / len)
            : null;
    }

    // =====================================================================
    //  العلامات المرجعية
    // =====================================================================

    private void AddBookmark(bool withNote)
    {
        var v = _currentVideo;
        if (v == null || !IsActive) { ShowStatus("شغّل فيديو الأول عشان تحط علامة"); return; }

        long t = _mp.Time;
        string note = "";
        if (withNote)
        {
            bool wasPlaying = _mp.IsPlaying;
            if (wasPlaying) _mp.SetPause(true);
            string? input = Dialogs.Input(this, "علامة جديدة", $"ملاحظة عند {TimeFormat.Format(t)}:");
            if (wasPlaying) _mp.SetPause(false);
            if (input == null) return;
            note = input;
        }

        v.Bookmarks.Add(new Bookmark { TimeMs = t, Note = note });
        v.Touch();
        MarkDirty();
        RefreshVideo(v);
        RefreshNotesPane();
        ShowNotesSection();
        ShowStatus($"علامة عند {TimeFormat.Format(t)} ✓" + (withNote ? "" : "  —  Shift+B لعلامة بملاحظة"), 3);
    }

    private Bookmark? SelectedBookmark() => (_bookmarkList.SelectedItem as BookmarkRow)?.Bookmark;

    private void JumpToSelectedBookmark()
    {
        var b = SelectedBookmark();
        var v = _notesVideo;
        if (b == null || v == null) return;

        if (v == _currentVideo && IsActive) { _mp.Time = b.TimeMs; return; }
        var g = _library.FindGroupOf(v);
        if (g != null) PlayVideo(g, v, b.TimeMs);
    }

    private void EditSelectedBookmark()
    {
        var b = SelectedBookmark();
        var v = _notesVideo;
        if (b == null || v == null) return;
        string? note = Dialogs.Input(this, "تعديل العلامة", $"ملاحظة عند {TimeFormat.Format(b.TimeMs)}:", b.Note);
        if (note == null) return;
        b.Note = note;
        v.Touch();
        MarkDirty();
        RefreshNotesPane();
    }

    private void DeleteSelectedBookmark()
    {
        var b = SelectedBookmark();
        var v = _notesVideo;
        if (b == null || v == null) return;
        v.Bookmarks.Remove(b);
        v.Touch();
        MarkDirty();
        RefreshVideo(v);
        RefreshNotesPane();
    }

    /// <summary>تلميح الشريط: لو الماوس جنب علامة، اعرض ملاحظتها.</summary>
    private string? BookmarkNear(double fraction)
    {
        var v = _currentVideo;
        long len = CurrentLength();
        if (v == null || len <= 0) return null;
        long t = (long)(fraction * len), tolerance = Math.Max(1500, len / 150);
        var b = v.Bookmarks.OrderBy(x => Math.Abs(x.TimeMs - t)).FirstOrDefault();
        if (b == null || Math.Abs(b.TimeMs - t) > tolerance) return null;
        return string.IsNullOrWhiteSpace(b.Note) ? "علامة" : b.Note;
    }

    // =====================================================================
    //  تكرار A–B
    // =====================================================================

    /// <summary>A: أول ضغطة بداية، التانية نهاية (يبدأ التكرار)، التالتة تلغي.</summary>
    private void CycleAbLoop()
    {
        if (!IsActive) { ShowStatus("شغّل فيديو الأول"); return; }
        long t = _mp.Time;

        if (_loopA < 0)
        {
            _loopA = t;
            _loopB = -1;
            ShowStatus($"الجزء هيبدأ من {TimeFormat.Format(t)} — اضغط A تاني عند آخر الجزء اللي عايز تكرره", 5);
        }
        else if (_loopB < 0)
        {
            if (t <= _loopA + 1000) { ShowStatus("النهاية لازم تبقى بعد البداية بثانية على الأقل"); return; }
            _loopB = t;
            _mp.Time = _loopA;
            ShowStatus($"بكرّر الجزء من {TimeFormat.Format(_loopA)} لـ {TimeFormat.Format(_loopB)}  (اضغط A تاني توقفه)", 5);
        }
        else
        {
            ClearAbLoop();
            ShowStatus("وقف تكرار الجزء");
        }
        UpdateSeekMarkers();
    }

    private void ClearAbLoop()
    {
        _loopA = _loopB = -1;
        _seekBar.LoopRange = null;
    }

    /// <summary>بيتنده من التايمر: لو عدّينا B نرجع لـ A.</summary>
    private void CheckAbLoop(long t)
    {
        if (_loopA >= 0 && _loopB > _loopA && t >= _loopB) _mp.Time = _loopA;
    }

    // =====================================================================
    //  لقطة شاشة
    // =====================================================================

    private void TakeScreenshot()
    {
        var v = _currentVideo;
        if (v == null || !IsActive) { ShowStatus("شغّل فيديو الأول"); return; }
        long t = _mp.Time;

        string fileName = NotesExporter.SafeFileName($"{v.Title} - {TimeFormat.Format(t).Replace(':', '-')}") + ".png";
        string? dir = SnapshotDirFor(v);
        if (dir == null) { ShowStatus("مقدرتش أحفظ اللقطة"); return; }

        string full = Path.Combine(dir, fileName);
        bool ok;
        try { ok = _mp.TakeSnapshot(0, full, 0, 0); }
        catch (Exception ex) { Log.Error("snapshot", ex); ok = false; }
        if (!ok) { ShowStatus("مقدرتش آخد اللقطة"); return; }

        _lastSnapshotDir = dir;
        // علامة تلقائية عند اللقطة عشان تلاقيها في الملاحظات
        v.Bookmarks.Add(new Bookmark { TimeMs = t, Note = "لقطة شاشة: " + fileName });
        v.Touch();
        MarkDirty();
        RefreshNotesPane();
        ShowStatus($"اتحفظت اللقطة في «{Path.GetFileName(dir)}» ✓", 4);
    }

    /// <summary>فولدر «لقطات» جنب الفيديو، ولو مش ممكن (أو فيديو نت) فـ Data\snapshots.</summary>
    private static string? SnapshotDirFor(VideoItem v)
    {
        var candidates = new List<string>();
        if (!v.IsOnline && Path.GetDirectoryName(v.LocalPath) is { } videoDir)
            candidates.Add(Path.Combine(videoDir, "لقطات"));
        candidates.Add(Path.Combine(Storage.AppDataDir, "snapshots"));

        foreach (string dir in candidates)
        {
            try
            {
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch { /* فولدر للقراءة بس */ }
        }
        return null;
    }

    private void OpenSnapshotsFolder()
    {
        string dir = _lastSnapshotDir ?? (_currentVideo != null ? SnapshotDirFor(_currentVideo) : null)
                     ?? Path.Combine(Storage.AppDataDir, "snapshots");
        Directory.CreateDirectory(dir);
        try { Process.Start("explorer.exe", $"\"{dir}\""); } catch { }
    }

    // =====================================================================
    //  تصدير الملاحظات
    // =====================================================================

    private void ExportNotesDialog(Course? course)
    {
        CommitNotes();
        course ??= SelectedCourse() ?? (_currentGroup != null ? _library.FindCourseOf(_currentGroup) : null);
        if (course == null) { Dialogs.Info(this, "حدد كورس الأول."); return; }

        using var dlg = new SaveFileDialog
        {
            Title = $"تصدير ملاحظات «{course.Name}»",
            Filter = "ملف ملاحظات (*.md)|*.md|نص عادي (*.txt)|*.txt",
            FileName = NotesExporter.SafeFileName(course.Name + " - ملاحظات") + ".md",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dlg.FileName, NotesExporter.BuildMarkdown(course), System.Text.Encoding.UTF8);
            ShowStatus("اتصدّرت الملاحظات ✓", 4);
            OpenInExplorer(dlg.FileName);
        }
        catch (Exception ex)
        {
            Log.Error("export-notes", ex);
            Dialogs.Error(this, "مقدرتش أحفظ الملف:\n" + ex.Message);
        }
    }

    // =====================================================================
    //  الشاشة الرئيسية: «كمّل اللي بدأته»
    // =====================================================================

    private void BuildHome()
    {
        _welcome.Controls.Clear();
        _welcome.Padding = new Padding(28, 20, 28, 12);
        // أي مكان في الشاشة الرئيسية بيستقبل فولدر/ملفات بالسحب
        foreach (Control c in new Control[] { _welcome, _homeTitle })
        {
            c.AllowDrop = true;
            c.DragEnter += OnExternalDragEnter;
            c.DragDrop += (_, e) => OnExternalDrop(e, null);
        }
        _welcome.RightToLeft = RightToLeft.Yes;

        _homeTitle.Dock = DockStyle.Top;
        _homeTitle.Height = 48;
        _homeTitle.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
        _homeTitle.ForeColor = Theme.Text;
        _homeTitle.TextAlign = ContentAlignment.MiddleRight;
        _homeTitle.Text = "كمّل اللي بدأته";

        _homeCards.Dock = DockStyle.Fill;
        _homeCards.AutoScroll = true;
        _homeCards.WrapContents = true;
        _homeCards.FlowDirection = FlowDirection.LeftToRight; // مع RightToLeft = من اليمين
        _homeCards.RightToLeft = RightToLeft.Yes;
        _homeCards.BackColor = Theme.Panel;
        _homeCards.AllowDrop = true;
        _homeCards.DragEnter += OnExternalDragEnter;
        _homeCards.DragDrop += (_, e) => OnExternalDrop(e, null);

        _homeHint.Dock = DockStyle.Bottom;
        _homeHint.Height = 84;
        _homeHint.ForeColor = Theme.Muted;
        _homeHint.TextAlign = ContentAlignment.MiddleCenter;
        _homeHint.RightToLeft = RightToLeft.Yes;
        _homeHint.Text = "اسحب فولدر كورس وارميه هنا (الفولدر = كورس، وكل فولدر فرعي = محاضرة)\n" +
                         "أو Ctrl+U لرابط يوتيوب أو قائمة تشغيل";
        _homeHint.AllowDrop = true;
        _homeHint.DragEnter += OnExternalDragEnter;
        _homeHint.DragDrop += (_, e) => OnExternalDrop(e, null);

        _homeActions.Dock = DockStyle.Top;
        _homeActions.FlowDirection = FlowDirection.RightToLeft;
        _homeActions.WrapContents = true;
        _homeActions.AutoSize = true;
        _homeActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _homeActions.BackColor = Theme.Panel;
        _homeActions.Padding = new Padding(0, 0, 0, LogicalToDeviceUnits(6));
        _homeActions.AllowDrop = true;
        _homeActions.DragEnter += OnExternalDragEnter;
        _homeActions.DragDrop += (_, e) => OnExternalDrop(e, null);

        BuildHomePlan();
        _homeTopics.Dock = DockStyle.Top;
        _homeTopics.Height = LogicalToDeviceUnits(44);
        _homeTopics.FlowDirection = FlowDirection.RightToLeft;
        _homeTopics.WrapContents = false;
        _homeTopics.AutoScroll = false;
        _homeTopics.BackColor = Theme.Panel;
        _homeTopics.Padding = new Padding(6, 6, 6, 4);
        _welcome.Controls.Add(_homeCards);
        _welcome.Controls.Add(_homeHint);
        _welcome.Controls.Add(_homePlan);
        _welcome.Controls.Add(_homeTopics);
        _welcome.Controls.Add(_homeActions);
        _welcome.Controls.Add(_homeTitle);
    }

    /// <summary>
    /// مكتبة فاضية: 4 أزرار كبيرة بشرح (أول حاجة المستخدم الجديد يشوفها).
    /// فيها كورسات: صف صغير لنفس الإجراءات فوق «كمّل اللي بدأته».
    /// </summary>
    private void RefreshHomeActions(bool empty)
    {
        if (_homeActionsBig == empty && _homeActions.Controls.Count > 0) return;
        _homeActionsBig = empty;
        _homeActions.SuspendLayout();
        var old = _homeActions.Controls.Cast<Control>().ToList();
        _homeActions.Controls.Clear();
        foreach (var c in old) c.Dispose();

        ActionTile Tile(string glyph, string title, string subtitle, Color color, Action click)
        {
            var t = new ActionTile(glyph, title, empty ? subtitle : "")
            {
                IconColor = color,
                Compact = !empty,
                Size = empty ? new Size(LogicalToDeviceUnits(330), LogicalToDeviceUnits(92))
                             : new Size(LogicalToDeviceUnits(200), LogicalToDeviceUnits(52)),
                Margin = new Padding(LogicalToDeviceUnits(empty ? 8 : 5)),
                AllowDrop = true
            };
            t.Click += (_, _) => click();
            t.DragEnter += OnExternalDragEnter;
            t.DragDrop += (_, e) => OnExternalDrop(e, null);
            _toolTip.SetToolTip(t, subtitle);
            return t;
        }

        _homeActions.Controls.Add(Tile(Glyphs.OpenFolder, empty ? "ضيف فولدر فيديوهات" : "ضيف فولدر",
            "اختار فولدر كورس من جهازك (أو اسحبه وارميه هنا)", Theme.Folder, ImportCourseDialog));
        _homeActions.Controls.Add(Tile(Glyphs.Link, empty ? "ضيف رابط يوتيوب" : "رابط يوتيوب",
            "فيديو أو قائمة تشغيل كاملة من يوتيوب", Theme.Danger, () => AddUrlsDialog(null)));
        _homeActions.Controls.Add(Tile(Glyphs.Radio, empty ? "اسمع راديو أو شوف الحرم" : "بث مباشر",
            "آلاف محطات الراديو، وبث مباشر من مكة والمدينة", Theme.Success, ShowLiveWindow));
        if (empty)
            _homeActions.Controls.Add(Tile(Glyphs.Help, "إزاي أستخدم البرنامج؟",
                "جولة سريعة في 3 خطوات، والدليل الكامل", Theme.Accent, StartTourOrGuide));
        _homeActions.ResumeLayout(true);
    }

    private void RefreshHome()
    {
        if (!_welcome.Visible) return;

        RefreshHomeTopics();
        var courses = _library.Courses
            .Where(c => c.VideoCount > 0 && !c.IsCompleted && c.Status != CourseStatus.Done && InHomeScope(c))
            .OrderBy(c => c.Status is CourseStatus.Paused or CourseStatus.Planned ? 1 : 0)
            .ThenByDescending(c => c.Priority)
            .ThenByDescending(c => c.LastActivityUtc)
            .ThenBy(c => _library.Courses.IndexOf(c))
            .Take(8)
            .ToList();

        _homeCards.SuspendLayout();
        var old = _homeCards.Controls.Cast<Control>().ToList();
        _homeCards.Controls.Clear();
        foreach (var c in old) c.Dispose();

        bool emptyLibrary = _library.Courses.Count == 0;
        RefreshHomeActions(emptyLibrary);
        _homeHint.Visible = emptyLibrary;
        _homeHint.Text = "أو اسحب فولدر فيه فيديوهات من الويندوز وارميه في أي مكان هنا\n" +
                         "(الفولدر بيبقى كورس، وكل فولدر جواه بيبقى محاضرة)";
        _homeTitle.Text = courses.Count == 0
            ? (emptyLibrary ? "أهلًا بيك في «مسار» 👋  ابدأ من هنا"
                : _settings.HomeTopicFilter != "" ? "مفيش كورسات شغالة في الموضوع ده ✓" : "خلّصت كل الكورسات ✓")
            : "كمّل اللي بدأته";

        foreach (var c in courses) _homeCards.Controls.Add(CourseCard(c));
        _homeCards.ResumeLayout(true);
        RefreshHomePlan();
    }

    private Control CourseCard(Course c)
    {
        int percent = (int)Math.Round(c.WatchedCount * 100.0 / Math.Max(1, c.VideoCount));
        var next = c.Groups.SelectMany(g => g.Videos).FirstOrDefault(v => !v.Watched && IsAvailable(v));
        long remaining = c.AllVideos.Where(v => !v.Watched).Sum(v => Math.Max(0, v.DurationMs - v.LastPositionMs));

        var card = new Card
        {
            Fill = Theme.PanelLight,
            Size = new Size(LogicalToDeviceUnits(330), LogicalToDeviceUnits(124)),
            Margin = new Padding(LogicalToDeviceUnits(8)),
            Padding = new Padding(14, 10, 14, 8),
            BackColor = Theme.PanelLight,
            Cursor = Cursors.Hand,
            RightToLeft = RightToLeft.Yes
        };

        var name = new Label
        {
            Dock = DockStyle.Top, Height = 28, AutoEllipsis = true,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Theme.Text,
            Text = Modern.Bidi(c.Name), TextAlign = ContentAlignment.MiddleRight
        };
        string last = c.LastActivityUtc > DateTime.MinValue ? "  ·  " + RelativeDay(c.LastActivityUtc) : "";
        // المسار فوق الكورس: «Backend › ROUTE» (من غير اللي متفلتر عليه أصلًا)
        string filterKey = _settings.HomeTopicFilter;
        var pathParts = new List<string>();
        if (filterKey == "" && _library.TopicOf(c) is { } topic) pathParts.Add(topic.Name);
        if (!filterKey.StartsWith("p:") && _library.PlaceOf(c) is { } place) pathParts.Add(place.Name);
        string pathText = pathParts.Count > 0 ? Modern.Bidi(string.Join(" › ", pathParts)) + "  ·  " : "";
        var info = new Label
        {
            Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Muted, AutoEllipsis = true,
            Text = pathText + $"{c.WatchedCount} من {c.VideoCount} فيديو  ·  {percent}%" +
                   (remaining > 0 ? $"  ·  فاضل {TimeFormat.Format(remaining)}" : "") + last,
            TextAlign = ContentAlignment.MiddleRight
        };
        var nextLabel = new Label
        {
            Dock = DockStyle.Top, Height = 24, ForeColor = Theme.Text, AutoEllipsis = true,
            Text = next != null ? "الجاي: " + next.Title : "",
            TextAlign = ContentAlignment.MiddleRight
        };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 6, BackColor = Theme.Back };
        bar.Paint += (_, e) =>
        {
            int w = (int)(bar.Width * percent / 100.0);
            using var b = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(b, bar.Width - w, 0, w, bar.Height); // بيتملى من اليمين
        };

        card.Controls.Add(bar);
        card.Controls.Add(nextLabel);
        card.Controls.Add(info);
        card.Controls.Add(name);

        void Open(object? s, EventArgs e) => PlayCourse(c);
        foreach (Control ctl in new Control[] { card, name, info, nextLabel, bar })
        {
            ctl.Click += Open;
            ctl.Cursor = Cursors.Hand;
        }
        void Hover(bool on) { card.Fill = card.BackColor = on ? Modern.Blend(Theme.PanelLight, Color.White, 0.06) : Theme.PanelLight; }
        card.MouseEnter += (_, _) => Hover(true);
        card.MouseLeave += (_, _) =>
        {
            if (!card.ClientRectangle.Contains(card.PointToClient(Cursor.Position))) Hover(false);
        };
        return card;
    }

    private static string RelativeDay(DateTime utc)
    {
        int days = (DateTime.Now.Date - utc.ToLocalTime().Date).Days;
        return days switch
        {
            <= 0 => "النهارده",
            1 => "امبارح",
            2 => "من يومين",
            < 11 => $"من {days} أيام",
            _ => $"من {days} يوم"
        };
    }

    /// <summary>يقفل الفيديو ويرجع للشاشة الرئيسية.</summary>
    private void GoHome()
    {
        if (_fullscreen) ToggleFullscreen();
        StopPlaybackCore(clearCurrent: true);
        RefreshHome();
    }

    // =====================================================================
    //  الترجمة ومسارات الصوت
    // =====================================================================

    private void BuildTracksMenu(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        bool active = IsActive;

        var load = Item("تحميل ملف ترجمة…", LoadSubtitleDialog);
        load.Enabled = active;
        menu.DropDownItems.Add(load);
        menu.DropDownItems.Add(new ToolStripSeparator());

        var subs = SubMenu("الترجمة");
        var audio = SubMenu("مسار الصوت");
        if (active)
        {
            try
            {
                int currentSpu = _mp.Spu;
                foreach (var t in _mp.SpuDescription)
                {
                    int id = t.Id;
                    var it = Item(id < 0 ? "بدون ترجمة" : t.Name, () => _mp.SetSpu(id));
                    it.Checked = id == currentSpu;
                    subs.DropDownItems.Add(it);
                }
                int currentAudio = _mp.AudioTrack;
                foreach (var t in _mp.AudioTrackDescription)
                {
                    int id = t.Id;
                    var it = Item(id < 0 ? "بدون صوت" : t.Name, () => _mp.SetAudioTrack(id));
                    it.Checked = id == currentAudio;
                    audio.DropDownItems.Add(it);
                }
            }
            catch (Exception ex) { Log.Error("tracks", ex); }
        }
        if (subs.DropDownItems.Count == 0) subs.DropDownItems.Add(new ToolStripMenuItem("(مفيش)") { Enabled = false });
        if (audio.DropDownItems.Count == 0) audio.DropDownItems.Add(new ToolStripMenuItem("(مفيش)") { Enabled = false });
        menu.DropDownItems.Add(subs);
        menu.DropDownItems.Add(audio);
    }

    private void LoadSubtitleDialog()
    {
        if (!IsActive) return;
        using var dlg = new OpenFileDialog
        {
            Title = "اختار ملف الترجمة",
            Filter = "ملفات الترجمة|*.srt;*.vtt;*.ass;*.ssa;*.sub|كل الملفات|*.*",
            InitialDirectory = _currentVideo is { IsOnline: false } v ? Path.GetDirectoryName(v.LocalPath) : null
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        bool ok = _mp.AddSlave(MediaSlaveType.Subtitle, new Uri(dlg.FileName).AbsoluteUri, true);
        ShowStatus(ok ? "اتحملت الترجمة ✓" : "مقدرتش أحمّل الترجمة");
    }

    // =====================================================================
    //  دايمًا فوق
    // =====================================================================

    private void ToggleAlwaysOnTop()
    {
        _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
        TopMost = _settings.AlwaysOnTop;
        if (_alwaysOnTopItem != null) _alwaysOnTopItem.Checked = _settings.AlwaysOnTop;
        ShowStatus(_settings.AlwaysOnTop ? "البرنامج دايمًا فوق الشبابيك التانية (T)" : "اتلغى «دايمًا فوق»");
    }
}
