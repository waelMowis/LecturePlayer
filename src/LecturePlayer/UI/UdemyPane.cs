using System.Globalization;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LecturePlayer.UI;

/// <summary>
/// مشغّل يوديمي جوه مساحة الفيديو: صفحة الدرس الحقيقية (المشغّل والحماية بتوع يوديمي)، و«مسار» بيتحكم في الفيديو
/// (تشغيل/إيقاف/تقديم/سرعة/صوت) ويقرا وقته عشان مكانك ووقت المذاكرة و«اتشاف».
/// مفيش تنزيل ولا قراية للصورة — الفيديو بيفضل محمي جوه المتصفح.
/// </summary>
internal sealed class UdemyPane : Panel
{
    /// <summary>حالة الفيديو في الصفحة (آخر قراية).</summary>
    public sealed record PlayerState(bool HasVideo, double TimeSec, double DurationSec, bool Paused, bool Ended, double Rate, string Href)
    {
        public long TimeMs => (long)(TimeSec * 1000);
        public long DurationMs => (long)(DurationSec * 1000);
        public bool Playing => HasVideo && !Paused && !Ended;
    }

    public WebView2 Web { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    public PlayerState? State { get; private set; }
    /// <summary>مفتاح اتداس جوه الصفحة (بيتنفذ كاختصار من اختصارات «مسار»).</summary>
    public event Action<Keys>? KeyForwarded;
    /// <summary>زرار ملء الشاشة بتاع يوديمي ← ملء الشاشة بتاع «مسار».</summary>
    public event Action<bool>? FullscreenChanged;
    /// <summary>الصفحة اتفتحت/فشلت (للرسايل).</summary>
    public event Action<bool, string>? Navigated;

    private bool _ready;
    private string? _cleanScriptId;
    private bool _clean = true;
    private long _pendingStartMs = -1;
    private float _pendingRate = 1f;
    private int _pendingVolume = -1;
    private bool _polling;
    private int _gen;
    private long _openedTick;

    /// <summary>الدرس اللي اتفتح آخر مرة (رقمه على يوديمي).</summary>
    public long OpenedLectureId { get; private set; }
    /// <summary>من إمتى الصفحة اتفتحت (ms) — يوديمي ممكن يعمل تحويل في الأول، فبنستنى قبل ما نصدّق تغيّر الدرس.</summary>
    public long MsSinceOpen => Environment.TickCount64 - _openedTick;

    public UdemyPane()
    {
        BackColor = Color.Black;
        Controls.Add(Web);
        Visible = false;
    }

    /// <summary>العرض النضيف: من غير هيدر يوديمي وقوايمه (الشجرة بتاعتك هي القايمة).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool CleanView
    {
        get => _clean;
        set
        {
            if (_clean == value) return;
            _clean = value;
            if (_ready) _ = ApplyCleanAsync();
        }
    }

    public async Task EnsureReadyAsync()
    {
        if (_ready) return;
        await UdemyWeb.InitAsync(Web);
        var core = Web.CoreWebView2;
        core.WebMessageReceived += OnMessage;
        core.ContainsFullScreenElementChanged += (_, _) => FullscreenChanged?.Invoke(core.ContainsFullScreenElement);
        core.NavigationCompleted += (_, e) => Navigated?.Invoke(e.IsSuccess, core.Source);
        core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); }; // روابط «شباك جديد» تفتح هنا
        await core.AddScriptToExecuteOnDocumentCreatedAsync(KeyScript);
        _ready = true;
        await ApplyCleanAsync();
    }

    /// <summary>بيفتح الدرس. startMs = مكانك المحفوظ (لو يوديمي ابتدى من الأول بنرجّعه)، والسرعة والصوت بيتطبقوا أول ما الفيديو يظهر.</summary>
    public async Task OpenAsync(string url, long startMs, float rate, int volume)
    {
        State = null;
        _gen++;
        _openedTick = Environment.TickCount64;
        OpenedLectureId = Data.Udemy.LectureId(url);
        await EnsureReadyAsync();
        _pendingStartMs = startMs > 3000 ? startMs : -1;
        _pendingRate = rate;
        _pendingVolume = volume;
        // نفس الكورس: يوديمي صفحة واحدة بتغيّر الدرس من غير تحميل — بس الأبسط والأضمن نفتح الرابط
        Web.CoreWebView2.Navigate(url);
    }

    /// <summary>بيقرا حالة الفيديو (من غير ما يستنى — النتيجة في State).</summary>
    public async void Poll()
    {
        if (!_ready || _polling || Web.CoreWebView2 == null) return;
        _polling = true;
        int gen = _gen;
        try
        {
            string raw = await Web.CoreWebView2.ExecuteScriptAsync(PollScript);
            if (gen != _gen) return; // اتفتح درس تاني وإحنا مستنيين
            string? json = UdemyWeb.Unwrap(raw);
            if (json == null) return;
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            bool has = r.GetProperty("v").GetInt32() == 1;
            State = new PlayerState(has,
                has ? r.GetProperty("t").GetDouble() : 0,
                has ? r.GetProperty("d").GetDouble() : 0,
                !has || r.GetProperty("p").GetBoolean(),
                has && r.GetProperty("e").GetBoolean(),
                has ? r.GetProperty("r").GetDouble() : 1,
                r.GetProperty("h").GetString() ?? "");
            if (has && State.DurationSec > 0) ApplyPending();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { }
        catch (Exception ex) { Data.Log.Info("udemy poll: " + ex.Message); }
        finally { _polling = false; }
    }

    private void ApplyPending()
    {
        if (_pendingStartMs > 0 && State != null)
        {
            // يوديمي بيفتكر مكانك هو كمان: نرجّع مكان «مسار» بس لو الفرق كبير
            if (Math.Abs(State.TimeMs - _pendingStartMs) > 5000) SeekTo(_pendingStartMs);
            _pendingStartMs = -1;
        }
        if (_pendingRate > 0) { SetRate(_pendingRate); _pendingRate = -1; }
        if (_pendingVolume >= 0) { SetVolume(_pendingVolume); _pendingVolume = -1; }
    }

    // ---------------- التحكم ----------------

    private void Run(string js)
    {
        if (!_ready || Web.CoreWebView2 == null) return;
        _ = Web.CoreWebView2.ExecuteScriptAsync("(() => { const v = document.querySelector('video'); if (!v) return; " + js + " })()");
    }

    public void Play() => Run("v.play();");
    public void Pause() => Run("v.pause();");
    public void Toggle() => Run("if (v.paused || v.ended) v.play(); else v.pause();");
    public void SeekTo(long ms) => Run($"v.currentTime = {Sec(ms)};");
    public void SeekBy(long ms) => Run($"v.currentTime = Math.max(0, Math.min((v.duration || 1e9) - 0.5, v.currentTime + {Sec(ms)}));");
    public void SetRate(float rate) => Run($"v.playbackRate = {rate.ToString("0.###", CultureInfo.InvariantCulture)};");
    /// <summary>0..200 زي «مسار» (المتصفح أقصاه 100%).</summary>
    public void SetVolume(int volume) => Run($"v.volume = {Math.Clamp(volume / 100.0, 0, 1).ToString("0.###", CultureInfo.InvariantCulture)};");
    public void SetMuted(bool muted) => Run($"v.muted = {(muted ? "true" : "false")};");

    private static string Sec(long ms) => (ms / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>بيوقف الفيديو ويقفل الصفحة (صفحة يوديمي فاتحة في الخلفية بتصحّي المعالج وبتاكل بطارية).</summary>
    public void Stop()
    {
        if (!_ready || Web.CoreWebView2 == null) return;
        Pause();
        _gen++;
        State = null;
        OpenedLectureId = 0;
        _pendingStartMs = -1;
        try { Web.CoreWebView2.Navigate("about:blank"); } catch { }
    }

    /// <summary>«مسار» خرج من ملء الشاشة: الصفحة كمان تخرج.</summary>
    public void ExitPageFullscreen()
    {
        if (!_ready || Web.CoreWebView2 == null || !Web.CoreWebView2.ContainsFullScreenElement) return;
        _ = Web.CoreWebView2.ExecuteScriptAsync("document.fullscreenElement && document.exitFullscreen()");
    }

    /// <summary>الدرس اتغيّر جوه الصفحة (التالي التلقائي بتاع يوديمي) وإحنا عرفنا: مانرجّعش له مكان قديم.</summary>
    public void AdoptLecture(long lectureId)
    {
        OpenedLectureId = lectureId;
        _pendingStartMs = -1;
    }

    // ---------------- الصفحة ----------------

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var r = doc.RootElement;
            var key = (Keys)r.GetProperty("k").GetInt32();
            if (r.GetProperty("c").GetBoolean()) key |= Keys.Control;
            if (r.GetProperty("s").GetBoolean()) key |= Keys.Shift;
            if (r.GetProperty("a").GetBoolean()) key |= Keys.Alt;
            KeyForwarded?.Invoke(key);
        }
        catch (Exception ex) { Data.Log.Info("udemy key: " + ex.Message); }
    }

    private async Task ApplyCleanAsync()
    {
        var core = Web.CoreWebView2;
        if (_cleanScriptId != null) { core.RemoveScriptToExecuteOnDocumentCreated(_cleanScriptId); _cleanScriptId = null; }
        if (_clean) _cleanScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(CleanScript);
        await core.ExecuteScriptAsync(_clean ? CleanScript : "document.getElementById('masar-clean')?.remove(); document.querySelector('[data-masar-fit]')?.removeAttribute('data-masar-fit');");
    }

    /// <summary>
    /// المفاتيح جوه الصفحة بتروح لـ «مسار» (نفس اختصاراتك)، إلا وإنت بتكتب في خانة (أسئلة/ملاحظات يوديمي).
    /// </summary>
    private const string KeyScript = """
        (() => {
          if (window.__masarKeys) return; window.__masarKeys = true;
          window.addEventListener('keydown', e => {
            const t = e.target, tag = ((t && t.tagName) || '').toLowerCase();
            if (tag === 'input' || tag === 'textarea' || tag === 'select' || (t && t.isContentEditable)) return;
            if (['Shift', 'Control', 'Alt', 'Meta'].includes(e.key)) return;
            e.preventDefault(); e.stopImmediatePropagation();
            try { window.chrome.webview.postMessage(JSON.stringify({ k: e.keyCode, c: e.ctrlKey, s: e.shiftKey, a: e.altKey })); } catch (x) { }
          }, true);
        })();
        """;

    /// <summary>
    /// العرض النضيف: بيخبي هيدر يوديمي والقايمة الجانبية واللي تحت الفيديو، والفيديو ياخد المساحة كلها.
    /// أسماء العناصر في يوديمي بتتغير، فبندوّر على أجزاء ثابتة من الأسماء — لو حاجة اتغيرت الصفحة بتفضل شغالة عادي.
    /// </summary>
    private const string CleanScript = """
        (() => {
          const css = `
            [class*="app--header--"], [class*="header--header--"], [data-purpose="header"],
            [class*="app--sidebar-column--"], [class*="sidebar--sidebar--"], [data-purpose="sidebar"],
            [class*="app--dashboard--"], [class*="app--body-container--"] > footer, footer[class*="footer"],
            [class*="ud-app-loader"] + header { display: none !important; }
            [class*="app--content-column--"], [class*="app--body-container--"], [class*="app--column-container--"] {
              width: 100% !important; max-width: none !important; margin: 0 !important; padding: 0 !important; }
            html, body { background: #000 !important; overflow: hidden !important; }
            [data-masar-fit] { position: fixed !important; inset: 0 !important; width: 100vw !important; height: 100vh !important;
              max-width: none !important; max-height: none !important; min-height: 0 !important; margin: 0 !important;
              z-index: 2147483000 !important; background: #000 !important; transform: none !important; }`;
          const add = () => {
            if (!document.head || document.getElementById('masar-clean')) return;
            const s = document.createElement('style'); s.id = 'masar-clean'; s.textContent = css; document.head.appendChild(s);
          };
          // المشغّل ياخد المساحة كلها: يوديمي بيحدد ارتفاعه (ويسيب مكان للتابات اللي تحت)، فبنلاقي «صندوق المشغّل»
          // = أبعد أب للفيديو بنفس مقاسه (الفيديو + أزراره)، ونخليه يملا الصفحة. من غير أسماء عناصر، فمش بيبوظ لو يوديمي غيّرها.
          const same = (a, b) => Math.abs(a.left - b.left) < 3 && Math.abs(a.top - b.top) < 3 &&
                                 Math.abs(a.width - b.width) < 3 && Math.abs(a.height - b.height) < 3;
          const fit = () => {
            if (!document.getElementById('masar-clean')) return; // العرض النضيف مقفول
            const v = document.querySelector('video');
            const cur = document.querySelector('[data-masar-fit]');
            if (cur && v && cur.contains(v)) return;
            if (cur) cur.removeAttribute('data-masar-fit');
            if (!v) return;
            const r = v.getBoundingClientRect();
            if (r.width < 50 || r.height < 50) return; // لسه بيتحمّل
            let root = v;
            for (let p = v.parentElement; p && p !== document.body && p !== document.documentElement; p = p.parentElement)
              if (same(p.getBoundingClientRect(), r)) root = p;
            root.setAttribute('data-masar-fit', '');
          };
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', add); else add();
          if (!window.__masarFit) { window.__masarFit = setInterval(fit, 700); }
        })();
        """;

    private const string PollScript = """
        (() => {
          const v = document.querySelector('video');
          if (!v) return JSON.stringify({ v: 0, h: location.href });
          return JSON.stringify({ v: 1, t: v.currentTime || 0, d: isFinite(v.duration) ? v.duration : 0,
                                  p: v.paused, e: v.ended, r: v.playbackRate || 1, h: location.href });
        })()
        """;
}
