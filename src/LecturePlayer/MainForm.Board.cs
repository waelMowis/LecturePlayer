using System.Drawing.Imaging;
using System.Text;
using System.Text.Json.Nodes;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// السبورة البيضاء: كشكول صفحات في «سبوراتي» (أو مربوط بمحاضرة) — بتتفتح في القارئ والأقلام جاهزة.
/// ملف السبورة = الصفحات (مقاس/خلفية/اسم)، والرسم في فولدر الشرح زي الكتب (فبيتزامن ويتدمج بين الأجهزة).
/// </summary>
internal partial class MainForm
{
    private const string BoardsShelfKind = "boards";
    private const string BoardsFolderName = "سبوراتي";
    private string BoardsDir => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_libraryPath)) ?? ".", BoardsFolderName);
    private string? _lastPickSave;

    private static readonly (string Key, int W, int H, string Name)[] BoardSizes =
        { ("wide", 960, 540, "شاشة عريضة 16:9"), ("a4l", 842, 595, "A4 عرضي"), ("a4p", 595, 842, "A4 طولي"), ("long", 595, 2520, "صفحة طويلة") };
    private static readonly (string Key, string Name)[] BoardBgs = { ("white", "أبيض"), ("cream", "ورق"), ("black", "أسود"), ("green", "أخضر (طباشير)") };
    private static readonly (string Key, string Name)[] BoardPats = { ("grid", "مربعات"), ("none", "فارغة"), ("lines", "سطور"), ("dots", "نقاط") };

    private Shelf BoardsShelf()
    {
        var s = _library.Shelves.FirstOrDefault(x => x.Kind == BoardsShelfKind);
        if (s != null) return s;
        s = AddShelf(BoardsFolderName, null, checkpoint: false);
        s.Kind = BoardsShelfKind;
        s.IconKey = "whiteboard";
        s.TouchInfo();
        return s;
    }

    /// <summary>للفحص: اختيارات السبورة الجديدة من غير شباك.</summary>
    internal (string Name, int Size, int Bg, int Pat)? SmokeBoardChoice;

    /// <summary>سبورة جديدة (من «إضافة» أو من محاضرة).</summary>
    internal Book? NewBoardDialog(LectureGroup? lecture = null, Shelf? shelf = null)
    {
        if (!EnsureLibrary()) return null;
        string name = lecture != null ? "سبورة — " + lecture.Name : "سبورة " + DateTime.Now.ToString("d MMMM");
        int size = 0, bg = 0, pat = 0;
        if (SmokeBoardChoice is { } sc) (name, size, bg, pat) = sc;
        else
        {
            using var kit = new FormKit(lecture != null ? "سبورة للمحاضرة" : "سبورة جديدة", 520);
            var nameBox = kit.Row("الاسم", FormKit.Style(new TextBox { Text = name, RightToLeft = RightToLeft.Yes }));
            var sizeBox = kit.Row("المقاس", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
            foreach (var x in BoardSizes) sizeBox.Items.Add($"{x.Name}");
            sizeBox.SelectedIndex = 0;
            var bgBox = kit.Row("الخلفية", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
            foreach (var x in BoardBgs) bgBox.Items.Add(x.Name);
            bgBox.SelectedIndex = 0;
            var patBox = kit.Row("النقشة", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
            foreach (var x in BoardPats) patBox.Items.Add(x.Name);
            patBox.SelectedIndex = 0;
            kit.Note("يمكنك تغيير المقاس والخلفية لكل صفحة لاحقًا، وإضافة صفحات وقوالب وصفحات PDF ولقطات شاشة.", 40, Theme.Muted);
            kit.Buttons("افتح السبورة");
            if (SmokeShowDialog(kit, "board-new")) return null;
            if (kit.Show(this) != DialogResult.OK) return null;
            name = string.IsNullOrWhiteSpace(nameBox.Text) ? name : nameBox.Text.Trim();
            size = sizeBox.SelectedIndex; bg = bgBox.SelectedIndex; pat = patBox.SelectedIndex;
        }
        var (_, w, h, _) = BoardSizes[Math.Clamp(size, 0, BoardSizes.Length - 1)];
        var meta = new JsonObject
        {
            ["v"] = 1, ["title"] = name, ["u"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["pages"] = new JsonArray(new JsonObject
            {
                ["id"] = "p" + Guid.NewGuid().ToString("N")[..10], ["w"] = w, ["h"] = h,
                ["bg"] = BoardBgs[Math.Clamp(bg, 0, BoardBgs.Length - 1)].Key, ["pat"] = BoardPats[Math.Clamp(pat, 0, BoardPats.Length - 1)].Key, ["name"] = ""
            })
        };
        if (lecture != null) meta["lecture"] = lecture.Id;
        string safe = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        if (safe.Length > 80) safe = safe[..80];
        Directory.CreateDirectory(BoardsDir);
        string file = Path.Combine(BoardsDir, (safe.Length > 0 ? safe : "سبورة") + BookFiles.BoardExtension);
        for (int i = 2; File.Exists(file); i++) file = Path.Combine(BoardsDir, $"{safe} ({i}){BookFiles.BoardExtension}");
        File.WriteAllText(file, meta.ToJsonString(), new UTF8Encoding(false));
        Checkpoint("سبورة جديدة");
        var b = new Book { Title = name, Path = file, LocalPath = file, ShelfId = (shelf ?? BoardsShelf()).Id, IconKey = "whiteboard", PageCount = 1, LectureId = lecture?.Id };
        b.TouchInfo();
        _library.Books.Add(b);
        MarkDirty();
        RebuildShelf();
        if (lecture != null && _groupNodes.TryGetValue(lecture, out var gn)) UpdateGroupNode(gn, lecture);
        OpenBook(b);
        return b;
    }

    private Book? BoardOf(LectureGroup g) => _library.Books.FirstOrDefault(b => b.LectureId == g.Id && b.IsBoard);

    /// <summary>من قائمة المحاضرة: سبورتها (أو سبورة جديدة ليها).</summary>
    internal void OpenLectureBoard(LectureGroup g)
    {
        if (BoardOf(g) is { } b && File.Exists(b.LocalPath)) { OpenBook(b); return; }
        NewBoardDialog(g);
    }

    /// <summary>الصفحات اتغيرت: الملف بيتكتب (آمن) وعدد الصفحات بيتحدّث.</summary>
    private void OnBoardChanged(int gen, string json)
    {
        var b = _inkByGen.TryGetValue(gen, out var s) ? s.Book : (gen == _readerGen ? _currentBook : null);
        if (b == null || !b.IsBoard) return;
        try
        {
            var meta = JsonNode.Parse(json) as JsonObject;
            if (meta?["pages"] is not JsonArray pages || pages.Count == 0) return;
            string file = b.LocalPath, tmp = file + ".tmp";
            File.WriteAllText(tmp, meta.ToJsonString(), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(tmp, file, null, ignoreMetadataErrors: true); else File.Move(tmp, file);
            if (b.PageCount != pages.Count) { b.PageCount = pages.Count; b.TouchInfo(); MarkPositionDirty(); if (_shelfMode) RefreshBookRows(b); }
        }
        catch (Exception ex) { Log.Error("board save", ex); ShowBanner("تعذّر حفظ صفحات السبورة: " + ex.Message, BannerKind.Warning, seconds: 8); }
    }

    /// <summary>لقطة شاشة ← صفحة جديدة في السبورة («مسار» بيستخبى ثانية عشان مايطلعش في الصورة).</summary>
    private async void BoardScreenshot()
    {
        if (!(ReaderShown && _currentBook is { IsBoard: true }) || _reader == null) return;
        var screen = Screen.FromControl(this);
        var prev = Opacity;
        try
        {
            if (!SmokeTest.Active) { Opacity = 0; await Task.Delay(250); }
            using var bmp = new Bitmap(screen.Bounds.Width, screen.Bounds.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(screen.Bounds.Location, Point.Empty, screen.Bounds.Size);
            using var ms = new MemoryStream();
            // صورة كبيرة: JPEG (الحجم في ملف الشرح)
            var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var ps = new EncoderParameters(1) { Param = { [0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L) } };
            using var scaled = bmp.Width > 2200 ? new Bitmap(bmp, 2200, bmp.Height * 2200 / bmp.Width) : (Bitmap)bmp.Clone();
            scaled.Save(ms, enc, ps);
            await _reader.BoardImageAsync("data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray()), "لقطة " + DateTime.Now.ToString("HH:mm"));
        }
        catch (Exception ex) { Log.Error("board shot", ex); ShowStatus("تعذّر التقاط الشاشة", 4); }
        finally { Opacity = prev; Activate(); }
    }

    /// <summary>أوامر السبورة من القارئ.</summary>
    private void OnBoardCommand(string name)
    {
        switch (name)
        {
            case "board-shot": BoardScreenshot(); break;
            case "board-busy": ShowStatus("📤 جارٍ تصدير السبورة…", 20); break;
            case "board-exported":
                if (_lastPickSave is { } p) ShowBanner("📤 صُدِّرت: " + Path.GetFileName(p), BannerKind.Success, "افتح مكانه", () => OpenInExplorer(p), seconds: 10);
                break;
            case "board-send":
                if (_lastPickSave is { } f && File.Exists(f))
                {
                    try { var list = new System.Collections.Specialized.StringCollection { f }; Clipboard.SetFileDropList(list); } catch (Exception ex) { Log.Info("clipboard: " + ex.Message); }
                    ShowBanner("📨 ملف السبورة جاهز ونُسخ — الصقه (Ctrl+V) في واتساب أو تيمز أو البريد للطلاب", BannerKind.Success, "افتح مكانه", () => OpenInExplorer(f), seconds: 15);
                }
                break;
            case "board-failed": ShowStatus("فشل التصدير", 4); break;
        }
    }

    /// <summary>قائمة السبورة (بدل قائمة الكتاب).</summary>
    private void BuildBoardMenu(Book b, ToolStripItemCollection items)
    {
        items.Add(MenuItem("▶  افتح السبورة", () => OpenBook(b), "Enter"));
        items.Add(MenuItem("🖊  افتح والأقلام جاهزة", () => OpenBook(b)));
        if (b.LectureId != null && _library.AllGroups.FirstOrDefault(g => g.Id == b.LectureId) is { } lec)
            items.Add(MenuItem($"▶  شغّل المحاضرة «{lec.Name}»", () => PlayGroup(lec)));
        items.Add(Sep());
        items.Add(MenuItem("➕  سبورة جديدة…", () => NewBoardDialog(null, _library.ShelfOf(b))));
        items.Add(Sep());
        items.Add(MenuItem("✏  إعادة تسمية…", () => RenameShelfItem(b), "F2"));
        items.Add(AppearanceMenuItem(b));
        items.Add(Sep());
        items.Add(ShelfMoveMenu(b));
        if (b.InkCount > 0) items.Add(SubMenuOf("↺  نسخ الشرح (حفظ/رجوع)", InkVersionItems(b)));
        items.Add(SubMenuOf("📤  صدّر",
            MenuItem("📤  كل الصفحات PDF…", () => BoardExportFromMenu(b, "all", false)),
            MenuItem("📨  أرسل إلى الطلاب (PDF جاهز للصق في المحادثة)…", () => BoardExportFromMenu(b, "all", true))));
        items.Add(MenuItem("📂  افتح مجلد السبورات", () => OpenInExplorer(b.LocalPath)));
        items.Add(Sep());
        items.Add(MenuItem("🗑  أزل السبورة من الرف (لا يُحذف الملف)", () => DeleteShelfItem(b), "Delete"));
    }

    private Book? _boardExportOnOpen;
    private (string Which, bool Send) _boardExportArgs;

    private void BoardExportFromMenu(Book b, string which, bool send)
    {
        if (ReaderShown && _currentBook == b) { _ = _reader?.BoardExportAsync(which, send); return; }
        _boardExportOnOpen = b; _boardExportArgs = (which, send);
        OpenBook(b);
    }
}
