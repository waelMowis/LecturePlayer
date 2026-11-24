import { Epub as E } from "./epub.js";
const host = window.chrome && window.chrome.webview;
function post(msg) {
  const s = JSON.stringify(msg);
  if (host) host.postMessage(s); else (window.__posted ||= []).push(msg);
}
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
const hasDoc = () => S.kind === "epub" ? E.ready : !!S.doc;
const curPage = () => S.kind === "epub" ? E.page() : viewer.currentPageNumber;
const pageTotal = () => S.kind === "epub" ? E.total : (S.doc ? S.doc.numPages : 0);
E.init({
  onMoved: () => { updatePage(); postState(false); },
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
  S.lastInput = Date.now();
  S.lastState = "";
  setTheme(o.theme || S.theme, false);
  setRtl(!!o.rtl, false);
  $("bookTitle").textContent = o.title || "";
  document.title = o.title || "قارئ مسار";
  setOverlay("loading", "بيفتح الكتاب…");

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
      setOverlay("failed", "مقدرتش أفتح الكتاب: " + msg);
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
  $("direction").hidden = false;
  $("zoomSelect").hidden = false;

  const task = S.task = pdfjsLib.getDocument({ ...DOC_OPTS, url: o.url });
  task.onPassword = (update, reason) => {
    const p = window.prompt(reason === 2 ? "كلمة السر غلط — اكتبها تاني:" : "الكتاب ده محمي بكلمة سر:");
    if (p === null) { post({ t: "error", gen: o.gen, code: "password", message: "الكتاب محمي بكلمة سر" }); task.destroy(); }
    else update(p);
  };
  let doc;
  try { doc = await task.promise; }
  catch (e) {
    if (gen !== S.gen) return;
    const msg = e && e.name === "PasswordException" ? "الكتاب محمي بكلمة سر" : (e && e.message) || String(e);
    setOverlay("failed", "مقدرتش أفتح الكتاب: " + msg);
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
  const d = S.doc, t = S.task;
  S.doc = null; S.task = null;
  findBarClose();
  if (E.ready) await E.close();
  if (d) { viewer.setDocument(null); linkService.setDocument(null, null); }
  try { if (t) await t.destroy(); else if (d) await d.destroy(); } catch (e) { }
  $("pageCount").textContent = "0";
  $("pageInput").value = "";
}

async function close() {
  S.gen++;
  await closeDoc();
  $("bookTitle").textContent = "";
  setOverlay("empty", "افتح كتاب من «الكتب»");
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
  if (!url) { if (!S.doc) throw new Error("مفيش كتاب مفتوح"); return fn(S.doc); }
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
  if (isEpubUrl(url) || (!url && S.kind === "epub")) { post({ t: "image", id, page: p, error: "EPUB مش محتاج OCR" }); return; }
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
    const msg = S.kind === "epub" ? { t: "state", gen: S.opts.gen, ...E.state(), theme: S.theme, rtl: S.rtl } : {
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
  if (S.kind === "epub") { E.goTo(Math.round(p)); return; }
  if (!S.doc) return;
  p = Math.min(Math.max(1, Math.round(p)), S.doc.numPages);
  viewer.currentPageNumber = p;
  container.focus();
}
function step(d) {
  if (S.kind === "epub") { E.step(d); return; }
  if (!S.doc) return;
  if (d > 0) viewer.nextPage(); else viewer.previousPage();
}
const ZOOMS = [0.5, 0.67, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4];
function zoom(dir) {
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
    $("findResult").textContent = "بيدوّر…";
    E.find(q, again, previous).then(m => { $("findResult").textContent = m.total ? `${m.current} من ${m.total}` : "مفيش نتايج"; });
    return;
  }
  eventBus.dispatch("find", {
    source: null, type: again ? "again" : "", query: q, caseSensitive: false, entireWord: false,
    highlightAll: true, findPrevious: !!previous, matchDiacritics: false
  });
}
eventBus.on("updatefindmatchescount", e => showMatches(e.matchesCount));
eventBus.on("updatefindcontrolstate", e => {
  if (e.state === 1) $("findResult").textContent = "مفيش نتايج";
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
  if (ctrl && !e.altKey && (k === "f" || k === "F")) { e.preventDefault(); findBarOpen(); return; }
  if (ctrl && (k === "g" || k === "G")) { e.preventDefault(); pageInput.focus(); return; }
  if (ctrl && (k === "=" || k === "+")) { e.preventDefault(); zoom(1); return; }
  if (ctrl && (k === "-" || k === "_")) { e.preventDefault(); zoom(-1); return; }
  if (ctrl && k === "0") { e.preventDefault(); if (S.kind === "epub") E.zoom(0); else if (S.doc) viewer.currentScaleValue = "page-width"; return; }
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
    if (S.kind === "epub" && (k === "PageDown" || k === " ")) { e.preventDefault(); step(1); return; }
    if (S.kind === "epub" && k === "PageUp") { e.preventDefault(); step(-1); return; }
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
  state: () => JSON.stringify({ kind: S.kind, page: hasDoc() ? curPage() : 0, pages: pageTotal(), scale: S.kind === "epub" ? "font:" + E.font : String(viewer.currentScaleValue), spread: S.spread, theme: S.theme, rtl: S.rtl, read: [...S.read], gen: S.opts.gen })
};
post({ t: "ready" });
