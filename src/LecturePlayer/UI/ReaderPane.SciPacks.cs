using Microsoft.Web.WebView2.Core;

namespace LecturePlayer.UI;

internal sealed partial class ReaderPane
{
    /// <summary>
    /// /scipack/... = رسوم مكتبة السبورة العلمية: من الحفظ المحلي لو موجودة، وإلا بتتنزل من مصدرها المسموح وتتحفظ.
    /// الطلب بيستنّى التنزيل (deferral) من غير ما الواجهة تقف.
    /// </summary>
    private void SciPackRequest(CoreWebView2WebResourceRequestedEventArgs e, CoreWebView2Environment env, string route, string query)
    {
        var target = Data.SciPacks.Resolve(route, query);
        if (target == null)
        {
            e.Response = env.CreateWebResourceResponse(null, 403, "Forbidden", "Content-Type: text/plain");
            return;
        }
        var (url, cache) = target.Value;
        if (cache != null && File.Exists(cache))
        {
            e.Response = FileResponse(env, cache, null);
            return;
        }
        var deferral = e.GetDeferral();
        _ = Task.Run(async () =>
        {
            CoreWebView2WebResourceResponse? resp = null;
            try
            {
                if (cache != null)
                {
                    string? file = await Data.SciPacks.FileAsync(url, cache);
                    await UiAsync(() => resp = file != null ? FileResponse(env, file, null)
                        : env.CreateWebResourceResponse(null, 502, "Bad Gateway", "Content-Type: text/plain"));
                }
                else
                {
                    var (status, body) = await Data.SciPacks.GetAsync(url);
                    await UiAsync(() => resp = env.CreateWebResourceResponse(new MemoryStream(body), status, status == 200 ? "OK" : "Error",
                        $"Content-Type: application/json\r\nContent-Length: {body.Length}\r\nCache-Control: no-store"));
                }
            }
            catch (Exception ex) { Data.Log.Info("scipack: " + ex.Message); }
            await UiAsync(() =>
            {
                try
                {
                    e.Response = resp ?? env.CreateWebResourceResponse(null, 500, "Error", "Content-Type: text/plain");
                }
                catch (Exception) { }
                deferral.Complete();
            });
        });
    }

    private Task UiAsync(Action a)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try { BeginInvoke(() => { try { a(); } finally { tcs.TrySetResult(); } }); }
        catch (Exception) { tcs.TrySetResult(); }
        return tcs.Task;
    }
}
