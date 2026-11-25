using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>غلطة من نوشن برسالة مفهومة (Status = كود HTTP، Code = كود نوشن زي object_not_found).</summary>
public sealed class NotionException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public NotionException(int status, string code, string message) : base(message) { Status = status; Code = code; }

    /// <summary>رسالة بالعربي للمستخدم.</summary>
    public string Friendly => (Status, Code) switch
    {
        (401, _) => "مفتاح نوشن غير صحيح أو أُلغي — أنشئ مفتاحًا جديدًا من notion.so/my-integrations والصقه في الإعدادات.",
        (403, _) => "الـ integration لا يملك صلاحية — تأكد من تفعيل «Insert content» و«Update content» في إعداداته.",
        (404, _) or (_, "object_not_found") => "لم يجد نوشن الصفحة — تأكد من مشاركة صفحة «مسار» مع الـ integration (⋯ ← Connections).",
        (429, _) => "طلب نوشن الانتظار قليلًا (ضغط طلبات) — سيكمل تلقائيًا.",
        (400, "validation_error") => "رفض نوشن البيانات: " + Message,
        (0, _) => "لا يوجد اتصال بنوشن — تأكد من الإنترنت.",
        _ => $"نوشن رد بـ {Status}: {Message}"
    };
}

/// <summary>
/// واجهة نوشن الرسمية (REST) بمفتاح Internal Integration. نسخة الـ API متثبتة، والطلبات بتمشي في طابور
/// (حوالي 3 في الثانية) وبتستنى وتعيد لو نوشن قال «استنى» (429) أو حصلت مشكلة مؤقتة.
/// </summary>
public sealed class NotionClient : IDisposable
{
    public const string ApiVersion = "2026-03-11";
    public static string DefaultBase = "https://api.notion.com/v1/";
    /// <summary>للفحص التلقائي: نوشن وهمي.</summary>
    public static HttpMessageHandler? TestHandler;

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _nextSlotUtc = DateTime.MinValue;
    /// <summary>أقل مسافة بين طلبين (نوشن بيسمح بحوالي 3 في الثانية).</summary>
    public TimeSpan MinGap { get; set; } = TimeSpan.FromMilliseconds(340);
    /// <summary>عدد الطلبات اللي اتبعتت (للسجل والفحص).</summary>
    public int Requests { get; private set; }

    public NotionClient(string token, HttpMessageHandler? handler = null, string? baseUrl = null)
    {
        handler ??= TestHandler;
        _http = handler != null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
        _http.BaseAddress = new Uri(baseUrl ?? DefaultBase);
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Add("Notion-Version", ApiVersion);
        if (handler == TestHandler && handler != null) MinGap = TimeSpan.Zero;
    }

    public void Dispose() => _http.Dispose();

    // ---------------- المعرّفات ----------------

    private static readonly Regex HexId = new(@"([0-9a-fA-F]{32})(?![0-9a-fA-F])|([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.Compiled);

    /// <summary>رابط صفحة نوشن أو رقمها ← الرقم بالشكل القياسي (بالشرط). null لو مفيش رقم.</summary>
    public static string? ParseId(string? urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId)) return null;
        string s = urlOrId.Trim();
        // الرقم في آخر الرابط (بعد اسم الصفحة)، قبل ? و#
        int cut = s.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) s = s[..cut];
        var all = HexId.Matches(s);
        if (all.Count == 0) return null;
        string hex = all[^1].Value.Replace("-", "").ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    public static string PageUrl(string id) => "https://www.notion.so/" + id.Replace("-", "");

    // ---------------- الطلبات ----------------

    public async Task<JsonObject> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            await _gate.WaitAsync(ct);
            HttpResponseMessage? resp = null;
            string text;
            try
            {
                var wait = _nextSlotUtc - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                using var req = new HttpRequestMessage(method, path);
                if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                Requests++;
                try { resp = await _http.SendAsync(req, ct); }
                catch (HttpRequestException ex) when (attempt < 3) { Log.Info("notion net: " + ex.Message); await Task.Delay(1500 * attempt, ct); continue; }
                catch (HttpRequestException ex) { throw new NotionException(0, "network", ex.Message); }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < 3) { await Task.Delay(1500 * attempt, ct); continue; }
                text = await resp.Content.ReadAsStringAsync(ct);
                _nextSlotUtc = DateTime.UtcNow + MinGap;

                if ((int)resp.StatusCode == 429)
                {
                    double secs = resp.Headers.RetryAfter?.Delta?.TotalSeconds ?? (double.TryParse(resp.Headers.TryGetValues("Retry-After", out var v) ? v.FirstOrDefault() : null, out double s) ? s : 1);
                    _nextSlotUtc = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Clamp(secs, 0.5, 60));
                    if (attempt < 8) continue;
                }
                else if (((int)resp.StatusCode >= 500 || resp.StatusCode == HttpStatusCode.Conflict) && attempt < 4)
                {
                    _nextSlotUtc = DateTime.UtcNow + TimeSpan.FromSeconds(attempt * 2);
                    continue;
                }
            }
            finally
            {
                _gate.Release();
            }

            using (resp)
            {
                JsonObject? obj = null;
                try { obj = JsonNode.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text) as JsonObject; } catch (JsonException) { }
                if (resp!.IsSuccessStatusCode) return obj ?? new JsonObject();
                string code = obj?["code"]?.GetValue<string>() ?? "";
                string msg = obj?["message"]?.GetValue<string>() ?? text;
                throw new NotionException((int)resp.StatusCode, code, msg.Length > 400 ? msg[..400] : msg);
            }
        }
    }

    public Task<JsonObject> GetAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Get, path, null, ct);
    public Task<JsonObject> PostAsync(string path, JsonNode body, CancellationToken ct) => SendAsync(HttpMethod.Post, path, body, ct);
    public Task<JsonObject> PatchAsync(string path, JsonNode body, CancellationToken ct) => SendAsync(HttpMethod.Patch, path, body, ct);

    // ---------------- العمليات ----------------

    /// <summary>اسم الـ integration (للتأكد من المفتاح).</summary>
    public async Task<string> MeAsync(CancellationToken ct)
    {
        var me = await GetAsync("users/me", ct);
        return me["name"]?.GetValue<string>() ?? "Notion";
    }

    /// <summary>عنوان الصفحة (وبيتأكد إنها متشاركة مع الـ integration).</summary>
    public async Task<string> PageTitleAsync(string pageId, CancellationToken ct)
    {
        var page = await GetAsync("pages/" + pageId, ct);
        if (page["in_trash"]?.GetValue<bool>() == true || page["archived"]?.GetValue<bool>() == true)
            throw new NotionException(404, "object_not_found", "الصفحة في سلة المهملات");
        foreach (var kv in page["properties"]?.AsObject() ?? new JsonObject())
            if (kv.Value?["type"]?.GetValue<string>() == "title")
                return PlainText(kv.Value["title"]);
        return "";
    }

    public async Task<bool> PageAliveAsync(string pageId, CancellationToken ct)
    {
        try
        {
            var page = await GetAsync("pages/" + pageId, ct);
            return page["in_trash"]?.GetValue<bool>() != true && page["archived"]?.GetValue<bool>() != true;
        }
        catch (NotionException ex) when (ex.Status == 404) { return false; }
    }

    public async Task<string> CreatePageAsync(JsonObject parent, JsonObject properties, string? emoji, JsonArray? children, CancellationToken ct)
    {
        var body = new JsonObject { ["parent"] = parent, ["properties"] = properties };
        if (emoji != null) body["icon"] = new JsonObject { ["type"] = "emoji", ["emoji"] = emoji };
        if (children is { Count: > 0 }) body["children"] = children;
        var page = await PostAsync("pages", body, ct);
        return page["id"]!.GetValue<string>();
    }

    public Task UpdatePageAsync(string pageId, JsonObject properties, CancellationToken ct) =>
        PatchAsync("pages/" + pageId, new JsonObject { ["properties"] = properties }, ct);

    /// <summary>قاعدة بيانات جديدة جوه صفحة ← (رقم القاعدة، رقم الـ data source بتاعها).</summary>
    public async Task<(string DbId, string DsId)> CreateDatabaseAsync(string parentPageId, string title, string emoji, JsonObject properties, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["parent"] = new JsonObject { ["type"] = "page_id", ["page_id"] = parentPageId },
            ["title"] = Rich(title),
            ["is_inline"] = true,
            ["icon"] = new JsonObject { ["type"] = "emoji", ["emoji"] = emoji },
            ["initial_data_source"] = new JsonObject { ["properties"] = properties }
        };
        var db = await PostAsync("databases", body, ct);
        string dbId = db["id"]!.GetValue<string>();
        string? ds = db["data_sources"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>();
        if (ds == null)
        {
            var again = await GetAsync("databases/" + dbId, ct);
            ds = again["data_sources"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>();
        }
        return (dbId, ds ?? throw new NotionException(500, "no_data_source", "لم يُرجع نوشن رقم قاعدة البيانات"));
    }

    /// <summary>بيضيف بلوكات في آخر الصفحة (100 في كل طلب) ← أرقام البلوكات الجديدة بنفس الترتيب.</summary>
    public async Task<List<string>> AppendAsync(string blockId, IReadOnlyList<JsonObject> blocks, CancellationToken ct)
    {
        var ids = new List<string>();
        for (int i = 0; i < blocks.Count; i += 100)
        {
            var chunk = new JsonArray();
            foreach (var b in blocks.Skip(i).Take(100)) chunk.Add(b.DeepClone());
            var r = await PatchAsync($"blocks/{blockId}/children", new JsonObject { ["children"] = chunk }, ct);
            foreach (var x in r["results"]?.AsArray() ?? new JsonArray()) ids.Add(x?["id"]?.GetValue<string>() ?? "");
        }
        return ids;
    }

    public Task UpdateBlockAsync(string blockId, JsonObject body, CancellationToken ct) => PatchAsync("blocks/" + blockId, body, ct);

    /// <summary>بيشيل بلوك عملناه إحنا (بيروح سلة المهملات في نوشن).</summary>
    public async Task TrashBlockAsync(string blockId, CancellationToken ct)
    {
        try { await SendAsync(HttpMethod.Delete, "blocks/" + blockId, null, ct); }
        catch (NotionException ex) when (ex.Status == 404) { }
    }

    /// <summary>كل صفوف القاعدة (صفحة ورا صفحة).</summary>
    public async Task<List<JsonObject>> QueryAllAsync(string dataSourceId, CancellationToken ct)
    {
        var rows = new List<JsonObject>();
        string? cursor = null;
        for (int page = 0; page < 200; page++)
        {
            var body = new JsonObject { ["page_size"] = 100 };
            if (cursor != null) body["start_cursor"] = cursor;
            var r = await PostAsync($"data_sources/{dataSourceId}/query", body, ct);
            foreach (var x in r["results"]?.AsArray() ?? new JsonArray()) if (x is JsonObject o) rows.Add(o);
            if (r["has_more"]?.GetValue<bool>() != true) break;
            cursor = r["next_cursor"]?.GetValue<string>();
            if (cursor == null) break;
        }
        return rows;
    }

    // ---------------- مساعدات JSON ----------------

    /// <summary>نص لنوشن (كل قطعة أقصاها 2000 حرف).</summary>
    public static JsonArray Rich(string text, bool bold = false, string? link = null)
    {
        var arr = new JsonArray();
        text ??= "";
        for (int i = 0; i < Math.Max(1, text.Length) && arr.Count < 100; i += 1900)
        {
            var t = new JsonObject { ["content"] = text.Length == 0 ? "" : text.Substring(i, Math.Min(1900, text.Length - i)) };
            if (link != null) t["link"] = new JsonObject { ["url"] = link };
            var item = new JsonObject { ["type"] = "text", ["text"] = t };
            if (bold) item["annotations"] = new JsonObject { ["bold"] = true };
            arr.Add(item);
        }
        return arr;
    }

    public static string PlainText(JsonNode? richArray)
    {
        if (richArray is not JsonArray a) return "";
        var sb = new StringBuilder();
        foreach (var x in a) sb.Append(x?["plain_text"]?.GetValue<string>() ?? x?["text"]?["content"]?.GetValue<string>() ?? "");
        return sb.ToString();
    }

    public static JsonObject Block(string type, JsonObject inner) => new() { ["object"] = "block", ["type"] = type, [type] = inner };
    public static JsonObject Paragraph(string text) => Block("paragraph", new JsonObject { ["rich_text"] = Rich(text) });
    public static JsonObject Heading(string text, int level = 2) => Block("heading_" + level, new JsonObject { ["rich_text"] = Rich(text) });
    public static JsonObject Bullet(string text) => Block("bulleted_list_item", new JsonObject { ["rich_text"] = Rich(text) });
    public static JsonObject ToDo(string text, bool done) => Block("to_do", new JsonObject { ["rich_text"] = Rich(text), ["checked"] = done });
    public static JsonObject Divider() => Block("divider", new JsonObject());
    public static JsonObject Callout(string text, string emoji, JsonArray? children = null)
    {
        var inner = new JsonObject { ["rich_text"] = Rich(text, bold: true), ["icon"] = new JsonObject { ["type"] = "emoji", ["emoji"] = emoji }, ["color"] = "gray_background" };
        if (children is { Count: > 0 }) inner["children"] = children;
        return Block("callout", inner);
    }

    // قيم الخصائص
    public static JsonObject TitleProp(string text) => new() { ["title"] = Rich(text) };
    public static JsonObject TextProp(string? text) => new() { ["rich_text"] = Rich(text ?? "") };
    public static JsonObject SelectProp(string? name) => new() { ["select"] = name == null ? null : new JsonObject { ["name"] = name } };
    public static JsonObject NumberProp(double? n) => new() { ["number"] = n == null ? null : JsonValue.Create(Math.Round(n.Value, 4)) };
    public static JsonObject CheckProp(bool b) => new() { ["checkbox"] = b };
    public static JsonObject UrlProp(string? u) => new() { ["url"] = string.IsNullOrWhiteSpace(u) ? null : u };
    public static JsonObject DateProp(DateTime? d) => new() { ["date"] = d == null ? null : new JsonObject { ["start"] = d.Value.ToString("yyyy-MM-dd") } };
    public static JsonObject RelationProp(params string[] ids)
    {
        var arr = new JsonArray();
        foreach (var id in ids.Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(100)) arr.Add(new JsonObject { ["id"] = id });
        return new JsonObject { ["relation"] = arr };
    }

    // قراية الخصائص من صف
    public static JsonNode? Prop(JsonObject page, string name) => page["properties"]?[name];
    public static DateTime? ReadDate(JsonObject page, string name)
    {
        string? s = Prop(page, name)?["date"]?["start"]?.GetValue<string>();
        return s != null && DateTime.TryParse(s.Length >= 10 ? s[..10] : s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d.Date : null;
    }
    public static bool ReadCheck(JsonObject page, string name) => Prop(page, name)?["checkbox"]?.GetValue<bool>() == true;
    public static string? ReadSelect(JsonObject page, string name) => Prop(page, name)?["select"]?["name"]?.GetValue<string>();
    public static DateTime ReadEdited(JsonObject page) =>
        DateTime.TryParse(page["last_edited_time"]?.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.MinValue;
}
