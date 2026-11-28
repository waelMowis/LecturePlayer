// =====================================================================
//  السبورة البيضاء: كشكول صفحات (مقاسات وخلفيات) والرسم عليها بنفس محرك الشرح.
//  الشرح متخزّن برقم الصفحة الثابت (id) — ترتيب الصفحات مابيحركش الرسم.
//  التصدير PDF فيكتور بنفس طريق «النسخة المشروحة».
// =====================================================================
import { Ink, InkDoc, renderSvg, W, onDark, adaptDark } from "./ink.js";
import { runJob, pagePlan, pagePng } from "./ink-export.js";
import { Sci } from "./sci.js";

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
const pid = () => "p" + Math.random().toString(36).slice(2, 8) + Date.now().toString(36).slice(-4);
const iid = () => Math.random().toString(36).slice(2, 9) + Date.now().toString(36).slice(-5);

export const SIZES = { a4p: [595, 842, "A4 طولي"], a4l: [842, 595, "A4 عرضي"], wide: [960, 540, "شاشة 16:9"], long: [595, 2520, "صفحة طويلة"] };
export const BGS = { white: ["#ffffff", "أبيض"], cream: ["#fdf6e3", "ورق"], black: ["#1e1e1e", "أسود"], green: ["#1f3d2b", "أخضر (طباشير)"] };
export const PATS = { none: "فارغ", grid: "مربعات", lines: "سطور", dots: "نقاط" };
export const isDark = bg => bg === "black" || bg === "green";
/** ألوان مقاطع الدفتر. */
export const SEC_COLORS = [["#1971c2", "أزرق"], ["#e03131", "أحمر"], ["#2f9e44", "أخضر"], ["#f08c00", "برتقالي"], ["#7048e8", "بنفسجي"], ["#0c8599", "فيروزي"], ["#d6336c", "وردي"], ["#5c940d", "زيتي"]];
const sid = () => "s" + Math.random().toString(36).slice(2, 8) + Date.now().toString(36).slice(-3);
const fmtDate = d => { try { return d ? new Date(d).toLocaleDateString("ar-EG", { weekday: "long", day: "numeric", month: "long", year: "numeric" }) : ""; } catch (e) { return ""; } };

export const Board = {
  ready: false, meta: null, area: null, scroller: null, pagesEl: null, side: null, k: 1, post: () => { }, onMoved: null, pick: null, docOpts: null, pdfjsLib: null,

  init(o) {
    Object.assign(this, o);
    this.area = o.area;
    this.area.textContent = "";
    this.side = h("div", { cls: "bd-side" });
    this.scroller = h("div", { cls: "bd-scroll", tabindex: "0" });
    this.pagesEl = h("div", { cls: "bd-pages" });
    this.scroller.appendChild(this.pagesEl);
    this.tools = h("div", { cls: "bd-tools" });
    this.area.append(this.tools, h("div", { cls: "bd-body" }, this.side, this.scroller));
    this.scroller.addEventListener("scroll", () => { this.trackPage(); }, { passive: true });
    this.scroller.addEventListener("wheel", e => {
      if (!e.ctrlKey) return;
      e.preventDefault();
      this.zoomAt(e.deltaY < 0 ? 1.12 : 1 / 1.12, e.clientX, e.clientY);
    }, { passive: false });
    // تحريك بزرار الماوس الوسطاني أو بالمسافة + السحب
    let pan = null, space = false;
    window.addEventListener("keydown", e => { if (e.code === "Space" && this.ready && !e.repeat && !/INPUT|TEXTAREA/.test(document.activeElement?.tagName) && !document.activeElement?.isContentEditable) space = true; });
    window.addEventListener("keyup", e => { if (e.code === "Space") space = false; });
    this.scroller.addEventListener("pointerdown", e => {
      if (!(e.button === 1 || (space && e.button === 0))) return;
      e.preventDefault(); e.stopPropagation();
      pan = { x: e.clientX, y: e.clientY, l: this.scroller.scrollLeft, t: this.scroller.scrollTop };
      this.scroller.setPointerCapture(e.pointerId); this.scroller.classList.add("panning");
    }, true);
    this.scroller.addEventListener("pointermove", e => { if (pan) { this.scroller.scrollLeft = pan.l - (e.clientX - pan.x); this.scroller.scrollTop = pan.t - (e.clientY - pan.y); } });
    this.scroller.addEventListener("pointerup", () => { pan = null; this.scroller.classList.remove("panning"); });
    window.addEventListener("resize", () => { if (this.ready) this.layout(); });
  },

  // ---------------------------------------------------------------- فتح وقفل
  open(o) {
    const m = typeof o.board === "string" ? JSON.parse(o.board) : (o.board || {});
    if (!Array.isArray(m.pages) || !m.pages.length) m.pages = [{ id: pid(), w: 842, h: 595, bg: "white", pat: "none", name: "" }];
    if (m.kind === "notebook") {
      // الدفتر: كل صفحة في مقطع، وكل مقطع فيه صفحة على الأقل
      if (!Array.isArray(m.sections) || !m.sections.length) m.sections = [{ id: sid(), name: "المقطع الأول", color: SEC_COLORS[0][0] }];
      const ids = new Set(m.sections.map(x => x.id));
      for (const p of m.pages) if (!ids.has(p.sec)) p.sec = m.sections[0].id;
      for (const sec of m.sections) if (!m.pages.some(p => p.sec === sec.id)) m.pages.push({ id: pid(), w: 595, h: 842, bg: "white", pat: "lines", name: "", sec: sec.id, d: Date.now() });
      if (!ids.has(m.sec)) m.sec = m.sections[0].id;
    }
    this.meta = m;
    this.k = o.zoom || 1;
    this.cur = Math.min(Math.max(1, o.page || 1), m.pages.length);
    this.ready = true;
    Sci.init(this);
    this.buildTools();
    this.render();
    requestAnimationFrame(() => this.goTo(this.cur, true));
    return { pages: m.pages.length, title: m.title || o.title || "" };
  },
  /** يبعت التغييرات للبرنامج دلوقتي (قبل القفل). */
  flush() { if (this._save) { clearTimeout(this._save); this._save = 0; this.post({ t: "board", board: this.meta }); } },
  close() { this.flush(); this.ready = false; this.pagesEl.textContent = ""; this.side.textContent = ""; Sci.closeEditor(); Sci.close(); },
  /** سطح الصفحة الحالية (للأدوات العلمية). */
  curSurface() { const p = this.curPage(); return p ? Ink.surfaces.get(p.id) || Ink.visibleSurface() : Ink.visibleSurface(); },
  onSciPanel(on) { this.tools.querySelector('[title^="معادلات رياضية"]')?.classList.toggle("on", on); },
  /** دفتر (مقاطع وصفحات A4) ولا سبورة عادية. */
  nb() { return this.meta?.kind === "notebook"; },
  /** الصفحات الظاهرة: كل صفحات السبورة، أو صفحات المقطع الحالي في الدفتر. */
  vis() { return !this.meta ? [] : this.nb() ? this.meta.pages.filter(p => p.sec === this.meta.sec) : this.meta.pages; },
  section() { return this.nb() ? this.meta.sections.find(x => x.id === this.meta.sec) : null; },
  get total() { return this.meta ? this.vis().length : 0; },
  page() { return this.cur || 1; },
  pageOf(id) { return this.vis().findIndex(p => p.id === id); },
  curPage() { return this.vis()[this.cur - 1]; },
  /** إدراج صفحة قبل الصفحة الظاهرة رقم i (أو في آخر المقطع). */
  insertVis(i, p) {
    const V = this.vis(), all = this.meta.pages;
    const fi = i < V.length ? all.indexOf(V[i]) : V.length ? all.indexOf(V[V.length - 1]) + 1 : all.length;
    all.splice(fi, 0, p);
  },

  // ---------------------------------------------------------------- الرسم
  // الدفتر: صفحة A4 بعرض مريح (زي الكراسة) مش بعرض الشاشة كلها
  fitW() { const w = Math.max(200, this.scroller.clientWidth - 48); return this.nb() ? Math.min(w, 880) : w; },
  pageWidthPx(p) {
    // كل الصفحات بنفس مقياس النقط (صفحة عرضية أعرض من الطولية) — والتكبير k فوقها
    const maxW = Math.max(...this.vis().map(x => x.w));
    return this.fitW() * (p.w / maxW) * this.k;
  },
  render() {
    this.pagesEl.textContent = "";
    const V = this.vis();
    if (this.nb()) {
      // صفحات المقاطع التانية مش معروضة: نفك طبقات رسمها
      const ids = new Set(V.map(p => p.id));
      for (const k of [...Ink.surfaces.keys()]) if (!ids.has(k)) Ink.detach(k);
    }
    V.forEach((p, i) => {
      const div = h("div", { cls: "bd-page" + (this.nb() ? " nb" : ""), "data-id": p.id, "data-n": i + 1 });
      if (this.nb()) { div.dataset.title = p.name || ""; div.dataset.date = fmtDate(p.d); }
      this.pagesEl.appendChild(div);
      this.style(div, p);
    });
    this.layout();
    this.renderSide();
  },
  style(div, p) {
    const [color] = BGS[p.bg] || BGS.white, dark = isDark(p.bg);
    const line = dark ? "rgba(255,255,255,.16)" : "rgba(120,130,150,.28)";
    // النقشة بوحدات النقط (20 نقطة للمربع، 24 للسطر) × عرض الصفحة الحالي (--bw) — فبتكبر مع التكبير
    const cell = `calc(var(--bw) * ${(20 / p.w).toFixed(5)})`, row = `calc(var(--bw) * ${(24 / p.w).toFixed(5)})`;
    const pats = {
      grid: [`linear-gradient(${line} 1px, transparent 1px), linear-gradient(90deg, ${line} 1px, transparent 1px)`, `${cell} ${cell}`],
      lines: [`linear-gradient(transparent calc(100% - 1px), ${line} 1px)`, `100% ${row}`],
      dots: [`radial-gradient(circle, ${dark ? "rgba(255,255,255,.35)" : "rgba(110,120,140,.55)"} 1.2px, transparent 1.6px)`, `${cell} ${cell}`]
    };
    let [img, size] = pats[p.pat] || ["none", "auto"];
    if (this.nb && this.nb()) {
      // هامش الكراسة: خط أحمر على اليمين، والعنوان فوق من غير سطور
      const mg = `calc(var(--bw) * 0.085)`, red = dark ? "rgba(255,135,135,.5)" : "rgba(224,49,49,.45)";
      const margin = `linear-gradient(90deg, transparent calc(100% - ${mg} - 1px), ${red} calc(100% - ${mg} - 1px), ${red} calc(100% - ${mg} + 1px), transparent calc(100% - ${mg} + 1px))`;
      img = img === "none" ? margin : `${margin}, ${img}`;
      size = img.includes(",") && size !== "auto" ? `100% 100%, ${size}` : "100% 100%";
    }
    div.style.backgroundColor = color;
    div.style.backgroundImage = img;
    div.style.backgroundSize = size;
    div.classList.toggle("dark", dark);
  },
  layout() {
    if (!this.meta) return;
    for (const div of this.pagesEl.children) {
      const p = this.meta.pages.find(x => x.id === div.dataset.id);
      if (!p) continue;
      const w = this.pageWidthPx(p);
      div.style.width = w + "px"; div.style.height = (w * p.h / p.w) + "px";
      div.style.setProperty("--bw", w + "px");
      Ink.attach(div, p.id, p.h / p.w, { dark: isDark(p.bg) });
    }
    Ink.grid = 20 * W / (this.curPage()?.w || 842);
  },
  zoomAt(f, cx, cy) {
    const old = this.k, k = Math.max(0.25, Math.min(5, old * f));
    if (Math.abs(k - old) < 1e-3) return;
    const r = this.scroller.getBoundingClientRect();
    const x = (cx ?? r.left + r.width / 2) - r.left + this.scroller.scrollLeft, y = (cy ?? r.top + r.height / 2) - r.top + this.scroller.scrollTop;
    this.k = k; this.layout();
    this.scroller.scrollLeft = x * k / old - ((cx ?? r.left + r.width / 2) - r.left);
    this.scroller.scrollTop = y * k / old - ((cy ?? r.top + r.height / 2) - r.top);
    this.moved();
  },
  zoom(dir) { if (dir === 0) { this.k = 1; this.layout(); this.goTo(this.cur, true); this.moved(); } else this.zoomAt(dir > 0 ? 1.2 : 1 / 1.2); },
  goTo(n, instant) {
    n = Math.min(Math.max(1, Math.round(n)), this.total);
    const div = this.pagesEl.children[n - 1]; if (!div) return;
    this.scroller.scrollTo({ top: div.offsetTop - 16, behavior: instant ? "auto" : "smooth" });
    this.cur = n; this.moved();
  },
  step(d) { this.goTo(this.cur + d); },
  trackPage() {
    const mid = this.scroller.scrollTop + this.scroller.clientHeight * 0.4;
    let n = 1;
    for (const div of this.pagesEl.children) { if (div.offsetTop <= mid) n = +div.dataset.n; else break; }
    if (n !== this.cur) { this.cur = n; this.moved(); this.markSide(); }
  },
  moved() { this.onMoved && this.onMoved(); },
  state() { return { page: this.cur, pages: this.total, scale: "board:" + this.k.toFixed(2) }; },

  // ---------------------------------------------------------------- شريط الصفحات
  renderSide() {
    this.side.textContent = "";
    this.side.classList.toggle("nb", this.nb());
    if (this.nb()) return this.renderNbSide();
    this.side.appendChild(h("div", { cls: "bd-side-h" }, "الصفحات", h("button", { title: "صفحة جديدة بعد الحالية", onclick: () => this.addPage() }, "＋")));
    this.meta.pages.forEach((p, i) => {
      const thumb = h("div", { cls: "bd-thumb" + (isDark(p.bg) ? " dark" : ""), style: { aspectRatio: `${p.w} / ${p.h}`, background: (BGS[p.bg] || BGS.white)[0] } });
      const items = Ink.doc.items(p.id);
      if (items.length) {
        const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.setAttribute("viewBox", `0 0 ${W} ${W * p.h / p.w}`);
        for (const it of items) if (it.ly !== "t") { const g = renderSvg(it); svg.appendChild(isDark(p.bg) ? adaptDark(g) : g); }
        thumb.appendChild(svg);
      }
      const card = h("div", { cls: "bd-card", draggable: "true", "data-n": i + 1, title: "اضغط للانتقال إلى الصفحة — انقر بالزر الأيمن للخيارات — اسحب للترتيب" },
        thumb, h("div", { cls: "bd-name" }, `${i + 1}` + (p.name ? ` · ${p.name}` : "")));
      card.onclick = () => this.goTo(i + 1);
      card.oncontextmenu = e => { e.preventDefault(); this.goTo(i + 1, true); this.pageMenu(e.clientX, e.clientY); };
      card.ondragstart = e => { e.dataTransfer.setData("text/plain", String(i)); };
      card.ondragover = e => e.preventDefault();
      card.ondrop = e => { e.preventDefault(); const from = +e.dataTransfer.getData("text/plain"); if (from !== i) this.movePage(from, i); };
      this.side.appendChild(card);
    });
    this.markSide();
  },
  markSide() { for (const c of this.side.querySelectorAll(".bd-card")) c.classList.toggle("on", +c.dataset.n === this.cur); },
  refreshSoon() { clearTimeout(this._rs); this._rs = setTimeout(() => this.renderSide(), 800); },

  // ---------------------------------------------------------------- أدوات السبورة
  buildTools() {
    const B = (label, title, fn) => h("button", { title, onclick: e => { e.stopPropagation(); fn(e); } }, label);
    this.tools.textContent = "";
    if (this.nb()) return this.buildNbTools(B);
    this.tools.append(
      B("➕ صفحة", "صفحة جديدة بعد الحالية (المقاس والخلفية نفسها)", () => this.addPage()),
      B("⧉", "كرّر الصفحة (بالرسم الذي عليها)", () => this.duplicate()),
      B("🗑", "امسح الصفحة", () => this.removePage()),
      B("▲", "الصفحة إلى الأعلى", () => this.movePage(this.cur - 1, this.cur - 2)),
      B("▼", "الصفحة إلى الأسفل", () => this.movePage(this.cur - 1, this.cur)),
      h("span", { cls: "sep" }),
      B("🎨 الخلفية ▾", "لون ونقشة الصفحة", e => this.menu(e.currentTarget, [
        ...Object.entries(BGS).map(([k, [, l]]) => [(this.curPage().bg === k ? "✓ " : "") + l, () => this.setBg(k)]),
        null, ...Object.entries(PATS).map(([k, l]) => [(this.curPage().pat === k ? "✓ " : "") + l, () => this.setPat(k)]),
        null, ["على كل الصفحات", () => this.bgAll()]])),
      B("📐 المقاس ▾", "مقاس الصفحة", e => this.menu(e.currentTarget, Object.entries(SIZES).map(([k, [w, hh, l]]) => [l, () => this.setSize(w, hh)]))),
      B("🧩 قالب ▾", "قوالب جاهزة على الصفحة", e => this.menu(e.currentTarget, [["محاور س/ص", () => this.template("axes")], ["جدول", () => this.template("table")], ["صفحة كود", () => this.template("code")],
        ["مخطط (Flowchart)", () => this.template("flow")], ["عنوان", () => this.template("title")]])),
      h("span", { cls: "sep" }),
      B("🧪 أدوات علمية", "معادلات رياضية وكيميائية وفيزيائية وأدوات المواد العلمية", () => Sci.toggle()),
      h("span", { cls: "sep" }),
      B("🖼", "صورة على الصفحة (أو Ctrl+V)", () => this.pickImage()),
      B("📄 صفحات PDF", "ضع صفحات من ملف PDF كخلفية تشرح عليها", () => this.insertPdf()),
      B("📸", "لقطة شاشة في صفحة جديدة", () => this.post({ t: "cmd", name: "board-shot", page: this.cur })),
      B("🧲", "الالتصاق بالشبكة", e => { Ink.snap = !Ink.snap; e.currentTarget.classList.toggle("on", Ink.snap); }),
      h("span", { cls: "sep" }),
      B("−", "تصغير (Ctrl+العجلة)", () => this.zoom(-1)),
      B("⟲", "مقاس الشاشة", () => this.zoom(0)),
      B("+", "تكبير", () => this.zoom(1)),
      h("span", { cls: "sep" }),
      B("📤 PDF ▾", "تصدير", e => this.menu(e.currentTarget, [["كل الصفحات", () => this.exportPdf("all")], ["هذه الصفحة فقط", () => this.exportPdf("cur")], ["هذه الصفحة كصورة PNG", () => this.exportPng()],
        null, ["📨 أرسل للطلبة (PDF جاهز للإرسال)", () => this.exportPdf("all", true)]]))
    );
    if (Ink.snap) this.tools.querySelector('[title="الالتصاق بالشبكة"]').classList.add("on");
  },
  /** شريط الدفتر: الصفحات والمقاطع وأدوات الكتابة بس. */
  buildNbTools(B) {
    this.tools.append(
      B("➕ صفحة", "صفحة جديدة بعد الحالية في هذا المقطع", () => this.addPage()),
      B("➕ مقطع", "مقطع جديد (مادة أو فصل أو وحدة) بصفحته الأولى", () => this.addSection()),
      B("✎ العنوان", "عنوان الصفحة (يظهر أعلاها وفي قائمة الصفحات)", () => this.rename()),
      B("⧉", "كرّر الصفحة (بالكتابة التي عليها)", () => this.duplicate()),
      B("🗑", "امسح الصفحة", () => this.removePage()),
      B("▲", "الصفحة إلى الأعلى", () => this.movePage(this.cur - 1, this.cur - 2)),
      B("▼", "الصفحة إلى الأسفل", () => this.movePage(this.cur - 1, this.cur)),
      h("span", { cls: "sep" }),
      B("🎨 الورق ▾", "لون الورق ونقشته", e => this.menu(e.currentTarget, [
        ...Object.entries(BGS).map(([k, [, l]]) => [(this.curPage().bg === k ? "✓ " : "") + l, () => this.setBg(k)]),
        null, ...Object.entries(PATS).map(([k, l]) => [(this.curPage().pat === k ? "✓ " : "") + l, () => this.setPat(k)]),
        null, ["على كل صفحات المقطع", () => this.bgAll(true)], ["على كل صفحات الدفتر", () => this.bgAll()]])),
      B("🖼", "صورة على الصفحة (أو Ctrl+V)", () => this.pickImage()),
      h("span", { cls: "sep" }),
      B("−", "تصغير (Ctrl+العجلة)", () => this.zoom(-1)),
      B("⟲", "مقاس الشاشة", () => this.zoom(0)),
      B("+", "تكبير", () => this.zoom(1)),
      h("span", { cls: "sep" }),
      B("📤 PDF ▾", "تصدير", e => this.menu(e.currentTarget, [["هذه الصفحة", () => this.exportPdf("cur")], ["هذا المقطع", () => this.exportPdf("sec")], ["الدفتر كله", () => this.exportPdf("all")],
        ["هذه الصفحة كصورة PNG", () => this.exportPng()], null, ["📨 أرسل المقطع للطلبة (PDF جاهز للإرسال)", () => this.exportPdf("sec", true)]]))
    );
  },
  /** عمود الدفتر: المقاطع فوق، وصفحات المقطع الحالي تحتها. */
  renderNbSide() {
    const m = this.meta, cur = this.section();
    let dragPage = null;
    this.side.appendChild(h("div", { cls: "bd-side-h" }, "المقاطع", h("button", { title: "مقطع جديد", onclick: () => this.addSection() }, "＋")));
    m.sections.forEach((sec, i) => {
      const n = m.pages.filter(p => p.sec === sec.id).length;
      const row = h("div", { cls: "nb-sec" + (sec.id === m.sec ? " on" : ""), "data-sec": sec.id, draggable: "true", style: { "--sc": sec.color || SEC_COLORS[0][0] },
        title: "اضغط لفتح المقطع — نقر مزدوج لإعادة التسمية — الزر الأيمن للخيارات — اسحب للترتيب، أو أفلت عليه صفحة لنقلها إليه" },
        h("span", { cls: "nb-dot" }), h("b", {}, sec.name || "بدون اسم"), h("small", {}, n));
      row.onclick = () => this.selectSection(sec.id);
      row.ondblclick = () => this.renameSection(sec.id);
      row.oncontextmenu = e => { e.preventDefault(); this.sectionMenu(sec.id, e.clientX, e.clientY); };
      row.ondragstart = e => { e.dataTransfer.setData("text/plain", "sec:" + i); };
      row.ondragover = e => { e.preventDefault(); row.classList.add("drop"); };
      row.ondragleave = () => row.classList.remove("drop");
      row.ondrop = e => {
        e.preventDefault(); row.classList.remove("drop");
        const d = e.dataTransfer.getData("text/plain");
        if (d.startsWith("sec:")) this.moveSection(+d.slice(4), i);
        else if (d.startsWith("page:")) this.movePageToSection(d.slice(5), sec.id);
      };
      this.side.appendChild(row);
    });
    this.side.appendChild(h("div", { cls: "bd-side-h nb-pages-h" }, `صفحات «${cur?.name || ""}»`, h("button", { title: "صفحة جديدة في آخر المقطع", onclick: () => this.addPage({}, this.total) }, "＋")));
    this.vis().forEach((p, i) => {
      const row = h("div", { cls: "bd-card nb-page", draggable: "true", "data-n": i + 1, "data-id": p.id, title: "اضغط للانتقال — نقر مزدوج لتغيير العنوان — الزر الأيمن للخيارات — اسحب للترتيب أو إلى مقطع آخر" },
        h("b", {}, p.name || "بدون عنوان"), h("small", {}, fmtDate(p.d)));
      row.onclick = () => this.goTo(i + 1);
      row.ondblclick = () => { this.goTo(i + 1, true); this.rename(); };
      row.oncontextmenu = e => { e.preventDefault(); this.goTo(i + 1, true); this.pageMenu(e.clientX, e.clientY); };
      row.ondragstart = e => { dragPage = i; e.dataTransfer.setData("text/plain", "page:" + p.id); };
      row.ondragover = e => e.preventDefault();
      row.ondrop = e => { e.preventDefault(); const d = e.dataTransfer.getData("text/plain"); if (d.startsWith("page:")) { const from = this.pageOf(d.slice(5)); if (from >= 0 && from !== i) this.movePage(from, i); } };
      this.side.appendChild(row);
    });
    this.markSide();
  },
  selectSection(id) {
    if (!this.nb() || this.meta.sec === id) return;
    Ink.flush();
    this.meta.sec = id; this.cur = 1;
    this.changed();
    this.goTo(1, true);
  },
  addSection(name) {
    const m = this.meta, c = this.curPage() || { w: 595, h: 842, bg: "white", pat: "lines" };
    name = name ?? prompt("اسم المقطع:", "مقطع " + (m.sections.length + 1));
    if (name == null) return null;
    const sec = { id: sid(), name: name.trim() || "مقطع " + (m.sections.length + 1), color: SEC_COLORS[m.sections.length % SEC_COLORS.length][0] };
    const at = m.sections.indexOf(this.section()) + 1;
    m.sections.splice(at, 0, sec);
    m.pages.push({ id: pid(), w: c.w, h: c.h, bg: c.bg, pat: c.pat, name: "", sec: sec.id, d: Date.now() });
    m.sec = sec.id; this.cur = 1;
    this.changed();
    this.goTo(1, true);
    return sec;
  },
  renameSection(id) {
    const sec = this.meta.sections.find(x => x.id === id); if (!sec) return;
    const n = prompt("اسم المقطع:", sec.name || "");
    if (n != null && n.trim()) { sec.name = n.trim(); this.changed(); }
  },
  sectionMenu(id, x, y) {
    const m = this.meta, i = m.sections.findIndex(s2 => s2.id === id), sec = m.sections[i];
    this.menu({ x, y }, [["✎ إعادة التسمية…", () => this.renameSection(id)],
      ...SEC_COLORS.map(([c, l]) => [(sec.color === c ? "✓ " : "● ") + "لون " + l, () => { sec.color = c; this.changed(); }]),
      null, ["▲ إلى الأعلى", () => this.moveSection(i, i - 1)], ["▼ إلى الأسفل", () => this.moveSection(i, i + 1)],
      null, ["🗑 احذف المقطع بصفحاته", () => this.removeSection(id)]]);
  },
  moveSection(from, to) {
    const L = this.meta.sections;
    if (to < 0 || to >= L.length || from === to) return;
    const [x] = L.splice(from, 1); L.splice(to, 0, x);
    this.changed();
  },
  removeSection(id) {
    const m = this.meta;
    if (m.sections.length <= 1) { alert("يجب أن يبقى مقطع واحد على الأقل"); return; }
    const sec = m.sections.find(x => x.id === id), pages = m.pages.filter(p => p.sec === id);
    if (!confirm(`هل تريد حذف المقطع «${sec.name}» بكل صفحاته (${pages.length})؟ لا يمكن التراجع عن ذلك.`)) return;
    for (const p of pages) Ink.clearPage(p.id, "all");
    m.pages = m.pages.filter(p => p.sec !== id);
    const i = m.sections.indexOf(sec);
    m.sections.splice(i, 1);
    if (m.sec === id) { m.sec = m.sections[Math.max(0, i - 1)].id; this.cur = 1; }
    this.changed();
    this.goTo(this.cur, true);
  },
  /** نقل صفحة لمقطع تاني (بتروح آخره). */
  movePageToSection(pageId, secId) {
    const m = this.meta, p = m.pages.find(x => x.id === pageId);
    if (!p || p.sec === secId) return;
    if (m.pages.filter(x => x.sec === p.sec).length <= 1) { alert("هذه آخر صفحة في مقطعها — أضف صفحة أخرى أولًا أو احذف المقطع"); return; }
    m.pages.splice(m.pages.indexOf(p), 1);
    const last = m.pages.map(x => x.sec).lastIndexOf(secId);
    p.sec = secId;
    m.pages.splice(last + 1, 0, p);
    this.cur = Math.min(this.cur, this.total);
    this.changed();
  },

  menu(anchor, items) {
    document.querySelector(".pe-menu")?.remove();
    const m = h("div", { cls: "pe-menu" }, items.map(x => x ? h("button", { onclick: () => { m.remove(); x[1](); } }, x[0]) : h("hr")));
    document.body.appendChild(m);
    const r = anchor.getBoundingClientRect ? anchor.getBoundingClientRect() : { left: anchor.x, bottom: anchor.y };
    m.style.top = Math.min(innerHeight - m.offsetHeight - 6, r.bottom + 4) + "px"; m.style.left = Math.min(innerWidth - m.offsetWidth - 6, r.left) + "px";
    setTimeout(() => document.addEventListener("pointerdown", function off(e) { if (!m.contains(e.target)) { m.remove(); document.removeEventListener("pointerdown", off, true); } }, true), 0);
  },
  pageMenu(x, y) {
    const moves = this.nb() ? this.meta.sections.filter(sc => sc.id !== this.meta.sec).map(sc => ["↪ انقلها إلى «" + sc.name + "»", () => this.movePageToSection(this.curPage().id, sc.id)]) : [];
    this.menu({ x, y }, [[this.nb() ? "✎ عنوان الصفحة…" : "✎ اسم الصفحة…", () => this.rename()], ...moves, ["⧉ كرر", () => this.duplicate()], ["▲ إلى الأعلى", () => this.movePage(this.cur - 1, this.cur - 2)], ["▼ إلى الأسفل", () => this.movePage(this.cur - 1, this.cur)],
      null, ["🗑 امسح الصفحة", () => this.removePage()]]);
  },

  // ---------------------------------------------------------------- عمليات الصفحات
  changed(re = true) {
    this.meta.u = Date.now();
    clearTimeout(this._save);
    this._save = setTimeout(() => this.post({ t: "board", board: this.meta }), 600);
    if (re) this.render();
    this.moved();
  },
  addPage(opts = {}, at) {
    const c = this.curPage() || { w: 842, h: 595, bg: "white", pat: "none" };
    const p = { id: pid(), w: opts.w || c.w, h: opts.h || c.h, bg: opts.bg || c.bg, pat: opts.pat ?? c.pat, name: opts.name || "" };
    if (this.nb()) { p.sec = this.meta.sec; p.d = Date.now(); }
    const i = at ?? this.cur;
    this.insertVis(i, p);
    this.changed();
    this.goTo(i + 1, true);
    return p;
  },
  duplicate() {
    const c = this.curPage();
    const p = this.addPage({ ...c, name: c.name ? c.name + " (نسخة)" : "" });
    const items = Ink.doc.items(c.id).filter(i => i.ly !== "t").map(i => ({ ...JSON.parse(JSON.stringify(i)), id: iid() }));
    if (items.length) Ink.edit(p.id, () => items);
    this.refreshSoon();
  },
  removePage() {
    if (this.total <= 1) { alert(this.nb() ? "يجب أن تبقى صفحة واحدة في المقطع على الأقل — لحذف المقطع كله انقر عليه بالزر الأيمن" : "يجب أن تبقى صفحة واحدة على الأقل"); return; }
    const c = this.curPage();
    if (Ink.doc.items(c.id).length && !confirm("هل تريد مسح هذه الصفحة بما عليها؟")) return;
    Ink.clearPage(c.id, "all");
    this.meta.pages.splice(this.meta.pages.indexOf(c), 1);
    this.cur = Math.min(this.cur, this.total);
    this.changed();
    this.goTo(this.cur, true);
  },
  movePage(from, to) {
    if (to < 0 || to >= this.total || from === to) return;
    const V = this.vis(), a = V[from], b = V[to], all = this.meta.pages;
    all.splice(all.indexOf(a), 1);
    const ib = all.indexOf(b);
    all.splice(to > from ? ib + 1 : ib, 0, a);
    this.changed();
    this.goTo(to + 1, true);
  },
  setBg(bg) { this.curPage().bg = bg; this.changed(); this.goTo(this.cur, true); },
  setPat(pat) { this.curPage().pat = pat; this.changed(); this.goTo(this.cur, true); },
  bgAll(sec) { const c = this.curPage(); for (const p of sec ? this.vis() : this.meta.pages) { p.bg = c.bg; p.pat = c.pat; } this.changed(); this.goTo(this.cur, true); },
  setSize(w, hh) { const c = this.curPage(); c.w = w; c.h = hh; this.changed(); this.goTo(this.cur, true); },
  rename() { const c = this.curPage(); const n = prompt(this.nb() ? "عنوان الصفحة:" : "اسم الصفحة:", c.name || ""); if (n != null) { c.name = n.trim(); this.changed(); this.goTo(this.cur, true); } },

  /** قوالب: عناصر شرح عادية (تتحرك وتتعدل وتتمسح). */
  template(kind) {
    const c = this.curPage(), H = W * c.h / c.w, ink = isDark(c.bg) ? "#f1f3f5" : "#343a40", ac = "#1971c2", t = Date.now();
    const S = (k, x1, y1, x2, y2, o = {}) => ({ id: iid(), t: "shape", k, x1, y1, x2, y2, c: ink, sw: 2.5, ly: "p", u: t, ...o });
    const T = (x, y, w, text, size, o = {}) => ({ id: iid(), t: "text", x, y, w, h: size * 1.5, text, size, font: "Segoe UI", c: ink, ly: "p", u: t, ...o });
    let items = [];
    if (kind === "axes") {
      const ox = 120, oy = H - 120;
      items.push(S("arrow", ox, oy, W - 80, oy), S("arrow", ox, oy, ox, 80), T(W - 110, oy + 8, 60, "س", 26), T(ox - 50, 60, 50, "ص", 26), T(ox - 40, oy + 6, 40, "0", 20));
      for (let x = ox + 80; x < W - 100; x += 80) items.push(S("line", x, oy - 8, x, oy + 8));
      for (let y = oy - 80; y > 110; y -= 80) items.push(S("line", ox - 8, y, ox + 8, y));
    } else if (kind === "table") {
      const x0 = 80, y0 = 120, cols = 4, rows = 6, cw = (W - 160) / cols, rh = Math.min(70, (H - 200) / rows);
      items.push(S("rect", x0, y0, x0 + cw * cols, y0 + rh * rows), S("rect", x0, y0, x0 + cw * cols, y0 + rh, { fill: ac, fo: 0.12 }));
      for (let i = 1; i < cols; i++) items.push(S("line", x0 + cw * i, y0, x0 + cw * i, y0 + rh * rows));
      for (let j = 1; j < rows; j++) items.push(S("line", x0, y0 + rh * j, x0 + cw * cols, y0 + rh * j));
    } else if (kind === "code") {
      items.push(T(60, 50, W - 120, "عنوان الكود", 34, { bold: true }),
        S("round", 60, 130, W - 60, H - 80, { fill: "#212529", fo: 0.95, c: "#495057" }),
        T(90, 160, W - 180, "// اكتب الكود هنا\nfunction hello() {\n  console.log(\"مسار\");\n}", 22, { font: "Consolas", c: "#e9ecef", dir: "ltr", align: "left" }));
    } else if (kind === "flow") {
      const cx = W / 2, y = [120, 280, 440, 600].map(v => v * H / 760);
      const a = S("ellipse", cx - 120, y[0] - 40, cx + 120, y[0] + 40, { c: ac }), b = S("rect", cx - 150, y[1] - 45, cx + 150, y[1] + 45, { c: ac }),
        c2 = S("round", cx - 150, y[2] - 45, cx + 150, y[2] + 45, { c: ac }), d = S("ellipse", cx - 120, y[3] - 40, cx + 120, y[3] + 40, { c: ac });
      items.push(a, b, c2, d,
        T(cx - 110, y[0] - 20, 220, "بداية", 24, { align: "center" }), T(cx - 140, y[1] - 20, 280, "خطوة", 24, { align: "center" }),
        T(cx - 140, y[2] - 20, 280, "قرار / خطوة", 24, { align: "center" }), T(cx - 110, y[3] - 20, 220, "نهاية", 24, { align: "center" }),
        S("arrow", cx, y[0] + 40, cx, y[1] - 45, { a1: a.id, a2: b.id }), S("arrow", cx, y[1] + 45, cx, y[2] - 45, { a1: b.id, a2: c2.id }), S("arrow", cx, y[2] + 45, cx, y[3] - 40, { a1: c2.id, a2: d.id }));
    } else if (kind === "title") {
      items.push(T(80, H * 0.35, W - 160, "العنوان", 64, { bold: true, align: "center" }), S("line", W * 0.3, H * 0.35 + 110, W * 0.7, H * 0.35 + 110, { c: ac, sw: 4 }),
        T(80, H * 0.35 + 130, W - 160, "عنوان فرعي — اسم المحاضر — التاريخ", 28, { align: "center", c: "#868e96" }));
    }
    Ink.edit(c.id, list => list.concat(items));
    this.refreshSoon();
  },

  pickImage() {
    const f = h("input", { type: "file", accept: "image/*", onchange: () => { if (f.files[0]) { const s = Ink.surfaces.get(this.curPage().id); Ink.insertImageFile(f.files[0], s); this.refreshSoon(); } } });
    f.click();
  },
  /** صفحة جديدة عليها صورة بمقاسها (لقطة شاشة مثلًا). */
  async addImagePage(dataUrl, name) {
    const img = await new Promise((r, j) => { const i = new Image(); i.onload = () => r(i); i.onerror = j; i.src = dataUrl; });
    const w = 960, hh = Math.round(960 * img.height / img.width);
    const p = this.addPage({ w, h: hh, bg: "white", pat: "none", name: name || "لقطة شاشة" });
    Ink.edit(p.id, () => [{ id: iid(), t: "image", img: dataUrl, x: 0, y: 0, w: W, h: W * hh / w, ly: "p", lock: 1, u: Date.now() }]);
    this.refreshSoon();
  },
  /** صفحات PDF كخلفية (صورة مقفولة تحت الرسم). */
  async insertPdf(url) {
    url = url || (this.pick && await this.pick("open-pdf"));
    if (!url) return;
    const task = this.pdfjsLib.getDocument({ ...this.docOpts, url });
    try {
      const d = await task.promise;
      const at = this.cur;
      for (let n = 1; n <= d.numPages && n <= 200; n++) {
        const pg = await d.getPage(n), v = pg.getViewport({ scale: 1 }), vp = pg.getViewport({ scale: Math.min(2.2, 2000 / v.width) });
        const c = document.createElement("canvas"); c.width = Math.ceil(vp.width); c.height = Math.ceil(vp.height);
        const x = c.getContext("2d"); x.fillStyle = "#fff"; x.fillRect(0, 0, c.width, c.height);
        await pg.render({ canvasContext: x, viewport: vp }).promise;
        const p = { id: pid(), w: Math.round(v.width), h: Math.round(v.height), bg: "white", pat: "none", name: "ص " + n };
        if (this.nb()) { p.sec = this.meta.sec; p.d = Date.now(); }
        this.insertVis(at + n - 1, p);
        Ink.edit(p.id, () => [{ id: iid(), t: "image", img: c.toDataURL("image/jpeg", 0.85), x: 0, y: 0, w: W, h: W * v.height / v.width, ly: "p", lock: 1, u: Date.now() }]);
        pg.cleanup();
      }
      this.changed();
      this.goTo(at + 1, true);
    } finally { task.destroy(); }
  },

  // ---------------------------------------------------------------- التصدير
  async exportPdf(which, send) {
    Ink.flush();
    const pages = which === "cur" ? [this.curPage()] : which === "sec" ? this.vis() : this.meta.pages;
    const base = this.meta.title || (this.nb() ? "دفتر" : "سبورة");
    const save = await this.pick("save-pdf", base + (which === "cur" ? ` — ${this.nb() && this.curPage().name ? this.curPage().name : "صفحة " + this.cur}` : which === "sec" ? ` — ${this.section()?.name || ""}` : "") + ".pdf");
    if (!save) return null;
    this.post({ t: "cmd", name: "board-busy", page: this.cur });
    try {
      const plans = [];
      for (const p of pages) {
        const vp = { width: p.w, height: p.h, transform: [1, 0, 0, -1, 0, p.h] };
        const items = Ink.doc.items(p.id).map(it => darkItem(it, p)).concat(this.nb() ? nbHeader(p) : []);
        plans.push({ w: p.w, h: p.h, bg: (BGS[p.bg] || BGS.white)[0], pat: p.pat, dark: isDark(p.bg), ...(await pagePlan(items, vp)) });
      }
      const r = await runJob({ job: "board", save, plans, title: base });
      this.post({ t: "cmd", name: send ? "board-send" : "board-exported", page: r.pages });
      return r;
    } catch (e) { this.post({ t: "log", message: "board export: " + e.message }); alert("فشل التصدير: " + e.message); return null; }
  },
  async exportPng() {
    Ink.flush();
    const p = this.curPage();
    const save = await this.pick("save-png", `${this.meta.title || "سبورة"} — صفحة ${this.cur}.png`);
    if (!save) return;
    // صفحة وهمية بنفس واجهة PDF.js: خلفية بس (والشرح بيترسم فوقها)
    const fake = {
      getViewport: ({ scale }) => ({ width: p.w * scale, height: p.h * scale }),
      render: ({ canvasContext: x, viewport: v }) => ({ promise: (async () => { const div = document.querySelector(`.bd-page[data-id="${p.id}"]`); x.fillStyle = (BGS[p.bg] || BGS.white)[0]; x.fillRect(0, 0, v.width, v.height); if (div) drawPattern(x, p, v.width / p.w); })() })
    };
    const png = await pagePng({ pdfPage: fake, items: Ink.doc.items(p.id).map(it => darkItem(it, p)), scale: 2 });
    const r = await fetch(save, { method: "POST", body: png });
    this.post({ t: "cmd", name: r.ok ? "board-exported" : "board-failed", page: 1 });
  }
};

/** سبورة من ملفها وشرحها دون فتحها (للتصدير): خطة PDF لكل صفحة. */
export async function boardPlans(meta, inkJson) {
  const doc = new InkDoc(inkJson || "{}");
  const plans = [];
  for (const p of (meta && meta.pages) || []) {
    const vp = { width: p.w, height: p.h, transform: [1, 0, 0, -1, 0, p.h] };
    const items = doc.items(p.id).filter(i => i && !i.tempPen && i.ly !== "t").map(it => darkItem(it, p));
    plans.push({ w: p.w, h: p.h, bg: (BGS[p.bg] || BGS.white)[0], pat: p.pat, dark: isDark(p.bg), ...(await pagePlan(items, vp)) });
  }
  return plans;
}

/** رأس صفحة الدفتر في الـ PDF: العنوان والتاريخ والهامش الأحمر (على الشاشة بيترسموا بـ CSS). */
function nbHeader(p) {
  const H = W * p.h / p.w, dark = isDark(p.bg), t = Date.now(), mx = W * (1 - 0.085);
  const out = [{ id: "nbm", t: "shape", k: "line", x1: mx, y1: 0, x2: mx, y2: H, c: dark ? "#ff8787" : "#e8868a", sw: 1.2, ly: "p", u: t }];
  if (p.name) out.push({ id: "nbt", t: "text", x: W * 0.32, y: W * 0.022, w: W * 0.58, h: 60, text: p.name, size: 38, bold: true, font: "Segoe UI", c: dark ? "#a5d8ff" : "#1c3d6e", align: "start", dir: "rtl", ly: "p", u: t });
  if (p.d) out.push({ id: "nbd", t: "text", x: W * 0.04, y: W * 0.034, w: W * 0.3, h: 36, text: fmtDate(p.d), size: 20, font: "Segoe UI", c: dark ? "#ced4da" : "#868e96", align: "end", dir: "rtl", ly: "p", u: t });
  return out;
}

/** على سبورة غامقة: ألوان العنصر الغامقة تبقى فاتحة (زي ما بتبان على الشاشة). */
function darkItem(it, p) {
  if (!isDark(p.bg)) return it;
  const o = { ...it };
  if (o.c) o.c = onDark(o.c);
  if (o.fill) o.fill = onDark(o.fill);
  return o;
}

function drawPattern(x, p, k) {
  const dark = isDark(p.bg);
  x.strokeStyle = x.fillStyle = dark ? "rgba(255,255,255,.16)" : "rgba(120,130,150,.28)";
  x.lineWidth = Math.max(1, k * 0.5);
  const W2 = p.w * k, H2 = p.h * k, st = 20 * k;
  if (p.pat === "grid") { for (let a = st; a < W2; a += st) { x.beginPath(); x.moveTo(a, 0); x.lineTo(a, H2); x.stroke(); } for (let b = st; b < H2; b += st) { x.beginPath(); x.moveTo(0, b); x.lineTo(W2, b); x.stroke(); } }
  else if (p.pat === "lines") for (let b = 24 * k; b < H2; b += 24 * k) { x.beginPath(); x.moveTo(0, b); x.lineTo(W2, b); x.stroke(); }
  else if (p.pat === "dots") for (let a = st; a < W2; a += st) for (let b = st; b < H2; b += st) { x.beginPath(); x.arc(a, b, k * 0.9, 0, 7); x.fill(); }
}
