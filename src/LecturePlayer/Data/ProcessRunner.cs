using System.Diagnostics;
using System.Text;

namespace LecturePlayer.Data;

/// <summary>تشغيل برنامج خارجي (ffmpeg، whisper، dotnet…) وقراية الناتج، مع مهلة وإلغاء.</summary>
public static class ProcessRunner
{
    public sealed record Result(int ExitCode, string StdOut, string StdErr, bool TimedOut);

    /// <param name="onLine">بيتنده مع كل سطر من stdout أو stderr (للتقدم).</param>
    public static async Task<Result> RunAsync(
        string exe,
        IEnumerable<string> args,
        TimeSpan timeout,
        CancellationToken ct = default,
        Action<string>? onLine = null,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Path.GetTempPath()
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        // الأدوات اللي في فولدر tools تلاقي بعض (مثلًا yt-dlp يلاقي ffmpeg و deno)
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        psi.Environment["PATH"] = Tools.ToolsDir + Path.PathSeparator + Tools.FfmpegDir + Path.PathSeparator + path;
        if (environment != null)
            foreach (var kv in environment) psi.Environment[kv.Key] = kv.Value;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (stdout) stdout.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (stderr) stderr.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            process.WaitForExit(); // نستنى آخر سطور الـ output
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            timedOut = true;
        }

        string o, e2;
        lock (stdout) o = stdout.ToString();
        lock (stderr) e2 = stderr.ToString();
        return new Result(timedOut ? -1 : process.ExitCode, o, e2, timedOut);
    }

    /// <summary>آخر سطر مفيد في رسالة خطأ.</summary>
    public static string LastErrorLine(string stderr, string fallback)
    {
        var lines = stderr.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return lines.LastOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)) ?? lines.LastOrDefault() ?? fallback;
    }
}
