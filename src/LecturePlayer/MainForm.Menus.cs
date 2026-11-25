using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// قوايم الشجرة (كليك يمين، «＋ إضافة»، المساحة الفاضية، Insert) — كلها من مكان واحد وبقاعدة واحدة:
///  • الشجرة: المكتبة ← موضوع ← مصدر ← كورس ← محاضرة ← فيديو. لما تقف على مستوى، أول إضافة هي المستوى اللي تحته على طول.
///  • نفس ترتيب المجموعات في كل قايمة: ▶ تشغيل ← ➕ إضافة ← 🔄 هات الجديد ← ✏ تعديل وشكل ← ↔ نقل وترتيب ← ✔ التقدّم ← 📤 حفظ ومشاركة ← 🗑 حذف (دايمًا الآخر).
///  • «➕ كورس هنا» واحد في كل مكان وجواه كل المصادر (فولدر، يوتيوب، يوديمي، مهارة تك/Moodle، IT Legend، فاضي).
///  • القوايم دي واحدة في الوضع البسيط والمتقدم.
/// </summary>
internal sealed partial class MainForm
{
    // =====================================================================
    //  نقطة الدخول
    // =====================================================================

    private void ShowNodeMenu(TreeNode node, Point location)
    {
        ClearNodeMenu();
        BuildNodeMenu(node.Tag, _nodeMenu.Items);
        _nodeMenu.Show(_tree, location);
    }

    private void ShowEmptyAreaMenu(Point location)
    {
        ClearNodeMenu();
        BuildEmptyAreaMenu(_nodeMenu.Items);
        _nodeMenu.Show(_tree, location);
    }

    /// <summary>Insert: قايمة «إضافة» للحاجة المحددة، جنبها في الشجرة.</summary>
    private void ShowAddMenuAtSelection()
    {
        ClearNodeMenu();
        BuildAddMenu(_tree.SelectedNode?.Tag, _nodeMenu.Items);
        var b = _tree.SelectedNode?.Bounds ?? Rectangle.Empty;
        _nodeMenu.Show(_tree, new Point(Math.Max(0, b.Left), b.Bottom));
    }

    internal void BuildNodeMenu(object? tag, ToolStripItemCollection items)
    {
        switch (tag)
        {
            case TopicDivider d: BuildDividerMenu(d.Topic, items); break;
            case Topic t: BuildTopicMenu(t, items); break;
            case Place p: BuildPlaceMenu(p, items); break;
            case Course c: BuildCourseMenu(c, items); break;
            case LectureGroup g: BuildGroupMenu(g, items); break;
            case VideoItem v: BuildVideoMenu(v, items); break;
        }
    }

    // =====================================================================
    //  حتت مشتركة
    // =====================================================================

    private static ToolStripSeparator Sep() => new();

    /// <summary>الأمر موجود في القايمة أو في أي قايمة فرعية جواها.</summary>
    internal static bool MenuHas(ToolStripItemCollection items, string text) =>
        items.OfType<ToolStripItem>().Any(i => i.Text?.Contains(text) == true ||
            (i is ToolStripDropDownItem dd && MenuHas(dd.DropDownItems, text)));

    /// <summary>«➕ كورس هنا ▸»: كل طرق إضافة كورس، والكورس بيتحط في الموضوع/المصدر ده على طول.</summary>
    private ToolStripMenuItem CourseSourcesMenu(string text, Topic? topic, Place? place)
    {
        var target = new ImportTarget(place != null ? _library.TopicOf(place) : topic, place);
        object? where = (object?)place ?? topic;
        return SubMenuOf(text,
            MenuItem("📁  من فولدر على الجهاز أو الدرايف…", () =>
            {
                if (!EnsureLibrary()) return;
                string? path = PickFolder(this, "اختار فولدر الكورس (أو الفولدر اللي فيه كذا كورس)" + (where != null ? " — هيتحط في " + target.Label : ""));
                if (path != null) ShowAddFolderDialog(path, place == null ? topic : null, place, useSelection: false);
            }, where == null ? "Ctrl+I" : null),
            MenuItem("🔗  من يوتيوب (فيديو أو قائمة تشغيل)…", () => AddUrlsDialog(null, where), where == null ? "Ctrl+U" : null),
            Sep(),
            MenuItem("🎓  من يوديمي (بحسابك)…", () => UdemyImport(null, target)),
            MenuItem("🎓  من مهارة تك أو أي موقع Moodle…", () => MoodleImport(target: target)),
            MenuItem("🎓  من IT Legend…", () => ItLegendImport(null, target)),
            Sep(),
            MenuItem("📄  كورس فاضي (تملاه بإيدك)…", () =>
            {
                if (place != null) AddCourse(placeFor: place);
                else if (topic != null) AddCourse(topicFor: topic);
                else AddCourse(atRoot: true);
            }));
    }

    /// <summary>🔄 تحت موضوع/مصدر: الفولدرات المتابَعة وقوايم يوتيوب المربوطة (لو فيه).</summary>
    private ToolStripMenuItem? RefreshUnderItem(object item)
    {
        int links = LinksFor(item).Count;
        var lists = CoursesUnder(item).SelectMany(c => c.Groups).Where(g => g.IsPlaylist).ToList();
        if (links == 0 && lists.Count == 0) return null;
        string what = links > 0 && lists.Count > 0 ? $"الفولدرات وقوايم يوتيوب" : links > 0 ? (links == 1 ? "الفولدر" : $"الفولدرات ({links})") : $"قوايم يوتيوب ({lists.Count})";
        return MenuItem("🔄  هات الجديد من " + what, () =>
        {
            if (links > 0) _ = SyncLinkedFoldersAsync(item, manual: true);
            if (lists.Count > 0) _ = SyncPlaylistsAsync(lists, quiet: false);
        });
    }

    /// <summary>🔄 للكورس: بيعرف مصدره لوحده (يوديمي، IT Legend، مهارة تك/Moodle، فولدر، يوتيوب).</summary>
    private ToolStripMenuItem? CourseRefreshItem(Course c)
    {
        if (c.UdemyId != null) return MenuItem("🔄  هات الجديد من يوديمي (والدروس اللي خلّصتها)", () => UdemyImport(c));
        if (ItLegend.CourseIdOf(c.SourceUrl) != null) return MenuItem("🔄  هات الجديد من IT Legend (والدروس اللي خلّصتها)", () => ItLegendImport(c));
        if (Moodle.ParseCourseUrl(c.SourceUrl) is { } m)
        {
            string site = SiteLabel(MoodleAccountFor(m.Site), m.Site);
            return MenuItem($"🔄  هات الجديد من {site} (والأنشطة اللي خلّصتها)", () => MoodleImport(c));
        }
        return RefreshUnderItem(c);
    }

    private IEnumerable<Course> CoursesUnder(object item) => item switch
    {
        Topic t => _library.CoursesOf(t),
        Place p => _library.AllCoursesUnder(p),
        Course c => new[] { c },
        _ => Enumerable.Empty<Course>()
    };

    private void SortCoursesByName(IEnumerable<Course> scope, string label)
    {
        Checkpoint(label);
        var mine = scope.OrderBy(c => c.Name, NaturalComparer.Instance).ToList();
        var slots = _library.Courses.Select((c, i) => (c, i)).Where(x => mine.Contains(x.c)).Select(x => x.i).ToList();
        for (int k = 0; k < slots.Count; k++) _library.Courses[slots[k]] = mine[k];
        MarkDirty();
        RebuildTree();
    }

    private static ToolStripMenuItem WithText(ToolStripMenuItem item, string text) { item.Text = text; return item; }

    private ToolStripMenuItem MoveUpDown(string? extraTitle = null) => SubMenuOf("↕  ترتيب",
        MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"),
        MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"));

    private void SelectThen(object item, Action action)
    {
        if (NodeOf(item) is { } n) _tree.SelectedNode = n;
        action();
    }

    // =====================================================================
    //  موضوع
    // =====================================================================

    private void BuildTopicMenu(Topic t, ToolStripItemCollection items)
    {
        items.Add(MenuItem("▶  كمّل الموضوع (آخر كورس كنت فيه)", () => PlayTopic(t)));
        items.Add(Sep());
        // ➕ اللي تحت الموضوع على طول: مصدر، وبعده كورس
        items.Add(MenuItem("➕  مصدر جديد هنا (منصة، أكاديمية، قناة…)…", () => AddPlace(t)));
        items.Add(CourseSourcesMenu("➕  كورس هنا", t, null));
        items.Add(MenuItem("➕  موضوع جديد بعده…", () => SelectThen(t, () => AddTopic())));
        if (RefreshUnderItem(t) is { } r) items.Add(r);
        items.Add(Sep());
        items.Add(MenuItem("✏  إعادة تسمية…", RenameSelected, "F2"));
        items.Add(AppearanceMenuItem(t));
        if (!t.SeparatorBefore)
            items.Add(MenuItem("➖  فاصل فوقه (بعنوان أو خط)…", () => EditTopicSeparator(t)));
        else
            items.Add(SubMenuOf("➖  الفاصل اللي فوقه",
                MenuItem("✏  تعديل…", () => EditTopicSeparator(t)),
                MenuItem("✕  شيل الفاصل", () => RemoveTopicSeparator(t))));
        items.Add(Sep());
        items.Add(SubMenuOf("↕  ترتيب وعرض",
            MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"),
            MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"),
            Sep(),
            MenuItem("ترتيب الكورسات بالاسم", () => SortCoursesByName(_library.DirectCoursesOf(t), "ترتيب الكورسات")),
            Sep(),
            FoldMenu(t)));
        items.Add(SubMenuOf("✔  التقدّم",
            MenuItem("تعليم كل فيديوهات الموضوع كمُشاهَدة", ToggleWatchedSelected, "W"),
            MenuItem("↺  تصفير التقدّم في الموضوع كله…", () => ResetWatched(_library.CoursesOf(t).SelectMany(c => c.AllVideos), $"موضوع «{t.Name}»"))));
        items.Add(ExportMenuItem(t));
        items.Add(NotionTopicMenu(t));
        items.Add(Sep());
        items.Add(MenuItem("🗑  حذف الموضوع (الكورسات بتفضل)", DeleteSelected, "Delete"));
    }

    private void BuildDividerMenu(Topic t, ToolStripItemCollection items)
    {
        items.Add(MenuItem("✏  تعديل عنوان الفاصل…", () => EditTopicSeparator(t), "F2"));
        items.Add(MenuItem("➖  خليه خط من غير عنوان", () => SetTopicSeparator(t, "")));
        items.Add(Sep());
        items.Add(MenuItem("🗑  شيل الفاصل", () => RemoveTopicSeparator(t), "Delete"));
    }

    // =====================================================================
    //  مصدر
    // =====================================================================

    private void BuildPlaceMenu(Place p, ToolStripItemCollection items)
    {
        bool top = _library.ParentOf(p) == null;
        items.Add(MenuItem("▶  كمّل آخر كورس في المصدر ده", () => PlayPlace(p)));
        items.Add(Sep());
        if (top) items.Add(MenuItem("➕  مصدر فرعي هنا (مدرّس، قناة…)…", () => AddPlace(null, parent: p)));
        items.Add(CourseSourcesMenu("➕  كورس هنا", null, p));
        items.Add(MenuItem("➕  مصدر جديد بعده…", () => AddPlace(_library.TopicOf(p), parent: _library.ParentOf(p))));
        if (RefreshUnderItem(p) is { } r) items.Add(r);
        items.Add(Sep());
        items.Add(MenuItem("✏  إعادة تسمية…", RenameSelected, "F2"));
        items.Add(AppearanceMenuItem(p));
        items.Add(Sep());
        items.Add(WithText(MovePlaceToTopicMenu(p), "↔  انقل لـ…"));
        items.Add(SubMenuOf("↕  ترتيب وعرض",
            MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"),
            MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"),
            Sep(),
            MenuItem("ترتيب الكورسات بالاسم", () => SortCoursesByName(_library.CoursesInPlace(p), "ترتيب الكورسات")),
            Sep(),
            FoldMenu(p)));
        items.Add(SubMenuOf("✔  التقدّم",
            MenuItem("تعليم كل فيديوهات المصدر كمُشاهَدة", ToggleWatchedSelected, "W"),
            MenuItem("↺  تصفير التقدّم في المصدر كله…", () => ResetWatched(_library.AllCoursesUnder(p).SelectMany(c => c.AllVideos), $"«{p.Name}»"))));
        items.Add(ExportMenuItem(p));
        items.Add(Sep());
        items.Add(MenuItem("🗑  حذف المصدر (الكورسات بتفضل)", DeleteSelected, "Delete"));
    }

    // =====================================================================
    //  كورس
    // =====================================================================

    private void BuildCourseMenu(Course c, ToolStripItemCollection items)
    {
        var link = _library.LinkOf(c);
        items.Add(MenuItem("▶  تشغيل / استكمال الكورس", () => PlayCourse(c)));
        items.Add(Sep());
        items.Add(MenuItem("➕  محاضرة جديدة فاضية…", () => AddGroup(course: c)));
        items.Add(SubMenuOf("➕  إضافة للكورس",
            MenuItem("📁  فولدر محاضرات من الجهاز…", () => ImportIntoCourseDialog(c)),
            MenuItem("🔗  فيديوهات أو قائمة من يوتيوب…", () => AddUrlsDialog(null, c))));
        items.Add(MenuItem("➕  كورس جديد بعده…", () => AddCourse(after: c)));
        if (CourseRefreshItem(c) is { } r) items.Add(r);
        items.Add(Sep());
        items.Add(MenuItem("✏  إعادة تسمية…", RenameSelected, "F2"));
        items.Add(AppearanceMenuItem(c));
        items.Add(MenuItem("📋  بيانات الكورس (الحالة، الموعد، الوسوم)…", () => EditCourseInfo(c), "F4"));
        items.Add(Sep());
        items.Add(WithText(MoveCourseMenu(c), "↔  انقل لـ…"));
        items.Add(SubMenuOf("↕  ترتيب وعرض",
            MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"),
            MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"),
            Sep(),
            MenuItem("ترتيب المحاضرات بالاسم", () => SortGroups(c)),
            Sep(),
            FoldMenu(c)));
        items.Add(SubMenuOf("✔  التقدّم",
            MenuItem(c.IsCompleted ? "إلغاء علامة المشاهدة للكورس كله" : "تعليم الكورس كله كمُشاهَد", ToggleWatchedSelected, "W"),
            MenuItem("↺  تصفير التقدّم في الكورس…", () => ResetWatched(c.AllVideos, $"كورس «{c.Name}»"))));
        var share = new List<ToolStripItem>
        {
            ExportMenuItem(c),
            MenuItem("📝  حفظ ملاحظات الكورس في ملف…", () => ExportNotesDialog(c), "Ctrl+E")
        };
        if (c.AllVideos.Any(v => v.IsDownloadable))
            share.Add(MenuItem("⬇  تنزيل الكورس كله (فيديو أو MP3)…", () => DownloadCourse(c)));
        items.Add(SubMenuOf("📤  حفظ ومشاركة", share.ToArray()));
        // المصدر: الفولدر على الجهاز أو صفحة الكورس على المنصة
        var source = new List<ToolStripItem>();
        if (link != null)
        {
            source.Add(MenuItem("📂  فتح الفولدر", () => OpenFolder(c.SourceFolder ?? link.Path)));
            source.Add(MenuItem("بطّل متابعة الفولدر (الكورس بيفضل)", () => StopFollowing(link)));
        }
        if (c.SourceUrl != null && MediaFiles.IsUrl(c.SourceUrl))
            source.Add(MenuItem("🌐  فتح صفحة الكورس في المتصفح", () => OpenInBrowser(c.SourceUrl)));
        if (source.Count > 0) items.Add(SubMenuOf("📂  المصدر", source.ToArray()));
        if (NotionCourseMenu(c) is { } notion) items.Add(notion);
        items.Add(Sep());
        items.Add(MenuItem("🗑  حذف الكورس من المكتبة", DeleteSelected, "Delete"));
    }

    // =====================================================================
    //  محاضرة
    // =====================================================================

    private void BuildGroupMenu(LectureGroup g, ToolStripItemCollection items)
    {
        var course = _library.FindCourseOf(g);
        bool platform = g.IsUdemySection || g.IsMoodleSection || g.IsItLegendSection;
        items.Add(MenuItem("▶  تشغيل المحاضرة", () => PlayGroup(g)));
        items.Add(MenuItem("⏮  تشغيل من أول فيديو", () => { if (g.Videos.Count > 0) PlayVideo(g, g.Videos[0], 0); }));
        if (g.Attachments.Count > 0) items.Add(AttachmentsMenu(g));
        items.Add(Sep());
        items.Add(MenuItem("➕  فيديوهات من الجهاز…", () => AddVideosDialog(g)));
        items.Add(MenuItem("➕  فيديوهات من يوتيوب…", () => AddUrlsDialog(null, g)));
        items.Add(MenuItem("➕  مرفقات (PDF، سلايدات، كود)…", () => AddAttachmentFiles(g)));
        items.Add(MenuItem("➕  محاضرة جديدة بعدها…", () => AddGroup(after: g)));
        if (g.IsPlaylist) items.Add(MenuItem("🔄  هات الجديد من يوتيوب", () => _ = SyncPlaylistsAsync(new[] { g }, quiet: false)));
        items.Add(Sep());
        items.Add(MenuItem("✏  إعادة تسمية…", RenameSelected, "F2"));
        if (!platform)
        {
            if (g.SourceUrl != null)
                items.Add(MenuItem("🔗  فك الربط بقائمة يوتيوب", () => { Checkpoint("فك الربط"); g.SourceUrl = null; g.InfoUpdatedUtc = DateTime.UtcNow; MarkDirty(); RefreshGroup(g); }));
            else items.Add(MenuItem("🔗  ربط بقائمة تشغيل يوتيوب (عشان الجديد يتضاف)…", () => LinkPlaylist(g)));
        }
        items.Add(Sep());

        var moveTo = SubMenuOf("↔  نقل لكورس تاني");
        foreach (var target in _library.Courses.Where(x => x != course))
        {
            var t = target;
            moveTo.DropDownItems.Add(MenuItem(t.Name, () => MoveGroupToCourse(g, t)));
        }
        if (moveTo.DropDownItems.Count > 0) moveTo.DropDownItems.Add(Sep());
        moveTo.DropDownItems.Add(MenuItem("كورس جديد…", () =>
        {
            var nc = AddCourse(after: course);
            if (nc != null) MoveGroupToCourse(g, nc);
        }));
        items.Add(moveTo);
        items.Add(SubMenuOf("↕  ترتيب",
            MenuItem("تحريك لفوق", () => MoveSelected(-1), "Alt+↑"),
            MenuItem("تحريك لتحت", () => MoveSelected(+1), "Alt+↓"),
            Sep(),
            MenuItem("ترتيب الفيديوهات بالاسم", () => SortGroupVideos(g)),
            MenuItem("تقسيم تلقائي (عند Part 01)…", () => AutoSplitDialog(g))));
        var progress = new List<ToolStripItem>
        {
            MenuItem(g.IsCompleted ? "إلغاء علامة المشاهدة للكل" : "تعليم الكل كمُشاهَد", ToggleWatchedSelected, "W"),
            MenuItem("↺  تصفير التقدّم في المحاضرة…", () => ResetWatched(g.Videos, $"محاضرة «{g.Name}»"))
        };
        if (g.NewCount > 0) progress.Insert(0, MenuItem($"شيل علامة «جديد» ({g.NewCount})", () => ClearNew(g)));
        items.Add(SubMenuOf("✔  التقدّم", progress.ToArray()));
        if (g.Videos.Any(x => x.IsDownloadable))
            items.Add(MenuItem("⬇  تنزيل فيديوهات يوتيوب (فيديو أو MP3)…", () => DownloadFromLibrary(g.Videos, g.Name)));
        if (NotionGroupMenu(g) is { } notion) items.Add(notion);
        items.Add(Sep());
        items.Add(MenuItem("🗑  حذف المحاضرة من المكتبة", DeleteSelected, "Delete"));
    }

    // =====================================================================
    //  فيديو
    // =====================================================================

    private void BuildVideoMenu(VideoItem v, ToolStripItemCollection items)
    {
        var owner = _library.FindGroupOf(v);
        var node = NodeOf(v);
        if (IsAvailable(v))
        {
            items.Add(MenuItem("▶  تشغيل", () => { if (node != null) PlayNode(node, fromStart: false); }, "Enter"));
            if (v.LastPositionMs > 0) items.Add(MenuItem("⏮  تشغيل من البداية", () => { if (node != null) PlayNode(node, fromStart: true); }));
        }
        else items.Add(MenuItem("⚠  الملف مش موجود — حدد مكانه الجديد…", () => RelocateMissing(v)));
        items.Add(MenuItem(v.Watched ? "✔  تعليم كغير مُشاهَد" : "✔  تعليم كمُشاهَد", () => SetWatched(v, !v.Watched), "W"));
        if (v.Watched || v.LastPositionMs > 0)
            items.Add(MenuItem("↺  تصفير التقدّم (يبدأ من الأول)", () => ResetWatched(new[] { v }, $"«{v.Title}»")));
        items.Add(Sep());
        items.Add(MenuItem("✏  إعادة تسمية…", RenameSelected, "F2"));
        items.Add(MenuItem("✂  ابدأ محاضرة جديدة من هنا…", () => SplitFromHere(v)));
        items.Add(Sep());
        items.Add(WithText(BuildMoveVideoMenu(v, owner), "↔  نقل إلى محاضرة"));
        items.Add(MoveUpDown());
        var file = new List<ToolStripItem>();
        if (v.IsOnline)
        {
            if (IsDownloaded(v)) file.Add(MenuItem("مسح النسخة المتنزلة", () => RemoveOffline(v)));
            else if (v.IsDownloadable) file.Add(MenuItem("⬇  تنزيل (فيديو أو MP3)…", () => DownloadFromLibrary(new[] { v }, v.Title)));
            file.Add(MenuItem("🌐  فتح في المتصفح", () => OpenInBrowser(v.Path)));
            file.Add(MenuItem("📋  نسخ الرابط", () => { try { Clipboard.SetText(v.Path); } catch { } }));
        }
        else if (IsAvailable(v)) file.Add(MenuItem("📂  فتح مكان الملف", () => OpenInExplorer(v.LocalPath)));
        if (file.Count > 0) items.Add(SubMenuOf(v.IsOnline ? "🌐  الرابط والتنزيل" : "📂  الملف", file.ToArray()));
        if (owner?.NotionPageId != null && NotionTopicOf(owner) != null)
            items.Add(MenuItem("📤  ابعت ملاحظات المحاضرة لنوشن", () => SendNotesToNotion(owner)));
        items.Add(Sep());
        items.Add(MenuItem("🗑  إزالة من المكتبة", DeleteSelected, "Delete"));
    }

    // =====================================================================
    //  المساحة الفاضية + «＋ إضافة»
    // =====================================================================

    private void BuildEmptyAreaMenu(ToolStripItemCollection items)
    {
        items.Add(MenuItem("➕  موضوع جديد…", () => { _tree.SelectedNode = null; AddTopic(); }, "Ctrl+Shift+N"));
        items.Add(CourseSourcesMenu("➕  كورس (من غير موضوع)", null, null));
        items.Add(Sep());
        var sepAll = MenuItem("➖  فواصل بين كل الموضوعات", ToggleAllTopicSeparators);
        sepAll.Checked = _settings.TopicSeparators;
        items.Add(sepAll);
        items.Add(MenuItem("⊞  فتح الكل", () => SetAllExpanded(true), "Ctrl+Shift+]"));
        items.Add(MenuItem("⊟  قفل الكل", () => SetAllExpanded(false), "Ctrl+Shift+["));
    }

    /// <summary>
    /// «＋ إضافة» (وInsert): أول حاجة دايمًا «موضوع جديد»، وبعدها المستويات اللي تحت الحاجة المحددة بالترتيب —
    /// موضوع ← مصدر ← كورس ← محاضرة ← فيديوهات.
    /// </summary>
    internal void BuildAddMenu(object? selection, ToolStripItemCollection items)
    {
        // السياق: الموضوع والمصدر والكورس والمحاضرة اللي إنت جواهم
        var group = selection switch { LectureGroup g => g, VideoItem v => _library.FindGroupOf(v), _ => null };
        var course = selection switch { Course c => c, _ when group != null => _library.FindCourseOf(group), _ => null };
        var place = selection switch { Place p => p, _ when course != null => _library.PlaceOf(course), _ => null };
        var topic = selection switch
        {
            TopicDivider d => d.Topic,
            Topic t => t,
            _ when place != null => _library.TopicOf(place),
            _ when course != null => _library.TopicOf(course),
            _ => null
        };

        items.Add(MenuItem("➕  موضوع جديد…", () => { if (topic != null) SelectThen(topic, () => AddTopic()); else AddTopic(); }, "Ctrl+Shift+N"));
        if (topic == null)
        {
            items.Add(CourseSourcesMenu("➕  كورس (من غير موضوع)", null, null));
            return;
        }
        items.Add(Sep());
        items.Add(MenuItem($"➕  مصدر جديد في «{topic.Name}»…", () => AddPlace(topic)));
        if (place != null && _library.ParentOf(place) == null)
            items.Add(MenuItem($"➕  مصدر فرعي في «{place.Name}»…", () => AddPlace(null, parent: place)));
        items.Add(place != null
            ? CourseSourcesMenu($"➕  كورس في «{place.Name}»", null, place)
            : CourseSourcesMenu($"➕  كورس في «{topic.Name}»", topic, null));
        if (course != null)
        {
            items.Add(Sep());
            items.Add(MenuItem($"➕  محاضرة في «{course.Name}»…", () => AddGroup(after: group, course: course)));
            items.Add(MenuItem($"➕  فولدر محاضرات في «{course.Name}»…", () => ImportIntoCourseDialog(course)));
        }
        if (group != null)
        {
            items.Add(Sep());
            items.Add(MenuItem($"➕  فيديوهات من الجهاز في «{group.Name}»…", () => AddVideosDialog(group)));
            items.Add(MenuItem($"➕  فيديوهات من يوتيوب في «{group.Name}»…", () => AddUrlsDialog(null, group)));
            items.Add(MenuItem($"➕  مرفقات في «{group.Name}»…", () => AddAttachmentFiles(group)));
        }
    }
}
