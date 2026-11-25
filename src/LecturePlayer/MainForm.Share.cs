using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>«📤 شارك الرف» و«📥 استقبل رف متشارك».</summary>
internal partial class MainForm
{
    /// <summary>للفحص: الاختيارات من غير شباك.</summary>
    internal ShelfShare.Options? SmokeShareOptions;

    private async void ShareShelfDialog(Shelf s)
    {
        if (!HasLibrary) return;
        SaveAllInk();
        var opt = SmokeShareOptions;
        if (opt == null)
        {
            using var kit = new FormKit($"شارك الرف «{s.Name}»", 600);
            kit.Note("يُنشأ ملف ZIP فيه الرف (والرفوف التي بداخله) ونسخة من الكتب — أرسله لأي شخص لديه «مسار» ليفتحه من «إضافة ← رف مشترك».", 44, Theme.Muted);
            var ann = kit.Row("الشرح", new ToggleSwitch { Text = "الأقلام والتظليل والعلامات والملاحظات", Checked = true });
            var copies = kit.Row("النسخ المشروحة", new ToggleSwitch { Text = "أضف «اسم الكتاب — مشروح.pdf» بجانب كل كتاب", Checked = false });
            var prog = kit.Row("تقدّمي", new ToggleSwitch { Text = "الصفحات التي قرأتها ومكاني في كل كتاب", Checked = false });
            var info = kit.Note("", 64, Theme.Text);
            void Update()
            {
                var o = new ShelfShare.Options(prog.Checked, ann.Checked, copies.Checked);
                var p = ShelfShare.MakePlan(_library, new[] { s }, BookFile, o);
                var lines = new List<string> { $"{p.Books.Count} كتاب في {p.Shelves.Count} رف — حوالي {YtDlp.FormatSize(p.Bytes)}" };
                if (p.PrivateLinks.Count > 0) lines.Add($"⚠ {p.PrivateLinks.Count} كتاب برابط خاص (غير منزّل): قد لا يستطيع المستلم فتحه — نزّلها أولًا إن أمكن");
                if (p.Missing.Count > 0) lines.Add($"⚠ {p.Missing.Count} كتاب ملفه غير موجود على هذا الجهاز (سيُسجَّل اسمه فقط)");
                info.Text = string.Join("\n", lines);
                info.ForeColor = p.PrivateLinks.Count + p.Missing.Count > 0 ? Color.FromArgb(240, 180, 90) : Theme.Text;
            }
            ann.CheckedChanged += (_, _) => Update();
            copies.CheckedChanged += (_, _) => Update();
            prog.CheckedChanged += (_, _) => Update();
            Update();
            kit.Buttons("أنشئ الملف");
            if (SmokeShowDialog(kit, "share-shelf")) return;
            if (kit.Show(this) != DialogResult.OK) return;
            opt = new ShelfShare.Options(prog.Checked, ann.Checked, copies.Checked);
        }
        var plan = ShelfShare.MakePlan(_library, new[] { s }, BookFile, opt);
        if (plan.Books.Count == 0) { ShowStatus("الرف فارغ", 4); return; }
        string? path = SmokeSavePath;
        if (path == null)
        {
            string safe = new string(s.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray());
            using var dlg = new SaveFileDialog { Title = "حفظ الرف المشترك", Filter = "رف مشترك (ZIP)|*.zip", FileName = $"رف {safe} — مسار.zip", RestoreDirectory = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
        }
        string lib = _libraryPath;
        var progress = new Progress<double>(p => ShowStatus($"📤 جارٍ تجهيز الرف… {(int)(p * 100)}%", 10));
        try
        {
            ShowStatus("📤 جارٍ تجهيز الرف…", 10);
            var r = await Task.Run(() => ShelfShare.Export(plan, lib, BookFile, path, opt, progress, CancellationToken.None));
            _lastShareZip = path;
            ShowBanner($"📤 أُنشئ ملف الرف «{s.Name}»: {r.Books} كتاب ({YtDlp.FormatSize(r.Bytes)})", BannerKind.Success,
                "افتح مكانه", () => OpenInExplorer(path), seconds: 15);
        }
        catch (Exception ex)
        {
            Log.Error("share shelf", ex);
            ShowBanner("تعذّر إنشاء ملف الرف: " + ex.Message, BannerKind.Warning, seconds: 10);
        }
    }

    internal string? _lastShareZip;

    private void ReceiveSharedShelfDialog(Shelf? parent)
    {
        if (!EnsureLibrary()) return;
        string? zip = SmokeOpenPath;
        if (zip == null)
        {
            using var dlg = new OpenFileDialog { Title = "اختر ملف الرف المشترك", Filter = "رف مشترك (ZIP)|*.zip", RestoreDirectory = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            zip = dlg.FileName;
        }
        _ = ReceiveSharedShelfAsync(zip, parent);
    }

    /// <summary>بيفك الرف في «Documents\مسار\رفوف متشاركة\اسمه» ويضيفه للمكتبة.</summary>
    internal async Task<ShelfShare.ImportResult?> ReceiveSharedShelfAsync(string zip, Shelf? parent, string? destRoot = null)
    {
        string name = ShelfShare.PeekName(zip) ?? Path.GetFileNameWithoutExtension(zip);
        string safe = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        string root = destRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "مسار", "رفوف متشاركة");
        string dest = Path.Combine(root, safe.Length > 0 ? safe : "رف");
        for (int i = 2; Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any(); i++) dest = Path.Combine(root, $"{safe} ({i})");
        string lib = _libraryPath;
        try
        {
            ShowStatus("📥 جارٍ فك الرف…", 30);
            var r = await Task.Run(() => ShelfShare.Import(zip, dest, _library, parent, lib));
            Checkpoint("استقبال رف");
            _library.Shelves.AddRange(r.Shelves);
            _library.Books.AddRange(r.Books);
            MarkDirty();
            if (!_shelfMode) ShowShelf(true);
            RebuildShelf();
            if (r.Shelves.FirstOrDefault(x => x.ParentId == parent?.Id) is { } top) SelectShelfItem(top);
            var local = r.Books.Where(b => !b.IsOnline && File.Exists(b.LocalPath)).ToList();
            if (local.Count > 0) QueueProbe(local);
            var session = ImportSessionFromZip(zip, r);
            if (session != null)
            {
                LastImportedSession = session;
                ShowBanner($"📥 وصلت «{session.Name}»: {r.Books.Count} ملف" + (r.Annotations > 0 ? $"، و{r.Annotations} عليها شرح 🖊" : "") +
                           (session.Homework is { Length: > 0 } hw ? "  ·  الواجب: " + FirstLine(hw) : ""), BannerKind.Success,
                    "افتحها", () => OpenSession(session), seconds: 0, key: "import-session");
                RefreshHome();
                return r;
            }
            ShowBanner($"📥 أُضيف رف «{r.Name}»: {r.Books.Count} كتاب" + (r.Annotations > 0 ? $"، و{r.Annotations} عليها شرح 🖊" : ""), BannerKind.Success,
                "افتح مجلد الكتب", () => OpenFolder(r.Folder), seconds: 15);
            return r;
        }
        catch (Exception ex)
        {
            Log.Error("receive shelf", ex);
            ShowBanner("تعذّر فتح الرف المشترك: " + ex.Message, BannerKind.Warning, seconds: 12);
            return null;
        }
    }
}
