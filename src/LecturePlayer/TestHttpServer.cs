using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LecturePlayer;

/// <summary>
/// سيرفر HTTP صغير للفحص التلقائي بس: بيقدّم ملفات (مع Range عشان «يكمّل من مكان ماوقف»)،
/// وممكن يبطّأ عشان نلحق نوقف التنزيل في النص. TcpListener عادي (من غير صلاحيات أدمن).
/// </summary>
internal sealed class TestHttpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Dictionary<string, (string File, string Type, int BytesPerSecond)> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    /// <summary>كل Range اتطلب (مثلًا «bytes=123456-»).</summary>
    public List<string> Ranges { get; } = new();

    public TestHttpServer()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public string Url(string path) => $"http://127.0.0.1:{Port}/{path}";

    public void Serve(string path, string file, string type, int bytesPerSecond = 0) => _routes[path] = (file, type, bytesPerSecond);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { return; }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                string? request = await reader.ReadLineAsync();
                if (request == null) return;
                string? range = null;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = line[6..].Trim();

                var parts = request.Split(' ');
                string method = parts[0];
                string path = parts.Length > 1 ? parts[1].TrimStart('/').Split('?')[0] : "";
                if (!_routes.TryGetValue(path, out var route) || !File.Exists(route.File))
                {
                    await WriteAsync(stream, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    return;
                }
                long length = new FileInfo(route.File).Length, start = 0, end = length - 1;
                bool partial = false;
                if (range != null && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                {
                    lock (Ranges) Ranges.Add(range);
                    var se = range[6..].Split('-');
                    if (long.TryParse(se[0], out long s)) start = s;
                    if (se.Length > 1 && long.TryParse(se[1], out long e2)) end = Math.Min(e2, length - 1);
                    partial = true;
                }
                if (start >= length)
                {
                    await WriteAsync(stream, $"HTTP/1.1 416 Range Not Satisfiable\r\nContent-Range: bytes */{length}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    return;
                }
                long count = end - start + 1;
                var head = new StringBuilder();
                head.Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n");
                head.Append($"Content-Type: {route.Type}\r\nContent-Length: {count}\r\nAccept-Ranges: bytes\r\nConnection: close\r\n");
                if (partial) head.Append($"Content-Range: bytes {start}-{end}/{length}\r\n");
                head.Append("\r\n");
                await WriteAsync(stream, head.ToString());
                if (method == "HEAD") return;

                using var fs = File.OpenRead(route.File);
                fs.Position = start;
                var buf = new byte[32 * 1024];
                long left = count;
                while (left > 0 && !_cts.IsCancellationRequested)
                {
                    int n = await fs.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)));
                    if (n <= 0) break;
                    await stream.WriteAsync(buf.AsMemory(0, n));
                    left -= n;
                    if (route.BytesPerSecond > 0) await Task.Delay(Math.Max(1, n * 1000 / route.BytesPerSecond));
                }
            }
            catch { /* العميل قفل (إلغاء/إيقاف) */ }
        }
    }

    private static Task WriteAsync(NetworkStream s, string text) => s.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }
}
