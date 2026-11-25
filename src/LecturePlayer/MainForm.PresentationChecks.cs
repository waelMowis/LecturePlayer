using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// PowerPoint وهمي للفحص (نفس أسماء أعضاء COM اللي «مسار» بيستخدمها) — شباك العرض شباك حقيقي عشان التضمين يتجرب فعلًا.
/// </summary>
internal sealed class FakePowerPoint
{
    public string Version => "16.0-fake";
    public FakePresentations Presentations { get; } = new();
    public bool QuitCalled;
    public void Quit() => QuitCalled = true;

    public sealed class FakePresentations
    {
        public readonly List<FakePres> Open_ = new();
        public int Count => Open_.Count;
        public FakePres Open(string path, int readOnly, int untitled, int withWindow)
        {
            var slides = Pptx.Read(path);
            var p = new FakePres(this, path, slides.Count);
            Open_.Add(p);
            return p;
        }
    }

    public sealed class FakePres
    {
        private readonly FakePresentations _owner;
        public readonly string Path;
        public FakePres(FakePresentations owner, string path, int n) { _owner = owner; Path = path; Slides = new FakeSlides(n); SlideShowSettings = new FakeSettings(this); }
        public FakeSlides Slides { get; }
        public FakeSettings SlideShowSettings { get; }
        public FakePageSetup PageSetup { get; } = new();
        public int CreateVideoStatus { get; private set; }
        public string? VideoPath;
        public void CreateVideo(string path, int useTimings, int seconds, int height, int fps, int quality)
        {
            VideoPath = path; CreateVideoStatus = 1;
            _ = Task.Run(async () => { await Task.Delay(1500); File.WriteAllBytes(path, new byte[2048]); CreateVideoStatus = 3; });
        }
        public void SaveCopyAs(string path) => File.Copy(Path, path, overwrite: true);
        public void Close() => _owner.Open_.Remove(this);
    }
    public sealed class FakePageSetup { public float SlideWidth => 960; public float SlideHeight => 540; }
    public sealed class FakeSlides : System.Collections.IEnumerable
    {
        private readonly List<FakeSlide> _s;
        public FakeSlides(int n) => _s = Enumerable.Range(1, n).Select(i => new FakeSlide(i)).ToList();
        public int Count => _s.Count;
        public FakeSlide this[int i] => _s[i - 1];
        public System.Collections.IEnumerator GetEnumerator() => _s.GetEnumerator();
    }
    public sealed class FakeSlide
    {
        public FakeSlide(int i) { Index = i; }
        public int Index;
        public List<FakeShape> Shapes { get; } = new();
        public FakeTransition SlideShowTransition { get; } = new();
        public void Export(string path, string filter, int w, int h) { using var b = new Bitmap(w, h); b.Save(path); }
    }
    public sealed class FakeShape { public int Type { get; set; } }
    public sealed class FakeTransition { public int Hidden { get; set; } public int AdvanceOnTime { get; set; } public float AdvanceTime { get; set; } public float Duration { get; set; } }
    public sealed class FakeSettings
    {
        private readonly FakePres _p;
        public FakeSettings(FakePres p) => _p = p;
        public int ShowType { get; set; }
        public int RangeType { get; set; }
        public int ShowWithAnimation { get; set; }
        public int ShowWithNarration { get; set; }
        public int AdvanceMode { get; set; }
        public static FakeWindow? Last;
        public FakeWindow Run() => Last = new FakeWindow(_p.Slides.Count);
    }
    public sealed class FakeWindow
    {
        public readonly Form Form;
        public FakeWindow(int n)
        {
            Form = new Form { FormBorderStyle = FormBorderStyle.None, BackColor = Color.FromArgb(25, 113, 194), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), Size = new Size(320, 180) };
            var lbl = new Label { Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 28, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, Text = "شريحة 1" };
            Form.Controls.Add(lbl);
            Form.Show();
            View = new FakeView(n, s => lbl.Text = "شريحة " + s);
        }
        public int HWND => Form.Handle.ToInt32();
        public FakeView View { get; }
    }
    public sealed class FakeView
    {
        private readonly int _n; private readonly Action<int> _show;
        public FakeView(int n, Action<int> show) { _n = n; _show = show; }
        public int CurrentShowPosition { get; private set; } = 1;
        public int State { get; private set; } = 1;
        public int PointerType { get; set; } = 4;
        public FakeColor PointerColor { get; } = new();
        public int Erased;
        public void Next() { if (CurrentShowPosition < _n) _show(++CurrentShowPosition); else State = 5; }
        public void Previous() { if (CurrentShowPosition > 1) _show(--CurrentShowPosition); }
        public void GotoSlide(int i) { CurrentShowPosition = Math.Clamp(i, 1, _n); _show(CurrentShowPosition); }
        public void EraseDrawing() => Erased++;
        public void Exit() => State = 5;
    }
    public sealed class FakeColor { public int RGB { get; set; } }
}

internal partial class MainForm
{
    internal void RunPresentationChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("pptx: " + what); }
        bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(20); } return cond(); }
        if (!HasLibrary) return;
        string dir = Path.Combine(Path.GetTempPath(), "masar-pptx-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "عرض الفحص.pptx");
        Pptx.WriteTest(file, ("مقدمة", "أهلًا", "رحّب بالطلبة", false), ("المتغيرات", "int x;", "", false), ("مخفية", "-", "", true), ("الخلاصة", "شكرًا", "", false));
        var added = new List<Book>();
        var fake = new FakePowerPoint();
        int readSeconds = _settings.ReadSeconds;
        try
        {
            PowerPointShow.TestFactory = () => fake;
            if (!_shelfMode) ToggleShelf();
            var shelf = AddShelf("عروض الفحص", null);
            added.AddRange(AddBooks(new[] { file }, shelf));
            var b = added[0];
            Check(b.IsPresentation && b.PageCount == 4 && b.Chapters.Count == 4 && b.Chapters[1].Title == "المتغيرات" && b.Chapters[2].Title.StartsWith("(مخفية)"), "slides read without PowerPoint: " + string.Join(",", b.Chapters.Select(c => c.Title)));
            Check(BookText.Load(b).Pages[1].Contains("رحّب بالطلبة"), "slide text + notes for search/AI");
            var menu = new ContextMenuStrip(); BuildBookMenu(b, menu.Items);
            Check(menu.Items.OfType<ToolStripMenuItem>().Any(i => i.Text.StartsWith("▶  اعرض")) && menu.Items.OfType<ToolStripMenuItem>().Any(i => i.Text.Contains("إلى فيديو")), "presentation menu");

            _settings.ReadSeconds = 1;
            OpenBook(b);
            Check(PresentationShown && _presPane!.Total == 4, "presentation started");
            Check(_presPane.Embedded, "slideshow window embedded in Masar");
            var win = FakePowerPoint.FakeSettings.Last!;
            Check(win.Form.Parent == null && GetParentHandle(win.Form.Handle) == FindChildHost(_presPane), "real SetParent");
            Check(Until(() => b.LastPage == 1 && b.Read.Contains(1), 4000), "slide 1 viewed after read time");
            win.View.Next();
            Check(Until(() => b.LastPage == 2, 3000), "next slide tracked");
            // أدوات الشرح فوق الشريحة: فوق مساحة العرض بالظبط، وكل شريحة ليها رسمها
            _presPane.ToggleInk();
            var ov = _presPane.InkOverlay;
            Check(ov != null && ov.Visible && ov.Overlay && ov.Owner == this, "ink overlay over the slide");
            Until(() => false, 300);
            ov!.AddStroke(ScreenInk.Tool.Pen, Color.Red, 6, new PointF(40, 40), new PointF(200, 120));
            Check(ov.Count == 1, "stroke on slide 2");
            win.View.Next();
            Check(Until(() => b.LastPage == 3, 3000) && Until(() => ov.Count == 0, 1500), "slide 3 starts clean");
            win.View.Previous();
            Check(Until(() => b.LastPage == 2, 3000) && Until(() => ov.Count == 1, 1500), "slide 2 drawing comes back");
            Until(() => false, 400);
            SaveSmokeShot(this, "pptx");
            _presPane.ToggleInk();
            Check(_presPane.InkOverlay == null, "ink overlay closed");

            // فتحت حاجة تانية (سبورة/كتاب): العرض يستخبى بس، والرجوع فوري من نفس الشريحة ومن غير ما يتفتح تاني
            int opens = fake.Presentations.Open_.Count;
            StopPlaybackCore(clearCurrent: false);
            Check(!PresentationShown && _presPane.Running && !_presPane.Visible, "presentation suspended, PowerPoint still running");
            OpenBook(b);
            Check(PresentationShown && _presPane.Visible && fake.Presentations.Open_.Count == opens && b.LastPage == 2 && _presPane.Current == 2, "resumed instantly on the same slide (no reopen)");
            _presPane.Show!.Pointer(PowerPointShow.PointerPen, Color.Red);
            Check(win.View.PointerType == PowerPointShow.PointerPen && win.View.PointerColor.RGB == 0x0000FF, "PowerPoint pen + colour (BGR)");
            // كتابة اتحفظت في الشريحة 2 ← نسخة مشروحة
            var pres = fake.Presentations.Open_[0];
            pres.Slides[2].Shapes.Add(new FakePowerPoint.FakeShape { Type = 22 });
            EndPresentation(showHome: true);
            Check(!PresentationShown && _presPane.Visible == false, "presentation closed");
            var copy = AnnotatedCopyOf(b);
            Check(copy != null && File.Exists(copy.LocalPath) && copy.LocalPath.EndsWith("— مشروح.pptx"), "annotated pptx copy saved: " + copy?.LocalPath);
            added.Add(copy!);
            Check(fake.Presentations.Count == 0, "presentation closed in PowerPoint");

            // ---------- أمر «عرض PowerPoint» المباشر: من قايمة الإضافة في «مكتبتي»، بيروح لرف «عروضي» ويتفتح ----------
            var addItems = new ContextMenuStrip(); BuildAddMenu(null, addItems.Items);
            Check(addItems.Items.OfType<ToolStripMenuItem>().Any(i => i.Text.Contains("عرض PowerPoint")) && addItems.Items.OfType<ToolStripMenuItem>().Any(i => i.Text.Contains("كتاب (PDF")), "library add menu has single book + PowerPoint");
            string file2 = Path.Combine(dir, "عرض تاني.pptx");
            Pptx.WriteTest(file2, ("أول", "١", "", false), ("تاني", "٢", "", false));
            SmokePresentationFiles = new[] { file2 };
            try { AddPresentationsDialog(null); } finally { SmokePresentationFiles = null; }
            var b2 = _library.Books.FirstOrDefault(x => x.Path == Path.GetFullPath(file2));
            Check(b2 != null && _library.ShelfOf(b2)?.Kind == "presentations" && _library.ShelfOf(b2)!.Name == "عروضي", "presentation added to «عروضي»");
            Check(PresentationShown && _presBook == b2, "single presentation opens right away");
            added.Add(b2!);
            EndPresentation(showHome: true);

            // ---------- فيديو ----------
            SmokeVideoChoice = (720, 3, false);
            ConvertPresentationToVideo(b);
            Check(Until(() => _library.Courses.Any(c => c.Name == "العروض التقديمية" && c.Groups.Any(g => g.Name == b.Title && g.Videos.Count == 1)), 15_000), "video added to a course");
            var g = _library.Courses.First(c => c.Name == "العروض التقديمية").Groups.First(x => x.Name == b.Title);
            var v = g.Videos[0];
            Check(v.Bookmarks.Count == 4 && v.Bookmarks[1].TimeMs == 3000 && v.Bookmarks[1].Note == "المتغيرات", "bookmark per slide: " + string.Join(",", v.Bookmarks.Select(x => x.TimeMs + ":" + x.Note)));
            Console.WriteLine("SMOKE pptx: ok");
            _library.Courses.RemoveAll(c => c.Name == "العروض التقديمية");
            try { File.Delete(v.Path); } catch { }
        }
        finally
        {
            FinishSuspendedPresentation();
            if (PresentationShown) EndPresentation(showHome: true);
            PowerPointShow.TestFactory = null;
            SmokeVideoChoice = null;
            _settings.ReadSeconds = readSeconds;
            foreach (var x in added) { if (x.AnnotatedOf != null) try { File.Delete(x.LocalPath); } catch { } _library.Books.Remove(x); }
            _library.Shelves.RemoveAll(s => s.Name == "عروض الفحص" || s.Kind == "presentations");
            _library.Shelves.RemoveAll(s => s.Kind == AnnotatedShelfKind && !_library.Books.Any(bk => bk.ShelfId == s.Id));
            foreach (var x in added) try { File.Delete(BookText.PathFor(x)); } catch { }
            RebuildTree();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr h);
    private static IntPtr GetParentHandle(IntPtr h) => GetParent(h);
    private static IntPtr FindChildHost(Control pane) => pane.Controls.OfType<Panel>().First(p => p.Dock == DockStyle.Fill).Handle;
}
