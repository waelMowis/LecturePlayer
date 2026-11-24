using System.Drawing.Drawing2D;

namespace LecturePlayer.UI;

/// <summary>
/// شريط تقديم/صوت مرسوم يدوي: تضغط في أي مكان يروح له على طول، وتقدر تسحب.
/// القيمة من 0 لـ 1.
/// </summary>
internal sealed class SeekBar : Control
{
    private double _value;
    private bool _dragging;
    private bool _hover;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
    public Color TrackColor { get; set; } = Theme.PanelLight;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
    public Color FillColor { get; set; } = Theme.Accent;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
    public Color MarkerColor { get; set; } = Theme.Folder;

    private IReadOnlyList<double> _markers = Array.Empty<double>();
    private (double Start, double End)? _loop;

    /// <summary>علامات مرجعية على الشريط (قيم من 0 لـ 1).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
    public IReadOnlyList<double> Markers
    {
        get => _markers;
        set { _markers = value ?? Array.Empty<double>(); Invalidate(); }
    }

    /// <summary>مدى تكرار A–B (null = مفيش).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
    public (double Start, double End)? LoopRange
    {
        get => _loop;
        set { _loop = value; Invalidate(); }
    }
    public bool IsDragging => _dragging;

    /// <summary>بيتنده أثناء السحب (للمعاينة).</summary>
    public event EventHandler<double>? Scrubbing;

    /// <summary>بيتنده لما تسيب الماوس (القيمة النهائية).</summary>
    public event EventHandler<double>? ValueCommitted;

    /// <summary>مكان الماوس فوق الشريط (-1 لما يخرج).</summary>
    public event EventHandler<double>? HoverChanged;

    public SeekBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Cursor = Cursors.Hand;
        Height = 24;
        BackColor = Theme.Panel;
        RightToLeft = RightToLeft.No;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden), System.ComponentModel.Browsable(false)]
    public double Value
    {
        get => _value;
        set
        {
            double v = Math.Clamp(value, 0, 1);
            if (_dragging || Math.Abs(v - _value) < 0.0001) return;
            _value = v;
            Invalidate();
        }
    }

    private int Pad => Math.Max(6, Height / 2);

    private double ValueFromX(int x)
    {
        int w = Width - 2 * Pad;
        return w <= 0 ? 0 : Math.Clamp((x - Pad) / (double)w, 0, 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool active = _hover || _dragging;
        float th = LogicalToDeviceUnits(active ? 6 : 4);
        float y = (Height - th) / 2f;
        float w = Width - 2 * Pad;
        if (w <= 0) return;

        // مدى التكرار A–B ورا الشريط
        if (_loop is { } loop && loop.End > loop.Start)
        {
            float lx = Pad + (float)(w * Math.Clamp(loop.Start, 0, 1));
            float lw = (float)(w * (Math.Clamp(loop.End, 0, 1) - Math.Clamp(loop.Start, 0, 1)));
            using var band = new SolidBrush(Color.FromArgb(70, FillColor));
            g.FillRectangle(band, lx, 2, Math.Max(2, lw), Height - 4);
        }

        using (var track = new SolidBrush(TrackColor))
        using (var path = Theme.RoundRect(new RectangleF(Pad, y, w, th), th / 2))
            g.FillPath(track, path);

        float fw = (float)(w * _value);
        if (fw > 0.5f)
        {
            using var fill = new SolidBrush(FillColor);
            using var path = Theme.RoundRect(new RectangleF(Pad, y, fw, th), th / 2);
            g.FillPath(fill, path);
        }

        // العلامات المرجعية
        if (_markers.Count > 0)
        {
            using var mark = new SolidBrush(MarkerColor);
            float mh = th + LogicalToDeviceUnits(8), mw = Math.Max(2f, LogicalToDeviceUnits(3));
            foreach (double m in _markers)
            {
                if (m < 0 || m > 1) continue;
                float mx = Pad + (float)(w * m);
                g.FillRectangle(mark, mx - mw / 2, (Height - mh) / 2f, mw, mh);
            }
        }

        if (active)
        {
            float r = th + LogicalToDeviceUnits(2);
            g.FillEllipse(Brushes.White, Pad + fw - r, Height / 2f - r, 2 * r, 2 * r);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        Capture = true;
        _value = ValueFromX(e.X);
        Invalidate();
        Scrubbing?.Invoke(this, _value);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_hover) { _hover = true; Invalidate(); }
        if (_dragging)
        {
            _value = ValueFromX(e.X);
            Invalidate();
            Scrubbing?.Invoke(this, _value);
        }
        HoverChanged?.Invoke(this, ValueFromX(e.X));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging || e.Button != MouseButtons.Left) return;
        _dragging = false;
        Capture = false;
        Invalidate();
        ValueCommitted?.Invoke(this, _value);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
        HoverChanged?.Invoke(this, -1);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        double v = Math.Clamp(_value + (e.Delta > 0 ? 0.05 : -0.05), 0, 1);
        _value = v;
        Invalidate();
        ValueCommitted?.Invoke(this, v);
    }
}

/// <summary>
/// الشجرة بتترسم بالكامل بالكود (OwnerDrawAll): صفوف بحواف مدورة، hover، شريط تحديد،
/// عدّاد على اليمين، وشريط تقدم رفيع تحت الكورس والمحاضرة.
/// ومن غير سكرول أفقي: الأسامي الطويلة بتتقص بـ «…» والاسم كامل في التلميح.
/// </summary>
internal sealed class LectureTreeView : TreeView
{
    private const int TVS_NOHSCROLL = 0x8000;
    private const int TVM_SETEXTENDEDSTYLE = 0x1100 + 44, TVS_EX_DOUBLEBUFFER = 0x0004;

    /// <summary>بيانات الصف اللي بتترسم جنب الاسم.</summary>
    /// <param name="Meta">نص باهت على اليمين (المدة).</param>
    /// <param name="Badge">عدّاد في «حبة» (مثلًا 2/46).</param>
    /// <param name="Progress">شريط تقدّم رفيع تحت الاسم (0..1)، أو سالب = من غيره.</param>
    /// <param name="LineAbove">خط فاصل فوق الصف (فاصل بين الموضوعات).</param>
    /// <param name="Divider">صف عنوان فاصل: العنوان وجنبه خط لآخر الشجرة.</param>
    public sealed record RowInfo(string Meta, string? Badge = null, bool Done = false, bool Accent = false, double Progress = -1, bool LineAbove = false, bool Divider = false);

    private readonly Dictionary<TreeNode, RowInfo> _rows = new();
    private TreeNode? _hot;
    private Font? _metaFont;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

    public LectureTreeView()
    {
        DrawMode = TreeViewDrawMode.OwnerDrawAll;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= TVS_NOHSCROLL;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { SendMessage(Handle, TVM_SETEXTENDEDSTYLE, (IntPtr)TVS_EX_DOUBLEBUFFER, (IntPtr)TVS_EX_DOUBLEBUFFER); } catch { }
    }

    public void SetRow(TreeNode node, RowInfo info)
    {
        _rows[node] = info;
        if (IsHandleCreated && node.TreeView == this) InvalidateRow(node);
    }

    internal RowInfo? RowOf(TreeNode node) => _rows.TryGetValue(node, out var r) ? r : null;

    public void ClearRows()
    {
        _rows.Clear();
        _hot = null;
    }

    private void InvalidateRow(TreeNode? n)
    {
        if (n == null || n.TreeView != this || !n.IsVisible) return;
        var b = n.Bounds;
        Invalidate(new Rectangle(0, b.Y, ClientSize.Width, ItemHeight));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var n = GetNodeAt(4, e.Y);
        if (n == _hot) return;
        var old = _hot;
        _hot = n;
        InvalidateRow(old);
        InvalidateRow(n);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        var old = _hot;
        _hot = null;
        InvalidateRow(old);
    }

    protected override void OnBeforeSelect(TreeViewCancelEventArgs e)
    {
        InvalidateRow(SelectedNode);
        base.OnBeforeSelect(e);
    }

    protected override void OnAfterSelect(TreeViewEventArgs e)
    {
        base.OnAfterSelect(e);
        InvalidateRow(e.Node);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _metaFont?.Dispose();
        _metaFont = null;
    }

    protected override void OnDrawNode(DrawTreeNodeEventArgs e)
    {
        var node = e.Node;
        if (node == null || e.Bounds.IsEmpty || e.Bounds.Height <= 0) return;
        var g = e.Graphics;
        _metaFont ??= new Font("Segoe UI", 8.5f);

        int w = ClientSize.Width;
        var row = new Rectangle(0, e.Bounds.Y, w, e.Bounds.Height);
        bool selected = node == SelectedNode;
        bool hot = node == _hot;

        // الخلفية: hover رمادي، والمحدد أزرق غامق بحد أزرق لو الشجرة عليها الفوكس
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, row);
        if (_rows.TryGetValue(node, out var dv) && dv.Divider) { DrawDivider(g, node, row, selected, hot); return; }
        if (selected || hot)
        {
            // صف بحواف مدورة جوه الكارت (مش شريط عرض الشاشة كلها)
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var pill = new RectangleF(3, row.Y + 1, w - 6, row.Height - 2);
            Color bgc = selected ? (Focused ? Theme.AccentDark : Theme.SelectionInactive) : Theme.PanelLight;
            Modern.FillRound(g, bgc, pill, 5);
            if (selected && Focused) Modern.DrawRound(g, Modern.Blend(Theme.Accent, BackColor, 0.25), pill, 5);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        }

        int x0 = node.Level * Indent;

        // فاصل بين الموضوعات: خط فوق الصف بعرض الشجرة
        if (_rows.TryGetValue(node, out var sepInfo) && sepInfo.LineAbove)
        {
            int lw = Math.Max(1, LogicalToDeviceUnits(2));
            using var sep = new SolidBrush(Modern.Blend(Theme.Muted, BackColor, 0.55));
            g.FillRectangle(sep, LogicalToDeviceUnits(8), row.Y, w - LogicalToDeviceUnits(16), lw);
        }

        // خطوط الإزاحة (indent guides)
        using (var guide = new Pen(Theme.IndentGuide))
        {
            for (int l = 1; l <= node.Level; l++)
            {
                int gx = (l - 1) * Indent + Indent / 2;
                g.DrawLine(guide, gx, row.Y, gx, row.Bottom);
            }
        }

        // سهم الفتح/القفل (في نفس مكان الأصلي عشان الضغط عليه يشتغل)
        if (node.Nodes.Count > 0)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float cx = x0 + Indent / 2f, cy = row.Y + row.Height / 2f, s = 3.4f;
            using var pen = new Pen(hot || selected ? Theme.Text : Theme.Muted, 1.4f)
            { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            if (node.IsExpanded)
                g.DrawLines(pen, new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) });
            else
                g.DrawLines(pen, new[] { new PointF(cx - s / 2, cy - s), new PointF(cx + s / 2, cy), new PointF(cx - s / 2, cy + s) });
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        }

        // الأيقونة
        int x = x0 + Indent;
        if (ImageList != null && !string.IsNullOrEmpty(node.ImageKey) && ImageList.Images.ContainsKey(node.ImageKey))
        {
            var img = ImageList.Images[node.ImageKey]!;
            g.DrawImage(img, x, row.Y + (row.Height - img.Height) / 2, img.Width, img.Height);
            x += img.Width + 6;
        }

        _rows.TryGetValue(node, out var info);
        int right = w - 10;

        // العدّاد في حبة
        if (info?.Badge is { Length: > 0 } badge)
        {
            var bs = TextRenderer.MeasureText(g, badge, _metaFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int bh = Math.Min(row.Height - 6, bs.Height + 4), bw = Math.Max(bh, bs.Width + 12);
            var br = new Rectangle(right - bw, row.Y + (row.Height - bh) / 2, bw, bh);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Modern.FillRound(g, info.Accent ? Theme.Accent : info.Done ? Modern.Blend(Theme.Success, Color.Black, 0.45) : Theme.Badge, br, bh / 2f);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            TextRenderer.DrawText(g, badge, _metaFont, br, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            right = br.Left - 8;
        }

        // المدة (باهتة)
        if (info != null && info.Meta.Length > 0)
        {
            var ms = TextRenderer.MeasureText(g, info.Meta, _metaFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            var mr = new Rectangle(right - ms.Width, row.Y, ms.Width, row.Height);
            TextRenderer.DrawText(g, info.Meta, _metaFont, mr, selected ? Theme.Text : Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            right = mr.Left - 10;
        }

        // الاسم
        var font = node.NodeFont ?? Font;
        var color = node.ForeColor.IsEmpty ? ForeColor : node.ForeColor;
        // على صف التحديد الأزرق: أي لون غير الأزرق/الأخضر (باهت أو لون مخصص) بيتقري أبيض
        if (selected && Focused && color != Theme.Accent && color != Theme.Success && color != Theme.Danger) color = Theme.Text;
        var tr = new Rectangle(x, row.Y, Math.Max(10, right - x), row.Height);
        TextRenderer.DrawText(g, node.Text, font, tr, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // شريط تقدّم رفيع تحت الاسم بدل أرقام الوقت (اتفرجت على قد إيه)
        if (info is { Progress: > 0 and < 1 })
        {
            int bw = Math.Min(tr.Width, LogicalToDeviceUnits(90));
            int by = row.Bottom - 4;
            using (var track = new SolidBrush(Theme.PanelLight)) g.FillRectangle(track, x, by, bw, 2);
            using (var fill = new SolidBrush(Theme.Accent)) g.FillRectangle(fill, x, by, Math.Max(2, (int)(bw * info.Progress)), 2);
        }
    }

    private Font? _dividerFont;

    /// <summary>صف عنوان الفاصل: العنوان باهت وعريض، وبعده خط لآخر الشجرة (والتحديد بحد خفيف بس).</summary>
    private void DrawDivider(Graphics g, TreeNode node, Rectangle row, bool selected, bool hot)
    {
        _dividerFont ??= new Font("Segoe UI", 8.5f, FontStyle.Bold);
        int w = ClientSize.Width, pad = LogicalToDeviceUnits(8);
        if (selected || hot)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var pill = new RectangleF(3, row.Y + 1, w - 6, row.Height - 2);
            Modern.DrawRound(g, selected && Focused ? Theme.Accent : Theme.PanelLight, pill, 5);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        }
        Color color = node.ForeColor.IsEmpty ? Theme.Muted : node.ForeColor;
        string text = node.Text;
        var ts = TextRenderer.MeasureText(g, text, _dividerFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        int maxText = Math.Max(20, w - pad * 4);
        var tr = new Rectangle(pad, row.Y, Math.Min(ts.Width + 2, maxText), row.Height);
        TextRenderer.DrawText(g, text, _dividerFont, tr, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        int lx = tr.Right + pad, lw = Math.Max(1, LogicalToDeviceUnits(2));
        if (lx < w - pad)
            using (var line = new SolidBrush(Modern.Blend(Theme.Muted, BackColor, 0.55)))
                g.FillRectangle(line, lx, row.Y + row.Height / 2, w - pad - lx, lw);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); InvalidateRow(SelectedNode); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); InvalidateRow(SelectedNode); }
}
