using LecturePlayer.Data;

namespace LecturePlayer;

/// <summary>
/// فحص قوايم الشجرة: نفس الهيكل في كل نوع (تشغيل ← إضافة ← هات الجديد ← تعديل ← نقل وترتيب ← تقدّم ← مشاركة ← حذف)،
/// أول إضافة هي المستوى اللي تحت، الحذف دايمًا الآخر، مفيش تكرار، والقوايم واحدة في الوضع البسيط والمتقدم،
/// و«＋ إضافة» ماشي بترتيب الشجرة، والكورس المستورد من كليك يمين بيتحط في مكانه.
/// </summary>
internal sealed partial class MainForm
{
    /// <summary>ترتيب المجموعات حسب أيقونة أول الأمر.</summary>
    private static int MenuSection(string text) => text.TrimStart() switch
    {
        var t when t.StartsWith("▶") || t.StartsWith("⏮") || t.StartsWith("⚠") || t.StartsWith("📎") => 0,
        var t when t.StartsWith("➕") => 1,
        var t when t.StartsWith("🔄") => 2,
        var t when t.StartsWith("✏") || t.StartsWith("🎨") || t.StartsWith("➖") || t.StartsWith("📋") || t.StartsWith("🔗") || t.StartsWith("✂") => 3,
        var t when t.StartsWith("↔") || t.StartsWith("↕") => 4,
        var t when t.StartsWith("✔") || t.StartsWith("↺") || t.StartsWith("✨") => 5,
        var t when t.StartsWith("📤") || t.StartsWith("📝") || t.StartsWith("⬇") || t.StartsWith("📂") || t.StartsWith("🌐") => 6,
        var t when t.StartsWith("🗑") => 7,
        _ => -1
    };

    internal void RunMenuChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("menus: " + what); }

        var t = new Topic { Name = "Menu Topic" };
        var p = new Place { Name = "Menu Source", TopicId = t.Id };
        var c = new Course { Name = "Menu Course", TopicId = t.Id, PlaceId = p.Id };
        var g = new LectureGroup { Name = "Menu Lecture" };
        var v = new VideoItem { Path = "https://www.youtube.com/watch?v=menuvideo01", Title = "Menu Video" };
        g.Videos.Add(v);
        c.Groups.Add(g);
        _library.Topics.Add(t);
        _library.Places.Add(p);
        _library.Courses.Add(c);
        t.SeparatorBefore = true;
        t.SeparatorTitle = "قسم";
        bool advBefore = _settings.AdvancedMode == true;
        var cleanup = new List<Course>();
        try
        {
            RebuildTree();
            List<string> Texts(object tag)
            {
                using var m = new ContextMenuStrip();
                BuildNodeMenu(tag, m.Items);
                Check(m.Items.Count > 0 && m.Items[0] is not ToolStripSeparator && m.Items[^1] is not ToolStripSeparator, "no edge separators: " + tag);
                for (int i = 1; i < m.Items.Count; i++)
                    Check(!(m.Items[i] is ToolStripSeparator && m.Items[i - 1] is ToolStripSeparator), "no double separators: " + tag);
                return m.Items.OfType<ToolStripMenuItem>().Select(i => i.Text ?? "").ToList();
            }

            foreach (var (tag, name, firstAdd) in new (object, string, string)[]
            {
                (t, "topic", "مصدر جديد"), (p, "source", "مصدر فرعي"), (c, "course", "محاضرة"), (g, "lecture", "فيديوهات"), (v, "video", "")
            })
            {
                var items = Texts(tag);
                Check(items[0].StartsWith("▶"), $"{name}: play first ({items[0]})");
                Check(items[^1].StartsWith("🗑"), $"{name}: delete last ({items[^1]})");
                Check(items.Distinct().Count() == items.Count, $"{name}: no duplicates");
                Check(items.All(x => MenuSection(x) >= 0), $"{name}: every item has its group icon: " + string.Join(" | ", items.Where(x => MenuSection(x) < 0)));
                if (tag is not VideoItem) // الفيديو: «تعليم كمُشاهَد» جنب التشغيل عن قصد (أكتر حاجة بتتعمل)
                {
                    var sections = items.Select(MenuSection).ToList();
                    for (int i = 1; i < sections.Count; i++)
                        Check(sections[i] >= sections[i - 1], $"{name}: group order at «{items[i]}» after «{items[i - 1]}»");
                }
                if (firstAdd.Length > 0)
                    Check(items.First(x => x.StartsWith("➕")).Contains(firstAdd), $"{name}: first add is the level below ({items.First(x => x.StartsWith("➕"))})");
                // نفس القايمة في الوضعين
                _settings.AdvancedMode = !(_settings.AdvancedMode == true);
                Check(Texts(tag).SequenceEqual(items), $"{name}: same menu in simple and advanced mode");
                _settings.AdvancedMode = advBefore;
            }

            // «➕ كورس هنا» فيه كل المصادر
            using (var m = new ContextMenuStrip())
            {
                BuildNodeMenu(t, m.Items);
                var courseMenu = m.Items.OfType<ToolStripMenuItem>().First(i => i.Text!.StartsWith("➕  كورس"));
                var subs = courseMenu.DropDownItems.OfType<ToolStripMenuItem>().Select(i => i.Text).ToList();
                foreach (string src in new[] { "فولدر", "يوتيوب", "يوديمي", "مهارة تك", "IT Legend", "كورس فاضي" })
                    Check(subs.Any(s => s!.Contains(src)), "course sources include " + src);
            }

            // الفاصل
            var div = Texts(new TopicDivider(t));
            Check(div[0].StartsWith("✏") && div[^1].StartsWith("🗑"), "divider menu");

            // «＋ إضافة»: موضوع جديد الأول، وبعدها بترتيب الشجرة
            List<string> Add(object? sel)
            {
                using var m = new ContextMenuStrip();
                BuildAddMenu(sel, m.Items);
                return m.Items.OfType<ToolStripMenuItem>().Select(i => i.Text ?? "").ToList();
            }
            var none = Add(null);
            Check(none.Count == 2 && none[0].Contains("موضوع جديد") && none[1].Contains("كورس"), "add: nothing selected");
            var deep = Add(v);
            int Idx(string s) => deep.FindIndex(x => x.Contains(s));
            Check(deep[0].Contains("موضوع جديد") && Idx("مصدر جديد في") < Idx("مصدر فرعي في") && Idx("مصدر فرعي في") < Idx("كورس في «Menu Source»")
                  && Idx("كورس في") < Idx("محاضرة في «Menu Course»") && Idx("محاضرة في") < Idx("فيديوهات من الجهاز في «Menu Lecture»"),
                "add: hierarchy order: " + string.Join(" | ", deep));
            var onTopic = Add(t);
            Check(onTopic.Count == 3 && onTopic[1].Contains("مصدر جديد في «Menu Topic»") && onTopic[2].Contains("كورس في «Menu Topic»"), "add on topic: " + string.Join(" | ", onTopic));

            // المساحة الفاضية
            using (var m = new ContextMenuStrip())
            {
                BuildEmptyAreaMenu(m.Items);
                Check(m.Items[0].Text!.Contains("موضوع جديد"), "empty area: topic first");
            }

            // المستورد من كليك يمين بيتحط في مكانه: من مصدر ← جواه، ومن موضوع ← الموضوع ← المنصة ← المدرّس
            var inPlace = new Course { Name = "Imported A" };
            PlaceImportedCourse(inPlace, new ImportTarget(t, p), null, null, "Udemy", "Teacher X");
            Check(inPlace.PlaceId == p.Id && inPlace.TopicId == t.Id, "import target: source");
            var inTopic = new Course { Name = "Imported B" };
            var used = PlaceImportedCourse(inTopic, new ImportTarget(t, null), null, null, "Udemy", "Teacher X");
            var ud = _library.Places.FirstOrDefault(x => x.Id == _library.Places.First(y => y.Id == inTopic.PlaceId).ParentId);
            Check(used == t && ud is { Name: "Udemy" } && ud.TopicId == t.Id, "import target: topic → platform → instructor");
            ApplyItLegend(new ItLegendCourse("11111111-2222-3333-4444-555555555555", "ITL in source", "Ali", 0),
                new List<ItLegendModule> { new("m1", "Mod", 1, new List<ItLegendNode> { new("aaaaaaaa-0000-0000-0000-000000000001", "intro", 0, 1, 1000, false) }) },
                null, target: new ImportTarget(t, p));
            var itl = _library.Courses.FirstOrDefault(x => x.Name == "ITL in source");
            Check(itl != null && itl.PlaceId == p.Id, "it legend import from a source goes into it (no question)");
            if (itl != null) cleanup.Add(itl);

            // صور للقوايم (للمراجعة بالعين)
            foreach (var (tag, shot) in new (object, string)[] { (t, "menu-topic"), (c, "menu-course") })
            {
                ClearNodeMenu();
                BuildNodeMenu(tag, _nodeMenu.Items);
                _tree.SelectedNode = NodeOf(tag);
                var b = NodeOf(tag)?.Bounds ?? Rectangle.Empty;
                _nodeMenu.Show(_tree, new Point(b.Left + 20, b.Bottom));
                for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(15); }
                SaveSmokeShot(this, shot);
                _nodeMenu.Close();
            }
            Console.WriteLine("SMOKE menus: ok");
        }
        finally
        {
            _settings.AdvancedMode = advBefore;
            foreach (var x in cleanup) _library.Courses.Remove(x);
            _library.Courses.Remove(c);
            _library.Places.RemoveAll(x => x.TopicId == t.Id);
            _library.Topics.Remove(t);
            RebuildTree();
        }
    }
}
