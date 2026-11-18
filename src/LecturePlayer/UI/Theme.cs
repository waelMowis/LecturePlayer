using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LecturePlayer.UI;

/// <summary>ألوان الثيم الغامق + أيقونات الشجرة + رسم المنيوهات.</summary>
internal static class Theme
{
    // ثيم غامق بروح محررات الأكواد الحديثة: الجانب أغمق من منطقة العرض، تحديد أزرق، وحدود رفيعة
    // التصميم «كروت عايمة»: خلفية الشباك أغمق، وكل لوحة كارت أفتح منها بحواف مدورة وحد رفيع وظل خفيف
    public static readonly Color Window = Color.FromArgb(17, 17, 18);      // الفراغ بين الكروت والمنيو
    private static Icon? _appIcon;

    /// <summary>أيقونة البرنامج (من جوه الـ exe) لكل الشبابيك.</summary>
    public static Icon? AppIcon
    {
        get
        {
            if (_appIcon != null) return _appIcon;
            try
            {
                using var s = typeof(Theme).Assembly.GetManifestResourceStream("LecturePlayer.ico");
                if (s != null) _appIcon = new Icon(s);
            }
            catch { }
            return _appIcon;
        }
    }

    public static readonly Color Back = Color.FromArgb(27, 27, 29);        // محتوى غامق جوه الكروت
    public static readonly Color Panel = Color.FromArgb(32, 32, 34);       // لون الكارت
    public static readonly Color PanelLight = Color.FromArgb(48, 48, 52);  // hover
    public static readonly Color Border = Color.FromArgb(54, 54, 58);
    public static readonly Color Text = Color.FromArgb(204, 204, 204);
    public static readonly Color Muted = Color.FromArgb(157, 157, 157);
    public static readonly Color Accent = Color.FromArgb(0, 120, 212);
    public static readonly Color AccentDark = Color.FromArgb(14, 62, 104); // التحديد
    public static readonly Color Success = Color.FromArgb(137, 209, 133);
    public static readonly Color Folder = Color.FromArgb(220, 182, 122);
    public static readonly Color Danger = Color.FromArgb(241, 76, 76);
    public static readonly Color CourseColor = Color.FromArgb(177, 128, 215);
    /// <summary>لون «المصدر» في الشجرة.</summary>
    public static readonly Color PlaceColor = Color.FromArgb(86, 182, 194);

    public static readonly Color Input = Color.FromArgb(24, 24, 26);
    public static readonly Color InputBorder = Color.FromArgb(66, 66, 72);
    public static readonly Color SelectionInactive = Color.FromArgb(52, 52, 58);
    public static readonly Color Badge = Color.FromArgb(78, 78, 84);
    public static readonly Color IndentGuide = Color.FromArgb(58, 58, 62);
    public static readonly Color MenuBack = Color.FromArgb(38, 38, 40);
    public static readonly Color MenuBorder = Color.FromArgb(70, 70, 74);

    public static void StyleButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = PanelLight;
        b.FlatAppearance.MouseDownBackColor = AccentDark;
        b.BackColor = Panel;
        b.ForeColor = Text;
        b.UseVisualStyleBackColor = false;
        b.TabStop = false;
        b.Cursor = Cursors.Hand;
    }

    // ---------------- أيقونات الشجرة (مرسومة بالكود، مفيش ملفات صور) ----------------

    public static ImageList CreateTreeIcons(int size)
    {
        var il = new ImageList { ImageSize = new Size(size, size), ColorDepth = ColorDepth.Depth32Bit };
        il.Images.Add("topic", Icon(size, g => DrawStack(g, size, Accent)));
        il.Images.Add("topicDone", Icon(size, g => DrawStack(g, size, Success)));
        il.Images.Add("place", Icon(size, g => DrawBuilding(g, size, PlaceColor)));
        il.Images.Add("placeDone", Icon(size, g => DrawBuilding(g, size, Success)));
        il.Images.Add("course", Icon(size, g => DrawBook(g, size, CourseColor)));
        il.Images.Add("courseDone", Icon(size, g => DrawBook(g, size, Success)));
        il.Images.Add("group", Icon(size, g => DrawFolder(g, size, Folder)));
        il.Images.Add("groupDone", Icon(size, g => DrawFolder(g, size, Success)));
        il.Images.Add("video", Icon(size, g => DrawCircle(g, size, Muted, filled: false, inner: DrawTriangle)));
        il.Images.Add("partial", Icon(size, g => DrawPartial(g, size)));
        il.Images.Add("watched", Icon(size, g => DrawCircle(g, size, Success, filled: true, inner: DrawCheck)));
        il.Images.Add("playing", Icon(size, g => DrawCircle(g, size, Accent, filled: true, inner: DrawTriangle)));
        il.Images.Add("online", Icon(size, g => DrawGlobe(g, size)));
        il.Images.Add("missing", Icon(size, g => DrawCircle(g, size, Danger, filled: true, inner: DrawCross)));
        return il;
    }

    private static Bitmap Icon(int size, Action<Graphics> paint)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        paint(g);
        return bmp;
    }

    private static void DrawFolder(Graphics g, int s, Color c)
    {
        float p = s * 0.08f;
        using var back = new SolidBrush(ControlPaint.Dark(c, 0.1f));
        using var front = new SolidBrush(c);
        g.FillRectangle(back, p, s * 0.18f, s * 0.42f, s * 0.2f);
        using var path = RoundRect(new RectangleF(p, s * 0.28f, s - 2 * p, s * 0.58f), s * 0.1f);
        g.FillPath(front, path);
    }

    /// <summary>أيقونة طبقات (للموضوع): 3 كروت فوق بعض.</summary>
    private static void DrawStack(Graphics g, int s, Color c)
    {
        for (int i = 2; i >= 0; i--)
        {
            float off = i * s * 0.14f;
            var r = new RectangleF(s * 0.1f + off, s * 0.1f + (2 - i) * s * 0.14f, s * 0.62f, s * 0.5f);
            using var b = new SolidBrush(i == 0 ? c : ControlPaint.Dark(c, 0.15f + i * 0.12f));
            using var path = RoundRect(r, s * 0.08f);
            g.FillPath(b, path);
        }
    }

    /// <summary>أيقونة مبنى (للمكان): سقف مثلث وتحته جسم وباب.</summary>
    private static void DrawBuilding(Graphics g, int s, Color c)
    {
        using var roof = new SolidBrush(ControlPaint.Dark(c, 0.15f));
        g.FillPolygon(roof, new[] { new PointF(s * 0.5f, s * 0.06f), new PointF(s * 0.94f, s * 0.38f), new PointF(s * 0.06f, s * 0.38f) });
        using (var b = new SolidBrush(c))
        using (var path = RoundRect(new RectangleF(s * 0.16f, s * 0.38f, s * 0.68f, s * 0.54f), s * 0.06f))
            g.FillPath(b, path);
        using var door = new SolidBrush(Color.FromArgb(230, Color.White));
        g.FillRectangle(door, s * 0.42f, s * 0.6f, s * 0.16f, s * 0.32f);
    }

    /// <summary>أيقونة كتاب (للكورس).</summary>
    private static void DrawBook(Graphics g, int s, Color c)
    {
        var body = new RectangleF(s * 0.16f, s * 0.08f, s * 0.68f, s * 0.84f);
        using (var b = new SolidBrush(c))
        using (var path = RoundRect(body, s * 0.1f))
            g.FillPath(b, path);
        using var spine = new SolidBrush(ControlPaint.Dark(c, 0.25f));
        g.FillRectangle(spine, body.X, body.Y + s * 0.04f, s * 0.12f, body.Height - s * 0.08f);
        using var pen = new Pen(Color.FromArgb(220, Color.White), Math.Max(1f, s / 14f));
        float x1 = body.X + s * 0.24f, x2 = body.Right - s * 0.1f;
        g.DrawLine(pen, x1, body.Y + body.Height * 0.32f, x2, body.Y + body.Height * 0.32f);
        g.DrawLine(pen, x1, body.Y + body.Height * 0.5f, x2, body.Y + body.Height * 0.5f);
    }

    private static void DrawCircle(Graphics g, int s, Color c, bool filled, Action<Graphics, RectangleF, Color> inner)
    {
        var r = new RectangleF(s * 0.1f, s * 0.1f, s * 0.8f, s * 0.8f);
        if (filled)
        {
            using var b = new SolidBrush(c);
            g.FillEllipse(b, r);
            inner(g, r, Color.White);
        }
        else
        {
            using var pen = new Pen(c, Math.Max(1f, s / 12f));
            g.DrawEllipse(pen, r);
            inner(g, r, c);
        }
    }

    /// <summary>كرة أرضية (فيديو من النت).</summary>
    private static void DrawGlobe(Graphics g, int s)
    {
        var r = new RectangleF(s * 0.1f, s * 0.1f, s * 0.8f, s * 0.8f);
        using var pen = new Pen(Accent, Math.Max(1f, s / 13f));
        g.DrawEllipse(pen, r);
        g.DrawEllipse(pen, r.X + r.Width * 0.3f, r.Y, r.Width * 0.4f, r.Height);
        g.DrawLine(pen, r.X, r.Y + r.Height / 2f, r.Right, r.Y + r.Height / 2f);
    }

    private static void DrawPartial(Graphics g, int s)
    {
        var r = new RectangleF(s * 0.1f, s * 0.1f, s * 0.8f, s * 0.8f);
        using var pen = new Pen(Accent, Math.Max(1f, s / 12f));
        using var b = new SolidBrush(Accent);
        g.DrawEllipse(pen, r);
        g.FillPie(b, r.X, r.Y, r.Width, r.Height, -90, 180);
    }

    private static void DrawTriangle(Graphics g, RectangleF r, Color c)
    {
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, h = r.Height * 0.42f;
        using var b = new SolidBrush(c);
        g.FillPolygon(b, new[]
        {
            new PointF(cx - h * 0.4f, cy - h * 0.55f),
            new PointF(cx - h * 0.4f, cy + h * 0.55f),
            new PointF(cx + h * 0.6f, cy)
        });
    }

    private static void DrawCheck(Graphics g, RectangleF r, Color c)
    {
        using var pen = new Pen(c, Math.Max(1.5f, r.Width / 7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[]
        {
            new PointF(r.X + r.Width * 0.27f, r.Y + r.Height * 0.52f),
            new PointF(r.X + r.Width * 0.44f, r.Y + r.Height * 0.68f),
            new PointF(r.X + r.Width * 0.74f, r.Y + r.Height * 0.34f)
        });
    }

    private static void DrawCross(Graphics g, RectangleF r, Color c)
    {
        using var pen = new Pen(c, Math.Max(1.5f, r.Width / 7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float a = r.Width * 0.32f;
        g.DrawLine(pen, r.X + a, r.Y + a, r.Right - a, r.Bottom - a);
        g.DrawLine(pen, r.Right - a, r.Y + a, r.X + a, r.Bottom - a);
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>ألوان المنيوهات والتولبار الغامقة.</summary>
internal sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin => Theme.Panel;
    public override Color MenuStripGradientEnd => Theme.Panel;
    public override Color ToolStripDropDownBackground => Theme.MenuBack;
    public override Color ImageMarginGradientBegin => Theme.MenuBack;
    public override Color ImageMarginGradientMiddle => Theme.MenuBack;
    public override Color ImageMarginGradientEnd => Theme.MenuBack;
    public override Color MenuBorder => Theme.MenuBorder;
    public override Color MenuItemBorder => Theme.Accent;
    public override Color MenuItemSelected => Theme.Accent;   // العنصر المحدد في القايمة المنسدلة: أزرق
    public override Color MenuItemSelectedGradientBegin => Theme.PanelLight;
    public override Color MenuItemSelectedGradientEnd => Theme.PanelLight;
    public override Color MenuItemPressedGradientBegin => Theme.PanelLight;
    public override Color MenuItemPressedGradientMiddle => Theme.PanelLight;
    public override Color MenuItemPressedGradientEnd => Theme.PanelLight;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Border;
    public override Color ToolStripGradientBegin => Theme.Panel;
    public override Color ToolStripGradientMiddle => Theme.Panel;
    public override Color ToolStripGradientEnd => Theme.Panel;
    public override Color ToolStripBorder => Theme.Panel;
    public override Color StatusStripGradientBegin => Theme.Panel;
    public override Color StatusStripGradientEnd => Theme.Panel;
    public override Color ButtonSelectedHighlight => Theme.PanelLight;
    public override Color ButtonSelectedGradientBegin => Theme.PanelLight;
    public override Color ButtonSelectedGradientMiddle => Theme.PanelLight;
    public override Color ButtonSelectedGradientEnd => Theme.PanelLight;
    public override Color ButtonSelectedBorder => Theme.Border;
    public override Color ButtonPressedGradientBegin => Theme.AccentDark;
    public override Color ButtonPressedGradientMiddle => Theme.AccentDark;
    public override Color ButtonPressedGradientEnd => Theme.AccentDark;
    public override Color ButtonCheckedGradientBegin => Theme.AccentDark;
    public override Color ButtonCheckedGradientMiddle => Theme.AccentDark;
    public override Color ButtonCheckedGradientEnd => Theme.AccentDark;
    public override Color CheckBackground => Theme.AccentDark;
    public override Color CheckSelectedBackground => Theme.Accent;
    public override Color CheckPressedBackground => Theme.Accent;
}

internal sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColorTable()) => RoundedEdges = false;

    // من غير الحد الفاتح اللي الويندوز بيرسمه فوق شريط الحالة وحوالين الشرايط
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown) base.OnRenderToolStripBorder(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool dropSelected = e.Item.Selected && e.Item.IsOnDropDown && e.Item.Enabled;
        e.TextColor = !e.Item.Enabled ? Theme.Muted : dropSelected ? Color.White : Theme.Text;
        base.OnRenderItemText(e);
    }

    // الأزرار في المنيو الرئيسي: hover رمادي (مش أزرق) زي المحررات الحديثة
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.IsOnDropDown && (e.Item.Selected || e.Item.Pressed))
        {
            // hover مدور على عناصر الشريط اللي فوق
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.RoundRect(new RectangleF(1, 2, e.Item.Width - 2, e.Item.Height - 4), 5);
            using var b = new SolidBrush(e.Item.Pressed ? Theme.Border : Theme.PanelLight);
            e.Graphics.FillPath(b, path);
            return;
        }
        if (e.Item.IsOnDropDown && e.Item.Selected && e.Item.Enabled)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.RoundRect(new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), 4);
            using var b = new SolidBrush(Theme.Accent);
            e.Graphics.FillPath(b, path);
            return;
        }
        base.OnRenderMenuItemBackground(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Text;
        base.OnRenderArrow(e);
    }
}

internal static class NativeMethods
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>سكرول بار وأسهم غامقة للشجرة (ويندوز 10/11).</summary>
    public static void UseDarkExplorerTheme(Control c)
    {
        try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
    }

    /// <summary>شريط عنوان غامق للنافذة (ويندوز 10 20H1+ / 11).</summary>
    public static void UseDarkTitleBar(Form f)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(f.Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(f.Handle, 19, ref on, sizeof(int)); // إصدارات ويندوز 10 القديمة
        }
        catch { }
    }
}
