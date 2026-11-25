using System.Text.Json.Nodes;
using LecturePlayer.Data;

namespace LecturePlayer;

/// <summary>
/// فحص نوشن (على نوشن وهمي، من غير نت): إنشاء قاعدة لموضوع من مسار، والكورسات والمحاضرات والمربعات، والتحديث التلقائي،
/// والخطة من نوشن للشجرة والرئيسية ومن مسار لنوشن، والملاحظات في صفحة المحاضرة، والقوايم، والدمج بين جهازين.
/// </summary>
internal sealed partial class MainForm
{
    internal void RunNotionChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("notion: " + what); }
        bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(15); } return cond(); }

        var fake = new NotionFake { ThrottleAt = 7 };
        NotionClient.TestHandler = fake;
        string root = fake.AddRootPage("مسار");
        string oldToken = _settings.NotionTokenProtected;
        string? oldParent = _settings.NotionParentPage;
        _settings.NotionToken = fake.Token;
        _settings.NotionParentPage = "https://www.notion.so/ws/Masar-" + root.Replace("-", "");
        var stateBackup = _notionState;
        _notionState = new NotionState();

        var t = new Topic { Name = "Notion Topic" };
        var src = new Place { Name = "ROUTE", TopicId = t.Id };
        var c = new Course { Name = "Notion Course", TopicId = t.Id, PlaceId = src.Id, Goal = "Backend" };
        for (int i = 1; i <= 3; i++)
        {
            var g = new LectureGroup { Name = "Lecture " + i };
            for (int j = 1; j <= 3; j++) g.Videos.Add(new VideoItem { Path = $"C:\\notion-smoke\\{i}\\{j}.mp4", Title = $"Part {j}", DurationMs = 300_000 });
            c.Groups.Add(g);
        }
        _library.Topics.Add(t);
        _library.Places.Add(src);
        _library.Courses.Add(c);
        RebuildTree();
        try
        {
            Check(NotionReady, "ready with token + parent");
            using (var m = new ContextMenuStrip())
            {
                BuildNodeMenu(t, m.Items);
                var notion = m.Items.OfType<ToolStripMenuItem>().FirstOrDefault(x => x.Text!.StartsWith("📤  نوشن"));
                Check(notion != null && notion.DropDownItems.OfType<ToolStripMenuItem>().Any(x => x.Text!.Contains("أنشئ قاعدة نوشن")), "topic menu offers create");
            }

            // الإنشاء (من غير رسالة التأكيد)
            var create = RunNotionAsync("إنشاء", (s, ct) => s.CreateTopicAsync(t, root, ct), quiet: false);
            Check(Until(() => create.IsCompleted, 30_000) && create.Result, "create topic: " + string.Join(" | ", _notionLog.TakeLast(3)));
            Check(t.Notion != null && fake.Sources.Count == 2, "page + 2 databases");
            Check(c.NotionPageId != null && c.Groups.All(g => g.NotionPageId != null) && c.AllVideos.All(v => v.NotionBlockId != null), "rows + to-dos created");
            Check(fake.Pages[c.NotionPageId!].Props["المصدر"]?["select"]?["name"]?.GetValue<string>() == "ROUTE", "source column");
            Check(fake.Sources[t.Notion!.LecturesDsId].Schema["الحضور"] != null, "attendance column");
            Check(_tree.RowOf(_topicNodes[t])?.Meta == "📓", "topic shows notion mark");
            Check(_dirty, "library marked dirty (ids saved)");

            // القوايم بعد الربط
            using (var m = new ContextMenuStrip())
            {
                BuildNodeMenu(c.Groups[0], m.Items);
                var notion = m.Items.OfType<ToolStripMenuItem>().FirstOrDefault(x => x.Text!.StartsWith("📤  نوشن"));
                var subs = notion?.DropDownItems.OfType<ToolStripMenuItem>().Select(x => x.Text ?? "").ToList() ?? new();
                Check(subs.Contains("TASK") && subs.Contains("SEARCH") && subs.Contains("SUMMARY") && subs.Any(x => x.Contains(NotionSync.Present)) && subs.Any(x => x.Contains("الموعد")),
                    "lecture menu: tasks + attendance + date: " + string.Join(" | ", subs));
            }
            using (var m = new ContextMenuStrip())
            {
                BuildNodeMenu(t, m.Items);
                var subs = m.Items.OfType<ToolStripMenuItem>().First(x => x.Text!.StartsWith("📤  نوشن")).DropDownItems.OfType<ToolStripMenuItem>().Select(x => x.Text ?? "").ToList();
                Check(subs.Any(x => x.Contains("زامن دلوقتي")) && subs.Any(x => x.Contains("وزّع المواعيد")) && subs.Any(x => x.Contains("سجل المذاكرة")) && subs.Any(x => x.Contains("فك الربط")),
                    "topic menu when linked: " + string.Join(" | ", subs));
            }

            // التقدّم + كورس جديد ← التحديث
            var g1 = c.Groups[0];
            foreach (var v in g1.Videos) v.Watched = true;
            var c2 = new Course { Name = "Added later", TopicId = t.Id };
            c2.Groups.Add(new LectureGroup { Name = "Intro", Videos = { new VideoItem { Path = "C:\\notion-smoke\\x.mp4", Title = "x" } } });
            _library.Courses.Add(c2);
            var sync = RunNotionAsync("تحديث", async (s, ct) => await s.SyncTopicAsync(t, ct), quiet: true);
            Check(Until(() => sync.IsCompleted, 30_000) && sync.Result, "auto sync");
            Check(fake.Pages[g1.NotionPageId!].Props["الحالة"]?["select"]?["name"]?.GetValue<string>() == NotionSync.Done, "lecture done in notion");
            Check(g1.Videos.All(v => fake.Blocks[v.NotionBlockId!].Data["checked"]?.GetValue<bool>() == true), "to-dos checked");
            Check(c2.NotionPageId != null, "new course added in notion");

            // الخطة: من مسار ← نوشن
            EditPlan(g1, p => p.Task = true);
            Check(Until(() => fake.Pages[g1.NotionPageId!].Props["TASK"]?["checkbox"]?.GetValue<bool>() == true && !_notionBusy, 10_000), "TASK from masar");
            EditPlan(g1, p => p.Attendance = NotionSync.Present);
            Check(Until(() => fake.Pages[g1.NotionPageId!].Props["الحضور"]?["select"]?["name"]?.GetValue<string>() == NotionSync.Present && !_notionBusy, 10_000), "attendance from masar");

            // الخطة: من نوشن ← مسار (الشجرة والرئيسية)
            var g2 = c.Groups[1];
            fake.Pages[g2.NotionPageId!].Props["الموعد"] = new JsonObject { ["type"] = "date", ["date"] = new JsonObject { ["start"] = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd") } };
            fake.Pages[g2.NotionPageId!].Props["SEARCH"] = new JsonObject { ["type"] = "checkbox", ["checkbox"] = true };
            var pull = RunNotionAsync("سحب", async (s, ct) => await s.PullPlanAsync(t, ct), quiet: true);
            Check(Until(() => pull.IsCompleted, 20_000) && g2.Plan?.Date == DateTime.Today.AddDays(-1) && g2.Plan.Search, "plan pulled: " + g2.Plan?.Date);
            Check(_tree.RowOf(_groupNodes[g2])?.Meta.Contains("⚠") == true, "overdue date in tree: " + _tree.RowOf(_groupNodes[g2])?.Meta);
            Check(_tree.RowOf(_groupNodes[g1])?.Meta.Contains("✔") == true && _tree.RowOf(_groupNodes[g1])!.Meta.Contains("☑1/3"), "attendance + tasks in tree: " + _tree.RowOf(_groupNodes[g1])?.Meta);
            var due = NotionDue();
            Check(due.Any(x => x.Group == g2 && x.Overdue && x.Left.Contains("TASK")), "home plan lists overdue lecture");
            RefreshHomePlan();
            Check(_homePlan.Controls.OfType<Label>().Any(l => l.Text.StartsWith("📓") && l.Text.Contains("Lecture 2")), "home shows notion row");

            // الملاحظات ← صفحة المحاضرة
            g1.Videos[0].Notes = "ملاحظة مهمة";
            g1.Videos[1].Bookmarks.Add(new Bookmark { TimeMs = 65_000, Note = "هنا" });
            SendNotesToNotion(g1);
            Check(Until(() => g1.NotionNotesBlockId != null && !_notionBusy, 10_000), "notes sent");
            var box = fake.Blocks[g1.NotionNotesBlockId!];
            var inside = fake.ChildBlocks(box.Id).Select(b => NotionFake.Text(b.Data["rich_text"])).ToList();
            Check(box.Type == "callout" && inside.Any(x => x.Contains("ملاحظة مهمة")) && inside.Any(x => x.Contains("1:05") && x.Contains("هنا")), "notes box content: " + string.Join(" | ", inside));

            // توزيع المواعيد (من غير الشباك)
            var end = NotionSync.Schedule(c.Groups, new DateTime(2026, 12, 6), new[] { DayOfWeek.Sunday }, 10, onlyEmpty: false);
            Check(c.Groups.Where(g => !g.IsCompleted).All(g => g.Plan?.Date?.DayOfWeek == DayOfWeek.Sunday) && end != null, "schedule");

            // جهازين: الأرقام والخطة بتتنقل
            var remote = Storage.FromJson(Storage.Serialize(_library), Path.Combine(Path.GetTempPath(), "notion-merge.lpl"));
            var local2 = Storage.FromJson(Storage.Serialize(_library), Path.Combine(Path.GetTempPath(), "notion-merge.lpl"));
            var lt = local2.Topics.First(x => x.Id == t.Id);
            lt.Notion = null;
            foreach (var lc in local2.Courses.Where(x => x.TopicId == t.Id)) { lc.NotionPageId = null; foreach (var lg in lc.Groups) { lg.NotionPageId = null; lg.Plan = null; foreach (var v in lg.Videos) v.NotionBlockId = null; } }
            LibrarySync.Merge(local2, remote);
            var mc = local2.Courses.First(x => x.Id == c.Id);
            Check(local2.Topics.First(x => x.Id == t.Id).Notion?.PageId == t.Notion.PageId && mc.NotionPageId == c.NotionPageId
                  && mc.Groups[1].Plan?.Search == true && mc.AllVideos.All(v => v.NotionBlockId != null), "merge keeps notion ids + plan");

            // فك الربط ← القايمة ترجع «أنشئ»، ونوشن زي ما هو
            int pagesBefore = fake.Pages.Count(p => !p.Value.Trash);
            t.Notion = null;
            using (var m = new ContextMenuStrip())
            {
                BuildNodeMenu(t, m.Items);
                Check(m.Items.OfType<ToolStripMenuItem>().First(x => x.Text!.StartsWith("📤  نوشن")).DropDownItems.OfType<ToolStripMenuItem>().Any(x => x.Text!.Contains("أنشئ")), "unlinked menu");
            }
            Check(fake.Pages.Count(p => !p.Value.Trash) == pagesBefore, "nothing deleted in notion");

            // مفتاح غلط ← رسالة مفهومة (من غير رسايل في الفحص: quiet)
            _settings.NotionToken = "wrong-token-123";
            t.Notion = new NotionTopicLink { PageId = root, CoursesDsId = fake.Sources.Keys.First(), LecturesDsId = fake.Sources.Keys.Last() };
            var bad = RunNotionAsync("مفتاح غلط", (s, ct) => s.SyncTopicAsync(t, ct), quiet: true);
            Check(Until(() => bad.IsCompleted, 10_000) && !bad.Result && _notionLog.Last().Contains("مفتاح"), "bad token logged: " + _notionLog.Last());
            Console.WriteLine($"SMOKE notion: ok ({fake.Calls} api calls, throttle retried)");
        }
        finally
        {
            NotionClient.TestHandler = null;
            _settings.NotionTokenProtected = oldToken;
            _settings.NotionParentPage = oldParent;
            _notionState = stateBackup;
            _library.Courses.RemoveAll(x => x.TopicId == t.Id);
            _library.Places.Remove(src);
            _library.Topics.Remove(t);
            RebuildTree();
        }
    }
}
