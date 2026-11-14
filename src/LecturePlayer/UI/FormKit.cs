namespace LecturePlayer.UI;

/// <summary>
/// بيبني حوارات بنفس تصميم البرنامج: خلفية غامقة، وكل قسم (Header) كارت بحواف مدورة،
/// والسطور جواه: العنوان على اليمين والأداة جنبه. لو الحوار طويل بيعمل سكرول، والأزرار ثابتة تحت.
/// </summary>
internal sealed class FormKit : IDisposable
{
    public Form Form { get; }
    public PillButton? Ok { get; private set; }

    private readonly int _width;
    private const int Margin = 14, CardGap = 10, LabelWidth = 150, Gap = 8, Pad = 14;
    private readonly Panel _scroll = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Window };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Window };
    private Control _container;      // الكارت الحالي (أو منطقة السكرول لو مفيش كارت)
    private Card? _card;
    private int _y = Margin;         // داخل الكارت الحالي
    private int _outerY = Margin;    // في منطقة السكرول

    public FormKit(string title, int width = 520)
    {
        _width = width;
        Form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            RightToLeft = RightToLeft.Yes,
            RightToLeftLayout = false, // التموضع يدوي من اليمين (عشان الرسم المخصص ميتقلبش)
            BackColor = Theme.Window,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.75f),
            AutoScaleDimensions = new SizeF(96F, 96F),
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(width, 100),
            KeyPreview = true
        };
        Form.HandleCreated += (_, _) => NativeMethods.UseDarkTitleBar(Form);
        Form.ShowIcon = false; // الحوارات من غير أيقونة في العنوان
        Form.SuspendLayout();
        Form.Controls.Add(_scroll);
        Form.Controls.Add(_footer);
        _scroll.HandleCreated += (_, _) => NativeMethods.UseDarkExplorerTheme(_scroll);
        _container = _scroll;
    }

    /// <summary>عرض المنطقة اللي بنرص فيها (الكارت أو منطقة السكرول).</summary>
    private int InnerWidth => _card != null ? CardWidth - 2 * Pad - _card.Padding.Horizontal : _width - 2 * Margin - SystemInformation.VerticalScrollBarWidth;
    private int CardWidth => _width - 2 * Margin - SystemInformation.VerticalScrollBarWidth;
    private int Left0 => _card != null ? Pad + _card.Padding.Left : Margin;

    /// <summary>x من اليمين ← x من الشمال (التموضع من اليمين للشمال).</summary>
    private int FromRight(int rightOffset, int width) => Left0 + InnerWidth - rightOffset - width;

    public static T Style<T>(T c) where T : Control
    {
        switch (c)
        {
            case TextBox tb:
                tb.BackColor = Theme.Input; tb.ForeColor = Theme.Text; tb.BorderStyle = BorderStyle.None;
                break;
            case ComboBox cb:
                cb.BackColor = Theme.Input; cb.ForeColor = Theme.Text; cb.FlatStyle = FlatStyle.Flat;
                break;
            case NumericUpDown n:
                n.BackColor = Theme.Input; n.ForeColor = Theme.Text; n.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ListView lv:
                lv.BackColor = Theme.Back; lv.ForeColor = Theme.Text; lv.BorderStyle = BorderStyle.None;
                break;
            case CheckBox ch:
                ch.ForeColor = Theme.Text;
                break;
            case Button b:
                Theme.StyleButton(b);
                break;
        }
        return c;
    }

    /// <summary>TextBox بيتحط جوه إطار مدور (زي باقي الخانات).</summary>
    private Control Wrap(Control control) => control is TextBox tb ? new RoundedHost(tb, padding: 7) : control;

    /// <summary>سطر: عنوان + أداة.</summary>
    public T Row<T>(string label, T control, int height = 32) where T : Control
    {
        EnsureCard();
        var l = new Label
        {
            Text = label, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight, RightToLeft = RightToLeft.Yes,
            AutoEllipsis = true
        };
        l.SetBounds(FromRight(0, LabelWidth), _y, LabelWidth, Math.Min(height, 32));
        Style(control);
        var host = Wrap(control);
        int w = InnerWidth - LabelWidth - Gap;
        host.SetBounds(FromRight(LabelWidth + Gap, w), _y, w, height);
        _container.Controls.Add(l);
        _container.Controls.Add(host);
        _y += height + Gap;
        return control;
    }

    /// <summary>أداة بعرض القسم كله.</summary>
    public T Full<T>(T control, int height) where T : Control
    {
        EnsureCard();
        Style(control);
        var host = Wrap(control);
        host.SetBounds(Left0, _y, InnerWidth, height);
        _container.Controls.Add(host);
        _y += height + Gap;
        return control;
    }

    public Label Note(string text, int height = 22, Color? color = null)
    {
        EnsureCard();
        var l = new Label
        {
            Text = text, ForeColor = color ?? Theme.Muted, TextAlign = ContentAlignment.TopRight, RightToLeft = RightToLeft.Yes,
            Font = new Font("Segoe UI", 9f)
        };
        // لو النص طويل بيلف على أكتر من سطر
        int needed = TextRenderer.MeasureText(text, l.Font, new Size(InnerWidth, 1000), TextFormatFlags.WordBreak | TextFormatFlags.RightToLeft).Height + 4;
        height = Math.Max(height, needed);
        l.SetBounds(Left0, _y, InnerWidth, height);
        _container.Controls.Add(l);
        _y += height + Gap - 2;
        return l;
    }

    /// <summary>بيبدأ قسم جديد = كارت جديد بعنوان.</summary>
    public Label Header(string text)
    {
        CloseCard();
        _card = new Card { Fill = Theme.Panel, BackColor = Theme.Panel };
        _container = _card;
        _y = Pad + _card.Padding.Top - 4;
        var l = new Label
        {
            Text = text, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleRight, RightToLeft = RightToLeft.Yes
        };
        l.SetBounds(Left0, _y, InnerWidth, 28);
        _card.Controls.Add(l);
        var line = new Panel { BackColor = Theme.Border };
        line.SetBounds(Left0, _y + 30, InnerWidth, 1);
        _card.Controls.Add(line);
        _y += 40;
        return l;
    }

    /// <summary>لو مفيش Header قبل أول سطر: نعمل كارت من غير عنوان.</summary>
    private void EnsureCard()
    {
        if (_card != null) return;
        _card = new Card { Fill = Theme.Panel, BackColor = Theme.Panel };
        _container = _card;
        _y = Pad + _card.Padding.Top - 2;
    }

    private void CloseCard()
    {
        if (_card == null) return;
        int h = _y + Pad - Gap + _card.Padding.Bottom;
        _card.SetBounds(Margin, _outerY, CardWidth, h);
        _scroll.Controls.Add(_card);
        _outerY += h + CardGap;
        _card = null;
        _container = _scroll;
    }

    public void Space(int px) => _y += px;

    /// <summary>زرار موافق (أزرق) وإلغاء في الشريط اللي تحت، وأي زراير زيادة على الشمال.</summary>
    public void Buttons(string ok = "حفظ", string? cancel = "إلغاء", params (string Text, Action Click)[] extra)
    {
        CloseCard();
        var line = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };
        _footer.Controls.Add(line);

        int right = _width - Margin, y = 14;
        Ok = new PillButton(ok, PillStyle.Primary) { DialogResult = DialogResult.OK, Height = 34 };
        Ok.Width = Math.Max(110, Ok.Width);
        Ok.Location = new Point(right - Ok.Width, y);
        _footer.Controls.Add(Ok);
        Form.AcceptButton = Ok;
        right = Ok.Left - 8;
        if (cancel != null)
        {
            var c = new PillButton(cancel, PillStyle.Ghost) { DialogResult = DialogResult.Cancel, Height = 34 };
            c.Width = Math.Max(90, c.Width);
            c.Location = new Point(right - c.Width, y);
            _footer.Controls.Add(c);
            Form.CancelButton = c;
        }
        if (cancel == null) Form.CancelButton = Ok; // Esc يقفل رسالة «تمام»
        int left = Margin;
        foreach (var (text, click) in extra)
        {
            var b = new PillButton(text, PillStyle.Subtle) { Height = 34 };
            b.Location = new Point(left, y);
            b.Click += (_, _) => click();
            _footer.Controls.Add(b);
            left += b.Width + 8;
        }
    }

    public DialogResult Show(IWin32Window owner)
    {
        CloseCard();
        int content = _outerY + Margin - CardGap;
        int max = (int)(Screen.FromPoint(Cursor.Position).WorkingArea.Height * 0.85) - 40;
        Form.ClientSize = new Size(_width, Math.Min(content + _footer.Height, Math.Max(300, max)));
        Form.ResumeLayout(false);
        return Form.ShowDialog(owner);
    }

    public void Dispose() => Form.Dispose();
}
