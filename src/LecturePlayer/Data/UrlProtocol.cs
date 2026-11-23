using Microsoft.Win32;

namespace LecturePlayer.Data;

/// <summary>
/// تسجيل مؤقت لرابط (زي moodlemobile://) على «مسار» في حساب الويندوز بتاعك (HKCU — من غير صلاحيات مسؤول)،
/// عشان المتصفح يرجّع نتيجة الدخول للبرنامج. بعد ما نخلص بنرجّع اللي كان مسجّل قبلنا (لو فيه تطبيق Moodle مثلًا).
/// </summary>
internal sealed class UrlProtocol : IDisposable
{
    private readonly string _scheme;
    private readonly string? _previousCommand;
    private readonly bool _existed;
    private bool _disposed;

    private UrlProtocol(string scheme, bool existed, string? previous)
    {
        _scheme = scheme;
        _existed = existed;
        _previousCommand = previous;
    }

    private static string KeyPath(string scheme) => @"Software\Classes\" + scheme;

    public static UrlProtocol Register(string scheme, string exePath)
    {
        string? previous;
        bool existed;
        using (var old = Registry.CurrentUser.OpenSubKey(KeyPath(scheme) + @"\shell\open\command"))
        {
            existed = Registry.CurrentUser.OpenSubKey(KeyPath(scheme)) is { } k && Close(k);
            previous = old?.GetValue("") as string;
        }
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath(scheme)))
        {
            key.SetValue("", "URL:" + scheme);
            key.SetValue("URL Protocol", "");
            using var cmd = key.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        return new UrlProtocol(scheme, existed, previous);
    }

    private static bool Close(RegistryKey k) { k.Dispose(); return true; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (!_existed) Registry.CurrentUser.DeleteSubKeyTree(KeyPath(_scheme), throwOnMissingSubKey: false);
            else if (_previousCommand != null)
            {
                using var cmd = Registry.CurrentUser.CreateSubKey(KeyPath(_scheme) + @"\shell\open\command");
                cmd.SetValue("", _previousCommand);
            }
        }
        catch (Exception ex) { Log.Error("url protocol restore", ex); }
    }
}
