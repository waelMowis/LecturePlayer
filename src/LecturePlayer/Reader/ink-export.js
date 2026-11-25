// =====================================================================
//  تصدير الشرح: «نسخة مشروحة» PDF (الخطوط فيكتور، النصوص العربي والملصقات صور عالية الدقة،
//  والملاحظات تعليقات PDF حقيقية بتتفتح في أي قارئ) + صورة PNG للصفحة بالشرح.
//  الشغل التقيل (pdf-lib) في Worker عشان القراية ماتقفش.
// =====================================================================
import { W, PENS, bbox, renderSvg } from "./ink.js";
import { getStroke } from "./lib/perfect-freehand.mjs";

const SVGNS = "http://www.w3.org/2000/svg";
const isRtl = t => /[֐-ࣿיִ-﷿ﹰ-﻿]/.test((t || "").replace(/[^\p{L}]/gu, "").slice(0, 1) || (t || "").match(/[\p{L}]/u)?.[0] || "");

// ---------------------------------------------------------------- مصفوفات
/** m = [a,b,c,d,e,f]: x' = a x + c y + e ، y' = b x + d y + f */
export const mul = (A, B) => [A[0] * B[0] + A[2] * B[1], A[1] * B[0] + A[3] * B[1], A[0] * B[2] + A[2] * B[3], A[1] * B[2] + A[3] * B[3],
  A[0] * B[4] + A[2] * B[5] + A[4], A[1] * B[4] + A[3] * B[5] + A[5]];
export const inv = m => { const det = m[0] * m[3] - m[1] * m[2]; return [m[3] / det, -m[1] / det, -m[2] / det, m[0] / det, (m[2] * m[5] - m[3] * m[4]) / det, (m[1] * m[4] - m[0] * m[5]) / det]; };
const apply = (m, p) => [m[0] * p[0] + m[2] * p[1] + m[4], m[1] * p[0] + m[3] * p[1] + m[5]];
const rotAbout = (c, deg) => { const a = deg * Math.PI / 180, s = Math.sin(a), k = Math.cos(a); return [k, s, -s, k, c[0] - k * c[0] + s * c[1], c[1] - s * c[0] - k * c[1]]; };

/**
 * المصفوفة اللي بتحوّل وحدات الشرح (العرض = 1000، الصادات لتحت) لإحداثيات الـ PDF نفسه.
 * T = تحويل PDF.js (من الـ PDF للشاشة، بيحسب الدوران والـ cropBox). pdf-lib بيقلب Y جوه drawSvgPath فبنضرب في قلبة كمان.
 */
export function pageCtm(viewport) {
  const s = viewport.width / W;
  return mul(inv(viewport.transform), mul([s, 0, 0, s, 0, 0], [1, 0, 0, -1, 0, 0]));
}

// ---------------------------------------------------------------- مسارات فيكتور (أوامر مطلقة: M L C Q Z)
function pathD(cmds, m) {
  const f = v => Math.round(v * 100) / 100;
  return cmds.map(([op, ...pts]) => {
    if (op === "Z") return "Z";
    const out = [];
    for (let i = 0; i < pts.length; i += 2) { const q = m ? apply(m, [pts[i], pts[i + 1]]) : [pts[i], pts[i + 1]]; out.push(f(q[0]), f(q[1])); }
    return op + " " + out.join(" ");
  }).join(" ");
}
function ellipseCmds(cx, cy, rx, ry) {
  const k = 0.5522847498;
  return [["M", cx + rx, cy], ["C", cx + rx, cy + ry * k, cx + rx * k, cy + ry, cx, cy + ry], ["C", cx - rx * k, cy + ry, cx - rx, cy + ry * k, cx - rx, cy],
    ["C", cx - rx, cy - ry * k, cx - rx * k, cy - ry, cx, cy - ry], ["C", cx + rx * k, cy - ry, cx + rx, cy - ry * k, cx + rx, cy], ["Z"]];
}
const polyCmds = (pts, close = true) => [["M", ...pts[0]], ...pts.slice(1).map(p => ["L", ...p]), ...(close ? [["Z"]] : [])];
function roundRectCmds(x0, y0, w, h, r) {
  r = Math.min(r, w / 2, h / 2);
  return [["M", x0 + r, y0], ["L", x0 + w - r, y0], ["Q", x0 + w, y0, x0 + w, y0 + r], ["L", x0 + w, y0 + h - r], ["Q", x0 + w, y0 + h, x0 + w - r, y0 + h],
    ["L", x0 + r, y0 + h], ["Q", x0, y0 + h, x0, y0 + h - r], ["L", x0, y0 + r], ["Q", x0, y0, x0 + r, y0], ["Z"]];
}
function outlineCmds(it) {
  const p = PENS[it.pen] || PENS.pen;
  if (it.pts.length === 1) return ellipseCmds(it.pts[0][0], it.pts[0][1], it.size / 2, it.size / 2);
  const o = getStroke(it.pts, { size: it.size, thinning: p.thinning, smoothing: p.smoothing, streamline: p.streamline, simulatePressure: !it.pr, last: true, start: { cap: true }, end: { cap: true } });
  if (!o.length) return [];
  const cmds = [["M", ...o[0]]];
  for (let i = 0; i < o.length; i++) { const a = o[i], b = o[(i + 1) % o.length]; cmds.push(["Q", a[0], a[1], (a[0] + b[0]) / 2, (a[1] + b[1]) / 2]); }
  cmds.push(["Z"]);
  return cmds;
}
function shapeBox(it) { return [Math.min(it.x1, it.x2), Math.min(it.y1, it.y2), Math.max(it.x1, it.x2), Math.max(it.y1, it.y2)]; }

/** عنصر ← رسومات فيكتور: [{d, fill, fo, stroke, sw, so, blend}] (وحدات الشرح). */
export function vectorOps(it) {
  const ops = [], o = it.o ?? 1;
  const rotM = it.rot && it.t !== "stroke" ? (() => { const b = it.t === "shape" ? shapeBox(it) : [it.x, it.y, it.x + (it.w || 0), it.y + (it.h || 0)]; return rotAbout([(b[0] + b[2]) / 2, (b[1] + b[3]) / 2], it.rot); })() : null;
  const add = (cmds, style) => { if (cmds.length) ops.push({ d: pathD(cmds, rotM), ...style }); };
  if (it.t === "stroke") {
    const p = PENS[it.pen] || PENS.pen;
    add(outlineCmds(it), { fill: it.c, fo: it.o ?? p.opacity, blend: p.blend ? "Multiply" : null });
  } else if (it.t === "shape") {
    const sw = it.sw || 3, st = { stroke: it.c || "#e03131", sw, so: o };
    const fill = it.fill ? { fill: it.fill, fo: (it.fo ?? 0.25) * o } : {};
    const [x0, y0, x1, y1] = shapeBox(it), w = x1 - x0, h = y1 - y0;
    const head = (from, to) => {
      const ang = Math.atan2(to[1] - from[1], to[0] - from[0]), L = Math.max(10, sw * 4.5), sp = 0.45;
      add(polyCmds([[to[0] - L * Math.cos(ang - sp), to[1] - L * Math.sin(ang - sp)], to, [to[0] - L * Math.cos(ang + sp), to[1] - L * Math.sin(ang + sp)]]), { fill: it.c || "#e03131", fo: o, stroke: it.c, sw: sw / 2, so: o });
    };
    switch (it.k) {
      case "line": case "arrow": case "arrow2":
        add([["M", it.x1, it.y1], ["L", it.x2, it.y2]], st);
        if (it.k !== "line") head([it.x1, it.y1], [it.x2, it.y2]);
        if (it.k === "arrow2") head([it.x2, it.y2], [it.x1, it.y1]);
        break;
      case "rect": add(polyCmds([[x0, y0], [x1, y0], [x1, y1], [x0, y1]]), { ...st, ...fill }); break;
      case "round": add(roundRectCmds(x0, y0, w, h, Math.min(w, h) * 0.18), { ...st, ...fill }); break;
      case "ellipse": add(ellipseCmds(x0 + w / 2, y0 + h / 2, w / 2, h / 2), { ...st, ...fill }); break;
      case "tri": add(polyCmds(it.poly || [[x0 + w / 2, y0], [x1, y1], [x0, y1]]), { ...st, ...fill }); break;
      case "star": {
        const cx = x0 + w / 2, cy = y0 + h / 2, pts = [];
        for (let i = 0; i < 10; i++) { const r = i % 2 ? 0.45 : 1, a = -Math.PI / 2 + i * Math.PI / 5; pts.push([cx + Math.cos(a) * w / 2 * r, cy + Math.sin(a) * h / 2 * r]); }
        add(polyCmds(pts), { ...st, ...fill }); break;
      }
      case "brace":
        add(h >= w
          ? [["M", x1, y0], ["C", x0 + w / 2, y0, x0 + w / 2, y0 + h / 2, x0, y0 + h / 2], ["C", x0 + w / 2, y0 + h / 2, x0 + w / 2, y1, x1, y1]]
          : [["M", x0, y1], ["C", x0, y0 + h / 2, x0 + w / 2, y0 + h / 2, x0 + w / 2, y0], ["C", x0 + w / 2, y0 + h / 2, x1, y0 + h / 2, x1, y1]], st);
        break;
      case "check": add([["M", x0, y0 + h * 0.55], ["L", x0 + w * 0.38, y1], ["L", x1, y0]], { ...st, sw: Math.max(sw, 4) }); break;
      case "cross": add([["M", x0, y0], ["L", x1, y1]], { ...st, sw: Math.max(sw, 4) }); add([["M", x1, y0], ["L", x0, y1]], { ...st, sw: Math.max(sw, 4) }); break;
    }
  } else if (it.t === "step") {
    const r = it.r || 16;
    add(ellipseCmds(it.x, it.y, r, r), { fill: it.c || "#e03131", fo: 1, stroke: "#ffffff", sw: r * 0.12, so: 1 });
  } else if (it.t === "callout") {
    const x0 = it.x, y0 = it.y, w = it.w, h = it.h, tip = it.tip || [x0 + w / 2, y0 + h + 40];
    const cx = x0 + w / 2, cy = y0 + h / 2, bw = Math.min(w, h) * 0.18;
    const ang = Math.atan2(tip[1] - cy, tip[0] - cx), perp = ang + Math.PI / 2;
    const style = { fill: it.bg || "#fff9db", fo: 1, stroke: it.c || "#f59f00", sw: 2, so: 1 };
    add(polyCmds([[cx + Math.cos(perp) * bw, cy + Math.sin(perp) * bw], tip, [cx - Math.cos(perp) * bw, cy - Math.sin(perp) * bw]]), style);
    add(roundRectCmds(x0, y0, w, h, 12), style);
  }
  return ops;
}

// ---------------------------------------------------------------- صور (نص عربي، ملصقات، أرقام)
function canvasBlob(c) { return new Promise(r => c.toBlob(b => b.arrayBuffer().then(a => r(new Uint8Array(a))), "image/png")); }

/** نص متعدد السطور بالمقاس والخط والاتجاه — العربي بيتشكّل صح لأن المتصفح هو اللي بيرسم. */
function drawText(ctx, text, o) {
  const { x, y, w, size, font, bold, color, align, dir, lineH = 1.35, padX = 6, padY = 4, vcenter, h } = o;
  ctx.font = `${bold ? "700 " : ""}${size}px ${JSON.stringify(font || "Segoe UI")}, Tahoma, sans-serif`;
  ctx.fillStyle = color || "#111";
  const rtl = dir === "rtl" || (dir !== "ltr" && isRtl(text));
  ctx.direction = rtl ? "rtl" : "ltr";
  const maxW = Math.max(10, w - padX * 2);
  const lines = [];
  for (const para of String(text || "").split("\n")) {
    const words = para.split(/(\s+)/);
    let line = "";
    for (const wd of words) {
      const t = line + wd;
      if (line && ctx.measureText(t.trimEnd()).width > maxW && wd.trim()) { lines.push(line.trimEnd()); line = wd.trimStart(); }
      else line = t;
    }
    lines.push(line.trimEnd());
  }
  const lh = size * lineH;
  let ty = y + padY + (vcenter ? Math.max(0, (h - padY * 2 - lines.length * lh) / 2) : 0);
  const al = align === "center" ? "center" : align === "right" ? "right" : align === "left" ? "left" : (rtl ? "right" : "left");
  ctx.textAlign = al;
  ctx.textBaseline = "middle";
  const ax = al === "center" ? x + w / 2 : al === "right" ? x + w - padX : x + padX;
  for (const ln of lines) { ctx.fillText(ln, ax, ty + lh / 2); ty += lh; }
  return lines.length * lh + padY * 2;
}

async function svgToPng(it, box, R) {
  const [bx, by, bw, bh] = box;
  const g = renderSvg(it);
  const svg = document.createElementNS(SVGNS, "svg");
  svg.setAttribute("xmlns", SVGNS);
  svg.setAttribute("viewBox", `${bx} ${by} ${bw} ${bh}`);
  svg.setAttribute("width", Math.ceil(bw * R)); svg.setAttribute("height", Math.ceil(bh * R));
  svg.appendChild(g);
  const url = URL.createObjectURL(new Blob([new XMLSerializer().serializeToString(svg)], { type: "image/svg+xml" }));
  try {
    const img = await new Promise((res, rej) => { const i = new Image(); i.onload = () => res(i); i.onerror = () => rej(new Error("svg raster")); i.src = url; });
    const c = document.createElement("canvas"); c.width = Math.ceil(bw * R); c.height = Math.ceil(bh * R);
    c.getContext("2d").drawImage(img, 0, 0, c.width, c.height);
    return await canvasBlob(c);
  } finally { URL.revokeObjectURL(url); }
}

async function textPng(it, R) {
  const c = document.createElement("canvas");
  const w = it.w, h0 = Math.max(it.h || 0, (it.size || 22) * 1.6);
  // نقيس الطول الحقيقي الأول (النص ممكن يكون نزل سطور أكتر من المحفوظ)
  const m = document.createElement("canvas").getContext("2d");
  const needH = drawText(m, it.text, { x: 0, y: 0, w: w * R, size: (it.size || 22) * R, font: it.font, bold: it.bold, color: it.c, align: it.align, dir: it.dir, padX: 6 * R, padY: 4 * R }) / R;
  const h = it.t === "callout" ? it.h : Math.max(h0 * 0, needH);
  c.width = Math.ceil(w * R); c.height = Math.ceil(h * R);
  const ctx = c.getContext("2d");
  if (it.t === "text" && it.bg) { ctx.fillStyle = it.bg; ctx.beginPath(); ctx.roundRect(0, 0, c.width, c.height, 4 * R); ctx.fill(); }
  drawText(ctx, it.text, {
    x: 0, y: 0, w: c.width, h: c.height, size: (Math.min(it.size || 22, it.t === "callout" ? 999 : 999)) * R, font: it.font, bold: it.bold, color: it.t === "callout" ? (it.tc || "#222") : it.c,
    align: it.t === "callout" ? "center" : it.align, dir: it.dir, padX: (it.t === "callout" ? 12 : 6) * R, padY: (it.t === "callout" ? 8 : 4) * R, vcenter: it.t === "callout"
  });
  return { png: await canvasBlob(c), box: [it.x, it.y, w, h] };
}

function dataUrlBytes(u) {
  const m = /^data:(image\/[a-z+]+);base64,(.*)$/i.exec(u || ""); if (!m) return null;
  const bin = atob(m[2]), out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return { type: m[1].toLowerCase(), bytes: out };
}
const hexRgb = c => { const m = /^#?([0-9a-f]{6})$/i.exec(c || ""); const n = m ? parseInt(m[1], 16) : 0xffd43b; return [(n >> 16 & 255) / 255, (n >> 8 & 255) / 255, (n & 255) / 255]; };

/**
 * خطة الصفحة للـ Worker: الفيكتور والصور والتعليقات بالترتيب اللي اترسموا بيه.
 * R = بكسل لكل وحدة شرح للصور (3× نقطة الـ PDF ≈ 216 DPI).
 */
export async function pagePlan(items, viewport) {
  const ctm = pageCtm(viewport), R = 3 * viewport.width / W, draws = [], notes = [];
  for (const it of items) {
    if (!it || it.ly === "t" || it.tempPen || it.t === "curtain") continue;
    try {
      if (it.t === "note") {
        const a = apply(ctm, [it.x, -it.y]), b = apply(ctm, [it.x + 26, -(it.y + 26)]);
        notes.push({ rect: [Math.min(a[0], b[0]), Math.min(a[1], b[1]), Math.max(a[0], b[0]), Math.max(a[1], b[1])], text: it.text || "", color: hexRgb(it.c) });
        continue;
      }
      for (const op of vectorOps(it)) draws.push({ k: "path", ...op });
      if (it.t === "text" || it.t === "callout") {
        if (!(it.text || "").trim()) continue;
        const t = await textPng(it, R);
        draws.push({ k: "img", type: "png", bytes: t.png, box: t.box, rot: it.rot || 0 });
      } else if (it.t === "image") {
        const d = dataUrlBytes(it.img); if (!d) continue;
        if (d.type === "image/png" || d.type === "image/jpeg" || d.type === "image/jpg") draws.push({ k: "img", type: d.type.endsWith("png") ? "png" : "jpg", bytes: d.bytes, box: [it.x, it.y, it.w, it.h], rot: it.rot || 0 });
        else draws.push({ k: "img", type: "png", bytes: await svgToPng(it, [it.x, it.y, it.w, it.h], R), box: [it.x, it.y, it.w, it.h], rot: 0 });
      } else if (it.t === "sticker" || it.t === "step") {
        const b = bbox(it), pad = 4, box = [b[0] - pad, b[1] - pad, b[2] - b[0] + pad * 2, b[3] - b[1] + pad * 2];
        // الرقم بس للخطوة (الدايرة فيكتور)؛ الملصق كله صورة (فيه إيموجي وكلام عربي)
        const only = it.t === "step" ? { ...it, _numOnly: 1 } : it;
        draws.push({ k: "img", type: "png", bytes: await svgToPng(only, box, R), box, rot: 0 });
      }
    } catch (e) { console.warn("ink export item", it && it.t, e); }
  }
  return { ctm, draws, notes };
}

// ---------------------------------------------------------------- الـ Worker
let worker = null, seq = 0;
const waiting = new Map();
function getWorker() {
  if (worker) return worker;
  worker = new Worker(new URL("./pdf-work.js", import.meta.url));
  worker.onmessage = e => { const m = e.data, w = waiting.get(m.id); if (!w) return; if (m.progress != null) { w.progress && w.progress(m.progress); return; } waiting.delete(m.id); m.ok ? w.res(m) : w.rej(new Error(m.error || "worker")); };
  worker.onerror = e => { for (const w of waiting.values()) w.rej(new Error("worker: " + (e.message || "crash"))); waiting.clear(); worker = null; };
  return worker;
}
export function runJob(msg, progress, transfer = []) {
  const id = ++seq;
  return new Promise((res, rej) => { waiting.set(id, { res, rej, progress }); getWorker().postMessage({ ...msg, id }, transfer); });
}

/**
 * النسخة المشروحة: src = الكتاب الأصلي، save = عنوان الحفظ عند البرنامج، ink = الشرح {pages:{...}}.
 * بيرجّع {ok, size, pages}.
 */
export async function buildAnnotated({ src, save, ink, title, pdfjsLib, docOpts, onProgress }) {
  const pages = ink && ink.pages || {};
  const keys = Object.keys(pages).filter(k => (pages[k] || []).some(i => i && i.ly !== "t" && !i.tempPen && i.t !== "curtain"));
  const task = pdfjsLib.getDocument({ ...docOpts, url: src });
  const doc = await task.promise;
  try {
    const plans = [], transfer = [];
    for (const k of keys) {
      const n = +k; if (!(n >= 1 && n <= doc.numPages)) continue;
      const page = await doc.getPage(n);
      const vp = page.getViewport({ scale: 1 });
      const plan = await pagePlan(pages[k], vp);
      for (const d of plan.draws) if (d.bytes) transfer.push(d.bytes.buffer);
      plans.push({ index: n - 1, ...plan });
      page.cleanup();
      onProgress && onProgress(plans.length / Math.max(1, keys.length) * 0.4);
    }
    return await runJob({ job: "annotate", src, save, plans, title }, p => onProgress && onProgress(0.4 + p * 0.6), transfer);
  } finally { await task.destroy(); }
}

/** صورة PNG لصفحة بالشرح اللي عليها (scale = تكبير الرسم). */
export async function pagePng({ pdfPage, items, scale = 2 }) {
  const vp = pdfPage.getViewport({ scale });
  const c = document.createElement("canvas"); c.width = Math.ceil(vp.width); c.height = Math.ceil(vp.height);
  const ctx = c.getContext("2d");
  ctx.fillStyle = "#fff"; ctx.fillRect(0, 0, c.width, c.height);
  await pdfPage.render({ canvasContext: ctx, viewport: vp }).promise;
  const k = vp.width / W;
  ctx.save(); ctx.scale(k, k);
  for (const it of items || []) {
    if (!it || it.ly === "t" || it.tempPen || it.t === "curtain") continue;
    for (const op of vectorOps(it)) {
      const p = new Path2D(op.d);
      ctx.globalCompositeOperation = op.blend === "Multiply" ? "multiply" : "source-over";
      if (op.fill) { ctx.globalAlpha = op.fo ?? 1; ctx.fillStyle = op.fill; ctx.fill(p); }
      if (op.stroke) { ctx.globalAlpha = op.so ?? 1; ctx.strokeStyle = op.stroke; ctx.lineWidth = op.sw || 1; ctx.lineCap = "round"; ctx.lineJoin = "round"; ctx.stroke(p); }
    }
    ctx.globalAlpha = 1; ctx.globalCompositeOperation = "source-over";
    if (it.t === "text" || it.t === "callout") {
      if ((it.text || "").trim()) {
        ctx.save();
        if (it.rot) { const b = [it.x, it.y, it.x + it.w, it.y + it.h], cx = (b[0] + b[2]) / 2, cy = (b[1] + b[3]) / 2; ctx.translate(cx, cy); ctx.rotate(it.rot * Math.PI / 180); ctx.translate(-cx, -cy); }
        if (it.t === "text" && it.bg) { ctx.fillStyle = it.bg; ctx.fillRect(it.x, it.y, it.w, it.h || it.size * 1.5); }
        drawText(ctx, it.text, { x: it.x, y: it.y, w: it.w, h: it.h, size: it.size || 22, font: it.font, bold: it.bold, color: it.t === "callout" ? "#222" : it.c, align: it.t === "callout" ? "center" : it.align, dir: it.dir, padX: it.t === "callout" ? 12 : 6, padY: it.t === "callout" ? 8 : 4, vcenter: it.t === "callout" });
        ctx.restore();
      }
    } else if (it.t === "image" || it.t === "sticker" || it.t === "step" || it.t === "note") {
      const b = bbox(it), pad = 4, box = [b[0] - pad, b[1] - pad, b[2] - b[0] + pad * 2, b[3] - b[1] + pad * 2];
      const png = it.t === "note" ? null : await svgToPng(it.t === "step" ? { ...it, _numOnly: 1 } : it, box, k * 2);
      if (png) {
        const bmp = await createImageBitmap(new Blob([png], { type: "image/png" }));
        ctx.drawImage(bmp, box[0], box[1], box[2], box[3]);
      } else { ctx.font = "20px Segoe UI Emoji"; ctx.textBaseline = "top"; ctx.fillText("📝", it.x, it.y); }
    }
  }
  ctx.restore();
  return await canvasBlob(c);
}
