using System.Drawing.Drawing2D;

namespace LecturePlayer.UI;

/// <summary>
/// زرار كبير واضح (أيقونة + عنوان + سطر شرح) للشاشة الرئيسية: «ضيف فولدر فيديوهات»، «ضيف رابط يوتيوب»…
/// بيتعمل بحجمين: كبير (للمكتبة الفاضية) وصغير (صف إجراءات سريعة).
/// </summary>
internal sealed class ActionTile : Control
{
    private bool _hover, _down;
    public string Glyph { get; }
    public string Title { get; }
    public string Subtitle { get; }
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color IconColor { get; init; } = Theme.Accent;
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Compact { get; init; }

    public ActionTile(string glyph, string title, string subtitle)
    {
        Glyph = glyph;
        Title = title;
        Subtitle = subtitle;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Cursor = Cursors.Hand;
        BackColor = Theme.Panel;
        TabStop = true;
        AccessibleName = title;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    /// <summary>كليك يمين مايشغّلش الكارت (الويندوز بيبعت Click لأي زرار في الماوس).</summary>
    protected override void OnClick(EventArgs e)
    {
        if (e is MouseEventArgs { Button: not MouseButtons.Left }) return;
        base.OnClick(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float f = DeviceDpi / 96f;
        var r = new RectangleF(1, 1, Width - 3, Height - 3);

        Color fill = _down ? Theme.AccentDark : _hover ? Theme.PanelLight : Color.FromArgb(40, 40, 44);
        Modern.FillRound(g, fill, r, 10 * f);
        Modern.DrawRound(g, _hover || Focused ? Theme.Accent : Theme.Border, r, 10 * f, Focused ? 2f : 1f);

        // الأيقونة في دايرة على اليمين
        int icon = (int)((Compact ? 32 : 48) * f);
        int pad = (int)((Compact ? 12 : 18) * f);
        var circle = new Rectangle(Width - pad - icon, (Height - icon) / 2, icon, icon);
        using (var b = new SolidBrush(Color.FromArgb(_hover ? 70 : 45, IconColor))) g.FillEllipse(b, circle);
        using (var gf = Glyphs.Font((Compact ? 13f : 19f)))
            TextRenderer.DrawText(g, Glyph, gf, circle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // العنوان والشرح على شمال الأيقونة (الكلام من اليمين)
        int right = circle.Left - (int)(12 * f);
        int left = pad;
        using var titleFont = new Font("Segoe UI", Compact ? 10.5f : 13f, FontStyle.Bold);
        using var subFont = new Font("Segoe UI", Compact ? 8.5f : 9.5f);
        var flags = TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        int th = TextRenderer.MeasureText(g, "Ag", titleFont).Height;
        int sh = Subtitle.Length > 0 ? TextRenderer.MeasureText(g, "Ag", subFont).Height : 0;
        int total = th + (sh > 0 ? sh + (int)(2 * f) : 0);
        int y = (Height - total) / 2;
        TextRenderer.DrawText(g, Title, titleFont, new Rectangle(left, y, right - left, th), Color.White, flags);
        if (sh > 0)
            TextRenderer.DrawText(g, Subtitle, subFont, new Rectangle(left, y + th + (int)(2 * f), right - left, sh), Theme.Muted, flags);
    }
}
