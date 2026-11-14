using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace LecturePlayer.Data;

/// <summary>
/// وسيط محلي بين مشغل الفيديو (VLC) وسيرفرات يوتيوب.
///
/// ليه؟ روابط يوتيوب حساسة: لازم تتطلب بنفس الهيدرز اللي أداة يوتيوب استخدمتها، والطلبات الكبيرة بتتبطّأ أو تترفض،
/// والرابط نفسه بينتهي بعد ساعات أو يترفض في النص (403). VLC لوحده بيقف ويقول «مقدرش أفتح الرابط».
///
/// الوسيط بيعمل اللي البرامج الكبيرة بتعمله:
///  • VLC بيطلب من http://127.0.0.1:port/… والوسيط بيجيب من يوتيوب بنفس هيدرز yt-dlp بالظبط.
///  • بيجيب على أجزاء (10 ميجا) زي yt-dlp، فمفيش تبطئة على الطلبات الكبيرة.
///  • التقديم والرجوع (Range) شغالين عادي.
///  • لو يوتيوب رفض جزء (403/410/انتهت الصلاحية) بيجيب رابط جديد لوحده ويكمّل من نفس البايت — من غير ما الفيديو يقف.
/// </summary>
public sealed class StreamProxy : IDisposable
{
    public sealed class Source
    {
        internal Source(string url, IReadOnlyDictionary<string, string> headers, Func<CancellationToken, Task<(string Url, IReadOnlyDictionary<string, string> Headers)?>>? refresh)
        {
            Url = url; Headers = headers; Refresh = refresh;
        }
        internal string Url;
        internal IReadOnlyDictionary<string, string> Headers;
        internal readonly Func<CancellationToken, Task<(string Url, IReadOnlyDictionary<string, string> Headers)?>>? Refresh;
        internal long Length = -1;
        internal string? ContentType;
        internal readonly SemaphoreSlim RefreshLock = new(1, 1);
        internal DateTime LastRefreshUtc = DateTime.MinValue;
        public int Refreshes { get; internal set; }
    }

    public const long ChunkSize = 10 * 1024 * 1024;

    private static readonly Lazy<StreamProxy> _shared = new(() => new StreamProxy());
    public static StreamProxy Shared => _shared.Value;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, Source> _sources = new();
    private readonly HttpClient _http;

    public int Port { get; }

    /// <summary>للتشخيص: بيتنده مع كل حاجة مهمة (رفض، تجديد رابط…).</summary>
    public static Action<string>? Trace { get; set; }

    public StreamProxy()
    {
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(20),
        }) { Timeout = Timeout.InfiniteTimeSpan };

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync();
    }

    /// <summary>بيرجّع الرابط المحلي اللي VLC هيشغّله.</summary>
    public string Register(string url, IReadOnlyDictionary<string, string> headers,
        Func<CancellationToken, Task<(string Url, IReadOnlyDictionary<string, string> Headers)?>>? refresh, out Source source)
    {
        string id = Guid.NewGuid().ToString("N");
        source = new Source(url, headers, refresh);
        _sources[id] = source;
        // نشيل القديم عشان الذاكرة (بنحتفظ بآخر 20 بس)
        if (_sources.Count > 20)
            foreach (var key in _sources.Keys.Take(_sources.Count - 20)) _sources.TryRemove(key, out _);
        return $"http://127.0.0.1:{Port}/s/{id}";
    }

    // =====================================================================

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { break; }
            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            var net = client.GetStream();
            try
            {
                // keep-alive: VLC ممكن يبعت أكتر من طلب على نفس الاتصال
                while (!_cts.IsCancellationRequested)
                {
                    var req = await ReadRequestAsync(net, _cts.Token);
                    if (req == null) return;
                    bool keep = await ServeAsync(net, req.Value, _cts.Token);
                    if (!keep) return;
                }
            }
            catch (IOException) { }          // VLC قفل الاتصال (تقديم/إيقاف) — طبيعي
            catch (OperationCanceledException) { }
            catch (Exception ex) { Trace?.Invoke("proxy: " + ex.GetType().Name + ": " + ex.Message); }
        }
    }

    private readonly record struct Request(string Method, string Path, long? RangeStart, long? RangeEnd);

    private static async Task<Request?> ReadRequestAsync(Stream s, CancellationToken ct)
    {
        var buf = new List<byte>(1024);
        var one = new byte[1];
        while (true)
        {
            int n = await s.ReadAsync(one, ct);
            if (n == 0) return null;
            buf.Add(one[0]);
            int c = buf.Count;
            if (c >= 4 && buf[c - 4] == '\r' && buf[c - 3] == '\n' && buf[c - 2] == '\r' && buf[c - 1] == '\n') break;
            if (c > 16 * 1024) return null;
        }
        string text = System.Text.Encoding.ASCII.GetString(buf.ToArray());
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;
        var first = lines[0].Split(' ');
        if (first.Length < 2) return null;
        long? rs = null, re = null;
        foreach (var line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            if (!line[..colon].Trim().Equals("Range", StringComparison.OrdinalIgnoreCase)) continue;
            (rs, re) = ParseRange(line[(colon + 1)..].Trim());
        }
        return new Request(first[0], first[1], rs, re);
    }

    /// <summary>"bytes=100-" أو "bytes=100-200" (public عشان الفحص).</summary>
    public static (long? Start, long? End) ParseRange(string value)
    {
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return (null, null);
        var part = value[6..].Split(',')[0].Trim();
        int dash = part.IndexOf('-');
        if (dash < 0) return (null, null);
        long? start = long.TryParse(part[..dash], out var a) ? a : null;
        long? end = long.TryParse(part[(dash + 1)..], out var b) ? b : null;
        return (start, end);
    }

    private async Task<bool> ServeAsync(Stream net, Request req, CancellationToken ct)
    {
        string id = req.Path.StartsWith("/s/") ? req.Path[3..].Split('?', '/')[0] : "";
        if (!_sources.TryGetValue(id, out var src))
        {
            await WriteHeadAsync(net, "404 Not Found", new() { ["Content-Length"] = "0" }, ct);
            return false;
        }

        long start = req.RangeStart ?? 0;
        // أول طلب: نعرف الحجم الكلي ونوع الملف
        if (src.Length < 0)
        {
            using var probe = await FetchAsync(src, 0, 0, ct);
            if (probe == null)
            {
                await WriteHeadAsync(net, "502 Bad Gateway", new() { ["Content-Length"] = "0" }, ct);
                return false;
            }
        }
        long total = src.Length;
        if (total > 0 && start >= total)
        {
            await WriteHeadAsync(net, "416 Range Not Satisfiable", new() { ["Content-Range"] = $"bytes */{total}", ["Content-Length"] = "0" }, ct);
            return true;
        }
        long end = total > 0 ? Math.Min(req.RangeEnd ?? total - 1, total - 1) : -1;

        var head = new Dictionary<string, string>
        {
            ["Accept-Ranges"] = "bytes",
            ["Content-Type"] = src.ContentType ?? "application/octet-stream",
            ["Connection"] = "keep-alive",
        };
        if (end >= 0) head["Content-Length"] = (end - start + 1).ToString();
        bool partial = req.RangeStart != null && total > 0;
        if (partial) head["Content-Range"] = $"bytes {start}-{end}/{total}";
        await WriteHeadAsync(net, partial ? "206 Partial Content" : "200 OK", head, ct);
        if (req.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)) return true;

        long pos = start;
        var buffer = new byte[256 * 1024];
        int emptyChunks = 0;
        while (end < 0 || pos <= end)
        {
            long chunkEnd = end < 0 ? pos + ChunkSize - 1 : Math.Min(end, pos + ChunkSize - 1);
            using var resp = await FetchAsync(src, pos, chunkEnd, ct);
            if (resp == null) return false; // فشل بعد كل المحاولات: نقفل الاتصال وVLC بيعيد الطلب
            await using var body = await resp.Content.ReadAsStreamAsync(ct);
            long got = 0;
            while (true)
            {
                int read;
                // القراية من يوتيوب: لو اتقطعت نكمّل من pos في اللفة الجاية
                try { read = await body.ReadAsync(buffer, ct); }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    Trace?.Invoke($"proxy: upstream cut at {pos}: {ex.Message}");
                    break;
                }
                if (read <= 0) break;
                // الكتابة لـ VLC: لو قفل الاتصال (تقديم/إيقاف) الـ Exception بيطلع ويقفل الطلب ده
                await net.WriteAsync(buffer.AsMemory(0, read), ct);
                pos += read;
                got += read;
            }
            if (got == 0)
            {
                if (end < 0 || ++emptyChunks >= 3) break;
            }
            else emptyChunks = 0;
            if (total > 0 && pos >= total) break;
        }
        await net.FlushAsync(ct);
        return end >= 0 && pos > end;
    }

    /// <summary>بيجيب جزء من يوتيوب، ولو اترفض بيجدد الرابط ويحاول تاني (لحد 4 مرات).</summary>
    private async Task<HttpResponseMessage?> FetchAsync(Source src, long from, long to, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            string url;
            IReadOnlyDictionary<string, string> headers;
            lock (src) { url = src.Url; headers = src.Headers; }
            HttpResponseMessage? resp = null;
            try
            {
                var msg = new HttpRequestMessage(HttpMethod.Get, url);
                foreach (var (k, v) in headers)
                {
                    if (k.Equals("Range", StringComparison.OrdinalIgnoreCase) || k.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                    msg.Headers.TryAddWithoutValidation(k, v);
                }
                msg.Headers.Range = new RangeHeaderValue(from, to);
                resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var cr = resp.Content.Headers.ContentRange;
                    if (src.Length < 0)
                    {
                        if (cr?.Length is long len) src.Length = len;
                        else if (resp.StatusCode == HttpStatusCode.OK && resp.Content.Headers.ContentLength is long cl) src.Length = cl;
                        src.ContentType = resp.Content.Headers.ContentType?.ToString();
                    }
                    // السيرفر تجاهل الـ Range ورجّع الملف من الأول: مينفعش نكمّل منه من نص الملف
                    if (resp.StatusCode == HttpStatusCode.OK && from > 0)
                    {
                        Trace?.Invoke("proxy: upstream ignored Range");
                        resp.Dispose();
                        return null;
                    }
                    return resp;
                }
                Trace?.Invoke($"proxy: upstream {(int)resp.StatusCode} at {from} (attempt {attempt + 1})");
            }
            catch (OperationCanceledException) { resp?.Dispose(); throw; }
            catch (Exception ex) { Trace?.Invoke($"proxy: upstream error at {from}: {ex.Message}"); }

            int status = resp != null ? (int)resp.StatusCode : 0;
            resp?.Dispose();
            if (attempt == 3) break;
            // 403/410/404 = الرابط اترفض أو انتهى ← رابط جديد. غير كده (شبكة/5xx) ← نستنى شوية ونعيد.
            if (status is 403 or 404 or 410 or 401 && src.Refresh != null) await RefreshAsync(src, ct);
            else await Task.Delay(TimeSpan.FromSeconds(1 + attempt * 2), ct);
        }
        return null;
    }

    private static async Task RefreshAsync(Source src, CancellationToken ct)
    {
        await src.RefreshLock.WaitAsync(ct);
        try
        {
            // لو جزء تاني لسه مجدد الرابط من ثواني، نستخدم الجديد بدل ما نطلب تاني
            if (DateTime.UtcNow - src.LastRefreshUtc < TimeSpan.FromSeconds(15)) return;
            Trace?.Invoke("proxy: refreshing stream url");
            var fresh = await src.Refresh!(ct);
            if (fresh is { } f)
            {
                lock (src) { src.Url = f.Url; src.Headers = f.Headers; }
                src.Refreshes++;
            }
            src.LastRefreshUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Trace?.Invoke("proxy: refresh failed: " + ex.Message); }
        finally { src.RefreshLock.Release(); }
    }

    private static async Task WriteHeadAsync(Stream net, string status, Dictionary<string, string> headers, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append("\r\n");
        foreach (var (k, v) in headers) sb.Append(k).Append(": ").Append(v).Append("\r\n");
        sb.Append("\r\n");
        var bytes = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
        await net.WriteAsync(bytes, ct);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _http.Dispose();
    }
}
