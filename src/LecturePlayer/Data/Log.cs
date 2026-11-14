using System.Text;

namespace LecturePlayer.Data;

/// <summary>سجل بسيط في Data\logs\yyyy-MM.log — عشان أي مشكلة يتعرف سببها.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string LogDir => Path.Combine(Storage.AppDataDir, "logs");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string where, Exception ex) => Write("ERROR", $"{where}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    public static void Error(string where, string message) => Write("ERROR", $"{where}: {message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                string file = Path.Combine(LogDir, DateTime.Now.ToString("yyyy-MM") + ".log");
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* السجل عمره ما يوقّع البرنامج */ }
    }
}
