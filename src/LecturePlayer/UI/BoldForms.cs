using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>المراجعة المتباعدة: علامة علامة — شغّل الجزء، وبعدين «فاكرها» أو «مش فاكرها».</summary>
internal sealed class ReviewForm : ToolWindow
{
    private readonly List<ReviewItem> _items;
    private readonly Action<ReviewItem> _play;
    private readonly Action<ReviewItem, bool?> _graded; // null = شيلها من المراجعة
    private readonly Label _progress = new() { Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _where = new() { Dock = DockStyle.Top, Height = 48, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _note = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    private readonly FlowLayoutPanel _bar;
    private int _index, _remembered;

    public ReviewForm(List<ReviewItem> items, Action<ReviewItem> play, Action<ReviewItem, bool?> graded)
        : base("المراجعة المتباعدة", 600, 360)
    {
        _items = items;
        _play = play;
        _graded = graded;
        _bar = ButtonBar();
        _bar.Controls.Add(Btn("✓ أتذكّرها", () => Grade(true), accent: true));
        _bar.Controls.Add(Btn("✗ لا أتذكّرها", () => Grade(false)));
        _bar.Controls.Add(Btn("▶ شغّل هذا الجزء", () => _play(_items[_index])));
        _bar.Controls.Add(Btn("أزلها من المراجعة", () => Grade(null)));
        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 40, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleCenter,
            Text = "حاول تذكّر هذه الفكرة أولًا دون تشغيل… ثم شغّل الجزء وتأكد."
        };
        Controls.Add(_note);
        Controls.Add(hint);
        Controls.Add(_where);
        Controls.Add(_progress);
        Controls.Add(_bar);
        Show(0);
    }

    private void Show(int i)
    {
        _index = i;
        if (i >= _items.Count)
        {
            _progress.Text = "";
            _where.Text = "";
            _note.Text = $"انتهت مراجعة اليوم 👏\nتذكّرت {_remembered} من {_items.Count}";
            foreach (Control c in _bar.Controls) c.Enabled = false;
            return;
        }
        var it = _items[i];
        _progress.Text = $"{i + 1} من {_items.Count}   ·   المرحلة {it.Bookmark.ReviewStage + 1} من {ReviewScheduler.IntervalsDays.Length}";
        _where.Text = $"{it.Course.Name}  ›  {it.Group.Name}\n{it.Video.Title}  —  {Transcript.Short(it.Bookmark.TimeMs)}";
        _note.Text = string.IsNullOrWhiteSpace(it.Bookmark.Note) ? "(علامة بدون ملاحظة — شغّل الجزء)" : it.Bookmark.Note;
    }

    private void Grade(bool? remembered)
    {
        if (_index >= _items.Count) return;
        if (remembered == true) _remembered++;
        _graded(_items[_index], remembered);
        Show(_index + 1);
    }
}

/// <summary>معمل الكود: اكتب أو الصق كود (C# / Python / JavaScript) وشغّله على طول.</summary>
internal sealed class CodeLabForm : ToolWindow
{
    private readonly TextBox _code = FormKit.Style(new TextBox
    {
        Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, RightToLeft = RightToLeft.No, Font = new Font("Consolas", 11f)
    });
    private readonly TextBox _output = FormKit.Style(new TextBox
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
        RightToLeft = RightToLeft.No, Font = new Font("Consolas", 10f)
    });
    private readonly SelectBox _lang = new() { Width = 140, Dock = DockStyle.Left, RightToLeft = RightToLeft.No };
    private readonly PillButton _run, _stop;
    private readonly string _workDir;
    private CancellationTokenSource? _cts;

    public CodeLabForm(string initialCode, string workDir) : base("معمل الكود", 900, 680)
    {
        _workDir = workDir;
        _code.Text = initialCode.Replace("\r\n", "\n").Replace("\n", "\r\n");
        foreach (CodeLang l in Enum.GetValues<CodeLang>()) _lang.Items.Add(CodeRunner.Label(l));
        _lang.SelectedIndex = (int)CodeRunner.Detect(initialCode);

        var top = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 2, 0, 6) };
        _run = Btn("▶ تشغيل (F5)", () => _ = RunAsync(), accent: true);
        _stop = Btn("إيقاف", () => _cts?.Cancel());
        _stop.Enabled = false;
        var detect = Btn("اكتشف اللغة", () => _lang.SelectedIndex = (int)CodeRunner.Detect(_code.Text));
        foreach (var b in new[] { _run, _stop, detect }) { b.Dock = DockStyle.Right; }
        top.BackColor = Theme.Panel;
        top.Controls.Add(_lang);
        top.Controls.Add(detect);
        top.Controls.Add(_stop);
        top.Controls.Add(_run);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Theme.Panel, SplitterWidth = 6 };
        split.Panel1.Controls.Add(_code);
        split.Panel2.Controls.Add(_output);
        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 22, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight,
            Text = "C# يعمل بـ «dotnet run» (يحتاج .NET 10 SDK) · Python يحتاج py · JavaScript يحتاج node"
        };
        Controls.Add(split);
        Controls.Add(hint);
        Controls.Add(top);
        Shown += (_, _) => { split.SplitterDistance = (int)(split.Height * 0.62); _code.Focus(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.F5) { e.Handled = true; _ = RunAsync(); } };
        FormClosed += (_, _) => _cts?.Cancel();
    }

    private void Append(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Append(line)); return; }
        _output.AppendText(line + "\r\n");
    }

    private async Task RunAsync()
    {
        if (_cts != null) return;
        var lang = (CodeLang)_lang.SelectedIndex;
        _cts = new CancellationTokenSource();
        _run.Enabled = false;
        _stop.Enabled = true;
        _output.Text = $"▶ {CodeRunner.Label(lang)}…" + (lang == CodeLang.CSharp ? " (المرة الأولى تستغرق وقتًا للبناء)" : "") + "\r\n";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var r = await CodeRunner.RunAsync(lang, _code.Text, _workDir, TimeSpan.FromMinutes(3), Append, _cts.Token);
            Append(r.TimedOut ? "⏱ توقف لأنه تجاوز 3 دقائق." : $"— انتهى (رمز الخروج {r.ExitCode}) في {sw.Elapsed.TotalSeconds:0.0} ث");
        }
        catch (OperationCanceledException) { Append("— توقف."); }
        catch (Exception ex) { Append("✗ " + ex.Message); }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            if (!IsDisposed) { _run.Enabled = true; _stop.Enabled = false; }
        }
    }
}
