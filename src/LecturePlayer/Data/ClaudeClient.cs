using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LecturePlayer.Data;

public sealed record ClaudeReply(string Text, int InputTokens, int OutputTokens, string? StopReason)
{
    public int TotalTokens => InputTokens + OutputTokens;
}

public sealed class ClaudeException : Exception
{
    public int? Status { get; }
    public ClaudeException(string message, int? status = null) : base(message) => Status = status;
}

/// <summary>
/// عميل بسيط لـ Claude Messages API (HttpClient من غير مكتبات).
/// المفتاح بيتحفظ في إعدادات الجهاز ده بس (settings.json في فولدر البيانات)، مش في ملف القائمة المتزامن.
/// </summary>
public sealed class ClaudeClient
{
    public const string Endpoint = "https://api.anthropic.com/v1/messages";
    public const string ApiVersion = "2023-06-01";
    public const string DefaultModel = "claude-sonnet-5-5";
    public const string FastModel = "claude-haiku-5-5";
    public static readonly string[] KnownModels = { "claude-sonnet-5-5", "claude-haiku-5-5", "claude-opus-5-5" };

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public ClaudeClient(string apiKey, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ClaudeException("مفيش مفتاح Claude. حطه من الإعدادات ← الذكاء الاصطناعي (فعّل الوضع المتقدم لو مش ظاهر).");
        _apiKey = apiKey.Trim();
        _http = http ?? SharedHttp;
    }

    public static string BuildBody(string model, string? system, IReadOnlyList<(string Role, string Content)> messages, int maxTokens)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens
        };
        if (!string.IsNullOrWhiteSpace(system)) body["system"] = system;
        var arr = new JsonArray();
        foreach (var (role, content) in messages) arr.Add(new JsonObject { ["role"] = role, ["content"] = content });
        body["messages"] = arr;
        return body.ToJsonString();
    }

    public static ClaudeReply ParseReply(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sb = new StringBuilder();
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var block in content.EnumerateArray())
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text" && block.TryGetProperty("text", out var txt))
                    sb.Append(txt.GetString());
        int inTok = 0, outTok = 0;
        if (root.TryGetProperty("usage", out var u))
        {
            if (u.TryGetProperty("input_tokens", out var i)) inTok = i.GetInt32();
            if (u.TryGetProperty("output_tokens", out var o)) outTok = o.GetInt32();
        }
        string? stop = root.TryGetProperty("stop_reason", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        return new ClaudeReply(sb.ToString(), inTok, outTok, stop);
    }

    public static string ErrorMessage(int status, string body)
    {
        string? msg = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m)) msg = m.GetString();
        }
        catch { }
        string hint = status switch
        {
            401 => "المفتاح غلط أو اتلغى.",
            403 => "المفتاح مالوش صلاحية للموديل ده.",
            404 => "اسم الموديل غلط.",
            429 => "طلبات كتير ورا بعض — استنى شوية وجرب تاني.",
            >= 500 => "مشكلة عند Anthropic — جرب تاني بعد شوية.",
            _ => ""
        };
        return $"خطأ من Claude ({status}). {hint}" + (msg != null ? "\n" + msg : "");
    }

    public async Task<ClaudeReply> SendAsync(string model, string? system, IReadOnlyList<(string Role, string Content)> messages,
        int maxTokens, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(BuildBody(model, system, messages, maxTokens), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", ApiVersion);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new ClaudeException("مفيش اتصال بـ Claude: " + ex.Message); }
        using (resp)
        {
            string body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new ClaudeException(ErrorMessage((int)resp.StatusCode, body), (int)resp.StatusCode);
            return ParseReply(body);
        }
    }

    public Task<ClaudeReply> AskAsync(string model, string? system, string user, int maxTokens, CancellationToken ct) =>
        SendAsync(model, system, new[] { ("user", user) }, maxTokens, ct);
}

/// <summary>حد شهري للتوكنز عشان الفاتورة متفاجئكش.</summary>
public static class AiBudget
{
    public static string MonthKey(DateTime utc) => utc.ToString("yyyy-MM");

    public static long UsedThisMonth(AppSettings s, DateTime nowUtc) => s.AiUsageMonth == MonthKey(nowUtc) ? s.AiUsageTokens : 0;

    /// <summary>null = تمام، أو رسالة بالسبب.</summary>
    public static string? CheckCanSpend(AppSettings s, DateTime nowUtc, long estimate)
    {
        if (s.AiMonthlyTokenCap <= 0) return null;
        long used = UsedThisMonth(s, nowUtc);
        if (used + estimate <= s.AiMonthlyTokenCap) return null;
        return $"وصلت للحد الشهري للذكاء الاصطناعي ({used:N0} من {s.AiMonthlyTokenCap:N0} وحدة).\nزوّد الحد من الإعدادات لو عايز تكمل.";
    }

    public static void Record(AppSettings s, DateTime nowUtc, ClaudeReply r)
    {
        string key = MonthKey(nowUtc);
        if (s.AiUsageMonth != key) { s.AiUsageMonth = key; s.AiUsageTokens = 0; }
        s.AiUsageTokens += r.TotalTokens;
    }

    /// <summary>تقدير تقريبي: حوالي 3.5 حرف للتوكن (العربي أكتر من كده شوية).</summary>
    public static long Estimate(string text, int maxOutput) => text.Length / 3 + maxOutput;
}

/// <summary>
/// حفظ المفتاح متشفّر بحساب الويندوز (DPAPI) — لو حد نسخ settings.json لجهاز تاني مش هيعرف يقراه.
/// على غير الويندوز (الاختبارات) بيتحفظ عادي.
/// </summary>
public static class SecretStore
{
    private const string DpapiPrefix = "dpapi:", PlainPrefix = "plain:";

    public static string Protect(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return "";
        if (OperatingSystem.IsWindows())
        {
            try { return DpapiPrefix + Convert.ToBase64String(Dpapi(Encoding.UTF8.GetBytes(secret), protect: true)); }
            catch { /* نكمل بالعادي */ }
        }
        return PlainPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
    }

    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try
        {
            if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
                return OperatingSystem.IsWindows() ? Encoding.UTF8.GetString(Dpapi(Convert.FromBase64String(stored[DpapiPrefix.Length..]), protect: false)) : "";
            if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
                return Encoding.UTF8.GetString(Convert.FromBase64String(stored[PlainPrefix.Length..]));
        }
        catch { return ""; }
        return stored; // مكتوب بإيد في الملف
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static byte[] Dpapi(byte[] data, bool protect)
    {
        var input = new DataBlob { cbData = data.Length, pbData = Marshal.AllocHGlobal(Math.Max(1, data.Length)) };
        var output = new DataBlob();
        try
        {
            Marshal.Copy(data, 0, input.pbData, data.Length);
            const int UiForbidden = 0x1;
            bool ok = protect
                ? CryptProtectData(ref input, "LecturePlayer", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok) throw new InvalidOperationException("DPAPI " + Marshal.GetLastWin32Error());
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }
}
