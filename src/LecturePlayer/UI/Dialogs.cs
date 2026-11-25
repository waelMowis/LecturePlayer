namespace LecturePlayer.UI;

/// <summary>رسائل وحوار إدخال نص بالعربي (من اليمين للشمال).</summary>
internal static class Dialogs
{

    private enum Kind { Info, Error, Question }

    /// <summary>رسالة بنفس تصميم البرنامج (كارت بأيقونة وعنوان) بدل رسايل الويندوز البيضا.</summary>
    private static bool Message(IWin32Window owner, string text, string title, Kind kind)
    {
        using var kit = new FormKit(title, 480);
        var (glyph, color, heading) = kind switch
        {
            Kind.Error => ("\uE783", Theme.Danger, "حدثت مشكلة"),
            Kind.Question => ("\uE9CE", Theme.Accent, title),
            _ => ("\uE946", Theme.Accent, title)
        };
        var head = kit.Header(heading);
        head.Text = "      " + heading;
        var icon = new Label
        {
            Text = glyph, Font = Glyphs.Font(14f), ForeColor = color, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter, Size = new Size(28, 28)
        };
        icon.Location = new Point(head.Right - 28, head.Top);
        head.Parent!.Controls.Add(icon);
        icon.BringToFront();
        kit.Note(text, 24, Theme.Text);
        if (kind == Kind.Question) kit.Buttons("نعم", "لا");
        else kit.Buttons("حسنًا", null);
        return kit.Show(owner) == DialogResult.OK;
    }

    public static void Info(IWin32Window owner, string text, string title = "مسار") => Message(owner, text, title, Kind.Info);

    public static void Error(IWin32Window owner, string text, string title = "خطأ") => Message(owner, text, title, Kind.Error);

    public static bool Confirm(IWin32Window owner, string text, string title = "تأكيد") => Message(owner, text, title, Kind.Question);

    /// <summary>بيرجّع النص اللي اتكتب، أو null لو اتلغى.</summary>
    public static string? Input(IWin32Window owner, string title, string prompt, string initial = "")
    {
        using var kit = new FormKit(title, 460);
        kit.Note(prompt, 22, Theme.Text);
        var box = kit.Full(new TextBox { Text = initial }, 34);
        // اتجاه الكتابة حسب أول حرف: إنجليزي ← من الشمال (عشان «C#» متتكتبش «#C»)
        void AutoDirection()
        {
            char first = box.Text.FirstOrDefault(char.IsLetter);
            var dir = first != default && first < 0x0590 ? RightToLeft.No : RightToLeft.Yes;
            if (box.RightToLeft != dir) box.RightToLeft = dir;
        }
        box.TextChanged += (_, _) => AutoDirection();
        AutoDirection();
        kit.Buttons("موافق");
        kit.Form.Shown += (_, _) => { box.Focus(); box.SelectAll(); };
        if (kit.Show(owner) != DialogResult.OK) return null;
        string text = box.Text.Trim();
        return text.Length == 0 ? null : text;
    }

}
