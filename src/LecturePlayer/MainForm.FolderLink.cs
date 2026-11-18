using System.Diagnostics;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// «اربط فولدر كورس»: الفولدر (غالبًا على Google Drive) بيتقري لوحده كل دقيقتين في الخلفية،
/// وأي محاضرة أو فيديو جديد بيظهر في مكانه مع تنبيه.
/// </summary>
internal sealed partial class MainForm
{
    private bool _folderSyncRunning;

    private void LinkFolderDialog()
    {
        string? path = PickFolder(this, "اختار فولدر الكورس الكبير (اللي جواه الكورسات الصغيرة، وجوه كل واحد المحاضرات)");
        if (path == null) return;
        string key = FolderLink.Norm(path);
        if (_library.Topics.FirstOrDefault(t => t.SourceFolder != null && FolderLink.Norm(t.SourceFolder) == key) is { } existing)
        {
            Dialogs.Info(this, $"الفولدر ده مربوط بالفعل بموضوع «{existing.Name}».\nالجديد فيه بيظهر لوحده، أو كليك يمين على الموضوع ← تحديث من الفولدر دلوقتي.");
            return;
        }
        _ = LinkFolderAsync(path);
    }

    private async Task LinkFolderAsync(string path)
    {
        ShowStatus("بقرا الفولدر…", 60);
        Cursor = Cursors.WaitCursor;
        FolderLink.Snapshot? snap;
        try { snap = await Task.Run(() => FolderLink.Scan(path)); }
        finally { Cursor = Cursors.Default; }
        if (snap == null || snap.Courses.Count == 0)
        {
            Dialogs.Info(this, "ملقتش فيديوهات ولا ملفات في الفولدر ده.\nاتأكد إنك اخترت فولدر الكورس الكبير، ولو على Google Drive اتأكد إنه ظاهر على الجهاز.");
            return;
        }

        Checkpoint("ربط فولدر");
        var topic = new Topic { Name = new DirectoryInfo(path).Name, SourceFolder = path };
        topic.TouchInfo();
        _library.Topics.Add(topic);
        var r = FolderLink.Apply(_library, topic, snap, initial: true);
        Log.Info($"folder linked: {path} — courses {r.NewCourses}, lectures {r.NewGroups}, videos {r.NewVideos.Count}, moved {r.Moved}, attachments {r.NewAttachments}");
        MarkDirty();
        RebuildTree();
        SaveLibrary();
        if (NodeOf(topic) is { } node) { node.Expand(); _tree.SelectedNode = node; }
        QueueProbe(_library.CoursesOf(topic).SelectMany(c => c.AllVideos));

        string moved = r.Moved > 0
            ? $"\n\nلقيت {r.Moved} فيديو كنت ضايفهم قبل كده بإيدك — نقلتهم للترتيب الجديد بتقدّمك وعلاماتك وملاحظاتك."
            : "";
        Dialogs.Info(this,
            $"اتربط «{topic.Name}» ✓\n\n" +
            $"{r.NewCourses} كورس · {r.NewGroups} محاضرة · {r.NewVideos.Count + r.Moved} فيديو · {r.NewAttachments} ملف مرفق" + moved +
            "\n\nمن دلوقتي أي محاضرة أو فيديو جديد ينزل في الفولدر هيظهر هنا لوحده.");
    }

    /// <summary>بيتنده كل دقيقتين، وأول ما المكتبة تتفتح، ومن «تحديث من الفولدر».</summary>
    private async Task SyncLinkedFoldersAsync(Topic? only = null, bool manual = false)
    {
        if (_folderSyncRunning || _closing) return;
        var topics = _library.Topics.Where(t => !string.IsNullOrWhiteSpace(t.SourceFolder) && (only == null || t == only)).ToList();
        if (topics.Count == 0) return;
        _folderSyncRunning = true;
        try
        {
            var library = _library;
            var newOnes = new List<(Course Course, LectureGroup Group, VideoItem Video)>();
            bool changed = false;
            foreach (var topic in topics)
            {
                string root = topic.SourceFolder!;
                var snap = await Task.Run(() => FolderLink.Scan(root));
                if (_closing || library != _library) return; // المكتبة اتغيرت وإحنا بنقرا
                if (snap == null)
                {
                    if (manual) Dialogs.Info(this, $"الفولدر بتاع «{topic.Name}» مش موجود دلوقتي:\n{root}\n\nلو على Google Drive اتأكد إن الدرايف شغال.");
                    continue;
                }
                var r = FolderLink.Apply(_library, topic, snap, initial: false);
                if (!r.Changed) continue;
                changed = true;
                Log.Info($"folder sync {topic.Name}: courses +{r.NewCourses}, lectures +{r.NewGroups}, videos +{r.NewVideos.Count}, attachments +{r.NewAttachments}");
                foreach (var (c, g) in r.GroupsWithNew)
                    foreach (var v in g.Videos.Where(r.NewVideos.Contains)) newOnes.Add((c, g, v));
            }
            if (changed)
            {
                MarkDirty();
                RebuildTree();
                QueueProbe(newOnes.Select(x => x.Video));
            }
            if (newOnes.Count > 0)
            {
                var first = newOnes[0];
                string where = newOnes.Select(x => x.Course).Distinct().Count() == 1 ? $"«{first.Course.Name} › {first.Group.Name}»" : "أكتر من كورس";
                ShowBanner($"📥 نزل {newOnes.Count} فيديو جديد في {where}", BannerKind.Success, "▶  شغّل",
                    () => PlayVideo(first.Group, first.Video, 0), seconds: 0, key: "folder-new");
            }
            else if (manual) ShowStatus("الفولدر متحدّث — مفيش جديد ✓", 4);
        }
        catch (Exception ex) { Log.Error("folder sync", ex); }
        finally { _folderSyncRunning = false; }
    }

    private void AddFolderLinkMenuItems(Topic t, ToolStripItemCollection items)
    {
        if (string.IsNullOrWhiteSpace(t.SourceFolder)) return;
        items.Add(MenuItem("🔄  تحديث من الفولدر دلوقتي", () => _ = SyncLinkedFoldersAsync(t, manual: true)));
        items.Add(MenuItem("📂  فتح الفولدر", () =>
        {
            try { Process.Start(new ProcessStartInfo(t.SourceFolder!) { UseShellExecute = true }); }
            catch (Exception ex) { Dialogs.Error(this, "مقدرتش أفتح الفولدر:\n" + ex.Message); }
        }));
        items.Add(MenuItem("فك الربط بالفولدر (الكورسات بتفضل زي ما هي)", () =>
        {
            if (!Dialogs.Confirm(this, $"«{t.Name}» مش هيتابع الفولدر تاني، والجديد مش هيتضاف لوحده.\nالكورسات والتقدّم بيفضلوا زي ما هما. متأكد؟", "فك الربط")) return;
            t.SourceFolder = null;
            t.TouchInfo();
            MarkDirty();
            RebuildTree();
        }));
        items.Add(new ToolStripSeparator());
    }
}
