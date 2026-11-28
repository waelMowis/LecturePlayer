import { Epub as E } from "./epub.js";
import { Ink } from "./ink.js";
import { InkUI } from "./ink-ui.js";
import { buildAnnotated, pagePng } from "./ink-export.js";
import { PageEditor } from "./page-editor.js";
import { Board, boardPlans } from "./board.js";
import { Sci } from "./sci.js";
import * as SciChem from "./sci-chem.js";
import * as SciShapes from "./sci-shapes.js";
import { runJob } from "./ink-export.js";
const host = window.chrome && window.chrome.webview;
function post(msg) {
  const s = JSON.stringify(msg);
  if (host) host.postMessage(s); else (window.__posted ||= []).push(msg);
}
Sci.post = post;   // الأدوات العلمية بتطلب من البرنامج يفتح مصادر أونلاين
window.addEventListener("error", e => post({ t: "log", message: "error: " + (e.message || e) + " @" + (e.filename || "") + ":" + (e.lineno || "") }));
window.addEventListener("unhandledrejection", e => post({ t: "log", message: "rejection: " + (e.reason && (e.reason.message || e.reason)) }));
const $ = id => document.getElementById(id);
// قارئ الكتب جوه «مسار» (PDF.js). البرنامج بيبعت أوامر عن طريق window.masar،
// والصفحة بتبعت للبرنامج (chrome.webview.postMessage) حالة القراية: الصفحة والمكان والتكبير والصفحات اللي اتقرت فعلًا.
const pdfjsLib = globalThis.pdfjsLib;
const V = globalThis.pdfjsViewer;
const LIB = new URL("lib/", location.href).href;
pdfjsLib.GlobalWorkerOptions.workerSrc = LIB + "pdf.worker.min.js";
const DOC_OPTS = {
  cMapUrl: LIB + "cmaps/", cMapPacked: true, standardFontDataUrl: LIB + "standard_fonts/",
  wasmUrl: LIB + "wasm/", iccUrl: LIB + "iccs/", enableXfa: true, isEvalSupported: false
};



const container = $("viewerContainer");
const eventBus = new V.EventBus();
const linkService = new V.PDFLinkService({ eventBus, externalLinkTarget: V.LinkTarget.BLANK, externalLinkRel: "noopener noreferrer" });
const findController = new V.PDFFindController({ eventBus, linkService });
const viewer = new V.PDFViewer({ container, eventBus, linkService, findController, textLayerMode: 1 });
linkService.setViewer(viewer);

const S = {
  gen: 0, doc: null, task: null, opts: {}, readSeconds: 10,
  read: new Set(), dwell: new Map(), lastInput: Date.now(), lastState: "", marks: new Set(),
  theme: "normal", rtl: false, kind: "pdf", spread: 0
};
const epubArea = $("epubArea");
const isEpubUrl = url => !!url && /\.epub$/i.test(decodeURIComponent(String(url).split("?")[0]));
const boardArea = $("boardArea");
const hasDoc = () => S.kind === "board" ? Board.ready : S.kind === "epub" ? E.ready : !!S.doc;
const curPage = () => S.kind === "board" ? Board.page() : S.kind === "epub" ? E.page() : viewer.currentPageNumber;
const pageTotal = () => S.kind === "board" ? Board.total : S.kind === "epub" ? E.total : (S.doc ? S.doc.numPages : 0);
E.init({
  onMoved: () => { $("pageCount").textContent = Board.total; updatePage(); postState(false); },
  onKey: e => onKeyDown(e),
  onInput: () => { S.lastInput = Date.now(); },
  onSelected: (cfi, text, x, y, page) => {
    if (!text) return;
    pendingSel = [{ page, rects: [[0, 0, 0, 0]], text, cfi }];
    showHlBar(x, y);
  },
  onHighlightClick: (id, e) => {
    const r = epubArea.getBoundingClientRect();
    showHlBar(e && e.clientX != null ? r.left + e.clientX : r.left + r.width / 2, e && e.clientY != null ? r.top + e.clientY : r.top + 80, id);
  }
});

// ---------------------------------------------------------------- فتح وقفل
async function open(o) {
  const gen = ++S.gen;
  await closeDoc();
  S.opts = o || {};
  S.readSeconds = Math.max(1, o.readSeconds || 10);
  S.read = new Set(o.read || []);
  S.dwell = new Map();
  S.marks = new Set(o.marks || []);
  S.highlights = o.highlights || [];
  hideHlBar();
  inkOpen(o);
  S.lastInput = Date.now();
  S.lastState = "";
  setTheme(o.theme || S.theme, false);
  setRtl(!!o.rtl, false);
  $("bookTitle").textContent = o.title || "";
  document.title = o.title || "قارئ مسار";
  setOverlay("loading", "جارٍ فتح الكتاب…");

  boardArea.hidden = true;
  document.body.classList.toggle("board", o.format === "board");
  if (o.format === "board") {
    S.kind = "board";
    container.hidden = true;
    epubArea.hidden = true;
    boardArea.hidden = false;
    let info;
    try { info = Board.open(o); }
    catch (e) {
      const msg = (e && e.message) || String(e);
      setOverlay("failed", "تعذّر فتح السبورة: " + msg);
      post({ t: "error", gen: o.gen, code: "board", message: msg });
      return;
    }
    setOverlay("", "");
    $("pageCount").textContent = info.pages;
    updatePage();
    post({ t: "loaded", gen: o.gen, pages: info.pages, title: info.title, outline: [] });
    postState(true);
    // السبورة مفتوحة = الأقلام جاهزة (والشريط تحت أدوات السبورة لو مكانه مش متحفظ)
    if (!Ink.prefs.barPos) { const r = boardArea.querySelector(".bd-tools").getBoundingClientRect(); InkUI.bar.style.top = (r.bottom + 8) + "px"; InkUI.bar.style.left = "10px"; }
    InkUI.setOn(true);
    return;
  }
  if (o.format === "epub") {
    S.kind = "epub";
    container.hidden = true;
    epubArea.hidden = false;
    $("direction").hidden = true;
    $("zoomSelect").hidden = true;
    let info;
    try { info = await E.open(o); }
    catch (e) {
      if (gen !== S.gen) return;
      const msg = (e && e.message) || String(e);
      setOverlay("failed", "تعذّر فتح الكتاب: " + msg);
      post({ t: "error", gen: o.gen, code: "epub", message: msg });
      return;
    }
    if (gen !== S.gen) return;
    S.spread = o.spread || 0;
    applySpread(S.spread);
    setOverlay("", "");
    $("pageCount").textContent = E.total;
    updatePage();
    post({ t: "loaded", gen: o.gen, ...info });
    postState(true);
    return;
  }
  S.kind = "pdf";
  container.hidden = false;
  epubArea.hidden = true;
  boardArea.hidden = true;
  $("direction").hidden = false;
  $("zoomSelect").hidden = false;

  const task = S.task = pdfjsLib.getDocument({ ...DOC_OPTS, url: o.url });
  task.onPassword = (update, reason) => {
    const p = window.prompt(reason === 2 ? "كلمة المرور خاطئة — اكتبها مرة أخرى:" : "هذا الكتاب محمي بكلمة مرور:");
    if (p === null) { post({ t: "error", gen: o.gen, code: "password", message: "الكتاب محمي بكلمة مرور" }); task.destroy(); }
    else update(p);
  };
  let doc;
  try { doc = await task.promise; }
  catch (e) {
    if (gen !== S.gen) return;
    const msg = e && e.name === "PasswordException" ? "الكتاب محمي بكلمة مرور" : (e && e.message) || String(e);
    setOverlay("failed", "تعذّر فتح الكتاب: " + msg);
    post({ t: "error", gen: o.gen, code: e && e.name || "error", message: msg });
    return;
  }
  if (gen !== S.gen) { doc.destroy(); return; }
  S.doc = doc;
  const init = new Promise(r => eventBus.on("pagesinit", r, { once: true }));
  viewer.setDocument(doc);
  linkService.setDocument(doc, null);
  await init;
  if (gen !== S.gen) return;
  applySpread(o.spread || 0);
  viewer.currentScaleValue = o.scale || "page-width";
  restore(o);
  setOverlay("", "");
  $("pageCount").textContent = doc.numPages;
  updatePage();
  decorateMarks();
  container.focus();

  const info = await docInfo(doc).catch(() => ({}));
  if (gen !== S.gen) return;
  post({ t: "loaded", gen: o.gen, pages: doc.numPages, ...info });
  postState(true);
}

function restore(o) {
  const p = Math.min(Math.max(1, o.page || 1), S.doc.numPages);
  if (o.top != null && o.page) {
    viewer.scrollPageIntoView({ pageNumber: p, destArray: [null, { name: "XYZ" }, o.left ?? null, o.top, null], allowNegativeOffset: true });
  } else if (p > 1) {
    viewer.currentPageNumber = p;
  }
}

async function closeDoc() {
  inkClose();
  const d = S.doc, t = S.task;
  S.doc = null; S.task = null;
  findBarClose();
  if (E.ready) await E.close();
  if (Board.ready) Board.close();
  if (d) { viewer.setDocument(null); linkService.setDocument(null, null); }
  try { if (t) await t.destroy(); else if (d) await d.destroy(); } catch (e) { }
  $("pageCount").textContent = "0";
  $("pageInput").value = "";
}

async function close() {
  S.gen++;
  await closeDoc();
  $("bookTitle").textContent = "";
  setOverlay("empty", "افتح كتابًا من «الكتب»");
}

function setOverlay(kind, text) {
  document.body.classList.remove("empty", "loading", "failed");
  if (kind) document.body.classList.add(kind);
  $("overlayText").textContent = text;
}

// ---------------------------------------------------------------- بيانات الكتاب (الفهرس والبصمة)
async function docInfo(doc) {
  let title = null, author = null;
  try {
    const m = await doc.getMetadata();
    title = (m.info && m.info.Title) || null;
    author = (m.info && m.info.Author) || null;
  } catch (e) { }
  return { fingerprint: (doc.fingerprints && doc.fingerprints[0]) || null, title, author, outline: await outlineOf(doc) };
}

/** الفهرس كقايمة مسطحة (مستويين): [{title, page, level}] */
async function outlineOf(doc) {
  let items;
  try { items = await doc.getOutline(); } catch (e) { return []; }
  const out = [];
  async function pageOf(dest) {
    try {
      if (typeof dest === "string") dest = await doc.getDestination(dest);
      if (!Array.isArray(dest) || !dest.length) return null;
      const ref = dest[0];
      if (typeof ref === "number") return ref + 1;
      return (await doc.getPageIndex(ref)) + 1;
    } catch (e) { return null; }
  }
  async function walk(list, level) {
    for (const it of list || []) {
      if (out.length >= 600) return;
      const page = it.dest ? await pageOf(it.dest) : null;
      const title = (it.title || "").replace(/\s+/g, " ").trim();
      if (page && title) out.push({ title, page, level });
      if (level < 1 && it.items && it.items.length) await walk(it.items, level + 1);
    }
  }
  await walk(items, 0);
  return out;
}

async function probe(id, url) {
  if (isEpubUrl(url)) {
    try { post({ t: "probe", id, ok: true, ...(await E.probe(url)) }); }
    catch (e) { post({ t: "probe", id, ok: false, code: "epub", message: (e && e.message) || String(e) }); }
    return;
  }
  let task;
  try {
    task = pdfjsLib.getDocument({ ...DOC_OPTS, url });
    task.onPassword = () => { post({ t: "probe", id, ok: false, code: "password" }); task.destroy(); };
    const doc = await task.promise;
    const info = await docInfo(doc);
    post({ t: "probe", id, ok: true, pages: doc.numPages, ...info });
  } catch (e) {
    post({ t: "probe", id, ok: false, code: e && e.name || "error", message: (e && e.message) || String(e) });
  } finally {
    try { if (task) await task.destroy(); } catch (e) { }
  }
}

// ---------------------------------------------------------------- النص (للبحث والذكاء) وصور الصفحات (للـ OCR)
async function withDoc(url, fn) {
  if (!url) { if (!S.doc) throw new Error("لا يوجد كتاب مفتوح"); return fn(S.doc); }
  const task = pdfjsLib.getDocument({ ...DOC_OPTS, url });
  task.onPassword = () => task.destroy();
  try { return await fn(await task.promise); } finally { try { await task.destroy(); } catch (e) { } }
}
function textOf(content) {
  let s = "";
  for (const it of content.items) { if (it.str) s += it.str; s += it.hasEOL ? "\n" : (it.str ? " " : ""); }
  return s.replace(/[ \t]+/g, " ").replace(/ *\n */g, "\n").trim();
}
async function pageTexts(id, url, from, to) {
  if (isEpubUrl(url) || (!url && S.kind === "epub")) {
    try { await E.texts(url, (pages, total, done) => post({ t: "text", id, pages, total, done })); }
    catch (e) { post({ t: "text", id, pages: [], done: true, error: (e && e.message) || String(e) }); }
    return;
  }
  try {
    await withDoc(url, async doc => {
      const last = Math.min(to || doc.numPages, doc.numPages);
      let batch = [];
      for (let p = Math.max(1, from || 1); p <= last; p++) {
        const page = await doc.getPage(p);
        batch.push([p, textOf(await page.getTextContent())]);
        page.cleanup();
        if (batch.length >= 25) { post({ t: "text", id, pages: batch, total: doc.numPages }); batch = []; }
      }
      post({ t: "text", id, pages: batch, total: doc.numPages, done: true });
    });
  } catch (e) {
    post({ t: "text", id, pages: [], done: true, error: (e && e.message) || String(e) });
  }
}
async function renderPage(id, url, p, scale) {
  if (isEpubUrl(url) || (!url && S.kind === "epub")) { post({ t: "image", id, page: p, error: "EPUB لا يحتاج إلى OCR" }); return; }
  try {
    await withDoc(url, async doc => {
      const page = await doc.getPage(p);
      let vp = page.getViewport({ scale: scale || 2 });
      const max = 2400;
      if (Math.max(vp.width, vp.height) > max) vp = page.getViewport({ scale: (scale || 2) * max / Math.max(vp.width, vp.height) });
      const canvas = document.createElement("canvas");
      canvas.width = Math.floor(vp.width); canvas.height = Math.floor(vp.height);
      const ctx = canvas.getContext("2d");
      ctx.fillStyle = "#fff"; ctx.fillRect(0, 0, canvas.width, canvas.height);
      await page.render({ canvasContext: ctx, viewport: vp }).promise;
      post({ t: "image", id, page: p, data: canvas.toDataURL("image/png").split(",")[1] });
      page.cleanup();
    });
  } catch (e) {
    post({ t: "image", id, page: p, error: (e && e.message) || String(e) });
  }
}

// ---------------------------------------------------------------- الحالة للبرنامج
let stateTimer = 0;
function postState(now) {
  if (!hasDoc()) return;
  clearTimeout(stateTimer);
  const send = () => {
    const loc = viewer._location || null;
    const msg = S.kind === "board" ? { t: "state", gen: S.opts.gen, ...Board.state(), spread: 0, theme: S.theme, rtl: S.rtl } : S.kind === "epub" ? { t: "state", gen: S.opts.gen, ...E.state(), theme: S.theme, rtl: S.rtl } : {
      t: "state", gen: S.opts.gen, page: viewer.currentPageNumber, pages: S.doc ? S.doc.numPages : 0,
      scale: String(viewer.currentScaleValue), top: loc ? loc.top : null, left: loc ? loc.left : null,
      spread: viewer.spreadMode, theme: S.theme, rtl: S.rtl
    };
    const key = JSON.stringify(msg);
    if (key === S.lastState) return;
    S.lastState = key;
    post(msg);
  };
  if (now) send(); else stateTimer = setTimeout(send, 350);
}
eventBus.on("updateviewarea", () => postState(false));
eventBus.on("pagechanging", () => { updatePage(); postState(false); });
eventBus.on("scalechanging", () => { updateZoomSelect(); postState(false); });
eventBus.on("pagerendered", e => decorate(e.pageNumber));

// ---------------------------------------------------------------- الصفحات اللي اتقرت (قعدت قدامك readSeconds ثانية)
function visiblePages() {
  if (S.kind === "epub") return E.visiblePages();
  if (S.kind === "board") return [];
  if (!S.doc) return [];
  const cr = container.getBoundingClientRect();
  const area = cr.width * cr.height;
  const cur = viewer.currentPageNumber, out = [];
  for (let p = Math.max(1, cur - 3); p <= Math.min(S.doc.numPages, cur + 3); p++) {
    const pv = viewer.getPageView(p - 1);
    if (!pv || !pv.div) continue;
    const r = pv.div.getBoundingClientRect();
    const w = Math.max(0, Math.min(r.right, cr.right) - Math.max(r.left, cr.left));
    const h = Math.max(0, Math.min(r.bottom, cr.bottom) - Math.max(r.top, cr.top));
    const vis = w * h, pageArea = r.width * r.height;
    if (vis <= 0 || pageArea <= 0) continue;
    if (vis / pageArea >= 0.6 || vis / area >= 0.45) out.push(p);
  }
  return out;
}
const IDLE_MS = 10 * 60 * 1000;
let tickCount = 0;
setInterval(() => {
  if (!hasDoc()) return;
  const active = document.visibilityState === "visible" && Date.now() - S.lastInput < IDLE_MS;
  if (active) {
    const fresh = [];
    for (const p of visiblePages()) {
      if (S.read.has(p)) continue;
      const d = (S.dwell.get(p) || 0) + 1;
      S.dwell.set(p, d);
      if (d >= S.readSeconds) { S.read.add(p); S.dwell.delete(p); fresh.push(p); }
    }
    if (fresh.length) post({ t: "read", gen: S.opts.gen, pages: fresh });
  }
  if (++tickCount % 5 === 0) post({ t: "tick", gen: S.opts.gen, active });
}, 1000);
for (const ev of ["wheel", "keydown", "mousedown", "mousemove", "touchstart"])
  window.addEventListener(ev, () => { S.lastInput = Date.now(); }, { passive: true, capture: true });
container.addEventListener("scroll", () => { S.lastInput = Date.now(); }, { passive: true });

// ---------------------------------------------------------------- التنقل والتكبير
function updatePage() {
  if (document.activeElement !== $("pageInput")) $("pageInput").value = hasDoc() ? curPage() : "";
}
function goTo(p) {
  if (S.kind === "board") { Board.goTo(p); return; }
  if (S.kind === "epub") { E.goTo(Math.round(p)); return; }
  if (!S.doc) return;
  p = Math.min(Math.max(1, Math.round(p)), S.doc.numPages);
  viewer.currentPageNumber = p;
  container.focus();
}
function step(d) {
  if (S.kind === "board") { Board.step(d); return; }
  if (S.kind === "epub") { E.step(d); return; }
  if (!S.doc) return;
  if (d > 0) viewer.nextPage(); else viewer.previousPage();
}
const ZOOMS = [0.5, 0.67, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4];
function zoom(dir) {
  if (S.kind === "board") { Board.zoom(dir); return; }
  if (S.kind === "epub") { E.zoom(dir); return; }
  if (!S.doc) return;
  const cur = viewer.currentScale;
  const next = dir > 0 ? ZOOMS.find(z => z > cur + 0.01) : [...ZOOMS].reverse().find(z => z < cur - 0.01);
  if (next) viewer.currentScaleValue = String(next);
}
function updateZoomSelect() {
  const sel = $("zoomSelect"), v = String(viewer.currentScaleValue);
  const opt = [...sel.options].find(o => o.value === v);
  if (opt) sel.value = v;
  else { const c = sel.querySelector('option[value="custom"]'); c.textContent = Math.round(viewer.currentScale * 100) + "%"; sel.value = "custom"; }
}
function applySpread(mode) {
  S.spread = mode;
  if (S.kind === "epub") E.setSpread(mode); else viewer.spreadMode = mode;
  $("spread").classList.toggle("on", mode !== 0);
  $("spread").textContent = mode === 0 ? "▯" : "▯▯";
}
function setTheme(t, notify = true) {
  S.theme = ["normal", "night", "sepia"].includes(t) ? t : "normal";
  document.body.classList.toggle("night", S.theme === "night");
  document.body.classList.toggle("sepia", S.theme === "sepia");
  $("theme").textContent = S.theme === "night" ? "☾" : S.theme === "sepia" ? "📜" : "☀";
  if (E.ready) E.setTheme(S.theme);
  if (notify) postState(true);
}
function setRtl(on, notify = true) {
  S.rtl = !!on;
  container.classList.toggle("rtl", S.rtl);
  $("direction").classList.toggle("on", S.rtl);
  if (notify) postState(true);
}

// ---------------------------------------------------------------- العلامات والتظليل
function decorate(p) {
  const pv = viewer.getPageView(p - 1);
  if (!pv || !pv.div) return;
  pv.div.classList.toggle("marked", S.marks.has(p));
  drawHighlights(pv.div, p);
  if (S.kind === "pdf" && pv.viewport) Ink.attach(pv.div, p, pv.viewport.height / pv.viewport.width);
}

S.highlights = [];
function drawHighlights(div, p) {
  let layer = div.querySelector(":scope > .masar-hl-layer");
  const mine = S.highlights.filter(h => h.page === p);
  if (!mine.length) { if (layer) layer.remove(); return; }
  if (!layer) {
    layer = document.createElement("div");
    layer.className = "masar-hl-layer";
    const text = div.querySelector(":scope > .textLayer");
    div.insertBefore(layer, text || null);
  }
  layer.textContent = "";
  for (const h of mine)
    for (const r of h.rects) {
      const d = document.createElement("div");
      d.className = h.color + (h.id === S.activeHl ? " active" : "");
      d.style.left = (r[0] * 100) + "%"; d.style.top = (r[1] * 100) + "%";
      d.style.width = (r[2] * 100) + "%"; d.style.height = (r[3] * 100) + "%";
      layer.appendChild(d);
    }
}

/** التحديد الحالي كمستطيلات نسبية لكل صفحة: [{page, rects, text}] */
function selectionByPage() {
  const sel = window.getSelection();
  if (!sel || sel.isCollapsed || !sel.rangeCount || !S.doc) return [];
  const range = sel.getRangeAt(0);
  if (!container.contains(range.commonAncestorContainer)) return [];
  const pages = [...container.querySelectorAll(".page[data-page-number]")].map(div => ({ div, r: div.getBoundingClientRect(), p: +div.dataset.pageNumber }));
  const out = new Map();
  for (const cr of range.getClientRects()) {
    if (cr.width < 1 || cr.height < 1) continue;
    const cx = cr.left + cr.width / 2, cy = cr.top + cr.height / 2;
    const pg = pages.find(x => cx >= x.r.left && cx <= x.r.right && cy >= x.r.top && cy <= x.r.bottom);
    if (!pg) continue;
    const list = out.get(pg.p) || [];
    list.push([(cr.left - pg.r.left) / pg.r.width, (cr.top - pg.r.top) / pg.r.height, cr.width / pg.r.width, cr.height / pg.r.height].map(v => Math.round(v * 10000) / 10000));
    out.set(pg.p, list);
  }
  const text = sel.toString().replace(/\s+/g, " ").trim();
  return [...out.entries()].map(([page, rects]) => ({ page, rects: mergeRects(rects), text }));
}

/** مستطيلات نفس السطر تبقى واحد (طبقة النص بتقسّم السطر حتت). */
function mergeRects(rects) {
  rects.sort((a, b) => a[1] - b[1] || a[0] - b[0]);
  const out = [];
  for (const r of rects) {
    const last = out[out.length - 1];
    if (last && Math.abs(last[1] - r[1]) < 0.004 && Math.abs(last[3] - r[3]) < 0.006 && r[0] <= last[0] + last[2] + 0.01) {
      const right = Math.max(last[0] + last[2], r[0] + r[2]);
      last[0] = Math.min(last[0], r[0]); last[2] = right - last[0];
    } else if (!out.some(o => Math.abs(o[0] - r[0]) < 0.002 && Math.abs(o[1] - r[1]) < 0.002)) out.push([...r]);
  }
  return out;
}

const hlbar = $("hlbar");
let pendingSel = null;
function showHlBar(x, y, editId) {
  S.activeHl = editId || null;
  $("hlDelete").hidden = !editId;
  $("hlNote").hidden = !!editId;
  hlbar.hidden = false;
  const w = hlbar.offsetWidth, h = hlbar.offsetHeight;
  hlbar.style.left = Math.max(4, Math.min(window.innerWidth - w - 4, x - w / 2)) + "px";
  hlbar.style.top = Math.max(44, y - h - 10) + "px";
  if (editId) decorateMarks();
}
function hideHlBar() {
  if (hlbar.hidden) return;
  hlbar.hidden = true;
  const had = S.activeHl;
  S.activeHl = null;
  pendingSel = null;
  if (had) decorateMarks();
}
container.addEventListener("mouseup", e => {
  if (e.button !== 0) return;
  setTimeout(() => {
    const parts = selectionByPage();
    if (parts.length && parts[0].text.length > 0) {
      pendingSel = parts;
      showHlBar(e.clientX, e.clientY);
      return;
    }
    // كليك على تظليل موجود: تغيير اللون أو الشيل
    const hit = highlightAt(e.clientX, e.clientY);
    if (hit) showHlBar(e.clientX, e.clientY, hit.id); else hideHlBar();
  }, 0);
});
function highlightAt(x, y) {
  const div = document.elementsFromPoint(x, y).map(el => el.closest && el.closest(".page[data-page-number]")).find(Boolean);
  if (!div) return null;
  const r = div.getBoundingClientRect(), p = +div.dataset.pageNumber;
  const nx = (x - r.left) / r.width, ny = (y - r.top) / r.height;
  return S.highlights.find(h => h.page === p && h.rects.some(q => nx >= q[0] && nx <= q[0] + q[2] && ny >= q[1] && ny <= q[1] + q[3])) || null;
}
hlbar.addEventListener("mousedown", e => e.preventDefault()); // التحديد مايضيعش
for (const b of hlbar.querySelectorAll(".sw"))
  b.addEventListener("click", () => {
    const color = b.dataset.color;
    if (S.activeHl) post({ t: "hledit", gen: S.opts.gen, id: S.activeHl, color });
    else if (pendingSel) for (const part of pendingSel) post({ t: "highlight", gen: S.opts.gen, ...part, color, note: false });
    window.getSelection().removeAllRanges();
    hideHlBar();
  });
$("hlNote").onclick = () => {
  if (pendingSel) for (const part of pendingSel) post({ t: "highlight", gen: S.opts.gen, ...part, color: "yellow", note: true });
  window.getSelection().removeAllRanges();
  hideHlBar();
};
$("hlDelete").onclick = () => { if (S.activeHl) post({ t: "hledit", gen: S.opts.gen, id: S.activeHl, del: true }); hideHlBar(); };
container.addEventListener("scroll", () => hideHlBar(), { passive: true });
function decorateMarks() {
  if (!S.doc) return;
  for (let p = 1; p <= S.doc.numPages; p++) decorate(p);
}

// ---------------------------------------------------------------- البحث
function findBarOpen() {
  $("findbar").hidden = false;
  $("findButton").classList.add("on");
  const inp = $("findInput");
  inp.focus(); inp.select();
}
function findBarClose() {
  if ($("findbar").hidden) return false;
  $("findbar").hidden = true;
  $("findButton").classList.remove("on");
  $("findResult").textContent = "";
  eventBus.dispatch("findbarclose", { source: null });
  container.focus();
  return true;
}
function find(again, previous) {
  const q = $("findInput").value;
  if (S.kind === "epub") {
    if (q.trim().length < 2) { $("findResult").textContent = ""; return; }
    $("findResult").textContent = "جارٍ البحث…";
    E.find(q, again, previous).then(m => { $("findResult").textContent = m.total ? `${m.current} من ${m.total}` : "لا توجد نتائج"; });
    return;
  }
  eventBus.dispatch("find", {
    source: null, type: again ? "again" : "", query: q, caseSensitive: false, entireWord: false,
    highlightAll: true, findPrevious: !!previous, matchDiacritics: false
  });
}
eventBus.on("updatefindmatchescount", e => showMatches(e.matchesCount));
eventBus.on("updatefindcontrolstate", e => {
  if (e.state === 1) $("findResult").textContent = "لا توجد نتائج";
  else showMatches(e.matchesCount);
});
function showMatches(m) {
  if (!m || !m.total) return;
  $("findResult").textContent = `${m.current} من ${m.total}`;
}

// ---------------------------------------------------------------- الأزرار والمفاتيح
$("prev").onclick = () => step(-1);
$("next").onclick = () => step(1);
$("zoomIn").onclick = () => zoom(1);
$("zoomOut").onclick = () => zoom(-1);
$("zoomSelect").onchange = e => { if (S.kind === "pdf" && S.doc && e.target.value !== "custom") viewer.currentScaleValue = e.target.value; container.focus(); };
$("spread").onclick = () => { if (hasDoc()) { applySpread(S.spread === 0 ? 1 : 0); postState(true); } };
$("direction").onclick = () => setRtl(!S.rtl);
$("theme").onclick = () => setTheme(S.theme === "normal" ? "night" : S.theme === "night" ? "sepia" : "normal");
$("findButton").onclick = () => ($("findbar").hidden ? findBarOpen() : findBarClose());
$("mark").onclick = () => { if (hasDoc()) post({ t: "cmd", gen: S.opts.gen, name: "mark", page: curPage() }); };
$("fullscreen").onclick = () => post({ t: "cmd", gen: S.opts.gen, name: "fullscreen" });
$("closeReader").onclick = () => post({ t: "cmd", gen: S.opts.gen, name: "close" });
$("ai").onclick = () => post({ t: "cmd", gen: S.opts.gen, name: "ai", page: curPage() });
$("findInput").addEventListener("input", () => find(false, false));
$("findInput").addEventListener("keydown", e => {
  if (e.key === "Enter") { e.preventDefault(); find(true, e.shiftKey); }
  else if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); findBarClose(); }
});
$("findNext").onclick = () => find(true, false);
$("findPrev").onclick = () => find(true, true);
$("findClose").onclick = () => findBarClose();
const pageInput = $("pageInput");
pageInput.addEventListener("keydown", e => {
  if (e.key === "Enter") { e.preventDefault(); const n = parseInt(toLatin(pageInput.value), 10); if (n > 0) goTo(n); else updatePage(); }
  else if (e.key === "Escape") { e.preventDefault(); updatePage(); container.focus(); }
});
pageInput.addEventListener("blur", updatePage);
pageInput.addEventListener("focus", () => pageInput.select());
function toLatin(s) { return String(s).replace(/[٠-٩]/g, d => "٠١٢٣٤٥٦٧٨٩".indexOf(d)).replace(/[۰-۹]/g, d => "۰۱۲۳۴۵۶۷۸۹".indexOf(d)); }

function typing(el) { return el && (el.tagName === "INPUT" || el.tagName === "SELECT" || el.tagName === "TEXTAREA"); }
function onKeyDown(e) {
  const k = e.key, ctrl = e.ctrlKey || e.metaKey;
  if (document.body.classList.contains("pe-on")) return; // محرر الصفحات بيتعامل مع الكيبورد بتاعه
  if (document.body.classList.contains("sci-on") || (e.target && e.target.closest && e.target.closest(".sci-panel, .sci-dlg, .ML__keyboard"))) return; // محرر المعادلات
  if (InkUI.key(e)) return;
  if (k === "F5" && (S.kind === "board" || (S.kind === "pdf" && S.doc)) && !ctrl && !e.altKey) { e.preventDefault(); setPresent(); return; }
  if (k === "Escape" && document.body.classList.contains("present")) { e.preventDefault(); setPresent(false); return; }
  if (ctrl && e.shiftKey && (k === "d" || k === "D") && (S.kind === "board" || (S.kind === "pdf" && S.doc))) { e.preventDefault(); toggleInk(); return; }
  if (ctrl && !e.altKey && !e.shiftKey && (k === "w" || k === "W" || e.code === "KeyW")) { e.preventDefault(); post({ t: "cmd", gen: S.opts.gen, name: "close" }); return; }
  if (ctrl && !e.altKey && (k === "f" || k === "F")) { e.preventDefault(); findBarOpen(); return; }
  if (ctrl && (k === "g" || k === "G")) { e.preventDefault(); pageInput.focus(); return; }
  if (ctrl && (k === "=" || k === "+")) { e.preventDefault(); zoom(1); return; }
  if (ctrl && (k === "-" || k === "_")) { e.preventDefault(); zoom(-1); return; }
  if (ctrl && k === "0") { e.preventDefault(); if (S.kind === "board") Board.zoom(0); else if (S.kind === "epub") E.zoom(0); else if (S.doc) viewer.currentScaleValue = "page-width"; return; }
  if (k === "Escape" && !hlbar.hidden) { e.preventDefault(); hideHlBar(); return; }
  if (k === "Escape" && findBarClose()) { e.preventDefault(); return; }
  const inText = typing(document.activeElement);
  if (!inText && !ctrl && !e.altKey) {
    if (k === "ArrowRight" || k === "ArrowLeft") {
      if (S.kind === "epub" || viewer.currentScaleValue === "page-fit" || viewer.spreadMode !== 0 || container.scrollWidth <= container.clientWidth + 2) {
        e.preventDefault();
        const forward = (k === "ArrowLeft") === (S.kind === "epub" ? E.rtl : S.rtl);
        step(forward ? 1 : -1);
        return;
      }
    }
    if ((S.kind === "epub" || S.kind === "board") && k === "PageDown") { e.preventDefault(); step(1); return; }
    if (S.kind === "epub" && k === " ") { e.preventDefault(); step(1); return; }
    if ((S.kind === "epub" || S.kind === "board") && k === "PageUp") { e.preventDefault(); step(-1); return; }
    if (k === "Home" && hasDoc()) { e.preventDefault(); goTo(1); return; }
    if (k === "End" && hasDoc()) { e.preventDefault(); goTo(pageTotal()); return; }
    if (k === "b" || k === "B") { e.preventDefault(); $("mark").click(); return; }
  }
  // اختصارات البرنامج (F11، F6، Esc، Ctrl+…، Alt+…) بتتنفذ في «مسار»
  const fn = /^F\d{1,2}$/.test(k);
  const forward = fn || k === "Escape" || e.altKey || (ctrl && !inText && !["c", "C", "a", "A"].includes(k)) || (ctrl && inText && !["c", "C", "a", "A", "v", "V", "x", "X", "z", "Z"].includes(k));
  if (forward && !["Control", "Shift", "Alt", "Meta"].includes(k)) {
    e.preventDefault();
    post({ t: "key", k: e.keyCode, c: ctrl, s: e.shiftKey, a: e.altKey });
  }
}
window.addEventListener("keydown", onKeyDown, true);
// تكبير بـ Ctrl+العجلة جوه الكتاب (مش تكبير الصفحة كلها)
container.addEventListener("wheel", e => { if (e.ctrlKey) { e.preventDefault(); zoom(e.deltaY < 0 ? 1 : -1); } }, { passive: false });

// ---------------------------------------------------------------- واجهة البرنامج
window.masar = {
  open: o => { open(o); return "ok"; },
  close: () => { close(); return "ok"; },
  goTo: p => { goTo(p); return "ok"; },
  step: d => { step(d); return "ok"; },
  setMarks: pages => { S.marks = new Set(pages || []); decorateMarks(); return "ok"; },
  setHighlights: list => { S.highlights = list || []; if (S.kind === "epub") E.setHighlights(S.highlights); else decorateMarks(); return "ok"; },
  pageText: (id, from, to) => { pageTexts(id, null, from, to); return "ok"; },
  extract: (id, url, from, to) => { pageTexts(id, url, from, to); return "ok"; },
  renderPage: (id, url, page, scale) => { renderPage(id, url, page, scale); return "ok"; },
  selectionForTest: () => JSON.stringify(selectionByPage()),
  setRead: pages => { S.read = new Set(pages || []); return "ok"; },
  setReadSeconds: s => { S.readSeconds = Math.max(1, s | 0); return "ok"; },
  probe: (id, url) => { probe(id, url); return "ok"; },
  focus: () => { container.focus(); return "ok"; },
  find: q => { findBarOpen(); $("findInput").value = q; find(false, false); return "ok"; },
  setInk: json => { Ink.mergeRemote(json); InkUI.refresh(); return "ok"; },
  setInkPrefs: json => { Ink.setPrefs(json); applyInkPrefs(); return "ok"; },
  inkFlush: () => { Ink.flush(); return "ok"; },
  ink: on => { if (on == null) toggleInk(); else setInk(!!on); return "ok"; },
  inkState: () => JSON.stringify({ on: Ink.on, tool: Ink.tool, count: Ink.count(), layers: Ink.surfaces.size, json: Ink.doc.toJSON() }),
  inkTest: () => ({ Ink, InkUI }),
  /** نسخة مشروحة: الأصل + الشرح ← PDF بيتبعت للبرنامج على save. النتيجة: رسالة job. */
  annotate: (id, src, save, inkJson, title) => { runAnnotate(id, src, save, inkJson, title); return "ok"; },
  boardPdfFile: (id, boardJson, inkJson, save, title) => { runBoardFile(id, boardJson, inkJson, save, title); return "ok"; },
  compose: (id, partsJson, save, title) => { runCompose(id, partsJson, save, title); return "ok"; },
  textPage: (id, pageJson, save) => { runTextPage(id, pageJson, save); return "ok"; },
  /** محرر الصفحات (البرنامج بيفتحه على نسخة العمل بس). */
  pageEditor: () => { openPageEditor(); return "ok"; },
  /** رد البرنامج على طلب ملف (فتح/حفظ): رابط أو null. */
  pickResult: (id, url) => { const r = picks.get(id); if (r) { picks.delete(id); r(url || null); } return "ok"; },
  peTest: () => PageEditor,
  boardTest: () => Board,
  sciTest: () => ({ Sci, Chem: SciChem, Shapes: SciShapes }),
  /** لقطة من نافذة مصادر العلوم (PhET/GeoGebra/Desmos) ← على الصفحة المفتوحة. */
  sciCaptured: d => { Sci.webCaptured(d); return "ok"; },
  boardExport: (which, send) => { if (S.kind === "board") Board.exportPdf(which || "all", !!send); return "ok"; },
  /** لقطة شاشة جاية من البرنامج ← صفحة جديدة في السبورة. */
  boardImage: (dataUrl, name) => { if (S.kind === "board") Board.addImagePage(dataUrl, name); return "ok"; },
  /** صورة الصفحة بالشرح (PNG) ← save. */
  pagePng: (id, page, save, scale) => { runPagePng(id, page, save, scale); return "ok"; },
  state: () => JSON.stringify({ kind: S.kind, page: hasDoc() ? curPage() : 0, pages: pageTotal(), scale: S.kind === "epub" ? "font:" + E.font : String(viewer.currentScaleValue), spread: S.spread, theme: S.theme, rtl: S.rtl, read: [...S.read], gen: S.opts.gen })
};
// ---------------------------------------------------------------- النسخة المشروحة وصورة الصفحة
async function runAnnotate(id, src, save, inkJson, title) {
  try {
    const ink = typeof inkJson === "string" ? JSON.parse(inkJson) : inkJson;
    const r = await buildAnnotated({ src, save, ink, title, pdfjsLib, docOpts: DOC_OPTS, onProgress: p => post({ t: "job", id, progress: p }) });
    post({ t: "job", id, ok: true, size: r.size, pages: r.pages });
  } catch (e) { post({ t: "job", id, ok: false, error: (e && e.message) || String(e) }); }
}
/** سبورة غير مفتوحة ← PDF (لتصدير الحصة). */
async function runBoardFile(id, boardJson, inkJson, save, title) {
  try {
    const meta = typeof boardJson === "string" ? JSON.parse(boardJson) : boardJson;
    const plans = await boardPlans(meta, inkJson);
    if (!plans.length) throw new Error("السبورة فارغة");
    const r = await runJob({ job: "board", save, plans, title: title || "سبورة" }, p => post({ t: "job", id, progress: p }));
    post({ t: "job", id, ok: true, size: r.size, pages: r.pages });
  } catch (e) { post({ t: "job", id, ok: false, error: (e && e.message) || String(e) }); }
}
/** صفحة A4 نصية (غلاف/واجب) كصورة PNG: المتصفح يشكّل العربية والرموز صحيحًا. */
async function runTextPage(id, pageJson, save) {
  try {
    const p = typeof pageJson === "string" ? JSON.parse(pageJson) : pageJson;
    const W = 1240, H = 1754, M = 110, width = W - 2 * M;
    const c = document.createElement("canvas"); c.width = W; c.height = H;
    const x = c.getContext("2d");
    x.fillStyle = "#fff"; x.fillRect(0, 0, W, H);
    x.fillStyle = "#005caf"; x.fillRect(0, 0, W, 18);
    x.direction = "rtl"; x.textAlign = "right"; x.textBaseline = "top";
    const font = (size, bold) => `${bold ? "700 " : ""}${size}px "Segoe UI", Tahoma, "Noto Sans Arabic", sans-serif`;
    let y = 90;
    const wrap = (text, size, bold, color, indent = 0) => {
      x.font = font(size, bold); x.fillStyle = color;
      const words = String(text).split(/\s+/), max = width - indent, lh = Math.round(size * 1.55);
      let line = "";
      const flush = () => { if (y < H - 140) x.fillText(line, W - M - indent, y); y += lh; line = ""; };
      for (const w of words) {
        const t = line ? line + " " + w : w;
        if (line && x.measureText(t).width > max) { flush(); line = w; } else line = t;
      }
      if (line) flush();
    };
    wrap(p.title || "", 54, true, "#1e1e22");
    y += 6;
    if (p.subtitle) { wrap(p.subtitle, 26, false, "#6e6e76"); y += 14; }
    x.fillStyle = "#dcdee4"; x.fillRect(M, y, width, 2); y += 32;
    for (const s of p.sections || []) {
      if (y > H - 220) break;
      if (s.head) { wrap(s.head, 32, true, "#005caf"); y += 8; }
      for (const l of s.lines || []) {
        if (y > H - 170) { x.font = font(26); x.fillStyle = "#6e6e76"; x.fillText("…", W - M, y); y = H; break; }
        x.font = font(26); x.fillStyle = "#1e1e22"; x.fillText("•", W - M, y);
        wrap(l, 26, false, "#1e1e22", 34); y += 6;
      }
      y += 26;
    }
    x.font = font(24); x.fillStyle = "#6e6e76"; x.fillText(p.footer || "أُعدّ بـ «مسار»", W - M, H - 90);
    const blob = await new Promise(r => c.toBlob(r, "image/png"));
    const res = await fetch(save, { method: "POST", body: blob, headers: { "Content-Type": "image/png" } });
    if (!res.ok) throw new Error("فشل الحفظ");
    post({ t: "job", id, ok: true, size: blob.size });
  } catch (e) { post({ t: "job", id, ok: false, error: (e && e.message) || String(e) }); }
}
/** ملف واحد من أجزاء: صفحات من ملفات PDF وصور (صفحة لكل صورة). */
async function runCompose(id, partsJson, save, title) {
  try {
    const parts = typeof partsJson === "string" ? JSON.parse(partsJson) : partsJson;
    const r = await runJob({ job: "compose", save, parts, title }, p => post({ t: "job", id, progress: p }));
    post({ t: "job", id, ok: true, size: r.size, pages: r.pages });
  } catch (e) { post({ t: "job", id, ok: false, error: (e && e.message) || String(e) }); }
}
async function runPagePng(id, page, save, scale) {
  try {
    if (!S.doc) throw new Error("لا يوجد كتاب مفتوح");
    Ink.flush();
    const pg = await S.doc.getPage(page);
    const png = await pagePng({ pdfPage: pg, items: Ink.doc.items(String(page)), scale: scale || 2 });
    const r = await fetch(save, { method: "POST", body: png, headers: { "Content-Type": "image/png" } });
    if (!r.ok) throw new Error("فشل الحفظ");
    post({ t: "job", id, ok: true, size: png.length });
  } catch (e) { post({ t: "job", id, ok: false, error: (e && e.message) || String(e) }); }
}

// ---------------------------------------------------------------- محرر الصفحات
const picks = new Map();
let pickSeq = 0;
/** طلب من البرنامج: open-pdf (ملف يتضم) / save-pdf (مكان حفظ) / save-self (نسخة العمل نفسها). */
function pick(kind, name) {
  const id = "p" + (++pickSeq);
  return new Promise(r => { picks.set(id, r); post({ t: "pick", gen: S.opts.gen, id, kind, name: name || "" }); });
}
async function openPageEditor() {
  if (S.kind !== "pdf" || !S.doc || document.body.classList.contains("pe-on")) return;
  if (Ink.on) InkUI.setOn(false);
  Ink.flush();
  await PageEditor.open({
    doc: S.doc, src: S.opts.url, title: S.opts.title, pick, pdfjsLib, docOpts: DOC_OPTS, rtl: S.rtl,
    onMap: map => post({ t: "pemap", gen: S.opts.gen, map }),
    goTo: p => goTo(p),
    onSaved: r => post({ t: "cmd", gen: S.opts.gen, name: "pe-saved", page: r && r.pages || 0 }),
    onClose: () => container.focus()
  });
}
$("pagesBtn").addEventListener("click", () => post({ t: "cmd", gen: S.opts.gen, name: "pe-open", page: curPage() }));

// ---------------------------------------------------------------- السبورة
Board.init({
  area: boardArea, post: m => post({ ...m, gen: S.opts.gen }), pick, pdfjsLib, docOpts: DOC_OPTS,
  onMoved: () => { $("pageCount").textContent = Board.total; updatePage(); postState(false); }
});

// ---------------------------------------------------------------- أدوات الشرح
Ink.init({ post: m => post({ ...m, gen: S.opts.gen }), container });
Ink.keepAlive($("viewer"));
Ink.onPinch = (f, cx, cy) => { if (S.kind === "board") Board.zoomAt(f, cx, cy); else if (S.doc) viewer.currentScale = Math.max(0.25, Math.min(6, viewer.currentScale * f)); };
Ink.onPan = (dx, dy) => { const el = S.kind === "board" ? Board.scroller : container; el.scrollLeft += dx; el.scrollTop += dy; };
Ink.onGoto = key => {
  if (S.kind === "board") { const i = Board.pageOf(key); if (i >= 0 && i + 1 !== Board.page()) Board.goTo(i + 1); return; }
  const p = +key; if (p && S.doc && Math.abs(p - viewer.currentPageNumber) > 1) goTo(p);
};
InkUI.init({
  onToggle: on => { $("inkBtn").classList.toggle("on", on); post({ t: "cmd", gen: S.opts.gen, name: on ? "ink-on" : "ink-off", page: curPage() }); },
  onPresent: on => setPresent(on),
  onExport: () => post({ t: "cmd", gen: S.opts.gen, name: "ink-save", page: curPage() })
});
function applyInkPrefs() {
  Ink.penOnly = !!Ink.prefs.penOnly; Ink.recognize = !!Ink.prefs.recognize;
  InkUI.applyBarPos(); InkUI.refresh();
}
function inkOpen(o) {
  Ink.setOn(false); InkUI.setOn(false); Ink.clearSurfaces();
  Ink.load(o.ink || null);
  if (o.inkPrefs) Ink.setPrefs(o.inkPrefs);
  applyInkPrefs();
  $("inkBtn").hidden = o.format === "epub"; // EPUB: التظليل بس (الصفحات بتتغير مع حجم الخط)
  $("pagesBtn").hidden = o.format !== "pdf" && o.format !== undefined;
  if (o.format === "board") for (const id of ["spread", "direction", "theme", "findButton", "ai", "zoomSelect", "mark"]) $(id).hidden = true;
  else for (const id of ["spread", "theme", "findButton", "ai", "mark"]) $(id).hidden = false;
  if (document.body.classList.contains("pe-on")) PageEditor.close(true);
}
function inkClose() { if (Ink.on) InkUI.setOn(false); Ink.flush(); Ink.clearSurfaces(); setPresent(false); }
function setInk(on) {
  if (on && !(S.kind === "board" || (S.kind === "pdf" && S.doc))) return;
  if (on === Ink.on) return;
  hideHlBar();
  InkUI.setOn(on);
  if (on && !Ink.prefs.seenTip) { Ink.prefs.seenTip = 1; Ink.savePrefs(); InkUI.say("ارسم على الكتاب — لا يُمس الأصل ويُحفظ الشرح تلقائيًا. Esc = تحريك الصفحة", 5000); }
}
function toggleInk() { setInk(!Ink.on); }
let presentPrev = null;
function setPresent(on) {
  if (on == null) on = !document.body.classList.contains("present");
  if (on === document.body.classList.contains("present")) return;
  document.body.classList.toggle("present", on);
  if (S.kind === "board") {
    if (on) { presentPrev = { board: Board.k }; Board.k = 1; } else if (presentPrev) { Board.k = presentPrev.board ?? 1; presentPrev = null; }
    Board.layout(); Board.goTo(Board.page(), true);
    post({ t: "cmd", gen: S.opts.gen, name: on ? "present-on" : "present-off", page: Board.page() });
  } else if (on && S.doc) {
    // صفحة واحدة بس على الشاشة، مقاسها كامل، والأسهم/المسافة صفحة صفحة
    const p = viewer.currentPageNumber;
    presentPrev = { scale: viewer.currentScaleValue, scroll: viewer.scrollMode, spread: viewer.spreadMode };
    viewer.spreadMode = 0;
    viewer.scrollMode = 3; // PAGE
    viewer.currentScaleValue = "page-fit";
    viewer.currentPageNumber = p;
    post({ t: "cmd", gen: S.opts.gen, name: "present-on", page: p });
  } else if (!on && S.doc && presentPrev) {
    const p = viewer.currentPageNumber;
    viewer.scrollMode = presentPrev.scroll;
    viewer.spreadMode = presentPrev.spread;
    viewer.currentScaleValue = presentPrev.scale;
    viewer.currentPageNumber = p;
    presentPrev = null;
    post({ t: "cmd", gen: S.opts.gen, name: "present-off", page: p });
  }
  if (on) InkUI.say("وضع العرض — الأسهم/المسافة للصفحات، F5 أو Esc للخروج", 3500);
}
window.addEventListener("resize", () => { if (presentPrev && S.doc && S.kind === "pdf") viewer.currentScaleValue = "page-fit"; });
$("inkBtn").addEventListener("click", () => toggleInk());
{ const prev = Ink.onChange; Ink.onChange = () => { if (prev) prev(); if (S.kind === "board") Board.refreshSoon(); }; }
// وضع العرض: الأسهم والمسافة صفحة صفحة حتى لو أدوات الشرح مقفولة
window.addEventListener("keydown", e => {
  if (!document.body.classList.contains("present") || e.ctrlKey || e.altKey) return;
  if (document.activeElement && (document.activeElement.isContentEditable || /INPUT|TEXTAREA/.test(document.activeElement.tagName))) return;
  const k = e.key;
  if (k === " " || k === "PageDown" || k === "ArrowDown") { e.preventDefault(); e.stopPropagation(); step(1); }
  else if (k === "PageUp" || k === "ArrowUp" || k === "Backspace") { e.preventDefault(); e.stopPropagation(); step(-1); }
}, true);
window.addEventListener("beforeunload", () => Ink.flush());
document.addEventListener("visibilitychange", () => { if (document.hidden) Ink.flush(); });

post({ t: "ready" });
