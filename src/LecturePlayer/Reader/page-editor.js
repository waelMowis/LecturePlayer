// =====================================================================
//  محرر الصفحات (على «نسخة عمل» — الأصل مابيتلمسش):
//  ترتيب بالسحب، لف، تكرار، مسح، صفحات سبورة (فاضية/مربعات/سطور/نقط)، صفحة صورة، ضم PDF تاني،
//  استخراج صفحات، أرقام صفحات، ختم/علامة مائية، وتثبيت الشرح في الملف.
//  الشرح بيمشي مع صفحته (Ink.remapPages).
// =====================================================================
import { Ink, renderSvg, W } from "./ink.js";
import { runJob, pagePlan } from "./ink-export.js";

const h = (tag, attrs = {}, ...kids) => {
  const e = document.createElement(tag);
  for (const k in attrs) {
    const v = attrs[k];
    if (v == null || v === false) continue;
    if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (k === "cls") e.className = v;
    else if (k === "style" && typeof v === "object") for (const sk in v) { if (sk.startsWith("--")) e.style.setProperty(sk, v[sk]); else e.style[sk] = v[sk]; }
    else e.setAttribute(k, v === true ? "" : v);
  }
  for (const c of kids.flat()) if (c != null && c !== false) e.append(c.nodeType ? c : document.createTextNode(String(c)));
  return e;
};
const BOARDS = [["blank", "فارغة"], ["grid", "مربعات"], ["lined", "سطور"], ["dots", "نقاط"]];
let uidN = 0;
const nid = () => "e" + (++uidN);

export const PageEditor = {
  root: null, entries: [], sel: new Set(), anchor: null, doc: null, ctx: null, merges: {}, mergeDocs: {}, dirty: false, busy: false, io: null,

  /** ctx: {doc (PDF.js), src (رابط الملف), title, post, pick(kind,name)→Promise<url|null>, pdfjsLib, docOpts, onSaved(), onClose()} */
  async open(ctx) {
    this.ctx = ctx; this.doc = ctx.doc; this.merges = {}; this.mergeDocs = {}; this.sel = new Set(); this.dirty = false;
    this.aspects = []; this.sizes = [];
    for (let i = 1; i <= this.doc.numPages; i++) {
      const v = (await this.doc.getPage(i)).getViewport({ scale: 1 });
      this.aspects.push(v.height / v.width); this.sizes.push([v.width, v.height]);
    }
    this.entries = this.sizes.map((s, i) => ({ id: nid(), kind: "src", index: i, rot: 0 }));
    this.build();
    document.body.classList.add("pe-on");
  },
  close(force) {
    if (!force && this.dirty && !confirm("توجد تعديلات غير محفوظة — هل تغلق دون حفظ؟")) return;
    if (this.io) this.io.disconnect();
    this.root?.remove(); this.root = null;
    document.body.classList.remove("pe-on");
    for (const t of Object.values(this.mergeTasks || {})) { try { t.destroy(); } catch (x) { } }
    this.mergeTasks = {};
    this.ctx?.onClose && this.ctx.onClose();
  },
  setDirty() { this.dirty = true; this.updateBar(); },

  // ---------------------------------------------------------------- الشكل
  build() {
    this.root?.remove();
    const btn = (id, label, title, fn) => h("button", { "data-a": id, title, onclick: e => { e.stopPropagation(); fn(e); } }, label);
    this.bar = h("div", { cls: "pe-bar" },
      h("span", { cls: "pe-title" }, "📑 محرر الصفحات — " + (this.ctx.title || "")),
      h("span", { cls: "pe-count" }),
      h("span", { cls: "pe-sp" }),
      btn("board", "➕ صفحة سبورة ▾", "صفحة جديدة بعد المختارة (فارغة/مربعات/سطور/نقاط)", e => this.menu(e.currentTarget, BOARDS.map(([k, l]) => [l, () => this.insertBoard(k)]))),
      btn("image", "🖼 صفحة صورة", "صورة على صفحة مستقلة", () => this.pickImage()),
      btn("merge", "📎 ضم PDF", "أضف صفحات ملف PDF آخر", () => this.addPdf()),
      btn("rotl", "↺", "تدوير لليسار", () => this.rotate(-90)),
      btn("rotr", "↻", "تدوير لليمين", () => this.rotate(90)),
      btn("dup", "⧉ تكرار", "كرّر الصفحات المختارة", () => this.duplicate()),
      btn("del", "🗑 مسح", "امسح الصفحات المختارة (Delete)", () => this.remove()),
      btn("extract", "📤 استخرج", "احفظ الصفحات المختارة في ملف مستقل", () => this.extract()),
      btn("numbers", "🔢 أرقام", "ترقيم الصفحات", e => this.numbersDialog(e.currentTarget)),
      btn("stamp", "🏷 ختم", "ختم أو علامة مائية على الصفحات", e => this.stampDialog(e.currentTarget)),
      btn("flatten", "🧷 ثبّت الشرح", "يصبح الشرح جزءًا من الملف نفسه (ويُمسح من طبقة الشرح)", () => this.flatten()),
      h("span", { cls: "pe-sp" }),
      btn("save", "💾 احفظ", "احفظ التعديلات في نسخة العمل (Ctrl+S)", () => this.save()),
      btn("close", "✕", "إغلاق (Esc)", () => this.close()));
    this.grid = h("div", { cls: "pe-grid", style: { direction: this.ctx.rtl ? "rtl" : "ltr" }, onclick: e => { if (e.target === this.grid) { this.sel.clear(); this.paint(); } } });
    this.status = h("div", { cls: "pe-status", hidden: true });
    this.root = h("div", { id: "pageEditor", tabindex: "0", onkeydown: e => this.key(e) }, this.bar, this.grid, this.status);
    document.body.appendChild(this.root);
    this.io = new IntersectionObserver(es => { for (const en of es) if (en.isIntersecting) this.thumb(en.target); }, { root: this.grid, rootMargin: "300px" });
    this.renderGrid();
    this.root.focus();
  },
  updateBar() {
    const n = this.sel.size;
    this.bar.querySelector(".pe-count").textContent = `${this.entries.length} صفحة` + (n ? ` · مختار ${n}` : "") + (this.dirty ? " · ✎ غير محفوظ" : "");
    for (const a of ["rotl", "rotr", "dup", "del", "extract"]) this.bar.querySelector(`[data-a="${a}"]`).disabled = !n;
    this.bar.querySelector('[data-a="save"]').classList.toggle("on", this.dirty);
  },
  renderGrid() {
    if (this.io) this.io.disconnect();
    this.grid.textContent = "";
    this.entries.forEach((e, i) => {
      const card = h("div", { cls: "pe-card", draggable: "true", "data-id": e.id },
        h("div", { cls: "pe-thumb" }), h("div", { cls: "pe-num" }, String(i + 1) + this.label(e)));
      card.addEventListener("click", ev => this.click(ev, e.id));
      card.addEventListener("dblclick", () => { if (e.kind === "src" && !this.dirty) { this.close(true); this.ctx.goTo && this.ctx.goTo(e.index + 1); } });
      card.addEventListener("dragstart", ev => { if (!this.sel.has(e.id)) { this.sel = new Set([e.id]); this.paint(); } ev.dataTransfer.effectAllowed = "move"; ev.dataTransfer.setData("text/plain", e.id); this.dragging = true; });
      card.addEventListener("dragend", () => { this.dragging = false; this.grid.querySelectorAll(".drop-l,.drop-r").forEach(x => x.classList.remove("drop-l", "drop-r")); });
      card.addEventListener("dragover", ev => {
        ev.preventDefault();
        const r = card.getBoundingClientRect(), right = ev.clientX > r.left + r.width / 2;
        this.grid.querySelectorAll(".drop-l,.drop-r").forEach(x => x.classList.remove("drop-l", "drop-r"));
        card.classList.add(right ? "drop-r" : "drop-l");
      });
      card.addEventListener("drop", ev => {
        ev.preventDefault();
        const r = card.getBoundingClientRect(), right = ev.clientX > r.left + r.width / 2;
        this.moveSelection(e.id, this.ctx.rtl ? !right : right); // في الكتاب العربي «بعد» = على الشمال
      });
      this.grid.appendChild(card);
      this.io.observe(card);
    });
    this.paint();
  },
  label(e) { return e.kind === "board" ? " · سبورة" : e.kind === "image" ? " · صورة" : e.kind === "merge" ? " · مضاف" : (e.index + 1 !== this.entries.indexOf(e) + 1 ? ` (كانت ${e.index + 1})` : ""); },
  paint() {
    for (const c of this.grid.children) c.classList.toggle("sel", this.sel.has(c.dataset.id));
    this.updateBar();
  },
  click(ev, id) {
    const ids = this.entries.map(e => e.id);
    if (ev.shiftKey && this.anchor) {
      const a = ids.indexOf(this.anchor), b = ids.indexOf(id);
      this.sel = new Set(ids.slice(Math.min(a, b), Math.max(a, b) + 1));
    } else if (ev.ctrlKey || ev.metaKey) { if (this.sel.has(id)) this.sel.delete(id); else this.sel.add(id); this.anchor = id; }
    else { this.sel = new Set([id]); this.anchor = id; }
    this.paint();
  },
  key(e) {
    if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); this.close(); }
    else if (e.key === "Delete") { e.preventDefault(); e.stopPropagation(); this.remove(); }
    else if ((e.ctrlKey || e.metaKey) && (e.key === "s" || e.key === "S")) { e.preventDefault(); e.stopPropagation(); this.save(); }
    else if ((e.ctrlKey || e.metaKey) && (e.key === "a" || e.key === "A")) { e.preventDefault(); e.stopPropagation(); this.sel = new Set(this.entries.map(x => x.id)); this.paint(); }
    else e.stopPropagation(); // مايوصلش لاختصارات القارئ
  },

  /** مصغّرة: صفحة الكتاب (بلفّتها) + الشرح عليها، أو شكل السبورة/الصورة. */
  async thumb(card) {
    if (card.dataset.done) return;
    card.dataset.done = 1;
    const e = this.entries.find(x => x.id === card.dataset.id); if (!e) return;
    const box = card.querySelector(".pe-thumb");
    try {
      if (e.kind === "src" || e.kind === "merge") {
        const d = e.kind === "src" ? this.doc : this.mergeDocs[e.key];
        const page = await d.getPage(e.index + 1);
        const v0 = page.getViewport({ scale: 1 });
        const vp = page.getViewport({ scale: 150 / Math.max(v0.width, v0.height) * 1.5 });
        const c = h("canvas"); c.width = Math.ceil(vp.width); c.height = Math.ceil(vp.height);
        await page.render({ canvasContext: c.getContext("2d"), viewport: vp }).promise;
        const wrap = h("div", { cls: "pe-page", style: { transform: `rotate(${e.rot || 0}deg)` } }, c);
        if (e.kind === "src") {
          const items = Ink.doc.items(String(e.index + 1));
          if (items.length) {
            const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
            svg.setAttribute("viewBox", `0 0 ${W} ${W * v0.height / v0.width}`); svg.setAttribute("class", "pe-ink");
            for (const it of items) if (it.ly !== "t") svg.appendChild(renderSvg(it));
            wrap.appendChild(svg);
            card.querySelector(".pe-num").append(" 🖊");
          }
        }
        box.appendChild(wrap);
      } else if (e.kind === "board") {
        box.appendChild(h("div", { cls: "pe-page pe-board pe-" + e.board, style: { aspectRatio: `${e.size[0]} / ${e.size[1]}` } }));
      } else if (e.kind === "image") {
        const url = URL.createObjectURL(new Blob([e.bytes], { type: e.type === "jpg" ? "image/jpeg" : "image/png" }));
        box.appendChild(h("div", { cls: "pe-page pe-board", style: { aspectRatio: `${e.size[0]} / ${e.size[1]}` } }, h("img", { src: url })));
      }
    } catch (x) { box.textContent = "⚠"; }
  },

  // ---------------------------------------------------------------- العمليات
  selIdx() { return this.entries.map((e, i) => this.sel.has(e.id) ? i : -1).filter(i => i >= 0); },
  insertAt() { const s = this.selIdx(); return s.length ? s[s.length - 1] + 1 : this.entries.length; },
  sizeNear(i) {
    for (let j = Math.min(i, this.entries.length - 1); j >= 0; j--) { const e = this.entries[j]; if (e && e.kind === "src") { const s = this.sizes[e.index]; return (e.rot / 90) % 2 ? [s[1], s[0]] : s.slice(); } if (e && e.size) return e.size.slice(); }
    return this.sizes[0] ? this.sizes[0].slice() : [595, 842];
  },
  add(list, at) {
    this.entries.splice(at, 0, ...list);
    this.sel = new Set(list.map(x => x.id)); this.anchor = list[0]?.id;
    this.setDirty(); this.renderGrid();
    this.grid.querySelector(`[data-id="${list[0].id}"]`)?.scrollIntoView({ block: "nearest" });
  },
  insertBoard(kind) { const at = this.insertAt(); this.add([{ id: nid(), kind: "board", board: kind, size: this.sizeNear(at - 1) }], at); },
  pickImage() {
    const f = h("input", { type: "file", accept: "image/png,image/jpeg", onchange: async () => {
      const file = f.files[0]; if (!file) return;
      const bytes = new Uint8Array(await file.arrayBuffer());
      const at = this.insertAt();
      this.add([{ id: nid(), kind: "image", bytes, type: /jpe?g/i.test(file.type) ? "jpg" : "png", size: this.sizeNear(at - 1) }], at);
    } });
    f.click();
  },
  async addPdf(url, name) {
    url = url || await this.ctx.pick("open-pdf");
    if (!url) return;
    const key = "m" + Object.keys(this.merges).length;
    const task = this.ctx.pdfjsLib.getDocument({ ...this.ctx.docOpts, url });
    const d = await task.promise;
    (this.mergeTasks ||= {})[key] = task;
    this.merges[key] = url; this.mergeDocs[key] = d;
    const list = [];
    for (let i = 0; i < d.numPages; i++) list.push({ id: nid(), kind: "merge", key, index: i, rot: 0 });
    this.add(list, this.insertAt());
    this.say(`أُضيفت ${d.numPages} صفحة`);
  },
  rotate(deg) {
    for (const e of this.entries) if (this.sel.has(e.id) && (e.kind === "src" || e.kind === "merge")) e.rot = (((e.rot || 0) + deg) % 360 + 360) % 360;
    for (const e of this.entries) if (this.sel.has(e.id) && e.size && e.kind !== "src") e.size = [e.size[1], e.size[0]];
    this.setDirty(); this.renderGrid();
  },
  duplicate() {
    const idx = this.selIdx(); if (!idx.length) return;
    const copies = idx.map(i => ({ ...this.entries[i], id: nid() }));
    this.add(copies, idx[idx.length - 1] + 1);
  },
  remove() {
    const n = this.sel.size; if (!n) return;
    if (n === this.entries.length) { alert("لا يمكن مسح كل الصفحات"); return; }
    this.entries = this.entries.filter(e => !this.sel.has(e.id));
    this.sel.clear(); this.setDirty(); this.renderGrid();
    this.say(`مُسحت ${n} صفحة — يمكنك الإغلاق دون حفظ إن غيّرت رأيك`);
  },
  moveSelection(targetId, after) {
    if (this.sel.has(targetId)) return;
    const moving = this.entries.filter(e => this.sel.has(e.id));
    const rest = this.entries.filter(e => !this.sel.has(e.id));
    let at = rest.findIndex(e => e.id === targetId) + (after ? 1 : 0);
    rest.splice(at, 0, ...moving);
    this.entries = rest;
    this.setDirty(); this.renderGrid();
  },
  /** للفحص: ترتيب جديد بأرقام الصفحات (من 1). */
  order(list) { const byIdx = this.entries.slice(); this.entries = list.map(n => byIdx[n - 1]); this.setDirty(); this.renderGrid(); },

  composePages(entries) {
    return entries.map(e => e.kind === "src" ? { doc: "src", index: e.index, rot: e.rot || 0 }
      : e.kind === "merge" ? { doc: e.key, index: e.index, rot: e.rot || 0 }
      : e.kind === "board" ? { board: e.board, size: e.size }
      : { image: e.bytes, type: e.type, size: e.size });
  },
  say(t, ms = 3000) { this.status.textContent = t; this.status.hidden = false; clearTimeout(this._st); this._st = setTimeout(() => this.status.hidden = true, ms); },
  async run(label, fn) {
    if (this.busy) return null;
    this.busy = true; this.root.classList.add("busy"); this.say(label + "…", 600000);
    try { return await fn(); }
    catch (e) { this.say("⚠ " + ((e && e.message) || e), 8000); return null; }
    finally { this.busy = false; this.root?.classList.remove("busy"); }
  },

  /** حفظ في نسخة العمل نفسها + الشرح يمشي مع صفحاته. */
  async save(extraOps = []) {
    if (!this.dirty && !extraOps.length) { this.say("لا توجد تعديلات"); return; }
    const save = await this.ctx.pick("save-self");
    if (!save) { this.say("الحفظ متاح لنسخة العمل فقط"); return; }
    const entries = this.entries.slice();
    const r = await this.run("جارٍ الحفظ", () => runJob({ job: "edit", src: this.ctx.src, save, ops: [{ op: "compose", pages: this.composePages(entries), merges: this.merges }, ...extraOps] },
      p => this.say(`جارٍ الحفظ… ${Math.round(p * 100)}%`, 600000)));
    if (!r) return;
    Ink.remapPages(entries, this.aspects);
    Ink.flush();
    // البرنامج ينقل العلامات والتظليل والمقروء: لكل صفحة جديدة [الصفحة القديمة، اللفة] (0 = صفحة جديدة)
    this.ctx.onMap && this.ctx.onMap(entries.map(e => e.kind === "src" ? [e.index + 1, e.rot || 0] : [0, 0]));
    this.dirty = false;
    this.say("حُفظ ✓");
    this.close(true);
    this.ctx.onSaved && this.ctx.onSaved(r);
  },
  async extract() {
    const idx = this.selIdx(); if (!idx.length) return;
    const save = await this.ctx.pick("save-pdf", `${this.ctx.title || "صفحات"} — ${idx.length} صفحة.pdf`);
    if (!save) return;
    const entries = idx.map(i => this.entries[i]);
    const r = await this.run("جارٍ الاستخراج", () => runJob({ job: "edit", src: this.ctx.src, save, ops: [{ op: "compose", pages: this.composePages(entries), merges: this.merges }] }));
    if (r) this.say(`حُفظت ${r.pages} صفحة ✓`);
  },

  numbersDialog(anchor) {
    const pos = h("select", {}, [["center", "في المنتصف"], ["right", "يمين"], ["left", "يسار"]].map(([v, l]) => h("option", { value: v }, l)));
    const fmt = h("select", {}, [["n", "1، 2، 3"], ["of", "1 / 20"], ["ar", "صفحة ١ من ٢٠"]].map(([v, l]) => h("option", { value: v }, l)));
    const start = h("input", { type: "number", value: 1, min: 0, style: { width: "70px" } });
    const skip = h("input", { type: "checkbox" });
    this.dialog(anchor, "ترقيم الصفحات", [h("label", {}, "الشكل ", fmt), h("label", {}, "المكان ", pos), h("label", {}, "يبدأ من ", start), h("label", {}, skip, " بدون رقم على الصفحة الأولى (الغلاف)")],
      async () => {
        const total = this.entries.length, st = +start.value || 1, first = skip.checked;
        const op = { op: "numbers", pos: pos.value, format: fmt.value, start: st, skipFirst: first };
        if (fmt.value === "ar") {
          // الأرقام العربي بتترسم صور (خطوط الـ PDF القياسية مافيهاش عربي)
          const last = st + total - 1 - (first ? 1 : 0), ar = n => String(n).replace(/\d/g, d => "٠١٢٣٤٥٦٧٨٩"[d]);
          op.images = {};
          for (let i = 0; i < total; i++) if (!(first && i === 0)) op.images[i] = await textPng(`صفحة ${ar(st + i - (first ? 1 : 0))} من ${ar(last)}`, "#555", 40);
        }
        await this.save([op]);
      });
  },
  stampDialog(anchor) {
    const text = h("input", { type: "text", value: "سري", maxlength: 40 });
    const color = h("input", { type: "color", value: "#e03131" });
    const mode = h("select", {}, [["diag", "علامة مائية مائلة في المنتصف"], ["corner", "ختم في الزاوية العلوية"]].map(([v, l]) => h("option", { value: v }, l)));
    const which = h("select", {}, [["all", "كل الصفحات"], ["sel", "الصفحات المختارة فقط"]].map(([v, l]) => h("option", { value: v }, l)));
    this.dialog(anchor, "ختم / علامة مائية", [h("label", {}, "النص ", text), h("label", {}, "اللون ", color), h("label", {}, "الشكل ", mode), h("label", {}, "على ", which)],
      async () => {
        const t = text.value.trim(); if (!t) return;
        const pages = which.value === "sel" ? this.selIdx() : this.entries.map((_, i) => i);
        if (!pages.length) return;
        await this.save([{ op: "stamp", png: await textPng(t, color.value, 120, mode.value !== "diag"), pages, diagonal: mode.value === "diag", opacity: mode.value === "diag" ? 0.16 : 0.85 }]);
      });
  },
  dialog(anchor, title, rows, ok) {
    document.querySelector(".pe-dialog")?.remove();
    const d = h("div", { cls: "pe-dialog" }, h("h4", {}, title), ...rows.map(r => h("div", { cls: "row" }, r)),
      h("div", { cls: "row" }, h("button", { cls: "on", onclick: async () => { d.remove(); await ok(); } }, "حسنًا (ويُحفظ)"), h("button", { onclick: () => d.remove() }, "إلغاء")));
    document.body.appendChild(d);
    const r = anchor.getBoundingClientRect();
    d.style.top = (r.bottom + 6) + "px"; d.style.left = Math.max(6, Math.min(innerWidth - d.offsetWidth - 6, r.left)) + "px";
  },
  menu(anchor, items) {
    document.querySelector(".pe-menu")?.remove();
    const m = h("div", { cls: "pe-menu" }, items.map(([l, fn]) => h("button", { onclick: () => { m.remove(); fn(); } }, l)));
    document.body.appendChild(m);
    const r = anchor.getBoundingClientRect();
    m.style.top = (r.bottom + 4) + "px"; m.style.left = r.left + "px";
    setTimeout(() => document.addEventListener("pointerdown", function off(e) { if (!m.contains(e.target)) { m.remove(); document.removeEventListener("pointerdown", off, true); } }, true), 0);
  },

  /** الشرح يتطبع في الملف (whiteout + كلام يبقى جزء من الصفحة) وطبقة الشرح تفضى. */
  async flatten() {
    if (this.dirty) { this.say("احفظ ترتيب الصفحات أولًا"); return; }
    const pages = [...Ink.doc.pages.entries()].filter(([, v]) => v.some(i => i.ly !== "t"));
    if (!pages.length) { this.say("لا يوجد شرح على الصفحات"); return; }
    if (!confirm("سيُطبع الشرح داخل نسخة العمل نفسها ولن يكون قابلًا للتعديل (لن يُمس الأصل). هل نتابع؟")) return;
    const save = await this.ctx.pick("save-self");
    if (!save) { this.say("متاح لنسخة العمل فقط"); return; }
    const r = await this.run("جارٍ تثبيت الشرح", async () => {
      const plans = [];
      for (const [k, items] of pages) {
        const n = +k; if (!(n >= 1 && n <= this.doc.numPages)) continue;
        const vp = (await this.doc.getPage(n)).getViewport({ scale: 1 });
        plans.push({ index: n - 1, ...(await pagePlan(items, vp)) });
      }
      return runJob({ job: "edit", src: this.ctx.src, save, ops: [{ op: "flatten", plans }] });
    });
    if (!r) return;
    for (const [k] of pages) Ink.clearPage(k, "all");
    Ink.flush();
    this.close(true);
    this.ctx.onSaved && this.ctx.onSaved(r);
  }
};

/** كلام ← PNG (للأرقام العربي والأختام). */
async function textPng(text, color, size, box) {
  const c = document.createElement("canvas"), x = c.getContext("2d");
  const font = `700 ${size}px "Segoe UI", Tahoma, sans-serif`;
  x.font = font; x.direction = "rtl";
  const w = Math.ceil(x.measureText(text).width) + size, hgt = Math.ceil(size * 1.6);
  c.width = w; c.height = hgt;
  x.font = font; x.direction = "rtl"; x.textAlign = "center"; x.textBaseline = "middle"; x.fillStyle = color;
  if (box) { x.strokeStyle = color; x.lineWidth = size * 0.08; x.beginPath(); x.roundRect(size * 0.1, size * 0.1, w - size * 0.2, hgt - size * 0.2, size * 0.25); x.stroke(); }
  x.fillText(text, w / 2, hgt / 2);
  const b = await new Promise(r => c.toBlob(r, "image/png"));
  return new Uint8Array(await b.arrayBuffer());
}
