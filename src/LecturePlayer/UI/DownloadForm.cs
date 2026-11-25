using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>
/// شاشة «تنزيل من يوتيوب»: رابط (فيديو أو قايمة تشغيل) أو فيديوهات من المكتبة ← النوع (MP4 / MP3 / M4A)،
/// الجودة بالحجم التقريبي، مكان الحفظ، والإضافات (الغلاف والبيانات، الفصول، الترجمة).
/// </summary>
internal static class DownloadForm
{
    /// <summary>فيديو هيتنزل. LibraryUrl = رابط فيديو المكتبة اللي الملف هيتربط بيه (لو «جوه المكتبة»).</summary>
    /// <param name="SubFolder">فولدر المحاضرة جوه فولدر الكورس (تنزيل كورس كامل لفولدر)، أو null.</param>
    /// <param name="Order">رقم الفيديو جوه محاضرته (01، 02…)؛ 0 = حسب الترتيب في القايمة.</param>
    public sealed record Item(string Url, string Title, long DurationMs, string? LibraryUrl, string? SubFolder = null, int Order = 0);

    /// <param name="GroupName">اسم قايمة التشغيل (فولدر فرعي لما تتنزل في فولدر).</param>
    public sealed record Plan(DownloadOptions Options, List<Item> Items, bool IntoLibrary, string Folder, string? GroupName, long EstimatedBytes);

    /// <summary>للفحص التلقائي: الشاشة تقرا الرابط وتختار دول وتدوس «نزّل» لوحدها.</summary>
    internal static (DownloadKind Kind, int Quality, string Folder)? SmokePick;
    internal static Action<Form>? SmokeShot;
    internal static string SmokeInfoText = "";
    /// <summary>للفحص: «جوه المكتبة» بدل «في فولدر».</summary>
    internal static bool SmokeIntoLibrary;

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "مسار");

    public static readonly int[] VideoHeights = { 360, 480, 720, 1080, 1440, 2160 };
    public static readonly int[] Mp3Rates = { 128, 192, 320 };

    /// <param name="url">رابط (وضع «من رابط»)، أو null لو الفيديوهات من المكتبة.</param>
    /// <param name="library">فيديوهات من المكتبة (وضع «جوه المكتبة» متاح).</param>
    public static Plan? Show(IWin32Window owner, AppSettings settings, string? url, IReadOnlyList<Item>? library, string? libraryName)
    {
        bool fromLibrary = library is { Count: > 0 };
        using var kit = new FormKit(fromLibrary ? "تنزيل من يوتيوب — " + (libraryName ?? "المكتبة") : "تنزيل من يوتيوب", 640);
        using var cts = new CancellationTokenSource();
        kit.Form.FormClosed += (_, _) => cts.Cancel();

        var items = new List<Item>(library ?? Array.Empty<Item>());
        VideoInfo? info = null;      // فيديو واحد: الجودات والأحجام الحقيقية
        string? groupName = null;
        bool busy = false;

        // ---------- الرابط ----------
        TextBox? urlBox = null;
        Label infoLabel;
        ListView? list = null;
        if (!fromLibrary)
        {
            kit.Header("🔗  الرابط");
            urlBox = kit.Row("الرابط:", new TextBox { Text = url ?? "", PlaceholderText = "رابط فيديو أو قائمة تشغيل من يوتيوب" });
            infoLabel = kit.Note("الصق الرابط وستتم قراءته تلقائيًا.", 34);
            list = kit.Full(DarkList.Setup(new ListView { CheckBoxes = false, MultiSelect = false }), 118);
            list.Columns.Add("الفيديو", 300);
            list.Columns.Add("المدة", 70);
            DarkList.ReverseColumns(list);
            // العمود ياخد الباقي بالظبط (من غير مساحة بيضا في العناوين ولا سكرول بالعرض) — وبيتعاد لما السكرول يظهر
            var lv = list;
            void FitColumns() { if (lv.ClientSize.Width > 120) lv.Columns[0].Width = lv.ClientSize.Width - lv.Columns[1].Width; }
            lv.ClientSizeChanged += (_, _) => FitColumns();
            lv.HandleCreated += (_, _) => FitColumns();
            FitColumns();
        }
        else
        {
            kit.Header("🎬  الفيديوهات");
            infoLabel = kit.Note(items.Count == 1 ? $"«{items[0].Title}»" : $"{items.Count} فيديو من «{libraryName}»", 40);
        }

        // ---------- النوع ----------
        kit.Header("🎚  النوع");
        var kindPanel = kit.Full(new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Panel }, 48);
        var kinds = new (DownloadKind Kind, string Text)[]
        {
            (DownloadKind.Mp4, "🎬  فيديو MP4"),
            (DownloadKind.Mp3, "🎧  صوت MP3"),
            (DownloadKind.M4a, "🎵  صوت M4A"),
        };
        var kindCards = new List<(DownloadKind Kind, OptionCard Card)>();
        int cardW = (kindPanel.Width - 12) / 3;
        foreach (var (k, text) in kinds)
        {
            var c = new OptionCard { Text = text, Width = cardW, Height = 42, Margin = new Padding(2, 0, 2, 0), Checked = k == settings.DownloadKind };
            kindPanel.Controls.Add(c);
            kindCards.Add((k, c));
        }
        DownloadKind Kind() => kindCards.FirstOrDefault(x => x.Card.Checked).Kind;

        // ---------- الجودة ----------
        var quality = kit.Row("الجودة:", new SelectBox());
        var qualityValues = new List<int>();

        // ---------- الحفظ ----------
        kit.Header("📁  الحفظ");
        var savePanel = kit.Full(new Panel { BackColor = Theme.Panel }, fromLibrary ? 96 : 46);
        var toFolder = new OptionCard { Text = "في مجلد", Dock = DockStyle.Top, Height = 42 };
        var toLibrary = new OptionCard { Text = "داخل المكتبة — يبقى الفيديو في مكانه في الدورة ويعمل دون إنترنت", Dock = DockStyle.Top, Height = 42, Margin = new Padding(0, 0, 0, 6) };
        savePanel.Controls.Add(toFolder);
        if (fromLibrary) savePanel.Controls.Add(toLibrary);   // Dock=Top: اللي بيتضاف آخر بيبقى فوق
        toLibrary.Checked = fromLibrary;
        toFolder.Checked = !fromLibrary;
        string folder = string.IsNullOrWhiteSpace(settings.DownloadFolder) ? DefaultFolder : settings.DownloadFolder;
        // الفولدر والزرار في سطر واحد
        var folderRow = kit.Row("المجلد:", new Panel { BackColor = Theme.Panel });
        var pick = new PillButton("اختر…", PillStyle.Ghost) { Dock = DockStyle.Left, Width = 90 };
        var folderBox = new TextBox { Text = folder, ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Input, ForeColor = Theme.Text };
        folderRow.Controls.Add(folderBox);
        folderRow.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 8, BackColor = Theme.Panel });
        folderRow.Controls.Add(pick);

        // ---------- إضافات ----------
        kit.Header("✨  إضافات");
        var cover = kit.Full(new ToggleSwitch { Text = "صورة الفيديو كغلاف، والاسم والقناة داخل بيانات الملف", Checked = settings.DownloadEmbedCover }, 30);
        var chapters = kit.Full(new ToggleSwitch { Text = "فصول الفيديو (إن وُجدت) — تظهر في المشغّلات ويمكن التنقل بها", Checked = settings.DownloadChapters }, 30);
        var subs = kit.Full(new ToggleSwitch { Text = "الترجمة العربية والإنجليزية داخل الفيديو (إن وُجدت)", Checked = settings.DownloadSubtitles }, 30);

        var summary = kit.Note("", 24);
        if (!settings.DownloadNoticeShown)
            kit.Note("⚖  نزّل فقط ما لك حق فيه: فيديوهاتك، أو Creative Commons، أو دورة سمح صاحبها بالتنزيل — للمذاكرة الشخصية دون نشر.", 40, Theme.Muted);

        kit.Buttons("⬇  نزّل", "إلغاء");
        Control? ok = kit.Ok;

        // ---------- المنطق ----------
        List<Item> Selected()
        {
            if (list == null || list.Items.Count != items.Count) return items;
            return list.Items.Cast<ListViewItem>().Where(i => (string)i.Tag! == "1").Select(i => items[i.Index]).ToList();
        }
        DownloadOptions Options(int q) => new(Kind(),
            MaxHeight: Kind() == DownloadKind.Mp4 ? q : settings.DownloadMaxHeight,
            Mp3Kbps: Kind() == DownloadKind.Mp3 ? q : settings.DownloadMp3Kbps,
            EmbedCover: cover.Checked, EmbedChapters: chapters.Checked, Subtitles: subs.Checked && Kind() == DownloadKind.Mp4);
        long Estimate(int q)
        {
            var sel = Selected();
            if (sel.Count == 1 && info != null) return YtDlp.EstimateBytes(info, sel[0].DurationMs, Options(q));
            return sel.Sum(i => YtDlp.EstimateBytes(null, i.DurationMs, Options(q)));
        }

        void FillQuality()
        {
            var k = Kind();
            int keep = qualityValues.Count > 0 && quality.SelectedIndex >= 0 ? qualityValues[quality.SelectedIndex] : -1;
            qualityValues.Clear();
            quality.Items.Clear();
            bool known = items.Sum(i => i.DurationMs) > 0 || info?.DurationMs > 0;
            string Size(int q) => known ? " — نحو " + YtDlp.FormatSize(Estimate(q)) : "";
            if (k == DownloadKind.Mp4)
            {
                var heights = info?.Heights is { Count: > 0 } hs
                    ? VideoHeights.Where(h => h <= hs.Max() + 8).ToList()
                    : VideoHeights.Take(4).ToList();
                if (info?.Heights is { Count: > 0 } avail && !heights.Any()) heights.Add(avail.Max());
                foreach (int h in heights) { qualityValues.Add(h); quality.Items.Add($"{h}p{(h == 720 ? " (مناسب للمحاضرات)" : "")}{Size(h)}"); }
                qualityValues.Add(0); quality.Items.Add("أعلى جودة متاحة" + Size(0));
                int want = keep > 0 || keep == 0 && qualityValues.Contains(0) ? keep : settings.DownloadMaxHeight;
                int idx = qualityValues.IndexOf(want);
                if (idx < 0) idx = qualityValues.FindLastIndex(h => h > 0 && h <= Math.Max(want, 360));
                quality.SelectedIndex = Math.Max(0, idx);
            }
            else if (k == DownloadKind.Mp3)
            {
                foreach (int r in Mp3Rates)
                {
                    qualityValues.Add(r);
                    quality.Items.Add($"{r} kbps{(r == 192 ? " (مقترح)" : r == 320 ? " (ملف أكبر، وليس صوتًا أفضل من يوتيوب)" : "")}{Size(r)}");
                }
                int idx = qualityValues.IndexOf(keep > 0 && Mp3Rates.Contains(keep) ? keep : settings.DownloadMp3Kbps);
                quality.SelectedIndex = Math.Max(0, idx < 0 ? 1 : idx);
            }
            else
            {
                qualityValues.Add(0);
                quality.Items.Add("مطابق لصوت يوتيوب تمامًا (دون تحويل)" + Size(0));
                quality.SelectedIndex = 0;
            }
            subs.Enabled = k == DownloadKind.Mp4;
            chapters.Enabled = true;
        }

        void Refresh()
        {
            int q = quality.SelectedIndex >= 0 && quality.SelectedIndex < qualityValues.Count ? qualityValues[quality.SelectedIndex] : 0;
            var sel = Selected();
            long est = Estimate(q);
            string target = toLibrary.Checked && fromLibrary ? Path.Combine(Storage.AppDataDir, "offline") : folder;
            long free = FreeSpace(target);
            bool enough = free < 0 || est <= 0 || free > est + 200L * 1024 * 1024;
            summary.Text = sel.Count == 0 ? "اختر فيديو واحدًا على الأقل."
                : $"{sel.Count} فيديو — نحو {YtDlp.FormatSize(est)}" + (free >= 0 ? $" • المساحة الفارغة على القرص: {YtDlp.FormatSize(free)}" : "") + (enough ? "" : "  ⚠ المساحة غير كافية");
            summary.ForeColor = enough ? Theme.Muted : Theme.Danger;
            if (ok != null) ok.Enabled = !busy && sel.Count > 0 && enough;
        }

        foreach (var (_, card) in kindCards) card.Click += (_, _) => { FillQuality(); Refresh(); };
        quality.SelectedIndexChanged += (_, _) => Refresh();
        toFolder.Click += (_, _) => Refresh();
        toLibrary.Click += (_, _) => Refresh();
        subs.CheckedChanged += (_, _) => Refresh();
        pick.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), UseDescriptionForTitle = true, Description = "أين تُحفظ التنزيلات؟" };
            if (dlg.ShowDialog(kit.Form) != DialogResult.OK) return;
            folder = dlg.SelectedPath;
            folderBox.Text = folder;
            toFolder.Checked = true;
            Refresh();
        };
        if (list != null)
            list.MouseClick += (_, e) =>
            {
                if (list.GetItemAt(e.X, e.Y) is not { } it) return;
                bool on = (string)it.Tag! != "1";
                it.Tag = on ? "1" : "0";
                it.SubItems[0].Text = (on ? "✓  " : "✗  ") + items[it.Index].Title;
                it.ForeColor = on ? Theme.Text : Theme.Muted;
                Refresh();
            };

        // قراية الرابط: قايمة تشغيل ← الفيديوهات؛ فيديو واحد ← الجودات والأحجام الحقيقية
        async Task ReadUrlAsync(string u)
        {
            if (!MediaFiles.IsUrl(YtDlp.NormalizeUrl(u))) { infoLabel.Text = "هذا ليس رابطًا."; return; }
            busy = true; Refresh();
            infoLabel.ForeColor = Theme.Muted;
            infoLabel.Text = "جارٍ قراءة الرابط…";
            try
            {
                var listing = await YtDlp.ListAsync(u, settings.WholePlaylist, cts.Token);
                items = listing.Entries.Select(e => new Item(e.Url, e.Title, e.DurationMs, null)).ToList();
                info = null;
                groupName = listing.IsPlaylist ? listing.Title : null;
                if (list != null)
                {
                    list.BeginUpdate();
                    list.Items.Clear();
                    foreach (var e in items)
                        list.Items.Add(new ListViewItem(new[] { "✓  " + e.Title, e.DurationMs > 0 ? TimeSpan.FromMilliseconds(e.DurationMs).ToString(e.DurationMs >= 3_600_000 ? @"h\:mm\:ss" : @"m\:ss") : "" }) { Tag = "1", ForeColor = Theme.Text });
                    list.EndUpdate();
                    if (list.ClientSize.Width > 120) list.Columns[0].Width = list.ClientSize.Width - list.Columns[1].Width;
                }
                if (listing.IsPlaylist)
                    infoLabel.Text = $"قائمة تشغيل «{listing.Title}» — {items.Count} فيديو. اضغط على فيديو لإزالته أو إعادته.";
                else if (items.Count == 1)
                {
                    infoLabel.Text = $"«{items[0].Title}» — جارٍ جلب الجودات المتاحة…";
                    try
                    {
                        info = await YtDlp.InfoAsync(items[0].Url, cts.Token);
                        items[0] = items[0] with { DurationMs = info.DurationMs > 0 ? info.DurationMs : items[0].DurationMs };
                        infoLabel.Text = $"«{info.Title}»" + (info.Channel != null ? $" — {info.Channel}" : "") +
                                         (info.DurationMs > 0 ? $" — {TimeSpan.FromMilliseconds(info.DurationMs):h\\:mm\\:ss}" : "");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        infoLabel.Text = $"«{items[0].Title}» (الأحجام تقريبية)";
                        Log.Info("download info: " + ex.Message);
                    }
                }
                SmokeInfoText = infoLabel.Text;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                items = new List<Item>();
                infoLabel.ForeColor = Theme.Danger;
                infoLabel.Text = YtDlp.Explain(ex.Message).Message.Split('\n')[0];
                SmokeInfoText = "ERROR " + ex.Message;
            }
            finally
            {
                busy = false;
                if (!kit.Form.IsDisposed) { FillQuality(); Refresh(); }
            }
        }

        if (urlBox != null)
        {
            string lastRead = "";
            var debounce = new System.Windows.Forms.Timer { Interval = 600 };
            debounce.Tick += async (_, _) =>
            {
                debounce.Stop();
                string u = urlBox.Text.Trim();
                if (u.Length == 0 || u == lastRead) return;
                lastRead = u;
                await ReadUrlAsync(u);
            };
            urlBox.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
            kit.Form.FormClosed += (_, _) => debounce.Dispose();
            kit.Form.Shown += async (_, _) =>
            {
                if (urlBox.Text.Trim().Length == 0) { urlBox.Focus(); return; }
                lastRead = urlBox.Text.Trim();
                await ReadUrlAsync(lastRead);
                if (SmokePick is { } sp) SmokeAccept(sp);
            };
        }
        else
        {
            // فيديو واحد من المكتبة: الأحجام الحقيقية (في الخلفية)
            kit.Form.Shown += async (_, _) =>
            {
                if (items.Count == 1)
                {
                    try { info = await YtDlp.InfoAsync(items[0].Url, cts.Token); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { Log.Info("download info: " + ex.Message); }
                    catch (OperationCanceledException) { return; }
                    if (!kit.Form.IsDisposed) { FillQuality(); Refresh(); }
                }
                if (SmokePick is { } sp) SmokeAccept(sp);
            };
        }

        void SmokeAccept((DownloadKind Kind, int Quality, string Folder) sp)
        {
            foreach (var (k, c) in kindCards) c.Checked = k == sp.Kind;
            FillQuality();
            int qi = qualityValues.IndexOf(sp.Quality);
            if (qi >= 0) quality.SelectedIndex = qi;
            folder = sp.Folder; folderBox.Text = folder;
            toFolder.Checked = !(SmokeIntoLibrary && fromLibrary);
            toLibrary.Checked = SmokeIntoLibrary && fromLibrary;
            Refresh();
            Application.DoEvents();
            SmokeShot?.Invoke(kit.Form);
            kit.Form.DialogResult = DialogResult.OK;
        }

        FillQuality();
        Refresh();
        if (kit.Show(owner) != DialogResult.OK) return null;

        var chosen = Selected();
        if (chosen.Count == 0) return null;
        int qv = quality.SelectedIndex >= 0 && quality.SelectedIndex < qualityValues.Count ? qualityValues[quality.SelectedIndex] : 0;
        var options = Options(qv);

        // نفتكر الاختيارات للمرة الجاية
        settings.DownloadKind = options.Kind;
        if (options.Kind == DownloadKind.Mp4) settings.DownloadMaxHeight = qv;
        if (options.Kind == DownloadKind.Mp3) settings.DownloadMp3Kbps = qv;
        settings.DownloadEmbedCover = cover.Checked;
        settings.DownloadChapters = chapters.Checked;
        settings.DownloadSubtitles = subs.Checked;
        bool intoLibrary = fromLibrary && toLibrary.Checked;
        if (!intoLibrary) settings.DownloadFolder = folder == DefaultFolder ? "" : folder;
        settings.DownloadNoticeShown = true;

        if (fromLibrary && chosen.Count > 1) groupName = libraryName;
        return new Plan(options, chosen, intoLibrary, folder, groupName, Estimate(qv));
    }

    /// <summary>المساحة الفاضية على الديسك اللي فيه الفولدر (-1 لو مش معروفة).</summary>
    public static long FreeSpace(string folder)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(folder));
            return string.IsNullOrEmpty(root) ? -1 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return -1; }
    }
}
