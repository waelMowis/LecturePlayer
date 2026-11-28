using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>
/// «بث مباشر»: المفضلة، ودليل الراديو (Radio Browser)، وقنوات التليفزيون اللي المستخدم بيضيفها بنفسه.
/// التشغيل نفسه بيحصل في المشغل الرئيسي (MainForm.PlayLive).
/// </summary>
internal sealed class LiveForm : ToolWindow
{
    public sealed record Host(
        Func<CourseLibrary> Library,
        Action<LiveChannel> Play,
        Action<string> Checkpoint,     // قبل أي تعديل (للتراجع)
        Action<LiveChannel> Forget,    // اتمسحت (عشان ماترجعش من جهاز تاني)
        Action Changed,                // بعد أي تعديل (حفظ)
        Func<LiveChannel?>? Current = null,   // اللي شغال الآن (يُميَّز في القائمة)
        Action? Stop = null);                 // إيقاف البث

    private enum Tab { Favorites, Radio, Tv }

    private readonly Host _host;
    private Tab _tab;
    private readonly PillButton _tabFav, _tabRadio, _tabTv;
    private readonly FlowLayoutPanel _filters;
    private readonly TextBox _search = new() { RightToLeft = RightToLeft.Yes };
    private readonly SelectBox _country = new() { Width = 130 };
    private readonly SelectBox _genre = new() { Width = 120 };
    private readonly Label _note = new() { Dock = DockStyle.Top, Height = 24, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight };
    private readonly ListView _list = DarkList.Setup(new ListView { Dock = DockStyle.Fill, MultiSelect = false });
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 24, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight };
    private readonly PillButton _favButton, _deleteButton, _stopButton;
    private List<RadioStation> _radioResults = new();
    private CancellationTokenSource? _searchCts;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 350 };
    private readonly System.Windows.Forms.Timer _nowTimer = new() { Interval = 1000 };
    private string? _shownPlaying;
    /// <summary>نتائج البحث في هذه الجلسة (نفس الدولة/النوع/الاسم لا يُطلب مرتين).</summary>
    private static readonly Dictionary<string, (DateTime At, List<RadioStation> Items)> Cache = new();
    /// <summary>آخر دولة ونوع اخترتهما (حتى تُغلق البرنامج).</summary>
    private static int? _lastCountry, _lastGenre;

    public LiveForm(Host host) : base("بث مباشر — راديو وتلفزيون", 820, 600)
    {
        _host = host;
        MinimizeBox = true;

        // التبويبات
        var tabs = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 46, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            BackColor = Theme.Panel, Padding = new Padding(0, 4, 0, 4)
        };
        _tabFav = TabButton("⭐ المفضلة", Tab.Favorites);
        _tabRadio = TabButton("📻 راديو", Tab.Radio);
        _tabTv = TabButton("📺 تلفزيون", Tab.Tv);
        tabs.Controls.AddRange(new Control[] { _tabFav, _tabRadio, _tabTv });

        // فلاتر الراديو
        _filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 44, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            BackColor = Theme.Panel, Padding = new Padding(0, 4, 0, 4)
        };
        FormKit.Style(_search);
        _search.PlaceholderText = "اسم المحطة…";
        var searchHost = new RoundedHost(_search, searchIcon: true) { Width = 230, Height = 34, Margin = new Padding(0, 0, 0, 0) };
        foreach (var (_, name) in RadioBrowser.Countries) _country.Items.Add(name);
        foreach (var (_, name) in RadioBrowser.Genres) _genre.Items.Add(name);
        _country.SelectedIndex = _lastCountry ?? DefaultCountry();
        _genre.SelectedIndex = _lastGenre ?? 0;
        _country.Height = _genre.Height = 34;
        _country.Margin = _genre.Margin = new Padding(8, 0, 0, 0);
        var go = Btn("بحث", () => _ = SearchRadioAsync());
        go.Margin = new Padding(8, 0, 0, 0);
        _filters.Controls.AddRange(new Control[] { searchHost, _country, _genre, go });
        _search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _debounce.Stop(); _ = SearchRadioAsync(); } };
        // الكتابة والتنقل بين الدول بالأسهم: بحث واحد بعد أن تتوقف لحظة، لا بحث مع كل حرف
        _search.TextChanged += (_, _) => Debounce();
        _country.SelectedIndexChanged += (_, _) => { _lastCountry = _country.SelectedIndex; Debounce(); };
        _genre.SelectedIndexChanged += (_, _) => { _lastGenre = _genre.SelectedIndex; Debounce(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = SearchRadioAsync(); };
        _nowTimer.Tick += (_, _) => MarkPlaying();

        // القايمة
        _list.Columns.Add("الاسم", 300);
        _list.Columns.Add("البلد / المجموعة", 160);
        _list.Columns.Add("النوع", 200);
        _list.Columns.Add("الجودة", 90);
        DarkList.ReverseColumns(_list);
        _list.MouseDoubleClick += (_, e) =>
        {
            // النقر المزدوج على أي مكان في الصف يشغّله
            var hit = _list.HitTest(e.Location).Item ?? _list.GetItemAt(8, e.Y);
            if (hit == null) return;
            hit.Selected = true;
            hit.Focused = true;
            PlaySelected();
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; PlaySelected(); }
            else if (e.KeyCode == Keys.Delete) DeleteSelected();
        };
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();

        // الأزرار
        var bar = ButtonBar();
        bar.Controls.Add(Btn("▶ تشغيل", PlaySelected, accent: true));
        _favButton = Btn("⭐ إلى المفضلة", ToggleFavorite);
        bar.Controls.Add(_favButton);
        bar.Controls.Add(Btn("＋ قناة برابط…", AddByLink));
        bar.Controls.Add(Btn("إضافة قنوات من ملف…", ImportM3u));
        _deleteButton = Btn("حذف", DeleteSelected);
        bar.Controls.Add(_deleteButton);
        _stopButton = Btn("⏹ إيقاف", () => { _host.Stop?.Invoke(); MarkPlaying(); });
        _stopButton.Visible = _host.Stop != null;
        bar.Controls.Add(_stopButton);

        // الترتيب مهم مع Dock: اللي بيتضاف الأول بيبقى تحت
        Controls.Add(_list);
        Controls.Add(_status);
        Controls.Add(_note);
        Controls.Add(_filters);
        Controls.Add(tabs);
        Controls.Add(bar);

        var lib = _host.Library();
        SelectTab(lib.Live.Any(l => l.Favorite) ? Tab.Favorites : Tab.Radio);
        _nowTimer.Start();
    }

    /// <summary>دولة الجهاز إن كانت في القائمة (السعودية لمن في السعودية…)، وإلا مصر.</summary>
    private static int DefaultCountry()
    {
        try
        {
            string code = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
            int i = Array.FindIndex(RadioBrowser.Countries, c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (i > 0) return i;
        }
        catch { }
        return 1;
    }

    private void Debounce()
    {
        if (_tab != Tab.Radio) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private PillButton TabButton(string text, Tab tab)
    {
        var b = new PillButton(text, PillStyle.Ghost) { Height = 34, Margin = new Padding(8, 0, 0, 0) };
        b.Width = Math.Max(110, b.Width);
        b.Click += (_, _) => SelectTab(tab);
        return b;
    }

    private void SelectTab(Tab tab)
    {
        _tab = tab;
        _tabFav.Style = tab == Tab.Favorites ? PillStyle.Primary : PillStyle.Ghost;
        _tabRadio.Style = tab == Tab.Radio ? PillStyle.Primary : PillStyle.Ghost;
        _tabTv.Style = tab == Tab.Tv ? PillStyle.Primary : PillStyle.Ghost;
        _filters.Visible = tab == Tab.Radio;
        _note.Text = tab switch
        {
            Tab.Radio => "محطات راديو الإنترنت من دليل Radio Browser المفتوح. انقر نقرًا مزدوجًا للتشغيل، و⭐ لحفظها.",
            Tab.Tv => "قنوات جاهزة ببثها الرسمي على يوتيوب: الحرمان، والرياضة، والأخبار. وأضف قنواتك من مصادرها الرسمية: رابط البث، أو بثها على يوتيوب، أو ملف قنوات (M3U) من اشتراكك.",
            _ => "محطاتك وقنواتك المفضلة (تتزامن مع أجهزتك الأخرى مع المكتبة)."
        };
        if (tab == Tab.Radio && _radioResults.Count == 0) _ = SearchRadioAsync();
        else Fill();
    }

    // =====================================================================

    private sealed record Row(LiveChannel Channel, RadioStation? Station);

    private void Fill()
    {
        var lib = _host.Library();
        IEnumerable<Row> rows = _tab switch
        {
            Tab.Favorites => lib.Live.Where(l => l.Favorite).Select(l => new Row(l, null)),
            // القنوات الرسمية الجاهزة الأول (لو اتحطت في المفضلة بتتاخد نسختها من المكتبة)، وبعدها قنواتك
            Tab.Tv => LivePresets.Tv
                .Select(p => new Row(lib.Live.FirstOrDefault(l => l.Id == p.Id) ?? LivePresets.Copy(p), null))
                .Concat(lib.Live.Where(l => l.Kind == LiveKind.Tv && !l.Id.StartsWith("preset:", StringComparison.Ordinal)).Select(l => new Row(l, null))),
            _ => _radioResults.Select(s => new Row(lib.Live.FirstOrDefault(l => l.Id == "rb:" + s.Uuid) ?? s.ToChannel(), s))
        };

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var r in rows)
        {
            var c = r.Channel;
            string kind = c.Kind == LiveKind.Radio ? "📻" : "📺";
            string quality = r.Station is { } st
                ? (st.Bitrate > 0 ? $"{st.Bitrate} kbps" : st.Codec)
                : (c.Kind == LiveKind.Tv ? (M3u.IsDirectStream(c.Url) ? "بث" : "يوتيوب/موقع") : "");
            string tags = Shorten(c.Tags ?? "", 60);
            var item = new ListViewItem(new[] { (c.Favorite ? "⭐ " : "") + kind + " " + c.Name, c.Group ?? "", tags, quality })
            {
                Tag = r, ForeColor = Theme.Text, ToolTipText = c.Name + (string.IsNullOrWhiteSpace(c.Tags) ? "" : "\n" + c.Tags)
            };
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        _shownPlaying = null;
        MarkPlaying();
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
        _status.Text = _tab switch
        {
            Tab.Favorites when _list.Items.Count == 0 => "لا توجد مفضلة بعد: من تبويب «راديو» اختر محطة واضغط ⭐ للمفضلة.",
            Tab.Tv when _list.Items.Count <= LivePresets.Tv.Count => "أضف قنواتك: «＋ قناة برابط» أو «استيراد ملف قنوات».",
            _ => $"{_list.Items.Count} عنصر"
        };
        UpdateButtons();
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private Row? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as Row : null;

    private void UpdateButtons()
    {
        var r = Selected;
        _favButton.Enabled = r != null;
        _favButton.Text = r?.Channel.Favorite == true ? "أزل من المفضلة" : "⭐ إلى المفضلة";
        _favButton.AutoSizeToText();
        _favButton.Width = Math.Max(110, _favButton.Width);
        // الحذف للحاجات المحفوظة في المكتبة بس (مش نتايج الدليل)
        _deleteButton.Enabled = r != null && _host.Library().Live.Contains(r.Channel);
    }

    private async Task SearchRadioAsync()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        string code = RadioBrowser.Countries[Math.Max(0, _country.SelectedIndex)].Code;
        string tag = RadioBrowser.Genres[Math.Max(0, _genre.SelectedIndex)].Tag;
        string key = $"{code}|{tag}|{_search.Text.Trim().ToLowerInvariant()}";
        if (Cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(30))
        {
            _radioResults = hit.Items;
            if (_tab == Tab.Radio) Fill();
            return;
        }
        // بلا مؤشر انتظار للنافذة كلها: القائمة تبقى تعمل، والحالة مكتوبة أسفلها
        _status.Text = "جارٍ البحث في دليل الراديو…";
        try
        {
            var result = await RadioBrowser.SearchAsync(_search.Text, code, tag, 200, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;
            Cache[key] = (DateTime.UtcNow, result);
            _radioResults = result;
            if (_tab == Tab.Radio) Fill();
            if (result.Count == 0) _status.Text = "لا توجد محطات بهذا البحث. جرّب دولة أو نوعًا آخر.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("radio-search", ex);
            if (!IsDisposed && _searchCts == cts) _status.Text = "⚠ " + ex.Message;
        }
    }

    // =====================================================================

    private void PlaySelected()
    {
        if (Selected is not { } r) return;
        _host.Play(r.Channel);
        _shownPlaying = null;
        MarkPlaying();
    }

    /// <summary>المحطة الشغالة: ▶ وخط عريض في القائمة، واسمها في السطر السفلي.</summary>
    private void MarkPlaying()
    {
        if (IsDisposed) return;
        var cur = _host.Current?.Invoke();
        string? id = cur == null ? null : cur.Id + "|" + cur.Url;
        _stopButton.Enabled = cur != null;
        if (id == _shownPlaying) return;
        _shownPlaying = id;
        _list.BeginUpdate();
        foreach (ListViewItem it in _list.Items)
        {
            bool on = cur != null && it.Tag is Row r && (r.Channel == cur || (r.Channel.Id == cur.Id && r.Channel.Url == cur.Url));
            string name = it.SubItems[0].Text;
            bool marked = name.StartsWith("🔊 ", StringComparison.Ordinal);
            if (on && !marked) it.SubItems[0].Text = "🔊 " + name;
            else if (!on && marked) it.SubItems[0].Text = name[3..];
            it.ForeColor = on ? Theme.Success : Theme.Text;
        }
        _list.EndUpdate();
        if (cur != null) _status.Text = "🔊 يعمل الآن: " + cur.Name;
        else if (_status.Text.StartsWith("🔊", StringComparison.Ordinal)) _status.Text = $"{_list.Items.Count} عنصر";
    }

    private void ToggleFavorite()
    {
        if (Selected is not { } r) return;
        var lib = _host.Library();
        var c = r.Channel;
        _host.Checkpoint(c.Favorite ? "أزل من المفضلة" : "إضافة إلى المفضلة");
        if (!c.Favorite)
        {
            c.Favorite = true;
            c.TouchInfo();
            if (!lib.Live.Contains(c)) lib.Live.Add(c);
        }
        else
        {
            c.Favorite = false;
            c.TouchInfo();
            // محطة من الدليل أو قناة جاهزة مالهاش لازمة في المكتبة لو مش مفضلة
            if ((c.StationUuid != null && c.Kind == LiveKind.Radio) || c.Id.StartsWith("preset:", StringComparison.Ordinal)) { lib.Live.Remove(c); _host.Forget(c); }
        }
        _host.Changed();
        Fill();
    }

    private void AddByLink()
    {
        using var kit = new FormKit("قناة أو محطة برابط", 560);
        kit.Header("البيانات");
        var name = kit.Row("الاسم", new TextBox { RightToLeft = RightToLeft.Yes });
        var url = kit.Row("الرابط", new TextBox { RightToLeft = RightToLeft.No });
        var kind = kit.Row("النوع", new SelectBox());
        kind.Items.AddRange(new[] { "📺 تلفزيون", "📻 راديو" });
        kind.SelectedIndex = _tab == Tab.Radio ? 1 : 0;
        var group = kit.Row("المجموعة (اختياري)", new TextBox { RightToLeft = RightToLeft.Yes });
        kit.Note("يمكن أن يكون الرابط: رابط بث (m3u8 أو mp3…)، أو صفحة بث مباشر على يوتيوب، أو رابط البث من موقع القناة الرسمي.");
        kit.Buttons("إضافة");
        if (kit.Show(this) != DialogResult.OK) return;

        string link = url.Text.Trim();
        if (!MediaFiles.IsUrl(link) && !link.Contains("://", StringComparison.Ordinal))
        {
            Dialogs.Error(this, "الرابط غير صحيح.");
            return;
        }
        var c = new LiveChannel
        {
            Name = string.IsNullOrWhiteSpace(name.Text) ? link : name.Text.Trim(),
            Url = link,
            Kind = kind.SelectedIndex == 1 ? LiveKind.Radio : LiveKind.Tv,
            Group = string.IsNullOrWhiteSpace(group.Text) ? null : group.Text.Trim(),
            Favorite = kind.SelectedIndex == 1 // الراديو المضاف بإيدك يظهر في المفضلة
        };
        c.TouchInfo();
        _host.Checkpoint("إضافة قناة");
        _host.Library().Live.Add(c);
        _host.Changed();
        SelectTab(c.Kind == LiveKind.Tv ? Tab.Tv : Tab.Favorites);
    }

    private void ImportM3u()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "استيراد ملف قنوات (M3U)",
            Filter = "ملفات القنوات (*.m3u;*.m3u8)|*.m3u;*.m3u8|كل الملفات|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var channels = M3u.Parse(File.ReadAllText(dlg.FileName));
            var lib = _host.Library();
            var existing = new HashSet<string>(lib.Live.Select(l => l.Url), StringComparer.OrdinalIgnoreCase);
            var fresh = channels.Where(c => existing.Add(c.Url)).ToList();
            if (fresh.Count == 0) { Dialogs.Info(this, channels.Count == 0 ? "الملف لا يحتوي على قنوات." : "هذه القنوات موجودة بالفعل."); return; }
            _host.Checkpoint("استيراد قنوات");
            lib.Live.AddRange(fresh);
            _host.Changed();
            SelectTab(Tab.Tv);
            _status.Text = $"أُضيفت {fresh.Count} قناة ✓";
        }
        catch (Exception ex)
        {
            Log.Error("m3u-import", ex);
            Dialogs.Error(this, "تعذّرت قراءة الملف:\n" + ex.Message);
        }
    }

    private void DeleteSelected()
    {
        if (Selected is not { } r) return;
        var lib = _host.Library();
        if (!lib.Live.Contains(r.Channel)) return;
        if (!Dialogs.Confirm(this, $"هل تريد حذف «{r.Channel.Name}»؟")) return;
        _host.Checkpoint("حذف قناة");
        lib.Live.Remove(r.Channel);
        _host.Forget(r.Channel);
        _host.Changed();
        Fill();
    }

    /// <summary>من برا (بعد تراجع أو مزامنة): نحدّث القايمة.</summary>
    public void RefreshList() { if (!IsDisposed) Fill(); }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _searchCts?.Cancel();
        _debounce.Dispose();
        _nowTimer.Dispose();
        base.OnFormClosed(e);
    }
}
