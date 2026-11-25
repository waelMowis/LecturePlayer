using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>
/// «إضافة كورس من فولدر» — شاشة واحدة لكل الحالات:
/// الفولدر ← البرنامج يعرف هو كورس واحد ولا كذا كورس ← «هيتحط فين؟» (الموضوع والمصدر) ← معاينة ← متابعة الجديد.
/// </summary>
internal static class AddFolderForm
{
    /// <summary>اختيار في القايمة: موجود (Existing) أو جديد بالاسم (NewName) أو «من غير» (الاتنين null).</summary>
    public sealed record Choice<T>(T? Existing, string? NewName) where T : class
    {
        public static readonly Choice<T> None = new(null, null);
        public bool IsNone => Existing == null && NewName == null;
    }

    /// <param name="SubSourceName">لو مش null: الفولدر نفسه يبقى مصدر فرعي بالاسم ده جوه المصدر المختار (مثلًا المدرّس جوه المنصة).</param>
    public sealed record Result(string Path, bool SingleCourse, FolderLink.Snapshot Snapshot,
        Choice<Topic> Topic, Choice<Place> Source, bool Follow, string? SubSourceName = null);

    private sealed record Item(string Text, object? Tag)
    {
        public override string ToString() => Text;
    }

    private const string NewTag = "\u0001new";

    /// <summary>للفحص التلقائي بس: الشاشة تتفتح وتقرا الفولدر، وبعدين «إضافة» لوحدها.</summary>
    internal static bool AutoAcceptForSmoke;

    public static Result? Show(IWin32Window owner, CourseLibrary lib, string path, Topic? topic, Place? source)
    {
        using var kit = new FormKit("إضافة دورة من مجلد", 600);

        kit.Header("📁  المجلد");
        var pathBox = kit.Row("المجلد:", new TextBox { ReadOnly = true, RightToLeft = RightToLeft.No }, 32);
        var kind = kit.Row("هذا:", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 30);
        kind.Items.AddRange(new object[]
        {
            "مجلد فيه عدة دورات (كل مجلد بداخله دورة مستقلة)",
            "دورة واحدة (كل مجلد بداخله محاضرة)"
        });
        var preview = kit.Full(new TextBox
        {
            ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
            Text = "جارٍ قراءة المجلد…"
        }, 120);

        kit.Header("📚  أين سيوضع؟");
        var topicBox = kit.Row("الموضوع:", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 30);
        var sourceBox = kit.Row("المصدر:", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 30);
        var asSub = kit.Full(new ToggleSwitch { Text = "", Checked = false }, 32);
        kit.Note("الموضوع: التصنيف الكبير (مثلًا «البرمجة» أو «اللغات»).\n" +
                 "المصدر: المنصة أو الأكاديمية أو القناة أو المدرّس الذي جاءت منه الدورات. كلاهما اختياري.", 36);

        kit.Header("🔄  المتابعة");
        var follow = kit.Full(new ToggleSwitch { Text = "تابِع المجلد: أي محاضرة أو فيديو جديد يُضاف إليه يُضاف تلقائيًا", Checked = true }, 32);

        string currentPath = path;
        FolderLink.Snapshot? many = null, single = null;
        kit.Buttons("إضافة", "إلغاء", ("مجلد آخر…", () => _ = PickAnother()));
        kit.Ok!.Enabled = false;

        // ---------- الموضوع والمصدر ----------
        void FillTopics(object? select)
        {
            topicBox.Items.Clear();
            topicBox.Items.Add(new Item("(بدون موضوع)", null));
            foreach (var t in lib.Topics) topicBox.Items.Add(new Item("📚  " + t.Name, t));
            topicBox.Items.Add(new Item("＋  موضوع جديد…", NewTag));
            int i = Enumerable.Range(0, topicBox.Items.Count).FirstOrDefault(k => Equals((topicBox.Items[k] as Item)?.Tag, select));
            topicBox.SelectedIndex = i;
        }
        void FillSources(object? select)
        {
            sourceBox.Items.Clear();
            sourceBox.Items.Add(new Item("(بدون مصدر)", null));
            var tag = (topicBox.SelectedItem as Item)?.Tag;
            var places = tag is Topic t ? lib.OrderedPlacesOf(t) : tag is string ? Enumerable.Empty<(Place, int)>() : lib.OrderedPlacesOf(null);
            foreach (var (p, depth) in places) sourceBox.Items.Add(new Item((depth > 0 ? "        ↳ " : "🏫  ") + p.Name, p));
            if (select is string newName) sourceBox.Items.Add(new Item("🏫  " + newName + "  (جديد)", newName));
            sourceBox.Items.Add(new Item("＋  مصدر جديد…", NewTag));
            int i = Enumerable.Range(0, sourceBox.Items.Count).FirstOrDefault(k => Equals((sourceBox.Items[k] as Item)?.Tag, select));
            sourceBox.SelectedIndex = i;
        }
        object? lastTopic = topic, lastSource = source;
        topicBox.SelectedIndexChanged += (_, _) =>
        {
            if ((topicBox.SelectedItem as Item)?.Tag is string s && s == NewTag)
            {
                string? name = Dialogs.Input(kit.Form, "موضوع جديد", "اسم الموضوع (مثلًا: البرمجة، اللغات، التصميم):");
                if (name == null) { topicBox.SelectedIndex = IndexOf(topicBox, lastTopic); return; }
                int at = topicBox.Items.Count - 1;
                topicBox.Items.Insert(at, new Item("📚  " + name + "  (جديد)", name));
                topicBox.SelectedIndex = at;
                return;
            }
            lastTopic = (topicBox.SelectedItem as Item)?.Tag;
            FillSources(null);
        };
        sourceBox.SelectedIndexChanged += (_, _) =>
        {
            if ((sourceBox.SelectedItem as Item)?.Tag is string s && s == NewTag)
            {
                string? name = Dialogs.Input(kit.Form, "مصدر جديد", "اسم المصدر (المنصة أو الأكاديمية أو القناة أو المدرّس):");
                if (name == null) { sourceBox.SelectedIndex = IndexOf(sourceBox, lastSource); return; }
                int at = sourceBox.Items.Count - 1;
                sourceBox.Items.Insert(at, new Item("🏫  " + name + "  (جديد)", name));
                sourceBox.SelectedIndex = at;
                return;
            }
            lastSource = (sourceBox.SelectedItem as Item)?.Tag;
        };
        FillTopics(source != null ? lib.TopicOf(source) : topic);
        FillSources(source);

        // ---------- قراية الفولدر والمعاينة ----------
        void ShowPreview()
        {
            var snap = kind.SelectedIndex == 1 ? single : many;
            if (snap == null) { preview.Text = "جارٍ قراءة المجلد…"; kit.Ok!.Enabled = false; return; }
            if (snap.Courses.Count == 0)
            {
                preview.Text = "لم أجد فيديوهات في هذا المجلد.\r\nإن كان على Google Drive فتأكد أن المجلد ظاهر على الجهاز.";
                kit.Ok!.Enabled = false;
                return;
            }
            var lines = snap.Courses.Select(c =>
                $"📘  {c.Name}  —  {c.Sessions.Count} محاضرة · {c.Sessions.Sum(s => s.Videos.Count)} فيديو" +
                (c.Sessions.Sum(s => s.Files.Count) is > 0 and var f ? $" · {f} ملف مرفق" : ""));
            preview.Text = $"سيُضاف {snap.Courses.Count} دورة:\r\n" + string.Join("\r\n", lines);
            kit.Ok!.Enabled = true;
        }
        // «اعمل للفولدر ده مستوى باسمه»: لما الفولدر فيه كذا كورس (فولدر مدرّس مثلًا)
        bool subTouched = false, subAvailable = false;
        void UpdateSub()
        {
            string folderName = new DirectoryInfo(currentPath.TrimEnd('\\', '/')).Name;
            var src = (sourceBox.SelectedItem as Item)?.Tag;
            string? srcName = src is Place sp ? sp.Name : src is string sn && sn != NewTag ? sn : null;
            Place? srcPlace = src as Place;
            bool can = kind.SelectedIndex == 0;
            subAvailable = can;   // مش asSub.Visible: بعد ما الشاشة تتقفل Visible بيرجع false دايمًا
            asSub.Visible = can;
            asSub.Text = srcName == null
                ? $"أنشئ لهذا المجلد مصدرًا باسمه «{folderName}» وضع الدورات بداخله"
                : srcPlace != null && lib.ParentOf(srcPlace) is { } parent
                    ? $"أنشئ لهذا المجلد مصدرًا فرعيًا باسمه «{folderName}» داخل «{parent.Name}»"
                    : $"أنشئ لهذا المجلد مصدرًا فرعيًا باسمه «{folderName}» داخل «{srcName}»";
            // بيتعلّم لوحده لما الفولدر غير المصدر المختار (فولدر المدرّس جوه المنصة)، إلا لو المستخدم غيّره بإيده
            if (!subTouched) asSub.Checked = can && srcName != null && !string.Equals(folderName, srcName, StringComparison.CurrentCultureIgnoreCase)
                                             && !(srcPlace != null && lib.ParentOf(srcPlace) != null);
            asSub.Invalidate();
        }
        asSub.Click += (_, _) => subTouched = true;
        kind.SelectedIndexChanged += (_, _) => { ShowPreview(); UpdateSub(); };
        sourceBox.SelectedIndexChanged += (_, _) => UpdateSub();

        async Task Load(string p)
        {
            currentPath = p;
            pathBox.Text = p;
            many = single = null;
            kind.Enabled = false;
            ShowPreview();
            var (m, s, looksMany) = await Task.Run(() => (FolderLink.Scan(p), FolderLink.ScanSingle(p), FolderLink.LooksLikeManyCourses(p)));
            if (kit.Form.IsDisposed || currentPath != p) return;
            many = m; single = s;
            kind.Enabled = true;
            kind.SelectedIndex = looksMany ? 0 : 1;
            ShowPreview();
        }
        async Task PickAnother()
        {
            using var dlg = new FolderBrowserDialog { Description = "اختر مجلد الدورة", UseDescriptionForTitle = true, ShowNewFolderButton = false, SelectedPath = currentPath };
            if (dlg.ShowDialog(kit.Form) == DialogResult.OK) await Load(dlg.SelectedPath);
        }
        kit.Form.Shown += async (_, _) =>
        {
            await Load(path);
            if (AutoAcceptForSmoke && !kit.Form.IsDisposed) { kit.Form.DialogResult = DialogResult.OK; kit.Form.Close(); }
        };

        if (kit.Show(owner) != DialogResult.OK) return null;
        bool isSingle = kind.SelectedIndex == 1;
        var chosen = isSingle ? single : many;
        if (chosen == null) return null;
        string? subName = !isSingle && subAvailable && asSub.Checked ? new DirectoryInfo(currentPath.TrimEnd('\\', '/')).Name : null;
        return new Result(currentPath, isSingle, chosen, ToChoice<Topic>(topicBox), ToChoice<Place>(sourceBox), follow.Checked, subName);
    }

    private static int IndexOf(ComboBox box, object? tag) =>
        Math.Max(0, Enumerable.Range(0, box.Items.Count).FirstOrDefault(k => Equals((box.Items[k] as Item)?.Tag, tag)));

    private static Choice<T> ToChoice<T>(ComboBox box) where T : class => (box.SelectedItem as Item)?.Tag switch
    {
        T existing => new Choice<T>(existing, null),
        string name when name != NewTag => new Choice<T>(null, name),
        _ => Choice<T>.None
    };
}
