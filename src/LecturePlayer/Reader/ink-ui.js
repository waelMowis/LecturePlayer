// =====================================================================
//  شريط أدوات الشرح: الأقلام والألوان والممحاة والأشكال والنص والملصقات وأدوات العرض.
//  عام: بيشتغل فوق أي سطح بيستخدم محرك ink.js (كتاب، سبورة، الشاشة).
// =====================================================================
import { Ink, PENS, COLORS, SHAPES, STICKERS, EMOJI, FONTS } from "./ink.js";

const h = (tag, attrs = {}, ...kids) => {
  const e = document.createElement(tag);
  for (const k in attrs) {
    const v = attrs[k];
    if (v == null || v === false) continue;
    if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (k === "style" && typeof v === "object") for (const sk in v) { if (sk.startsWith("--")) e.style.setProperty(sk, v[sk]); else e.style[sk] = v[sk]; }
    else if (k === "cls") e.className = v;
    else if (k === "text") e.textContent = v;
    else e.setAttribute(k, v === true ? "" : v);
  }
  for (const c of kids.flat()) if (c != null && c !== false) e.append(c.nodeType ? c : document.createTextNode(String(c)));
  return e;
};

const PEN_ICONS = { pen: "🖊", pencil: "✏", marker: "🖍", hl: "▬", temp: "✨", line: "╱" };
const TOOL_KEYS = { p: "pen", h: "hl", e: "eraser", l: "laser", t: "text", s: "shape", v: "select" };

export const InkUI = {
  bar: null, pop: null, selbar: null, toast: null, popFor: null, opts: {},

  init(opts = {}) {
    this.opts = opts;
    this.bar = h("div", { id: "inkbar", hidden: true });
    this.pop = h("div", { cls: "ink-pop", hidden: true });
    this.selbar = h("div", { id: "inksel", hidden: true });
    this.toast = h("div", { id: "inktoast", hidden: true });
    document.body.append(this.bar, this.pop, this.selbar, this.toast);
    Ink.onTool = () => this.refresh();
    Ink.onSel = () => this.showSelBar();
    Ink.onChange = () => { if (!this.selbar.hidden) this.placeSelBar(); };
    document.addEventListener("pointerdown", e => {
      if (!this.pop.hidden && !this.pop.contains(e.target) && !this.bar.contains(e.target)) this.closePop();
    }, true);
    window.addEventListener("wheel", e => {
      if (Ink.on && Ink.tool === "spot" && !e.ctrlKey) { e.preventDefault(); Ink.spot.wheel(e.deltaY); Ink.spot.d.style.width && Ink.spot.place(e.clientX, e.clientY); }
      if (Ink.on && Ink.tool === "lens" && e.ctrlKey) { e.preventDefault(); Ink.lens.z = Math.max(1.3, Math.min(5, Ink.lens.z - e.deltaY * 0.002)); }
    }, { passive: false });
    this.build();
    this.dragBar();
  },

  // ---------------------------------------------------------------- الشريط
  build() {
    const B = (id, icon, title, fn) => h("button", { "data-t": id, title, onclick: e => { e.stopPropagation(); fn(e); } }, icon);
    const bar = this.bar;
    bar.textContent = "";
    bar.append(
      h("div", { cls: "grip", title: "اسحب الشريط لأي مكان — دبل كليك يقلبه أفقي/رأسي" }, "⠿"),
      B("hand", "✋", "تحريك وتصفح الكتاب (Esc)", () => this.tool("hand")),
      B("pen", "🖊", "الأقلام (P) — دوس تاني للألوان والمقاس", () => this.toolOrPop("pen", this.penPop)),
      B("hl", "🖍", "قلم التظليل (H) — لو وقفت ثانية في آخر الخط يتعدل مستقيم", () => { if (Ink.tool === "pen" && Ink.pen === "hl") this.openPop("hl", this.penPop); else this.tool("pen", "hl"); }),
      B("eraser", "🧽", "الممحاة (E) — دوس تاني لأنواعها", () => this.toolOrPop("eraser", this.eraserPop)),
      B("shape", "⬠", "الأشكال والأسهم (S)", () => this.toolOrPop("shape", this.shapePop, true)),
      B("text", "T", "نص (T) — كليك واكتب، أو اسحب مربع", () => this.toolOrPop("text", this.textPop)),
      B("note", "📝", "ملاحظة لاصقة", () => this.tool("note")),
      B("callout", "💬", "فقاعة كلام بسهم", () => this.tool("callout")),
      B("step", "①", "خطوات مترقمة ①②③", () => this.tool("step")),
      B("sticker", "⭐", "ملصقات وأختام وإيموجي وصور", () => this.openPop("sticker", this.stickerPop)),
      B("select", "➤", "تحديد (V): كليك أو لف دايرة حوالين الحاجات — نقل وتكبير ولف ونسخ", () => this.tool("select")),
      B("present", "🔦", "أدوات العرض: ليزر، كشاف، عدسة، ستارة، مسطرة، منقلة", () => this.openPop("present", this.presentPop)),
      h("div", { cls: "hr" }),
      h("button", { cls: "colorbtn", "data-t": "color", title: "اللون", onclick: e => { e.stopPropagation(); this.openPop("color", this.colorPop); } }),
      B("undo", "↶", "تراجع (Ctrl+Z)", () => Ink.doUndo()),
      B("redo", "↷", "إعادة (Ctrl+Y)", () => Ink.doRedo()),
      B("layer", "◐", "الطبقة: دائم (بيتحفظ) أو مؤقت (للشرح بس وبيتمسح بزرار)", () => { Ink.layer = Ink.layer === "p" ? "t" : "p"; this.say(Ink.layer === "t" ? "الطبقة المؤقتة: اللي هترسمه للشرح بس — «امسح المؤقت» يشيله" : "الطبقة الدائمة: اللي هترسمه بيتحفظ"); this.refresh(); }),
      B("eye", "👁", "إظهار/إخفاء الشرح", () => Ink.setVisible(!Ink.visible)),
      B("more", "⚙", "إعدادات وعرض واختصارات", () => this.openPop("more", this.morePop)),
      B("close", "✕", "قفل أدوات الشرح (كل حاجة اتحفظت)", () => this.setOn(false))
    );
    if (Ink.prefs.barH) bar.classList.add("horizontal");
    this.refresh();
  },
  refresh() {
    if (!this.bar) return;
    const t = Ink.tool;
    for (const b of this.bar.querySelectorAll("button[data-t]")) {
      const id = b.dataset.t;
      const on = id === t && !(id === "pen" && Ink.pen === "hl") || (id === "hl" && t === "pen" && Ink.pen === "hl")
        || (id === "present" && ["laser", "spot", "lens", "curtain"].includes(t)) || (id === "eye" && !Ink.visible);
      b.classList.toggle("on", !!on);
    }
    const pen = this.bar.querySelector('[data-t="pen"]');
    if (pen) { pen.firstChild.textContent = PEN_ICONS[Ink.pen === "hl" ? "pen" : Ink.pen] || "🖊"; this.dot(pen, Ink.penOpts(Ink.pen === "hl" ? "pen" : Ink.pen).c); }
    const hl = this.bar.querySelector('[data-t="hl"]'); if (hl) this.dot(hl, Ink.penOpts("hl").c);
    const shape = this.bar.querySelector('[data-t="shape"]'); if (shape) shape.firstChild.textContent = (SHAPES.find(x => x[0] === Ink.shape) || SHAPES[1])[2];
    const pres = this.bar.querySelector('[data-t="present"]'); if (pres) pres.firstChild.textContent = { laser: "🔴", spot: "🔦", lens: "🔍", curtain: "🎭" }[t] || "🔦";
    const c = this.bar.querySelector(".colorbtn"); if (c) c.style.setProperty("--c", Ink.currentColor());
    const ly = this.bar.querySelector('[data-t="layer"]');
    if (ly) { ly.classList.toggle("on", Ink.layer === "t"); ly.title = Ink.layer === "t" ? "الطبقة المؤقتة شغالة — دوس للدائمة" : "الطبقة الدائمة شغالة — دوس للمؤقتة (للشرح بس)"; ly.firstChild.textContent = Ink.layer === "t" ? "⏳" : "◐"; }
    const u = this.bar.querySelector('[data-t="undo"]'); if (u) u.disabled = !Ink.undo.length;
    const r = this.bar.querySelector('[data-t="redo"]'); if (r) r.disabled = !Ink.redo.length;
    if (this.opts.onRefresh) this.opts.onRefresh();
  },
  dot(btn, c) { let d = btn.querySelector(".dot"); if (!d) { d = h("span", { cls: "dot" }); btn.append(d); } d.style.background = c; },
  tool(t, sub) { this.closePop(); Ink.setTool(t, sub); if (t === "pen" && sub === "hl") this.say("قلم التظليل — اقف ثانية في آخر الخط يتعدل مستقيم"); },
  toolOrPop(t, popFn, alwaysPop) {
    const same = Ink.tool === t && (t !== "pen" || Ink.pen !== "hl");
    if (same || alwaysPop) { if (!same) Ink.setTool(t); this.openPop(t, popFn); }
    else this.tool(t, t === "pen" && Ink.pen === "hl" ? "pen" : undefined);
  },
  setOn(on) {
    const changed = !!on !== Ink.on;
    Ink.setOn(on);
    this.bar.hidden = !on;
    if (!on) { this.closePop(); this.selbar.hidden = true; document.body.classList.remove("present"); }
    if (on && Ink.tool === "hand") Ink.setTool("pen");
    this.refresh();
    if (changed && this.opts.onToggle) this.opts.onToggle(on);
  },
  dragBar() {
    const bar = this.bar; let d = null;
    let last = 0;
    bar.addEventListener("pointerdown", e => {
      if (!e.target.classList.contains("grip")) return;
      e.preventDefault();
      // دبل كليك على المقبض: أفقي ⇄ رأسي (بنحسبه إحنا عشان المقبض بيمسك الماوس)
      if (Date.now() - last < 450) { last = 0; bar.classList.toggle("horizontal"); Ink.prefs.barH = bar.classList.contains("horizontal"); Ink.savePrefs(); return; }
      last = Date.now();
      d = { x: e.clientX - bar.offsetLeft, y: e.clientY - bar.offsetTop }; bar.setPointerCapture(e.pointerId);
    });
    bar.addEventListener("pointermove", e => {
      if (!d) return;
      bar.style.left = Math.max(0, Math.min(innerWidth - 40, e.clientX - d.x)) + "px";
      bar.style.top = Math.max(0, Math.min(innerHeight - 40, e.clientY - d.y)) + "px";
    });
    bar.addEventListener("pointerup", () => { if (d) { d = null; Ink.prefs.barPos = [bar.offsetLeft, bar.offsetTop]; Ink.savePrefs(); } });
  },
  applyBarPos() {
    const p = Ink.prefs.barPos;
    if (p) { this.bar.style.left = Math.min(p[0], innerWidth - 50) + "px"; this.bar.style.top = Math.min(p[1], innerHeight - 50) + "px"; }
    this.bar.classList.toggle("horizontal", !!Ink.prefs.barH);
  },
  say(text, ms = 2600) {
    this.toast.textContent = text; this.toast.hidden = false;
    clearTimeout(this._toast); this._toast = setTimeout(() => this.toast.hidden = true, ms);
  },

  // ---------------------------------------------------------------- القوائم الصغيرة
  openPop(id, fn) {
    if (this.popFor === id && !this.pop.hidden) { this.closePop(); return; }
    this.popFor = id;
    this.pop.textContent = "";
    fn.call(this, this.pop);
    this.pop.hidden = false;
    const btn = this.bar.querySelector(`[data-t="${id}"]`) || this.bar;
    const r = btn.getBoundingClientRect(), pr = this.pop.getBoundingClientRect();
    let x, y;
    if (this.bar.classList.contains("horizontal")) { x = r.left; y = r.bottom + 6; if (y + pr.height > innerHeight) y = r.top - pr.height - 6; }
    else { x = r.right + 8; y = r.top - 8; if (x + pr.width > innerWidth) x = r.left - pr.width - 8; }
    this.pop.style.left = Math.max(4, Math.min(innerWidth - pr.width - 4, x)) + "px";
    this.pop.style.top = Math.max(4, Math.min(innerHeight - pr.height - 4, y)) + "px";
  },
  closePop() { this.pop.hidden = true; this.popFor = null; },
  rerender() { const id = this.popFor; if (!id) return; const fn = { pen: this.penPop, hl: this.penPop, eraser: this.eraserPop, shape: this.shapePop, text: this.textPop, sticker: this.stickerPop, present: this.presentPop, color: this.colorPop, more: this.morePop }[id]; if (fn) { this.pop.textContent = ""; fn.call(this, this.pop); } },

  swatches(cur, pick, list = COLORS) {
    return h("div", { cls: "grid g8" }, list.map(c => h("button", { cls: "sw" + (cur && cur.toLowerCase() === c.toLowerCase() ? " on" : ""), style: { "--c": c }, title: c, onclick: () => { pick(c); this.rerender(); } })));
  },
  colorRows(cur, pick) {
    const kids = [h("h4", {}, "الألوان"), this.swatches(cur, pick)];
    if (Ink.prefs.recent.length) kids.push(h("h4", {}, "آخر ألوان استخدمتها"), this.swatches(cur, pick, Ink.prefs.recent));
    const inp = h("input", { type: "color", value: /^#[0-9a-f]{6}$/i.test(cur || "") ? cur : "#e03131", oninput: e => pick(e.target.value), onchange: () => this.rerender() });
    kids.push(h("div", { cls: "row" },
      h("label", {}, "لون تاني:", inp),
      window.EyeDropper ? h("button", { title: "خد لون من أي حتة في الشاشة", onclick: async () => { try { const r = await new window.EyeDropper().open(); pick(r.sRGBHex); this.rerender(); } catch (x) { } } }, "💧 قطّارة") : null));
    return kids;
  },
  slider(label, min, max, step, val, fn, fmt = v => v) {
    const out = h("span", { cls: "val" }, fmt(val));
    return h("label", { cls: "wide" }, label, h("input", { type: "range", min, max, step, value: val, oninput: e => { const v = +e.target.value; out.textContent = fmt(v); fn(v); } }), out);
  },

  penPop(p) {
    const isHl = this.popFor === "hl";
    const names = isHl ? ["hl"] : ["pen", "pencil", "marker", "temp", "line"];
    if (!isHl) {
      p.append(h("h4", {}, "الأقلام"));
      for (const n of names) {
        const o = Ink.penOpts(n);
        const svg = `<svg viewBox="0 0 60 20"><path d="M3 14 C 18 2, 30 20, 57 6" stroke="${o.c}" stroke-width="${Math.min(8, o.size)}" stroke-opacity="${o.o}" fill="none" stroke-linecap="round" ${n === "line" ? 'stroke-dasharray="0"' : ""}/></svg>`;
        const b = h("button", { cls: "pen" + (Ink.tool === "pen" && Ink.pen === n ? " on" : ""), onclick: () => { Ink.setTool("pen", n); this.rerender(); } }, PEN_ICONS[n] + " " + PENS[n].name);
        b.insertAdjacentHTML("beforeend", svg);
        p.append(b);
      }
    }
    const cur = isHl ? "hl" : (Ink.pen === "hl" ? "pen" : Ink.pen), o = Ink.penOpts(cur);
    p.append(h("h4", {}, "مقاس «" + PENS[cur].name + "»"),
      h("div", { cls: "row" }, (isHl ? [8, 12, 16, 22, 30] : [1, 2, 3, 5, 8, 12]).map(v => h("button", { cls: o.size === v ? "on" : "", onclick: () => { Ink.setPenOpt(cur, "size", v); this.rerender(); } }, String(v)))),
      h("div", { cls: "row" }, this.slider("المقاس", 0.5, 40, 0.5, o.size, v => Ink.setPenOpt(cur, "size", v))),
      h("div", { cls: "row" }, this.slider("الشفافية", 0.1, 1, 0.05, o.o, v => Ink.setPenOpt(cur, "o", v), v => Math.round(v * 100) + "%")),
      ...this.colorRows(o.c, c => Ink.setPenOpt(cur, "c", c)),
      h("div", { cls: "hint" }, isHl ? "التظليل بيبان تحت الكلام زي القلم الفسفوري. وقفة ثانية في آخر الخط = خط مستقيم." : "Shift وانت بترسم = خط مستقيم (بيلقط كل 15°). كل قلم بيفتكر لونه ومقاسه."));
  },
  eraserPop(p) {
    const m = (id, label, tip) => h("button", { cls: "wide pen" + (Ink.tool === "eraser" && Ink.eraser === id ? " on" : ""), title: tip, onclick: () => { Ink.setTool("eraser", id); this.rerender(); } }, label);
    p.append(h("h4", {}, "نوع الممحاة"),
      m("stroke", "🧽 تمسح الخط كله", "أي خط تلمسه بيتمسح كله"),
      m("partial", "✂ تمسح حتة من الخط بس", "بتقطع الخط مكان ما تعدّي"),
      m("area", "⭕ لف دايرة تمسح اللي جواها", ""),
      h("h4", {}, "مسح سريع للصفحة الحالية"),
      h("div", { cls: "row" },
        h("button", { onclick: () => this.clear("temp") }, "⏳ امسح المؤقت"),
        h("button", { onclick: () => this.clear(Ink.currentColor()) }, h("span", { cls: "sw", style: { "--c": Ink.currentColor(), display: "inline-block", width: "14px", minWidth: "14px", height: "14px", verticalAlign: "middle" } }), " امسح اللون ده"),
        h("button", { onclick: () => this.clear("all") }, "🗑 امسح الصفحة كلها")),
      h("div", { cls: "hint" }, "زرار الممحاة اللي في القلم الديجيتال بيمسح على طول. وكل مسح ليه تراجع (Ctrl+Z)."));
  },
  clear(what) {
    const s = Ink.visibleSurface(); if (!s) return;
    if (what === "all" && !confirm("تمسح كل الشرح اللي على الصفحة دي؟ (تقدر ترجعه بـ Ctrl+Z)")) return;
    Ink.clearPage(s.key, what); this.closePop();
    this.say(what === "temp" ? "اتمسح الشرح المؤقت" : "اتمسح");
  },
  shapePop(p) {
    p.append(h("h4", {}, "الأشكال"),
      h("div", { cls: "grid" }, SHAPES.map(([id, name, icon]) => h("button", { title: name, cls: Ink.shape === id ? "on" : "", onclick: () => { Ink.setTool("shape", id); this.rerender(); } }, icon))));
    const sp = Ink.prefs.shape;
    p.append(h("div", { cls: "row" }, this.slider("سُمك الخط", 1, 14, 0.5, sp.sw, v => { sp.sw = v; Ink.savePrefs(); })),
      h("div", { cls: "row" },
        h("label", {}, h("input", { type: "checkbox", checked: !!sp.fill, onchange: e => { sp.fill = e.target.checked ? (sp.fillC || Ink.prefs.color) : ""; Ink.savePrefs(); this.rerender(); } }), " مليان"),
        sp.fill ? h("input", { type: "color", value: sp.fill, oninput: e => { sp.fill = sp.fillC = e.target.value; Ink.savePrefs(); } }) : null),
      sp.fill ? h("div", { cls: "row" }, this.slider("شفافية التعبئة", 0.05, 1, 0.05, sp.fo, v => { sp.fo = v; Ink.savePrefs(); }, v => Math.round(v * 100) + "%")) : "",
      ...this.colorRows(Ink.prefs.color, c => Ink.setColor(c)),
      h("div", { cls: "hint" }, "Shift = مربع/دايرة مظبوطة أو سهم على زاوية مظبوطة. أي شكل فاضي بتمسكه من حدوده."));
  },
  textPop(p) {
    const T = Ink.prefs.text, save = () => Ink.savePrefs();
    p.append(h("h4", {}, "شكل النص"),
      h("div", { cls: "row" }, h("select", { onchange: e => { T.font = e.target.value; save(); } }, FONTS.map(f => h("option", { value: f, selected: f === T.font }, f)))),
      h("div", { cls: "row" }, [14, 18, 22, 28, 36, 48].map(v => h("button", { cls: T.size === v ? "on" : "", onclick: () => { T.size = v; save(); this.rerender(); } }, String(v)))),
      h("div", { cls: "row" },
        h("button", { cls: T.bold ? "on" : "", onclick: () => { T.bold = !T.bold; save(); this.rerender(); } }, h("b", {}, "B")),
        h("button", { cls: T.align === "right" ? "on" : "", title: "يمين", onclick: () => { T.align = "right"; save(); this.rerender(); } }, "⇥"),
        h("button", { cls: T.align === "center" ? "on" : "", title: "نص", onclick: () => { T.align = "center"; save(); this.rerender(); } }, "↔"),
        h("button", { cls: T.align === "left" ? "on" : "", title: "شمال", onclick: () => { T.align = "left"; save(); this.rerender(); } }, "⇤"),
        h("select", { title: "الاتجاه", style: { flex: "0 0 92px" }, onchange: e => { T.dir = e.target.value; save(); } },
          [["auto", "تلقائي"], ["rtl", "عربي ←"], ["ltr", "English →"]].map(([v, l]) => h("option", { value: v, selected: T.dir === v }, l)))),
      h("div", { cls: "row" }, h("label", {}, h("input", { type: "checkbox", checked: !!T.bg, onchange: e => { T.bg = e.target.checked ? "#fff3bf" : ""; save(); this.rerender(); } }), " خلفية"),
        T.bg ? h("input", { type: "color", value: T.bg, oninput: e => { T.bg = e.target.value; save(); } }) : null),
      ...this.colorRows(T.c, c => { T.c = c; Ink.useColor(c); save(); this.refresh(); }),
      h("div", { cls: "hint" }, "كليك = نص جديد، أو اسحب مربع بالعرض اللي عايزه. دبل كليك على أي نص يعدّله. Esc أو Ctrl+Enter يخلّص."));
  },
  stickerPop(p) {
    const tab = this._stab || "built";
    const T = (id, label) => h("button", { cls: tab === id ? "on" : "", onclick: () => { this._stab = id; this.rerender(); } }, label);
    p.append(h("div", { cls: "tabs" }, T("built", "ملصقات"), T("emoji", "إيموجي"), T("stamp", "أختام"), T("img", "صورة"), T("mine", "ملصقاتي")));
    const pick = st => { Ink.setTool("sticker", st); this.closePop(); this.say("كليك مكان ما عايز تحطه — أو اسحب عشان تحدد المقاس"); };
    if (tab === "built") p.append(h("div", { cls: "grid g4 stk" }, STICKERS.map(s => h("button", { title: s.label || "سهم", style: { color: s.label ? "" : s.color }, onclick: () => pick({ sid: s.id }) },
      s.arrow != null ? h("span", { style: { display: "inline-block", transform: `rotate(${s.arrow}deg)` } }, "➜") : (s.icon + " " + s.label)))));
    if (tab === "emoji") p.append(h("div", { cls: "grid" }, EMOJI.map(e => h("button", { onclick: () => pick({ emoji: e }) }, e))));
    if (tab === "stamp") {
      const st = Ink.prefs.stamps;
      p.append(h("div", { cls: "grid g4 stk" }, ["ممتاز", "راجع", "مهم جدًا", "تم", "سؤال امتحان", "لم يُشرح", "مطلوب", "سؤال", ...st.map(s => s.text)].map((t, i) => {
        const mine = st.find(s => s.text === t);
        return h("button", { style: { color: mine ? mine.c : "#ff8787" }, oncontextmenu: mine ? e => { e.preventDefault(); Ink.prefs.stamps = st.filter(x => x !== mine); Ink.savePrefs(); this.rerender(); } : null, title: mine ? "كليك يمين يمسحه" : "", onclick: () => pick({ stamp: t, c: mine ? mine.c : "#e03131", shape: mine ? mine.shape : "round" }) }, t);
      })));
      const txt = h("input", { type: "text", placeholder: "ختم جديد: اكتب كلامه…", maxlength: 24 });
      const col = h("input", { type: "color", value: "#1971c2" });
      const shp = h("select", { style: { flex: "0 0 74px" } }, [["round", "مدوّر"], ["rect", "مربع"], ["circle", "دايرة"]].map(([v, l]) => h("option", { value: v }, l)));
      p.append(h("h4", {}, "اعمل ختم بكلامك"), h("div", { cls: "row" }, txt, col, shp),
        h("div", { cls: "row" }, h("button", { cls: "wide", onclick: () => { const t = txt.value.trim(); if (!t) return; Ink.prefs.stamps.push({ text: t, c: col.value, shape: shp.value }); Ink.savePrefs(); pick({ stamp: t, c: col.value, shape: shp.value }); } }, "➕ احفظه واستخدمه")));
    }
    if (tab === "img") {
      const f = h("input", { type: "file", accept: "image/*", hidden: true, onchange: e => { const file = e.target.files[0]; if (file) Ink.insertImageFile(file); this.closePop(); } });
      p.append(f, h("button", { cls: "wide pen", onclick: () => f.click() }, "🖼 اختار صورة من الجهاز"),
        h("div", { cls: "hint" }, "أو انسخ أي صورة (لقطة شاشة مثلًا) واعمل Ctrl+V وانت في وضع الشرح — بتتحط في نص الصفحة اللي قدامك وتقدر تكبرها وتلفها بأداة التحديد."));
    }
    if (tab === "mine") {
      const list = Ink.prefs.myStickers;
      if (!list.length) p.append(h("div", { cls: "hint" }, "حدد أي حاجة رسمتها (➤) ودوس ⭐ في الشريط الصغير اللي فوقها — هتتحفظ هنا وتستخدمها في أي كتاب."));
      for (const st of list) p.append(h("div", { cls: "mystk" },
        h("button", { onclick: () => pick({ mine: st }) }, "⭐ " + st.name),
        h("button", { title: "تغيير الاسم", onclick: () => { const n = prompt("اسم الملصق:", st.name); if (n) { st.name = n.trim(); Ink.savePrefs(); this.rerender(); } } }, "✎"),
        h("button", { title: "مسح", onclick: () => { Ink.prefs.myStickers = list.filter(x => x !== st); Ink.savePrefs(); this.rerender(); } }, "🗑")));
    }
  },
  presentPop(p) {
    const t = (id, label, tip) => h("button", { cls: "wide pen" + (Ink.tool === id ? " on" : ""), title: tip || "", onclick: () => { Ink.setTool(id); this.closePop(); if (tip) this.say(tip, 3500); } }, label);
    p.append(h("h4", {}, "أدوات الشرح والعرض"),
      t("laser", "🔴 مؤشر ليزر (L)", "مؤشر ليزر: بيرسم أثر بيختفي لوحده"),
      t("spot", "🔦 كشاف", "كشاف: كليك يحط الضوء، اسحب يعمل مستطيل، العجلة تكبّر الدايرة"),
      t("lens", "🔍 عدسة مكبرة", "عدسة: حرّك الماوس على الكلام — Ctrl+العجلة تغير التكبير"),
      t("curtain", "🎭 ستارة (كشف تدريجي)", "ستارة: اسحب مستطيل يغطي الإجابة — كليك عليه يكشف ربع ربع"),
      h("div", { cls: "row" },
        h("button", { onclick: () => { Ink.setTool("ruler"); this.closePop(); this.say("مسطرة: اسحبها، ولفها من الدايرة الزرقا أو بالعجلة — القلم بيمشي على حرفها"); } }, "📏 مسطرة"),
        h("button", { onclick: () => { Ink.setTool("protractor"); this.closePop(); } }, "📐 منقلة"),
        h("button", { onclick: () => { Ink.resetCurtains(); this.closePop(); } }, "↺ اقفل الستاير")));
  },
  colorPop(p) { p.append(...this.colorRows(Ink.currentColor(), c => Ink.setColor(c))); },
  morePop(p) {
    const chk = (label, val, fn, tip) => h("label", { cls: "wide", title: tip || "" }, h("input", { type: "checkbox", checked: !!val, onchange: e => { fn(e.target.checked); Ink.savePrefs(); } }), " " + label);
    p.append(h("h4", {}, "الكتابة"),
      chk("القلم بس يرسم (اللمس يحرك الصفحة)", Ink.penOnly, v => { Ink.penOnly = Ink.prefs.penOnly = v; Ink.setTool(Ink.tool); }, "للشاشات اللمس: كف إيدك مابيرسمش"),
      chk("تكملة الأشكال (ارسم دايرة بإيدك تبقى دايرة مظبوطة)", Ink.recognize, v => { Ink.recognize = Ink.prefs.recognize = v; }),
      h("h4", {}, "العرض"),
      h("div", { cls: "row" },
        h("button", { onclick: () => { this.closePop(); if (this.opts.onPresent) this.opts.onPresent(); } }, "🖥 وضع العرض (F5)"),
        h("button", { onclick: () => { Ink.flush(); this.closePop(); if (this.opts.onExport) this.opts.onExport(); } }, "💾 احفظ دلوقتي")),
      h("h4", {}, "الاختصارات (وأدوات الشرح مفتوحة)"),
      h("table", {}, h("tbody", {}, [["P", "قلم"], ["H", "تظليل"], ["E", "ممحاة"], ["S", "أشكال"], ["T", "نص"], ["V", "تحديد"], ["L", "ليزر"], ["Esc", "تحريك الكتاب / قفل"], ["Ctrl+Z / Y", "تراجع / إعادة"], ["Ctrl+C / X / V", "نسخ / قص / لصق"], ["Ctrl+D", "تكرار"], ["Delete", "مسح المتحدد"], ["Shift", "خط مستقيم / شكل مظبوط"], ["1…9", "ألوان سريعة"]].map(([k, v]) => h("tr", {}, h("td", {}, h("kbd", {}, k)), h("td", {}, v))))));
  },

  // ---------------------------------------------------------------- شريط التحديد
  showSelBar() {
    const sel = Ink.sel;
    if (!sel) { this.selbar.hidden = true; return; }
    const b = this.selbar; b.textContent = "";
    const its = Ink.selItems();
    b.append(...Ink.prefs.fav.slice(0, 6).map(c => h("button", { cls: "sw", style: { "--c": c }, title: "لوّن", onclick: () => Ink.recolorSel(c) })),
      h("button", { title: "لون تاني", onclick: () => { this.popFor = null; this.openPop("color", this.colorPop); } }, "🎨"),
      h("button", { title: "أتخن/أكبر", onclick: () => Ink.resizeSel(Math.min(40, (its[0].size || its[0].sw || 3) * 1.4)) }, "＋"),
      h("button", { title: "أرفع/أصغر", onclick: () => Ink.resizeSel(Math.max(0.5, (its[0].size || its[0].sw || 3) / 1.4)) }, "－"),
      h("button", { title: "قدّام (فوق الباقي)", onclick: () => Ink.orderSel(true) }, "⬆"),
      h("button", { title: "ورا", onclick: () => Ink.orderSel(false) }, "⬇"),
      h("button", { title: "تكرار (Ctrl+D)", onclick: () => Ink.duplicateSel() }, "⧉"),
      h("button", { title: "احفظه في «ملصقاتي»", onclick: () => { const n = prompt("اسم الملصق:", "ملصق " + (Ink.prefs.myStickers.length + 1)); if (n != null) { Ink.saveSelAsSticker(n.trim()); this.say("اتحفظ في «ملصقاتي» ⭐"); } } }, "⭐"),
      h("button", { title: "مسح (Delete)", onclick: () => Ink.deleteSel() }, "🗑"));
    b.hidden = false;
    this.placeSelBar();
  },
  placeSelBar() {
    const sel = Ink.sel; if (!sel) return;
    const box = Ink.surfaces.get(sel.key)?.ui.querySelector(".ink-selbox"); if (!box) { this.selbar.hidden = true; return; }
    const r = box.getBoundingClientRect(), w = this.selbar.offsetWidth;
    let y = r.top - 76; if (y < 4) y = r.bottom + 10;
    this.selbar.style.left = Math.max(4, Math.min(innerWidth - w - 4, r.left + r.width / 2 - w / 2)) + "px";
    this.selbar.style.top = Math.min(innerHeight - 40, y) + "px";
  },

  // ---------------------------------------------------------------- الكيبورد
  /** بيرجّع true لو الزرار اتنفذ هنا (مايتبعتش للقارئ ولا للبرنامج). */
  key(e) {
    if (!Ink.on) return false;
    const a = document.activeElement;
    // كتابة جوه نص/ملاحظة/قائمة الشرح: الزراير تفضل هناك (مابتروحش للقارئ ولا للبرنامج)
    if (a && (a.isContentEditable || /INPUT|TEXTAREA|SELECT/.test(a.tagName)))
      return !!(a.closest && a.closest(".ink-layer, .ink-pop, #inkbar, #inksel"));
    const k = e.key, ctrl = e.ctrlKey || e.metaKey, low = k.length === 1 ? k.toLowerCase() : k;
    const ok = () => { e.preventDefault(); e.stopPropagation(); return true; };
    if (ctrl && !e.altKey) {
      if (low === "z" && !e.shiftKey) { Ink.doUndo(); return ok(); }
      if (low === "y" || (low === "z" && e.shiftKey)) { Ink.doRedo(); return ok(); }
      if (low === "c" && Ink.sel) { Ink.copySel(false); this.say("اتنسخ"); return ok(); }
      if (low === "x" && Ink.sel) { Ink.copySel(true); return ok(); }
      if (low === "d" && Ink.sel) { Ink.duplicateSel(); return ok(); }
      if (low === "a" && Ink.tool === "select") { const s = Ink.visibleSurface(); if (s) Ink.setSel({ key: s.key, ids: new Set(Ink.doc.items(s.key).map(i => i.id)) }); return ok(); }
      // Ctrl+V بيتعامل معاه حدث اللصق (صورة أو اللي اتنسخ)
      return false;
    }
    if (e.altKey) return false;
    if ((k === "Delete" || k === "Backspace") && Ink.sel) { Ink.deleteSel(); return ok(); }
    if (k === "Escape") {
      if (!this.pop.hidden) { this.closePop(); return ok(); }
      if (Ink.sel) { Ink.setSel(null); return ok(); }
      if (document.body.classList.contains("present")) { if (this.opts.onPresent) this.opts.onPresent(false); return ok(); }
      if (Ink.tool !== "hand") { Ink.setTool("hand"); this.say("تحريك الكتاب — Esc تاني يقفل أدوات الشرح"); return ok(); }
      this.setOn(false); return ok();
    }
    if (k === "F5") { if (this.opts.onPresent) this.opts.onPresent(); return ok(); }
    if (TOOL_KEYS[low] && !e.shiftKey) {
      const t = TOOL_KEYS[low];
      if (t === "hl") this.tool("pen", "hl"); else if (t === "pen") this.tool("pen", Ink.pen === "hl" ? "pen" : Ink.pen); else this.tool(t);
      return ok();
    }
    if (/^[1-9]$/.test(k) && !e.shiftKey) { const c = (Ink.prefs.recent.length ? [...new Set([...Ink.prefs.recent, ...COLORS])] : COLORS)[+k - 1]; if (c) { Ink.setColor(c); this.say("اللون اتغير"); } return ok(); }
    return false;
  }
};
