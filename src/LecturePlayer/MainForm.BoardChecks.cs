using System.Text.Json;
using System.Text.Json.Nodes;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

internal partial class MainForm
{
    /// <summary>فحص السبورة: إنشاء ← رسم ← صفحات وقوالب ← حفظ ← تصدير PDF وللطلبة ← لقطة شاشة ← إعادة فتح ← سبورة محاضرة.</summary>
    internal void RunBoardChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("board: " + what); }
        bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(20); } return cond(); }
        string Js(string script)
        {
            var t = _reader!.Web.CoreWebView2.ExecuteScriptAsync(script);
            Check(Until(() => t.IsCompleted, 10_000) && !t.IsFaulted, "js timed out: " + script[..Math.Min(60, script.Length)]);
            return UdemyWeb.Unwrap(t.Result) ?? "";
        }
        Check(BookFiles.FormatOf("x/سبورة" + BookFiles.BoardExtension) == "board" && BookFiles.FormatOf("a.pdf") == "pdf", "format");
        if (!ReaderPane.Available || !HasLibrary) { Console.WriteLine("SMOKE board: reader not available — skipped"); return; }

        string dir = Path.Combine(Path.GetTempPath(), "masar-board-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var made = new List<Book>();
        bool hadShelf = _library.Shelves.Any(s => s.Kind == BoardsShelfKind);
        try
        {
            SmokeDialogShots = true;
            try { NewBoardDialog(); } finally { SmokeDialogShots = false; }   // صورة الشباك
            SmokeBoardChoice = ("سبورة الفحص", 0, 0, 0);
            var b = NewBoardDialog();
            Check(b != null && File.Exists(b.LocalPath) && b.LocalPath.Contains(BoardsFolderName) && b.IsBoard, "board file in «سبوراتي»: " + b?.LocalPath);
            made.Add(b!);
            Check(_library.Shelves.First(s => s.Id == b!.ShelfId).Kind == BoardsShelfKind, "board shelf");
            Check(Until(() => ReaderShown && _currentBook == b && _reader!.Info?.Pages == 1, 20_000), "board opened in the reader");
            Check(Until(() => Js("String(masar.inkTest().Ink.on && document.querySelectorAll('.bd-page .ink-layer').length===1)") == "true", 10_000), "pens ready on the board");
            string count = Js("""
                (()=>{const L=document.querySelector('.bd-page .ink-layer');const R=L.getBoundingClientRect();const k=R.width/1000;
                const ev=(t,x,y,b)=>L.dispatchEvent(new PointerEvent(t,{bubbles:true,cancelable:true,pointerId:1,pointerType:'mouse',button:0,buttons:b,clientX:R.left+x*k,clientY:R.top+y*k}));
                const I=masar.inkTest().Ink;I.setPenOpt('pen','c','#e03131');I.setTool('pen','pen');
                ev('pointerdown',100,100,1); for(let i=1;i<=15;i++) ev('pointermove',100+i*30,100+Math.sin(i/2)*40,1); ev('pointerup',550,100,0);
                const B=masar.boardTest();B.addPage();B.template('flow');B.setBg('green');
                return String(I.count())})()
                """);
            Check(count == "12", "drawn + template items: " + count);
            Check(Until(() => b!.PageCount == 2 && (JsonNode.Parse(File.ReadAllText(b.LocalPath))?["pages"] as JsonArray)?.Count == 2, 8000), "pages saved to the board file");
            var meta = JsonNode.Parse(File.ReadAllText(b!.LocalPath))!;
            Check(meta["pages"]![1]!["bg"]!.GetValue<string>() == "green", "page background saved");
            Js("masar.inkFlush()");
            Check(Until(() => InkStore.Count(InkStore.Load(InkStore.FileFor(_libraryPath, b.Id))) == 12, 8000), "board ink saved");
            Check(Until(() => b.InkCount == 12, 5000), "board ink count");
            Until(() => false, 600);
            SaveSmokeShot(this, "board");

            // ---------- تصدير PDF وللطلبة ----------
            SmokeSavePath = Path.Combine(dir, "board.pdf");
            BoardExportFromMenu(b, "all", false);
            Check(Until(() => File.Exists(SmokeSavePath) && new FileInfo(SmokeSavePath).Length > 1000, 30_000), "board pdf exported");
            var probe = _reader!.ProbeAsync(SmokeSavePath);
            Check(Until(() => probe.IsCompleted, 30_000) && probe.Result?.Pages == 2, "exported pdf has 2 pages: " + probe.Result?.Pages);
            SmokeSavePath = Path.Combine(dir, "send.pdf");
            BoardExportFromMenu(b, "all", true);
            Check(Until(() => File.Exists(SmokeSavePath) && _lastPickSave == SmokeSavePath, 30_000), "send-to-students pdf");

            // ---------- لقطة شاشة ← صفحة ----------
            BoardScreenshot();
            Check(Until(() => b.PageCount == 3, 15_000), "screenshot page added: " + b.PageCount);

            // ---------- إعادة الفتح ----------
            GoHome();
            Check(Until(() => InkStore.Count(InkStore.Load(InkStore.FileFor(_libraryPath, b.Id))) == 13, 8000), "screenshot image saved as ink");
            OpenBook(b);
            Check(Until(() => ReaderShown && _currentBook == b && _reader!.Info?.Pages == 3, 20_000), "board reopens with 3 pages");
            Check(Until(() => Js("String(masar.inkTest().Ink.count())") == "13", 8000), "board ink back");
            Check(Js("masar.boardTest().meta.pages[1].bg") == "green", "background back");
            RunScienceChecks(Js, Until, b);
            var menu = new ContextMenuStrip(); BuildBookMenu(b, menu.Items);
            Check(menu.Items.OfType<ToolStripMenuItem>().Any(i => i.Text.Contains("صدّر")) && !menu.Items.OfType<ToolStripMenuItem>().Any(i => i.Text.Contains("الذكاء")), "board menu");
            GoHome();

            // ---------- سبورة للمحاضرة ----------
            if (_library.AllGroups.FirstOrDefault() is { } g)
            {
                SmokeBoardChoice = ("سبورة المحاضرة", 1, 2, 2);
                OpenLectureBoard(g);
                var lb = BoardOf(g);
                Check(lb != null && lb.LectureId == g.Id, "lecture board");
                made.Add(lb!);
                Check(Until(() => ReaderShown && _currentBook == lb, 20_000), "lecture board opened");
                Check(Until(() => Js("String(masar.inkTest().Ink.surfaces.size===1 && [...masar.inkTest().Ink.surfaces.values()][0].dark)") == "true", 8000), "dark board surface");
                var gm = new ContextMenuStrip(); BuildGroupMenu(g, gm.Items);
                Check(gm.Items.OfType<ToolStripMenuItem>().Any(i => i.Text.StartsWith("▶  سبورة المحاضرة")), "lecture menu opens its board");
                GoHome();
            }
            Console.WriteLine("SMOKE board: ok");
        }
        finally
        {
            SmokeBoardChoice = null;
            SmokeSavePath = null;
            if (ReaderShown) CloseReader(showHome: true);
            foreach (var b in made)
            {
                try { File.Delete(b.LocalPath); } catch { }
                try { File.Delete(InkStore.FileFor(_libraryPath, b.Id)); } catch { }
                _library.Books.Remove(b);
            }
            if (!hadShelf) _library.Shelves.RemoveAll(s => s.Kind == BoardsShelfKind && !_library.Books.Any(x => x.ShelfId == s.Id));
            RebuildTree();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
