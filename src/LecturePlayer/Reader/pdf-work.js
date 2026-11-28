// =====================================================================
//  Worker للشغل التقيل على ملفات الـ PDF (pdf-lib): النسخة المشروحة ومحرر الصفحات.
//  بيقرا الأصل من البرنامج، ويبعت الناتج للبرنامج على عنوان حفظ بتوكن (POST) — من غير base64.
// =====================================================================
importScripts("lib/pdf-lib.min.js");
const L = self.PDFLib;
const { PDFDocument, rgb, BlendMode, PDFName, PDFHexString, PDFString, degrees, StandardFonts, pushGraphicsState, popGraphicsState, concatTransformationMatrix } = L;

const hex = c => { const m = /^#?([0-9a-f]{6})$/i.exec(c || ""); const n = m ? parseInt(m[1], 16) : 0; return rgb((n >> 16 & 255) / 255, (n >> 8 & 255) / 255, (n & 255) / 255); };

async function load(src) {
  const r = await fetch(src);
  if (!r.ok) throw new Error("تعذّرت قراءة الكتاب (" + r.status + ")");
  const bytes = new Uint8Array(await r.arrayBuffer());
  const doc = await PDFDocument.load(bytes, { ignoreEncryption: true, updateMetadata: false, throwOnInvalidObject: false });
  if (doc.isEncrypted) throw new Error("الكتاب محمي بكلمة مرور/تشفير — لا يمكن تعديله");
  return doc;
}

async function saveTo(doc, save) {
  const out = await doc.save({ useObjectStreams: true, addDefaultPage: false });
  const r = await fetch(save, { method: "POST", body: out, headers: { "Content-Type": "application/pdf" } });
  if (!r.ok) throw new Error("فشل الحفظ: " + (await r.text().catch(() => r.status)));
  return out.length;
}

/** رسم خطة صفحة (من ink-export.js) فوق الصفحة: فيكتور + صور + تعليقات PDF. */
async function drawPlan(doc, page, plan, cache) {
  const [a, b, c, d, e, f] = plan.ctm;
  for (const dr of plan.draws) {
    page.pushOperators(pushGraphicsState(), concatTransformationMatrix(a, b, c, d, e, f));
    if (dr.k === "path") {
      const o = { x: 0, y: 0 };
      if (dr.fill) { o.color = hex(dr.fill); o.opacity = Math.max(0, Math.min(1, dr.fo ?? 1)); }
      if (dr.stroke && dr.sw) { o.borderColor = hex(dr.stroke); o.borderWidth = dr.sw; o.borderOpacity = Math.max(0, Math.min(1, dr.so ?? 1)); o.borderLineCap = L.LineCapStyle.Round; }
      if (!o.color && !o.borderColor) { page.pushOperators(popGraphicsState()); continue; }
      if (dr.blend === "Multiply") o.blendMode = BlendMode.Multiply;
      page.drawSvgPath(dr.d, o);
    } else if (dr.k === "img") {
      const key = dr.bytes;
      let img = cache.get(key);
      if (!img) { img = dr.type === "jpg" ? await doc.embedJpg(dr.bytes) : await doc.embedPng(dr.bytes); cache.set(key, img); }
      const [x, y, w, h] = dr.box;
      if (dr.rot) {
        // لف حوالين نص الصورة (وحدات الشرح، الصادات لتحت بعد القلبة)
        const cx = x + w / 2, cy = y + h / 2, r = -dr.rot * Math.PI / 180, cs = Math.cos(r), sn = Math.sin(r);
        // جوه المصفوفة (M·F) نقطة (u, -v) هي (u, v) — فاللف حوالين (cx, -cy) في الفضاء المقلوب
        page.pushOperators(concatTransformationMatrix(cs, sn, -sn, cs, cx - cs * cx - sn * cy, -cy - sn * cx + cs * cy));
      }
      page.drawImage(img, { x, y: -(y + h), width: w, height: h });
    }
    page.pushOperators(popGraphicsState());
  }
  for (const n of plan.notes || []) {
    const annot = doc.context.obj({
      Type: "Annot", Subtype: "Text", Rect: n.rect, Name: "Comment", F: 4, Open: false,
      C: n.color, T: PDFHexString.fromText("مسار"), Contents: PDFHexString.fromText(n.text || ""),
      M: PDFString.fromDate(new Date())
    });
    page.node.addAnnot(doc.context.register(annot));
  }
}

async function annotate(m) {
  const doc = await load(m.src);
  const pages = doc.getPages(), cache = new Map();
  let i = 0;
  for (const plan of m.plans) {
    const page = pages[plan.index]; if (!page) continue;
    await drawPlan(doc, page, plan, cache);
    post({ id: m.id, progress: ++i / Math.max(1, m.plans.length) * 0.7 });
  }
  if (m.title) doc.setTitle(m.title + " — مشروح");
  doc.setProducer("مسار (Masar)");
  doc.setModificationDate(new Date());
  const size = await saveTo(doc, m.save);
  return { size, pages: doc.getPageCount() };
}

// ---------------------------------------------------------------- محرر الصفحات
/** صفحة سبورة: خلفية (فاضية/مربعات/سطور/نقط) بنفس مقاس الصفحة اللي جنبها. */
function boardPage(doc, size, kind, at) {
  const [w, h] = size;
  const p = at == null ? doc.addPage([w, h]) : doc.insertPage(at, [w, h]);
  const grey = rgb(0.78, 0.8, 0.84), step = 20;
  if (kind === "grid") {
    for (let x = step; x < w; x += step) p.drawLine({ start: { x, y: 0 }, end: { x, y: h }, thickness: 0.4, color: grey });
    for (let y = step; y < h; y += step) p.drawLine({ start: { x: 0, y }, end: { x: w, y }, thickness: 0.4, color: grey });
  } else if (kind === "lined") {
    for (let y = h - 70; y > 30; y -= 24) p.drawLine({ start: { x: 36, y }, end: { x: w - 36, y }, thickness: 0.5, color: grey });
    p.drawLine({ start: { x: w - 60, y: 0 }, end: { x: w - 60, y: h }, thickness: 0.6, color: rgb(0.95, 0.6, 0.6) });
  } else if (kind === "dots") {
    for (let x = step; x < w; x += step) for (let y = step; y < h; y += step) p.drawCircle({ x, y, size: 0.7, color: grey });
  }
  return p;
}

async function edit(m) {
  const src = await load(m.src);
  let doc = src;
  const sizeOf = i => { const p = doc.getPage(Math.max(0, Math.min(doc.getPageCount() - 1, i))); const s = p.getSize(); const r = p.getRotation().angle % 180 ? [s.height, s.width] : [s.width, s.height]; return r; };
  let n = 0;
  for (const op of m.ops) {
    switch (op.op) {
      case "compose": {
        // ترتيب الصفحات الجديد كله مرة واحدة: صفحات من الكتاب (أو ملف متضاف) بلفّة، وصفحات سبورة وصور
        const out = await PDFDocument.create();
        const docs = { src: doc };
        for (const k in op.merges || {}) docs[k] = await load(op.merges[k]);
        const want = {};
        for (const p of op.pages) if (p.doc) (want[p.doc] ||= []).push(p.index);
        const copies = {};
        for (const k in want) copies[k] = await out.copyPages(docs[k], want[k]);
        const used = {};
        for (const p of op.pages) {
          if (p.doc) {
            const pg = copies[p.doc][used[p.doc] = (used[p.doc] ?? -1) + 1];
            if (p.rot) pg.setRotation(degrees(((pg.getRotation().angle + p.rot) % 360 + 360) % 360));
            out.addPage(pg);
          } else if (p.board) boardPage(out, p.size, p.board);
          else if (p.image) {
            const img = p.type === "jpg" ? await out.embedJpg(p.image) : await out.embedPng(p.image);
            const [w, h] = p.size, pg = out.addPage([w, h]);
            const k = Math.min((w - 40) / img.width, (h - 40) / img.height, 1e9);
            pg.drawImage(img, { x: (w - img.width * k) / 2, y: (h - img.height * k) / 2, width: img.width * k, height: img.height * k });
          }
        }
        try { const t = doc.getTitle(); if (t) out.setTitle(t); const a = doc.getAuthor(); if (a) out.setAuthor(a); } catch (x) { }
        doc = out;
        break;
      }
      case "order": {
        // ترتيب جديد (أرقام الصفحات القديمة من 0) — بيعمل مستند جديد بالترتيب ده
        const out = await PDFDocument.create();
        const copied = await out.copyPages(doc, op.order);
        copied.forEach(p => out.addPage(p));
        doc = out;
        break;
      }
      case "rotate": for (const i of op.pages) { const p = doc.getPage(i); p.setRotation(degrees(((p.getRotation().angle + op.deg) % 360 + 360) % 360)); } break;
      case "board": boardPage(doc, op.size || sizeOf(op.at ?? doc.getPageCount() - 1), op.kind, op.at); break;
      case "image": {
        const img = op.type === "jpg" ? await doc.embedJpg(op.bytes) : await doc.embedPng(op.bytes);
        const [w, h] = op.size || sizeOf(op.at ?? 0);
        const p = op.at == null ? doc.addPage([w, h]) : doc.insertPage(op.at, [w, h]);
        const k = Math.min((w - 40) / img.width, (h - 40) / img.height);
        p.drawImage(img, { x: (w - img.width * k) / 2, y: (h - img.height * k) / 2, width: img.width * k, height: img.height * k });
        break;
      }
      case "merge": {
        const other = await load(op.src);
        const copied = await doc.copyPages(other, other.getPageIndices());
        let at = op.at ?? doc.getPageCount();
        for (const p of copied) doc.insertPage(at++, p);
        break;
      }
      case "numbers": {
        // أرقام الصفحات: أرقام لاتيني بخط PDF القياسي، أو صورة لكل رقم (لو الشكل عربي) جاية جاهزة من الصفحة
        const font = await doc.embedFont(StandardFonts.Helvetica);
        const total = doc.getPageCount();
        for (let i = 0; i < total; i++) {
          if (op.skipFirst && i === 0) continue;
          const p = doc.getPage(i), { width, height } = p.getSize();
          const num = (op.start || 1) + i - (op.skipFirst ? 1 : 0);
          if (op.images && op.images[i]) {
            const im = await doc.embedPng(op.images[i]);
            const h2 = 14, w2 = im.width / im.height * h2;
            const x = op.pos === "right" ? width - 36 - w2 : op.pos === "left" ? 36 : (width - w2) / 2;
            p.drawImage(im, { x, y: 18, width: w2, height: h2 });
          } else {
            const text = op.format === "of" ? `${num} / ${total - (op.skipFirst ? 1 : 0) + (op.start || 1) - 1}` : String(num);
            const tw = font.widthOfTextAtSize(text, 10);
            const x = op.pos === "right" ? width - 36 - tw : op.pos === "left" ? 36 : (width - tw) / 2;
            p.drawText(text, { x, y: 20, size: 10, font, color: rgb(0.3, 0.3, 0.3) });
          }
        }
        break;
      }
      case "stamp": {
        // ختم/علامة مائية: صورة جاهزة (كلام عربي) على الصفحات المختارة، مايلة وشفافة
        const im = await doc.embedPng(op.png);
        for (const i of op.pages) {
          const p = doc.getPage(i), { width, height } = p.getSize();
          const w2 = Math.min(width * 0.7, im.width), h2 = w2 * im.height / im.width;
          if (op.diagonal) p.drawImage(im, { x: width / 2 - w2 / 2 * Math.cos(Math.PI / 4) + h2 / 2 * Math.sin(Math.PI / 4), y: height / 2 - w2 / 2 * Math.sin(Math.PI / 4) - h2 / 2 * Math.cos(Math.PI / 4), width: w2, height: h2, rotate: degrees(45), opacity: op.opacity ?? 0.18 });
          else p.drawImage(im, { x: width - w2 - 30, y: height - h2 - 30, width: w2, height: h2, opacity: op.opacity ?? 0.9 });
        }
        break;
      }
      case "flatten": {
        const pages = doc.getPages(), cache = new Map();
        for (const plan of op.plans) if (pages[plan.index]) await drawPlan(doc, pages[plan.index], plan, cache);
        break;
      }
    }
    post({ id: m.id, progress: ++n / m.ops.length * 0.8 });
  }
  if (m.extract) {
    const out = await PDFDocument.create();
    (await out.copyPages(doc, m.extract)).forEach(p => out.addPage(p));
    doc = out;
  }
  doc.setProducer("مسار (Masar)");
  doc.setModificationDate(new Date());
  const size = await saveTo(doc, m.save);
  return { size, pages: doc.getPageCount() };
}

/** السبورة ← PDF: صفحة بمقاسها وخلفيتها ونقشتها، والرسم فوقها فيكتور. */
async function boardPdf(m) {
  const doc = await PDFDocument.create(), cache = new Map();
  let i = 0;
  for (const pl of m.plans) {
    const page = doc.addPage([pl.w, pl.h]);
    page.drawRectangle({ x: 0, y: 0, width: pl.w, height: pl.h, color: hex(pl.bg) });
    const c = pl.dark ? rgb(0.42, 0.45, 0.48) : rgb(0.8, 0.82, 0.86);
    if (pl.pat === "grid") {
      for (let x = 20; x < pl.w; x += 20) page.drawLine({ start: { x, y: 0 }, end: { x, y: pl.h }, thickness: 0.4, color: c });
      for (let y = pl.h - 20; y > 0; y -= 20) page.drawLine({ start: { x: 0, y }, end: { x: pl.w, y }, thickness: 0.4, color: c });
    } else if (pl.pat === "lines") {
      for (let y = pl.h - 24; y > 0; y -= 24) page.drawLine({ start: { x: 0, y }, end: { x: pl.w, y }, thickness: 0.5, color: c });
    } else if (pl.pat === "dots") {
      for (let x = 20; x < pl.w; x += 20) for (let y = pl.h - 20; y > 0; y -= 20) page.drawCircle({ x, y, size: 0.8, color: c });
    }
    await drawPlan(doc, page, pl, cache);
    post({ id: m.id, progress: ++i / m.plans.length });
  }
  doc.setTitle(m.title || "سبورة");
  doc.setProducer("مسار (Masar)");
  doc.setCreationDate(new Date());
  const size = await saveTo(doc, m.save);
  return { size, pages: doc.getPageCount() };
}

/** ملف من أجزاء بالترتيب: {pdf: رابط, pages: [أرقام من 0] أو كل الصفحات} أو {img: رابط, w, h}. */
async function compose(m) {
  const out = await PDFDocument.create();
  let i = 0;
  for (const p of m.parts || []) {
    if (p.pdf) {
      const src = await load(p.pdf);
      const idx = Array.isArray(p.pages) && p.pages.length ? p.pages.filter(n => n >= 0 && n < src.getPageCount()) : src.getPageIndices();
      const copies = await out.copyPages(src, idx);
      for (const c of copies) out.addPage(c);
    } else if (p.img) {
      const r = await fetch(p.img);
      if (!r.ok) throw new Error("تعذّرت قراءة صورة (" + r.status + ")");
      const bytes = new Uint8Array(await r.arrayBuffer());
      const png = bytes[0] === 0x89 && bytes[1] === 0x50;
      const img = png ? await out.embedPng(bytes) : await out.embedJpg(bytes);
      const w = p.w || img.width, h = p.h || img.height;
      const page = out.addPage([w, h]);
      page.drawImage(img, { x: 0, y: 0, width: w, height: h });
      // روابط قابلة للنقر فوق الصورة (مواضعها نسبة من الصفحة، من أعلى اليمين كما رُسمت)
      for (const l of p.links || []) {
        if (!/^https?:\/\//i.test(l.url || "")) continue;
        const rect = [l.x * w, h - (l.y + l.h) * h, (l.x + l.w) * w, h - l.y * h];
        const annot = out.context.obj({
          Type: "Annot", Subtype: "Link", Rect: rect, Border: [0, 0, 0],
          A: { Type: "Action", S: "URI", URI: PDFString.of(l.url) }
        });
        page.node.addAnnot(out.context.register(annot));
      }
    }
    post({ id: m.id, progress: ++i / Math.max(1, m.parts.length) * 0.9 });
  }
  if (!out.getPageCount()) throw new Error("لا توجد صفحات للتصدير");
  out.setTitle(m.title || "مسار");
  out.setProducer("مسار (Masar)");
  out.setCreationDate(new Date());
  const size = await saveTo(out, m.save);
  return { size, pages: out.getPageCount() };
}

function post(m) { self.postMessage(m); }

self.onmessage = async e => {
  const m = e.data;
  try {
    const r = m.job === "annotate" ? await annotate(m) : m.job === "edit" ? await edit(m) : m.job === "board" ? await boardPdf(m) : m.job === "compose" ? await compose(m) : null;
    if (!r) throw new Error("job?");
    post({ id: m.id, ok: true, ...r });
  } catch (err) {
    post({ id: m.id, ok: false, error: (err && err.message) || String(err) });
  }
};
