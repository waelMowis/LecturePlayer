// =====================================================================
//  الأدوات العلمية للسبورة: لوحة جانبية بأربع مواد (رياضيات/كيمياء/فيزياء/أحياء).
//  كل أداة بتنزّل على الصفحة عنصر «image» عادي (صورة SVG فيكتور) ومعاه sci = مصدره،
//  فبيتحرك ويتكبّر ويتصدّر PDF زي أي صورة، والنقر المزدوج بيرجّعه لمحرره.
// =====================================================================
import { Ink, W, onDark } from "./ink.js";
import { balance, rebuild, parseOnly, isBalanced } from "./sci-chem.js";
import { MATH_T, PHYS_T, CHEM_T, BIO_T, ARABIC_KEYS } from "./sci-data.js";

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
  for (const c of kids.flat()) if (c != null && c !== false) e.append(c.nodeType ? c : document.createTextNode(String(c)));
  return e;
};
const LIB = "lib/sci/";
const SIZES = { s: [24, "صغير"], m: [34, "متوسط"], l: [48, "كبير"], xl: [68, "كبير جدًا"] };
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
    Ink.onSciEdit = (s, it) => this.editItem(s, it);
    Ink.onSciRecolor = (key, ids, c) => this.recolor(key, ids, c);
  },
  /** لون جديد من شريط التحديد ← نرسم العناصر العلمية تاني بنفس مكانها ومقاسها. */
  async recolor(key, ids, c) {
    for (const id of ids) {
      const it = Ink.doc.items(key).find(i => i.id === id);
      if (!it?.sci || !this.canRender(it.sci)) continue;
      try { await this.place({ ...it.sci, c }, { key, id }); } catch (e) { }
    }
  },
  canRender(sci) { return sci.k === "eq" || sci.k === "chem"; },
  isOn() { return !!(this.panel && !this.panel.hidden); },
  toggle() { if (this.isOn()) this.close(); else this.open(); },
  close() { if (this.panel) this.panel.hidden = true; this.board?.onSciPanel?.(false); },
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
      this.section("المعادلات", this.tile("✎", "معادلة جديدة", "كسور، جذور، أسس، مصفوفات، تكامل — ولوحة رموز عربية", () => this.editor({ k: "eq", tex: "" }))),
      this.section("صيغ جاهزة", this.templates(MATH_T, "eq"))
    ];
    if (tab === "chem") return [
      this.section("المعادلات الكيميائية",
        this.tile("⚗", "معادلة كيميائية", "اكتب 2H2 + O2 -> 2H2O فتظهر بالأرقام السفلية والأسهم", () => this.editor({ k: "chem", ce: "" })),
        this.tile("⚖", "وازن معادلة", "اكتبها بلا معاملات واضغط «وازن»، أو أخفِ المعاملات للتمرين", () => this.editor({ k: "chem", ce: "", balance: 1 }))),
      this.section("تفاعلات جاهزة", this.templates(CHEM_T, "chem"))
    ];
    if (tab === "phys") return [
      this.section("القوانين والمعادلات", this.tile("✎", "قانون أو معادلة", "متجهات، دلتا، وحدات، كسور", () => this.editor({ k: "eq", tex: "" }))),
      this.section("قوانين جاهزة", this.templates(PHYS_T, "eq"))
    ];
    return [
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
    const r = await renderTex(texOf(src), s.dark ? onDark(base) : base, src.fs || 34, !!src.rtl);
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
    const it = Ink.add(s.key, { t: "image", img: r.url, x: (W - w) / 2, y: Math.max(10, cy - hh / 2), w, h: hh, sci: src });
    Ink.setTool("select");
    Ink.setSel({ key: s.key, ids: new Set([it.id]) });
    return it;
  },

  /** نقر مزدوج على عنصر علمي. */
  editItem(s, it) {
    if (!it?.sci) return;
    if (it.sci.k === "eq" || it.sci.k === "chem") this.editor(it.sci, { key: s.key, id: it.id });
  }
};
