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
  theme: "normal", rtl: false
};

// ---------------------------------------------------------------- فتح وقفل
async function open(o) {
  const gen = ++S.gen;
  await closeDoc();
  S.opts = o || {};
  S.readSeconds = Math.max(1, o.readSeconds || 10);
  S.read = new Set(o.read || []);
  S.dwell = new Map();
  S.marks = new Set(o.marks || []);
  S.lastInput = Date.now();
  S.lastState = "";
  setTheme(o.theme || S.theme, false);
  setRtl(!!o.rtl, false);
  $("bookTitle").textContent = o.title || "";
  document.title = o.title || "قارئ مسار";
  setOverlay("loading", "بيفتح الكتاب…");

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

// ---------------------------------------------------------------- الحالة للبرنامج
let stateTimer = 0;
function postState(now) {
  if (!S.doc) return;
  clearTimeout(stateTimer);
  const send = () => {
    const loc = viewer._location || null;
    const msg = {
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
  if (!S.doc) return;
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
  if (document.activeElement !== $("pageInput")) $("pageInput").value = S.doc ? viewer.currentPageNumber : "";
}
function goTo(p) {
  if (!S.doc) return;
  p = Math.min(Math.max(1, Math.round(p)), S.doc.numPages);
  viewer.currentPageNumber = p;
  container.focus();
}
function step(d) {
  if (!S.doc) return;
  if (d > 0) viewer.nextPage(); else viewer.previousPage();
}
const ZOOMS = [0.5, 0.67, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4];
function zoom(dir) {
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
  viewer.spreadMode = mode;
  $("spread").classList.toggle("on", mode !== 0);
  $("spread").textContent = mode === 0 ? "▯" : "▯▯";
}
function setTheme(t, notify = true) {
  S.theme = ["normal", "night", "sepia"].includes(t) ? t : "normal";
  document.body.classList.toggle("night", S.theme === "night");
  document.body.classList.toggle("sepia", S.theme === "sepia");
  $("theme").textContent = S.theme === "night" ? "☾" : S.theme === "sepia" ? "📜" : "☀";
  if (notify) postState(true);
}
function setRtl(on, notify = true) {
  S.rtl = !!on;
  container.classList.toggle("rtl", S.rtl);
  $("direction").classList.toggle("on", S.rtl);
  if (notify) postState(true);
}

// ---------------------------------------------------------------- العلامات
function decorate(p) {
  const pv = viewer.getPageView(p - 1);
  if (pv && pv.div) pv.div.classList.toggle("marked", S.marks.has(p));
}
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
$("zoomSelect").onchange = e => { if (S.doc && e.target.value !== "custom") viewer.currentScaleValue = e.target.value; container.focus(); };
$("spread").onclick = () => { if (S.doc) { applySpread(viewer.spreadMode === 0 ? 1 : 0); postState(true); } };
$("direction").onclick = () => setRtl(!S.rtl);
$("theme").onclick = () => setTheme(S.theme === "normal" ? "night" : S.theme === "night" ? "sepia" : "normal");
$("findButton").onclick = () => ($("findbar").hidden ? findBarOpen() : findBarClose());
$("mark").onclick = () => { if (S.doc) post({ t: "cmd", gen: S.opts.gen, name: "mark", page: viewer.currentPageNumber }); };
$("fullscreen").onclick = () => post({ t: "cmd", gen: S.opts.gen, name: "fullscreen" });
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
window.addEventListener("keydown", e => {
  const k = e.key, ctrl = e.ctrlKey || e.metaKey;
  if (ctrl && !e.altKey && (k === "f" || k === "F")) { e.preventDefault(); findBarOpen(); return; }
  if (ctrl && (k === "g" || k === "G")) { e.preventDefault(); pageInput.focus(); return; }
  if (ctrl && (k === "=" || k === "+")) { e.preventDefault(); zoom(1); return; }
  if (ctrl && (k === "-" || k === "_")) { e.preventDefault(); zoom(-1); return; }
  if (ctrl && k === "0") { e.preventDefault(); if (S.doc) viewer.currentScaleValue = "page-width"; return; }
  if (k === "Escape" && findBarClose()) { e.preventDefault(); return; }
  const inText = typing(document.activeElement);
  if (!inText && !ctrl && !e.altKey) {
    if (k === "ArrowRight" || k === "ArrowLeft") {
      if (viewer.currentScaleValue === "page-fit" || viewer.spreadMode !== 0 || container.scrollWidth <= container.clientWidth + 2) {
        e.preventDefault();
        const forward = (k === "ArrowLeft") === S.rtl;
        step(forward ? 1 : -1);
        return;
      }
    }
    if (k === "Home" && S.doc) { e.preventDefault(); goTo(1); return; }
    if (k === "End" && S.doc) { e.preventDefault(); goTo(S.doc.numPages); return; }
    if (k === "b" || k === "B") { e.preventDefault(); $("mark").click(); return; }
  }
  // اختصارات البرنامج (F11، F6، Esc، Ctrl+…، Alt+…) بتتنفذ في «مسار»
  const fn = /^F\d{1,2}$/.test(k);
  const forward = fn || k === "Escape" || e.altKey || (ctrl && !inText && !["c", "C", "a", "A"].includes(k)) || (ctrl && inText && !["c", "C", "a", "A", "v", "V", "x", "X", "z", "Z"].includes(k));
  if (forward && !["Control", "Shift", "Alt", "Meta"].includes(k)) {
    e.preventDefault();
    post({ t: "key", k: e.keyCode, c: ctrl, s: e.shiftKey, a: e.altKey });
  }
}, true);
// تكبير بـ Ctrl+العجلة جوه الكتاب (مش تكبير الصفحة كلها)
container.addEventListener("wheel", e => { if (e.ctrlKey) { e.preventDefault(); zoom(e.deltaY < 0 ? 1 : -1); } }, { passive: false });

// ---------------------------------------------------------------- واجهة البرنامج
window.masar = {
  open: o => { open(o); return "ok"; },
  close: () => { close(); return "ok"; },
  goTo: p => { goTo(p); return "ok"; },
  step: d => { step(d); return "ok"; },
  setMarks: pages => { S.marks = new Set(pages || []); decorateMarks(); return "ok"; },
  setRead: pages => { S.read = new Set(pages || []); return "ok"; },
  setReadSeconds: s => { S.readSeconds = Math.max(1, s | 0); return "ok"; },
  probe: (id, url) => { probe(id, url); return "ok"; },
  focus: () => { container.focus(); return "ok"; },
  find: q => { findBarOpen(); $("findInput").value = q; find(false, false); return "ok"; },
  state: () => JSON.stringify({ page: S.doc ? viewer.currentPageNumber : 0, pages: S.doc ? S.doc.numPages : 0, scale: String(viewer.currentScaleValue), spread: viewer.spreadMode, theme: S.theme, rtl: S.rtl, read: [...S.read], gen: S.opts.gen })
};
post({ t: "ready" });
