using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace LecturePlayer.UI;

/// <summary>أدوات الواجهة العصرية: زراير بحواف مدورة، صندوق بحث مدور، شريط تقدم رفيع.</summary>
internal static class Modern
{
    public const float Radius = 8f;

    /// <summary>
    /// نص بيبدأ بحروف إنجليزي جوه عنصر عربي: علامة LRM في الآخر عشان الرموز متتنقلش
    /// (من غيرها «C#» بتظهر «#C»).
    /// </summary>
    public static string Bidi(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        foreach (char ch in s)
        {
            if (char.IsLetter(ch))
                return ch < 0x0590 ? s + "‎" : s; // أول حرف لاتيني ← نثبّت الاتجاه
        }
        return s;
    }

    public static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        using var path = Theme.RoundRect(r, radius);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
    }

    public static void DrawRound(Graphics g, Color c, RectangleF r, float radius, float width = 1f)
    {
        using var path = Theme.RoundRect(r, radius);
        using var p = new Pen(c, width);
        g.DrawPath(p, path);
    }

    /// <summary>شريط تقدم رفيع بحواف مدورة.</summary>
    public static void DrawProgress(Graphics g, RectangleF r, double value, Color fill)
    {
        FillRound(g, Blend(Theme.Back, Theme.PanelLight, 0.9), r, r.Height / 2);
        if (value <= 0) return;
        float w = Math.Max(r.Height, (float)(r.Width * Math.Clamp(value, 0, 1)));
        FillRound(g, fill, new RectangleF(r.X, r.Y, w, r.Height), r.Height / 2);
    }

    public static void EnableDoubleBuffer(Control c) =>
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(c, true);
}

public enum PillStyle { Ghost, Primary, Subtle }

/// <summary>
/// أيقونات من خط الويندوز نفسه (Segoe Fluent Icons في ويندوز 11، وSegoe MDL2 Assets في ويندوز 10)
/// — من غير أي ملفات صور.
/// </summary>
internal static class Glyphs
{
    public const string Play = "\uE768", Pause = "\uE769", Stop = "\uE71A", Previous = "\uE892", Next = "\uE893",
        Rewind = "\uEB9E", Forward = "\uEB9D", Volume = "\uE767", Mute = "\uE74F", FullScreen = "\uE740",
        List = "\uE8FD", Library = "\uE8F1", Search = "\uE721", Settings = "\uE713", More = "\uE712",
        Add = "\uE710", Filter = "\uE71C", NewFolder = "\uE8F4", OpenFolder = "\uE838", Video = "\uE714",
        Link = "\uE71B", Code = "\uE943", Chat = "\uE8BD", History = "\uE81C", Chart = "\uE9D9",
        Timer = "\uE916", Collapse = "\uE70E", Expand = "\uE70D",
        MiniPlayer = "\uE73F";

    private static string? _family;

    public static string Family => _family ??= Exists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    private static bool Exists(string name)
    {
        try
        {
            using var f = new Font(name, 10f);
            return string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static Font Font(float size) => new(Family, size);
}

/// <summary>زرار أيقونة صغير (للشريط الجانبي والعناوين): hover رمادي، و«نشط» بشريط أزرق جنبه.</summary>
internal sealed class GlyphButton : Control
{
    private bool _hot, _down, _active;
    private string _glyph;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active { get => _active; set { _active = value; Invalidate(); } }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }

    /// <summary>الشريط الأزرق للنشط على أنهي جنب (للشريط الجانبي).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IndicatorOnRight { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowIndicator { get; set; }

    /// <summary>زرار أساسي (تشغيل): أزرق بحواف مدورة وأيقونة بيضا.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }

    /// <summary>النص = الأيقونة (عشان الكود القديم اللي بيغيّر Text يفضل شغال).</summary>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => _glyph; set => Glyph = value ?? ""; }

    public GlyphButton(string glyph, float size = 12f)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        _glyph = glyph;
        Font = Glyphs.Font(size);
        Cursor = Cursors.Hand;
        Size = new Size(28, 28);
        Margin = new Padding(1);
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        if (Primary)
        {
            Color pf = _down ? Modern.Blend(Theme.Accent, Color.Black, 0.2) : _hot ? Modern.Blend(Theme.Accent, Color.White, 0.12) : Theme.Accent;
            Modern.FillRound(g, pf, new RectangleF(1.5f, 1.5f, Width - 3, Height - 3), 7);
            TextRenderer.DrawText(g, _glyph, Font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            return;
        }
        if (!ShowIndicator && (_hot || _down))
            Modern.FillRound(g, _down ? Theme.Border : Theme.PanelLight, new RectangleF(2, 2, Width - 4, Height - 4), 6);
        if (ShowIndicator && _active)
        {
            using var b = new SolidBrush(Theme.Accent);
            g.FillRectangle(b, IndicatorOnRight ? Width - 2 : 0, 0, 2, Height);
        }
        Color c = !Enabled ? Theme.Border
            : ShowIndicator ? (_active || _hot ? Color.White : Theme.Muted)
            : _active ? Theme.Accent : _hot ? Color.White : Theme.Text;
        TextRenderer.DrawText(g, _glyph, Font, ClientRectangle, c,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

/// <summary>زرار بحواف مدورة وحالة hover/pressed (بديل الـ ToolStrip الكلاسيكي).</summary>
internal class PillButton : Control, IButtonControl
{
    /// <summary>عشان يشتغل كزرار «موافق/إلغاء» في الحوارات (Enter و Esc).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DialogResult DialogResult { get; set; }

    public void NotifyDefault(bool value) { }

    public void PerformClick()
    {
        if (Enabled) OnClick(EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is { } f) f.DialogResult = DialogResult;
    }

    private bool _hot, _down;
    private PillStyle _style = PillStyle.Ghost;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PillStyle Style { get => _style; set { _style = value; Invalidate(); } }

    public PillButton(string text, PillStyle style = PillStyle.Ghost)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        Text = text;
        _style = style;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        Font = new Font("Segoe UI", 9.5f, style == PillStyle.Primary ? FontStyle.Bold : FontStyle.Regular);
        Height = 32;
        Margin = new Padding(0, 0, 6, 6);
        AutoSizeToText();
    }

    public void AutoSizeToText()
    {
        var size = TextRenderer.MeasureText(Text, Font);
        Width = size.Width + 24;
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AutoSizeToText(); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fill, text = Theme.Text, border = Color.Empty;
        switch (_style)
        {
            case PillStyle.Primary:
                fill = _down ? Modern.Blend(Theme.Accent, Color.Black, 0.2) : _hot ? Modern.Blend(Theme.Accent, Color.White, 0.12) : Theme.Accent;
                text = Color.White;
                break;
            case PillStyle.Subtle:
                fill = _down ? Theme.Border : _hot ? Theme.PanelLight : Theme.Panel;
                text = _hot ? Theme.Text : Theme.Muted;
                break;
            default:
                fill = _down ? Theme.Border : _hot ? Modern.Blend(Theme.PanelLight, Color.White, 0.06) : Theme.PanelLight;
                border = Modern.Blend(Theme.PanelLight, Theme.Border, 0.6);
                break;
        }
        if (!Enabled) { fill = Theme.Panel; text = Theme.Muted; }
        Modern.FillRound(g, fill, r, Modern.Radius);
        if (border != Color.Empty) Modern.DrawRound(g, border, r, Modern.Radius);
        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

/// <summary>إطار مدور حوالين أداة (TextBox من غير حدود جواه) — بيعلّم بلون مميز وقت الكتابة.</summary>
internal sealed class RoundedHost : Panel
{
    private readonly Control _inner;
    private readonly bool _searchIcon;

    public RoundedHost(Control inner, bool searchIcon = false, int padding = 8)
    {
        _inner = inner;
        _searchIcon = searchIcon;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        Padding = new Padding(searchIcon ? 30 : padding, padding - 2, padding, padding - 2);
        inner.Dock = DockStyle.Fill;
        inner.BackColor = Theme.Input;
        inner.ForeColor = Theme.Text;
        if (inner is TextBox tb) tb.BorderStyle = BorderStyle.None;
        Controls.Add(inner);
        inner.GotFocus += (_, _) => Invalidate();
        inner.LostFocus += (_, _) => Invalidate();
        inner.Click += (_, _) => inner.Focus();
        Click += (_, _) => inner.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Modern.FillRound(g, Theme.Input, r, 4);
        Modern.DrawRound(g, _inner.Focused ? Theme.Accent : Theme.InputBorder, r, 4, 1f);

        if (_searchIcon)
        {
            // عدسة بحث مرسومة
            float cx = 14, cy = Height / 2f - 1, rad = 5f;
            using var pen = new Pen(_inner.Focused ? Theme.Accent : Theme.Muted, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
            g.DrawLine(pen, cx + rad * 0.7f, cy + rad * 0.7f, cx + rad * 1.5f, cy + rad * 1.5f);
        }
    }
}

/// <summary>شريط تقدم رفيع (للمكتبة كلها في العنوان).</summary>
internal sealed class ProgressLine : Control
{
    private double _value;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value { get => _value; set { _value = value; Invalidate(); } }

    public ProgressLine()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 6;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Panel);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Modern.DrawProgress(e.Graphics, new RectangleF(0, 1, Width - 1, Height - 2), _value, _value >= 1 ? Theme.Success : Theme.Accent);
    }
}

/// <summary>
/// كارت «عايم»: حواف مدورة، حد رفيع، خط إضاءة خفيف فوق، وظل ناعم تحت — فوق خلفية الشباك الأغمق.
/// المحتوى جواه بيبعد عن الحواف بالـ Padding عشان الدوران يبان نضيف.
/// Flat = بيلغي الشكل ده (ملء الشاشة والمشغل الصغير).
/// </summary>
internal sealed class Card : Panel
{
    private bool _flat;
    private Color _fill = Theme.Panel;
    public const int Radius = 9;
    private static readonly Padding Inset = new(5, 5, 5, 7);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Flat
    {
        get => _flat;
        set { _flat = value; Padding = value ? Padding.Empty : Inset; Invalidate(); }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill { get => _fill; set { _fill = value; Invalidate(); } }

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Padding = Inset;
        BackColor = Theme.Panel;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_flat) { using var fb = new SolidBrush(_fill); g.FillRectangle(fb, ClientRectangle); return; }
        g.Clear(Parent?.BackColor ?? Theme.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(1.5f, 1.5f, Width - 4f, Height - 5f);

        // ظل ناعم تحت الكارت
        for (int i = 3; i >= 1; i--)
            Modern.FillRound(g, Color.FromArgb(22 + (3 - i) * 14, 0, 0, 0), new RectangleF(r.X, r.Y + i, r.Width, r.Height), Radius + 1);

        Modern.FillRound(g, _fill, r, Radius);
        Modern.DrawRound(g, Theme.Border, r, Radius);

        // خط إضاءة فوق (بيدّي إحساس إن الكارت بارز)
        using var hl = new Pen(Color.FromArgb(26, 255, 255, 255));
        g.DrawLine(hl, r.X + Radius, r.Y + 1, r.Right - Radius, r.Y + 1);
    }
}
