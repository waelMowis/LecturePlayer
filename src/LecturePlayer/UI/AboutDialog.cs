namespace LecturePlayer.UI;

/// <summary>«عن البرنامج»: اسم البرنامج والإصدار والمطوّر، بنفس تصميم الكروت.</summary>
internal static class AboutDialog
{
    public const string AppName = "مسار";
    public const string AppNameEn = "Masar";
    public const string DeveloperAr = "د. وائل عويس";
    public const string DeveloperEn = "Dr. Wael Owais";

    public static void Show(IWin32Window owner, string version, string technicalDetails)
    {
        using var form = new Form
        {
            Text = "عن البرنامج",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ShowIcon = false,
            RightToLeft = RightToLeft.Yes,
            BackColor = Theme.Window,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.75f),
            AutoScaleDimensions = new SizeF(96F, 96F),
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(420, 440),
            Padding = new Padding(14, 14, 14, 0),
            KeyPreview = true
        };
        form.HandleCreated += (_, _) => NativeMethods.UseDarkTitleBar(form);

        var card = new Card { Dock = DockStyle.Fill };
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Theme.Panel,
            Padding = new Padding(16, 18, 16, 12)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Label Line(string text, float size, Color color, FontStyle style = FontStyle.Regular, int top = 0)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = color,
                BackColor = Theme.Panel,
                Font = new Font("Segoe UI", size, style),
                Margin = new Padding(0, top, 0, 0),
                UseMnemonic = false
            };
            l.Height = TextRenderer.MeasureText(text, l.Font).Height + 4;
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.Controls.Add(l);
            return l;
        }

        // الأيقونة
        var icon = new PictureBox
        {
            Size = new Size(72, 72),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Panel,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 6)
        };
        try { if (Theme.AppIcon is { } ic) icon.Image = new Icon(ic, 256, 256).ToBitmap(); } catch { }
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.Controls.Add(icon);

        Line(AppName, 20f, Color.White, FontStyle.Bold);
        Line(AppNameEn + "  ·  الإصدار " + version, 9.5f, Theme.Muted);
        Line("فيديوهاتك مترتّبة في مسار واضح، وتكمّل من حيث وقفت", 9.5f, Theme.Text, top: 8);

        var sep = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Theme.Border, Margin = new Padding(30, 14, 30, 10) };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.Controls.Add(sep);

        Line("فكرة وتطوير", 9f, Theme.Muted);
        Line(DeveloperAr, 14f, Theme.Accent, FontStyle.Bold);
        Line(DeveloperEn, 10f, Theme.Text);
        Line($"© {DateTime.Now.Year} {DeveloperAr} — جميع الحقوق محفوظة", 8.5f, Theme.Muted, top: 12);

        card.Controls.Add(body);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Theme.Window,
            Padding = new Padding(0, 12, 0, 0),
            RightToLeft = RightToLeft.No
        };
        var ok = new PillButton("تمام", PillStyle.Primary) { Size = new Size(96, 34), DialogResult = DialogResult.OK };
        var details = new PillButton("تفاصيل تقنية", PillStyle.Ghost) { Size = new Size(120, 34), Margin = new Padding(3, 3, 8, 3) };
        details.Click += (_, _) => Dialogs.Info(form, technicalDetails, "تفاصيل تقنية");
        footer.Controls.Add(ok);
        footer.Controls.Add(details);

        form.Controls.Add(card);
        form.Controls.Add(footer);
        form.AcceptButton = ok;
        form.CancelButton = ok;
        form.ShowDialog(owner);
    }
}
