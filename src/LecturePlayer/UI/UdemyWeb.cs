using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>
/// بيئة WebView2 واحدة لكل البرنامج (فولدر بروفايل واحد = جلسة يوديمي واحدة محفوظة على الجهاز ده بس،
/// ويندوز بيشفّر الكوكيز بتاعته). شباك الدخول والمشغّل والطلبات كلهم بيستخدموها.
/// </summary>
internal static class UdemyWeb
{
    private static Task<CoreWebView2Environment>? _env;

    /// <summary>فولدر البروفايل (جوه بيانات البرنامج على الجهاز — مش في المكتبة ولا بيتزامن).</summary>
    public static string ProfileDir => Path.Combine(Storage.AppDataDir, "webview");

    public static string? RuntimeVersion
    {
        get { try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { return null; } }
    }

    public static Task<CoreWebView2Environment> EnvironmentAsync() =>
        _env ??= CoreWebView2Environment.CreateAsync(null, ProfileDir, new CoreWebView2EnvironmentOptions
        {
            // التشغيل التلقائي للفيديو من غير ما تدوس جوه الصفحة (إنت أصلًا اخترت الدرس)
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
        });

    /// <summary>بيجهّز WebView2 على البيئة المشتركة.</summary>
    public static async Task InitAsync(WebView2 web)
    {
        if (web.CoreWebView2 != null) return;
        var env = await EnvironmentAsync();
        await web.EnsureCoreWebView2Async(env);
        var s = web.CoreWebView2.Settings;
        s.AreDevToolsEnabled = false;
        s.IsStatusBarEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false; // F5/Ctrl+P… مايشتغلوش جوه الصفحة (المفاتيح بتاعة «مسار»)
    }

    private static int _seq;

    /// <summary>
    /// GET لواجهة يوديمي من جوه الصفحة نفسها (نفس الجلسة والكوكيز، ويوديمي بيشوفها كمتصفح عادي).
    /// لازم الصفحة تكون مفتوحة على udemy.com. بيرجّع (الكود، النص).
    /// </summary>
    public static async Task<(int Status, string Body)> FetchAsync(WebView2 web, string path, CancellationToken ct, int timeoutSeconds = 30)
    {
        string key = "k" + Interlocked.Increment(ref _seq);
        string url = JsonSerializer.Serialize(path);
        await web.CoreWebView2.ExecuteScriptAsync(
            "window.__masar = window.__masar || {};" +
            "(async () => { try { const r = await fetch(" + url + ", { credentials: 'include', headers: { 'Accept': 'application/json, text/plain, */*' } });" +
            " window.__masar['" + key + "'] = { s: r.status, b: await r.text() }; }" +
            " catch (e) { window.__masar['" + key + "'] = { s: 0, b: String(e) }; } })(); 'started'");
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(150, ct);
            string raw = await web.CoreWebView2.ExecuteScriptAsync(
                "(() => { const x = window.__masar && window.__masar['" + key + "']; if (!x) return null; delete window.__masar['" + key + "']; return JSON.stringify(x); })()");
            string? json = Unwrap(raw);
            if (json == null) continue;
            using var doc = JsonDocument.Parse(json);
            return (doc.RootElement.GetProperty("s").GetInt32(), doc.RootElement.GetProperty("b").GetString() ?? "");
        }
        throw new TimeoutException("لم يستجب يوديمي");
    }

    /// <summary>ExecuteScriptAsync بيرجّع JSON للقيمة: نص ← بنفكه، null ← null.</summary>
    public static string? Unwrap(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw == "null" || raw == "undefined") return null;
        try { return JsonSerializer.Deserialize<string>(raw); }
        catch (JsonException) { return raw; }
    }

    /// <summary>بيفتح الصفحة ويستنى لحد ما تخلص (أو المهلة).</summary>
    public static async Task NavigateAsync(WebView2 web, string url, CancellationToken ct, int timeoutSeconds = 45)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult(e.IsSuccess);
        web.CoreWebView2.NavigationCompleted += Handler;
        try
        {
            web.CoreWebView2.Navigate(url);
            var t = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct));
            ct.ThrowIfCancellationRequested();
            if (t != done.Task) throw new TimeoutException("تأخرت الصفحة");
        }
        finally { web.CoreWebView2.NavigationCompleted -= Handler; }
    }

    /// <summary>خروج: بيمسح كوكيز يوديمي من البروفايل.</summary>
    public static async Task SignOutAsync(WebView2 web)
    {
        await InitAsync(web);
        var cm = web.CoreWebView2.CookieManager;
        foreach (var site in new[] { Udemy.Origin, "https://udemy.com" })
            foreach (var c in await cm.GetCookiesAsync(site))
                cm.DeleteCookie(c);
    }
}
