// «البرمجة بالكتل (مبنية على Scratch 3)» جوه «مسار»: نسخة مجمّدة من scratch-gui 4.1.7 (BSD-3) في فولدر scratch جنب البرنامج،
// بتشتغل من غير نت (الصفحة ممنوعة من أي اتصال خارجي)، بشخصية «مسار» بدل القطة ومن غير شعار Scratch.
// من هنا: فتحها في نافذة كبيرة، و«التقط المنصة إلى السبورة» (صورة المنصة + المشروع نفسه لإعادة فتحه بالنقر المزدوج).
import { Apps } from "./sci-apps.js";

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

  /** فتح النافذة (state.sb3 = مشروع محفوظ في السبورة) */
  async open(state = {}, edit = null) {
    if (!(await this.available())) throw new Error("البرمجة بالكتل غير مثبتة مع هذه النسخة من «مسار». أعد التثبيت من أحدث إصدار.");
    const frame = h("iframe", { cls: "sci-iframe", src: SCRATCH_URL, title: "البرمجة بالكتل" });
    const cap = h("button", { cls: "pri", disabled: true, title: "ضع صورة المنصة على صفحة السبورة، ومعها المشروع (انقر عليها مرتين لاحقًا لفتحه من جديد)", onclick: () => this.capture() },
      edit ? "⤓ حدّث الصورة على السبورة" : "⤓ التقط المنصة إلى السبورة");
    Apps.frame("البرمجة بالكتل (مبنية على Scratch 3)", "🧩", [cap], frame, SCRATCH_CREDIT);
    this.frame = frame; this.edit = edit; this.api = null;
    Apps.cleanup = () => { if (this.frame === frame) { this.frame = null; this.api = null; } };
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
    if (state.sb3) {
      // الواجهة ممكن تحمّل المشروع الافتراضي متأخر شوية وتكتب فوق مشروعنا: نتأكد ونعيد
      const sig = () => api.vm.runtime.targets.map(t => t.getName() + "@" + t.getCostumes().length).join("|");
      try {
        let want = null;
        for (let i = 0; i < 4; i++) {
          await api.load(state.sb3);
          want = sig();
          await new Promise(r => setTimeout(r, 900));
          if (this.frame !== frame) return;
          if (sig() === want) break;
        }
      } catch (e) { Apps.say("تعذّر فتح المشروع المحفوظ: " + (e.message || e), "err"); }
    }
    cap.disabled = false;
    Apps.say(state.sb3 ? "فُتح المشروع من السبورة." : "اسحب الكتل إلى مساحة العمل ثم اضغط العلم الأخضر.", "ok");
  },

  /** صورة المنصة + المشروع (لو مش كبير) ← عنصر على السبورة */
  async capture() {
    const api = this.api; if (!api) return;
    try {
      const png = await api.snapshot();
      if (!png) throw new Error("تعذّر التقاط المنصة");
      const buf = await api.save();
      const src = { app: "scratch", title: api.title || "" };
      if (buf && buf.byteLength <= MAX_SB3) src.sb3 = toB64(buf);
      await Apps.insert(src, png, this.edit, 480);
      if (!src.sb3) this.sciToast?.("المشروع أكبر من أن يُحفظ في السبورة؛ احفظه من قائمة «ملف» على جهازك.");
    } catch (e) { Apps.say(e.message || String(e), "err"); }
  },
};
