using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>حاجات مشتركة بين منصات الكورسات (يوديمي، Moodle/مهارة تك): المكان في المكتبة، واختيار الكورسات.</summary>
/// <summary>المكان اللي الكورس المستورد هيتحط فيه لما الأمر يتدّاس من كليك يمين (موضوع أو مصدر) — من غير ما يسأل.</summary>
internal sealed record ImportTarget(Topic? Topic, Place? Place)
{
    public string Label => Place != null ? "«" + Place.Name + "»" : Topic != null ? "«" + Topic.Name + "»" : "";
}

internal sealed partial class MainForm
{
    /// <summary>
    /// بيحط كورس مستورد من منصة: لو اتطلب من مصدر ← جواه على طول؛ من موضوع ← الموضوع ← المنصة ← المدرّس؛
    /// وإلا الموضوع اللي اخترته في شباك الاختيار. بيرجّع الموضوع (عشان باقي الكورسات تروح نفس المكان).
    /// </summary>
    private Topic? PlaceImportedCourse(Course course, ImportTarget? target, Topic? topic, string? topicName, string platform, string? instructor)
    {
        if (target?.Place is { } p)
        {
            course.TopicId = p.TopicId;
            course.PlaceId = p.Id;
            course.Instructor = instructor;
            course.TouchInfo();
            return topic;
        }
        topic ??= target?.Topic
                  ?? _library.Topics.FirstOrDefault(t => string.Equals(t.Name, topicName, StringComparison.OrdinalIgnoreCase))
                  ?? NewTopic(string.IsNullOrWhiteSpace(topicName) ? "كورسات " + platform : topicName);
        PlacePlatformCourse(course, topic, platform, instructor);
        return topic;
    }

    private Topic NewTopic(string name)
    {
        var t = new Topic { Name = name };
        t.TouchInfo();
        _library.Topics.Add(t);
        return t;
    }

    /// <summary>الموضوع ← المنصة ← المدرّس ← الكورس.</summary>
    private void PlacePlatformCourse(Course course, Topic topic, string platform, string? instructor)
    {
        var place = _library.TopPlacesOf(topic).FirstOrDefault(p => string.Equals(p.Name, platform, StringComparison.OrdinalIgnoreCase));
        if (place == null)
        {
            place = new Place { Name = platform, TopicId = topic.Id };
            place.TouchInfo();
            _library.Places.Add(place);
        }
        Place target = place;
        if (!string.IsNullOrWhiteSpace(instructor))
        {
            var sub = _library.ChildrenOf(place).FirstOrDefault(p => string.Equals(p.Name, instructor.Trim(), StringComparison.OrdinalIgnoreCase));
            if (sub == null)
            {
                sub = new Place { Name = instructor.Trim(), TopicId = topic.Id, ParentId = place.Id };
                sub.TouchInfo();
                _library.Places.Add(sub);
            }
            target = sub;
        }
        course.TopicId = topic.Id;
        course.PlaceId = target.Id;
        course.Instructor = instructor;
        course.TouchInfo();
    }

    internal static Action<Form, HashSet<long>>? SmokeCoursePicker;

    /// <summary>
    /// اختيار الكورسات (دوسة = ☑/☐) والموضوع. exists = موجود في المكتبة (هيتحدّث بس، ومش متعلّم من الأول).
    /// </summary>
    private (HashSet<long> Ids, string Topic)? PickPlatformCourses(IWin32Window owner, string title, string platform,
        List<(long Id, string Title, string? Instructor, bool Exists)> all, string defaultTopic, string structureNote, ImportTarget? target = null)
    {
        using var kit = new FormKit($"{title} — اختار الكورسات", 680);
        kit.Header($"🎓  كورساتك على {title} ({all.Count})");
        var list = kit.Full(DarkList.Setup(new ListView { MultiSelect = false }), 320);
        list.Columns.Add("الكورس", 420);
        list.Columns.Add("المدرّس", 180);
        DarkList.ReverseColumns(list);
        var picked = new HashSet<long>();
        foreach (var c in all)
        {
            if (!c.Exists) picked.Add(c.Id);
            var item = new ListViewItem("") { Tag = c.Id, ForeColor = c.Exists ? Theme.Muted : Theme.Text };
            item.SubItems.Add(c.Instructor ?? "");
            list.Items.Add(item);
        }
        Label? count = null;
        void Paint()
        {
            for (int i = 0; i < all.Count; i++)
                list.Items[i].Text = (picked.Contains(all[i].Id) ? "☑  " : "☐  ") + all[i].Title + (all[i].Exists ? "   (موجود — هيتحدّث)" : "");
            if (count != null) count.Text = picked.Count == 0 ? "دوس على الكورس عشان تختاره" : $"هيتضاف/يتحدّث {picked.Count} كورس";
        }
        void Toggle(long id) { if (!picked.Remove(id)) picked.Add(id); Paint(); }
        list.MouseClick += (_, e) => { if (list.HitTest(e.Location).Item is { Tag: long id }) Toggle(id); };
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space || list.FocusedItem is not { Tag: long id }) return;
            Toggle(id);
            e.Handled = true;
        };
        count = kit.Note("", 24, Theme.Muted);

        ComboBox topicBox = new();
        if (target is { Topic: not null } or { Place: not null })
        {
            // الأمر اتدّاس من موضوع/مصدر: المكان معروف، مفيش سؤال
            kit.Note($"📁  هتتحط في {target.Label}" + (target.Place == null ? $" ← {platform} ← المدرّس" : "") + ". " + structureNote, 44, Theme.Muted);
        }
        else
        {
        kit.Header("📁  تتحط فين");
        topicBox = kit.Row("الموضوع:", new ComboBox { DropDownStyle = ComboBoxStyle.DropDown });
        foreach (var t in _library.Topics) topicBox.Items.Add(t.Name);
        // الموضوع اللي فيه المنصة دي قبل كده، أو موضوع جديد
        topicBox.Text = _library.Topics.FirstOrDefault(t => _library.TopPlacesOf(t).Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)))?.Name
                        ?? defaultTopic;
        kit.Note($"الترتيب: الموضوع ← {platform} ← المدرّس ← الكورس. {structureNote}\n" +
                 "الكورسات الموجودة بتتحدّث بس (ملاحظاتك ومكانك وعلاماتك بيفضلوا).", 44, Theme.Muted);
        }
        kit.Buttons("استيراد", "إلغاء",
            ("تحديد الكل", () => { foreach (var c in all) picked.Add(c.Id); Paint(); }),
            ("ولا حاجة", () => { picked.Clear(); Paint(); }));
        Paint();
        SmokeCoursePicker?.Invoke(kit.Form, picked);
        if (kit.Show(owner) != DialogResult.OK || picked.Count == 0) return null;
        string topic = string.IsNullOrWhiteSpace(topicBox.Text) ? defaultTopic : topicBox.Text.Trim();
        return (picked, topic);
    }
}
