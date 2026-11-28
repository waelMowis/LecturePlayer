// «البرمجة بالكتل (مبنية على Scratch 3)» جوه «مسار»: نسخة مجمّدة من scratch-gui 4.1.7 (BSD-3) في فولدر scratch جنب البرنامج،
// بتشتغل من غير نت (الصفحة ممنوعة من أي اتصال خارجي)، بشخصية «مسار» بدل القطة ومن غير شعار Scratch.
// من هنا: فتحها في نافذة كبيرة، و«التقط المنصة إلى السبورة» (صورة المنصة + المشروع نفسه لإعادة فتحه بالنقر المزدوج).
import { Apps } from "./sci-apps.js";
import { ToolStore, hash } from "./tool-store.js";

const h = (tag, attrs = {}, ...kids) => {
  const e = document.createElement(tag);
  for (const k in attrs) {
    const v = attrs[k];
    if (v == null || v === false) continue;
    if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (k === "cls") e.className = v;
    else e.setAttribute(k, v === true ? "" : v);
  }
  for (const c of kids.flat(Infinity)) if (c != null && c !== false) e.append(c.nodeType ? c : document.createTextNode(String(c)));
  return e;
};
const MAX_SB3 = 4 * 1024 * 1024;   // أكبر مشروع بيتحفظ جوه السبورة
const toB64 = buf => { const u = new Uint8Array(buf); let s = ""; for (let i = 0; i < u.length; i += 0x8000) s += String.fromCharCode.apply(null, u.subarray(i, i + 0x8000)); return btoa(s); };
export const SCRATCH_URL = "/scratch/index.html?locale=ar";
export const SCRATCH_CREDIT = "مبنية على Scratch 3 (scratch-gui 4.1.7، رخصة BSD) من مؤسسة Scratch — Scratch اسم وعلامة تجارية لمعهد MIT، و«مسار» غير تابع لهما. الوسائط: Scratch (رخصة CC BY-SA 2.0).";

export const ScratchApp = {
  frame: null, api: null, edit: null,

  async available() {
    try { const r = await fetch(SCRATCH_URL.split("?")[0], { method: "HEAD", cache: "no-store" }); return r.ok; } catch { return false; }
  },

  /** فتح النافذة (state.sb3 = مشروع محفوظ في السبورة، و edit = العنصر اللي اتفتح منه) */
  async open(state = {}, edit = null) {
    if (!(await this.available())) throw new Error("البرمجة بالكتل غير مثبتة مع هذه النسخة من «مسار». أعد التثبيت من أحدث إصدار.");
    // الحفظ التلقائي: لكل سبورة مفتاح، ولكل عنصر مفتوح من السبورة مفتاح (تعديلات لسه ماتحفظتش فيه)
    const docKey = "scratch:board:" + (this.sci?.docId || "none"), itemKey = edit ? "scratch:item:" + edit.id : null;
    const base = edit ? hash(state.sb3 || "") : null;
    let sb3 = state.sb3 || null, restored = false;
    if (edit) { const r = await ToolStore.get(itemKey); if (r?.sb3 && r.base === base) { sb3 = r.sb3; restored = true; } }
    else if (!sb3) { const r = await ToolStore.get(docKey); if (r?.sb3) { sb3 = r.sb3; restored = true; } }

    const frame = h("iframe", { cls: "sci-iframe", src: SCRATCH_URL, title: "البرمجة بالكتل" });
    const cap = h("button", { cls: "pri", disabled: true, title: "ضع صورة المنصة على صفحة السبورة، ومعها المشروع (انقر عليها مرتين لاحقًا لفتحه من جديد)", onclick: () => this.capture() },
      edit ? "⤓ حدّث الصورة على السبورة" : "⤓ التقط المنصة إلى السبورة");
    Apps.frame("البرمجة بالكتل (مبنية على Scratch 3)", "🧩", [cap], frame, SCRATCH_CREDIT);
    const ctx = this.ctx = { frame, key: edit ? itemKey : docKey, base, openSig: null, savedSig: null, timer: 0 };
    this.frame = frame; this.edit = edit; this.api = null;
    // الإغلاق (أو تغيير الملف المفتوح، أو قفل البرنامج): لو المشروع اتعدّل ← يتحفظ.
    // مفتوح من عنصر على السبورة ← الصورة والمشروع على السبورة يتحدّثوا لوحدهم؛ غير كده ← حفظ تلقائي للسبورة دي.
    Apps.cleanup = () => {
      clearInterval(ctx.timer);
      const api = ctx.api;
      if (this.frame === frame) { this.frame = null; this.api = null; }
      if (!api || !ctx.openSig) return null;
      const sig = this.sigOf(api);
      if (sig === ctx.openSig || sig === ctx.savedSig) return null;
      if (!edit) return this.autosave(ctx, true);
      return (async () => {
        try {
          const it = await Apps.store(await this.capSrc(api), await api.snapshot(), edit, 480, false);
          if (it) await ToolStore.del(itemKey); else await this.autosave({ ...ctx, key: docKey, base: null }, true);
        } catch (e) { await this.autosave(ctx, true); }
      })();
    };
    Apps.say("جارٍ التحميل…");
    const api = await new Promise((res, rej) => {
      const t0 = Date.now();
      const poll = () => {
        if (this.frame !== frame) return rej(new Error("أُغلقت النافذة"));
        const a = frame.contentWindow?.masarScratch;
        if (a?.vm) return a.ready.then(res);
        if (Date.now() - t0 > 60000) return rej(new Error("تعذّر تحميل البرمجة بالكتل"));
        setTimeout(poll, 150);
      };
      poll();
    });
    if (this.frame !== frame) return;
    this.api = api;
    if (sb3) {
      // الواجهة ممكن تحمّل المشروع الافتراضي متأخر شوية وتكتب فوق مشروعنا: نتأكد ونعيد
      const names = () => api.vm.runtime.targets.map(t => t.getName() + "@" + t.getCostumes().length).join("|");
      try {
        for (let i = 0; i < 4; i++) {
          await api.load(sb3);
          const want = names();
          await new Promise(r => setTimeout(r, 900));
          if (this.frame !== frame) return;
          if (names() === want) break;
        }
      } catch (e) { Apps.say("تعذّر فتح المشروع المحفوظ: " + (e.message || e), "err"); }
    }
    if (this.frame !== frame) return;
    ctx.api = api;
    // نقطة المقارنة: المشروع زي ما اتفتح (لو اتسترجع من الحفظ التلقائي ← لسه متعدّل عن اللي على السبورة)
    ctx.openSig = restored && edit ? "restored" : this.sigOf(api);
    ctx.lastSig = this.sigOf(api);
    ctx.timer = setInterval(() => { if (this.frame === frame) this.autosave(ctx); }, 8000);
    cap.disabled = false;
    Apps.say(restored ? (edit ? "استُعيدت تعديلات لم تُحفظ في السبورة." : "استُعيد آخر عمل على هذه السبورة.")
      : sb3 ? "فُتح المشروع من السبورة." : "اسحب الكتل إلى مساحة العمل ثم اضغط العلم الأخضر.", "ok");
  },

  /** بصمة المشروع الحالي (من غير الملفات: الكائنات والكتل والأماكن والأزياء) */
  sigOf(api) { try { return hash(api.vm.toJSON()); } catch { return null; } },

  /** حفظ تلقائي على الجهاز لو المشروع اتغيّر من آخر حفظ */
  async autosave(ctx, force) {
    const api = ctx.api; if (!api || ctx.busy) return;
    const sig = this.sigOf(api);
    if (!force && sig === ctx.lastSig) return;
    ctx.busy = true;
    try {
      const buf = await api.save();
      await ToolStore.set(ctx.key, ctx.base ? { base: ctx.base, sb3: toB64(buf) } : { sb3: toB64(buf) });
      ctx.lastSig = sig;
    } catch { } finally { ctx.busy = false; }
  },

  async capSrc(api) {
    const buf = await api.save();
    const src = { app: "scratch", title: api.title || "" };
    if (buf && buf.byteLength <= MAX_SB3) src.sb3 = toB64(buf);
    return src;
  },

  /** صورة المنصة + المشروع (لو مش كبير) ← عنصر على السبورة */
  async capture() {
    const api = this.api, ctx = this.ctx, edit = this.edit; if (!api) return;
    try {
      const png = await api.snapshot();
      if (!png) throw new Error("تعذّر التقاط المنصة");
      const src = await this.capSrc(api), sig = this.sigOf(api);
      if (ctx) ctx.savedSig = sig;
      const it = await Apps.insert(src, png, edit, 480);
      if (!it) { if (ctx) ctx.savedSig = null; }
      else if (edit) ToolStore.del("scratch:item:" + edit.id);
      else if (ctx) this.autosave(ctx, true);
      if (it && !src.sb3) this.sciToast?.("المشروع أكبر من أن يُحفظ في السبورة؛ احفظه من قائمة «ملف» على جهازك.");
    } catch (e) { if (ctx) ctx.savedSig = null; Apps.say(e.message || String(e), "err"); }
  },
};
