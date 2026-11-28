using LecturePlayer.Data;

namespace LecturePlayer;

internal partial class MainForm
{
    /// <summary>
    /// الأدوات العلمية على السبورة (السبورة مفتوحة): اللوحة ← معادلة رياضية وكيميائية وترميز عربي ←
    /// موازنة ← تعديل بنفس المكان ← تلوين ← محفوظة في الشرح ← وبعدين بنشيلها (عشان باقي الفحص).
    /// </summary>
    private void RunScienceChecks(Func<string, string> Js, Func<Func<bool>, int, bool> Until, Book b)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("science: " + what); }
        string before = Js("String(masar.inkTest().Ink.count())");
        // الموازنة (حسابات بحتة)
        string bal = Js("""
            (()=>{const C=masar.sciTest().Chem;const r=[];
            for (const e of ["H2 + O2 -> H2O","Fe + O2 -> Fe2O3","KMnO4 + HCl -> KCl + MnCl2 + Cl2 + H2O","MnO4- + Fe2+ + H+ -> Mn2+ + Fe3+ + H2O","Ca(OH)2 + H3PO4 -> Ca3(PO4)2 + H2O"]) r.push(C.balance(e).coefs.join(":"));
            let err="";try{C.balance("H2 + O2 -> H2O2 + H2O")}catch(x){err="multi"}
            r.push(err, String(C.isBalanced("2H2 + O2 -> 2H2O")), String(C.isBalanced("H2 + O2 -> H2O")));
            return r.join("|")})()
            """);
        Check(bal == "2:1:2|4:3:2|2:16:2:2:5:8|1:5:8:1:5:4|3:2:1:6|multi|true|false", "balance: " + bal);

        // اللوحة + الإدراج (المعادلة بتترسم بـ MathJax جوه البرنامج نفسه)
        Js("masar.sciTest().Sci.open('chem'); 'ok'");
        Check(Until(() => Js("String(!!document.querySelector('.sci-panel:not([hidden]) .sci-tile') && document.querySelector('.bd-tools button.on[title^=\"معادلات\"]')!==null)") == "true", 5000), "panel open");
        Js("""
            window.__sci=null;(async()=>{try{const S=masar.sciTest().Sci;
            const a=await S.place({k:'eq',tex:'x=\\frac{-b\\pm\\sqrt{b^2-4ac}}{2a}',fs:34});
            const c=await S.place({k:'chem',ce:'2H2 + O2 -> 2H2O',fs:34});
            const r=await S.place({k:'eq',tex:'\\text{ص}=\\text{س}^2+\\operatorname{جا}\\text{س}',fs:34,rtl:1});
            const h=await S.place({k:'chem',ce:'Fe + O2 -> Fe2O3',hide:1,fs:34});
            window.__sci={ids:[a.id,c.id,r.id,h.id],ok:[a,c,r,h].every(i=>i.t==='image'&&i.sci&&i.img.startsWith('data:image/svg+xml')&&i.w>40&&i.h>10)};
            }catch(e){window.__sci={err:String(e&&e.message||e)}}})();'ok'
            """);
        Check(Until(() => Js("String(!!window.__sci)") == "true", 30_000), "equations rendered (MathJax)");
        Check(Js("JSON.stringify(window.__sci.err||window.__sci.ok)") == "true", "equations placed: " + Js("JSON.stringify(window.__sci)"));
        Check(Js("String(masar.inkTest().Ink.count())") == (int.Parse(before) + 4).ToString(), "4 science items");
        Until(() => false, 500);
        SaveSmokeShot(this, "board-science");

        // التعديل بنفس المكان (نفس الرقم) + التلوين بيرسمها تاني
        Js("""
            window.__sci2=null;(async()=>{const S=masar.sciTest().Sci,I=masar.inkTest().Ink,key=S.board.curPage().id,id=window.__sci.ids[0];
            const old=I.doc.items(key).find(i=>i.id===id);
            await S.place({k:'eq',tex:'a^2+b^2=c^2',fs:34},{key,id});
            const n=I.doc.items(key).find(i=>i.id===id);
            I.setSel({key,ids:new Set([id])});I.recolorSel('#1971c2');
            await new Promise(r=>setTimeout(r,1500));
            const c=I.doc.items(key).find(i=>i.id===id);
            window.__sci2={same:n.id===old.id&&Math.abs((n.x+n.w/2)-(old.x+old.w/2))<1,tex:n.sci.tex,recol:c.sci.c==='#1971c2'&&c.img!==n.img};I.setSel(null)})();'ok'
            """);
        Check(Until(() => Js("String(!!window.__sci2)") == "true", 20_000), "edit finished");
        Check(Js("JSON.stringify(window.__sci2)") == "{\"same\":true,\"tex\":\"a^2+b^2=c^2\",\"recol\":true}", "edit/recolor: " + Js("JSON.stringify(window.__sci2)"));

        // المحرر: MathLive + المعاينة
        Js("masar.sciTest().Sci.editor({k:'eq',tex:'\\\\sqrt{x}'}); 'ok'");
        Check(Until(() => Js("String(!!document.querySelector('.sci-dlg math-field') && !!document.querySelector('.sci-prev img'))") == "true", 20_000), "editor with MathLive + preview");
        Check(Js("String(document.body.classList.contains('sci-on'))") == "true", "keys stay in the editor");
        Until(() => false, 400);
        SaveSmokeShot(this, "board-science-editor");
        Js("masar.sciTest().Sci.closeEditor(); 'ok'");
        Js("masar.sciTest().Sci.editor({k:'chem',ce:'Al + HCl -> AlCl3 + H2'}); 'ok'");
        Check(Until(() => Js("String(!!document.querySelector('.sci-dlg .sci-bal'))") == "true", 5000), "chem editor");
        Js("document.querySelector('.sci-dlg .sci-bal').click(); 'ok'");
        Check(Until(() => Js("document.querySelector('.sci-ce').value") == "2 Al + 6 HCl -> 2 AlCl3 + 3 H2", 5000), "balance button: " + Js("document.querySelector('.sci-ce').value"));
        Js("masar.sciTest().Sci.closeEditor(); masar.sciTest().Sci.close(); 'ok'");
        Check(Js("String(document.body.classList.contains('sci-on'))") == "false", "editor closed");

        // محفوظة في ملف الشرح، وبعدين بنشيلها
        Js("masar.inkFlush()");
        Check(Until(() => InkStore.Load(InkStore.FileFor(_libraryPath, b.Id)).ToJsonString().Contains("\"sci\""), 8000), "science items saved with their source");
        Js("""
            (()=>{const I=masar.inkTest().Ink,ids=new Set(window.__sci.ids);for(const [k] of I.doc.pages) I.edit(k,its=>its.filter(i=>!ids.has(i.id)));I.flush();return 'ok'})()
            """);
        Check(Until(() => Js("String(masar.inkTest().Ink.count())") == before, 5000), "science items removed");
        Console.WriteLine("SMOKE science: ok");
    }
}
