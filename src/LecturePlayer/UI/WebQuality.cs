using System.Globalization;

namespace LecturePlayer.UI;

/// <summary>
/// «أعلى جودة تلقائيًا» لدروس المنصات (يوديمي، IT Legend/VdoCipher، أي مشغّل فيه قايمة جودة):
/// سكربت بيفتح قايمة الإعدادات في مشغّل الموقع نفسه، يلاقي اختيارات زي «1080p»، ويختار الأعلى (لحد الحد اللي إنت محدده)،
/// ويقفل القايمة. مش بيلمس الفيديو ولا الحماية — بيعمل نفس اللي كنت هتعمله بإيدك من زرار ⚙.
/// النتيجة بتتحط في window.__masarQ و«مسار» بيقراها مع حالة الفيديو.
/// </summary>
internal static class WebQuality
{
    /// <summary>من غير حد = أعلى المتاح.</summary>
    public const int Max = 9999;

    /// <summary>الاختيارات اللي في الإعدادات وقايمة الجودة: (الحد، الاسم). 0 = سيبها للموقع.</summary>
    public static readonly (int Cap, string Label)[] Caps =
    {
        (Max, "أعلى جودة متاحة"),
        (1080, "لحد 1080p"),
        (720, "لحد 720p (نت أبطأ)"),
        (0, "سيبها للموقع (Auto)"),
    };

    public static int CapIndex(int cap) => Math.Max(0, Array.FindIndex(Caps, c => c.Cap == cap));

    /// <summary>السكربت (بيشتغل في الإطار اللي فيه الفيديو). tok = رقم المحاولة عشان نعرف إن النتيجة بتاعتها.</summary>
    public static string Script(int cap, int tok) =>
        Source.Replace("__CAP__", Math.Max(1, cap).ToString(CultureInfo.InvariantCulture)).Replace("__TOK__", tok.ToString(CultureInfo.InvariantCulture));

    /// <summary>الدقة بالاسم المعروف (1920×1080 = 1080p، 1920×800 سينما = 1080p، 1280×720 = 720p).</summary>
    public static int NominalHeight(int w, int h)
    {
        if (w <= 0 || h <= 0) return 0;
        int lo = Math.Min(w, h), hi = Math.Max(w, h);
        int eq = Math.Max(lo, (int)Math.Round(hi * 9.0 / 16));
        foreach (int s in new[] { 144, 240, 360, 480, 540, 576, 720, 900, 1080, 1440, 2160, 4320 })
            if (Math.Abs(eq - s) <= s * 0.08) return s;
        return eq;
    }

    public static string Label(int w, int h)
    {
        int p = NominalHeight(w, h);
        return p >= 2160 ? (p >= 4320 ? "8K" : "4K") : p + "p";
    }

    private const string Source = """
        (async (cap, tok) => {
          // أعلى جودة في مشغّل الموقع (يوديمي، VdoCipher، video.js…): بندوّر على اختيارات زي «1080p» في قايمة الإعدادات ونختار الأعلى (لحد cap).
          // النتيجة في window.__masarQ ويقراها «مسار» مع حالة الفيديو.
          const old = window.__masarQ;
          if (old && old.s === 'run' && Date.now() - old.at < 15000) { old.tok = tok; return; }
          const R = window.__masarQ = { s: 'run', at: Date.now(), cap, tok, p: '', max: 0, list: [], how: '' };
          try {
            const sleep = ms => new Promise(r => setTimeout(r, ms));
            const digits = t => t.replace(/[٠-٩]/g, d => String(d.charCodeAt(0) - 0x660)).replace(/[۰-۹]/g, d => String(d.charCodeAt(0) - 0x6F0));
            const txt = e => digits((e.innerText || e.textContent || '').replace(/\s+/g, ' ').trim());
            const resOf = t => {
              t = t.replace(/^[^0-9A-Za-z؀-ۿ]+/, '');
              let m = /^(\d{3,4})\s*p(?:\s*\d{2})?(?![a-z])/i.exec(t);
              if (m) return +m[1];
              m = /^([248])\s*k(?![a-z])/i.exec(t);
              return m ? ({ 2: 1440, 4: 2160, 8: 4320 })[m[1]] : 0;
            };
            const shown = e => { const r = e.getBoundingClientRect(), cs = getComputedStyle(e);
              return r.width > 0 && r.height > 0 && cs.visibility !== 'hidden' && cs.display !== 'none'; };
            const clickable = e => e.closest('button,[role^="menuitem"],[role="option"],[role="radio"],[role="button"],li,label,a') || e;
            const isOn = e => {
              for (let x = e, i = 0; x && i < 3; x = x.parentElement, i++) {
                for (const a of ['aria-checked', 'aria-selected', 'aria-pressed', 'aria-current', 'data-selected', 'data-state'])
                  { const v = x.getAttribute && x.getAttribute(a); if (v === 'true' || v === 'checked' || v === 'on' || v === 'page') return true; }
                if (typeof x.className === 'string' && /(^|[\s_-])(selected|active|checked|current|vjs-selected)($|[\s_-])/i.test(x.className)) return true;
                if (x.querySelector && x.querySelector('input:checked')) return true;
              }
              return false;
            };
            // الاختيارات: أصغر عنصر نصه بيبدأ بدقة (1080p، 720p HD، 4K…)
            const scan = () => {
              const out = [], seen = new Set();
              for (const e of document.querySelectorAll('button,[role^="menuitem"],[role="option"],[role="radio"],li,label,a,span,div')) {
                const t = txt(e);
                if (!t || t.length > 24) continue;
                const res = resOf(t);
                if (!res) continue;
                if ([...e.children].some(c => resOf(txt(c)) === res)) continue; // فيه ابن أدق
                const c = clickable(e);
                if (seen.has(c)) continue;
                seen.add(c);
                out.push({ el: c, res, label: t, vis: shown(c) });
              }
              return out;
            };
            const fire = (e, how) => {
              if (how === 'click') { e.click(); return; }
              const r = e.getBoundingClientRect(), o = { bubbles: true, cancelable: true, composed: true, button: 0, buttons: 1,
                clientX: r.left + r.width / 2, clientY: r.top + r.height / 2, pointerType: 'mouse', isPrimary: true, view: window };
              e.dispatchEvent(new PointerEvent('pointerdown', o)); e.dispatchEvent(new MouseEvent('mousedown', o));
              e.dispatchEvent(new PointerEvent('pointerup', { ...o, buttons: 0 })); e.dispatchEvent(new MouseEvent('mouseup', { ...o, buttons: 0 }));
              if (how === 'full') e.dispatchEvent(new MouseEvent('click', { ...o, buttons: 0 }));
            };
            const word = /quality|resolution|الجودة|جودة|الدقة|دقة/i;
            const gear = /setting|gear|cog|إعداد|الاعداد|options/i;
            const keyOf = e => [e.getAttribute('aria-label'), e.getAttribute('title'), e.getAttribute('data-purpose'), e.getAttribute('data-testid'),
              typeof e.className === 'string' ? e.className : '', e.id].filter(Boolean).join(' ');
            // زرار القايمة: «الجودة» الأول، وبعدين «الإعدادات» — جوه نطاق المشغّل لو لقيناه
            const openers = () => {
              let all = [...document.querySelectorAll('button,[role="button"]')].filter(shown);
              // الأزرار القريبة من الفيديو بس (أزرار المشغّل)، عشان مانفتحش «إعدادات» تانية في الصفحة
              let v = null, a = 0;
              for (const x of document.querySelectorAll('video')) { const r = x.getBoundingClientRect(); if (r.width * r.height > a) { a = r.width * r.height; v = r; } }
              if (v) {
                const near = all.filter(b => { const r = b.getBoundingClientRect(), cx = r.left + r.width / 2, cy = r.top + r.height / 2;
                  return cx >= v.left - 80 && cx <= v.right + 80 && cy >= v.top - 80 && cy <= v.bottom + 80; });
                if (near.length) all = near;
              }
              // الأقرب للفيديو في الشجرة الأول (أصغر أب مشترك بينهم = جوه المشغّل نفسه)
              const vid = (() => { let b = null, a = 0; for (const x of document.querySelectorAll('video')) { const r = x.getBoundingClientRect(); if (r.width * r.height > a) { a = r.width * r.height; b = x; } } return b; })();
              const dist = b => { if (!vid) return 0; for (let p = b; p; p = p.parentElement) if (p.contains(vid)) { const r = p.getBoundingClientRect(); return r.width * r.height; } return 1e12; };
              const geo = b => { if (!vid) return 0; const v = vid.getBoundingClientRect(), r = b.getBoundingClientRect(), cx = r.left + r.width / 2, cy = r.top + r.height / 2;
                return Math.hypot(Math.max(v.left - cx, 0, cx - v.right), Math.max(v.top - cy, 0, cy - v.bottom)); };
              const byNear = list => list.map(b => [b, dist(b), geo(b)]).sort((x, y) => x[1] - y[1] || x[2] - y[2]).map(x => x[0]);
              const q = byNear(all.filter(b => word.test(keyOf(b)) && !resOf(txt(b))));
              const s = byNear(all.filter(b => gear.test(keyOf(b)) && !q.includes(b)));
              return [...q, ...s].slice(0, 3);
            };
            // «الجودة» جوه قايمة الإعدادات (بتفتح قايمة تانية)
            const subEntry = () => {
              const re = /^(video quality|quality|resolution|الجودة|جودة الفيديو|جودة|الدقة)(?![a-z])/i;
              const hits = [...document.querySelectorAll('button,[role^="menuitem"],[role="option"],li,a,div,span')]
                .filter(e => { const t = txt(e); return t && t.length <= 40 && re.test(t) && shown(e); });
              const leaf = hits.filter(e => !hits.some(o => o !== e && e.contains(o)));
              return leaf.length ? clickable(leaf[0]) : null;
            };
            let opts = scan(), opener = null, how = '';
            if (!opts.length) {
              for (const b of openers()) {
                for (const h of ['click', 'press']) {
                  fire(b, h);
                  await sleep(450);
                  opts = scan();
                  if (!opts.length) { const s = subEntry(); if (s) { fire(s, 'click'); await sleep(450); opts = scan(); if (!opts.length) { fire(s, 'full'); await sleep(450); opts = scan(); } } }
                  if (opts.length) { opener = b; how = h; break; }
                  if (h === 'click') { fire(b, 'click'); await sleep(150); } // نقفل اللي اتفتح قبل ما نجرّب طريقة تانية
                }
                if (opts.length) break;
                try { document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', keyCode: 27, bubbles: true })); } catch (x) { }
              }
            }
            R.list = [...new Set(opts.map(o => o.res))].sort((a, b) => b - a);
            R.max = R.list[0] || 0;
            if (!opts.length) { R.s = 'none'; return; }
            const fits = opts.filter(o => o.res <= cap);
            const pool = fits.length ? fits : opts;
            const best = Math.max(...pool.map(o => o.res));
            const targets = pool.filter(o => o.res === best).sort((a, b) => (b.vis - a.vis));
            const t = targets[0];
            R.p = t.label.replace(/\s+/g, ' ');
            if (isOn(t.el)) { R.already = true; }
            else {
              fire(t.el, 'click');
              await sleep(500);
              if (!isOn(t.el) && t.el.isConnected) { fire(t.el, 'full'); await sleep(400); }
              R.picked = true;
            }
            R.how = how || 'direct';
            // نقفل القايمة لو إحنا اللي فتحناها
            if (opener) {
              await sleep(150);
              const still = scan().some(o => o.vis) || opener.getAttribute('aria-expanded') === 'true';
              if (still) { fire(opener, how === 'press' ? 'press' : 'click'); await sleep(200); }
              if (scan().some(o => o.vis)) try { document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', keyCode: 27, bubbles: true })); } catch (x) { }
            }
            R.s = 'ok';
          } catch (x) { R.s = 'err'; R.err = String(x && x.message || x).slice(0, 120); }
          finally { R.at = Date.now(); }
        })(__CAP__, __TOK__);
        """;
}
