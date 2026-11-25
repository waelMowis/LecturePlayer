using System.Text.Json;
using System.Text.Json.Nodes;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

internal partial class MainForm
{
    /// <summary>فحص الأقلام على الكتب: رسم حقيقي في القارئ ← ملف الشرح جنب المكتبة ← إعادة الفتح ← دمج من جهاز تاني ← وضع العرض.</summary>
    internal void RunInkChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("ink: " + what); }
        bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(20); } return cond(); }
        string Js(string script)
        {
            var t = _reader!.Web.CoreWebView2.ExecuteScriptAsync(script);
            Check(Until(() => t.IsCompleted, 10_000) && !t.IsFaulted, "js timed out: " + script[..Math.Min(60, script.Length)]);
            return UdemyWeb.Unwrap(t.Result) ?? "";
        }
        JsonElement State() => JsonDocument.Parse(Js("masar.inkState()")).RootElement;

        // ---------- الدمج (من غير واجهة) ----------
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var a = InkStore.Parse($$$"""{"pages":{"1":[{"id":"a","u":{{{now}}}},{"id":"b","u":{{{now}}}}]}}""");
        var b = InkStore.Parse($$$"""{"pages":{"1":[{"id":"a","u":{{{now + 5}}},"c":"#fff"},{"id":"c","u":{{{now}}}}]},"del":{"b":{{{now + 1}}}}}""");
        InkStore.Merge(a, b);
        Check(string.Join(",", InkStore.Pages(a)["1"]!.AsArray().Select(x => x!["id"] + (x["c"] != null ? "*" : ""))) == "a*,c", "merge: " + a.ToJsonString());

        if (!ReaderPane.Available) { Console.WriteLine("SMOKE ink: reader not available — UI flow skipped"); return; }

        string dir = Path.Combine(Path.GetTempPath(), "masar-ink-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string pdf = Path.Combine(dir, "Ink Book.pdf");
        TestPdf.Write(pdf, 6, "Ink", new("Intro", 1), new("Two", 4));
        var added = new List<Book>();
        string? inkFile = null;
        bool wasFull = _fullscreen;
        try
        {
            if (!_shelfMode) ToggleShelf();
            var shelf = AddShelf("أقلام الفحص", null);
            added.AddRange(AddBooks(new[] { pdf }, shelf));
            var book = added[0];
            inkFile = InkStore.FileFor(_libraryPath, book.Id);
            Check(Until(() => book.PageCount == 6, 60_000), "probe");

            // ---------- من القائمة: «اشرح عليه» يفتح الكتاب والأدوات جاهزة ----------
            var menu = new ContextMenuStrip();
            BuildBookMenu(book, menu.Items);
            var item = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text.Contains("اشرح عليه"));
            Check(item != null, "book menu has «اشرح عليه»");
            item!.PerformClick();
            Check(Until(() => ReaderShown && _reader!.Info?.Pages == 6, 20_000), "book opened");
            Check(Until(() => Js("String(!!document.querySelector('.page[data-page-number=\"1\"] .ink-layer'))") == "true", 10_000), "ink layer on page 1");
            Check(Until(() => State().GetProperty("on").GetBoolean(), 8000), "ink mode on after opening from the menu");

            // ---------- رسم حقيقي (أحداث الماوس على الصفحة) ----------
            string count = Js("""
                (()=>{const L=document.querySelector('.page[data-page-number="1"] .ink-layer');const R=L.getBoundingClientRect();const k=R.width/1000;
                const ev=(t,x,y,b)=>L.dispatchEvent(new PointerEvent(t,{bubbles:true,cancelable:true,pointerId:1,pointerType:'mouse',button:0,buttons:b,clientX:R.left+x*k,clientY:R.top+y*k}));
                const I=masar.inkTest().Ink;
                I.setTool('pen','marker'); ev('pointerdown',100,230,1); for(let i=1;i<=20;i++) ev('pointermove',100+i*20,230+Math.sin(i/3)*30,1); ev('pointerup',500,230,0);
                I.setTool('shape','rect'); ev('pointerdown',120,300,1); ev('pointermove',420,420,1); ev('pointerup',420,420,0);
                I.add('1',{t:'text',x:130,y:330,w:380,h:40,text:'شرح بالعربي — Masar ink',font:'Segoe UI',size:26,c:'#1971c2'});
                I.setTool('sticker',{sid:'imp'}); ev('pointerdown',720,260,1); ev('pointerup',720,260,0);
                I.setTool('pen','hl'); ev('pointerdown',100,160,1); ev('pointermove',260,160,1); ev('pointermove',420,160,1); ev('pointerup',420,160,0);
                I.setTool('shape','arrow'); ev('pointerdown',600,450,1); ev('pointermove',460,380,1); ev('pointerup',460,380,0);
                I.setTool('pen','pen');
                return String(I.count())})()
                """);
            Check(count == "6", "drawn items: " + count);
            Js("masar.inkFlush()");
            Check(Until(() => File.Exists(inkFile) && InkStore.Count(InkStore.Load(inkFile)) == 6, 8000), "saved next to the library: " + inkFile);
            Check(book.InkCount == 6, "book ink count: " + book.InkCount);
            Check(_shelfNodes.TryGetValue(book, out var node) && node.ToolTipText.Contains("🖊"), "tree tooltip shows ink");
            Check(!File.ReadAllText(inkFile).Contains("tempPen"), "no temp pen in file");
            Until(() => false, 600);
            SaveSmokeShot(this, "ink-book");
            Js("document.querySelector('#inkbar [data-t=\"shape\"]').click()");
            Until(() => false, 400);
            SaveSmokeShot(this, "ink-shapes");
            Js("document.querySelector('#inkbar .colorbtn').click()");

            // ---------- قفل من غير «احفظ»: آخر رسم مايضيعش ----------
            Js("""
                (()=>{const L=document.querySelector('.page[data-page-number="1"] .ink-layer');const R=L.getBoundingClientRect();const k=R.width/1000;
                const ev=(t,x,y,b)=>L.dispatchEvent(new PointerEvent(t,{bubbles:true,cancelable:true,pointerId:1,pointerType:'mouse',button:0,buttons:b,clientX:R.left+x*k,clientY:R.top+y*k}));
                ev('pointerdown',100,600,1); ev('pointermove',300,640,1); ev('pointerup',300,640,0); return 'ok'})()
                """);
            GoHome();
            Check(Until(() => InkStore.Count(InkStore.Load(inkFile)) == 7, 8000), "last stroke saved on close: " + InkStore.Count(InkStore.Load(inkFile)));

            // ---------- إعادة الفتح: الشرح في مكانه ----------
            OpenBook(book, 1);
            Check(Until(() => ReaderShown && _reader!.Info?.Pages == 6, 20_000), "reopen");
            Check(Until(() => Js("String(document.querySelectorAll('.page[data-page-number=\"1\"] .ink-svg > g').length)") == "7", 10_000), "7 items drawn after reopen");
            Check(Js("document.querySelector('.page[data-page-number=\"1\"] .ink-text')?.textContent || ''") == "شرح بالعربي — Masar ink", "arabic text back");
            Check(!State().GetProperty("on").GetBoolean(), "normal open = ink tools closed (reading as usual)");

            // ---------- جهاز تاني رسم على نفس الكتاب ----------
            var remote = InkStore.Load(inkFile);
            var p1 = InkStore.Pages(remote)["1"]!.AsArray();
            string victim = p1[0]!["id"]!.GetValue<string>();
            p1.Add(new JsonObject { ["id"] = "remote-step", ["t"] = "step", ["x"] = 900, ["y"] = 120, ["n"] = 1, ["r"] = 18, ["c"] = "#2f9e44", ["ly"] = "p", ["u"] = now + 10_000 });
            InkStore.Tombs(remote)[victim] = now + 10_000;
            InkStore.Pages(remote)["2"] = new JsonArray(new JsonObject { ["id"] = "remote-p2", ["t"] = "shape", ["k"] = "ellipse", ["x1"] = 100, ["y1"] = 100, ["x2"] = 300, ["y2"] = 200, ["c"] = "#e03131", ["sw"] = 3, ["ly"] = "p", ["u"] = now + 10_000 });
            InkStore.Save(inkFile, remote);
            File.SetLastWriteTimeUtc(inkFile, DateTime.UtcNow.AddSeconds(5));
            _dueInkDisk = 0;
            Check(Until(() => Js("String(masar.inkTest().Ink.doc.items('1').some(i=>i.id==='remote-step') && !masar.inkTest().Ink.doc.items('1').some(i=>i.id===" + JsonSerializer.Serialize(victim) + ") && masar.inkTest().Ink.doc.items('2').length===1)") == "true", 25_000), "remote ink merged into the open book");
            Check(book.InkCount == 8, "count after merge: " + book.InkCount);

            // ---------- النسخة المشروحة في «شروحاتي» ----------
            var built = BuildAnnotatedCopyAsync(book, quiet: false);
            Check(Until(() => built.IsCompleted, 90_000) && built.Result, "annotated copy built");
            var copy = AnnotatedCopyOf(book);
            Check(copy != null && File.Exists(copy.LocalPath) && copy.LocalPath.Contains(AnnotatedFolderName) && _library.Shelves.First(x => x.Id == copy.ShelfId).Kind == AnnotatedShelfKind, "copy in «شروحاتي»: " + copy?.LocalPath);
            Check(Until(() => copy!.PageCount == 6, 30_000), "copy probed: " + copy!.PageCount);
            Check(!NeedsAnnotated(book), "copy up to date");
            Check(File.ReadAllBytes(copy.LocalPath).Length > new FileInfo(pdf).Length, "copy bigger than the original (ink inside)");
            var menuItems = new ContextMenuStrip(); BuildBookMenu(book, menuItems.Items);
            Check(menuItems.Items.OfType<ToolStripMenuItem>().Any(i => i.Text.StartsWith("🖊  افتح النسخة المشروحة")), "menu: open annotated copy");

            // ---------- صورة الصفحة ----------
            SmokeSavePath = Path.Combine(dir, "page.png");
            ExportPagePng(book);
            Check(Until(() => File.Exists(SmokeSavePath) && new FileInfo(SmokeSavePath).Length > 2000, 30_000), "page png saved");
            var head = File.ReadAllBytes(SmokeSavePath).Take(4).ToArray();
            Check(head.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), "png header");

            // ---------- نسخ الشرح ----------
            SmokeAskText = q => q.Length == 0 ? "نسخة الفحص" : q;
            SaveInkVersion(book, "نسخة الفحص");
            var vers = InkStore.Versions(_libraryPath, book.Id);
            Check(vers.Count == 1 && vers[0].Name == "نسخة الفحص" && vers[0].Count == 8, "version saved: " + string.Join(",", vers.Select(v => v.Name + ":" + v.Count)));
            Js("masar.inkTest().Ink.clearPage('1','all')");
            Js("masar.inkFlush()");
            Check(Until(() => book.InkCount == 1, 8000), "page cleared: " + book.InkCount);
            RestoreInkVersion(book, vers[0]);
            Check(book.InkCount == 8, "restored count: " + book.InkCount);
            Check(Until(() => Js("String(masar.inkTest().Ink.count())") == "8", 8000), "reader shows the restored ink");
            Check(InkStore.Versions(_libraryPath, book.Id).Count == 2, "auto version before restore");
            Check(Until(() => !NeedsAnnotated(book), 60_000), "annotated copy rebuilt after restore");

            // ---------- وضع العرض ----------
            Js("window.dispatchEvent(new KeyboardEvent('keydown',{key:'F5',bubbles:true}))");
            Check(Until(() => _fullscreen && Js("String(document.body.classList.contains('present'))") == "true", 8000), "present mode → fullscreen");
            Until(() => false, 500);
            SaveSmokeShot(this, "ink-present");
            Js("window.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))");
            Check(Until(() => _fullscreen == wasFull && Js("String(document.body.classList.contains('present'))") == "false", 8000), "leave present mode");

            // ---------- الإعدادات (لون القلم) بتتحفظ في البرنامج ----------
            Js("masar.inkTest().Ink.setPenOpt('pen','c','#6741d9')");
            Check(Until(() => _settings.InkPrefs?.Contains("#6741d9") == true, 5000), "ink prefs saved in settings");
            GoHome();
            Console.WriteLine("SMOKE ink: ok");
        }
        finally
        {
            SmokeAskText = null;
            SmokeSavePath = null;
            if (_fullscreen != wasFull) ToggleFullscreen();
            if (ReaderShown) CloseReader(showHome: true);
            var ids = added.Select(x => x.Id).ToHashSet();
            foreach (var c in _library.Books.Where(x => x.AnnotatedOf != null && ids.Contains(x.AnnotatedOf)).ToList())
            {
                try { File.Delete(c.LocalPath); } catch { }
                _library.Books.Remove(c);
            }
            foreach (var id in ids) try { Directory.Delete(InkStore.VersionsDir(_libraryPath, id), true); } catch { }
            _library.Shelves.RemoveAll(x => x.Kind == AnnotatedShelfKind && !_library.Books.Any(bk => bk.ShelfId == x.Id));
            _library.Books.RemoveAll(x => ids.Contains(x.Id));
            _library.Shelves.RemoveAll(s => s.Name == "أقلام الفحص");
            RebuildTree();
            try { if (inkFile != null) File.Delete(inkFile); } catch { }
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
