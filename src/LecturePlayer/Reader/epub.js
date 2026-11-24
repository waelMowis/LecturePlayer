// كتب EPUB جوه قارئ «مسار» (epub.js + JSZip). «الصفحات» هنا = مواقع ثابتة في الكتاب (حوالي 1000 حرف لكل واحد)
// عشان التتبّع (فين وقفت، والصفحات اللي اتقرت، والفصول) يشتغل زي الـ PDF بالظبط ومايتغيرش مع حجم الخط والشاشة.
const CHARS_PER_PAGE = 1000;
let ctx = null;

function script(src) {
  return new Promise((ok, fail) => {
    if ([...document.scripts].some(s => s.src.endsWith(src))) return ok();
    const s = document.createElement("script");
    s.src = src; s.onload = ok; s.onerror = () => fail(new Error("مقدرتش أحمّل " + src));
    document.head.appendChild(s);
  });
}
async function libs() {
  if (window.ePub) return;
  await script("lib/jszip.min.js");
  await script("lib/epub.min.js");
}

/** المواقع (بتتحسب مرة وتتحفظ في المتصفح لنفس النسخة من الملف). */
async function locationsOf(book, url) {
  const key = "masar-epub-loc:" + url.split("?")[0];
  try {
    const saved = localStorage.getItem(key);
    if (saved) { book.locations.load(saved); if (book.locations.length()) return book.locations.length(); }
  } catch (e) { }
  await book.locations.generate(CHARS_PER_PAGE);
  try { localStorage.setItem(key, book.locations.save()); } catch (e) { }
  return book.locations.length();
}

function cfiList(book) {
  const l = book.locations;
  return l._locations || (l.save ? JSON.parse(l.save()) : []);
}

/** أول «صفحة» في فصل (من href في الفهرس). */
function pageOfHref(book, href) {
  const sec = book.spine.get(href) || book.spine.get(String(href).split("#")[0]);
  if (!sec) return null;
  const prefix = "epubcfi(" + sec.cfiBase + "!";
  const idx = cfiList(book).findIndex(c => c.indexOf(prefix) === 0);
  return idx >= 0 ? idx + 1 : null;
}

async function infoOf(book, total) {
  const md = (book.packaging && book.packaging.metadata) || {};
  const outline = [];
  const nav = book.navigation && book.navigation.toc || [];
  const walk = (items, level) => {
    for (const it of items || []) {
      if (outline.length >= 600) return;
      const page = pageOfHref(book, it.href);
      const title = (it.label || "").replace(/\s+/g, " ").trim();
      if (page && title) outline.push({ title, page, level });
      if (level < 1 && it.subitems && it.subitems.length) walk(it.subitems, level + 1);
    }
  };
  walk(nav, 0);
  outline.sort((a, b) => a.page - b.page);
  return { pages: total, fingerprint: md.identifier ? "epub:" + md.identifier : null, title: md.title || null, author: md.creator || null, outline };
}

const THEMES = {
  normal: { body: { background: "#ffffff !important", color: "#1a1a1a !important" } },
  night: { body: { background: "#1e1f22 !important", color: "#d6d6d6 !important" }, a: { color: "#7cc4f0 !important" } },
  sepia: { body: { background: "#f4ecd8 !important", color: "#3b2f1e !important" } }
};
const HL = { yellow: "#ffe066", green: "#8ce99a", blue: "#74c0fc", pink: "#faa2c1" };

export const Epub = {
  book: null, rendition: null, total: 0, loc: null, font: 100, hlShown: [], hits: [], hitIdx: -1, hitQuery: "", rtl: false, spread: 0,

  init(c) { ctx = c; },
  get ready() { return !!this.rendition; },

  async open(o) {
    await libs();
    const area = document.getElementById("epubArea");
    area.textContent = "";
    const book = this.book = window.ePub(o.url, { openAs: "epub" });
    await book.ready;
    this.total = await locationsOf(book, o.url);
    this.rtl = (book.packaging && book.packaging.metadata && book.packaging.metadata.direction) === "rtl";
    this.spread = o.spread || 0;
    const r = this.rendition = book.renderTo(area, { width: "100%", height: "100%", flow: "paginated", spread: this.spread ? "auto" : "none", allowScriptedContent: false });
    for (const [name, rules] of Object.entries(THEMES)) r.themes.register(name, rules);
    this.setTheme(o.theme || "normal");
    this.font = /^font:(\d+)$/.test(o.scale || "") ? +o.scale.slice(5) : 100;
    r.themes.fontSize(this.font + "%");
    r.on("relocated", loc => { this.loc = loc; ctx.onMoved(); });
    for (const ev of ["keydown"]) r.on(ev, e => ctx.onKey(e));
    for (const ev of ["mousemove", "mousedown", "wheel", "touchstart", "keydown"]) r.on(ev, () => ctx.onInput());
    r.on("selected", (cfiRange, contents) => {
      try {
        const range = contents.range(cfiRange);
        const rect = range.getBoundingClientRect();
        const frame = contents.window.frameElement.getBoundingClientRect();
        ctx.onSelected(cfiRange, range.toString().replace(/\s+/g, " ").trim(), frame.left + rect.left + rect.width / 2, frame.top + rect.top, this.pageOfCfi(cfiRange));
      } catch (e) { }
    });
    let target;
    if (o.cfi) target = o.cfi;
    else if (o.page > 1) target = book.locations.cfiFromLocation(Math.min(o.page, this.total) - 1);
    await r.display(target);
    this.setHighlights(o.highlights || []);
    return await infoOf(book, this.total);
  },

  async close() {
    const b = this.book;
    this.book = null; this.rendition = null; this.loc = null; this.hits = []; this.hlShown = [];
    if (b) try { b.destroy(); } catch (e) { }
    const area = document.getElementById("epubArea");
    if (area) area.textContent = "";
  },

  pageOfCfi(cfi) {
    try { const i = this.book.locations.locationFromCfi(cfi); return i >= 0 ? i + 1 : this.page(); } catch (e) { return this.page(); }
  },
  page() { return this.loc && this.loc.start ? Math.max(1, (this.loc.start.location ?? 0) + 1) : 1; },
  visiblePages() {
    if (!this.loc || !this.loc.start) return [];
    const a = (this.loc.start.location ?? 0) + 1, b = (this.loc.end && this.loc.end.location != null ? this.loc.end.location : a - 1) + 1;
    const out = [];
    for (let p = a; p <= Math.max(a, b) && out.length < 12; p++) out.push(p);
    return out;
  },
  state() {
    return { page: this.page(), pages: this.total, scale: "font:" + this.font, top: null, left: null, spread: this.spread, cfi: this.loc && this.loc.start ? this.loc.start.cfi : null };
  },

  goTo(p) { if (this.rendition) this.rendition.display(this.book.locations.cfiFromLocation(Math.min(Math.max(1, p), this.total) - 1)); },
  step(d) { if (this.rendition) (d > 0 ? this.rendition.next() : this.rendition.prev()); },
  zoom(dir) {
    if (!this.rendition) return;
    this.font = dir === 0 ? 100 : Math.max(60, Math.min(250, this.font + dir * 10));
    this.rendition.themes.fontSize(this.font + "%");
    ctx.onMoved();
  },
  setSpread(mode) { this.spread = mode; if (this.rendition) this.rendition.spread(mode ? "auto" : "none"); },
  setTheme(t) {
    if (!this.rendition) return;
    this.rendition.themes.select(THEMES[t] ? t : "normal");
    const area = document.getElementById("epubArea");
    if (area) area.dataset.theme = t;
  },

  setHighlights(list) {
    const r = this.rendition;
    if (!r) return;
    for (const cfi of this.hlShown) try { r.annotations.remove(cfi, "highlight"); } catch (e) { }
    this.hlShown = [];
    for (const h of list || []) {
      if (!h.cfi) continue;
      try {
        r.annotations.highlight(h.cfi, { id: h.id }, e => ctx.onHighlightClick(h.id, e), "masar-hl-" + h.color,
          { fill: HL[h.color] || HL.yellow, "fill-opacity": "0.4", "mix-blend-mode": "multiply" });
        this.hlShown.push(h.cfi);
      } catch (e) { }
    }
  },

  /** البحث جوه الكتاب: كل فصل بيتحمّل ويتدوّر فيه، والنتايج بالترتيب. */
  async find(q, again, previous) {
    if (!this.book || !q) return { current: 0, total: 0 };
    if (!again || q !== this.hitQuery) {
      this.hitQuery = q; this.hits = []; this.hitIdx = -1;
      for (const item of this.book.spine.spineItems) {
        try {
          await item.load(this.book.load.bind(this.book));
          this.hits.push(...item.find(q));
        } catch (e) { } finally { try { item.unload(); } catch (e) { } }
      }
    }
    if (!this.hits.length) return { current: 0, total: 0 };
    this.hitIdx = this.hitIdx < 0 ? 0 : (this.hitIdx + (previous ? -1 : 1) + this.hits.length) % this.hits.length;
    await this.rendition.display(this.hits[this.hitIdx].cfi);
    return { current: this.hitIdx + 1, total: this.hits.length };
  },

  /** نص الكتاب: كل فصل بيترقّم بأول «صفحة» فيه. */
  async texts(url, onBatch) {
    const own = !url;
    const book = own ? this.book : window.ePub(url, { openAs: "epub" });
    if (!book) throw new Error("مفيش كتاب مفتوح");
    try {
      if (!own) { await libs(); await book.ready; await locationsOf(book, url); }
      const cfis = cfiList(book);
      const total = cfis.length || 1;
      let batch = [];
      for (const item of book.spine.spineItems) {
        let text = "";
        try {
          await item.load(book.load.bind(book));
          const body = item.document && item.document.body;
          if (body) {
            // سطر جديد بعد كل فقرة/عنوان (textContent لوحده بيلزّقهم في بعض)
            body.querySelectorAll("p,div,h1,h2,h3,h4,h5,h6,li,tr,br,section,article,blockquote,pre").forEach(el => el.append("\n"));
            text = body.textContent || "";
          }
        } catch (e) { } finally { try { item.unload(); } catch (e) { } }
        const prefix = "epubcfi(" + item.cfiBase + "!";
        const idx = cfis.findIndex(c => c.indexOf(prefix) === 0);
        if (idx < 0) continue;
        batch.push([idx + 1, text.replace(/[ \t]+/g, " ").replace(/\s*\n\s*/g, "\n").trim()]);
        if (batch.length >= 10) { onBatch(batch, total, false); batch = []; }
      }
      onBatch(batch, total, true);
    } finally {
      if (!own) try { book.destroy(); } catch (e) { }
    }
  },

  async probe(url) {
    await libs();
    const book = window.ePub(url, { openAs: "epub" });
    try {
      await book.ready;
      const total = await locationsOf(book, url);
      return await infoOf(book, total);
    } finally { try { book.destroy(); } catch (e) { } }
  }
};
