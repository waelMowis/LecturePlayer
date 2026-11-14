using System.Drawing.Drawing2D;

namespace LecturePlayer.UI;

/// <summary>اللي المستخدم اختاره في آخر الجولة.</summary>
internal enum TourChoice { None, AddFolder, AddUrl, Live }

/// <summary>
/// جولة سريعة من 3 خطوات بتظهر أول مرة: إزاي تضيف فيديوهات، إزاي تذاكر بيها، وفين باقي الحاجات.
/// </summary>
internal sealed class TourForm : Form
{
    private sealed record Step(string Glyph, Color Color, string Title, string[] Lines);

    private static readonly Step[] Steps =
    {
        new(Glyphs.Library, Theme.Folder, "1.  ضيف فيديوهاتك",
            new[]
            {
                "«مسار» بيرتب فيديوهاتك في «مكتبة»: كل كورس جواه محاضرات، وكل محاضرة جواها فيديوهات.",
                "عندك فولدر فيه فيديوهات؟  اسحبه وارميه على البرنامج، أو دوس «ضيف فولدر فيديوهات».",
                "عندك رابط يوتيوب (فيديو أو قائمة تشغيل)؟  انسخه وادوس Ctrl+V، أو «ضيف رابط يوتيوب».",
                "المكتبة بتتحفظ لوحدها — مش محتاج تدوس حفظ.",
            }),
        new(Glyphs.Play, Theme.Accent, "2.  ذاكر وكمّل من مكانك",
            new[]
            {
                "دبل كليك على أي فيديو يشتغل. ولما ترجع تاني، البرنامج بيفتكر وقفت فين ويكمّل منه.",
                "لقيت جزء مهم؟  دوس «🔖 علامة هنا» تحت الفيديو (أو حرف B) عشان ترجعله بعدين.",
                "كليك يمين على الفيديو: علامة بملاحظة، صورة من الشاشة، وتكرار جزء معيّن.",
                "السرعة من تحت على الشمال، والمسافة (Space) بتوقف وتشغّل.",
            }),
        new(Glyphs.Radio, Theme.CourseColor, "3.  وفين باقي الحاجات؟",
            new[]
            {
                "الشريط اللي على أقصى اليمين: «مكتبتي» بتظهر وتخفي المكتبة، و«بث مباشر» للراديو والتليفزيون والحرمين.",
                "اسم المكتبة فوق (📚 … ▾): دوس عليه تعمل مكتبة جديدة أو تفتح واحدة تانية.",
                "البرنامج بادئ بالشكل البسيط. عايز أدوات أكتر (ذكاء اصطناعي، إحصائيات، مراجعة، تركيز)؟  مساعدة ← الوضع المتقدم.",
                "أي وقت تتلخبط: F1 بيفتح دليل الاستخدام، والجولة دي موجودة في منيو مساعدة.",
            }),
    };

    private int _index;
    private readonly Panel _page = new() { Dock = DockStyle.Fill, BackColor = Theme.Panel };
    private readonly PillButton _next = new("التالي  ←", PillStyle.Primary);
    private readonly PillButton _back = new("→  السابق", PillStyle.Ghost);
    private readonly PillButton _skip = new("تخطي الجولة", PillStyle.Subtle);
    private readonly FlowLayoutPanel _finish = new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
        FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Window, Visible = false
    };
    private readonly Panel _dots = new() { Height = 14, BackColor = Theme.Window };

    public TourChoice Choice { get; private set; }

    public static TourChoice Run(IWin32Window owner)
    {
        using var f = new TourForm();
        f.ShowDialog(owner);
        return f.Choice;
    }

    private TourForm()
    {
        Text = "جولة سريعة في «مسار»";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        RightToLeft = RightToLeft.Yes;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10f);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 430);
        KeyPreview = true;
        HandleCreated += (_, _) => NativeMethods.UseDarkTitleBar(this);

        var card = new Card { Dock = DockStyle.Fill };
        card.Controls.Add(_page);
        var cardHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 14, 14, 6), BackColor = Theme.Window };
        cardHost.Controls.Add(card);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Theme.Window, Padding = new Padding(14, 4, 14, 12) };
        _dots.Dock = DockStyle.Top;
        _dots.Paint += PaintDots;
        foreach (var b in new[] { _next, _back, _skip }) { b.Size = new Size(b == _skip ? 120 : 110, 34); }
        _next.Dock = DockStyle.Left;
        _back.Dock = DockStyle.Left;
        _skip.Dock = DockStyle.Right;
        _finish.Dock = DockStyle.Left;
        var buttons = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, RightToLeft = RightToLeft.No };
        var gap = new Panel { Dock = DockStyle.Left, Width = 8, BackColor = Theme.Window };
        buttons.Controls.Add(_skip);
        buttons.Controls.Add(gap);
        buttons.Controls.Add(_back);
        buttons.Controls.Add(_next);
        buttons.Controls.Add(_finish);
        footer.Controls.Add(buttons);
        footer.Controls.Add(_dots);

        PillButton Finish(string text, TourChoice choice, PillStyle style)
        {
            var b = new PillButton(text, style) { Size = new Size(TextRenderer.MeasureText(text, Font).Width + 34, 34), Margin = new Padding(0, 0, 8, 0) };
            b.Click += (_, _) => { Choice = choice; Close(); };
            _finish.Controls.Add(b);
            return b;
        }
        Finish("📁  ضيف فولدر فيديوهات", TourChoice.AddFolder, PillStyle.Primary);
        Finish("🔗  ضيف رابط يوتيوب", TourChoice.AddUrl, PillStyle.Ghost);
        Finish("خلاص، هتفرّج بنفسي", TourChoice.None, PillStyle.Subtle);

        Controls.Add(cardHost);
        Controls.Add(footer);

        _next.Click += (_, _) => Go(_index + 1);
        _back.Click += (_, _) => Go(_index - 1);
        _skip.Click += (_, _) => Close();
        Go(0);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: Close(); return true;
            case Keys.Left when _index < Steps.Length - 1:
            case Keys.Enter when _index < Steps.Length - 1:
                Go(_index + 1); return true;
            case Keys.Right when _index > 0: Go(_index - 1); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Go(int i)
    {
        _index = Math.Clamp(i, 0, Steps.Length - 1);
        bool last = _index == Steps.Length - 1;
        _back.Visible = _index > 0;
        _next.Visible = !last;
        _finish.Visible = last;
        _skip.Visible = !last;
        BuildPage(Steps[_index]);
        _dots.Invalidate();
        if (!last) _next.Focus();
    }

    private void BuildPage(Step step)
    {
        _page.SuspendLayout();
        foreach (Control c in _page.Controls.Cast<Control>().ToList()) { _page.Controls.Remove(c); c.Dispose(); }

        var head = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Panel };
        var icon = new Panel { Dock = DockStyle.Right, Width = 74, BackColor = Theme.Panel };
        icon.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(14, 12, 50, 50);
            using (var b = new SolidBrush(Modern.Blend(Theme.Panel, step.Color, 0.25))) g.FillEllipse(b, r);
            using var f = Glyphs.Font(20f);
            TextRenderer.DrawText(g, step.Glyph, f, r, step.Color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
        var title = new Label
        {
            Dock = DockStyle.Fill, Text = step.Title, ForeColor = Color.White, RightToLeft = RightToLeft.Yes,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 6, 0)
        };
        head.Controls.Add(title);
        head.Controls.Add(icon);

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Theme.Panel, Padding = new Padding(18, 4, 22, 8), RightToLeft = RightToLeft.Yes
        };
        int width = ClientSize.Width - 28 - 40 - 8;
        foreach (string line in step.Lines)
        {
            body.Controls.Add(new Label
            {
                Text = "•  " + line, ForeColor = Theme.Text, RightToLeft = RightToLeft.Yes, AutoSize = true,
                MaximumSize = new Size(width, 0), Margin = new Padding(0, 6, 0, 6), Font = new Font("Segoe UI", 10.5f)
            });
        }

        _page.Controls.Add(body);
        _page.Controls.Add(head);
        _page.ResumeLayout(true);
    }

    private void PaintDots(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        const int d = 8, gap = 8;
        int total = Steps.Length * d + (Steps.Length - 1) * gap;
        int x = (_dots.Width - total) / 2;
        for (int i = 0; i < Steps.Length; i++)
        {
            // من اليمين للشمال: الخطوة الأولى على اليمين
            int pos = x + (Steps.Length - 1 - i) * (d + gap);
            using var b = new SolidBrush(i == _index ? Theme.Accent : Theme.Border);
            g.FillEllipse(b, pos, 2, d, d);
        }
    }
}
