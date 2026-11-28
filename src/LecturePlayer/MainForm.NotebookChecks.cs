using System.Text.Json.Nodes;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

internal partial class MainForm
{
    /// <summary>
    /// قائمة «السبورة» والدفاتر: القائمة هي الوحيدة لأوامر السبورات في الشريط العلوي، والزرار السريع،
    /// ودفتر جديد ← مقاطع وصفحات A4 ← نقل صفحة بين المقاطع ← حفظ ← تصدير المقطع PDF ← إعادة فتح.
    /// </summary>
    internal void RunNotebookChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("notebook: " + what); }
        bool Until(Func<bool> cond, int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { if (cond()) return true; Application.DoEvents(); Thread.Sleep(20); } return cond(); }
        static IEnumerable<ToolStripMenuItem> All(ToolStripItemCollection c) =>
            c.OfType<ToolStripMenuItem>().SelectMany(i => new[] { i }.Concat(All(i.DropDownItems)));

        // ---------- القوائم ----------
        var top = _menu.Items.OfType<ToolStripMenuItem>().Select(i => i.Text).ToList();
        Check(top.Take(8).SequenceEqual(new[] { "ملف", "تحرير", "المكتبة", "السبورة", "التشغيل", "عرض", "الأدوات", "مساعدة" }), "menu bar order: " + string.Join(" | ", top));
        Check(_boardQuick.Text!.Contains("سبورة") && _menu.Items.IndexOf(_boardQuick) == _menu.Items.Count - 1, "quick board button after the menus");
        var others = _menu.Items.OfType<ToolStripMenuItem>().Where(i => i != _boardMenu && i != _boardQuick).ToList();
        foreach (var m in others) m.ShowDropDown();
        foreach (var m in others) m.HideDropDown();
        var stray = others.SelectMany(m => All(m.DropDownItems)).Where(i => (i.Text!.Contains("سبورة") || i.Text.Contains("دفتر")) && !i.Text.StartsWith("تراجع") && !i.Text.StartsWith("إعادة")).Select(i => i.Text).ToList();
        Check(stray.Count == 0, "board commands only in «السبورة»: " + string.Join(" | ", stray));
        _boardMenu.ShowDropDown();
        Until(() => false, 200);
        var bm = All(_boardMenu.DropDownItems).Select(i => i.Text ?? "").ToList();
        Check(bm.Any(t => t.Contains("سبورة جديدة")) && bm.Any(t => t.Contains("دفتر جديد")) && bm.Any(t => t.Contains("سبوراتي")) && bm.Any(t => t.Contains("دفاتري")) && bm.Any(t => t.Contains("آخر السبورات")),
            "board menu items: " + string.Join(" | ", bm));
        Check(All(_boardMenu.DropDownItems).Any(i => i.ShortcutKeys == (Keys.Control | Keys.Alt | Keys.B)) && All(_boardMenu.DropDownItems).Any(i => i.ShortcutKeys == (Keys.Control | Keys.Alt | Keys.N)), "board/notebook shortcuts");
        SaveSmokeShot(this, "menu-board");
        _boardMenu.HideDropDown();
        if (!ReaderPane.Available || !HasLibrary) { Console.WriteLine("SMOKE notebook: menus ok, reader not available — skipped"); return; }

        Book? nb = null;
        bool hadShelf = _library.Shelves.Any(s => s.Kind == NotebooksShelfKind);
        string dir = Path.Combine(Path.GetTempPath(), "masar-nb-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            SmokeDialogShots = true;
            try { NewNotebookDialog(); } finally { SmokeDialogShots = false; }
            SmokeNotebookChoice = ("دفتر الفحص", 0);
            nb = NewNotebookDialog();
            Check(nb != null && nb.IsNotebook && nb.IsBoard && nb.LocalPath.Contains(NotebooksFolderName), "notebook file in «دفاتري»: " + nb?.LocalPath);
            Check(_library.Shelves.First(s => s.Id == nb!.ShelfId).Kind == NotebooksShelfKind, "notebooks shelf");
            Check(Until(() => ReaderShown && _currentBook == nb && _reader!.Info?.Pages == 1, 20_000), "notebook opened");
            string Js(string script)
            {
                var t = _reader!.Web.CoreWebView2.ExecuteScriptAsync(script);
                Check(Until(() => t.IsCompleted, 10_000) && !t.IsFaulted, "js timed out");
                return UdemyWeb.Unwrap(t.Result) ?? "";
            }
            Check(Until(() => Js("String(document.querySelectorAll('.nb-sec').length===1 && document.querySelectorAll('.bd-page.nb').length===1 && masar.boardTest().curPage().w===595)") == "true", 10_000), "A4 page + section column");
            // كتابة، صفحة، مقطع تاني، نقل صفحة، عنوان
            string r = Js("""
                (()=>{const B=masar.boardTest(),I=masar.inkTest().Ink;const L=document.querySelector('.bd-page .ink-layer');const R=L.getBoundingClientRect();const k=R.width/1000;
                const ev=(t,x,y,b)=>L.dispatchEvent(new PointerEvent(t,{bubbles:true,cancelable:true,pointerId:1,pointerType:'mouse',button:0,buttons:b,clientX:R.left+x*k,clientY:R.top+y*k}));
                I.setTool('pen','pen');ev('pointerdown',150,200,1);for(let i=1;i<=10;i++)ev('pointermove',150+i*40,200+Math.sin(i)*20,1);ev('pointerup',560,200,0);
                B.curPage().name='الدرس الأول';B.addPage();B.addSection('الكهرباء');B.addPage();
                const sec2=B.meta.sec;B.selectSection(B.meta.sections[0].id);B.movePageToSection(B.vis()[1].id,sec2);
                return [B.meta.sections.length,B.total,B.meta.pages.length,B.meta.pages.filter(p=>p.sec===sec2).length,I.count()].join(',')})()
                """);
            Check(r == "2,1,4,3,1", "sections/pages: " + r);
            Check(Until(() => nb!.PageCount == 4 && JsonNode.Parse(File.ReadAllText(nb.LocalPath)) is JsonObject m && m["kind"]?.GetValue<string>() == "notebook" && (m["sections"] as JsonArray)?.Count == 2, 8000), "notebook saved with its sections");
            Js("masar.inkFlush()");
            Check(Until(() => InkStore.Count(InkStore.Load(InkStore.FileFor(_libraryPath, nb!.Id))) == 1, 8000), "notebook ink saved");
            Until(() => false, 600);
            SaveSmokeShot(this, "notebook");
            // تصدير المقطع
            SmokeSavePath = Path.Combine(dir, "section.pdf");
            BoardExportFromMenu(nb!, "sec", false);
            Check(Until(() => File.Exists(SmokeSavePath) && new FileInfo(SmokeSavePath).Length > 1000, 30_000), "section pdf exported");
            // إعادة الفتح على نفس المقطع
            GoHome();
            OpenBook(nb!);
            Check(Until(() => ReaderShown && _currentBook == nb && Js("String(document.querySelectorAll('.nb-sec').length===2 && masar.boardTest().section().name)") == "المقطع الأول", 20_000), "notebook reopens on its section");
            Check(Until(() => Js("String(masar.inkTest().Ink.count())") == "1", 8000), "notebook ink back");
            // رف الدفاتر وآخر الدفاتر في القائمة
            _boardMenu.ShowDropDown(); Until(() => false, 200);
            Check(All(_boardMenu.DropDownItems).Any(i => i.Text!.Contains("دفتر الفحص")), "recent notebooks list");
            _boardMenu.HideDropDown();
            Console.WriteLine("SMOKE notebook: ok");
        }
        finally
        {
            SmokeNotebookChoice = null;
            SmokeSavePath = null;
            if (ReaderShown) CloseReader(showHome: true);
            if (nb != null)
            {
                try { File.Delete(nb.LocalPath); } catch { }
                try { File.Delete(InkStore.FileFor(_libraryPath, nb.Id)); } catch { }
                _library.Books.Remove(nb);
            }
            if (!hadShelf) _library.Shelves.RemoveAll(s => s.Kind == NotebooksShelfKind && !_library.Books.Any(x => x.ShelfId == s.Id));
            RebuildTree();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
