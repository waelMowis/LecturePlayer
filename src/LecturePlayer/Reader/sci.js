// =====================================================================
//  الأدوات العلمية للسبورة: لوحة جانبية بأربع مواد (رياضيات/كيمياء/فيزياء/أحياء).
//  كل أداة بتنزّل على الصفحة عنصر «image» عادي (صورة SVG فيكتور) ومعاه sci = مصدره،
//  فبيتحرك ويتكبّر ويتصدّر PDF زي أي صورة، والنقر المزدوج بيرجّعه لمحرره.
// =====================================================================
import { Ink, W, onDark } from "./ink.js";
import { balance, rebuild, parseOnly, isBalanced } from "./sci-chem.js";
import { MATH_T, PHYS_T, CHEM_T, BIO_T, ARABIC_KEYS } from "./sci-data.js";
import { TOOLS, SYMBOLS } from "./sci-shapes.js";
import { LAB, LAB_GROUPS } from "./sci-lab.js";
import { wrap } from "./sci-svg.js";
import { loadLib, search, thumbEl, displayName, drawPic, needsCredit, licName, phyloSearch } from "./sci-lib.js";
import { CAT_AR, TAB_CATS } from "./sci-lib-ar.js";
import { Apps } from "./sci-apps.js";
import { Ardu } from "./sci-arduino.js";
import { PHET, SITES, phetUrl, CREDIT, captionedShot, pubchemCompound, pubchemSvg, commonsSearch, commonsSvg } from "./sci-web.js";

const h = (tag, attrs = {}, ...kids) => {
  const e = document.createElement(tag);
  for (const k in attrs) {
    const v = attrs[k];
    if (v == null || v === false) continue;
    if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (k === "cls") e.className = v;
    else if (k === "text") e.textContent = v;
    else if (k === "style" && typeof v === "object") Object.assign(e.style, v);
    else e.setAttribute(k, v === true ? "" : v);
  }
  for (const c of kids.flat(Infinity)) if (c != null && c !== false) e.append(c.nodeType ? c : document.createTextNode(String(c)));
  return e;
};
const LIB = "lib/sci/";
const SIZES = { s: [24, "صغير"], m: [34, "متوسط"], l: [48, "كبير"], xl: [68, "كبير جدًا"] };
/** مقاس الأشكال على الصفحة (مضروب في مقاس الرسم الأصلي). */
const SHAPE_SC = { s: [0.5, "صغير"], m: [0.75, "متوسط"], l: [1.05, "كبير"], xl: [1.45, "كبير جدًا"] };
const SWATCH = ["#111111", "#1971c2", "#e03131", "#2f9e44", "#f08c00", "#7048e8"];
const AR_DIG = "٠١٢٣٤٥٦٧٨٩";
const pref = (k, d) => { try { const v = localStorage.getItem("masar.sci." + k); return v == null ? d : JSON.parse(v); } catch (e) { return d; } };
const setPref = (k, v) => { try { localStorage.setItem("masar.sci." + k, JSON.stringify(v)); } catch (e) { } };

// ---------------------------------------------------------------- MathJax (رسم المعادلة SVG) و MathLive (المحرر)
let mj = null;
export function loadMathJax() {
  if (mj) return mj;
  window.MathJax = {
    loader: { load: [] },
    tex: { packages: { "[+]": ["mhchem", "ams", "color", "boldsymbol"] } },
    svg: { fontCache: "none", mtextInheritFont: true },
    startup: { typeset: false }
  };
  mj = new Promise((res, rej) => {
    const s = document.createElement("script");
    s.src = LIB + "mathjax/tex-svg-full.js";
    s.onload = () => window.MathJax.startup.promise.then(res, rej);
    s.onerror = () => { mj = null; rej(new Error("تعذّر تحميل محرك المعادلات")); };
    document.head.appendChild(s);
  });
  return mj;
}
let ml = null;
async function loadMathLive() {
  if (ml) return ml;
  ml = import("./" + LIB + "mathlive/mathlive.esm.js").then(m => {
    m.MathfieldElement.fontsDirectory = new URL(LIB + "mathlive/fonts/", location.href).href;
    m.MathfieldElement.soundsDirectory = null;
    const kb = window.mathVirtualKeyboard;
    if (kb) kb.layouts = ["numeric", "symbols", "alphabetic", "greek", {
      label: "عربي", tooltip: "رموز عربية: س ص ع، جا جتا ظا، لو",
      rows: ARABIC_KEYS.map(r => r.map(k => ({ latex: k[1], label: k[0] })))
    }];
    return m;
  }).catch(e => { ml = null; throw e; });
  return ml;
}

/** أرقام هندية (١٢٣) جوه المعادلة: بنغيّر الأرقام بس، مش أسماء الأوامر. */
export function arabicDigits(tex) { return String(tex).replace(/(\\[a-zA-Z]+)|[0-9]/g, (m, cmd) => cmd || AR_DIG[+m]); }

/** المصدر ← نص TeX اللي بيترسم. */
export function texOf(sci) {
  let t;
  if (sci.k === "chem") {
    let ce = sci.ce || "";
    if (sci.hide) { const p = parseOnly(ce); if (p) ce = rebuild(p, null, true); }
    t = "\\ce{" + ce + "}";
  } else t = sci.tex || "";
  return sci.ar || sci.rtl ? arabicDigits(t) : t;
}

/** رسم: TeX ← {url, w, h} (صورة SVG مستقلة بلونها، والمقاس بوحدات الصفحة). */
export async function renderTex(tex, color, fs, rtl) {
  await loadMathJax();
  // العربي: كلمة كاملة في نص واحد (عشان الحروف تتشبك وتتقري صح)، والدوال العربية (جا، جتا) كمان
  tex = String(tex).replace(/\\operatorname\{([^{}]*[\u0600-\u06FF][^{}]*)\}/g, "\\mathop{\\text{$1}}\\nolimits");
  const node = window.MathJax.tex2svg(tex, { display: true });
  const svg = node.querySelector("svg");
  if (!svg) throw new Error("تعذّر رسم المعادلة");
  const err = svg.querySelector("[data-mml-node='merror']");
  if (err) throw new Error("خطأ في كتابة المعادلة: " + (err.getAttribute("title") || err.textContent || "").trim());
  const ex = fs * 0.5;
  const w = parseFloat(svg.getAttribute("width")) * ex, hgt = parseFloat(svg.getAttribute("height")) * ex;
  if (!(w > 0 && hgt > 0)) throw new Error("المعادلة فارغة");
  svg.setAttribute("xmlns", "http://www.w3.org/2000/svg");
  svg.setAttribute("width", (w * 2).toFixed(1)); svg.setAttribute("height", (hgt * 2).toFixed(1));
  svg.removeAttribute("style"); svg.removeAttribute("role"); svg.removeAttribute("focusable");
  svg.setAttribute("color", color); svg.setAttribute("style", "color:" + color);
  // النص العربي: MathJax بيرسم كل حرف لوحده من الشمال لليمين (فالكلمة بتطلع مقلوبة ومش متشبكة)
  // ← بنجمع الحروف المتتالية في نص واحد مكان أول حرف، والمتصفح بيشبكها ويقلبها صح
  for (const g of new Set([...svg.querySelectorAll("text")].map(t => t.parentNode))) {
    let run = [];
    const flush = () => {
      if (run.length > 1 && run.some(t => /[\u0600-\u06FF]/.test(t.textContent))) {
        run[0].textContent = run.map(t => t.textContent).join("");
        for (const t of run.slice(1)) t.remove();
      }
      run = [];
    };
    for (const c of [...g.childNodes]) { if (c.nodeName === "text") run.push(c); else flush(); }
    flush();
  }
  // (خط الكلام يفضل زي ما MathJax قاسه، عشان العرض اللي حجزه يكفيه)
  layoutText(svg, rtl);
  const str = new XMLSerializer().serializeToString(svg);
  const url = "data:image/svg+xml;base64," + btoa(unescape(encodeURIComponent(str)));
  return { url, w: Math.round(w * 10) / 10, h: Math.round(hgt * 10) / 10 };
}

/**
 * قياس النص بعد الرسم (لازم يبقى جوه الصفحة عشان getBBox):
 * - الكلام العربي بيترسم من مكانه ناحية الشمال (اتجاهه RTL) فبيدخل على اللي قبله ← بنزحزحه يبدأ من مكانه.
 * - ترميز عربي (rtl) زي الكتب المدرسية: بنقلب المعادلة كلها مراية (الأس يروح شمال، والجذر والمجموع
 *   والتكامل يتقلبوا زي الرموز العربية)، وبعدين نرجّع الكلام والحروف والأرقام نفسها معدولة مكانها.
 */
function layoutText(svg, rtl) {
  const texts = [...svg.querySelectorAll("text")];
  svg.setAttribute("direction", "ltr");
  if (!texts.length && !rtl) return;
  const vb = (svg.getAttribute("viewBox") || "0 0 0 0").split(/\s+/).map(Number);
  const host = document.createElement("div");
  // الصورة النهائية بتترسم LTR (الصفحة نفسها RTL) — فبنقيس في نفس الاتجاه
  host.style.cssText = "position:absolute;left:-10000px;top:0;visibility:hidden;direction:ltr";
  svg.setAttribute("direction", "ltr");
  host.appendChild(svg); document.body.appendChild(host);
  const add = (el, t) => el.setAttribute("transform", ((el.getAttribute("transform") || "") + " " + t).trim());
  const flipAt = (el, cx) => add(el, `translate(${cx},0) scale(-1,1) translate(${-cx},0)`);
  try {
    for (const t of texts) {
      const b = t.getBBox();
      // الترتيب مهم: آخر تحويل في القائمة بيتطبق الأول (القلب حوالين نص الكلام في مكانه الأصلي، وبعده الزحزحة)
      if (Math.abs(b.x) > 0.5) add(t, `translate(${-b.x},0)`);
      if (rtl && b.width) flipAt(t, b.x + b.width / 2);
    }
    if (!rtl) return;
    for (const p of svg.querySelectorAll("path[data-c]")) {
      const ch = String.fromCodePoint(parseInt(p.getAttribute("data-c"), 16));
      if (!/[\p{L}\p{N}]/u.test(ch)) continue;
      const b = p.getBBox(); if (b.width) flipAt(p, b.x + b.width / 2);
    }
    const root = svg.querySelector("g[data-mml-node='math']");
    if (root) flipAt(root, vb[0] + vb[2] / 2);
  } finally { host.remove(); }
}

// ---------------------------------------------------------------- اللوحة
export const Sci = {
  board: null, panel: null, dlg: null, tab: pref("tab", "math"),

  init(board) {
    this.board = board;
    Apps.init(this);
    Ardu.init(this);
    Ink.onSciEdit = (s, it) => this.editItem(s, it);
    Ink.onSciRecolor = (key, ids, c) => this.recolor(key, ids, c);
  },
  /** لون جديد من شريط التحديد ← نرسم العناصر العلمية تاني بنفس مكانها ومقاسها. */
  async recolor(key, ids, c) {
    for (const id of ids) {
      const it = Ink.doc.items(key).find(i => i.id === id);
      if (!it?.sci || !this.canRender(it.sci) || it.sci.k === "pic" || it.sci.k === "app") continue;
      try { await this.place({ ...it.sci, c }, { key, id }); } catch (e) { }
    }
  },
  canRender(sci) { return sci.k === "eq" || sci.k === "chem" || sci.k === "pic" || sci.k === "app" || (sci.k === "shape" && !!TOOLS[sci.type]); },
  /** القيم الافتراضية لحقول أداة. */
  defaults(type) { const o = {}; for (const f of TOOLS[type]?.f || []) o[f[0]] = f[3]; return o; },
  /** يرسم أي مصدر علمي ← {url, w, h} بوحدات الصفحة. */
  async render(src, color) {
    if (src.k === "app") {
      if (!src.__img) throw new Error("افتح الأداة لتحديث هذا العنصر");
      const sc = src.sc || 1;
      return { url: src.__img, w: src.__w * sc, h: src.__h * sc };
    }
    if (src.k !== "shape" && src.k !== "pic") return renderTex(texOf(src), color, src.fs || 34, !!src.rtl);
    let r;
    if (src.k === "pic") r = await drawPic(src);
    else {
      const tool = TOOLS[src.type];
      if (!tool) throw new Error("أداة غير معروفة");
      r = await tool.draw({ ...this.defaults(src.type), ...(src.p || {}) }, color);
    }
    const out = wrap(r.inner, r.w, r.h, color), sc = src.sc || SHAPE_SC.m[0];
    return { url: out.url, w: out.w * sc, h: out.h * sc };
  },
  /** صورة مصغرة للوحة (للأدوات اللي رسمها فوري). */
  thumb(type, p) {
    try {
      const r = TOOLS[type].draw({ ...this.defaults(type), ...p }, "#222");
      if (r && r.then) return h("span", { cls: "th" });
      return h("img", { src: wrap(r.inner, r.w, r.h, "#222").url, alt: "", loading: "lazy" });
    } catch (e) { return h("span", { cls: "th" }); }
  },
  /** أدوات المادة (بتفتح محرر الحقول). */
  toolTiles(tab) { return Object.entries(TOOLS).filter(([, t]) => t.tab === tab && !t.hidden).map(([id, t]) => this.tile(t.icon, t.name, t.sub, () => this.shapeEditor({ k: "shape", type: id }), { "data-tool": id })); },
  /** شبكة صور مصغرة بتدرج بضغطة (رموز الدوائر وأدوات المعمل). */
  grid(items) {
    return h("div", { cls: "sci-grid" }, items.map(([type, p, name]) => h("button", {
      title: name + " — انقر للإدراج، وانقر نقرًا مزدوجًا عليها في الصفحة لتعديلها", "data-shape": type + ":" + (p.s || p.type || ""),
      onclick: async () => { try { await this.place({ k: "shape", type, p: { ...this.defaults(type), ...p }, sc: SHAPE_SC[pref("ssize", "m")]?.[0] }); } catch (e) { this.board?.toast?.(e.message); } }
    }, this.thumb(type, p), h("small", {}, name))));
  },
  isOn() { return !!(this.panel && !this.panel.hidden); },
  toggle() { if (this.isOn()) this.close(); else this.open(); },
  close() { Apps.close(); if (this.panel) this.panel.hidden = true; this.board?.onSciPanel?.(false); },
  apps: Apps,
  ardu: Ardu,
  appTiles(list) {
    return this.section("أدوات تفاعلية (نافذة كبيرة، والنتيجة تنزل على الصفحة)", list.map(([id, icon, name, sub]) => this.tile(icon, name, sub, () => { const r = Apps[id](); if (r?.catch) r.catch(e => this.toast(e.message)); }, { "data-app": id })));
  },
  open(tab) {
    if (tab) { this.tab = tab; setPref("tab", tab); }
    if (!this.panel) {
      this.panel = h("div", { cls: "sci-panel", role: "dialog", "aria-label": "الأدوات العلمية" });
      document.body.appendChild(this.panel);
    }
    this.panel.hidden = false;
    this.renderPanel();
    this.board?.onSciPanel?.(true);
    loadMathJax().catch(() => { });
  },
  renderPanel() {
    const P = this.panel; P.textContent = "";
    const TABS = [["math", "رياضيات", "∑"], ["chem", "كيمياء", "⚗"], ["phys", "فيزياء", "⚛"], ["bio", "أحياء", "🧬"]];
    P.append(
      h("div", { cls: "sci-head" }, h("b", {}, "🧪 أدوات علمية"), h("button", { cls: "sci-x", title: "إغلاق", onclick: () => this.close() }, "✕")),
      h("div", { cls: "sci-tabs", role: "tablist" }, TABS.map(([id, label, ic]) => h("button", {
        role: "tab", "aria-selected": String(this.tab === id), cls: this.tab === id ? "on" : "", "data-tab": id,
        onclick: () => { this.tab = id; setPref("tab", id); this.renderPanel(); }
      }, ic + " " + label))),
      h("div", { cls: "sci-body" }, this.tabBody(this.tab))
    );
  },
  tile(icon, label, sub, fn, extra = {}) {
    return h("button", { cls: "sci-tile", onclick: fn, ...extra }, h("span", { cls: "ic" }, icon), h("span", { cls: "tx" }, h("b", {}, label), sub ? h("small", {}, sub) : null));
  },
  section(title, ...kids) { return h("div", { cls: "sci-sec" }, h("div", { cls: "sci-sec-t" }, title), ...kids); },
  templates(list, kind) {
    return h("div", { cls: "sci-tpl" }, list.map(([name, src]) => h("button", {
      title: "افتح هذه الصيغة في المحرر لتعديلها ثم أدرجها",
      onclick: () => this.editor(kind === "chem" ? { k: "chem", ce: src } : { k: "eq", tex: src })
    }, name)));
  },
  tabBody(tab) {
    if (tab === "math") return [
      this.libSection("math"),
      this.onlineSection("math"),
      this.section("المعادلات", this.tile("✎", "معادلة جديدة", "كسور، جذور، أسس، مصفوفات، تكامل — ولوحة رموز عربية", () => this.editor({ k: "eq", tex: "" }))),
      this.section("الرسوم والأشكال", ...this.toolTiles("math")),
      this.section("صيغ جاهزة", this.templates(MATH_T, "eq"))
    ];
    if (tab === "chem") return [
      this.section("المعادلات الكيميائية",
        this.tile("⚗", "معادلة كيميائية", "اكتب 2H2 + O2 -> 2H2O فتظهر بالأرقام السفلية والأسهم", () => this.editor({ k: "chem", ce: "" })),
        this.tile("⚖", "وازن معادلة", "اكتبها بلا معاملات واضغط «وازن»، أو أخفِ المعاملات للتمرين", () => this.editor({ k: "chem", ce: "", balance: 1 }))),
      this.appTiles([["ptable", "▦", "الجدول الدوري", "118 عنصرًا بالعربية: الكتلة والتوزيع والسالبية والحالة، وبطاقة العنصر أو الجدول كاملًا على الصفحة"],
        ["ketcher", "⌬", "محرر الجزيئات", "ارسم الصيغ البنائية والحلقات والروابط (Ketcher) — يُنزَّل أول مرة"],
        ["mol3d", "🧬", "الجزيئات ثلاثية الأبعاد", "52 جزيئًا جاهزًا، وبحث في PubChem، بنماذج الكرات والعصي والنموذج الفراغي"]]),
      this.section("نماذج الذرة", ...this.toolTiles("chem")),
      ...LAB_GROUPS.map(([g, title]) => this.section("أدوات المعمل — " + title, this.grid(LAB.filter(x => x[2] === g).map(([id, name]) => ["lab", { type: id }, name])))),
      this.onlineSection("chem"),
      this.libSection("chem"),
      this.section("تفاعلات جاهزة", this.templates(CHEM_T, "chem"))
    ];
    if (tab === "phys") return [
      this.section("القوانين والمعادلات", this.tile("✎", "قانون أو معادلة", "متجهات، دلتا، وحدات، كسور", () => this.editor({ k: "eq", tex: "" }))),
      this.appTiles([["optics", "🔦", "محاكي البصريات", "مرايا وعدسات ومناشير وأشعة تتحرك مباشرة (Ray Optics) — يُنزَّل أول مرة"],
        ["motion", "⚽", "مشهد الحركة", "كرات وصناديق ومستوى مائل وبندول ونابض وتصادم بالجاذبية، ومتجهات السرعة"]]),
      this.section("الرسوم الفيزيائية", ...this.toolTiles("phys")),
      this.section("رموز الدوائر الكهربائية (وصّلها بأداة الخط)", this.grid(SYMBOLS.map(([id, name]) => ["symbol", { s: id }, name]))),
      this.onlineSection("phys"),
      this.libSection("phys"),
      this.section("قوانين جاهزة", this.templates(PHYS_T, "eq"))
    ];
    return [
      this.libSection("bio"),
      this.appTiles([["mol3d", "🧬", "الجزيئات ثلاثية الأبعاد", "الجلوكوز والأحماض الأمينية وقواعد DNA، وبحث في PubChem"]]),
      this.onlineSection("bio"),
      this.section("المعادلات الحيوية", this.tile("⚗", "معادلة حيوية", "البناء الضوئي، التنفس، الإنزيمات", () => this.editor({ k: "chem", ce: "" }))),
      this.section("صيغ جاهزة", h("div", { cls: "sci-tpl" }, BIO_T.map(([name, src, k]) => h("button", {
        onclick: () => this.editor(k === "eq" ? { k: "eq", tex: src } : { k: "chem", ce: src })
      }, name))))
    ];
  },

  // ---------------------------------------------------------------- المحرر
  /** يفتح المحرر: sci = مصدر عنصر (جديد أو موجود)، edit = {s, id} لو تعديل. */
  async editor(sci, edit) {
    this.closeEditor();
    const st = {
      k: sci.k === "chem" ? "chem" : "eq", tex: sci.tex || "", ce: sci.ce || "", hide: !!sci.hide, ar: !!(sci.ar ?? pref("ar", false)), rtl: !!(sci.rtl ?? pref("rtl", false)),
      c: sci.c || null, fs: sci.fs || SIZES[pref("size", "m")]?.[0] || 34
    };
    document.body.classList.add("sci-on");
    const prev = h("div", { cls: "sci-prev", "aria-live": "polite" }), msg = h("div", { cls: "sci-msg" });
    const box = h("div", { cls: "sci-dlg", role: "dialog", "aria-modal": "true", "aria-label": edit ? "تعديل المعادلة" : "معادلة جديدة" });
    const wrap = h("div", { cls: "sci-cover", onpointerdown: e => { if (e.target === wrap) this.closeEditor(); } }, box);
    this.dlg = wrap;
    document.body.appendChild(wrap);
    box.addEventListener("keydown", e => {
      if (e.key === "Escape") { e.preventDefault(); this.closeEditor(); }
      else if (e.key === "Enter" && (e.ctrlKey || st.k === "chem") && !e.shiftKey) { e.preventDefault(); done(); }
    });

    let field = null, ce = null, latex = null;
    const color = () => st.c || Ink.currentColor() || "#111111";
    let timer = 0;
    const refresh = () => {
      clearTimeout(timer);
      timer = setTimeout(async () => {
        const tex = texOf(st);
        if (!tex.replace(/\\ce\{\s*\}/, "").trim()) { prev.textContent = ""; prev.append(h("span", { cls: "ph" }, "المعاينة تظهر هنا")); return; }
        try {
          const r = await renderTex(tex, color(), 34, st.rtl);
          prev.textContent = "";
          prev.append(h("img", { src: r.url, alt: "معاينة", style: { maxWidth: "100%", maxHeight: "160px" } }));
          if (st.k === "chem" && !st.hide && /->|<=>|=/.test(st.ce)) {
            let ok = false; try { ok = isBalanced(st.ce); } catch (e) { }
            say(ok ? "✓ المعادلة موزونة" : "المعادلة غير موزونة — اضغط «وازن»", ok ? "ok" : "warn");
          } else if (!msg.dataset.keep) say("");
        } catch (e) { say(e.message, "err"); }
      }, 160);
    };
    const say = (t, kind) => { msg.textContent = t; msg.className = "sci-msg " + (kind || ""); };

    const modeBtns = h("div", { cls: "sci-modes" },
      [["eq", "∑ رياضيات وفيزياء"], ["chem", "⚗ كيمياء"]].map(([k, l]) => h("button", { cls: st.k === k ? "on" : "", onclick: () => { if (st.k !== k) { st.k = k; build(); } } }, l)));
    const area = h("div", { cls: "sci-area" });

    const build = async () => {
      area.textContent = ""; say("");
      for (const b of modeBtns.children) b.classList.toggle("on", b.textContent.includes(st.k === "chem" ? "كيمياء" : "رياضيات"));
      if (st.k === "chem") {
        ce = h("input", { cls: "sci-ce", dir: "ltr", spellcheck: "false", value: st.ce, placeholder: "مثال: Fe + O2 -> Fe2O3", "aria-label": "المعادلة الكيميائية" });
        ce.oninput = () => { st.ce = ce.value; msg.dataset.keep = ""; refresh(); };
        const ins = t => { const a = ce.selectionStart ?? ce.value.length, b = ce.selectionEnd ?? a; ce.value = ce.value.slice(0, a) + t + ce.value.slice(b); ce.selectionStart = ce.selectionEnd = a + t.length; ce.focus(); ce.oninput(); };
        const KEYS = [["→", " -> ", "سهم التفاعل"], ["⇌", " <=> ", "اتزان"], ["→Δ", " ->[\\Delta] ", "بالتسخين"], ["→عامل", " ->[\\text{عامل حفاز}] ", "سهم عليه كتابة (غيّرها)"], ["+", " + ", "إضافة مادة"],
          ["↑", " ^", "غاز متصاعد"], ["↓", " v", "راسب"], ["(s)", "(s)", "صلب"], ["(l)", "(l)", "سائل"], ["(g)", "(g)", "غاز"], ["(aq)", "(aq)", "محلول مائي"],
          ["⁺", "^+", "شحنة موجبة"], ["⁻", "^-", "شحنة سالبة"], ["²⁺", "^2+", ""], ["²⁻", "^2-", ""], ["e⁻", "e-", "إلكترون"], ["·", "*", "ماء التبلور CuSO4*5H2O"]];
        const bal = h("button", { cls: "sci-bal", onclick: () => {
          try {
            const r = balance(st.ce);
            st.ce = rebuild(r, r.coefs, false); ce.value = st.ce;
            msg.dataset.keep = "1"; say("✓ وُزنت: " + r.coefs.join(" : "), "ok"); refresh();
          } catch (e) { say(e.message, "err"); }
        } }, "⚖ وازن");
        const hide = h("label", { cls: "sci-chk" }, h("input", { type: "checkbox", checked: st.hide || null, onchange: e => { st.hide = e.target.checked; refresh(); } }), " أخفِ المعاملات (تمرين للطلاب)");
        area.append(ce, h("div", { cls: "sci-keys" }, KEYS.map(([l, t, tip]) => h("button", { title: tip || t.trim(), onclick: () => ins(t) }, l)), bal), hide);
        setTimeout(() => ce.focus(), 0);
        if (sci.balance && st.ce) bal.click();
      } else {
        let M = null;
        try { M = await loadMathLive(); } catch (e) { }
        if (!this.dlg) return;
        latex = h("textarea", { cls: "sci-latex", dir: "ltr", spellcheck: "false", rows: 2, "aria-label": "نص LaTeX", hidden: true });
        latex.value = st.tex;
        if (M) {
          field = new M.MathfieldElement();
          field.className = "sci-mf"; field.setAttribute("dir", "ltr");
          field.mathVirtualKeyboardPolicy = "manual";
          field.smartFence = true;
          field.value = st.tex;
          field.addEventListener("input", () => { st.tex = field.value; latex.value = st.tex; refresh(); });
          latex.oninput = () => { st.tex = latex.value; field.value = st.tex; refresh(); };
        } else {
          latex.hidden = false;
          latex.oninput = () => { st.tex = latex.value; refresh(); };
        }
        const cmd = (l, t, tip) => h("button", { title: tip, onclick: () => { if (field) { field.executeCommand(["insert", t, { focus: true, format: "latex", selectionMode: "placeholder" }]); } else { latex.value += t; latex.oninput(); } } }, l);
        const QUICK = [["a/b", "\\frac{#@}{#?}", "كسر"], ["√", "\\sqrt{#0}", "جذر تربيعي"], ["ⁿ√", "\\sqrt[#?]{#0}", "جذر نوني"], ["x²", "#@^{2}", "تربيع"], ["xⁿ", "#@^{#?}", "أس"], ["x₁", "#@_{#?}", "دليل سفلي"],
          ["±", "\\pm", ""], ["×", "\\times", ""], ["÷", "\\div", ""], ["≤", "\\le", ""], ["≥", "\\ge", ""], ["≠", "\\ne", ""], ["≈", "\\approx", ""], ["∞", "\\infty", ""],
          ["π", "\\pi", ""], ["θ", "\\theta", ""], ["Δ", "\\Delta", ""], ["°", "^{\\circ}", "درجة"], ["→v", "\\vec{#0}", "متجه"], ["|x|", "\\left|#0\\right|", "القيمة المطلقة"],
          ["∑", "\\sum_{#?}^{#?}", "مجموع"], ["∫", "\\int_{#?}^{#?}", "تكامل"], ["lim", "\\lim_{#?\\to#?}", "نهاية"], ["dy/dx", "\\frac{d#?}{d#?}", "مشتقة"], ["( )", "\\left(#0\\right)", "أقواس"],
          ["[2×2]", "\\begin{pmatrix}#?&#?\\\\#?&#?\\end{pmatrix}", "مصفوفة"], ["{ نظام", "\\begin{cases}#?\\\\#?\\end{cases}", "نظام معادلات"]];
        area.append(field || h("div"), h("div", { cls: "sci-keys" }, QUICK.map(([l, t, tip]) => cmd(l, t, tip || t)),
          h("button", { cls: "sci-kb", title: "لوحة الرموز الكاملة (فيها لوحة «عربي»: س ص ع، جا جتا ظا)", onclick: () => { if (field) { field.focus(); window.mathVirtualKeyboard?.show(); } } }, "⌨ لوحة الرموز"),
          h("button", { title: "تحرير نص LaTeX مباشرة", onclick: () => { latex.hidden = !latex.hidden; } }, "LaTeX")), latex);
        if (field) setTimeout(() => field.focus(), 0);
      }
      refresh();
    };

    const sizeSel = h("select", { "aria-label": "حجم المعادلة", onchange: e => { st.fs = +e.target.value; setPref("size", Object.keys(SIZES).find(k => SIZES[k][0] === st.fs) || "m"); } },
      Object.values(SIZES).map(([v, l]) => h("option", { value: v, selected: v === st.fs || null }, l)));
    const swatches = h("div", { cls: "sci-sw" }, SWATCH.map(c => h("button", {
      cls: "sw" + (st.c === c ? " on" : ""), title: "اللون", style: { background: c },
      onclick: e => { st.c = c; for (const b of swatches.children) b.classList.toggle("on", b === e.currentTarget); refresh(); }
    })));
    const arChk = h("label", { cls: "sci-chk" }, h("input", { type: "checkbox", checked: st.ar || null, onchange: e => { st.ar = e.target.checked; setPref("ar", st.ar); refresh(); } }), " أرقام عربية (١٢٣)");
    const rtlChk = h("label", { cls: "sci-chk", title: "الترميز العربي كما في الكتب المدرسية: المعادلة تُقرأ من اليمين، والأس على اليسار، والجذر والمجموع معكوسان — استخدم الحروف العربية من لوحة «عربي»" },
      h("input", { type: "checkbox", checked: st.rtl || null, onchange: e => { st.rtl = e.target.checked; setPref("rtl", st.rtl); refresh(); } }), " من اليمين (ترميز عربي)");

    const done = async () => {
      const src = st.k === "chem" ? { k: "chem", ce: st.ce.trim(), hide: st.hide ? 1 : 0 } : { k: "eq", tex: st.tex.trim() };
      if (!(src.ce || src.tex)) { say("اكتب المعادلة أولًا", "err"); return; }
      src.ar = st.ar ? 1 : 0; src.fs = st.fs; if (st.c) src.c = st.c;
      if (st.k === "eq" && st.rtl) src.rtl = 1;
      try {
        const it = await this.place(src, edit);
        if (it) this.closeEditor();
      } catch (e) { say(e.message, "err"); }
    };
    box.append(
      h("div", { cls: "sci-dlg-h" }, h("b", {}, edit ? "تعديل المعادلة" : "معادلة جديدة"), h("button", { cls: "sci-x", title: "إغلاق (Esc)", onclick: () => this.closeEditor() }, "✕")),
      modeBtns, area,
      h("div", { cls: "sci-opts" }, h("span", {}, "اللون"), swatches, h("span", {}, "الحجم"), sizeSel, arChk, st.k === "eq" ? rtlChk : null),
      prev, msg,
      h("div", { cls: "sci-foot" },
        h("button", { cls: "pri", onclick: done }, edit ? "احفظ التعديل" : "أدرج على الصفحة"),
        h("button", { onclick: () => this.closeEditor() }, "إلغاء"),
        h("small", {}, st.k === "chem" ? "Enter للإدراج" : "Ctrl+Enter للإدراج"))
    );
    build();
  },
  // ---------------------------------------------------------------- مكتبة الرسوم
  /** قسم المكتبة في كل مادة: بحث بالعربي والإنجليزي، وفئات المادة، ونتائج بصور مصغرة. */
  libSection(tab) {
    const cats = TAB_CATS[tab] || [];
    const st = this.libState ||= {};
    const s = st[tab] ||= { q: "", cat: tab === "bio" ? cats[0] : "", n: 30 };
    const res = h("div", { cls: "sci-lib-res" }), more = h("div", { cls: "sci-lib-more" });
    const input = h("input", { type: "search", cls: "sci-lib-q", placeholder: "ابحث: قلب، خلية، بكتيريا، دورق، heart…", value: s.q, "aria-label": "ابحث في مكتبة الرسوم" });
    let timer = 0;
    const run = async () => {
      res.textContent = ""; more.textContent = "";
      let r;
      try { r = await search(s.q, s.q ? "" : s.cat || (tab === "bio" ? "" : cats[0])); }
      catch (e) { res.append(h("div", { cls: "sci-lib-msg" }, e.message)); return; }
      if (!r.items.length) {
        res.append(h("div", { cls: "sci-lib-msg" }, r.unknown?.length ? `لا أعرف كلمة «${r.unknown.join("، ")}» بعد — جرّب مرادفًا أو اكتبها بالإنجليزية` : "لا توجد نتائج"));
      }
      const show = r.items.slice(0, s.n);
      res.append(...show.map(it => h("button", {
        cls: "sci-lib-it", "data-lib": it.i, title: `${it.name} — ${it.author} (${licName(it.lic)})${needsCredit(it.lic) ? " — يُكتب المصدر تحت الرسم" : ""}`,
        onclick: () => this.placePic({ k: "pic", src: it.src, path: it.path, name: it.name, lic: it.lic, author: it.author }, it.ar)
      }, thumbEl(it, 84), h("small", {}, displayName(it)))));
      if (r.items.length > s.n) more.append(h("button", { onclick: () => { s.n += 30; run(); } }, `المزيد (${r.items.length - s.n})`));
      if (tab === "bio" && s.q) more.append(h("button", { cls: "sci-lib-phylo", title: "صور ظلية لآلاف الكائنات من PhyloPic (الملكية العامة فقط) — تحتاج اتصالًا بالإنترنت", onclick: () => this.phylo(s.q, res, more) }, "🦋 ابحث عن الكائن في PhyloPic"));
    };
    input.oninput = () => { s.q = input.value.trim(); s.n = 30; clearTimeout(timer); timer = setTimeout(run, 220); };
    const chips = h("div", { cls: "sci-lib-cats" }, [...(tab === "bio" ? [["", "الكل"]] : []), ...cats.map(c => [c, CAT_AR[c] || c])].map(([c, l]) => h("button", {
      cls: s.cat === c ? "on" : "", "data-cat": c,
      onclick: e => { s.cat = c; s.q = ""; input.value = ""; s.n = 30; for (const b of chips.children) b.classList.toggle("on", b === e.currentTarget); run(); }
    }, l)));
    run();
    return this.section(tab === "bio" ? "مكتبة الرسوم العلمية" : "رسوم من المكتبة", input, chips, res, more);
  },
  async phylo(q, res, more) {
    res.textContent = ""; more.textContent = "";
    res.append(h("div", { cls: "sci-lib-msg" }, "جارٍ البحث في PhyloPic…"));
    try {
      const list = await phyloSearch(q);
      res.textContent = "";
      if (!list.length) { res.append(h("div", { cls: "sci-lib-msg" }, "لا توجد صور بالملكية العامة لهذا الكائن")); return; }
      res.append(...list.slice(0, 30).map(it => h("button", {
        cls: "sci-lib-it", title: `${it.name} — ${it.author} (ملكية عامة)`, onclick: () => this.placePic({ k: "pic", ...it, thumb: undefined }, 0)
      }, it.thumb ? h("img", { src: it.thumb, alt: "", loading: "lazy" }) : h("span", { cls: "sci-th" }), h("small", {}, it.name))));
    } catch (e) { res.textContent = ""; res.append(h("div", { cls: "sci-lib-msg" }, "تعذّر البحث في PhyloPic: " + e.message)); }
  },
  /** إدراج رسم من المكتبة (بيتنزل أول مرة). */
  async placePic(src, ar) {
    src.sc = SHAPE_SC[pref("ssize", "m")]?.[0] || 0.75;
    document.body.classList.add("sci-busy");
    try { return await this.place(src); }
    catch (e) { this.toast(e.message); }
    finally { document.body.classList.remove("sci-busy"); }
  },
  toast(msg) {
    const t = h("div", { cls: "sci-toast", role: "status" }, msg);
    document.body.appendChild(t);
    setTimeout(() => t.remove(), 5000);
  },
  /** أسهم تسمية فارغة حوالين الرسم (تمرين «سمِّ الأجزاء»): كل سهم وخانته عناصر عادية تتحرك وتتعدل. */
  addLabels(key, id, n) {
    const it = Ink.doc.items(key).find(i => i.id === id);
    if (!it || n < 1) return [];
    const T = Ink.prefs.text || {}, size = 20, made = [];
    const rows = Math.ceil(n / 2), dig = k => String(k).replace(/\d/g, d => AR_DIG[d]);
    const leftOk = it.x > 190, rightOk = it.x + it.w < W - 190;
    for (let i = 0; i < n; i++) {
      let right = i % 2 === 1;
      if (right && !rightOk) right = false; else if (!right && !leftOk) right = true;
      const row = Math.floor(i / 2), y = it.y + it.h * (row + 0.5) / rows;
      const tx = right ? it.x + it.w + 24 : it.x - 174;
      made.push(Ink.add(key, { t: "text", x: tx, y: y - size * 0.75, w: 150, h: size * 1.5, text: dig(i + 1) + " ـــــــــــــ", font: T.font || "Segoe UI", size, c: "#111111", bg: null, align: right ? "end" : "start", dir: "rtl" }));
      const px = it.x + it.w * (right ? 0.68 : 0.32), py = it.y + it.h * (row + 0.5) / rows + (i % 3 - 1) * it.h * 0.06;
      made.push(Ink.add(key, { t: "shape", k: "arrow", x1: right ? tx - 6 : tx + 156, y1: y, x2: px, y2: py, c: "#e03131", sw: 2.5, fill: null, ly: "p" }));
    }
    Ink.setTool("select");
    Ink.setSel({ key, ids: new Set([id, ...made.map(m => m.id)]) });
    return made;
  },
  /** محرر رسم المكتبة: عنوان تحته، الحجم، وأسهم تسمية فارغة. */
  picEditor(sci, edit) {
    this.closeEditor();
    const st = { cap: sci.cap || "", sc: sci.sc || 0.75, credit: needsCredit(sci.lic) ? 1 : (sci.credit ? 1 : 0), labels: 4 };
    document.body.classList.add("sci-on");
    const prev = h("div", { cls: "sci-prev big" }), msg = h("div", { cls: "sci-msg" });
    const box = h("div", { cls: "sci-dlg", role: "dialog", "aria-modal": "true", "aria-label": "رسم من المكتبة", "data-pic-dlg": "1" });
    const wrapEl = h("div", { cls: "sci-cover", onpointerdown: e => { if (e.target === wrapEl) this.closeEditor(); } }, box);
    this.dlg = wrapEl; document.body.appendChild(wrapEl);
    const say = (t, kind) => { msg.textContent = t; msg.className = "sci-msg " + (kind || ""); };
    let timer = 0;
    const refresh = () => { clearTimeout(timer); timer = setTimeout(async () => {
      try { const r = await this.render({ ...sci, cap: st.cap, credit: st.credit, sc: 1 }, "#111"); if (!this.dlg) return; prev.textContent = ""; prev.append(h("img", { src: r.url, alt: "معاينة", style: { maxWidth: "100%", maxHeight: "300px" } })); }
      catch (e) { say(e.message, "err"); }
    }, 150); };
    const cap = h("input", { type: "text", cls: "wide", value: st.cap, placeholder: "مثال: تركيب الخلية النباتية", oninput: e => { st.cap = e.target.value; refresh(); } });
    const credit = h("input", { type: "checkbox", checked: st.credit ? true : null, disabled: needsCredit(sci.lic) ? true : null, onchange: e => { st.credit = e.target.checked ? 1 : 0; refresh(); } });
    const sizeSel = h("select", { onchange: e => { st.sc = +e.target.value; } }, Object.values(SHAPE_SC).map(([v, l]) => h("option", { value: v, selected: Math.abs(v - st.sc) < 1e-6 ? true : null }, l)));
    const nSel = h("select", { "aria-label": "عدد الأسهم", onchange: e => { st.labels = +e.target.value; } }, [2, 3, 4, 5, 6, 7, 8].map(v => h("option", { value: v, selected: v === 4 ? true : null }, String(v))));
    const done = async (labels) => {
      const src = { ...sci, cap: st.cap.trim() || undefined, credit: st.credit || undefined, sc: st.sc };
      try {
        const it = await this.place(src, edit);
        if (!it) return;
        this.closeEditor();
        if (labels) this.addLabels(edit ? edit.key : (this.board?.curSurface?.() || Ink.visibleSurface()).key, it.id, st.labels);
      } catch (e) { say(e.message, "err"); }
    };
    box.addEventListener("keydown", e => { if (e.key === "Escape") { e.preventDefault(); this.closeEditor(); } });
    box.append(
      h("div", { cls: "sci-dlg-h" }, h("b", {}, "🖼 " + (sci.name || "رسم من المكتبة")), h("button", { cls: "sci-x", title: "إغلاق (Esc)", onclick: () => this.closeEditor() }, "✕")),
      h("div", { cls: "sci-form" },
        h("label", { cls: "sci-row wide" }, h("span", {}, "عنوان تحت الرسم"), cap),
        h("label", { cls: "sci-row" }, h("span", {}, "الحجم"), sizeSel),
        h("label", { cls: "sci-row chk", title: needsCredit(sci.lic) ? "ترخيص هذا الرسم يشترط ذكر المصدر" : "" }, credit, h("span", {}, "اذكر المصدر تحت الرسم")),
        h("div", { cls: "sci-row wide sci-credit" }, `المصدر: ${sci.author || "—"} · الترخيص: ${licName(sci.lic)}`)),
      prev, msg,
      h("div", { cls: "sci-foot" },
        h("button", { cls: "pri", onclick: () => done(false) }, edit ? "احفظ التعديل" : "أدرج على الصفحة"),
        h("button", { title: "يضيف أسهمًا حمراء وخانات مرقمة فارغة حول الرسم يكتب فيها الطالب أسماء الأجزاء — اسحب رأس كل سهم إلى الجزء المطلوب", onclick: () => done(true) }, "✍ أضف أسهم تسمية فارغة"), nSel,
        h("button", { onclick: () => this.closeEditor() }, "إلغاء"))
    );
    refresh();
  },

  // ---------------------------------------------------------------- مصادر أونلاين
  /** PhET وGeoGebra وDesmos (نافذة جوه «مسار») + بحث PubChem وWikimedia Commons. */
  onlineSection(tab) {
    const out = [];
    const sites = SITES[tab] || [];
    if (sites.length) out.push(this.section("تطبيقات أونلاين (تُفتح في نافذة داخل «مسار»، ثم «التقط إلى السبورة»)",
      sites.map(([src, url, name, icon]) => this.tile(icon, name, src === "desmos" ? "رسم الدوال والجداول والمنزلقات" : "من موقع GeoGebra الرسمي (للاستخدام غير التجاري)", () => this.web(src, url, name), { "data-web": src }))));
    const sims = PHET[tab] || [];
    if (sims.length) {
      const st = (this.phetN ||= {});
      const n = st[tab] || 8, box = h("div", { cls: "sci-tpl" });
      const fill = k => { box.textContent = ""; box.append(...sims.slice(0, k).map(([id, name]) => h("button", { "data-phet": id, title: "محاكاة PhET — تُفتح من موقعها الرسمي بالعربية إن وُجدت ترجمة", onclick: () => this.web("phet", phetUrl(id), "PhET — " + name) }, "▶ " + name)));
        if (k < sims.length) box.append(h("button", { onclick: () => { st[tab] = sims.length; fill(sims.length); } }, `المزيد (${sims.length - k})`)); };
      fill(n);
      out.push(this.section("محاكيات PhET التفاعلية (أونلاين)", box));
    }
    if (tab === "chem") out.push(this.section("بحث المركبات في PubChem (أونلاين)", this.searchBox("اسم المركب بالإنجليزية أو صيغته: aspirin، C6H12O6", async (q, res) => {
      const c = await pubchemCompound(q);
      res.append(h("div", { cls: "sci-pc" }, h("img", { src: c.png, alt: c.title }), h("b", {}, c.title), h("small", {}, `${c.formula} · ${c.weight} جم/مول`), h("small", { dir: "ltr" }, c.iupac || ""),
        h("button", { cls: "pri", onclick: async () => { try { await Apps.insert({ app: "pubchem", cid: c.cid, title: c.title }, await pubchemSvg(c), null, 360); } catch (e) { this.toast(e.message); } } }, "أدرج في السبورة"),
        h("button", { onclick: () => Apps.mol3d({}).then(() => { const q2 = document.querySelector(".m3-side input"); if (q2) { q2.value = c.title; q2.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter" })); } }) }, "اعرضه ثلاثي الأبعاد")));
    })));
    out.push(this.section("صور من Wikimedia Commons (أونلاين، بترخيص كل صورة)", this.searchBox("ابحث: بركان، خلية، heart…", async (q, res) => {
      const list = await commonsSearch(q);
      if (!list.length) { res.append(h("div", { cls: "sci-lib-msg" }, "لا توجد نتائج")); return; }
      const grid = h("div", { cls: "sci-lib-res" });
      grid.append(...list.map(it => h("button", { cls: "sci-lib-it", title: `${it.title}\n${it.author} — ${it.lic}`, onclick: async () => {
        document.body.classList.add("sci-busy");
        try { await Apps.insert({ app: "commons", title: it.title, page: it.page, lic: it.lic, author: it.author }, await commonsSvg(it), null, 480); } catch (e) { this.toast(e.message); }
        finally { document.body.classList.remove("sci-busy"); }
      } }, h("img", { src: it.thumb, alt: "", loading: "lazy" }), h("small", {}, it.lic))));
      res.append(grid);
    })));
    return out;
  },
  /** خانة بحث بنتيجة تحتها (Enter أو زر). */
  searchBox(ph, run) {
    const res = h("div", { cls: "sci-sbox-res" });
    const input = h("input", { type: "search", cls: "sci-lib-q", placeholder: ph });
    const go = async () => { const q = input.value.trim(); if (!q) return; res.textContent = ""; res.append(h("div", { cls: "sci-lib-msg" }, "جارٍ البحث…"));
      try { const tmp = h("div"); await run(q, tmp); res.textContent = ""; res.append(...tmp.childNodes); } catch (e) { res.textContent = ""; res.append(h("div", { cls: "sci-lib-msg" }, e.message)); } };
    input.addEventListener("keydown", e => { if (e.key === "Enter") { e.preventDefault(); go(); } });
    return h("div", { cls: "sci-sbox" }, h("div", { cls: "sci-sbox-in" }, input, h("button", { onclick: go }, "ابحث")), res);
  },
  /** فتح مصدر أونلاين في نافذة البرنامج (اللقطة بترجع على webCaptured). */
  web(source, url, title, edit) {
    this.webEdit = edit || null;
    if (!this.post) { this.toast("هذه المصادر تعمل داخل «مسار» فقط"); return; }
    this.post({ t: "sciweb", message: url, name: title || "", code: source });
  },
  async webCaptured(d) {
    try {
      const url = await captionedShot(d.img, CREDIT[d.source] || d.source);
      const edit = this.webEdit; this.webEdit = null;
      if (edit && !Ink.doc.items(edit.key).some(i => i.id === edit.id)) return Apps.insert({ app: "web", source: d.source, url: d.url, title: d.title }, url, null, 640);
      return await Apps.insert({ app: "web", source: d.source, url: d.url, title: d.title }, url, edit, 640);
    } catch (e) { this.toast(e.message); }
  },

  // ---------------------------------------------------------------- محرر الأشكال (حقول عامة)
  shapeEditor(sci, edit) {
    this.closeEditor();
    const tool = TOOLS[sci.type];
    if (!tool) return;
    const st = { p: { ...this.defaults(sci.type), ...(sci.p || {}) }, c: sci.c || null, sc: sci.sc || SHAPE_SC[pref("ssize", "m")]?.[0] || 0.75 };
    document.body.classList.add("sci-on");
    const prev = h("div", { cls: "sci-prev big", "aria-live": "polite" }), msg = h("div", { cls: "sci-msg" });
    const box = h("div", { cls: "sci-dlg", role: "dialog", "aria-modal": "true", "aria-label": tool.name, "data-shape-dlg": sci.type });
    const wrapEl = h("div", { cls: "sci-cover", onpointerdown: e => { if (e.target === wrapEl) this.closeEditor(); } }, box);
    this.dlg = wrapEl;
    document.body.appendChild(wrapEl);
    const say = (t, kind) => { msg.textContent = t; msg.className = "sci-msg " + (kind || ""); };
    const color = () => st.c || Ink.currentColor() || "#111111";
    let timer = 0, seq = 0;
    const refresh = () => {
      clearTimeout(timer);
      timer = setTimeout(async () => {
        const my = ++seq;
        try {
          const r = await this.render({ k: "shape", type: sci.type, p: st.p, sc: 1 }, color());
          if (my !== seq || !this.dlg) return;
          prev.textContent = ""; say("");
          prev.append(h("img", { src: r.url, alt: "معاينة", style: { maxWidth: "100%", maxHeight: "300px" } }));
        } catch (e) { if (my === seq) say(e.message, "err"); }
      }, 140);
    };
    const form = h("div", { cls: "sci-form" });
    const fields = [];
    const vis = () => { for (const [f, row] of fields) row.hidden = !!(f[5] && !f[5](st.p)); };
    for (const f of tool.f) {
      const [key, label, type, , opts] = f;
      let input;
      if (type === "chk") {
        input = h("input", { type: "checkbox", checked: st.p[key] ? true : null, onchange: e => { st.p[key] = e.target.checked ? 1 : 0; vis(); refresh(); } });
        const row = h("label", { cls: "sci-row chk", "data-f": key }, input, h("span", {}, label));
        fields.push([f, row]); form.append(row); continue;
      }
      if (type === "sel") {
        input = h("select", { onchange: e => { st.p[key] = e.target.value; vis(); refresh(); } }, (opts || []).map(([v, l]) => h("option", { value: v, selected: String(st.p[key]) === String(v) ? true : null }, l)));
      } else {
        input = h("input", { type: "text", dir: "ltr", spellcheck: "false", inputmode: type === "num" ? "decimal" : null, value: st.p[key] ?? "", placeholder: opts || "", oninput: e => { st.p[key] = type === "num" ? e.target.value : e.target.value; refresh(); } });
        if (type === "text") input.classList.add("wide");
      }
      const row = h("label", { cls: "sci-row" + (type === "text" ? " wide" : ""), "data-f": key }, h("span", {}, label), input);
      fields.push([f, row]); form.append(row);
    }
    vis();
    const sizeSel = h("select", { "aria-label": "الحجم على الصفحة", onchange: e => { st.sc = +e.target.value; setPref("ssize", Object.keys(SHAPE_SC).find(k => SHAPE_SC[k][0] === st.sc) || "m"); } },
      Object.values(SHAPE_SC).map(([v, l]) => h("option", { value: v, selected: Math.abs(v - st.sc) < 1e-6 ? true : null }, l)));
    const swatches = h("div", { cls: "sci-sw" }, SWATCH.map(c => h("button", {
      cls: "sw" + (st.c === c ? " on" : ""), title: "اللون", style: { background: c },
      onclick: e => { st.c = c; for (const b of swatches.children) b.classList.toggle("on", b === e.currentTarget); refresh(); }
    })));
    const done = async () => {
      const src = { k: "shape", type: sci.type, p: { ...st.p }, sc: st.sc };
      if (st.c) src.c = st.c;
      try { const it = await this.place(src, edit); if (it) this.closeEditor(); } catch (e) { say(e.message, "err"); }
    };
    box.addEventListener("keydown", e => {
      if (e.key === "Escape") { e.preventDefault(); this.closeEditor(); }
      else if (e.key === "Enter" && e.target.tagName !== "SELECT") { e.preventDefault(); done(); }
    });
    box.append(
      h("div", { cls: "sci-dlg-h" }, h("b", {}, (edit ? "تعديل: " : "") + tool.icon + " " + tool.name), h("button", { cls: "sci-x", title: "إغلاق (Esc)", onclick: () => this.closeEditor() }, "✕")),
      form,
      h("div", { cls: "sci-opts" }, h("span", {}, "اللون"), swatches, h("span", {}, "الحجم"), sizeSel),
      prev, msg,
      h("div", { cls: "sci-foot" },
        h("button", { cls: "pri", onclick: done }, edit ? "احفظ التعديل" : "أدرج على الصفحة"),
        h("button", { onclick: () => this.closeEditor() }, "إلغاء"),
        h("small", {}, "Enter للإدراج"))
    );
    refresh();
    setTimeout(() => form.querySelector("input,select")?.focus(), 0);
  },
  closeEditor() {
    if (!this.dlg) return;
    try { window.mathVirtualKeyboard?.hide(); } catch (e) { }
    this.dlg.remove(); this.dlg = null;
    document.body.classList.remove("sci-on");
  },

  /** يرسم المصدر ويحطه على الصفحة (جديد في نص الجزء الظاهر، أو مكان القديم لو تعديل). */
  async place(src, edit) {
    const s = edit ? Ink.surfaces.get(edit.key) : (this.board?.curSurface?.() || Ink.visibleSurface());
    if (!s) throw new Error("افتح صفحة من السبورة أولًا");
    const base = src.c || Ink.currentColor() || "#111111";
    const r = await this.render(src, s.dark ? onDark(base) : base);
    // المصدر اللي بيتحفظ مع العنصر (من غير صورة الأداة المؤقتة)
    src = { ...src }; delete src.__img; delete src.__w; delete src.__h;
    src.nw = r.w;
    if (edit) {
      const old = Ink.doc.items(s.key).find(i => i.id === edit.id);
      if (!old) throw new Error("العنصر لم يعد موجودًا");
      const k = old.sci?.nw ? old.w / old.sci.nw : 1;
      const w = r.w * k, hh = r.h * k, cx = old.x + old.w / 2, cy = old.y + old.h / 2;
      let out = null;
      Ink.edit(s.key, items => items.map(i => i.id !== edit.id ? i : (out = { ...i, img: r.url, x: cx - w / 2, y: cy - hh / 2, w, h: hh, sci: src, u: Date.now() })));
      return out;
    }
    const rc = s.layer.getBoundingClientRect();
    const vis = [Math.max(rc.top, 0), Math.min(rc.bottom, innerHeight)];
    const cy = ((vis[0] + vis[1]) / 2 - rc.top) / rc.width * W;
    const w = Math.min(r.w, W * 0.9), hh = r.h * (w / r.w);
    // لو فيه عنصر علمي في نفس المكان بالظبط (إدراج ورا بعض) ← نزحزح الجديد شوية عشان ميتغطاش
    let x = (W - w) / 2, y = Math.max(10, cy - hh / 2);
    const others = Ink.doc.items(s.key).filter(i => i.sci);
    for (let k = 0; k < 8 && others.some(i => Math.abs(i.x - x) < 8 && Math.abs(i.y - y) < 8); k++) { x += 28; y += 28; }
    const it = Ink.add(s.key, { t: "image", img: r.url, x, y, w, h: hh, sci: src });
    Ink.setTool("select");
    Ink.setSel({ key: s.key, ids: new Set([it.id]) });
    return it;
  },

  /** نقر مزدوج على عنصر علمي. */
  editItem(s, it) {
    if (!it?.sci) return;
    if (it.sci.k === "eq" || it.sci.k === "chem") this.editor(it.sci, { key: s.key, id: it.id });
    else if (it.sci.k === "shape" && TOOLS[it.sci.type]) this.shapeEditor(it.sci, { key: s.key, id: it.id });
    else if (it.sci.k === "pic") this.picEditor(it.sci, { key: s.key, id: it.id });
    else if (it.sci.k === "app") Apps.reopen(it.sci, { key: s.key, id: it.id });
  }
};
