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
        // لو المحدد في الشجرة «مكان» (ROUTE مثلًا) ← الفولدر يتربط جواه
        if (_tree.SelectedNode?.Tag is Place place) { LinkFolderIntoPlaceDialog(place); return; }
        string? path = PickFolder(this, "اختار فولدر الكورس الكبير (اللي جواه الكورسات الصغيرة، وجوه كل واحد المحاضرات)");
        if (path == null || AlreadyLinked(path)) return;
        _ = LinkFolderAsync(path);
    }

    /// <summary>الفولدر مربوط بالفعل بموضوع أو مكان؟ (وبيقول للمستخدم فين)</summary>
    private bool AlreadyLinked(string path)
    {
        string key = FolderLink.Norm(path);
        string? owner =
            _library.Topics.FirstOrDefault(t => t.SourceFolder != null && FolderLink.Norm(t.SourceFolder) == key) is { } t ? $"بموضوع «{t.Name}»"
            : _library.Places.FirstOrDefault(p => p.SourceFolders.Any(f => FolderLink.Norm(f) == key)) is { } p ? $"بالمكان «{p.Name}»"
            : null;
        if (owner == null) return false;
        Dialogs.Info(this, $"الفولدر ده مربوط بالفعل {owner}.\nالجديد فيه بيظهر لوحده، أو كليك يمين عليه ← تحديث من الفولدر دلوقتي.");
        return true;
    }

    private void LinkFolderIntoPlaceDialog(Place place)
    {
        string? path = PickFolder(this, $"اختار الفولدر اللي جواه كورسات «{place.Name}» (مثلًا فولدر المحاضر على الدرايف)");
        if (path == null || AlreadyLinked(path)) return;
        _ = LinkFolderIntoPlaceAsync(place, path);
    }

    private async Task<FolderLink.Snapshot?> ScanForLinkAsync(string path)
    {
        ShowStatus("بقرا الفولدر…", 60);
        Cursor = Cursors.WaitCursor;
        FolderLink.Snapshot? snap;
        try { snap = await Task.Run(() => FolderLink.Scan(path)); }
        finally { Cursor = Cursors.Default; }
        if (snap == null || snap.Courses.Count == 0)
        {
            Dialogs.Info(this, "ملقتش فيديوهات ولا ملفات في الفولدر ده.\nاتأكد إنك اخترت فولدر الكورس الكبير، ولو على Google Drive اتأكد إنه ظاهر على الجهاز.");
            return null;
        }
        return snap;
    }

    /// <summary>ربط فولدر جوه مكان: كل فولدر فرعي فيه ← كورس تحت المكان على طول.</summary>
    private async Task LinkFolderIntoPlaceAsync(Place place, string path)
    {
        var snap = await ScanForLinkAsync(path);
        if (snap == null || !_library.Places.Contains(place)) return;
        Checkpoint("ربط فولدر بمكان");
        place.SourceFolders.Add(path);
        place.TouchInfo();
        place.Expanded = true;
        var r = FolderLink.Apply(_library, place, snap, initial: true);
        Log.Info($"folder linked into place {place.Name}: {path} — courses {r.NewCourses}, lectures {r.NewGroups}, videos {r.NewVideos.Count}, moved {r.Moved}, attachments {r.NewAttachments}");
        MarkDirty();
        RebuildTree();
        SaveLibrary();
        SelectItem(place);
        QueueProbe(_library.CoursesInPlace(place).SelectMany(c => c.AllVideos));
        ShowLinkSummary($"اتربط «{new DirectoryInfo(path).Name}» جوه «{place.Name}» ✓", r);
    }

    private void ShowLinkSummary(string title, FolderLink.Result r)
    {
        string moved = r.Moved > 0
            ? $"\n\nلقيت {r.Moved} فيديو كنت ضايفهم قبل كده بإيدك — نقلتهم للترتيب الجديد بتقدّمك وعلاماتك وملاحظاتك."
            : "";
        Dialogs.Info(this,
            title + "\n\n" +
            $"{r.NewCourses} كورس · {r.NewGroups} محاضرة · {r.NewVideos.Count + r.Moved} فيديو · {r.NewAttachments} ملف مرفق" + moved +
            "\n\nمن دلوقتي أي محاضرة أو فيديو جديد ينزل في الفولدر هيظهر هنا لوحده.");
    }

    private async Task LinkFolderAsync(string path)
    {
        var snap = await ScanForLinkAsync(path);
        if (snap == null) return;

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

        ShowLinkSummary($"اتربط «{topic.Name}» ✓", r);
    }

    /// <summary>بيتنده كل دقيقتين، وأول ما المكتبة تتفتح، ومن «تحديث من الفولدر».</summary>
    /// <param name="only">موضوع أو مكان بعينه (null = الكل).</param>
    private async Task SyncLinkedFoldersAsync(object? only = null, bool manual = false)
    {
        if (_folderSyncRunning || _closing) return;
        // كل فولدر مربوط: بموضوع (الفولدر = الموضوع) أو جوه مكان (الفولدر = كورسات المكان)
        var links = new List<(string Name, string Root, Func<FolderLink.Snapshot, FolderLink.Result> Apply)>();
        foreach (var t in _library.Topics.Where(t => !string.IsNullOrWhiteSpace(t.SourceFolder) && (only == null || only == t)))
        {
            var topic = t;
            links.Add((t.Name, t.SourceFolder!, snap => FolderLink.Apply(_library, topic, snap, initial: false)));
        }
        foreach (var p in _library.Places.Where(p => only == null || only == p))
            foreach (string f in p.SourceFolders.ToList())
            {
                var place = p;
                links.Add((p.Name, f, snap => FolderLink.Apply(_library, place, snap, initial: false)));
            }
        if (links.Count == 0) return;
        _folderSyncRunning = true;
        try
        {
            var library = _library;
            var newOnes = new List<(Course Course, LectureGroup Group, VideoItem Video)>();
            bool changed = false;
            foreach (var link in links)
            {
                string root = link.Root;
                var snap = await Task.Run(() => FolderLink.Scan(root));
                if (_closing || library != _library) return; // المكتبة اتغيرت وإحنا بنقرا
                if (snap == null)
                {
                    if (manual) Dialogs.Info(this, $"الفولدر بتاع «{link.Name}» مش موجود دلوقتي:\n{root}\n\nلو على Google Drive اتأكد إن الدرايف شغال.");
                    continue;
                }
                var r = link.Apply(snap);
                if (!r.Changed) continue;
                changed = true;
                Log.Info($"folder sync {link.Name} ({root}): courses +{r.NewCourses}, lectures +{r.NewGroups}, videos +{r.NewVideos.Count}, attachments +{r.NewAttachments}");
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

    /// <summary>أوامر الربط في قايمة المكان: ربط فولدر جديد، وتحديث، وفتح وفك ربط كل فولدر.</summary>
    private void AddPlaceFolderLinkMenuItems(Place p, ToolStripItemCollection items)
    {
        items.Add(MenuItem("🗂  اربط فولدر من الدرايف هنا…", () => LinkFolderIntoPlaceDialog(p)));
        if (p.SourceFolders.Count == 0) return;
        items.Add(MenuItem("🔄  تحديث من الفولدرات دلوقتي", () => _ = SyncLinkedFoldersAsync(p, manual: true)));
        foreach (string folder in p.SourceFolders.ToList())
        {
            string f = folder;
            var sub = new ToolStripMenuItem("🔗  " + Modern.Bidi(new DirectoryInfo(f).Name)) { ForeColor = Theme.Text, ToolTipText = f };
            sub.DropDownItems.Add(MenuItem("📂  فتح الفولدر", () =>
            {
                try { Process.Start(new ProcessStartInfo(f) { UseShellExecute = true }); }
                catch (Exception ex) { Dialogs.Error(this, "مقدرتش أفتح الفولدر:\n" + ex.Message); }
            }));
            sub.DropDownItems.Add(MenuItem("فك الربط بالفولدر ده (الكورسات بتفضل زي ما هي)", () =>
            {
                if (!Dialogs.Confirm(this, $"«{p.Name}» مش هيتابع الفولدر ده تاني:\n{f}\n\nالكورسات والتقدّم بيفضلوا زي ما هما. متأكد؟", "فك الربط")) return;
                Checkpoint("فك ربط فولدر");
                p.SourceFolders.RemoveAll(x => FolderLink.Norm(x) == FolderLink.Norm(f));
                p.TouchInfo();
                MarkDirty();
                RebuildTree();
            }));
            items.Add(sub);
        }
    }

    /// <summary>
    /// موضوع مربوط بفولدر (زي «Eng Doaa Amin Elfawal») يتحوّل لمكان جوه موضوع تاني (زي Backend › ROUTE):
    /// الكورسات بتتنقل بتقدّمها وعلاماتها، والفولدر بيفضل متتابع من المكان.
    /// </summary>
    private ToolStripMenuItem ConvertTopicToPlaceMenu(Topic source)
    {
        var menu = new ToolStripMenuItem("🏫  حوّله لمكان جوه موضوع…") { ForeColor = Theme.Text };
        foreach (var t in _library.Topics.Where(t => t != source))
        {
            var target = t;
            menu.DropDownItems.Add(MenuItem("📚  " + Modern.Bidi(t.Name), () => ConvertTopicToPlace(source, target)));
        }
        if (menu.DropDownItems.Count > 0) menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(MenuItem("موضوع جديد…", () =>
        {
            string? name = Dialogs.Input(this, "موضوع جديد", "اسم الموضوع اللي المكان هيبقى جواه (مثلًا: Backend):");
            if (!string.IsNullOrWhiteSpace(name)) ConvertTopicToPlace(source, null, name.Trim());
        }));
        return menu;
    }

    /// <param name="target">الموضوع اللي المكان هيبقى جواه، أو null + newTopicName لموضوع جديد.</param>
    private bool ConvertTopicToPlace(Topic source, Topic? target, string? newTopicName = null)
    {
        string targetName = target?.Name ?? newTopicName ?? "";
        var existing = target != null ? _library.PlacesOf(target).ToList() : new List<Place>();
        string hint = existing.Count > 0 ? $"\n(الأماكن الموجودة: {string.Join("، ", existing.Select(p => p.Name))} — لو كتبت اسم منهم الكورسات هتتحط فيه)" : "";
        string? name = Dialogs.Input(this, "حوّله لمكان",
            $"الكورسات اللي في «{source.Name}» هتتنقل جوه «{targetName}» في مكان اسمه (مثلًا: ROUTE):{hint}", existing.FirstOrDefault()?.Name ?? "");
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();

        Checkpoint("تحويل موضوع لمكان");
        if (target == null)
        {
            target = new Topic { Name = targetName };
            target.TouchInfo();
            _library.Topics.Insert(Math.Max(0, _library.Topics.IndexOf(source)), target);
        }
        var place = existing.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (place == null)
        {
            place = new Place { Name = name, TopicId = target.Id };
            _library.Places.Add(place);
        }
        var courses = ConvertTopicToPlaceCore(_library, source, place);
        _deletedKeys.Add(LibrarySync.TopicKey(source));
        target.Expanded = true;
        MarkDirty();
        RebuildTree();
        SaveLibrary();
        SelectItem(place);
        ShowStatus($"اتنقل {courses} كورس لـ «{target.Name} › {place.Name}» بتقدّمهم ✓ (Ctrl+Z للتراجع)", 8);
        return true;
    }

    /// <summary>
    /// القلب (من غير واجهة، وبيتفحص في الفحص التلقائي): الكورسات تتنقل للمكان بترتيبها،
    /// والفولدر المربوط يبقى تبع المكان، والأماكن اللي جوه الموضوع القديم تروح للموضوع الجديد، والموضوع القديم يتشال.
    /// </summary>
    internal static int ConvertTopicToPlaceCore(CourseLibrary lib, Topic source, Place place)
    {
        var target = lib.TopicOf(place);
        var direct = lib.DirectCoursesOf(source).ToList();
        foreach (var c in direct) lib.MoveCourse(c, target, place);
        foreach (var p in lib.PlacesOf(source).ToList()) lib.MovePlace(p, target);
        if (!string.IsNullOrWhiteSpace(source.SourceFolder) &&
            !place.SourceFolders.Any(f => FolderLink.Norm(f) == FolderLink.Norm(source.SourceFolder!)))
            place.SourceFolders.Add(source.SourceFolder!);
        place.Expanded = true;
        place.TouchInfo();
        lib.Topics.Remove(source);
        return direct.Count;
    }
}
