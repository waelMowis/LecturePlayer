using LecturePlayer.Data;
using LecturePlayer.UI;

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

        RunScienceShapeChecks(Js, Until);
        RunScienceLibraryChecks(Js, Until);
        RunScienceAppChecks(Js, Until);
        RunScienceOnlineChecks(Js, Until);

        // محفوظة في ملف الشرح، وبعدين بنشيلها
        Js("masar.inkFlush()");
        Check(Until(() => InkStore.Load(InkStore.FileFor(_libraryPath, b.Id)).ToJsonString().Contains("\"sci\""), 8000), "science items saved with their source");
        Js("""
            (()=>{const I=masar.inkTest().Ink,ids=new Set(window.__sci.ids);for(const [k] of I.doc.pages) I.edit(k,its=>its.filter(i=>!ids.has(i.id)));I.flush();return 'ok'})()
            """);
        Check(Until(() => Js("String(masar.inkTest().Ink.count())") == before, 5000), "science items removed");
        Console.WriteLine("SMOKE science: ok");
    }

    /// <summary>
    /// المرحلة الثانية: الأشكال المرسومة بالكود — رسم الدوال (JSXGraph)، الهندسة، الفيزياء، رموز الدوائر، أدوات المعمل، نموذج الذرة —
    /// بتتحط صورة وتتعدل بالنقر المزدوج من نفس الحقول. العناصر بتنضاف لـ __sci.ids عشان تتشال مع الباقي.
    /// </summary>
    private void RunScienceShapeChecks(Func<string, string> Js, Func<Func<bool>, int, bool> Until)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("science shapes: " + what); }
        // حسابات بحتة: تحويل الدالة، توزيع الإلكترونات، كل أداة بترسم
        string pure = Js("""
            (()=>{const M=masar.sciTest().Shapes;const r=[M.jsExpr('ص = 3x^2 - 2x + 1'),M.jsExpr('2sin(x)+ln(x)'),M.shells(11).join(','),M.shells(26).join(','),M.shells(29).join(','),M.pretty('x^2 - 4')];
            let bad=[];for(const [id,t] of Object.entries(M.TOOLS)){if(t.async)continue;try{const o=t.draw(Object.fromEntries(t.f.map(f=>[f[0],f[3]])),'#111');if(!(o.inner.length>50&&o.w>20&&o.h>20))bad.push(id)}catch(e){bad.push(id+':'+e.message)}}
            r.push(bad.join(';')||'all');return r.join('|')})()
            """);
        Check(pure == "3*x^2 - 2*x + 1|2*sin(x)+log(x)|2,8,1|2,8,14,2|2,8,18,1|x² - 4|all", "pure: " + pure);

        // اللوحة: أدوات كل مادة + شبكة المعمل والرموز
        Js("masar.sciTest().Sci.open('math'); 'ok'");
        Check(Until(() => Js("String(document.querySelectorAll('.sci-panel .sci-tile[data-tool]').length)") == "8", 5000), "math tools: " + Js("String(document.querySelectorAll('.sci-panel .sci-tile[data-tool]').length)"));
        Js("masar.sciTest().Sci.open('phys'); 'ok'");
        Check(Until(() => Js("document.querySelectorAll('.sci-panel .sci-tile[data-tool]').length+':'+document.querySelectorAll('.sci-panel .sci-grid button').length") == "7:21", 5000), "physics tools + circuit symbols");
        Js("masar.sciTest().Sci.open('chem'); 'ok'");
        Check(Until(() => Js("String(document.querySelectorAll('.sci-panel .sci-grid button[data-shape^=\"lab:\"] img').length)") == "36", 5000), "lab apparatus thumbnails: " + Js("String(document.querySelectorAll('.sci-panel .sci-grid img').length)"));

        // الإدراج: شبكة المعمل بضغطة + أدوات بحقول + رسم دالة
        Js("""
            window.__shp=null;(async()=>{try{const S=masar.sciTest().Sci,I=masar.inkTest().Ink;const n0=I.count();
            document.querySelector('.sci-grid button[data-shape="lab:titration"]').click();
            await new Promise(r=>setTimeout(r,600));
            const key=S.board.curPage().id,lab=I.doc.items(key).filter(i=>i.sci&&i.sci.k==='shape').at(-1);
            const g=await S.place({k:'shape',type:'graph',p:{f1:'x^2-4',f2:'2sin(x)',points:'(2,0) (-2,0)'}});
            const t=await S.place({k:'shape',type:'triangle',p:{type:'right',sides:1}});
            const v=await S.place({k:'shape',type:'freebody',p:{incline:30}});
            const s=await S.place({k:'shape',type:'symbol',p:{s:'lamp',label:'L'}});
            const a=await S.place({k:'shape',type:'atom',p:{z:11}});
            const all=[lab,g,t,v,s,a];window.__sci.ids.push(...all.map(i=>i.id));
            window.__shp={n:I.count()-n0,ok:all.every(i=>i.t==='image'&&i.img.startsWith('data:image/svg+xml')&&i.w>30&&i.h>30),lab:lab.sci.type+':'+lab.sci.p.type,graph:atob(g.img.split(',')[1]).includes('<path')};
            }catch(e){window.__shp={err:String(e&&e.message||e)}}})();'ok'
            """);
        Check(Until(() => Js("String(!!window.__shp)") == "true", 30_000), "shapes placed");
        Check(Js("JSON.stringify(window.__shp)") == "{\"n\":6,\"ok\":true,\"lab\":\"lab:titration\",\"graph\":true}", "shapes: " + Js("JSON.stringify(window.__shp)"));
        Until(() => false, 500);
        SaveSmokeShot(this, "board-science-shapes");

        // التعديل بالنقر المزدوج: محرر الحقول ← غيّر مستوى السائل ← احفظ بنفس الرقم
        Js("""
            (()=>{const S=masar.sciTest().Sci,I=masar.inkTest().Ink,key=S.board.curPage().id,id=window.__sci.ids.at(-6);
            S.editItem(I.surfaces.get(key),I.doc.items(key).find(i=>i.id===id));return 'ok'})()
            """);
        Check(Until(() => Js("String(!!document.querySelector('.sci-dlg[data-shape-dlg=lab] .sci-prev img'))") == "true", 8000), "shape editor with preview");
        Check(Js("String(!document.querySelector('.sci-row[data-f=liq2]').hidden && document.querySelector('.sci-row[data-f=flame]').hidden)") == "true", "fields shown per apparatus");
        Until(() => false, 300);
        SaveSmokeShot(this, "board-science-shape-editor");
        Js("""
            (()=>{const i=document.querySelector('.sci-row[data-f=level] input');i.value='85';i.dispatchEvent(new Event('input'));
            [...document.querySelectorAll('.sci-dlg .sci-foot button')][0].click();return 'ok'})()
            """);
        Check(Until(() => Js("""
            (()=>{const S=masar.sciTest().Sci,I=masar.inkTest().Ink,key=S.board.curPage().id,it=I.doc.items(key).find(i=>i.id===window.__sci.ids.at(-6));return String(!document.querySelector('.sci-dlg')&&it.sci.p.level==='85')})()
            """) == "true", 8000), "lab item edited in place");
        Js("masar.sciTest().Sci.close(); 'ok'");
    }

    /// <summary>
    /// المرحلة الثالثة: مكتبة الرسوم — الفهرس والبحث بالعربي، والصور المصغرة والرسوم بتتنزل عن طريق البرنامج
    /// من مصادرها المسموحة بس وتتحفظ، وذكر المصدر لـ CC BY، وأسهم التسمية الفارغة. PhyloPic أونلاين: بنسجل النتيجة بس.
    /// </summary>
    private void RunScienceLibraryChecks(Func<string, string> Js, Func<Func<bool>, int, bool> Until)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("science library: " + what); }
        Js("""
            window.__lib=null;(async()=>{try{const L=await import('./sci-lib.js');
            const n=async q=>(await L.search(q)).items.length;
            const r={heart:await n('قلب'),plant:await n('خلية نباتية'),bact:await n('بكتير'),flask:await n('دورق'),en:await n('mitochondria')};
            const d=await L.loadLib();r.total=d.list.length;
            const t=await fetch('/scipack/gh/waelMowis/LecturePlayer/master/packs/thumbs/v1/'+encodeURIComponent(d.sheets[0]));r.thumb=t.status+':'+(await t.blob()).size;
            r.forbid=(await fetch('/scipack/gh/evil/repo/main/x.svg')).status+','+(await fetch('/scipack/gh/duerrsimon/bioicons/../../x')).status;
            window.__lib=r}catch(e){window.__lib={err:String(e&&e.message||e)}}})();'ok'
            """);
        Check(Until(() => Js("String(!!window.__lib)") == "true", 60_000), "index + thumbs");
        string lib = Js("JSON.stringify(window.__lib)");
        Console.WriteLine("SMOKE science library: " + lib);
        Check(Js("""(()=>{const r=window.__lib;return String(!r.err&&r.total>3000&&r.heart>5&&r.plant>0&&r.bact>5&&r.flask>3&&r.en>0&&/^200:\d{4,}/.test(r.thumb)&&/^40\d,40\d$/.test(r.forbid))})()""") == "true", "library: " + lib);

        // الإدراج من اللوحة: رسم CC BY (بذكر المصدر) ورسم CC0، وأسهم تسمية
        Js("masar.sciTest().Sci.open('bio'); 'ok'");
        Check(Until(() => Js("String(document.querySelectorAll('.sci-panel .sci-lib-it').length>=10)") == "true", 20_000), "library grid in the biology tab");
        Js("""
            window.__pic=null;(async()=>{try{const S=masar.sciTest().Sci,I=masar.inkTest().Ink,L=await import('./sci-lib.js');
            const key=S.board.curPage().id,n0=I.count();
            const by=(await L.search('heart')).items.find(i=>i.lic.startsWith('cc-by')),c0=(await L.search('قلب')).items.find(i=>i.lic==='cc-0');
            const a=await S.placePic({k:'pic',src:by.src,path:by.path,name:by.name,lic:by.lic,author:by.author});
            const b=await S.placePic({k:'pic',src:c0.src,path:c0.path,name:c0.name,lic:c0.lic,author:c0.author});
            const labels=S.addLabels(key,a.id,3);
            window.__sci.ids.push(a.id,b.id,...labels.map(x=>x.id));
            const svgA=atob(a.img.split(',')[1]);
            window.__pic={n:I.count()-n0,credit:svgA.includes(by.author)&&svgA.includes('CC BY'),noCredit:!atob(b.img.split(',')[1]).includes('CC0'),labels:labels.map(x=>x.t+(x.k?':'+x.k:'')).join(','),img:a.img.length>2000&&b.img.length>500};
            }catch(e){window.__pic={err:String(e&&e.message||e)}}})();'ok'
            """);
        Check(Until(() => Js("String(!!window.__pic)") == "true", 90_000), "pictures downloaded and placed");
        Check(Js("JSON.stringify(window.__pic)") == "{\"n\":8,\"credit\":true,\"noCredit\":true,\"labels\":\"text,shape:arrow,text,shape:arrow,text,shape:arrow\",\"img\":true}", "pictures: " + Js("JSON.stringify(window.__pic)"));
        Until(() => false, 600);
        SaveSmokeShot(this, "board-science-library");

        // محرر الرسم بالنقر المزدوج
        Js("""
            (()=>{const S=masar.sciTest().Sci,I=masar.inkTest().Ink,key=S.board.curPage().id,id=window.__sci.ids.at(-8);
            S.editItem(I.surfaces.get(key),I.doc.items(key).find(i=>i.id===id));return 'ok'})()
            """);
        Check(Until(() => Js("String(!!document.querySelector('.sci-dlg[data-pic-dlg] .sci-prev img'))") == "true", 15_000), "picture editor");
        Check(Js("String(document.querySelector('.sci-dlg[data-pic-dlg] input[type=checkbox]').disabled)") == "true", "CC BY credit can't be removed");
        Js("masar.sciTest().Sci.closeEditor(); masar.sciTest().Sci.close(); 'ok'");

        // PhyloPic (أونلاين، مصدر خارجي): بنسجل النتيجة من غير ما نوقف الفحص
        Js("""
            window.__phy=null;(async()=>{try{const L=await import('./sci-lib.js');const r=await L.phyloSearch('نحلة');
            let img='';if(r[0]){const x=await fetch('/scipack/phylo/img/'+r[0].path);img=x.status+':'+(await x.text()).slice(0,40)}
            window.__phy=r.length+' images; first: '+(r[0]?r[0].name+' / '+r[0].author+' / '+r[0].path+' / '+img:'-')}catch(e){window.__phy='error: '+(e&&e.message||e)}})();'ok'
            """);
        Until(() => Js("String(!!window.__phy)") == "true", 45_000);
        Console.WriteLine("SMOKE phylopic: " + Js("String(window.__phy)"));
    }

    /// <summary>
    /// المرحلة الرابعة: الأدوات التفاعلية الكبيرة — الجدول الدوري، الجزيئات ثلاثية الأبعاد، مشهد الحركة،
    /// محرر الجزيئات (Ketcher بيتنزل أول مرة)، محاكي البصريات (بيتنزل أول مرة). كل واحدة بتنزل نتيجتها على الصفحة
    /// ومعاها مصدرها، والنقر المزدوج بيفتحها تاني.
    /// </summary>
    private void RunScienceAppChecks(Func<string, string> Js, Func<Func<bool>, int, bool> Until)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("science apps: " + what); }
        // الجدول الدوري
        Js("window.__ap=null;masar.sciTest().Sci.apps.ptable({z:26}).then(()=>window.__ap='ok',e=>window.__ap=String(e.message)); 'ok'");
        Check(Until(() => Js("String(window.__ap)") != "null", 15_000), "periodic table opened");
        Check(Js("document.querySelectorAll('.sci-win .pt-el').length+':'+document.querySelector('.pt-big b').textContent") == "118:Fe", "periodic table: " + Js("String(window.__ap)"));
        Until(() => false, 400);
        SaveSmokeShot(this, "board-science-ptable");
        Js("""
            window.__pt=null;(async()=>{const S=masar.sciTest().Sci,I=masar.inkTest().Ink,key=S.board.curPage().id,n0=I.count();
            [...document.querySelectorAll('.pt-ins button')][0].click();await new Promise(r=>setTimeout(r,1500));
            await S.apps.ptable({});[...document.querySelectorAll('.pt-ins button')].at(-1).click();await new Promise(r=>setTimeout(r,2000));
            const its=I.doc.items(key).filter(i=>i.sci&&i.sci.k==='shape'&&(i.sci.type==='element'||i.sci.type==='ptable'));window.__sci.ids.push(...its.map(i=>i.id));
            window.__pt=(I.count()-n0)+':'+its.map(i=>i.sci.type+(i.sci.p.z||'')).join(',')+':'+!document.querySelector('.sci-win')})();'ok'
            """);
        Check(Until(() => Js("String(window.__pt)") != "null", 15_000), "periodic inserts");
        Check(Js("String(window.__pt)") == "2:element26,ptable:true", "periodic inserts: " + Js("String(window.__pt)"));

        // الجزيئات ثلاثية الأبعاد (WebGL) ومشهد الحركة
        Js("""
            window.__m3r=null;(async()=>{try{const S=masar.sciTest().Sci,I=masar.inkTest().Ink,key=S.board.curPage().id;
            await S.apps.mol3d({});await new Promise(r=>setTimeout(r,800));
            const n=document.querySelectorAll('.m3-list button').length;document.querySelector('.m3-list button[data-name="جلوكوز"]').click();await new Promise(r=>setTimeout(r,800));
            document.querySelector('.sci-win .sci-win-act .pri').click();await new Promise(r=>setTimeout(r,2500));
            const it=I.doc.items(key).filter(i=>i.sci&&i.sci.app==='mol3d').at(-1);window.__sci.ids.push(it.id);
            await S.apps.motion({});await new Promise(r=>setTimeout(r,300));for(const b of [...document.querySelectorAll('.mo-bar button')].slice(0,5))b.click();await new Promise(r=>setTimeout(r,1500));
            const bodies=window.__motion.engine.world.bodies.length;document.querySelector('.sci-win .sci-win-act .pri').click();await new Promise(r=>setTimeout(r,2000));
            const mo=I.doc.items(key).filter(i=>i.sci&&i.sci.app==='motion').at(-1);window.__sci.ids.push(mo.id);
            window.__m3r=[n,it.sci.name,!it.sci.__img,it.img.startsWith('data:image/jpeg'),it.img.length>8000,bodies>=8,mo.img.length>3000].join(',')}catch(e){window.__m3r='err '+(e&&e.message||e)}})();'ok'
            """);
        Check(Until(() => Js("String(window.__m3r)") != "null", 30_000), "3D + motion");
        Check(Js("String(window.__m3r)") == "52,جلوكوز,true,true,true,true,true", "3D + motion: " + Js("String(window.__m3r)"));

        // محرر الجزيئات (Ketcher): بيتنزل من حزم «مسار» أول مرة
        Js("window.__kt=null;masar.sciTest().Sci.apps.ketcher({}); 'ok'");
        bool ktReady = Until(() => Js("String(!!window.__ketcherCall && document.querySelector('.sci-win-msg') && document.querySelector('.sci-win-msg').textContent==='')") == "true", 180_000);
        Check(ktReady, "Ketcher downloaded and loaded: " + Js("document.querySelector('.sci-win-msg')?.textContent||''"));
        Js("""
            (async()=>{try{await window.__ketcherCall('set','c1ccccc1O');await new Promise(r=>setTimeout(r,600));const g=await window.__ketcherCall('get');
            document.querySelector('.sci-win .sci-win-act .pri').click();await new Promise(r=>setTimeout(r,2500));
            const S=masar.sciTest().Sci,I=masar.inkTest().Ink,key=S.board.curPage().id,it=I.doc.items(key).filter(i=>i.sci&&i.sci.app==='ketcher').at(-1);window.__sci.ids.push(it.id);
            window.__kt=[g.smiles.length>4,it.sci.mol.includes('V2000')||it.sci.mol.includes('V3000'),it.img.startsWith('data:image/svg+xml')].join(',')}catch(e){window.__kt='err '+(e&&e.message||e)}})();'ok'
            """);
        Check(Until(() => Js("String(window.__kt)") != "null", 30_000), "Ketcher insert");
        Check(Js("String(window.__kt)") == "true,true,true", "Ketcher insert: " + Js("String(window.__kt)"));
        Until(() => false, 500);
        SaveSmokeShot(this, "board-science-apps");

        // محاكي البصريات: مثال جاهز ← لقطة على الصفحة ومعاها المشهد
        Js("window.__op=null;masar.sciTest().Sci.apps.optics({hash:'convex-lens'}); 'ok'");
        Check(Until(() => Js("(()=>{try{const f=document.querySelector('.sci-iframe');return String(f.contentWindow.location.hash.length>40)}catch(e){return 'false'}})()") == "true", 180_000), "optics simulator downloaded, example loaded");
        Until(() => false, 1500);
        SaveSmokeShot(this, "board-science-optics");
        Js("""
            (async()=>{document.querySelector('.sci-win .sci-win-act .pri').click();await new Promise(r=>setTimeout(r,2500));
            const S=masar.sciTest().Sci,I=masar.inkTest().Ink,key=S.board.curPage().id,it=I.doc.items(key).filter(i=>i.sci&&i.sci.app==='optics').at(-1);
            if(it)window.__sci.ids.push(it.id);window.__op=it?[it.sci.hash.length>40,it.img.length>5000].join(','):'none'})();'ok'
            """);
        Check(Until(() => Js("String(window.__op)") != "null", 15_000), "optics capture");
        Check(Js("String(window.__op)") == "true,true", "optics capture: " + Js("String(window.__op)"));

        // PubChem (أونلاين، مصدر خارجي): بنسجل النتيجة بس
        Js("window.__pc=null;fetch('/scipack/pubchem/compound/name/caffeine/SDF?record_type=3d').then(async r=>window.__pc=r.status+' '+(await r.text()).length,e=>window.__pc='error '+e); 'ok'");
        Until(() => Js("String(window.__pc)") != "null", 30_000);
        Console.WriteLine("SMOKE pubchem: " + Js("String(window.__pc)"));
        Js("masar.sciTest().Sci.close(); 'ok'");
    }

    /// <summary>
    /// المرحلة الخامسة: المصادر الأونلاين — محاكاة PhET في نافذة «مسار» ولقطتها على الصفحة بذكر المصدر،
    /// وبحث PubChem وWikimedia Commons وإدراج النتيجة بترخيصها. GeoGebra وDesmos: بنسجل إن الصفحة اتفتحت.
    /// </summary>
    private void RunScienceOnlineChecks(Func<string, string> Js, Func<Func<bool>, int, bool> Until)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("science online: " + what); }
        // PhET في نافذة البرنامج
        Js("masar.sciTest().Sci.open('phys'); document.querySelector('.sci-panel [data-phet=\"pendulum-lab\"]')?.click() || document.querySelector('.sci-panel [data-phet]').click(); 'ok'");
        Check(Until(() => SciWebForm.Current is { IsDisposed: false }, 10_000), "PhET window opened");
        var form = SciWebForm.Current!;
        bool loaded = Until(() => form.PageLoaded, 90_000);
        Until(() => false, 6000);   // المحاكاة نفسها بتحمّل بعد الصفحة
        Console.WriteLine("SMOKE phet: loaded=" + loaded + " title=" + form.Text);
        SaveSmokeShot(form, "science-phet-window");
        Js("window.__webn=masar.inkTest().Ink.count(); 'ok'");
        var cap = form.CaptureAsync();
        Check(Until(() => cap.IsCompleted, 20_000) && cap.Result is { Length: > 5000 }, "capture");
        try { string tmp = Path.Combine(Path.GetTempPath(), "masar-phet.jpg"); File.WriteAllBytes(tmp, cap.Result!); SaveSmokeShotFile(tmp, "science-phet-capture"); } catch (Exception) { }
        Check(Until(() => Js("String(masar.inkTest().Ink.count()>window.__webn)") == "true", 15_000), "capture placed on the page");
        Check(Js("""
            (()=>{const S=masar.sciTest().Sci,I=masar.inkTest().Ink,key=S.board.curPage().id,it=I.doc.items(key).filter(i=>i.sci&&i.sci.app==='web').at(-1);
            window.__sci.ids.push(it.id);return String(it.sci.source==='phet'&&it.sci.url.includes('phet.colorado.edu')&&atob(it.img.split(',')[1]).includes('PhET Interactive Simulations'))})()
            """) == "true", "PhET capture with credit");
        // GeoGebra في نفس النافذة
        Js("masar.sciTest().Sci.open('math'); document.querySelector('.sci-panel [data-web=\"geogebra\"]').click(); 'ok'");
        bool gg = Until(() => form.PageLoaded && form.Web.CoreWebView2?.Source?.Contains("geogebra.org") == true, 60_000);
        Console.WriteLine("SMOKE geogebra: " + gg + " " + form.Web.CoreWebView2?.Source);
        form.Close();

        // PubChem وWikimedia Commons (بحث عن طريق البرنامج)
        Js("""
            window.__onl=null;(async()=>{const r={};try{const W=await import('./sci-web.js');const c=await W.pubchemCompound('aspirin');r.pc=c.formula+' '+c.cid;
            const A=masar.sciTest().Sci.apps;const a=await A.insert({app:'pubchem',cid:c.cid,title:c.title},await W.pubchemSvg(c),null,360);window.__sci.ids.push(a.id);r.pcimg=atob(a.img.split(',')[1]).includes('PubChem CID');
            const list=await W.commonsSearch('بركان');r.cm=list.length;const b=await A.insert({app:'commons',title:list[0].title,lic:list[0].lic},await W.commonsSvg(list[0]),null,480);window.__sci.ids.push(b.id);
            r.cmimg=atob(b.img.split(',')[1]).includes('Wikimedia Commons');r.lic=list[0].lic}catch(e){r.err=String(e&&e.message||e)}window.__onl=r})();'ok'
            """);
        Check(Until(() => Js("String(!!window.__onl)") == "true", 90_000), "PubChem + Commons finished");
        string onl = Js("JSON.stringify(window.__onl)");
        Console.WriteLine("SMOKE science online: " + onl);
        Check(Js("(()=>{const r=window.__onl;return String(!r.err&&r.pc.startsWith('C9H8O4')&&r.pcimg&&r.cm>3&&r.cmimg)})()") == "true", "PubChem + Commons: " + onl);
        Until(() => false, 500);
        SaveSmokeShot(this, "board-science-online");
        Js("masar.sciTest().Sci.close(); 'ok'");
    }
}
