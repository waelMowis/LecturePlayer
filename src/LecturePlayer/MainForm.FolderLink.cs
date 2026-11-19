using System.Diagnostics;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// «إضافة كورس من فولدر» والفولدرات المتابَعة: الفولدر (غالبًا على Google Drive) بيتقري لوحده كل دقيقتين
/// في الخلفية، وأي محاضرة أو فيديو جديد بيظهر في مكانه مع تنبيه.
/// </summary>
internal sealed partial class MainForm
{
    private bool _folderSyncRunning;

    /// <summary>اختيار فولدر ← شاشة «إضافة كورس» (هيتحط فين، معاينة، متابعة).</summary>
    private void ImportCourseDialog()
    {
        if (!EnsureLibrary()) return;
        string? path = PickFolder(this, "اختار فولدر الكورس (أو الفولدر اللي فيه كذا كورس)");
        if (path != null) ShowAddFolderDialog(path);
    }

    /// <param name="topic">الموضوع المقترح (null = حسب المحدد في الشجرة).</param>
    /// <param name="source">المصدر المقترح.</param>
    private void ShowAddFolderDialog(string path, Topic? topic = null, Place? source = null, bool useSelection = true)
    {
        if (!EnsureLibrary()) return;
        path = Path.GetFullPath(path);

        // جزء من فولدر متابَع بالفعل؟ ← الجديد فيه بيوصل لوحده
        string key = FolderLink.Norm(path);
        if (_library.Links.FirstOrDefault(l => key.StartsWith(FolderLink.Norm(l.Path) + "\\")) is { } parent)
        {
            Dialogs.Info(this, $"الفولدر ده جوه فولدر متابَع بالفعل:\n{parent.Path}\n\nأي جديد فيه بيتضاف لوحده في «{TargetText(parent)}».");
            return;
        }

        if (useSelection && topic == null && source == null)
        {
            source = SelectedPlaceContext();
            topic = source == null ? SelectedTopicContext() : null;
        }
        var result = AddFolderForm.Show(this, _library, path, topic, source);
        if (result != null) AddFolder(result);
    }

    private string TargetText(LinkedFolder l)
    {
        var (t, p) = _library.TargetOf(l);
        var parts = new[] { t?.Name, p?.Name }.Where(x => x != null).ToList();
        return parts.Count > 0 ? string.Join(" › ", parts) : "المكتبة";
    }

    /// <summary>بينفّذ اختيار شاشة «إضافة كورس»: الموضوع/المصدر الجديد، الكورسات، والمتابعة.</summary>
    private void AddFolder(AddFolderForm.Result r)
    {
        Checkpoint("إضافة كورس من فولدر");
        var (topic, place) = ResolveDestination(r.Topic, r.Source);
        if (r.SubSourceName != null) place = SubSourceFor(topic, place, r.SubSourceName);

        // نفس الفولدر متضاف قبل كده؟ ← يتنقل للمكان الجديد بتقدّمه بدل ما يتكرر
        string key = FolderLink.Norm(r.Path);
        var existing = _library.Links.FirstOrDefault(l => FolderLink.Norm(l.Path) == key);
        int movedCourses = 0;
        if (existing != null)
        {
            existing.Ignored.Clear(); // إضافة الفولدر تاني = رجّع كل اللي كان اتمسح منه
            foreach (var c in _library.Courses.Where(c => _library.LinkOf(c) == existing).ToList())
            {
                PlaceCourseAtEnd(c, topic, place);
                movedCourses++;
            }
            existing.TopicId = topic?.Id;
            existing.PlaceId = place?.Id;
            existing.SingleCourse = r.SingleCourse;
            existing.TouchInfo();
        }

        var res = FolderLink.Apply(_library, place?.TopicId ?? topic?.Id, place?.Id, r.Snapshot, initial: true);
        // الكورسات اللي كانت متضافة من الفولدر ده (بمسار تاني أو بإيدك) تيجي في المكان المختار
        foreach (var cs in r.Snapshot.Courses)
            if (_library.Courses.FirstOrDefault(c => c.SourceFolder != null && FolderLink.Norm(c.SourceFolder) == FolderLink.Norm(cs.Path)) is { } c
                && (_library.TopicOf(c) != (place != null ? _library.TopicOf(place) : topic) || _library.PlaceOf(c) != place))
                PlaceCourseAtEnd(c, topic, place);

        if (r.Follow)
        {
            if (existing == null)
            {
                // فولدرات متابَعة جوه الفولدر ده بقت جزء منه
                _library.Links.RemoveAll(l => FolderLink.Norm(l.Path).StartsWith(key + "\\"));
                var link = new LinkedFolder { Path = r.Path, SingleCourse = r.SingleCourse, TopicId = topic?.Id, PlaceId = place?.Id };
                link.TouchInfo();
                _library.Links.Add(link);
            }
        }
        else if (existing != null)
        {
            _deletedKeys.Add(LibrarySync.LinkKey(existing));
            _library.Links.Remove(existing);
        }

        Log.Info($"folder added: {r.Path} ({(r.SingleCourse ? "one course" : "courses")}, follow={r.Follow}) → {(topic?.Name ?? "-")} / {(place?.Name ?? "-")} — " +
                 $"courses {res.NewCourses}, lectures {res.NewGroups}, videos {res.NewVideos.Count}, recognized {res.Moved}, attachments {res.NewAttachments}, relinked {movedCourses}");
        MarkDirty();
        RebuildTree();
        SaveLibrary();

        var first = r.Snapshot.Courses.Select(cs => _library.Courses.FirstOrDefault(c => c.SourceFolder != null && FolderLink.Norm(c.SourceFolder) == FolderLink.Norm(cs.Path)))
            .FirstOrDefault(c => c != null);
        if (first != null) SelectItem(first);
        QueueProbe(_library.Courses.Where(c => r.Snapshot.Courses.Any(cs => c.SourceFolder != null && FolderLink.Norm(c.SourceFolder) == FolderLink.Norm(cs.Path)))
            .SelectMany(c => c.AllVideos));

        string where = string.Join(" › ", new[] { topic?.Name ?? (place != null ? _library.TopicOf(place)?.Name : null),
            place != null ? _library.ParentOf(place)?.Name : null, place?.Name }.Where(x => x != null));
        string msg = $"اتضاف ✓  {r.Snapshot.Courses.Count} كورس · {res.NewGroups} محاضرة · {res.NewVideos.Count + res.Moved} فيديو" +
                     (where.Length > 0 ? $"  في «{where}»" : "");
        if (res.Moved > 0) msg += $"\n{res.Moved} فيديو كانوا عندك قبل كده — اتنقلوا بتقدّمك وعلاماتك.";
        if (r.Follow) msg += "\nوأي جديد ينزل في الفولدر هيتضاف لوحده.";
        ShowBanner(msg.Replace("\n", "  ·  "), BannerKind.Success, seconds: 10, key: "folder-added");
    }

    /// <summary>بيحوّل اختيار الشاشة لموضوع/مصدر حقيقي (ويعمل الجديد).</summary>
    private (Topic? Topic, Place? Place) ResolveDestination(AddFolderForm.Choice<Topic> tc, AddFolderForm.Choice<Place> pc)
    {
        Topic? topic = tc.Existing;
        if (tc.NewName != null)
        {
            topic = _library.Topics.FirstOrDefault(t => string.Equals(t.Name, tc.NewName, StringComparison.CurrentCultureIgnoreCase));
            if (topic == null)
            {
                topic = new Topic { Name = tc.NewName };
                topic.TouchInfo();
                _library.Topics.Add(topic);
            }
        }
        Place? place = pc.Existing;
        if (pc.NewName != null)
        {
            place = _library.PlacesOf(topic).FirstOrDefault(p => string.Equals(p.Name, pc.NewName, StringComparison.CurrentCultureIgnoreCase));
            if (place == null)
            {
                place = new Place { Name = pc.NewName, TopicId = topic?.Id };
                place.TouchInfo();
                _library.Places.Add(place);
            }
        }
        if (topic != null) topic.Expanded = true;
        if (place != null) place.Expanded = true;
        return (topic, place);
    }

    /// <summary>
    /// المصدر الفرعي باسم الفولدر (مثلًا المدرّس) جوه المصدر المختار. لو المختار نفسه فرعي، بيتعمل جنبه
    /// (جوه نفس الأكبر). لو مفيش مصدر مختار، بيبقى مصدر أساسي في الموضوع. لو موجود بنفس الاسم بيتستخدم.
    /// </summary>
    private Place SubSourceFor(Topic? topic, Place? chosen, string name)
    {
        var parent = chosen == null ? null : _library.ParentOf(chosen) ?? chosen;
        var t = parent != null ? _library.TopicOf(parent) : topic;
        var existing = _library.PlacesOf(t).FirstOrDefault(p => _library.ParentOf(p) == parent
            && string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing != null) return existing;
        var sub = new Place { Name = name, TopicId = t?.Id, ParentId = parent?.Id };
        sub.TouchInfo();
        var after = _library.PlacesOf(t).LastOrDefault(p => p == parent || _library.ParentOf(p) == parent);
        int at = after != null ? _library.Places.IndexOf(after) + 1 : _library.Places.Count;
        _library.Places.Insert(Math.Clamp(at, 0, _library.Places.Count), sub);
        if (parent != null) parent.Expanded = true;
        return sub;
    }

    /// <summary>الفولدرات المتابَعة اللي ليها علاقة بالعنصر ده (موضوع/مصدر/كورس)، أو كلها.</summary>
    private List<LinkedFolder> LinksFor(object? item)
    {
        bool InTopic(LinkedFolder l, Topic t) => _library.TargetOf(l).Topic == t || _library.Courses.Any(c => _library.LinkOf(c) == l && _library.TopicOf(c) == t);
        bool InPlace(LinkedFolder l, Place p)
        {
            var target = _library.TargetOf(l).Place;
            return target == p || (target != null && _library.ParentOf(target) == p)
                   || _library.AllCoursesUnder(p).Any(c => _library.LinkOf(c) == l);
        }
        return item switch
        {
            null => _library.Links.ToList(),
            LinkedFolder l => new List<LinkedFolder> { l },
            Course c => _library.LinkOf(c) is { } l ? new List<LinkedFolder> { l } : new(),
            Place p => _library.Links.Where(l => InPlace(l, p)).ToList(),
            Topic t => _library.Links.Where(l => InTopic(l, t)).ToList(),
            _ => new()
        };
    }

    /// <summary>بيتنده كل دقيقتين، وأول ما المكتبة تتفتح، ومن «هات الجديد من الفولدر».</summary>
    /// <param name="only">فولدر أو موضوع أو مصدر أو كورس بعينه (null = الكل).</param>
    private async Task SyncLinkedFoldersAsync(object? only = null, bool manual = false)
    {
        if (_folderSyncRunning || _closing) return;
        var links = LinksFor(only);
        if (links.Count == 0) return;
        _folderSyncRunning = true;
        try
        {
            var library = _library;
            var newOnes = new List<(Course Course, LectureGroup Group, VideoItem Video)>();
            bool changed = false;
            foreach (var link in links)
            {
                string root = link.Path;
                bool single = link.SingleCourse;
                var snap = await Task.Run(() => FolderLink.Scan(root, single));
                if (_closing || library != _library) return; // المكتبة اتغيرت وإحنا بنقرا
                if (!_library.Links.Contains(link)) continue;
                if (snap == null)
                {
                    if (manual) Dialogs.Info(this, $"الفولدر ده مش موجود دلوقتي:\n{root}\n\nلو على Google Drive اتأكد إن الدرايف شغال.");
                    continue;
                }
                var r = FolderLink.Apply(_library, link, snap, initial: false);
                if (!r.Changed) continue;
                changed = true;
                Log.Info($"folder sync {root}: courses +{r.NewCourses}, lectures +{r.NewGroups}, videos +{r.NewVideos.Count}, attachments +{r.NewAttachments}");
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

    /// <summary>الفولدر المتابَع اللي المسار ده جواه (null لو مش من فولدر متابَع).</summary>
    private LinkedFolder? LinkContaining(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || MediaFiles.IsUrl(path)) return null;
        string p = FolderLink.Norm(path);
        return _library.Links.FirstOrDefault(l => { string lp = FolderLink.Norm(l.Path); return p == lp || p.StartsWith(lp + "\\"); });
    }

    /// <summary>
    /// المستخدم مسح حاجة جاية من فولدر متابَع: نفتكرها عشان المزامنة مترجّعهاش بعد دقيقتين.
    /// (لو أضاف الفولدر تاني من «إضافة كورس» كل اللي اتمسح بيرجع.)
    /// </summary>
    private void IgnoreInFollowed(string? path)
    {
        if (LinkContaining(path) is not { } link) return;
        string p = FolderLink.Norm(path!);
        if (link.Ignored.Any(x => FolderLink.Norm(x) == p)) return;
        link.Ignored.Add(path!);
        link.TouchInfo();
    }

    /// <summary>سطر بيتضاف لرسالة الحذف لو الحاجة جاية من فولدر متابَع.</summary>
    private string FollowedDeleteNote(string? path) => LinkContaining(path) != null
        ? "\n\nده من فولدر متابَع: البرنامج مش هيرجّعه تاني لوحده (ولو عايزه يرجع، ضيف الفولدر تاني من «إضافة كورس من فولدر»)."
        : "";

    /// <summary>أوامر الفولدر المتابَع في قايمة الكورس (لو الكورس جاي من فولدر متابَع).</summary>
    private void AddCourseLinkMenuItems(Course c, ToolStripItemCollection items)
    {
        if (_library.LinkOf(c) is not { } link) return;
        items.Add(MenuItem("🔄  هات الجديد من الفولدر", () => _ = SyncLinkedFoldersAsync(link, manual: true)));
        items.Add(MenuItem("📂  فتح الفولدر", () => OpenFolder(c.SourceFolder ?? link.Path)));
        items.Add(MenuItem("بطّل متابعة الفولدر (الكورسات بتفضل)", () => StopFollowing(link)));
    }

    /// <summary>«هات الجديد» في قايمة الموضوع/المصدر (لو فيه فولدرات متابَعة).</summary>
    private void AddRefreshLinksMenuItem(object item, ToolStripItemCollection items)
    {
        int n = LinksFor(item).Count;
        if (n > 0) items.Add(MenuItem(n == 1 ? "🔄  هات الجديد من الفولدر" : $"🔄  هات الجديد من الفولدرات ({n})", () => _ = SyncLinkedFoldersAsync(item, manual: true)));
    }

    private void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Dialogs.Error(this, "مقدرتش أفتح الفولدر:\n" + ex.Message); }
    }

    private void StopFollowing(LinkedFolder link)
    {
        if (!Dialogs.Confirm(this, $"البرنامج مش هيتابع الفولدر ده تاني:\n{link.Path}\n\nالكورسات والتقدّم بيفضلوا زي ما هما، بس الجديد مش هيتضاف لوحده. متأكد؟", "بطّل المتابعة")) return;
        Checkpoint("إيقاف متابعة فولدر");
        _deletedKeys.Add(LibrarySync.LinkKey(link));
        _library.Links.Remove(link);
        MarkDirty();
        RebuildTree();
    }

    /// <summary>
    /// موضوع (زي اللي كان بيتعمل من «ربط فولدر» في 4.1.30) يتحوّل لمصدر جوه موضوع تاني:
    /// الكورسات بتتنقل بتقدّمها وعلاماتها، والفولدرات المتابَعة بتحط الجديد في المصدر.
    /// </summary>
    private ToolStripMenuItem ConvertTopicToPlaceMenu(Topic source)
    {
        var menu = new ToolStripMenuItem("حوّله لمصدر جوه موضوع تاني…") { ForeColor = Theme.Text };
        foreach (var t in _library.Topics.Where(t => t != source))
        {
            var target = t;
            menu.DropDownItems.Add(MenuItem("📚  " + Modern.Bidi(t.Name), () => ConvertTopicToPlace(source, target)));
        }
        if (menu.DropDownItems.Count > 0) menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(MenuItem("موضوع جديد…", () =>
        {
            string? name = Dialogs.Input(this, "موضوع جديد", "اسم الموضوع اللي المصدر هيبقى جواه:");
            if (!string.IsNullOrWhiteSpace(name)) ConvertTopicToPlace(source, null, name.Trim());
        }));
        return menu;
    }

    /// <param name="target">الموضوع اللي المصدر هيبقى جواه، أو null + newTopicName لموضوع جديد.</param>
    private bool ConvertTopicToPlace(Topic source, Topic? target, string? newTopicName = null)
    {
        string targetName = target?.Name ?? newTopicName ?? "";
        var existing = target != null ? _library.PlacesOf(target).ToList() : new List<Place>();
        string hint = existing.Count > 0 ? $"\n(المصادر الموجودة: {string.Join("، ", existing.Select(p => p.Name))} — لو كتبت اسم منهم الكورسات هتتحط فيه)" : "";
        string? name = Dialogs.Input(this, "حوّله لمصدر",
            $"الكورسات اللي في «{source.Name}» هتتنقل جوه «{targetName}» في مصدر اسمه:{hint}", existing.FirstOrDefault()?.Name ?? source.Name);
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();

        Checkpoint("تحويل موضوع لمصدر");
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
        int courses = ConvertTopicToPlaceCore(_library, source, place);
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
    /// القلب (من غير واجهة، وبيتفحص في الفحص التلقائي): الكورسات تتنقل للمصدر بترتيبها، والفولدرات المتابَعة
    /// اللي كانت بتحط في الموضوع القديم تحط في المصدر، والمصادر اللي جواه تروح للموضوع الجديد، والموضوع القديم يتشال.
    /// </summary>
    internal static int ConvertTopicToPlaceCore(CourseLibrary lib, Topic source, Place place)
    {
        var target = lib.TopicOf(place);
        var direct = lib.DirectCoursesOf(source).ToList();
        foreach (var c in direct) lib.MoveCourse(c, target, place);
        foreach (var p in lib.PlacesOf(source).ToList()) lib.MovePlace(p, target);
        foreach (var l in lib.Links.Where(l => l.TopicId == source.Id && l.PlaceId == null))
        {
            l.TopicId = target?.Id;
            l.PlaceId = place.Id;
            l.TouchInfo();
        }
        foreach (var l in lib.Links.Where(l => l.TopicId == source.Id)) { l.TopicId = target?.Id; l.TouchInfo(); }
        place.Expanded = true;
        place.TouchInfo();
        lib.Topics.Remove(source);
        return direct.Count;
    }
}
