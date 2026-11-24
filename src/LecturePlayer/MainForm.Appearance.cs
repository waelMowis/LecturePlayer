using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// أيقونة ولون مخصصين للموضوع/المصدر/الكورس — عشان تفرّق بينهم بنظرة في الشجرة.
/// بيتحفظوا في ملف المكتبة (بيتنقلوا مع المكتبة وبيتزامنوا بين الأجهزة)، ومن غيرهم كل حاجة زي ما هي.
/// </summary>
internal sealed partial class MainForm
{
    /// <summary>
    /// مفتاح صورة العقدة في الشجرة. من غير تخصيص = الأيقونات العادية (topic / topicDone …)؛
    /// مع تخصيص بتترسم مرة واحدة لكل (شكل، لون، خلص) وبتتخزن في ImageList الشجرة.
    /// </summary>
    private string NodeImageKey(string kind, string? icon, string? colorHex, bool done)
    {
        bool hasIcon = IconGlyphs.Exists(icon);
        var color = IconGlyphs.ParseHex(colorHex);
        if (!hasIcon && color == null) return done ? kind + "Done" : kind;
        var c = color ?? Theme.DefaultKindColor(kind);
        string key = $"x|{kind}|{(hasIcon ? icon : "")}|{IconGlyphs.ToHex(c)}|{(done ? 1 : 0)}";
        var il = _tree.ImageList;
        if (il != null && !il.Images.ContainsKey(key))
            il.Images.Add(key, Theme.NodeIcon(il.ImageSize.Width, kind, hasIcon ? icon : null, c, done));
        return key;
    }

    /// <summary>لون الاسم لو ليه لون مخصص (متفتّح لو غامق عشان يتقري)؛ null = اللون العادي.</summary>
    private static Color? CustomNameColor(string? colorHex) =>
        IconGlyphs.ParseHex(colorHex) is { } c ? IconGlyphs.Readable(c) : null;

    private ToolStripMenuItem AppearanceMenuItem(object item) => MenuItem("🎨  الأيقونة واللون…", () => EditAppearance(item));

    private void EditAppearance(object item)
    {
        if (!EnsureLibrary()) return;
        (string kind, string name, string? icon, string? color, string? children) = item switch
        {
            Topic t => ("topic", t.Name, t.IconKey, t.ColorHex,
                _library.PlacesOf(t).Any() || _library.CoursesOf(t).Any() ? "نفس اللون للمصادر والكورسات اللي جوه الموضوع" : null),
            Place p => ("place", p.Name, p.IconKey, p.ColorHex,
                _library.ChildrenOf(p).Any() || _library.AllCoursesUnder(p).Any() ? "نفس اللون للكورسات (والمصادر الفرعية) اللي جوه المصدر" : null),
            Course c => ("course", c.Name, c.IconKey, c.ColorHex, (string?)null),
            _ => ("", "", null, null, null)
        };
        if (kind.Length == 0) return;
        var r = AppearanceForm.Show(this, kind, name, icon, color, children);
        if (r != null) ApplyAppearance(item, r.Icon, r.ColorHex, r.ApplyToChildren);
    }

    /// <summary>بيطبق الأيقونة واللون (وممكن اللون على اللي تحته). بيرجع عدد العناصر اللي اتغيرت.</summary>
    internal int ApplyAppearance(object item, string? icon, string? colorHex, bool applyToChildren)
    {
        if (!IconGlyphs.Exists(icon)) icon = null;
        colorHex = IconGlyphs.ParseHex(colorHex) is { } col ? IconGlyphs.ToHex(col) : null;

        // اللي هيتغير: العنصر نفسه (أيقونة + لون) واللي تحته (لون بس)
        var self = item switch
        {
            Topic t => (Get: (Func<(string?, string?)>)(() => (t.IconKey, t.ColorHex)), Set: (Action)(() => { t.IconKey = icon; t.ColorHex = colorHex; t.TouchInfo(); })),
            Place p => (Get: () => (p.IconKey, p.ColorHex), Set: () => { p.IconKey = icon; p.ColorHex = colorHex; p.TouchInfo(); }),
            Course c => (Get: () => (c.IconKey, c.ColorHex), Set: () => { c.IconKey = icon; c.ColorHex = colorHex; c.TouchInfo(); }),
            _ => default
        };
        if (self.Get == null) return 0;

        var places = new List<Place>();
        var courses = new List<Course>();
        if (applyToChildren)
        {
            if (item is Topic tt) { places.AddRange(_library.PlacesOf(tt)); courses.AddRange(_library.CoursesOf(tt)); }
            else if (item is Place pp) { places.AddRange(_library.ChildrenOf(pp)); courses.AddRange(_library.AllCoursesUnder(pp)); }
        }
        bool selfChanged = self.Get() != (icon, colorHex);
        places.RemoveAll(p => p.ColorHex == colorHex);
        courses.RemoveAll(c => c.ColorHex == colorHex);
        if (!selfChanged && places.Count == 0 && courses.Count == 0) return 0;

        Checkpoint("الأيقونة واللون");
        if (selfChanged) self.Set();
        foreach (var p in places) { p.ColorHex = colorHex; p.TouchInfo(); }
        foreach (var c in courses) { c.ColorHex = colorHex; c.TouchInfo(); }
        MarkDirty();
        RebuildTree();
        SelectItem(item);
        int n = (selfChanged ? 1 : 0) + places.Count + courses.Count;
        ShowStatus(icon == null && colorHex == null
            ? "رجعت الأيقونة واللون العاديين" + (n > 1 ? $" ({n - 1} تحته كمان)" : "")
            : "اتغيرت الأيقونة واللون" + (places.Count + courses.Count > 0 ? $" — واللون اتطبق على {places.Count + courses.Count} تحته" : ""), 5);
        return n;
    }

    /// <summary>صورة من شاشة في فولدر smoke-shots (CI بيرفعها كـ artifact). أي فشل بيتجاهل.</summary>
    internal static void SaveSmokeShot(Control c, string name)
    {
        try
        {
            string dir = Path.Combine(Environment.CurrentDirectory, "smoke-shots");
            Directory.CreateDirectory(dir);
            var r = c is Form f ? f.Bounds : c.RectangleToScreen(c.ClientRectangle);
            using (var screen = new Bitmap(r.Width, r.Height))
            {
                using (var g = Graphics.FromImage(screen)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                screen.Save(Path.Combine(dir, name + "-screen.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            using var bmp = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(bmp, new Rectangle(Point.Empty, c.Size));
            bmp.Save(Path.Combine(dir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            // في السجل كمان (base64 مقطّع) عشان تتراجع من غير تحميل الـ artifact
            foreach (var file in new[] { name + "-screen.png", name + ".png" })
            {
                string b64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(dir, file)));
                for (int i = 0, k = 0; i < b64.Length; i += 3000, k++)
                    Console.WriteLine($"SMOKESHOT {file} {k} {b64.Substring(i, Math.Min(3000, b64.Length - i))}");
            }
        }
        catch (Exception ex) { Console.WriteLine("SMOKE shot " + name + " failed: " + ex.Message); }
    }

    // =====================================================================
    //  فحص تلقائي
    // =====================================================================

    /// <summary>
    /// الأيقونات كلها بتترسم صح، والتخصيص بيظهر في الشجرة، وبيتحفظ ويرجع بعد الفتح، وبيتزامن بين نسختين،
    /// و«تراجع» بيرجّعه، و«طبّق على اللي تحته» بيلوّن اللي تحته بس، والشاشة نفسها (البحث والاختيار والحفظ).
    /// </summary>
    internal void RunAppearanceChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("appearance: " + what); }

        // 1) كل الأيقونات: مسار سليم جوه المربع، وبيترسم فيه ملء معقول (مش فاضي ومش مربع مليان)
        Check(IconGlyphs.All.Length >= 320, "icon count " + IconGlyphs.All.Length);
        Check(IconGlyphs.All.Select(x => x.Key).Distinct().Count() == IconGlyphs.All.Length, "duplicate keys");
        foreach (var (_, key, label, _) in IconGlyphs.All)
        {
            var path = IconGlyphs.PathOf(key) ?? throw new Exception("appearance: no path " + key);
            var b = path.GetBounds();
            // الشعارات العريضة (.NET، Go) أو الطويلة (MongoDB) بتملى البُعد الكبير بس
            Check(Math.Max(b.Width, b.Height) > 0.6f && Math.Min(b.Width, b.Height) > 0.2f
                  && b.Left > -0.05f && b.Top > -0.05f && b.Right < 1.05f && b.Bottom < 1.05f, $"bounds {key}: {b}");
            Check(label.Length > 0, "label " + key);
            using var bmp = Theme.NodeIcon(24, "course", key, Color.Red, done: false);
            int filled = 0;
            for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++) if (bmp.GetPixel(x, y).A > 128) filled++;
            // الشعارات الرفيعة (Java، SQL Server) بتغطي حوالي 6% بس
            Check(filled > 24 * 24 * 0.035 && filled < 24 * 24 * 0.92, $"fill {key}: {filled}");
        }
        // الفئات الجديدة والشعارات الأساسية موجودة، ولون العلامة للشعارات بس
        foreach (var cat in new[] { "لغات برمجة", "فريموركات ومكتبات", "قواعد بيانات وسحابة وأنظمة", "أدوات ومحررات", "منصات ومواقع" })
            Check(IconGlyphs.Categories.Contains(cat), "category " + cat);
        foreach (var k in new[] { "csharp", "dotnet", "python", "javascript", "java", "sqlserver", "visualstudio", "vscode", "github", "youtube", "udemy", "backpack", "stethoscope" })
            Check(IconGlyphs.Exists(k), "icon " + k);
        Check(IconGlyphs.BrandColor("csharp") is { } cs && cs.GetBrightness() > 0.3f && IconGlyphs.BrandColor("code") == null, "brand color");
        // evenodd («!»): مربع جواه مربع = حلقة فاضية من جوه
        using (var ring = IconGlyphs.Parse("!M0 0L1 0L1 1L0 1ZM0.25 0.25L0.75 0.25L0.75 0.75L0.25 0.75Z"))
            Check(ring.FillMode == System.Drawing.Drawing2D.FillMode.Alternate && !ring.IsVisible(0.5f, 0.5f) && ring.IsVisible(0.1f, 0.1f), "evenodd");
        using (var solid = IconGlyphs.Parse("M0 0L1 0L1 1L0 1ZM0.25 0.25L0.75 0.25L0.75 0.75L0.25 0.75Z"))
            Check(solid.FillMode == System.Drawing.Drawing2D.FillMode.Winding && solid.IsVisible(0.5f, 0.5f), "nonzero");
        Check(IconGlyphs.ParseHex("#E86870") == Color.FromArgb(232, 104, 112) && IconGlyphs.ParseHex("xyz") == null, "hex parse");
        Check(IconGlyphs.Readable(Color.FromArgb(20, 20, 60)).GetBrightness() > 0.45f, "dark color lightened");

        // 2) مكتبة: موضوع ← مصدر ← مصدر فرعي، وكورسات في كل مستوى، وموضوع تاني مايتأثرش
        string dir = Path.Combine(Path.GetTempPath(), "masar-smoke-appearance");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var lib = new CourseLibrary { Name = "appearance" };
        var t = new Topic { Name = "Backend" };
        var other = new Topic { Name = "Languages" };
        lib.Topics.AddRange(new[] { t, other });
        var p = new Place { Name = "ROUTE", TopicId = t.Id };
        var sub = new Place { Name = "Eng Nada", TopicId = t.Id, ParentId = p.Id };
        lib.Places.AddRange(new[] { p, sub });
        Course NewCourse(string name, Topic topic, Place? place)
        {
            var c = new Course { Name = name, TopicId = topic.Id, PlaceId = place?.Id };
            var g = new LectureGroup { Name = "L1" };
            g.Videos.Add(new VideoItem { Path = Path.Combine(dir, name + ".mp4"), Title = "v" });
            c.Groups.Add(g);
            lib.Courses.Add(c);
            return c;
        }
        NewCourse("InRoute", t, p); NewCourse("InNada", t, sub); NewCourse("Direct", t, null); NewCourse("English", other, null);
        string path1 = Path.Combine(dir, "appearance.lpl");
        Storage.SaveLibrary(path1, lib);
        LoadLibrary(path1, remember: false);
        Topic T() => _library.Topics.Single(x => x.Name == "Backend");
        Place P(string n) => _library.Places.Single(x => x.Name == n);
        Course C(string n) => _library.Courses.Single(x => x.Name == n);
        TreeNode N(object o) => NodeOf(o) ?? throw new Exception("appearance: not in tree " + o);

        // من غير تخصيص: الأيقونات العادية زي ما هي
        Check(N(T()).ImageKey == "topic" && N(P("ROUTE")).ImageKey == "place" && N(C("Direct")).ImageKey == "course", "defaults unchanged");

        // 3) أيقونة بس لكورس
        Check(ApplyAppearance(C("Direct"), "code", null, false) == 1, "apply course icon");
        var directKey = N(C("Direct")).ImageKey;
        Check(directKey.StartsWith("x|course|code|") && directKey.EndsWith("|0") && _tree.ImageList!.Images.ContainsKey(directKey), "course icon key " + directKey);
        Check(N(C("Direct")).ForeColor == Theme.Text, "icon only keeps name color");

        // 4) أيقونة ولون للموضوع + اللون على اللي تحته
        int changed = ApplyAppearance(T(), "database", "#e86870", true);
        Check(changed == 6, "topic + 2 sources + 3 courses, got " + changed);
        Check(T().IconKey == "database" && T().ColorHex == "#E86870", "topic saved normalized");
        Check(P("ROUTE").ColorHex == "#E86870" && P("Eng Nada").ColorHex == "#E86870" && C("InNada").ColorHex == "#E86870", "children colored");
        Check(C("Direct").IconKey == "code" && P("ROUTE").IconKey == null, "children keep their own icons");
        Check(C("English").ColorHex == null && _library.Topics.Single(x => x.Name == "Languages").ColorHex == null, "other topic untouched");
        Check(N(T()).ImageKey.StartsWith("x|topic|database|#E86870"), "topic key " + N(T()).ImageKey);
        Check(N(T()).ForeColor == IconGlyphs.Readable(Color.FromArgb(232, 104, 112)), "topic name tinted");
        Check(N(P("ROUTE")).ImageKey.StartsWith("x|place||#E86870"), "colored default place shape " + N(P("ROUTE")).ImageKey);
        Check(ApplyAppearance(T(), "database", "#E86870", true) == 0, "no-op apply should not checkpoint");

        // 5) تراجع
        UndoLast();
        Check(T().IconKey == null && T().ColorHex == null && C("InRoute").ColorHex == null && C("Direct").IconKey == "code", "undo");
        Check(N(T()).ImageKey == "topic", "undo tree");

        // 6) الشاشة: بحث، واختيار أيقونة ولون، وتطبيق على اللي تحته
        try
        {
            AppearanceForm.SmokePick = ("bug", 3, true, "كود");
            AppearanceForm.SmokeShot = f => SaveSmokeShot(f, "appearance-dialog");
            EditAppearance(P("ROUTE"));
            AppearanceForm.SmokeShot = null;
            Check(AppearanceForm.SmokeTiles == IconGlyphs.All.Length + 1, "tiles " + AppearanceForm.SmokeTiles);
            Check(AppearanceForm.SmokeVisibleAfterSearch >= 1 && AppearanceForm.SmokeVisibleAfterSearch < 10, "search shown " + AppearanceForm.SmokeVisibleAfterSearch);
            string hex = IconGlyphs.ToHex(IconGlyphs.Palette[3].Color);
            Check(P("ROUTE").IconKey == "bug" && P("ROUTE").ColorHex == hex, "dialog applied to source");
            Check(P("Eng Nada").ColorHex == hex && C("InRoute").ColorHex == hex && C("InNada").ColorHex == hex, "dialog children");
            Check(C("Direct").ColorHex == null && T().ColorHex == null, "dialog did not touch siblings/parent");

            // شعار C#: البحث بالعربي بيلاقيه، واللون الافتراضي بيبقى لون العلامة لوحده
            AppearanceForm.SmokePick = ("csharp", -2, false, "سي شارب");
            AppearanceForm.SmokeShot = f => SaveSmokeShot(f, "appearance-brands");
            EditAppearance(C("Direct"));
            AppearanceForm.SmokeShot = null;
            Check(AppearanceForm.SmokeVisibleAfterSearch == 1, "search c# shown " + AppearanceForm.SmokeVisibleAfterSearch);
            Check(C("Direct").IconKey == "csharp" && C("Direct").ColorHex == IconGlyphs.ToHex(IconGlyphs.BrandColor("csharp")!.Value),
                "brand icon + brand color " + C("Direct").ColorHex);
            AppearanceForm.SmokePick = ("python", -2, false, "C#");
            EditAppearance(C("Direct"));
            Check(AppearanceForm.SmokeVisibleAfterSearch == 1, "search 'C#' shown " + AppearanceForm.SmokeVisibleAfterSearch);
            Check(C("Direct").IconKey == "python" && C("Direct").ColorHex == IconGlyphs.ToHex(IconGlyphs.BrandColor("python")!.Value),
                "switching brand follows brand color " + C("Direct").ColorHex);
            AppearanceForm.SmokePick = ("bug", -2, false, null);
            EditAppearance(C("Direct"));
            Check(C("Direct").IconKey == "bug" && C("Direct").ColorHex == null, "plain icon drops auto brand color " + C("Direct").ColorHex);
            ApplyAppearance(C("Direct"), "code", null, false);

            // «رجّع الافتراضي» للمصدر واللي تحته
            AppearanceForm.SmokePick = (null, -1, true, null);
            EditAppearance(P("ROUTE"));
            Check(P("ROUTE").IconKey == null && P("ROUTE").ColorHex == null && C("InNada").ColorHex == null, "reset");
        }
        finally { AppearanceForm.SmokePick = null; }

        // 7) خلص ← علامة ✓ مع اللون (مش أيقونة خضرا)
        ApplyAppearance(C("InRoute"), "rocket", "#4FC3F7", false);
        foreach (var v in C("InRoute").AllVideos) SetWatched(v, true);
        RebuildTree();
        Check(C("InRoute").IsCompleted && N(C("InRoute")).ImageKey == "x|course|rocket|#4FC3F7|1", "done badge key " + N(C("InRoute")).ImageKey);

        // 8) القايمة فيها الأمر للتلاتة
        foreach (var o in new object[] { T(), P("ROUTE"), C("Direct") })
        {
            ShowNodeMenu(N(o), Point.Empty);
            bool has = MenuHas(_nodeMenu.Items, "الأيقونة واللون");
            _nodeMenu.Close();
            Check(has, "menu item for " + o.GetType().Name);
        }

        // 9) الحفظ والفتح تاني
        ApplyAppearance(T(), "hat_graduation", "#B180D7", false);
        SaveLibrary();
        LoadLibrary(path1, remember: false);
        Check(T().IconKey == "hat_graduation" && T().ColorHex == "#B180D7" && C("Direct").IconKey == "code" && C("InRoute").IconKey == "rocket",
            "persisted after reopen");
        Check(N(T()).ImageKey.StartsWith("x|topic|hat_graduation|"), "tree after reopen");

        // صورة للشجرة بعد التلوين (للمراجعة بالعين)
        ApplyAppearance(P("ROUTE"), "building", "#56B6C2", true);
        ApplyAppearance(P("Eng Nada"), "person", "#F078AA", true);
        ApplyAppearance(_library.Topics.Single(x => x.Name == "Languages"), "translate", "#E8C46E", true);
        ExpandToLevel(4);
        Application.DoEvents();
        SaveSmokeShot(this, "appearance-tree");

        // 10) التزامن بين جهازين: التعديل الأحدث بيكسب
        var remote = Storage.LoadLibrary(path1);
        var rt = remote.Topics.Single(x => x.Name == "Backend");
        rt.IconKey = "translate"; rt.ColorHex = "#7EC878"; rt.InfoUpdatedUtc = DateTime.UtcNow.AddMinutes(5);
        var rc = remote.Courses.Single(x => x.Name == "English");
        rc.IconKey = "mic"; rc.ColorHex = "#F09C5A"; rc.InfoUpdatedUtc = DateTime.UtcNow.AddMinutes(5);
        var rp = remote.Places.Single(x => x.Name == "ROUTE");
        rp.IconKey = "server"; rp.InfoUpdatedUtc = DateTime.UtcNow.AddMinutes(5);
        LibrarySync.Merge(_library, remote);
        Check(T().IconKey == "translate" && T().ColorHex == "#7EC878", "sync topic");
        Check(C("English").IconKey == "mic" && C("English").ColorHex == "#F09C5A", "sync course");
        Check(P("ROUTE").IconKey == "server", "sync place");

        // 11) أيقونة اتشالت من نسخة أحدث / مفتاح مش معروف: بيرجع للعادي من غير مايقع
        C("English").IconKey = "no_such_icon";
        RebuildTree();
        Check(N(C("English")).ImageKey.StartsWith("x|course||#F09C5A"), "unknown icon falls back to kind shape");
        _dirty = false;
        Console.WriteLine($"SMOKE appearance OK ({IconGlyphs.All.Length} icons)");
    }
}
