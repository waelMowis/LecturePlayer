using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace LecturePlayer.UI;

/// <summary>
/// «الأيقونة واللون» لموضوع أو مصدر أو كورس: معاينة حية، ألوان جاهزة + لون حر، وأيقونات متقسمة فئات مع بحث.
/// </summary>
internal static class AppearanceForm
{
    /// <param name="Icon">مفتاح الأيقونة، أو null = أيقونة النوع العادية.</param>
    /// <param name="ColorHex">«#RRGGBB»، أو null = لون النوع العادي.</param>
    /// <param name="ApplyToChildren">اللون يتطبق كمان على اللي تحته (المصادر والكورسات).</param>
    public sealed record Result(string? Icon, string? ColorHex, bool ApplyToChildren);

    /// <summary>للفحص التلقائي: الشاشة تختار دول لوحدها وتدوس «حفظ». ColorIndex = -1 افتراضي.</summary>
    internal static (string? Icon, int ColorIndex, bool Children, string? Search)? SmokePick;
    internal static int SmokeTiles, SmokeVisibleAfterSearch;
    /// <summary>للفحص التلقائي: صورة من الشاشة بعد الاختيار (للمراجعة بالعين).</summary>
    internal static Action<Form>? SmokeShot;

    /// <param name="kind">topic / place / course.</param>
    /// <param name="childrenText">نص خيار «طبّق على اللي تحته» (null = مايظهرش).</param>
    public static Result? Show(IWin32Window owner, string kind, string name, string? icon, string? colorHex, string? childrenText)
    {
        string kindLabel = kind switch { "topic" => "الموضوع", "place" => "المصدر", _ => "الكورس" };
        using var kit = new FormKit($"الأيقونة واللون — {name}", 760);
        using var tips = new ToolTip { InitialDelay = 250, ReshowDelay = 100 };

        string? selIcon = IconGlyphs.Exists(icon) ? icon : null;
        Color? selColor = IconGlyphs.ParseHex(colorHex);
        Color Current() => selColor ?? Theme.DefaultKindColor(kind);
        // شعار ليه لون أصلي (C#، Python…): لو اللون لسه افتراضي أو جاي من شعار قبله، ياخد لون العلامة.
        // أول ما المستخدم يختار لون بإيده، بنسيب اختياره.
        bool autoColor = selColor is { } c0 && IconGlyphs.BrandColor(selIcon) is { } b0 && c0.ToArgb() == b0.ToArgb();
        var tiles = new List<Tile>();

        // ---------- المعاينة ----------
        kit.Header("🎨  اللون");
        var preview = kit.Full(new PreviewBox(), 48);
        preview.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = preview.ClientSize.Width, h = preview.ClientSize.Height;
            var c = Current();
            // أيقونة كبيرة على اليمين + الاسم بلونه
            var big = new RectangleF(w - 52, (h - 44) / 2f, 44, 44);
            DrawIcon(g, kind, selIcon, c, big);
            using var nameFont = new Font("Segoe UI", 12f, FontStyle.Bold);
            var textColor = selColor is { } sc ? IconGlyphs.Readable(sc) : Theme.Text;
            TextRenderer.DrawText(g, name, nameFont, new Rectangle(140, 0, w - 200, h), textColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.RightToLeft | TextFormatFlags.NoPrefix);
            // على الشمال: شكله الحقيقي في الشجرة (16px) عادي وبعد مايخلص
            int s = Math.Max(16, preview.LogicalToDeviceUnits(16));
            using (var a = Theme.NodeIcon(s, kind, selIcon, c, done: false)) g.DrawImage(a, 12, h / 2 - s - 3);
            using (var b = Theme.NodeIcon(s, kind, selIcon, c, done: true)) g.DrawImage(b, 12, h / 2 + 3);
            using var small = new Font("Segoe UI", 8.5f);
            TextRenderer.DrawText(g, "في الشجرة", small, new Point(16 + s, h / 2 - s - 2), Theme.Muted);
            TextRenderer.DrawText(g, "عند الاكتمال ✓", small, new Point(16 + s, h / 2 + 4), Theme.Muted);
        };

        // ---------- اللون ----------
        var colors = kit.Full(new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, // مع RightToLeft بتاع الشاشة = بتبدأ من اليمين
            WrapContents = true, BackColor = Theme.Panel, Padding = new Padding(0, 2, 0, 0)
        }, 44);
        var swatches = new List<Swatch>();
        Swatch? customSwatch = null;
        void SelectColor(Color? c)
        {
            selColor = c;
            // لون مش من الجاهزين (لون علامة أو لون حر): يظهر في خانة «لون تاني» ويتعلّم عليها
            if (customSwatch != null && c is { } free && !IconGlyphs.Palette.Any(p => p.Color.ToArgb() == free.ToArgb()))
            {
                customSwatch.Value = free;
                customSwatch.Invalidate();
            }
            foreach (var sw in swatches)
                sw.Selected = sw.IsCustom ? c is { } x && !IconGlyphs.Palette.Any(p => p.Color.ToArgb() == x.ToArgb()) && sw.Value?.ToArgb() == x.ToArgb()
                                          : sw.Value?.ToArgb() == c?.ToArgb();
            preview.Invalidate();
            foreach (Control t in IconTiles()) t.Invalidate();
        }
        var def = new Swatch(null, Theme.DefaultKindColor(kind), isCustom: false);
        tips.SetToolTip(def, $"افتراضي (لون {kindLabel} العادي)");
        def.Click += (_, _) => { autoColor = false; SelectColor(null); };
        swatches.Add(def);
        foreach (var (label, c) in IconGlyphs.Palette)
        {
            var sw = new Swatch(c, c, isCustom: false);
            tips.SetToolTip(sw, label);
            sw.Click += (_, _) => { autoColor = false; SelectColor(sw.Value); };
            swatches.Add(sw);
        }
        customSwatch = new Swatch(null, Theme.Muted, isCustom: true);
        tips.SetToolTip(customSwatch, "لون آخر…");
        customSwatch.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, AnyColor = true, Color = Current() };
            if (dlg.ShowDialog(kit.Form) != DialogResult.OK) return;
            customSwatch!.Value = dlg.Color;
            customSwatch.Invalidate();
            autoColor = false;
            SelectColor(Color.FromArgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
        };
        swatches.Add(customSwatch);
        // لون حر محفوظ قبل كده (مش من الجاهزين): يظهر في خانة «لون تاني»
        if (selColor is { } start && !IconGlyphs.Palette.Any(p => p.Color.ToArgb() == start.ToArgb())) customSwatch.Value = start;
        colors.Controls.AddRange(swatches.ToArray());

        // ---------- اللي تحته ----------
        ToggleSwitch? children = null;
        if (childrenText != null)
        {
            children = kit.Full(new ToggleSwitch { Text = childrenText + " (اللون فقط — يحتفظ كل عنصر بأيقونته)", Checked = false }, 32);
        }

        // ---------- الأيقونة ----------
        kit.Header("🔣  الأيقونة");
        var search = kit.Row("ابحث:", new TextBox { PlaceholderText = "مثلًا: كود، C#، بايثون، SQL، يوتيوب، طب…" }, 32);
        var grid = kit.Full(new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, // مع RightToLeft بتاع الشاشة = بتبدأ من اليمين
            WrapContents = true, AutoScroll = true, BackColor = Theme.Panel
        }, GridHeight(children != null));
        grid.HandleCreated += (_, _) => NativeMethods.UseDarkExplorerTheme(grid);

        var headers = new List<(Label Label, List<Tile> Tiles)>();
        IEnumerable<Control> IconTiles() => tiles;
        void SelectIcon(string? key)
        {
            selIcon = key;
            foreach (var t in tiles) t.Selected = t.Key == key;
            if (selColor == null || autoColor)
            {
                if (IconGlyphs.BrandColor(key) is { } brand) { SelectColor(brand); autoColor = true; }
                else if (autoColor) { SelectColor(null); autoColor = false; }
            }
            preview.Invalidate();
        }
        int labelWidth = grid.Width - SystemInformation.VerticalScrollBarWidth - 10;
        Label Head(string text) => new()
        {
            Text = text, Width = labelWidth, Height = 24, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomRight, RightToLeft = RightToLeft.No, Margin = new Padding(4, 4, 0, 2)
        };
        Tile MakeTile(string? key, string tip)
        {
            var t = new Tile(key, kind, Current) { Selected = key == selIcon };
            tips.SetToolTip(t, tip);
            t.Click += (_, _) => SelectIcon(t.Key);
            t.DoubleClick += (_, _) => { SelectIcon(t.Key); kit.Form.DialogResult = DialogResult.OK; };
            tiles.Add(t);
            return t;
        }

        grid.SuspendLayout();
        bool first = true;
        foreach (var cat in IconGlyphs.Categories)
        {
            var head = Head(cat);
            grid.Controls.Add(head);
            var list = new List<Tile>();
            if (first)
            {
                // أول خانة: أيقونة النوع العادية (من غير صف لوحدها)
                var defTile = MakeTile(null, $"أيقونة {kindLabel} العادية");
                grid.Controls.Add(defTile);
                list.Add(defTile);
                first = false;
            }
            foreach (var g in IconGlyphs.All.Where(x => x.Category == cat))
            {
                var t = MakeTile(g.Key, g.Label);
                t.Tag = g.Label + " " + g.Key.Replace('_', ' ') + " " + cat;
                grid.Controls.Add(t);
                list.Add(t);
            }
            headers.Add((head, list));
        }
        grid.ResumeLayout();

        search.TextChanged += (_, _) =>
        {
            string q = search.Text.Trim();
            grid.SuspendLayout();
            foreach (var (label, list) in headers)
            {
                int shown = 0;
                foreach (var t in list)
                {
                    bool ok = q.Length == 0 || t.Key == null && "الافتراضي".Contains(q)
                              || (t.Tag as string)?.Contains(q, StringComparison.CurrentCultureIgnoreCase) == true;
                    t.Visible = ok;
                    if (ok) shown++;
                }
                label.Visible = shown > 0;
            }
            grid.ResumeLayout();
            grid.AutoScrollPosition = Point.Empty;
        };

        kit.Buttons("حفظ", "إلغاء", ("↺  استعادة الافتراضي", () => { autoColor = false; SelectIcon(null); SelectColor(null); }));
        SelectColor(selColor);

        // لو فيه أيقونة مختارة (ممكن تكون في آخر الفئات)، الشبكة تفتح عندها
        void ShowSelected()
        {
            if (tiles.FirstOrDefault(t => t.Selected && t.Key != null) is { } sel) grid.ScrollControlIntoView(sel);
        }
        kit.Form.Shown += (_, _) => ShowSelected();

        if (SmokePick is { } pick)
        {
            kit.Form.Shown += (_, _) =>
            {
                SmokeTiles = tiles.Count;
                if (pick.Search != null)
                {
                    search.Text = pick.Search;
                    SmokeVisibleAfterSearch = tiles.Count(t => t.Visible);
                    search.Text = "";
                }
                SelectIcon(pick.Icon);
                // -2 = سيب اللون زي ما الشاشة اختارته (لون العلامة للشعارات)
                if (pick.ColorIndex != -2) SelectColor(pick.ColorIndex < 0 ? null : IconGlyphs.Palette[pick.ColorIndex].Color);
                ShowSelected();
                if (children != null) children.Checked = pick.Children;
                Application.DoEvents();
                SmokeShot?.Invoke(kit.Form);
                kit.Form.DialogResult = DialogResult.OK;
            };
        }

        if (kit.Show(owner) != DialogResult.OK) return null;
        return new Result(selIcon, selColor is { } col ? IconGlyphs.ToHex(col) : null, children?.Checked == true);
    }

    /// <summary>
    /// طول شبكة الأيقونات: أكبر حاجة تخلي الشاشة كلها تبان من غير سكرول بره (شاشة لابتوب 768 لحد شاشة كبيرة).
    /// الأرقام بالـ 96 DPI (الشاشة بتتكبر لوحدها مع تكبير الويندوز).
    /// </summary>
    private static int GridHeight(bool hasChildren)
    {
        float scale = 1f;
        try { using var g = Graphics.FromHwnd(IntPtr.Zero); scale = g.DpiY / 96f; } catch { }
        int screen = (int)(Screen.FromPoint(Cursor.Position).WorkingArea.Height / scale);
        int available = (int)(screen * 0.85) - 40;
        // متقاسة من صورة الشاشة: الأزرار 60 + كارت اللون 178 + خيار اللي تحته 40 + عنوان وبحث الأيقونات 102 + الهوامش 38
        int other = 60 + 178 + (hasChildren ? 40 : 0) + 102 + 38;
        return Math.Clamp(available - other, 140, 340);
    }

    private static void DrawIcon(Graphics g, string kind, string? icon, Color c, RectangleF r)
    {
        if (IconGlyphs.Exists(icon)) { IconGlyphs.Draw(g, icon!, r, c); return; }
        var state = g.Save();
        g.TranslateTransform(r.X, r.Y);
        Theme.DrawKind(g, (int)r.Width, kind, c);
        g.Restore(state);
    }

    private sealed class PreviewBox : Panel
    {
        public PreviewBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }
    }

    /// <summary>خانة أيقونة في الشبكة (بتترسم باللون المختار حاليًا).</summary>
    private sealed class Tile : Control
    {
        public string? Key { get; }
        private readonly string _kind;
        private readonly Func<Color> _color;
        private bool _selected, _hot;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Invalidate(); } }

        public Tile(string? key, string kind, Func<Color> color)
        {
            Key = key; _kind = kind; _color = color;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(40, 40);
            Margin = new Padding(3);
            Cursor = Cursors.Hand;
            BackColor = Theme.Panel;
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 2, Height - 2);
            if (_selected || _hot)
            {
                Modern.FillRound(g, _selected ? Theme.AccentDark : Theme.PanelLight, r, 6);
                if (_selected) Modern.DrawRound(g, Theme.Accent, r, 6);
            }
            float s = Math.Min(Width, Height) * 0.56f;
            DrawIcon(g, _kind, Key, _color(), new RectangleF((Width - s) / 2f, (Height - s) / 2f, s, s));
        }
    }

    /// <summary>دايرة لون. IsCustom = خانة «لون تاني…» (فيها ＋).</summary>
    private sealed class Swatch : Control
    {
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? Value { get; set; }
        public bool IsCustom { get; }
        private readonly Color _shown;
        private bool _selected, _hot;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Invalidate(); } }

        public Swatch(Color? value, Color shown, bool isCustom)
        {
            Value = value; _shown = shown; IsCustom = isCustom;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(34, 34);
            Margin = new Padding(3, 2, 3, 2);
            Cursor = Cursors.Hand;
            BackColor = Theme.Panel;
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var outer = new RectangleF(1.5f, 1.5f, Width - 3, Height - 3);
            if (_selected)
                using (var pen = new Pen(Theme.Text, 2f)) g.DrawEllipse(pen, outer);
            else if (_hot)
                using (var pen = new Pen(Theme.Muted, 1.5f)) g.DrawEllipse(pen, outer);
            var inner = RectangleF.Inflate(outer, -4.5f, -4.5f);
            var fill = IsCustom ? Value ?? Theme.PanelLight : _shown;
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, inner);
            if (IsCustom)
            {
                // ＋ في النص
                using var pen = new Pen(Value is { } v && (0.299 * v.R + 0.587 * v.G + 0.114 * v.B) > 150 ? Color.Black : Color.White, 2f);
                float cx = inner.X + inner.Width / 2, cy = inner.Y + inner.Height / 2, a = inner.Width * 0.25f;
                g.DrawLine(pen, cx - a, cy, cx + a, cy);
                g.DrawLine(pen, cx, cy - a, cx, cy + a);
            }
            else if (Value == null)
            {
                // «افتراضي»: خط مايل
                using var pen = new Pen(Theme.Panel, 2f);
                g.DrawLine(pen, inner.Right - inner.Width * 0.2f, inner.Y + inner.Height * 0.2f, inner.X + inner.Width * 0.2f, inner.Bottom - inner.Height * 0.2f);
            }
        }
    }
}
