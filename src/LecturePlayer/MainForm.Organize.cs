using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// تنظيم الواجهة: «الأدوات» (من الشريط الجانبي ومن القوايم) و«ابحث عن أمر» (Ctrl+Shift+Space).
/// القوايم هي المصدر الوحيد للأوامر: البحث بيقرا منها مباشرة، فأي أمر في القوايم بيلاقيه البحث تلقائيًا.
/// </summary>
internal sealed partial class MainForm
{
    // ---------------- «الأدوات» ----------------

    /// <summary>أدوات الشرح (بتتكرر في قايمة «الأدوات» وفي زرار «الأدوات» في الشريط الجانبي).</summary>
    private IEnumerable<ToolStripItem> TeachingToolItems() => new ToolStripItem[]
    {
        Item("⏺  سجّل شرح (الشاشة والكاميرا والصوت)…", () => { if (Recording) _ = StopRecordingAsync(save: true); else StartRecordingDialog(SelectedGroup()); }, display: "Ctrl+Shift+F9"),
        Item("🖊  أقلام على الشاشة (فوق أي برنامج)", ToggleScreenInk, display: "Ctrl+Shift+F8"),
        Item("🧑‍🏫  سبورة جديدة…", () => NewBoardDialog(SelectedGroup())),
        Item("📽  عرض PowerPoint…", () => AddPresentationsDialog(SelectedShelfContext()), display: "Ctrl+Shift+P"),
        Item("💻  معمل الكود…", () => OpenCodeLab(""), display: "Ctrl+L"),
    };

    /// <summary>أدوات المذاكرة اللي مش مربوطة بالفيديو اللي شغال.</summary>
    private IEnumerable<ToolStripItem> StudyToolItems() => new ToolStripItem[]
    {
        Item("مراجعة اليوم…", ShowReview, display: "F7"),
        Item("إحصائيات المذاكرة", ShowStats, display: "F8"),
        Item("بومودورو (تشغيل / إيقاف)", TogglePomodoro, display: "F9"),
        Item("وضع التركيز…", StartFocusDialog),
        Item("بحث في كلام كل الفيديوهات…", ShowTranscriptSearch, display: "Ctrl+Shift+F"),
        Item("اسأل عن أي شيء في الدورة…", AskCourse, display: "Ctrl+K"),
    };

    private IEnumerable<ToolStripItem> NetToolItems() => new ToolStripItem[]
    {
        Item("📻  بث مباشر (راديو وتلفزيون)", ShowLiveWindow, display: "Ctrl+Shift+L"),
        Item("⬇  تنزيل من يوتيوب (فيديو أو صوت)…", () => OpenDownloadDialog(null), display: "Ctrl+D"),
        Item("التنزيلات…", ShowDownloadsPanel),
    };

    /// <summary>زرار «الأدوات» في الشريط الجانبي: نفس محتوى قايمة «الأدوات» بأقسام واضحة.</summary>
    private void ShowToolsMenu(Control anchor)
    {
        var m = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        void Head(string t) => m.Items.Add(new ToolStripLabel(t) { ForeColor = Theme.Muted, Font = new Font(m.Font, FontStyle.Bold) });
        m.Items.Add(Item("🔎  ابحث عن أمر…", ShowCommandPalette, display: "Ctrl+Shift+Space"));
        m.Items.Add(new ToolStripSeparator());
        // الأقرب لدورك أولًا: المذاكرة للطالب، والشرح للمعلم
        void Teaching() { Head("الشرح"); m.Items.AddRange(TeachingToolItems().ToArray()); m.Items.Add(new ToolStripSeparator()); }
        void Study() { Head("المذاكرة"); m.Items.AddRange(StudyToolItems().ToArray()); m.Items.Add(new ToolStripSeparator()); }
        if (_settings.Role == "student") { Study(); Teaching(); } else { Teaching(); Study(); }
        Head(SessionWord == "حصة" ? "الحصص" : "الجلسات");
        m.Items.AddRange(SessionMenuItems().ToArray());
        m.Items.Add(new ToolStripSeparator());
        Head("الإنترنت");
        m.Items.AddRange(NetToolItems().ToArray());
        m.Closed += (_, _) => BeginInvoke(m.Dispose);
        m.Show(anchor, new Point(-4, 0), ToolStripDropDownDirection.Left);
        LastToolsMenu = m;
    }
    internal ContextMenuStrip? LastToolsMenu;

    // ---------------- «ابحث عن أمر» ----------------

    internal sealed record CommandEntry(string Title, string Path, string? Keys, Action Run);

    /// <summary>كل الأوامر: من القوايم (بمساراتها) + قايمة الإضافة حسب المحدد. الأوامر المعطّلة والفواصل مش بتدخل.</summary>
    internal List<CommandEntry> CollectCommands()
    {
        var list = new List<CommandEntry>();
        var seen = new HashSet<string>();
        void Walk(ToolStripItemCollection items, string path)
        {
            foreach (var it in items.OfType<ToolStripMenuItem>())
            {
                string title = CleanTitle(it.Text ?? "");
                if (title.Length == 0 || title == "…") continue;
                if (it.DropDownItems.Count > 0) { Walk(it.DropDownItems, path.Length > 0 ? path + " › " + title : title); continue; }
                if (!it.Enabled) continue;
                string key = path + "|" + title;
                if (!seen.Add(key)) continue;
                var item = it;
                string? keys = !string.IsNullOrEmpty(it.ShortcutKeyDisplayString) ? it.ShortcutKeyDisplayString
                    : it.ShortcutKeys != Keys.None ? new KeysConverter().ConvertToString(it.ShortcutKeys) : null;
                list.Add(new CommandEntry(title, path, keys, () => item.PerformClick()));
            }
        }
        Walk(_menu.Items, "");
        using (var add = new ContextMenuStrip())
        {
            if (_shelfMode) BuildShelfAddMenu(_shelfTree.SelectedNode?.Tag, add.Items);
            else BuildAddMenu(_tree.SelectedNode?.Tag, add.Items);
            foreach (var it in add.Items.OfType<ToolStripMenuItem>().Where(i => i.Enabled && i.DropDownItems.Count == 0))
            {
                var item = it;
                string title = CleanTitle(it.Text ?? "");
                if (seen.Add("إضافة|" + title)) list.Add(new CommandEntry(title, "إضافة", it.ShortcutKeyDisplayString, () => item.PerformClick()));
            }
        }
        return list;
    }

    private static string CleanTitle(string t)
    {
        t = t.Replace("&", "").Trim();
        // الإيموجي والرموز اللي في أول الأمر مش جزء من اسمه
        int i = 0;
        while (i < t.Length && !char.IsLetterOrDigit(t[i]) && t[i] != '«' && t[i] != '(') i++;
        return t[i..].Trim();
    }

    /// <summary>ترتيب النتايج: أول الاسم > كلمة في الاسم > جوه الاسم > في المسار. من غير تشكيل، والأ/إ/ا واحد.</summary>
    internal static List<CommandEntry> RankCommands(IEnumerable<CommandEntry> all, string query)
    {
        var q = BookText.Normalize(query).Trim();
        // نفس الأمر ممكن يكون في أكتر من قايمة: يظهر مرة واحدة
        all = all.GroupBy(c => BookText.Normalize(c.Title)).Select(g => g.First()).ToList();
        if (q.Length == 0) return all.Take(40).ToList();
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return all.Select(c =>
            {
                var t = BookText.Normalize(c.Title);
                var p = BookText.Normalize(c.Path);
                int score = 0;
                foreach (var w in words)
                {
                    if (t.StartsWith(w)) score += 30;
                    else if ((" " + t).Contains(" " + w)) score += 20;
                    else if (t.Contains(w)) score += 10;
                    else if (p.Contains(w)) score += 4;
                    else return (c, -1);
                }
                return (c, score - Math.Min(t.Length, 60) / 20);
            })
            .Where(x => x.Item2 >= 0).OrderByDescending(x => x.Item2).Select(x => x.c).Take(40).ToList();
    }

    internal Form? CommandPalette { get; private set; }

    internal void ShowCommandPalette()
    {
        if (CommandPalette is { IsDisposed: false } open) { open.Activate(); return; }
        var all = CollectCommands();
        var f = new Form
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false,
            BackColor = Theme.Border, Padding = new Padding(1), RightToLeft = RightToLeft.Yes, RightToLeftLayout = true, KeyPreview = true,
            Size = new Size(LogicalToDeviceUnits(560), LogicalToDeviceUnits(420))
        };
        var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(LogicalToDeviceUnits(8)) };
        var box = new TextBox
        {
            Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Window, ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 12f), PlaceholderText = "اكتب اسم الأمر… (مثلًا: سبورة، تسجيل، PowerPoint، مراجعة)"
        };
        var list = new ListBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = LogicalToDeviceUnits(40), IntegralHeight = false
        };
        var gap = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(8), BackColor = Theme.Panel };
        inner.Controls.Add(list);
        inner.Controls.Add(gap);
        inner.Controls.Add(box);
        f.Controls.Add(inner);
        List<CommandEntry> shown = new();
        void Fill() { shown = RankCommands(all, box.Text); list.BeginUpdate(); list.Items.Clear(); foreach (var c in shown) list.Items.Add(c); if (list.Items.Count > 0) list.SelectedIndex = 0; list.EndUpdate(); }
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= shown.Count) return;
            var c = shown[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? Theme.Accent : Theme.Panel)) e.Graphics.FillRectangle(bg, e.Bounds);
            var r = e.Bounds; r.Inflate(-LogicalToDeviceUnits(8), 0);
            var flags = TextFormatFlags.RightToLeft | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var top = new Rectangle(r.X, r.Y + LogicalToDeviceUnits(3), r.Width, r.Height / 2);
            var bottom = new Rectangle(r.X, r.Y + r.Height / 2, r.Width, r.Height / 2 - LogicalToDeviceUnits(2));
            TextRenderer.DrawText(e.Graphics, c.Title, new Font("Segoe UI", 10f), top, sel ? Color.White : Theme.Text, flags);
            if (c.Path.Length > 0) TextRenderer.DrawText(e.Graphics, c.Path, new Font("Segoe UI", 8f), bottom, sel ? Color.Gainsboro : Theme.Muted, flags);
            if (c.Keys != null) TextRenderer.DrawText(e.Graphics, c.Keys, new Font("Segoe UI", 8.5f), top, sel ? Color.White : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        };
        void Run()
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= shown.Count) return;
            var c = shown[list.SelectedIndex];
            f.Close();
            BeginInvoke(() => { try { c.Run(); } catch (Exception ex) { Log.Error("command " + c.Title, ex); } });
        }
        box.TextChanged += (_, _) => Fill();
        f.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { f.Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { Run(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up && list.Items.Count > 0) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); e.Handled = true; }
        };
        list.MouseClick += (_, _) => Run();
        f.Deactivate += (_, _) => { if (!f.IsDisposed && SmokeDialogShots == false) f.Close(); };
        f.FormClosed += (_, _) => CommandPalette = null;
        var area = RectangleToScreen(ClientRectangle);
        f.Location = new Point(area.Left + (area.Width - f.Width) / 2, area.Top + LogicalToDeviceUnits(70));
        Fill();
        CommandPalette = f;
        f.Show(this);
        box.Focus();
    }
}
