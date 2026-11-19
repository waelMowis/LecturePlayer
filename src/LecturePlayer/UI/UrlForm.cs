using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>
/// «إضافة من يوتيوب»: الروابط، و«يتحط في أنهي كورس؟» — كورس موجود، أو كورس جديد باسم تكتبه
/// (مثلًا «سورة البقرة»). كده مفيش كورسات بأسامي تلقائية ملهاش معنى.
/// </summary>
internal static class UrlForm
{
    /// <param name="Course">كورس موجود اتختار (null = كورس جديد).</param>
    /// <param name="NewCourseName">اسم الكورس الجديد (null/فاضي = قوائم التشغيل كل واحدة كورس باسمها).</param>
    public sealed record Result(List<string> Urls, bool PlayFirst, Course? Course, string? NewCourseName);

    private sealed record Item(string Text, Course? Course)
    {
        public override string ToString() => Text;
    }

    /// <summary>للفحص التلقائي بس: الشاشة بتتفتح بجد، وتتملا بالرابط والاسم دول، وتدوس «إضافة» لوحدها.</summary>
    internal static (string Url, string? Name, int CourseIndex)? AutoFillForSmoke;

    /// <param name="where">وصف المكان (الموضوع/المصدر) اللي الكورس الجديد هيتعمل فيه.</param>
    /// <param name="courses">الكورسات اللي في نفس المكان (عشان تختار منها).</param>
    /// <param name="preselected">كورس متحدد في الشجرة (لو فيه).</param>
    /// <param name="lecture">لو المحدد محاضرة: الفيديوهات بتتحط فيها على طول ومفيش اختيار كورس.</param>
    public static Result? Show(IWin32Window owner, string initial, string where, IReadOnlyList<Course> courses,
        Course? preselected, LectureGroup? lecture)
    {

        using var kit = new FormKit("إضافة من يوتيوب", 600);
        kit.Header("الروابط");
        kit.Note("حط رابط فيديو أو قائمة تشغيل (أو كذا رابط، كل واحد في سطر):", 22, Theme.Text);
        var box = kit.Full(new TextBox
        {
            Text = initial, Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = false,
            RightToLeft = RightToLeft.No, Font = new Font("Consolas", 10f), AcceptsReturn = true
        }, 120);

        kit.Header("هيتحط فين؟");
        ComboBox? courseBox = null;
        TextBox? nameBox = null;
        Label? hint = null;
        if (lecture != null)
        {
            kit.Note($"الفيديوهات هتتضاف في محاضرة «{lecture.Name}».\n" +
                     "وقائمة التشغيل بتبقى محاضرة جديدة باسمها في نفس الكورس.", 40, Theme.Text);
        }
        else
        {
            courseBox = kit.Row("الكورس:", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 30);
            courseBox.Items.Add(new Item("＋  كورس جديد في " + where, null));
            foreach (var c in courses) courseBox.Items.Add(new Item("📘  " + c.Name, c));
            int pre = preselected == null ? -1 : courses.ToList().IndexOf(preselected);
            courseBox.SelectedIndex = pre >= 0 ? pre + 1 : 0;
            nameBox = kit.Row("اسم الكورس الجديد:", new TextBox(), 30);
            hint = kit.Note("", 40);
            void Update()
            {
                bool isNew = (courseBox.SelectedItem as Item)?.Course == null;
                nameBox.Enabled = isNew;
                hint.Text = isNew
                    ? "مثلًا «سورة البقرة». لو كل الروابط قوائم تشغيل وسبت الاسم فاضي، كل قائمة بتبقى كورس باسمها.\n" +
                      "الفيديوهات لوحدها بتتحط في محاضرة «الفيديوهات»، وقائمة التشغيل بتبقى محاضرة باسمها."
                    : "الفيديوهات لوحدها بتتحط في محاضرة «الفيديوهات» في الكورس ده، وقائمة التشغيل بتبقى محاضرة جديدة باسمها.";
            }
            courseBox.SelectedIndexChanged += (_, _) => Update();
            Update();
        }

        var play = kit.Full(new ToggleSwitch { Text = "شغّل أول فيديو بعد الإضافة", Checked = true }, 32);
        kit.Buttons("إضافة");
        kit.Form.Shown += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
            if (AutoFillForSmoke is { } auto)
            {
                box.Text = auto.Url;
                if (courseBox != null) courseBox.SelectedIndex = auto.CourseIndex;
                if (nameBox != null && auto.Name != null) nameBox.Text = auto.Name;
                kit.Form.DialogResult = DialogResult.OK;
                kit.Form.Close();
            }
        };
        kit.Form.FormClosing += (_, e) =>
        {
            if (kit.Form.DialogResult != DialogResult.OK) return;
            var urls = MediaFiles.ExtractUrls(box.Text);
            if (urls.Count == 0)
            {
                e.Cancel = true;
                Dialogs.Error(kit.Form, "مفيش رابط صحيح. الرابط لازم يبدأ بـ https://");
                return;
            }
            // كورس جديد فيه فيديوهات لوحدها (مش قوائم): لازم اسم، عشان ميبقاش «فيديوهات من يوتيوب» تاني
            if (courseBox != null && (courseBox.SelectedItem as Item)?.Course == null && string.IsNullOrWhiteSpace(nameBox!.Text)
                && urls.Any(u => !PlaylistSync.IsPlaylistUrl(u)))
            {
                e.Cancel = true;
                Dialogs.Error(kit.Form, "اكتب اسم الكورس الجديد (مثلًا: سورة البقرة)، أو اختار كورس موجود.");
                nameBox.Focus();
            }
        };
        if (kit.Show(owner) != DialogResult.OK) return null;
        var chosen = (courseBox?.SelectedItem as Item)?.Course;
        string? name = chosen == null && nameBox != null && !string.IsNullOrWhiteSpace(nameBox.Text) ? nameBox.Text.Trim() : null;
        return new Result(MediaFiles.ExtractUrls(box.Text), play.Checked, chosen, name);
    }
}
