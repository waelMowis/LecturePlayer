using System.Drawing.Drawing2D;

namespace LecturePlayer.UI;

/// <summary>
/// شريط التبويبات فوق مساحة العرض: تبويب لكل دورة أو كتاب أو سبورة أو عرض مفتوح،
/// وعلى الطرف زر «الجلسة» (جلسة مذاكرة أو حصة). الرسم يدوي من اليمين لليسار.
/// </summary>
internal sealed class TabStrip : Control
{
    public sealed record Item(string Glyph, string Title, string Tip, bool Active);

    private List<Item> _items = new();
    private readonly List<(Rectangle Tab, Rectangle Close)> _rects = new();
    private Rectangle _sessionRect, _moreRect;
    private int _hover = -1;
    private bool _hoverClose, _hoverSession, _hoverMore;
    private int _visible;
    private readonly ToolTip _tip = new() { InitialDelay = 500 };
    private string _lastTip = "";

    /// <summary>نص زر الجلسة (مثل «📚 بلا جلسة ▾»).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string SessionText { get; set; } = "";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool SessionActive { get; set; }

    public event Action<int>? TabClicked;
    public event Action<int>? TabClosed;
    public event Action<int, Point>? TabMenu;
    public event Action<Point>? SessionClicked;
    public event Action<Point>? MoreClicked;

    public TabStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.25f);
        Height = 34;
        TabStop = false;
    }

    public IReadOnlyList<Item> Items => _items;
    /// <summary>عدد التبويبات الظاهرة (الباقي في «▾ المزيد»).</summary>
    public int VisibleCount => _visible;

    public void SetItems(List<Item> items)
    {
        _items = items;
        _hover = -1;
        _hoverClose = false;
        // المستطيلات تُحسب الآن (لا عند الرسم): حركة الفأرة قد تسبق الرسم، وبمستطيلات قديمة يشير رقم التبويب إلى عنصر لم يعد موجودًا
        Layout2();
        Invalidate();
    }

    private int S(int v) => LogicalToDeviceUnits(v);

    private void Layout2()
    {
        _rects.Clear();
        int h = Height - S(6);
        int y = S(4);
        // زر الجلسة على اليسار
        int sw = Math.Min(S(260), TextRenderer.MeasureText(SessionText, Font).Width + S(24));
        _sessionRect = new Rectangle(S(4), y, sw, h);
        int right = Width - S(4);
        int left = _sessionRect.Right + S(10);
        int moreW = S(46);
        int avail = right - left;
        if (_items.Count == 0) { _visible = 0; _moreRect = Rectangle.Empty; return; }

        var natural = _items.Select(i => Math.Clamp(TextRenderer.MeasureText(i.Glyph + "  " + i.Title, Font).Width + S(48), S(110), S(240))).ToList();
        int min = S(96);
        int total = natural.Sum() + S(4) * _items.Count;
        List<int> widths = natural;
        if (total > avail)
        {
            int each = Math.Max(min, (avail - S(4) * _items.Count) / _items.Count);
            widths = natural.Select(w => Math.Min(w, each)).ToList();
        }
        // ما لا يتسع يذهب إلى «المزيد»
        _visible = 0;
        int x = right, need = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            int w = widths[i];
            bool last = i == _items.Count - 1;
            int reserve = last ? 0 : moreW + S(4);
            if (need + w + S(4) + reserve > avail && i > 0) break;
            need += w + S(4);
            _visible++;
        }
        for (int i = 0; i < _visible; i++)
        {
            int w = widths[i];
            var r = new Rectangle(x - w, y, w, h);
            var c = new Rectangle(r.Left + S(6), r.Top + (h - S(18)) / 2, S(18), S(18));
            _rects.Add((r, c));
            x -= w + S(4);
        }
        _moreRect = _visible < _items.Count ? new Rectangle(x - moreW, y, moreW, h) : Rectangle.Empty;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Layout2();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        // زر الجلسة
        using (var path = Round(_sessionRect, S(8)))
        using (var fill = new SolidBrush(_hoverSession ? Theme.PanelLight : SessionActive ? Theme.AccentDark : Theme.Panel))
        using (var pen = new Pen(SessionActive ? Theme.Accent : Theme.Border))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, SessionText, Font, _sessionRect, SessionActive ? Color.White : Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.RightToLeft);

        for (int i = 0; i < _rects.Count && i < _items.Count; i++)
        {
            var (r, c) = _rects[i];
            var it = _items[i];
            bool hot = i == _hover;
            using (var path = Round(r, S(8)))
            using (var fill = new SolidBrush(it.Active ? Theme.Panel : hot ? Theme.PanelLight : Theme.Back))
            {
                g.FillPath(fill, path);
                if (it.Active)
                {
                    using var pen = new Pen(Theme.Border);
                    g.DrawPath(pen, path);
                    using var acc = new SolidBrush(Theme.Accent);
                    g.FillRectangle(acc, r.Left + S(8), r.Bottom - S(3), r.Width - S(16), S(3));
                }
            }
            var textRect = new Rectangle(c.Right + S(4), r.Top, r.Right - c.Right - S(10), r.Height);
            TextRenderer.DrawText(g, it.Glyph + "  " + it.Title, Font, textRect, it.Active ? Color.White : Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.RightToLeft | TextFormatFlags.SingleLine);
            if (it.Active || hot)
            {
                bool hc = hot && _hoverClose;
                if (hc) using (var b = new SolidBrush(Theme.Danger)) g.FillEllipse(b, c);
                TextRenderer.DrawText(g, "✕", Font, c, hc ? Color.White : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        if (!_moreRect.IsEmpty)
        {
            using var path = Round(_moreRect, S(8));
            using var fill = new SolidBrush(_hoverMore ? Theme.PanelLight : Theme.Back);
            g.FillPath(fill, path);
            TextRenderer.DrawText(g, $"▾ {_items.Count - _visible}", Font, _moreRect, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private int HitTab(Point p)
    {
        for (int i = 0; i < _rects.Count && i < _items.Count; i++) if (_rects[i].Tab.Contains(p)) return i;
        return -1;
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Layout2(); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTab(e.Location);
        bool hc = h >= 0 && _rects[h].Close.Contains(e.Location);
        bool hs = _sessionRect.Contains(e.Location), hm = !_moreRect.IsEmpty && _moreRect.Contains(e.Location);
        if (h != _hover || hc != _hoverClose || hs != _hoverSession || hm != _hoverMore)
        {
            _hover = h; _hoverClose = hc; _hoverSession = hs; _hoverMore = hm;
            Invalidate();
        }
        string tip = h >= 0 && h < _items.Count ? (hc ? "إغلاق التبويب (Ctrl+W)" : _items[h].Tip) : hs ? "الجلسة: احفظ التبويبات المفتوحة باسم وارجع إليها لاحقًا" : "";
        if (tip != _lastTip) { _lastTip = tip; _tip.SetToolTip(this, tip); }
        Cursor = h >= 0 || hs || hm ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1; _hoverClose = _hoverSession = _hoverMore = false;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_sessionRect.Contains(e.Location)) { SessionClicked?.Invoke(PointToScreen(new Point(_sessionRect.Left, _sessionRect.Bottom))); return; }
        if (!_moreRect.IsEmpty && _moreRect.Contains(e.Location)) { MoreClicked?.Invoke(PointToScreen(new Point(_moreRect.Left, _moreRect.Bottom))); return; }
        int h = HitTab(e.Location);
        if (h < 0) return;
        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && _rects[h].Close.Contains(e.Location))) { TabClosed?.Invoke(h); return; }
        if (e.Button == MouseButtons.Right) { TabMenu?.Invoke(h, PointToScreen(e.Location)); return; }
        if (e.Button == MouseButtons.Left) TabClicked?.Invoke(h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
