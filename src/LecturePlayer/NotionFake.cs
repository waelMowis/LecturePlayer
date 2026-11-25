using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace LecturePlayer;

/// <summary>
/// نوشن وهمي للفحص التلقائي بس (من غير نت ولا حساب): بيقلّد الأجزاء اللي «مسار» بيستخدمها من الـ API
/// ويتأكد زي نوشن الحقيقي: أسماء الأعمدة وأنواعها لازم تطابق القاعدة، 100 بلوك أقصى في الطلب، النص أقصاه 2000 حرف،
/// الـ relation لقاعدة موجودة، والصفحة اللي في السلة ماتتعدّلش. وممكن يرد 429 مرة عشان نتأكد إن الانتظار شغال.
/// </summary>
internal sealed class NotionFake : HttpMessageHandler
{
    public sealed class Page
    {
        public string Id = "";
        public string? ParentPage, ParentDs;
        public JsonObject Props = new();
        public bool Trash;
        public DateTime Edited = DateTime.UtcNow;
        public List<string> Children = new();
    }
    public sealed class Ds { public string Id = "", DbId = "", ParentPage = ""; public JsonObject Schema = new(); public string Title = ""; }
    public sealed class Block { public string Id = "", Parent = "", Type = ""; public JsonObject Data = new(); public bool Trash; public List<string> Children = new(); }

    public readonly Dictionary<string, Page> Pages = new();
    public readonly Dictionary<string, Ds> Sources = new();
    public readonly Dictionary<string, string> Databases = new(); // db ← ds
    public readonly Dictionary<string, Block> Blocks = new();
    public string Token = "secret_test";
    /// <summary>الطلب رقم كام يرد 429 (0 = لأ).</summary>
    public int ThrottleAt;
    public int Calls;
    public readonly List<string> Log = new();

    private static string NewId() => Guid.NewGuid().ToString();

    public string AddRootPage(string title)
    {
        var p = new Page { Id = NewId() };
        p.Props["title"] = new JsonObject { ["type"] = "title", ["title"] = Rich(title) };
        Pages[p.Id] = p;
        return p.Id;
    }

    private static JsonArray Rich(string t) => new(new JsonObject { ["type"] = "text", ["text"] = new JsonObject { ["content"] = t }, ["plain_text"] = t });

    public static string Text(JsonNode? rich) => LecturePlayer.Data.NotionClient.PlainText(rich);

    public string Title(Page p)
    {
        foreach (var kv in p.Props) if (kv.Value?["type"]?.GetValue<string>() == "title") return Text(kv.Value["title"]);
        return "";
    }

    public IEnumerable<Page> RowsOf(string dsId) => Pages.Values.Where(p => p.ParentDs == dsId);
    public IEnumerable<Block> ChildBlocks(string parent) =>
        (Pages.TryGetValue(parent, out var p) ? p.Children : Blocks.TryGetValue(parent, out var b) ? b.Children : new List<string>())
        .Where(Blocks.ContainsKey).Select(id => Blocks[id]).Where(x => !x.Trash);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        Calls++;
        string body = req.Content == null ? "" : await req.Content.ReadAsStringAsync(ct);
        string path = req.RequestUri!.AbsolutePath;
        int v1 = path.IndexOf("/v1/", StringComparison.Ordinal);
        if (v1 >= 0) path = path[(v1 + 4)..];
        path = path.Trim('/');
        lock (Log) Log.Add(req.Method + " " + path);
        if (req.Headers.Authorization?.Parameter != Token) return Err(401, "unauthorized", "API token is invalid.");
        if (!req.Headers.TryGetValues("Notion-Version", out var ver) || ver.FirstOrDefault() != Data.NotionClient.ApiVersion)
            return Err(400, "missing_version", "Notion-Version header missing");
        if (ThrottleAt > 0 && Calls == ThrottleAt)
        {
            var r = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("{\"object\":\"error\",\"status\":429,\"code\":\"rate_limited\",\"message\":\"slow down\"}") };
            r.Headers.TryAddWithoutValidation("Retry-After", "1");
            return r;
        }
        JsonObject? json = null;
        if (body.Length > 0)
        {
            try { json = JsonNode.Parse(body) as JsonObject; } catch { return Err(400, "invalid_json", "bad json"); }
            if (CheckRichTextLimits(json) is { } bad) return Err(400, "validation_error", bad);
        }
        try
        {
            lock (this) return Route(req.Method, path, json ?? new JsonObject());
        }
        catch (FakeError e) { return Err(e.Status, e.Code, e.Message); }
    }

    private sealed class FakeError : Exception { public int Status; public string Code; public FakeError(int s, string c, string m) : base(m) { Status = s; Code = c; } }
    private static FakeError Bad(string m) => new(400, "validation_error", m);
    private static FakeError NotFound(string id) => new(404, "object_not_found", $"Could not find object with ID: {id}.");

    private static string? CheckRichTextLimits(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o:
                if (o["text"] is JsonObject t && t["content"] is JsonValue c && c.GetValue<string>().Length > 2000) return "body.text.content.length should be ≤ 2000";
                foreach (var kv in o) if (CheckRichTextLimits(kv.Value) is { } e) return e;
                break;
            case JsonArray a:
                foreach (var x in a) if (CheckRichTextLimits(x) is { } e) return e;
                break;
        }
        return null;
    }

    private HttpResponseMessage Route(HttpMethod m, string path, JsonObject b)
    {
        var parts = path.Split('/');
        if (m == HttpMethod.Get && path == "users/me") return Ok(new JsonObject { ["object"] = "user", ["name"] = "Masar test", ["type"] = "bot" });
        if (m == HttpMethod.Post && path == "databases") return CreateDatabase(b);
        if (m == HttpMethod.Get && parts[0] == "databases" && parts.Length == 2)
        {
            if (!Databases.TryGetValue(parts[1], out var ds)) throw NotFound(parts[1]);
            return Ok(DbJson(parts[1], ds));
        }
        if (m == HttpMethod.Post && path == "pages") return CreatePage(b);
        if (parts[0] == "pages" && parts.Length == 2)
        {
            if (!Pages.TryGetValue(parts[1], out var p)) throw NotFound(parts[1]);
            if (m == HttpMethod.Get) return Ok(PageJson(p));
            if (m == HttpMethod.Patch) return UpdatePage(p, b);
        }
        if (parts[0] == "blocks" && parts.Length == 3 && parts[2] == "children" && m == HttpMethod.Patch) return Append(parts[1], b);
        if (parts[0] == "blocks" && parts.Length == 2)
        {
            if (!Blocks.TryGetValue(parts[1], out var bl)) throw NotFound(parts[1]);
            if (m == HttpMethod.Delete) { bl.Trash = true; return Ok(new JsonObject { ["object"] = "block", ["id"] = bl.Id, ["in_trash"] = true }); }
            if (m == HttpMethod.Patch)
            {
                if (bl.Trash) throw Bad("Can't edit block that is archived. You must unarchive the block before editing.");
                if (b[bl.Type] is JsonObject upd) foreach (var kv in upd) bl.Data[kv.Key] = kv.Value?.DeepClone();
                else throw Bad($"body.{bl.Type} should be defined");
                return Ok(new JsonObject { ["object"] = "block", ["id"] = bl.Id });
            }
        }
        if (parts[0] == "data_sources" && parts.Length == 3 && parts[2] == "query" && m == HttpMethod.Post) return Query(parts[1], b);
        throw new FakeError(400, "invalid_request_url", "Invalid request URL: " + m + " " + path);
    }

    private HttpResponseMessage CreateDatabase(JsonObject b)
    {
        string parent = b["parent"]?["page_id"]?.GetValue<string>() ?? throw Bad("body.parent.page_id should be defined");
        if (!Pages.TryGetValue(parent, out var pp) || pp.Trash) throw NotFound(parent);
        if (b["properties"] != null) throw Bad("body.properties is not allowed in this version; use initial_data_source.properties");
        var props = b["initial_data_source"]?["properties"] as JsonObject ?? throw Bad("body.initial_data_source.properties should be defined");
        int titles = 0;
        var schema = new JsonObject();
        foreach (var kv in props)
        {
            var def = kv.Value as JsonObject ?? throw Bad("property " + kv.Key);
            string type = def.Select(x => x.Key).FirstOrDefault(x => x != "description" && x != "type") ?? throw Bad("property type " + kv.Key);
            if (type == "title") titles++;
            if (type == "relation")
            {
                string target = def["relation"]?["data_source_id"]?.GetValue<string>() ?? throw Bad("relation.data_source_id should be defined");
                if (!Sources.ContainsKey(target)) throw NotFound(target);
                if (def["relation"]?["type"]?.GetValue<string>() is not ("single_property" or "dual_property")) throw Bad("relation.type");
            }
            if (type == "select")
                foreach (var o in def["select"]?["options"]?.AsArray() ?? new JsonArray())
                    if ((o?["name"]?.GetValue<string>() ?? "").Contains(',')) throw Bad("select option names can't contain commas");
            schema[kv.Key] = new JsonObject { ["type"] = type, [type] = def[type]?.DeepClone() };
        }
        if (titles != 1) throw Bad("A database must have exactly one title property");
        string dbId = NewId(), dsId = NewId();
        Sources[dsId] = new Ds { Id = dsId, DbId = dbId, ParentPage = parent, Schema = schema, Title = Text(b["title"]) };
        Databases[dbId] = dsId;
        pp.Children.Add(dbId);
        return Ok(DbJson(dbId, dsId));
    }

    private JsonObject DbJson(string dbId, string dsId) => new()
    {
        ["object"] = "database", ["id"] = dbId,
        ["data_sources"] = new JsonArray(new JsonObject { ["id"] = dsId, ["name"] = Sources[dsId].Title })
    };

    private JsonObject NormalizeProps(JsonObject input, Ds? ds)
    {
        var outp = new JsonObject();
        foreach (var kv in input)
        {
            var val = kv.Value as JsonObject ?? throw Bad("property value " + kv.Key);
            string type = val.Select(x => x.Key).FirstOrDefault(x => x != "type") ?? throw Bad("property " + kv.Key);
            if (ds != null)
            {
                if (ds.Schema[kv.Key] is not JsonObject def) throw Bad($"{kv.Key} is not a property that exists.");
                string want = def["type"]!.GetValue<string>();
                if (want != type) throw Bad($"{kv.Key} is expected to be {want}.");
                if (type == "select" && val["select"] is JsonObject so)
                {
                    string name = so["name"]?.GetValue<string>() ?? throw Bad("select.name");
                    if (name.Contains(',')) throw Bad("select option names can't contain commas");
                    var opts = def["select"]!["options"] as JsonArray ?? new JsonArray();
                    if (!opts.Any(o => o?["name"]?.GetValue<string>() == name)) { opts.Add(new JsonObject { ["name"] = name }); def["select"]!["options"] = opts; }
                }
                if (type == "relation")
                    foreach (var r in val["relation"]!.AsArray())
                        if (!Pages.ContainsKey(r!["id"]!.GetValue<string>())) throw Bad("relation page not found");
                if (type == "number" && val["number"] is JsonValue nv && !nv.TryGetValue<double>(out _)) throw Bad($"{kv.Key} should be a number");
                if (type == "checkbox" && val["checkbox"] is not JsonValue) throw Bad($"{kv.Key} should be a boolean");
            }
            else if (type != "title") throw Bad("If the parent is a page, title is the only valid property.");
            var copy = val.DeepClone().AsObject();
            if (copy[type] is JsonArray arr)
                foreach (var it in arr) if (it is JsonObject io && io["text"]?["content"] is JsonValue tc) io["plain_text"] = tc.GetValue<string>();
            copy["type"] = type;
            outp[kv.Key] = copy;
        }
        return outp;
    }

    private HttpResponseMessage CreatePage(JsonObject b)
    {
        var parent = b["parent"] as JsonObject ?? throw Bad("body.parent should be defined");
        var page = new Page { Id = NewId() };
        var props = b["properties"] as JsonObject ?? new JsonObject();
        if (parent["type"]?.GetValue<string>() == "data_source_id")
        {
            string ds = parent["data_source_id"]!.GetValue<string>();
            if (!Sources.TryGetValue(ds, out var src)) throw NotFound(ds);
            page.ParentDs = ds;
            page.Props = NormalizeProps(props, src);
        }
        else if (parent["type"]?.GetValue<string>() == "page_id")
        {
            string pid = parent["page_id"]!.GetValue<string>();
            if (!Pages.TryGetValue(pid, out var pp) || pp.Trash) throw NotFound(pid);
            page.ParentPage = pid;
            page.Props = NormalizeProps(props, null);
            pp.Children.Add(page.Id);
        }
        else throw Bad("body.parent.type");
        Pages[page.Id] = page;
        if (b["children"] is JsonArray kids)
        {
            if (kids.Count > 100) { Pages.Remove(page.Id); throw Bad("body.children.length should be ≤ 100"); }
            AddBlocks(page.Id, kids, null);
        }
        return Ok(PageJson(page));
    }

    private HttpResponseMessage UpdatePage(Page p, JsonObject b)
    {
        if (p.Trash) throw Bad("Can't edit block that is archived. You must unarchive the block before editing.");
        if (b["in_trash"]?.GetValue<bool>() == true) { p.Trash = true; return Ok(PageJson(p)); }
        var props = b["properties"] as JsonObject ?? new JsonObject();
        var norm = NormalizeProps(props, p.ParentDs != null ? Sources[p.ParentDs] : null);
        foreach (var kv in norm) p.Props[kv.Key] = kv.Value!.DeepClone();
        p.Edited = DateTime.UtcNow;
        return Ok(PageJson(p));
    }

    private JsonObject PageJson(Page p)
    {
        var parent = p.ParentDs != null ? new JsonObject { ["type"] = "data_source_id", ["data_source_id"] = p.ParentDs } : new JsonObject { ["type"] = "page_id", ["page_id"] = p.ParentPage };
        return new JsonObject
        {
            ["object"] = "page", ["id"] = p.Id, ["in_trash"] = p.Trash, ["parent"] = parent,
            ["last_edited_time"] = p.Edited.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["properties"] = p.Props.DeepClone()
        };
    }

    private List<string> AddBlocks(string parent, JsonArray kids, string? after, int depth = 0)
    {
        var list = Pages.TryGetValue(parent, out var p) ? p.Children : Blocks.TryGetValue(parent, out var pb) ? pb.Children : throw NotFound(parent);
        int at = after == null ? list.Count : list.IndexOf(after) is int i and >= 0 ? i + 1 : throw Bad("after_block not found");
        var ids = new List<string>();
        foreach (var k in kids)
        {
            var o = k as JsonObject ?? throw Bad("block");
            string type = o["type"]?.GetValue<string>() ?? throw Bad("block.type should be defined");
            var data = o[type] as JsonObject ?? throw Bad($"block.{type} should be defined");
            var bl = new Block { Id = NewId(), Parent = parent, Type = type, Data = (JsonObject)data.DeepClone() };
            bl.Data.Remove("children");
            Blocks[bl.Id] = bl;
            list.Insert(at++, bl.Id);
            ids.Add(bl.Id);
            if (data["children"] is JsonArray sub)
            {
                if (depth >= 1 && sub.Count > 0) throw Bad("children nested more than 2 levels");
                if (sub.Count > 100) throw Bad("children.length should be ≤ 100");
                AddBlocks(bl.Id, sub, null, depth + 1);
            }
        }
        return ids;
    }

    private HttpResponseMessage Append(string parent, JsonObject b)
    {
        var kids = b["children"] as JsonArray ?? throw Bad("body.children should be defined");
        if (kids.Count > 100) throw Bad("body.children.length should be ≤ 100");
        if (b["after"] != null && b["position"] != null) throw Bad("after and position can't be used together");
        if (b["after"] != null) throw Bad("after is not supported in this version; use position");
        if (Pages.TryGetValue(parent, out var pg) && pg.Trash) throw Bad("Can't edit block that is archived.");
        string? after = null;
        if (b["position"] is JsonObject pos)
        {
            string t = pos["type"]?.GetValue<string>() ?? "";
            if (t == "after_block") after = pos["after_block"]?["id"]?.GetValue<string>() ?? throw Bad("position.after_block.id");
            else if (t != "end") throw Bad("position.type");
        }
        var ids = AddBlocks(parent, kids, after);
        var results = new JsonArray();
        foreach (var id in ids) results.Add(new JsonObject { ["object"] = "block", ["id"] = id, ["type"] = Blocks[id].Type });
        return Ok(new JsonObject { ["object"] = "list", ["results"] = results });
    }

    private HttpResponseMessage Query(string dsId, JsonObject b)
    {
        if (!Sources.ContainsKey(dsId)) throw NotFound(dsId);
        int size = Math.Clamp(b["page_size"]?.GetValue<int>() ?? 100, 1, 100);
        int start = int.TryParse(b["start_cursor"]?.GetValue<string>(), out int s) ? s : 0;
        var rows = RowsOf(dsId).Where(p => !p.Trash).ToList();
        var results = new JsonArray();
        foreach (var p in rows.Skip(start).Take(size)) results.Add(PageJson(p));
        bool more = start + size < rows.Count;
        return Ok(new JsonObject { ["object"] = "list", ["results"] = results, ["has_more"] = more, ["next_cursor"] = more ? (start + size).ToString() : null });
    }

    private static HttpResponseMessage Ok(JsonObject o) => new(HttpStatusCode.OK) { Content = new StringContent(o.ToJsonString(), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Err(int status, string code, string msg) =>
        new((HttpStatusCode)status) { Content = new StringContent(new JsonObject { ["object"] = "error", ["status"] = status, ["code"] = code, ["message"] = msg }.ToJsonString(), Encoding.UTF8, "application/json") };
}
