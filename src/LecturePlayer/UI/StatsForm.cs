using System.Drawing.Drawing2D;
using System.Globalization;
using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>شاشة الإحصائيات: النهارده والسلسلة، خريطة 12 أسبوع، آخر 14 يوم، والكورسات مع التوقعات.</summary>
internal sealed class StatsForm : Form
{
    private readonly CourseLibrary _lib;
    private readonly IReadOnlyList<JournalEntry> _entries;
    private readonly AppSettings _settings;
    private SortedDictionary<DateOnly, double> _daily = new();
    private List<StudyStats.HeatCell> _cells = new();
    private IReadOnlyList<JournalEntry> _scope;               // سجلات النطاق المختار (كل حاجة أو موضوع)
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private readonly ToolTip _tip = new();
    private readonly Panel _heat = new();
    private readonly Panel _bars = new();
    private readonly Label _summary = new();
    private readonly SelectBox _topicCombo = new();
    private readonly List<Scope> _scopes = new();
    private readonly ListView _topics = new();
    private readonly Label _topicsTitle;
    private readonly ListView _courses = new();
    private const int Cell = 15, CellGap = 3;
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("ar-EG");

    /// <summary>عنصر في قايمة النطاق: كل المواضيع، موضوع، أو «من غير موضوع».</summary>
    private sealed record Scope(string Key, string Text, Topic? Topic, Place? Place = null)
    {
        public override string ToString() => Text;
    }

    public StatsForm(CourseLibrary lib, IReadOnlyList<JournalEntry> entries, AppSettings settings)
    {
        _lib = lib;
        _entries = entries;
        _settings = settings;
        _scope = entries;

        Text = "إحصائيات المذاكرة";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        RightToLeft = RightToLeft.Yes;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(860, 800);
        MinimumSize = new Size(720, 600);
        Padding = new Padding(10, 8, 10, 6);
        HandleCreated += (_, _) => NativeMethods.UseDarkTitleBar(this);
        if (Theme.AppIcon is { } icon) Icon = icon;

        // ---------- اختيار النطاق: كل المواضيع أو موضوع واحد ----------
        var scopeRow = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 2, 4, 6), BackColor = Theme.Window };
        var scopeLabel = new Label { Dock = DockStyle.Right, Width = 70, Text = "الموضوع:", ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight };
        _topicCombo.Dock = DockStyle.Right;
        _topicCombo.Width = 260;
        _scopes.Add(new Scope("", "كل المواضيع", null));
        foreach (var t in lib.Topics)
        {
            _scopes.Add(new Scope(t.Id, t.Name, t));
            foreach (var p in lib.PlacesOf(t)) _scopes.Add(new Scope("p:" + p.Id, "      🏫 " + p.Name, t, p));
        }
        foreach (var p in lib.PlacesOf(null)) _scopes.Add(new Scope("p:" + p.Id, "🏫 " + p.Name, null, p));
        if (lib.Topics.Count > 0 && lib.CoursesOf(null).Any()) _scopes.Add(new Scope("-", TopicStats.NoTopicName, null));
        _topicCombo.Items.AddRange(_scopes.Select(x => x.Text));
        _topicCombo.SelectedIndex = Math.Max(0, _scopes.FindIndex(x => x.Key == settings.StatsTopicFilter));
        _topicCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_topicCombo.SelectedIndex >= 0) { _settings.StatsTopicFilter = _scopes[_topicCombo.SelectedIndex].Key; Reload(); }
        };
        scopeRow.Controls.Add(_topicCombo);
        scopeRow.Controls.Add(scopeLabel);
        scopeRow.Visible = lib.Topics.Count > 0;

        _summary.Dock = DockStyle.Top;
        _summary.Height = 66;
        _summary.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _summary.TextAlign = ContentAlignment.MiddleRight;

        var heatTitle = Title("آخر 12 أسبوع");
        _heat.Dock = DockStyle.Top;
        _heat.Height = 7 * (Cell + CellGap) + 16;
        _heat.BackColor = Theme.Panel;
        _heat.Paint += PaintHeat;
        _heat.MouseMove += HeatHover;
        EnableDoubleBuffer(_heat);

        var barsTitle = Title("آخر 14 يوم (الخط = الهدف)");
        _bars.Dock = DockStyle.Top;
        _bars.Height = 150;
        _bars.BackColor = Theme.Panel;
        _bars.Paint += PaintBars;
        EnableDoubleBuffer(_bars);

        // ---------- المواضيع ----------
        _topicsTitle = Title("المواضيع");
        _topics.Dock = DockStyle.Top;
        _topics.Height = 120;
        SetupList(_topics);
        _topics.Columns.Add("الموضوع", 190);
        _topics.Columns.Add("كورسات", 60);
        _topics.Columns.Add("التقدم", 90);
        _topics.Columns.Add("الأسبوع ده", 90);
        _topics.Columns.Add("الإجمالي", 90);
        _topics.Columns.Add("فاضل", 90);
        _topics.Columns.Add("هتخلص", 95);
        _topics.Columns.Add("الفهم", 70);
        ReverseColumns(_topics);
        _topics.DoubleClick += (_, _) =>
        {
            // دبل كليك على موضوع ← الإحصائيات بتاعته
            if (_topics.SelectedItems.Count == 0 || _topics.SelectedItems[0].Tag is not TopicSummary ts) return;
            string key = ts.Place != null ? "p:" + ts.Place.Id : ts.Topic?.Id ?? "-";
            int target = _scopes.FindIndex(x => x.Key == key);
            if (target >= 0) _topicCombo.SelectedIndex = target;
        };

        // ---------- الكورسات ----------
        var coursesTitle = Title("الكورسات");
        _courses.Dock = DockStyle.Fill;
        SetupList(_courses);
        _courses.Columns.Add("الكورس", 210);
        _courses.Columns.Add("الحالة", 60);
        _courses.Columns.Add("التقدم", 60);
        _courses.Columns.Add("فاضل", 90);
        _courses.Columns.Add("معدلك/يوم", 85);
        _courses.Columns.Add("هتخلص", 95);
        _courses.Columns.Add("الموعد", 170);
        _courses.Columns.Add("الفهم", 120);
        ReverseColumns(_courses);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4, 10, 4, 0), BackColor = Theme.Window };
        buttons.Controls.Add(Btn("قفل", Close, PillStyle.Ghost));
        buttons.Controls.Add(Btn("حفظ تقرير الأسبوع في ملف…", SaveWeeklyReport, PillStyle.Primary));
        buttons.Controls.Add(Btn("نسخ التقرير", () =>
        {
            Clipboard.SetText(StudyStats.WeeklyReport(_lib, _entries, _today, _settings.DailyGoalMinutes));
            Dialogs.Info(this, "اتنسخ ✓ — الصقه في أي مكان (رسالة، ملاحظات، إيميل).");
        }, PillStyle.Ghost));

        // كل قسم كارت لوحده (زي باقي البرنامج)
        _summary.BackColor = Theme.Panel;
        _summaryCard = Section(null, _summary, 84);
        var heatCard = Section(heatTitle, _heat, _heat.Height + 44);
        var barsCard = Section(barsTitle, _bars, _bars.Height + 44);
        _topicsCard = Section(_topicsTitle, _topics, _topics.Height + 48);
        var coursesCard = Section(coursesTitle, _courses, 0);

        Controls.Add(coursesCard);
        foreach (var c in new Control[] { _topicsCard, barsCard, heatCard, _summaryCard })
        {
            Controls.Add(Spacer());
            Controls.Add(c);
        }
        Controls.Add(scopeRow);
        Controls.Add(buttons);
        Reload();
    }

    private static void SetupList(ListView lv)
    {
        lv.OwnerDraw = true;
        lv.DrawColumnHeader += (_, e) =>
        {
            using (var bg = new SolidBrush(Theme.Panel)) e.Graphics.FillRectangle(bg, e.Bounds);
            using (var line = new Pen(Theme.Border)) e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var r = Rectangle.Inflate(e.Bounds, -6, 0);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, new Font("Segoe UI", 9f, FontStyle.Bold), r, Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis);
        };
        lv.DrawItem += (_, e) => { };
        lv.DrawSubItem += (_, e) =>
        {
            if (e.Item == null || e.SubItem == null) return;
            bool sel = e.Item.Selected;
            using (var bg = new SolidBrush(sel ? Theme.AccentDark : Theme.Panel)) e.Graphics.FillRectangle(bg, e.Bounds);
            var r = Rectangle.Inflate(e.Bounds, -6, 0);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lv.Font, r, e.Item.ForeColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        lv.BackColor = Theme.Panel;
        lv.SmallImageList = new ImageList { ImageSize = new Size(1, 26) }; // صفوف أعلى شوية
        lv.View = View.Details;
        lv.FullRowSelect = true;
        lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        // من غير عكس (الرسم يدوي)، والأعمدة بتتعكس بـ DisplayIndex عشان أول عمود يبقى على اليمين
        lv.RightToLeftLayout = false;
        lv.RightToLeft = RightToLeft.No;
        lv.ShowItemToolTips = true;
        lv.ForeColor = Theme.Text;
        lv.BorderStyle = BorderStyle.None;
        lv.HandleCreated += (_, _) => NativeMethods.UseDarkExplorerTheme(lv);
    }

    private static void ReverseColumns(ListView lv)
    {
        int n = lv.Columns.Count;
        for (int i = 0; i < n; i++) lv.Columns[i].DisplayIndex = n - 1 - i;
    }

    private Scope CurrentScope => _topicCombo.SelectedIndex >= 0 && _topicCombo.SelectedIndex < _scopes.Count
        ? _scopes[_topicCombo.SelectedIndex] : new Scope("", "كل المواضيع", null);

    /// <summary>بيحسب كل حاجة من جديد للنطاق المختار.</summary>
    private void Reload()
    {
        var sc = CurrentScope;
        bool all = sc.Key == "";
        _scope = all ? _entries
            : sc.Place != null ? TopicStats.EntriesOfCourses(_lib.CoursesInPlace(sc.Place), _entries)
            : TopicStats.EntriesOf(_lib, sc.Topic, _entries, noTopic: sc.Key == "-");
        _daily = StudyStats.DailyMinutes(_scope);
        _cells = StudyStats.Heatmap(_daily, _today, 12);

        double today = StudyStats.MinutesOn(_daily, _today);
        var ws = StudyStats.StartOfWeek(_today);
        double week = Enumerable.Range(0, 7).Sum(i => StudyStats.MinutesOn(_daily, ws.AddDays(i)));
        double total = _daily.Values.Sum();
        if (all)
        {
            var streak = StudyStats.Streak(_daily, _today, minMinutes: 10);
            _summary.Text = $"النهارده: {StudyStats.FormatMinutes(today)} من {_settings.DailyGoalMinutes} دقيقة {(today >= _settings.DailyGoalMinutes ? "✓" : "")}" +
                            $"     🔥 السلسلة: {streak.Current} يوم" + (streak.FreezesUsed > 0 ? $" (فيها {streak.FreezesUsed} يوم متجمد)" : "") +
                            $"\nالأسبوع ده: {StudyStats.FormatMinutes(week)}     أحسن سلسلة: {streak.Best} يوم     الإجمالي: {StudyStats.FormatMinutes(total)}";
        }
        else
        {
            var t = sc.Place != null
                ? TopicStats.ForPlace(_lib, sc.Place, _entries, _today, _settings.Rate)
                : TopicStats.For(_lib, sc.Topic, _entries, _today, _settings.Rate);
            string fc = t.Videos > 0 && t.Watched == t.Videos ? "خلص ✓"
                : t.FinishDate is { } fd ? $"هتخلص حوالي {fd:yyyy/MM/dd}" : "مفيش معدل كفاية للتوقع";
            _summary.Text = $"{Modern.Bidi(t.Name)}:  {t.Percent}%  ({t.Watched} من {t.Videos} فيديو في {t.Courses} كورس)     {fc}" +
                            $"\nالنهارده: {StudyStats.FormatMinutes(today)}     الأسبوع ده: {StudyStats.FormatMinutes(week)}     الإجمالي: {StudyStats.FormatMinutes(total)}" +
                            (t.Understanding is { } u ? $"     الفهم: {u}/100" : "");
        }

        // جدول المواضيع: بيظهر في «كل المواضيع» بس
        bool showTopics = all && _lib.Topics.Count > 0;
        _topicsCard.Visible = showTopics;
        if (showTopics)
        {
            _topics.BeginUpdate();
            _topics.Items.Clear();
            foreach (var t in TopicStats.AllWithPlaces(_lib, _entries, _today, _settings.Rate))
            {
                bool done = t.Videos > 0 && t.Watched == t.Videos;
                var item = new ListViewItem(new[]
                {
                    t.Place != null && t.Topic != null ? "      ↳ 🏫 " + t.Place.Name : t.Place != null ? "🏫 " + t.Place.Name : t.Name, t.Courses.ToString(), $"{t.Percent}%  ({t.Watched}/{t.Videos})",
                    StudyStats.FormatMinutes(t.WeekMinutes), StudyStats.FormatMinutes(t.TotalMinutes),
                    StudyStats.FormatMinutes(t.RemainingMinutes),
                    done ? "خلص ✓" : t.FinishDate is { } d ? d.ToString("yyyy/MM/dd") : "—",
                    t.Understanding is { } u ? $"{u}/100" : "—"
                }) { Tag = t, ForeColor = done ? Theme.Success : Theme.Text, ToolTipText = "دبل كليك لإحصائيات الموضوع ده لوحده" };
                _topics.Items.Add(item);
            }
            _topics.EndUpdate();
        }

        // الكورسات: كلها أو كورسات الموضوع
        var courses = all ? _lib.Courses
            : sc.Place != null ? _lib.CoursesInPlace(sc.Place).ToList()
            : _lib.CoursesOf(sc.Key == "-" ? null : sc.Topic).ToList();
        _courses.BeginUpdate();
        _courses.Items.Clear();
        FillCourses(_courses, courses, showTopic: all && _lib.Topics.Count > 0);
        _courses.EndUpdate();

        _heat.Invalidate();
        _bars.Invalidate();
    }

    private Card _summaryCard = null!, _topicsCard = null!;

    private static Label Title(string text) => new()
    {
        Dock = DockStyle.Top, Height = 30, Text = text, ForeColor = Theme.Text, BackColor = Theme.Panel,
        Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight
    };

    /// <summary>كارت بعنوان ومحتوى. height = 0 ← بياخد الباقي.</summary>
    private static Card Section(Label? title, Control content, int height)
    {
        var card = new Card { Dock = height == 0 ? DockStyle.Fill : DockStyle.Top, Fill = Theme.Panel };
        if (height > 0) card.Height = height + card.Padding.Vertical;
        var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 2, 12, 6) };
        content.Dock = DockStyle.Fill;
        inner.Controls.Add(content);
        if (title != null) inner.Controls.Add(title);
        card.Controls.Add(inner);
        card.VisibleChanged += (_, _) => { };
        return card;
    }

    private static Panel Spacer() => new() { Dock = DockStyle.Top, Height = 6, BackColor = Theme.Window };

    private static PillButton Btn(string text, Action click, PillStyle style)
    {
        var b = new PillButton(text, style) { Height = 34, Margin = new Padding(8, 0, 0, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    private static void EnableDoubleBuffer(Control c) =>
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(c, true);

    private void FillCourses(ListView list, IEnumerable<Course> source, bool showTopic)
    {
        foreach (var c in source.Where(c => c.VideoCount > 0)
                     .OrderBy(c => c.Status).ThenByDescending(c => c.LastActivityUtc))
        {
            var f = StudyStats.Forecast(c, _entries, _today, defaultRate: _settings.Rate);
            var p = StudyPlanner.PlanFor(c, _entries, _today, _settings.Rate);
            var u = UnderstandingIndex.For(c, _entries);
            string pct = $"{c.WatchedCount * 100 / Math.Max(1, c.VideoCount)}%";
            string finish = f.DaysLeft == 0 ? "خلص ✓" : f.FinishDate is { } d ? d.ToString("yyyy/MM/dd") : "—";
            string deadline = c.Deadline is { } dl
                ? $"{dl:yyyy/MM/dd} · " + p.Status switch
                {
                    PlanStatus.Overdue => "⚠ عدّى",
                    PlanStatus.Unrealistic => "⚠ مش واقعي",
                    _ => $"{StudyStats.FormatMinutes(p.MinutesPerStudyDay)}/يوم"
                }
                : "";
            string where = string.Join(" › ", new[] { _lib.TopicOf(c)?.Name, _lib.PlaceOf(c)?.Name }.Where(x => x != null));
            string name = showTopic && where.Length > 0 ? $"{c.Name}  ‹{where}›" : c.Name;
            var item = new ListViewItem(new[]
            {
                name, CourseFilter.StatusText(c.Status), pct, StudyStats.FormatMinutes(f.RemainingMinutes),
                StudyStats.FormatMinutes(f.AvgMinutesPerDay), finish, deadline,
                $"{u.Score}/100 · {u.Level}"
            });
            item.ToolTipText = $"مشاهدة {u.CoveragePct}%" + (u.QuizPct is { } q ? $" · اختبارات {q}%" : " · من غير اختبارات") +
                               (u.ReviewPct is { } r ? $" · مراجعة {r}%" : "") + (u.DifficultSpots > 0 ? $" · {u.DifficultSpots} جزء صعب" : "") + "\n" + u.Advice;
            if (p.Status is PlanStatus.Overdue or PlanStatus.Unrealistic && c.Deadline != null) item.ForeColor = Theme.Danger;
            else if (c.IsCompleted) item.ForeColor = Theme.Success;
            list.Items.Add(item);
        }
    }

    private Color LevelColor(int level)
    {
        if (level <= 0) return Theme.PanelLight;
        double t = new[] { 0, 0.3, 0.55, 0.8, 1.0 }[level];
        Color a = Theme.PanelLight, b = Theme.Success;
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    private int HeatLeft => 4;

    private void PaintHeat(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int weeks = _cells.Count == 0 ? 0 : _cells.Max(c => c.Week) + 1;
        foreach (var c in _cells)
        {
            var r = new Rectangle(HeatLeft + c.Week * (Cell + CellGap), 4 + c.Day * (Cell + CellGap), Cell, Cell);
            using var b = new SolidBrush(LevelColor(StudyStats.HeatLevel(c.Minutes, _settings.DailyGoalMinutes)));
            g.FillRectangle(b, r);
            if (c.Date == _today)
            {
                using var p = new Pen(Theme.Accent, 2);
                g.DrawRectangle(p, r);
            }
        }

        // أسماء الأيام جنب الخريطة
        using var font = new Font("Segoe UI", 8.5f);
        using var muted = new SolidBrush(Theme.Muted);
        int x = HeatLeft + weeks * (Cell + CellGap) + 6;
        for (int d = 0; d < 7; d += 2)
        {
            string name = Ar.DateTimeFormat.GetDayName(StudyStats.StartOfWeek(_today).AddDays(d).DayOfWeek);
            g.DrawString(name, font, muted, x, 4 + d * (Cell + CellGap) - 1);
        }

        // مفتاح الألوان
        int lx = x + 70;
        g.DrawString("أقل", font, muted, lx, 4);
        for (int i = 0; i <= 4; i++)
        {
            using var b = new SolidBrush(LevelColor(i));
            g.FillRectangle(b, lx + 30 + i * (Cell + 2), 6, Cell, Cell);
        }
        g.DrawString("أكتر", font, muted, lx + 30 + 5 * (Cell + 2) + 2, 4);
    }

    private void HeatHover(object? sender, MouseEventArgs e)
    {
        int w = (e.X - HeatLeft) / (Cell + CellGap), d = (e.Y - 4) / (Cell + CellGap);
        var cell = _cells.FirstOrDefault(c => c.Week == w && c.Day == d);
        string text = cell == null ? "" : $"{Ar.DateTimeFormat.GetDayName(cell.Date.DayOfWeek)} {cell.Date:yyyy/MM/dd}: {StudyStats.FormatMinutes(cell.Minutes)}";
        if (_tip.GetToolTip(_heat) != text) _tip.SetToolTip(_heat, text);
    }

    private void PaintBars(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        const int days = 14, top = 18, bottom = 24;
        double goal = Math.Max(1, _settings.DailyGoalMinutes);
        var values = Enumerable.Range(0, days).Select(i => (Date: _today.AddDays(i - days + 1), Min: StudyStats.MinutesOn(_daily, _today.AddDays(i - days + 1)))).ToList();
        double max = Math.Max(goal * 1.15, values.Max(v => v.Min) * 1.1);
        int h = _bars.Height - top - bottom, w = _bars.Width - 8;
        float slot = w / (float)days, bw = slot * 0.62f;

        using var font = new Font("Segoe UI", 8f);
        using var muted = new SolidBrush(Theme.Muted);
        using var text = new SolidBrush(Theme.Text);
        var center = new StringFormat { Alignment = StringAlignment.Center };

        for (int i = 0; i < days; i++)
        {
            var (date, min) = values[i];
            float x = 4 + i * slot + (slot - bw) / 2;
            float bh = (float)(min / max * h);
            using var b = new SolidBrush(min >= goal ? Theme.Success : min > 0 ? Theme.Accent : Theme.PanelLight);
            g.FillRectangle(b, x, top + h - Math.Max(bh, 2), bw, Math.Max(bh, 2));
            if (min > 0) g.DrawString(((int)Math.Round(min)).ToString(), font, text, x + bw / 2, top + h - bh - 15, center);
            g.DrawString(date.Day.ToString(), font, date == _today ? text : muted, x + bw / 2, top + h + 4, center);
        }

        float gy = top + h - (float)(goal / max * h);
        using var pen = new Pen(Theme.Folder, 1.5f) { DashStyle = DashStyle.Dash };
        g.DrawLine(pen, 4, gy, w, gy);
    }

    private void SaveWeeklyReport()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "ملف ملاحظات (*.md)|*.md",
            FileName = $"تقرير-المذاكرة-{StudyStats.StartOfWeek(_today):yyyy-MM-dd}.md"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, StudyStats.WeeklyReport(_lib, _entries, _today, _settings.DailyGoalMinutes), new System.Text.UTF8Encoding(true));
            Dialogs.Info(this, "اتحفظ ✓\n" + dlg.FileName);
        }
        catch (Exception ex) { Dialogs.Error(this, "مقدرتش أحفظ التقرير:\n" + ex.Message); }
    }
}
