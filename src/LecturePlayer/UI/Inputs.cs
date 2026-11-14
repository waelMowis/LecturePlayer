using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace LecturePlayer.UI;

/// <summary>
/// أدوات إدخال غامقة بالكامل (بدل NumericUpDown و DateTimePicker و ComboBox و CheckBox الكلاسيكية
/// اللي أسهمها وخلفياتها بتفضل بيضا مهما غيّرت ألوانها).
/// </summary>
internal abstract class InputFrame : Control
{
    private bool _focusedInside;

    protected InputFrame()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.75f);
        Height = 32;
    }

    protected void TrackFocus(Control c)
    {
        c.GotFocus += (_, _) => { _focusedInside = true; Invalidate(); };
        c.LostFocus += (_, _) => { _focusedInside = false; Invalidate(); };
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        foreach (Control c in Controls) c.Enabled = Enabled;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Modern.FillRound(g, Enabled ? Theme.Input : Modern.Blend(Theme.Input, Theme.Panel, 0.5), r, 5);
        Modern.DrawRound(g, _focusedInside ? Theme.Accent : Theme.InputBorder, r, 5, _focusedInside ? 1.4f : 1f);
        PaintContent(g);
    }

    protected virtual void PaintContent(Graphics g) { }

    protected static TextBox InnerTextBox() => new()
    {
        BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = new Font("Segoe UI", 9.75f)
    };

    /// <summary>بيحط الـ TextBox في نص الارتفاع (TextBox من غير حدود ارتفاعه ثابت).</summary>
    protected void CenterVertically(Control inner, int left, int right)
    {
        int h = inner.PreferredSize.Height;
        inner.SetBounds(left, Math.Max(1, (Height - h) / 2), Math.Max(10, Width - left - right), h);
    }
}

/// <summary>خانة رقم بزرارين − و + (العجلة والأسهم بتغيّر القيمة كمان).</summary>
internal class StepBox : InputFrame
{
    protected readonly TextBox Box = InnerTextBox();
    private readonly GlyphButton _minus = new("", 9f), _plus = new(Glyphs.Add, 9f);

    public StepBox()
    {
        Box.RightToLeft = RightToLeft.No;
        Box.TextAlign = HorizontalAlignment.Center;
        foreach (var b in new[] { _minus, _plus }) { b.Size = new Size(28, 26); Controls.Add(b); }
        Controls.Add(Box);
        TrackFocus(Box);
        _minus.Click += (_, _) => { Commit(); Step(-1, ModifierKeys.HasFlag(Keys.Shift)); };
        _plus.Click += (_, _) => { Commit(); Step(+1, ModifierKeys.HasFlag(Keys.Shift)); };
        Box.Leave += (_, _) => Commit();
        Box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Up) { Commit(); Step(+1, e.Shift); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { Commit(); Step(-1, e.Shift); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter) Commit();
        };
        Box.MouseWheel += (_, e) => { Commit(); Step(Math.Sign(e.Delta), false); };
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int bh = Math.Min(26, Height - 6), y = (Height - bh) / 2;
        _minus.SetBounds(3, y, 28, bh);           // − على الشمال
        _plus.SetBounds(Width - 31, y, 28, bh);   // + على اليمين
        CenterVertically(Box, 34, 34);
    }

    protected virtual void Step(int dir, bool big) { }
    protected virtual void Commit() { }
    public event EventHandler? ValueChanged;
    protected void RaiseChanged() => ValueChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class NumberBox : StepBox
{
    private decimal _value, _min, _max = 100, _inc = 1;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Minimum { get => _min; set { _min = value; Value = _value; } }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Maximum { get => _max; set { _max = value; Value = _value; } }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Increment { get => _inc; set => _inc = value <= 0 ? 1 : value; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ThousandsSeparator { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, _min, Math.Max(_min, _max));
            bool changed = v != _value;
            _value = v;
            Box.Text = ThousandsSeparator ? v.ToString("N0", CultureInfo.InvariantCulture) : v.ToString("0", CultureInfo.InvariantCulture);
            if (changed) RaiseChanged();
        }
    }

    protected override void Step(int dir, bool big) => Value = _value + dir * _inc * (big ? 10 : 1);

    protected override void Commit()
    {
        string t = Box.Text.Replace(",", "").Replace("٬", "").Trim();
        t = new string(t.Select(ch => ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch).ToArray()); // أرقام عربي
        Value = decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : _value;
    }
}

/// <summary>وقت (ساعة:دقيقة) بخطوة 15 دقيقة.</summary>
internal sealed class TimeBox : StepBox
{
    private TimeOnly _value = new(21, 0);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TimeOnly Value
    {
        get => _value;
        set { _value = value; Box.Text = value.ToString("hh:mm tt", CultureInfo.GetCultureInfo("ar-EG")); RaiseChanged(); }
    }

    protected override void Step(int dir, bool big) => Value = _value.AddMinutes(dir * (big ? 60 : 15));

    protected override void Commit()
    {
        string t = Box.Text.Trim();
        if (TimeOnly.TryParse(t, CultureInfo.GetCultureInfo("ar-EG"), out var v) || TimeOnly.TryParse(t, CultureInfo.InvariantCulture, out v))
            Value = v;
        else Value = _value;
    }
}

/// <summary>تاريخ بزرارين يوم لورا/لقدام (Shift = أسبوع)، وتقدر تكتبه yyyy/MM/dd.</summary>
internal sealed class DateBox : StepBox
{
    private DateTime _value = DateTime.Today;
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("ar-EG");

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime Value
    {
        get => _value;
        set
        {
            _value = value.Date;
            Box.Text = $"{Ar.DateTimeFormat.GetDayName(_value.DayOfWeek)}  {_value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)}";
            RaiseChanged();
        }
    }

    protected override void Step(int dir, bool big) => Value = _value.AddDays(dir * (big ? 7 : 1));

    protected override void Commit()
    {
        string t = Box.Text.Trim();
        int i = t.IndexOfAny("0123456789".ToCharArray());
        if (i >= 0 && DateTime.TryParseExact(t[i..].Trim(), new[] { "yyyy/MM/dd", "yyyy-MM-dd", "yyyy/M/d" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            Value = d;
        else Value = _value;
    }
}

/// <summary>مفتاح تشغيل/إيقاف (بديل الـ CheckBox) والنص جنبه.</summary>
internal sealed class ToggleSwitch : Control
{
    private bool _checked, _hot;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Font = new Font("Segoe UI", 9.75f);
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
        Height = 32;
        TabStop = true;
    }

    protected override void OnClick(EventArgs e) { Focus(); Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) Checked = !Checked; base.OnKeyDown(e); }
    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        const int tw = 40, th = 22;
        // المفتاح على اليمين (عربي) والنص على شماله
        var track = new RectangleF(Width - tw - 2, (Height - th) / 2f, tw, th);
        Color on = Enabled ? Theme.Accent : Theme.Border;
        Modern.FillRound(g, _checked ? on : (_hot ? Theme.PanelLight : Theme.Input), track, th / 2f);
        if (!_checked) Modern.DrawRound(g, Theme.InputBorder, track, th / 2f);
        if (Focused) Modern.DrawRound(g, Modern.Blend(Theme.Accent, Color.White, 0.3), RectangleF.Inflate(track, 2, 2), th / 2f + 2);
        float d = th - 8, kx = _checked ? track.Left + 4 : track.Right - d - 4; // في العربي «شغال» = الدائرة على الشمال
        using (var knob = new SolidBrush(_checked ? Color.White : Theme.Muted)) g.FillEllipse(knob, kx, track.Top + 4, d, d);

        var textRect = new Rectangle(0, 0, (int)track.Left - 10, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>قايمة اختيار غامقة: بتفتح منيو بالاختيارات. Editable = تقدر تكتب قيمة من عندك.</summary>
internal sealed class SelectBox : InputFrame
{
    private readonly TextBox _box = InnerTextBox();
    private readonly ContextMenuStrip _menu = new() { Renderer = new DarkRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
    private int _index = -1;
    private bool _editable;

    public List<string> Items { get; } = new();
    public event EventHandler? SelectedIndexChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Editable { get => _editable; set { _editable = value; _box.ReadOnly = !value; _box.Cursor = value ? Cursors.IBeam : Cursors.Hand; } }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _index;
        set
        {
            _index = value >= 0 && value < Items.Count ? value : -1;
            _box.Text = _index >= 0 ? Items[_index] : "";
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [AllowNull]
    public override string Text
    {
        get => _box.Text;
        set { _box.Text = value ?? ""; _index = Items.IndexOf(_box.Text); }
    }

    public SelectBox()
    {
        _box.ReadOnly = true;
        _box.Cursor = Cursors.Hand;
        _box.BackColor = Theme.Input;
        Controls.Add(_box);
        TrackFocus(_box);
        Cursor = Cursors.Hand;
        _box.Click += (_, _) => { if (!_editable) Open(); };
        _box.KeyDown += (_, e) => { if (e.KeyCode is Keys.Down or Keys.F4 || (e.Alt && e.KeyCode == Keys.Down)) { Open(); e.Handled = true; } };
        Click += (_, _) => Open();
        _box.TextChanged += (_, _) => { if (_editable) _index = Items.IndexOf(_box.Text); };
    }

    protected override void OnRightToLeftChanged(EventArgs e)
    {
        base.OnRightToLeftChanged(e);
        _box.RightToLeft = RightToLeft;
        _menu.RightToLeft = RightToLeft;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        CenterVertically(_box, 30, 10); // السهم على الشمال
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.X < 30) Open();
    }

    protected override void PaintContent(Graphics g)
    {
        // سهم ▾ على الشمال
        float cx = 16, cy = Height / 2f, s = 3.5f;
        using var pen = new Pen(Enabled ? Theme.Text : Theme.Muted, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) });
    }

    private void Open()
    {
        if (!Enabled) return;
        _menu.Items.Clear();
        for (int i = 0; i < Items.Count; i++)
        {
            int idx = i;
            var item = new ToolStripMenuItem(Items[i]) { ForeColor = Theme.Text, Checked = i == _index || (_index < 0 && Items[i] == _box.Text) };
            item.Click += (_, _) => SelectedIndex = idx;
            _menu.Items.Add(item);
        }
        _menu.MinimumSize = new Size(Width, 0);
        _menu.Show(this, new Point(RightToLeft == RightToLeft.Yes ? Width : 0, Height + 2),
            RightToLeft == RightToLeft.Yes ? ToolStripDropDownDirection.BelowLeft : ToolStripDropDownDirection.BelowRight);
    }
}

/// <summary>زرار حبة بيتعلّم/يتشال (أيام المذاكرة مثلًا).</summary>
internal sealed class ChipToggle : PillButton
{
    private bool _checked;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; Style = value ? PillStyle.Primary : PillStyle.Ghost; CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    public event EventHandler? CheckedChanged;

    public ChipToggle(string text, bool isChecked) : base(text, isChecked ? PillStyle.Primary : PillStyle.Ghost)
    {
        _checked = isChecked;
        Height = 28;
        Font = new Font("Segoe UI", 9f);
        AutoSizeToText();
        Click += (_, _) => Checked = !Checked;
    }
}

/// <summary>اختيار في سؤال (بديل RadioButton): كارت مدور، والمختار حده أزرق. واحد بس يتختار في نفس الأب.</summary>
internal sealed class OptionCard : Control
{
    private bool _checked, _hot;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (value && Parent != null)
                foreach (var o in Parent.Controls.OfType<OptionCard>()) if (o != this) o.Checked = false;
            Invalidate();
        }
    }

    public OptionCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10.5f);
        Height = 44;
        Margin = new Padding(0, 0, 0, 6);
    }

    protected override void OnClick(EventArgs e) { if (Enabled) Checked = true; base.OnClick(e); }
    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        bool marked = ForeColor != Theme.Text;           // صح/غلط بعد التأكيد
        Color fill = _checked ? Modern.Blend(Theme.Input, Theme.Accent, 0.18) : _hot && Enabled ? Theme.PanelLight : Theme.Input;
        Color border = marked ? ForeColor : _checked ? Theme.Accent : Theme.InputBorder;
        Modern.FillRound(g, fill, r, 7);
        Modern.DrawRound(g, border, r, 7, _checked || marked ? 1.6f : 1f);

        // دايرة الاختيار على اليمين
        float d = 16, cx = Width - 14 - d, cy = (Height - d) / 2f;
        using (var p = new Pen(_checked ? Theme.Accent : Theme.Muted, 1.5f)) g.DrawEllipse(p, cx, cy, d, d);
        if (_checked) using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, cx + 4, cy + 4, d - 8, d - 8);

        var tr = new Rectangle(12, 0, (int)cx - 22, Height);
        TextRenderer.DrawText(g, Text, Font, tr, marked ? ForeColor : Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}

/// <summary>جداول غامقة: عناوين أعمدة مرسومة، صفوف أعلى، والأعمدة من اليمين للشمال.</summary>
internal static class DarkList
{
    public static ListView Setup(ListView lv)
    {
        lv.View = View.Details;
        lv.FullRowSelect = true;
        lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        lv.RightToLeftLayout = false;
        lv.RightToLeft = RightToLeft.No;
        lv.ShowItemToolTips = true;
        lv.BorderStyle = BorderStyle.None;
        lv.BackColor = Theme.Panel;
        lv.ForeColor = Theme.Text;
        lv.OwnerDraw = true;
        lv.SmallImageList = new ImageList { ImageSize = new Size(1, 26) };
        lv.DrawColumnHeader += (_, e) =>
        {
            using (var bg = new SolidBrush(Theme.Panel)) e.Graphics.FillRectangle(bg, e.Bounds);
            using (var line = new Pen(Theme.Border)) e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, new Font("Segoe UI", 9f, FontStyle.Bold), Rectangle.Inflate(e.Bounds, -6, 0), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis);
        };
        lv.DrawItem += (_, _) => { };
        lv.DrawSubItem += (_, e) =>
        {
            if (e.Item == null || e.SubItem == null) return;
            using (var bg = new SolidBrush(e.Item.Selected ? Theme.AccentDark : Theme.Panel)) e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lv.Font, Rectangle.Inflate(e.Bounds, -6, 0), e.Item.ForeColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        lv.HandleCreated += (_, _) => NativeMethods.UseDarkExplorerTheme(lv);
        return lv;
    }

    /// <summary>بعد إضافة الأعمدة: أول عمود يظهر على اليمين.</summary>
    public static void ReverseColumns(ListView lv)
    {
        int n = lv.Columns.Count;
        for (int i = 0; i < n; i++) lv.Columns[i].DisplayIndex = n - 1 - i;
    }
}
