using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// التبويبات: تبويب لكل دورة (والفيديو الحالي فيها) أو كتاب أو سبورة أو عرض مفتوح.
/// التبديل يوقف المعروض ويحفظ موضعه، ويفتح الآخر من موضعه. التبويبات تُحفظ لكل مكتبة على هذا الجهاز
/// وتعود عند الفتح التالي. داخل «جلسة» يُحفظ موضع كل تبويب في الجلسة نفسها (لكل مجموعة موضعها).
/// </summary>
internal partial class MainForm
{
    private const int MaxTabs = 12;
    private readonly List<WorkTab> _tabs = new();
    private int _activeTab = -1;
    private string _tabsKey = "";
    private TabStrip? _tabStrip;
    private bool _tabsBusy;                 // أثناء فتح/إغلاق تبويب بأنفسنا: لا مزامنة من العنوان
    private System.Windows.Forms.Timer? _tabsSaveTimer;

    internal IReadOnlyList<WorkTab> OpenTabs => _tabs;
    internal int ActiveTabIndex => _activeTab;
    internal TabStrip? TabStripControl => _tabStrip;

    private void BuildTabStrip()
    {
        _tabStrip = new TabStrip { Dock = DockStyle.Top, Visible = false };
        _tabStrip.Height = LogicalToDeviceUnits(36);
        _tabStrip.TabClicked += i => ActivateTab(i);
        _tabStrip.TabClosed += i => CloseTab(i);
        _tabStrip.TabMenu += ShowTabMenu;
        _tabStrip.SessionClicked += p => ShowSessionMenu(p);
        _tabStrip.ActionClicked += () => { if (ActiveSession is { } s) EndLessonDialog(s); };
        _tabStrip.MoreClicked += ShowMoreTabsMenu;
        _videoCard.Controls.Add(_tabStrip);
        _tabStrip.SendToBack(); // Dock=Top فوق _videoArea (Fill)
    }

    private static string TabsKeyFor(string libraryPath)
    {
        try { return Path.GetFullPath(libraryPath).ToLowerInvariant(); } catch { return libraryPath.ToLowerInvariant(); }
    }

    // ---------------- الحالة الحالية ← تبويب ----------------

    /// <summary>التبويب الذي يمثّل المعروض الآن (null = الرئيسية أو بث مباشر).</summary>
    private WorkTab? CurrentItemTab()
    {
        if (!HasLibrary) return null;
        if (PresentationShown && _presBook is { } pb)
            return new WorkTab { Kind = WorkTab.BookKind, Ref = pb.Id, Title = pb.Title, Page = pb.LastPage };
        if (ReaderShown && _currentBook is { } b)
            return new WorkTab { Kind = WorkTab.BookKind, Ref = b.Id, Title = b.Title, Page = b.LastPage };
        if (_currentLive == null && _currentVideo is { } v && _currentGroup is { } g)
        {
            var c = _library.FindCourseOf(g);
            return new WorkTab
            {
                Kind = WorkTab.CourseKind, Ref = c?.Id ?? "g:" + g.Id, GroupId = g.Id, ItemPath = v.Path,
                Title = c?.Name ?? g.Name, PositionMs = v.LastPositionMs
            };
        }
        return null;
    }

    /// <summary>بعد أي فتح أو إغلاق (من UpdateTitle): التبويب النشط يتبع المعروض.</summary>
    private void SyncTabs()
    {
        if (_tabsBusy || !HasLibrary || _tabStrip == null) return;
        var cur = CurrentItemTab();
        int prev = _activeTab;
        if (cur == null)
        {
            if (prev >= 0) { CaptureTab(prev); _activeTab = -1; TabsChanged(); }
            return;
        }
        int i = _tabs.FindIndex(t => t.Same(cur));
        if (prev >= 0 && prev != i) CaptureTab(prev);
        if (i < 0)
        {
            i = prev >= 0 && prev < _tabs.Count ? prev + 1 : _tabs.Count;
            _tabs.Insert(i, cur);
            if (_tabs.Count > MaxTabs)
            {
                int drop = _tabs.FindIndex(t => t != cur);
                if (drop >= 0)
                {
                    var gone = _tabs[drop];
                    if (gone.Kind == WorkTab.BookKind && _presSuspended?.Id == gone.Ref) FinishSuspendedPresentation();
                    _tabs.RemoveAt(drop);
                    if (drop < i) i--;
                    ShowStatus($"أُغلق تبويب «{gone.Title}» (الحد {MaxTabs} تبويبًا)", 4);
                }
            }
        }
        else
        {
            var t = _tabs[i];
            t.Title = cur.Title;
            if (cur.Kind == WorkTab.CourseKind) { t.GroupId = cur.GroupId; t.ItemPath = cur.ItemPath; }
        }
        _activeTab = i;
        TabsChanged();
    }

    /// <summary>يحفظ موضع التبويب من حالة الملف نفسه (بعد إيقافه يكون موضعه محفوظًا).</summary>
    private void CaptureTab(int i)
    {
        if (i < 0 || i >= _tabs.Count) return;
        var t = _tabs[i];
        if (t.Kind == WorkTab.BookKind)
        {
            if (_library.Books.FirstOrDefault(x => x.Id == t.Ref) is { } b) { t.Page = b.LastPage; t.Title = b.Title; }
        }
        else if (t.ItemPath != null)
        {
            var v = _library.AllVideos.FirstOrDefault(x => x.Path == t.ItemPath);
            if (v != null) t.PositionMs = v.LastPositionMs;
        }
    }

    private void TabsChanged()
    {
        RefreshTabStrip();
        MirrorSession();
        if (_tabsKey.Length > 0)
        {
            _settings.OpenTabs[_tabsKey] = new TabsState { Tabs = _tabs.Select(t => t.Copy()).ToList(), Active = _activeTab, SessionId = _sessionId };
            if (_settings.OpenTabs.Count > 20)
                foreach (var k in _settings.OpenTabs.Keys.Where(k => k != _tabsKey).Take(_settings.OpenTabs.Count - 20).ToList()) _settings.OpenTabs.Remove(k);
            if (_tabsSaveTimer == null)
            {
                _tabsSaveTimer = new System.Windows.Forms.Timer { Interval = 1500 };
                _tabsSaveTimer.Tick += (_, _) => { _tabsSaveTimer!.Stop(); Storage.SaveSettings(_settings); };
            }
            _tabsSaveTimer.Stop();
            _tabsSaveTimer.Start();
        }
    }

    private string TabGlyph(WorkTab t)
    {
        if (t.Kind == WorkTab.CourseKind)
        {
            var v = t.ItemPath == null ? null : _library.AllVideos.FirstOrDefault(x => x.Path == t.ItemPath);
            return v?.IsWebLesson == true ? "🎓" : "▶";
        }
        var b = _library.Books.FirstOrDefault(x => x.Id == t.Ref);
        return b == null ? "📄" : b.IsPresentation ? "📽" : b.IsBoard ? "🧑‍🏫" : "📖";
    }

    private void RefreshTabStrip()
    {
        if (_tabStrip == null) return;
        var items = new List<TabStrip.Item>();
        for (int i = 0; i < _tabs.Count; i++)
        {
            var t = _tabs[i];
            string tip = t.Title ?? "";
            if (t.Kind == WorkTab.CourseKind && t.ItemPath != null && _library.AllVideos.FirstOrDefault(x => x.Path == t.ItemPath) is { } v)
                tip += "  ›  " + v.Title;
            if (i < 9) tip += $"   (Ctrl+{i + 1})";
            items.Add(new TabStrip.Item(TabGlyph(t), t.Title ?? "", tip, i == _activeTab));
        }
        var s = ActiveSession;
        _tabStrip.SessionActive = s != null;
        bool lesson = s?.ClassId != null;
        _tabStrip.SessionText = s != null ? (lesson ? $"👥  {s.Name}{LessonRemainingText(s)}  ▾" : $"📚  {s.Name}  ▾")
            : IsTeacher ? "👥  الحصة  ▾" : $"📚  {SessionWord}  ▾";
        _tabStrip.SessionTip = IsTeacher ? "الحصة: ابدأ حصة إحدى مجموعاتك (تكمل من حيث توقفت)، أو الخطة والواجب والتصدير"
            : "الجلسة: احفظ التبويبات المفتوحة باسم وارجع إليها لاحقًا";
        _tabStrip.ActionText = lesson ? "⏹  إنهاء الحصة" : "";
        _tabStrip.ActionTip = "إنهاء الحصة: اكتب الواجب وصدّر للطلاب إن أردت — والحصة القادمة لهذه المجموعة تبدأ من هنا";
        _tabStrip.SetItems(items);
        UpdateLessonTimer();
        UpdateTabStripVisible();
    }

    private void UpdateTabStripVisible()
    {
        if (_tabStrip == null) return;
        bool show = HasLibrary && !_fullscreen && !_miniMode && (_tabs.Count > 0 || _sessionId != null || _settings.Role != null);
        if (_tabStrip.Visible != show) _tabStrip.Visible = show;
    }

    // ---------------- أوامر التبويبات ----------------

    internal void ActivateTab(int i)
    {
        if (i < 0 || i >= _tabs.Count) return;
        var t = _tabs[i];
        if (i == _activeTab && CurrentItemTab()?.Same(t) == true) return;
        if (_activeTab >= 0) CaptureTab(_activeTab);
        bool saved = _sessionId != null;   // داخل جلسة: موضع الجلسة (لكل مجموعة موضعها)
        if (t.Kind == WorkTab.BookKind)
        {
            var b = _library.Books.FirstOrDefault(x => x.Id == t.Ref);
            if (b == null) { RemoveMissingTab(i); return; }
            int? page = saved && t.Page is int p && p > 0 && p != b.LastPage ? p : null;
            OpenBook(b, page);
            return;
        }
        var g = (t.GroupId != null ? _library.AllGroups.FirstOrDefault(x => x.Id == t.GroupId) : null);
        var v = t.ItemPath != null ? g?.Videos.FirstOrDefault(x => x.Path == t.ItemPath) : null;
        if (v == null && t.ItemPath != null)
        {
            v = _library.AllVideos.FirstOrDefault(x => x.Path == t.ItemPath);
            g = v != null ? _library.FindGroupOf(v) : g;
        }
        if (v == null || g == null)
        {
            var c = _library.Courses.FirstOrDefault(x => x.Id == t.Ref);
            if (c == null) { RemoveMissingTab(i); return; }
            PlayCourse(c);
            return;
        }
        long? start = saved && t.PositionMs is long ms && Math.Abs(ms - v.LastPositionMs) > 2000 ? ms : null;
        PlayVideo(g, v, start);
    }

    private void RemoveMissingTab(int i)
    {
        var t = _tabs[i];
        _tabs.RemoveAt(i);
        if (_activeTab == i) _activeTab = -1; else if (_activeTab > i) _activeTab--;
        TabsChanged();
        ShowStatus($"«{t.Title}» لم يعد موجودًا في المكتبة — أُغلق تبويبه", 5);
    }

    /// <summary>إغلاق تبويب: يُحفظ موضعه، وإن كان المعروض يُغلق ويعود إلى الرئيسية.</summary>
    internal void CloseTab(int i)
    {
        if (i < 0 || i >= _tabs.Count) return;
        var t = _tabs[i];
        bool shown = i == _activeTab && CurrentItemTab()?.Same(t) == true;
        CaptureTab(i);
        _tabsBusy = true;
        try
        {
            if (shown)
            {
                if (_fullscreen) ToggleFullscreen();
                if (PresentationShown) EndPresentation(showHome: true);
                else if (ReaderShown) CloseReader(showHome: true);
                else StopPlaybackCore(clearCurrent: true);
            }
            else if (t.Kind == WorkTab.BookKind && _presSuspended?.Id == t.Ref) FinishSuspendedPresentation();
            else if (t.Kind != WorkTab.BookKind && _videoSuspended?.Video.Path == t.ItemPath) DropSuspendedVideo();
        }
        finally { _tabsBusy = false; }
        // أثناء الإغلاق قد تتغيّر القائمة (رسالة إغلاق ثانية من القارئ مثلًا): نحذف التبويب نفسه لا رقمه القديم
        var active = _activeTab >= 0 && _activeTab < _tabs.Count ? _tabs[_activeTab] : null;
        _tabs.Remove(t);
        _activeTab = shown || active == null || active == t ? -1 : _tabs.IndexOf(active);
        TabsChanged();
        if (shown) RefreshHome();
    }

    private void CloseTab(WorkTab t) { int i = _tabs.IndexOf(t); if (i >= 0) CloseTab(i); }
    private void ActivateTab(WorkTab t) { int i = _tabs.IndexOf(t); if (i >= 0) ActivateTab(i); }

    /// <summary>Ctrl+W و«✕ إغلاق»: يغلق التبويب المعروض (وإن لم يكن له تبويب يغلق المعروض فقط).</summary>
    internal void CloseActiveTab()
    {
        if (_activeTab >= 0 && _activeTab < _tabs.Count && CurrentItemTab()?.Same(_tabs[_activeTab]) == true) { CloseTab(_activeTab); return; }
        if (_fullscreen) ToggleFullscreen();
        if (PresentationShown) EndPresentation(showHome: true);
        else if (ReaderShown) CloseReader(showHome: true);
        else if (_currentVideo != null || _currentLive != null) GoHome();
    }

    private void CloseOtherTabs(WorkTab keep)
    {
        foreach (var t in _tabs.Where(x => x != keep).ToList()) CloseTab(t);
    }

    private void CloseAllTabs()
    {
        foreach (var t in _tabs.ToList()) CloseTab(t);
    }

    private void StepTab(int delta)
    {
        if (_tabs.Count == 0) return;
        int from = _activeTab >= 0 ? _activeTab : (delta > 0 ? -1 : 0);
        int to = ((from + delta) % _tabs.Count + _tabs.Count) % _tabs.Count;
        ActivateTab(to);
    }

    private void MoveTab(int i, int delta)
    {
        int j = i + delta;
        if (i < 0 || j < 0 || i >= _tabs.Count || j >= _tabs.Count) return;
        (_tabs[i], _tabs[j]) = (_tabs[j], _tabs[i]);
        if (_activeTab == i) _activeTab = j; else if (_activeTab == j) _activeTab = i;
        TabsChanged();
    }

    /// <summary>مفاتيح التبويبات. true = نُفّذ.</summary>
    private bool HandleTabKeys(Keys keyData)
    {
        if (!HasLibrary) return false;
        switch (keyData)
        {
            case Keys.Control | Keys.Tab: if (_tabs.Count == 0) return false; StepTab(1); return true;
            case Keys.Control | Keys.Shift | Keys.Tab: if (_tabs.Count == 0) return false; StepTab(-1); return true;
            case Keys.Control | Keys.W:
                if (_activeTab < 0 && !ReaderShown && !PresentationShown) return false;
                CloseActiveTab(); return true;
        }
        if ((keyData & Keys.Modifiers) == Keys.Control)
        {
            var k = keyData & Keys.KeyCode;
            if (k is >= Keys.D1 and <= Keys.D9 && _tabs.Count > 0)
            {
                int n = k - Keys.D1;
                ActivateTab(k == Keys.D9 ? _tabs.Count - 1 : Math.Min(n, _tabs.Count - 1));
                return true;
            }
        }
        return false;
    }

    private void ShowTabMenu(int i, Point screen)
    {
        if (i < 0 || i >= _tabs.Count) return;
        var tab = _tabs[i];
        var m = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        m.Items.Add(Item("إغلاق التبويب", () => CloseTab(tab), display: "Ctrl+W"));
        m.Items.Add(Item("إغلاق التبويبات الأخرى", () => CloseOtherTabs(tab)));
        m.Items.Add(Item("إغلاق كل التبويبات", CloseAllTabs));
        m.Items.Add(new ToolStripSeparator());
        var right = Item("انقل إلى اليمين", () => MoveTab(_tabs.IndexOf(tab), -1)); right.Enabled = i > 0; m.Items.Add(right);
        var left = Item("انقل إلى اليسار", () => MoveTab(_tabs.IndexOf(tab), 1)); left.Enabled = i < _tabs.Count - 1; m.Items.Add(left);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item(ActiveSession == null ? $"📚  احفظ التبويبات كـ{SessionWord}…" : $"📚  احفظ نسخة باسم آخر…", () => NewSessionDialog(fromTabs: true)));
        m.Closed += (_, _) => BeginInvoke(m.Dispose);
        m.Show(screen);
        LastTabMenu = m;
    }
    internal ContextMenuStrip? LastTabMenu;

    private void ShowMoreTabsMenu(Point screen)
    {
        if (_tabStrip == null) return;
        var m = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        for (int i = _tabStrip.VisibleCount; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            m.Items.Add(Item(TabGlyph(tab) + "  " + tab.Title, () => ActivateTab(tab)));
        }
        m.Closed += (_, _) => BeginInvoke(m.Dispose);
        m.Show(screen);
    }

    /// <summary>القائمة «عرض ← التبويبات».</summary>
    private ToolStripMenuItem BuildTabsMenu()
    {
        var menu = SubMenu("التبويبات");
        menu.DropDownOpening += (_, _) =>
        {
            menu.DropDownItems.Clear();
            var next = Item("التبويب التالي", () => StepTab(1), display: "Ctrl+Tab"); next.Enabled = _tabs.Count > 1;
            var prev = Item("التبويب السابق", () => StepTab(-1), display: "Ctrl+Shift+Tab"); prev.Enabled = _tabs.Count > 1;
            var close = Item("إغلاق التبويب", CloseActiveTab, display: "Ctrl+W"); close.Enabled = _activeTab >= 0 || ReaderShown || PresentationShown;
            var all = Item("إغلاق كل التبويبات", CloseAllTabs); all.Enabled = _tabs.Count > 0;
            menu.DropDownItems.AddRange(new ToolStripItem[] { next, prev, close, all });
            if (_tabs.Count > 0) menu.DropDownItems.Add(new ToolStripSeparator());
            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                var it = Item(TabGlyph(tab) + "  " + tab.Title, () => ActivateTab(tab), display: i < 9 ? $"Ctrl+{i + 1}" : null);
                it.Checked = i == _activeTab;
                menu.DropDownItems.Add(it);
            }
        };
        menu.DropDownItems.Add(new ToolStripMenuItem("…"));
        return menu;
    }

    // ---------------- الحفظ والاستعادة ----------------

    /// <summary>بعد فتح مكتبة: تعود تبويباتها (دون فتح أي منها).</summary>
    private void RestoreTabs()
    {
        _tabs.Clear();
        _activeTab = -1;
        _sessionId = null;
        _tabsKey = HasLibrary ? TabsKeyFor(_libraryPath) : "";
        if (_tabsKey.Length > 0 && _settings.OpenTabs.TryGetValue(_tabsKey, out var st) && st?.Tabs != null)
        {
            foreach (var t in st.Tabs)
            {
                if (t == null || string.IsNullOrEmpty(t.Ref)) continue;
                bool ok = t.Kind == WorkTab.BookKind ? _library.Books.Any(b => b.Id == t.Ref)
                    : _library.Courses.Any(c => c.Id == t.Ref) || (t.ItemPath != null && _library.AllVideos.Any(v => v.Path == t.ItemPath));
                if (ok && !_tabs.Any(x => x.Same(t))) _tabs.Add(t.Copy());
            }
            if (st.SessionId != null && _library.Sessions.Any(s => s.Id == st.SessionId)) _sessionId = st.SessionId;
        }
        RefreshTabStrip();
    }

    /// <summary>قبل الإغلاق: موضع المعروض يدخل تبويبه وجلسته.</summary>
    private void SaveTabsForClose()
    {
        if (!HasLibrary || _tabsKey.Length == 0) return;
        if (_activeTab >= 0) CaptureTab(_activeTab);
        if (_activeTab >= 0 && CurrentItemTab() is { } cur && _tabs[_activeTab].Same(cur))
        {
            if (cur.Kind == WorkTab.CourseKind) _tabs[_activeTab].PositionMs = _currentVideo?.LastPositionMs;
        }
        _tabsSaveTimer?.Stop();
        MirrorSession();
        _settings.OpenTabs[_tabsKey] = new TabsState { Tabs = _tabs.Select(t => t.Copy()).ToList(), Active = _activeTab, SessionId = _sessionId };
    }
}
