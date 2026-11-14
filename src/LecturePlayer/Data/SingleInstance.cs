using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace LecturePlayer.Data;

/// <summary>
/// نسخة واحدة من البرنامج: لو فتحت فيديو بـ «فتح بواسطة» والبرنامج شغال أصلًا،
/// النسخة الجديدة بتبعت مسار الملف للنسخة الشغالة وتقفل، بدل ما يفتح شباك تاني على نفس المكتبة.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string Name =
        "LecturePlayer." + new string((Environment.UserDomainName + "." + Environment.UserName)
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    private static string PipeName => Name + "." + System.Diagnostics.Process.GetCurrentProcess().SessionId;

    private readonly Mutex _mutex;
    private volatile bool _disposed;

    public bool IsFirst { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(true, @"Local\" + Name, out bool created);
        IsFirst = created;
    }

    /// <summary>من النسخة التانية: ابعت الملفات للنسخة الشغالة. false لو ماردتش.</summary>
    public static bool Send(IReadOnlyList<string> paths, int timeoutMs = 15000)
    {
        try
        {
            try { AllowSetForegroundWindow(-1); } catch { } // عشان النسخة الشغالة تقدر تيجي قدام
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var w = new StreamWriter(client, new UTF8Encoding(false));
            foreach (string p in paths) w.WriteLine(p);
            w.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("single-instance send", ex);
            return false;
        }
    }

    /// <summary>في النسخة الشغالة: استقبل الملفات اللي جاية من نسخ تانية.</summary>
    public void Listen(Action<IReadOnlyList<string>> onPaths)
    {
        if (!IsFirst) return;
        var t = new Thread(() =>
        {
            while (!_disposed)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                    server.WaitForConnection();
                    using var r = new StreamReader(server, Encoding.UTF8);
                    var list = new List<string>();
                    string? line;
                    while ((line = r.ReadLine()) != null)
                        if (line.Length > 0) list.Add(line);
                    if (list.Count > 0) onPaths(list);
                }
                catch (Exception ex)
                {
                    if (_disposed) return;
                    Log.Error("single-instance listen", ex);
                    Thread.Sleep(500);
                }
            }
        })
        { IsBackground = true, Name = "single-instance" };
        t.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        try { if (IsFirst) _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
