// =====================================================================
//  مكتبة الرسوم العلمية (المرحلة الثالثة): نحو 3200 رسم مفتوح الترخيص (Bioicons وفيها رسوم Servier،
//  وHealth Icons) — الفهرس والصور المصغرة صغيرة، والرسم نفسه بيتنزل أول مرة بس من مصدره (عن طريق البرنامج
//  على /scipack/) ويتحفظ على الجهاز. وفوقها بحث أونلاين في PhyloPic (صور ظلية للكائنات، الملكية العامة بس).
//  البحث بالعربي والإنجليزي، وذكر المصدر بيتكتب تحت الرسم تلقائيًا لما الترخيص يطلبه (CC BY).
// =====================================================================
import { GLOSS, AR_OF, CAT_AR, TAB_CATS, normAr } from "./sci-lib-ar.js";
import { text, esc } from "./sci-svg.js";

const THUMBS = "/scipack/gh/waelMowis/LecturePlayer/master/packs/thumbs/v1/";
const CELL = 96, COLS = 10;
const LIC_NAME = { "cc-0": "CC0", "mit": "MIT", "cc-by-3.0": "CC BY 3.0", "cc-by-4.0": "CC BY 4.0", "pd": "ملكية عامة" };
export const needsCredit = lic => /^cc-by/.test(lic || "");
const enc = p => p.split("/").map(encodeURIComponent).join("/");

let lib = null;
/** الفهرس (مرة واحدة). */
export function loadLib() {
  if (lib) return lib;
  lib = fetch("lib/sci/library.json").then(r => { if (!r.ok) throw new Error("فهرس المكتبة غير موجود"); return r.json(); }).then(d => {
    d.list = d.items.map((a, i) => {
      const [src, cat, path, name, lic, author, sheet, cell, ar, tags] = a;
      const words = words_(name);
      const it = { i, src, cat: d.cats[cat], path, name, lic: d.lics[lic], author: d.authors[author].replace(/[-_]/g, " "), sheet: d.sheets[sheet], cell, ar, tags: tags || "", words };
      it.hay = " " + [...words, ...words_(it.tags), ...words_(it.cat.split("/")[1])].join(" ") + " ";
      return it;
    });
    return d;
  }).catch(e => { lib = null; throw e; });
  return lib;
}
const words_ = s => String(s || "").replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase().split(/[^a-z0-9]+/).filter(w => w && !/^\d+$/.test(w));

/** الاسم اللي يظهر: عربي لو الاسم كلمة واحدة معروفة، وإلا الإنجليزي منظّف. */
export function displayName(it) {
  const w = it.words.filter(x => x.length > 1);
  if (w.length === 1 && AR_OF.has(w[0])) return AR_OF.get(w[0]);
  return String(it.name).replace(/[_-]+/g, " ").replace(/\s*\(\d+\)\s*$/, "").replace(/\s+/g, " ").trim();
}

/** كلمات البحث ← مجموعات بدائل إنجليزية (كل مجموعة لازم تتحقق). */
export function queryTerms(q) {
  q = String(q || "").trim();
  if (!q) return [];
  const groups = [];
  // الإنجليزي كما هو
  const en = q.toLowerCase().match(/[a-z0-9]+/g) || [];
  for (const w of en) groups.push([w]);
  // العربي: أطول عبارة معروفة أولًا (خلية دم حمراء قبل خلية)
  let ar = normAr(q.replace(/[a-zA-Z0-9]+/g, " ")).split(" ").filter(Boolean);
  while (ar.length) {
    let hit = null;
    for (let n = Math.min(4, ar.length); n >= 1 && !hit; n--) {
      const k = ar.slice(0, n).join(" ");
      if (GLOSS.has(k)) hit = [n, GLOSS.get(k)];
    }
    if (!hit && ar[0].length >= 3) {
      // نسبة وجمع (نباتية ← نبات، خلوية ← خلي) ثم بداية كلمة (بكتير ← بكتيريا) ثم كلمة معروفة في أولها
      const w = ar[0], stems = [w, w.replace(/(يه|ي|ات|ون|ين|ه)$/, ""), w.replace(/(يه|ي)$/, "ه")].filter(x => x.length >= 2);
      for (const st of stems) if (!hit && GLOSS.has(st)) hit = [1, GLOSS.get(st)];
      const keys = [...GLOSS.keys()].filter(k => !k.includes(" "));
      if (!hit) { const pre = keys.filter(k => k.startsWith(w)); if (pre.length) hit = [1, [...new Set(pre.flatMap(k => GLOSS.get(k)))]]; }
      if (!hit) { const sub = keys.filter(k => k.length >= 3 && w.startsWith(k)).sort((a, b) => b.length - a.length)[0]; if (sub) hit = [1, GLOSS.get(sub)]; }
    }
    if (hit) { groups.push(hit[1]); ar = ar.slice(hit[0]); } else { groups.push(["\u0000" + ar[0]]); ar = ar.slice(1); }
  }
  return groups;
}

/** بحث في الفهرس (والفئة اختيارية). */
export async function search(q, cat, limit = 600) {
  const d = await loadLib();
  let groups = queryTerms(q);
  const unknown = groups.filter(g => g[0].startsWith("\u0000")).map(g => g[0].slice(1));
  groups = groups.filter(g => !g[0].startsWith("\u0000"));
  if (unknown.length && !groups.length) return { items: [], unknown };
  const out = [];
  for (const it of d.list) {
    if (cat && it.cat !== cat) continue;
    let score = 0, ok = true;
    for (const g of groups) {
      let best = 0;
      for (const w of g) {
        if (it.words.includes(w)) best = Math.max(best, 3);
        else if (it.hay.includes(" " + w + " ")) best = Math.max(best, 2);
        else if (w.length >= 4 && it.hay.includes(" " + w)) best = Math.max(best, 1);
      }
      if (!best) { ok = false; break; }
      score += best;
    }
    if (!ok) continue;
    score -= it.words.length * 0.05;
    out.push([score, it]);
    if (!groups.length && out.length >= limit) break;
  }
  if (groups.length) out.sort((a, b) => b[0] - a[0]);
  return { items: out.slice(0, limit).map(x => x[1]), unknown };
}

/** صورة مصغرة (جزء من لوحة الصور المصغرة). */
export function thumbEl(it, size = 80) {
  const e = document.createElement("span");
  e.className = "sci-th";
  const k = size / CELL;
  Object.assign(e.style, {
    width: size + "px", height: size + "px",
    backgroundImage: `url("${THUMBS}${encodeURIComponent(it.sheet)}")`,
    backgroundSize: `${COLS * CELL * k}px auto`,
    backgroundPosition: `${-(it.cell % COLS) * size}px ${-Math.floor(it.cell / COLS) * size}px`
  });
  return e;
}

/** عنوان الرسم الأصلي (بيتنزل مرة ويتحفظ). */
export async function picUrl(src) {
  if (src.src === "p") return `/scipack/phylo/img/${src.path}`;
  const d = await loadLib();
  if (src.src === "b") return `/scipack/gh/duerrsimon/bioicons/${d.sha.b}/static/icons/${enc(src.path)}`;
  return `/scipack/gh/resolvetosavelives/healthicons/${d.sha.h}/public/icons/svg/filled/${enc(src.path)}`;
}

const blobImg = blob => new Promise((res, rej) => { const u = URL.createObjectURL(blob), im = new Image(); im.onload = () => res([im, u]); im.onerror = () => { URL.revokeObjectURL(u); rej(new Error("الرسم تالف")); }; im.src = u; });
const toData = blob => new Promise(res => { const r = new FileReader(); r.onload = () => res(r.result); r.readAsDataURL(blob); });

/**
 * الرسم جاهز للسبورة: SVG (أو PNG لو الرسم ضخم) ملفوف في SVG فيه العنوان وذكر المصدر.
 * بيرجّع {inner, w, h} زي باقي الأشكال.
 */
export async function drawPic(src) {
  const url = await picUrl(src);
  let r;
  try { r = await fetch(url); } catch (e) { r = null; }
  if (!r || !r.ok) throw new Error("تعذّر تنزيل الرسم — يحتاج اتصالًا بالإنترنت أول مرة فقط، وبعدها يعمل بلا إنترنت");
  let blob = await r.blob();
  const isSvg = /svg/.test(blob.type) || /\.svg$/i.test(src.path);
  if (isSvg) blob = new Blob([await blob.text()], { type: "image/svg+xml" });
  const [im, u] = await blobImg(blob);
  let iw = im.naturalWidth || 600, ih = im.naturalHeight || 600, data;
  try {
    if (!isSvg || blob.size > 700_000) {
      // رسم ضخم ← صورة PNG بحجم معقول (ملف الشرح مايتقلش)
      const s = Math.min(1, 1400 / Math.max(iw, ih)), c = document.createElement("canvas");
      c.width = Math.max(1, Math.round(iw * s)); c.height = Math.max(1, Math.round(ih * s));
      c.getContext("2d").drawImage(im, 0, 0, c.width, c.height);
      data = c.toDataURL("image/png");
    } else data = await toData(blob);
  } finally { URL.revokeObjectURL(u); }
  // أطول بعد 600: الرسم الطولي مايبقاش ضخم على الصفحة
  const asp = iw / ih || 1, W = Math.round(asp >= 1 ? 600 : 600 * asp), H = Math.round(W / asp);
  let s = `<image href="${data}" x="0" y="0" width="${W}" height="${H}" preserveAspectRatio="xMidYMid meet"/>`, h = H;
  // العنوان وذكر المصدر بعرض مناسب حتى لو الرسم ضيق
  const CW = Math.max(W, 360), dx = (CW - W) / 2;
  s = `<g transform="translate(${dx},0)">${s}</g>`;
  if (src.cap) { s += text(CW / 2, h + 34, src.cap, { size: 30, bold: true }); h += 52; }
  if (needsCredit(src.lic) || src.credit) { s += text(CW / 2, h + 20, creditLine(src), { size: 15, fill: "#868e96" }); h += 30; }
  return { inner: s, w: CW, h };
}
export const creditLine = src => src.src === "p" ? `PhyloPic — ${src.author || ""} (${LIC_NAME[src.lic] || src.lic})` : `${src.author || ""} — ${LIC_NAME[src.lic] || src.lic}`;
export const licName = lic => LIC_NAME[lic] || lic;

// ---------------------------------------------------------------- PhyloPic (أونلاين)
/** أسماء شائعة ← الاسم العلمي اللي PhyloPic بيفهرس بيه. */
const PHYLO = {
  "قطة": "felis catus", "قط": "felis catus", "كلب": "canis lupus familiaris", "ذئب": "canis lupus", "أسد": "panthera leo", "نمر": "panthera tigris", "فهد": "acinonyx jubatus", "حصان": "equus caballus",
  "حمار": "equus asinus", "حمار وحشي": "equus quagga", "جمل": "camelus dromedarius", "بقرة": "bos taurus", "خروف": "ovis aries", "ماعز": "capra hircus", "غزال": "gazella", "زرافة": "giraffa camelopardalis",
  "فيل": "elephantidae", "دب": "ursidae", "قرد": "primates", "غوريلا": "gorilla", "شمبانزي": "pan troglodytes", "إنسان": "homo sapiens", "أرنب": "oryctolagus cuniculus", "فأر": "mus musculus",
  "جرذ": "rattus norvegicus", "خفاش": "chiroptera", "حوت": "cetacea", "دلفين": "delphinidae", "فقمة": "pinnipedia", "طائر": "aves", "نسر": "aquila", "صقر": "falco", "بومة": "strigiformes",
  "حمامة": "columba livia", "دجاجة": "gallus gallus", "بطة": "anas platyrhynchos", "نعامة": "struthio camelus", "بطريق": "spheniscidae", "ببغاء": "psittaciformes", "سمكة": "actinopterygii", "قرش": "selachimorpha",
  "ضفدع": "anura", "سلحفاة": "testudines", "تمساح": "crocodylidae", "ثعبان": "serpentes", "سحلية": "squamata", "ديناصور": "dinosauria", "حشرة": "insecta", "نحلة": "apis mellifera", "نملة": "formicidae",
  "فراشة": "lepidoptera", "ذبابة": "diptera", "بعوضة": "culicidae", "خنفساء": "coleoptera", "جرادة": "acrididae", "عنكبوت": "araneae", "عقرب": "scorpiones", "سلطعون": "brachyura", "أخطبوط": "octopoda",
  "حلزون": "gastropoda", "دودة": "annelida", "قنديل البحر": "scyphozoa", "نجم البحر": "asteroidea", "شجرة": "tracheophyta", "سرخس": "polypodiopsida", "فطر": "fungi", "بكتيريا": "bacteria"
};
export function phyloName(q) {
  q = String(q || "").trim();
  const n = normAr(q);
  for (const [k, v] of Object.entries(PHYLO)) if (normAr(k) === n) return v;
  return /^[a-z\s.-]+$/i.test(q) ? q.toLowerCase() : null;
}
const PD = href => /creativecommons\.org\/(publicdomain|licenses\/zero)/i.test(href || "");
const tail = href => { const m = String(href || "").match(/\/images\/([0-9a-f-]{36})\/(.+)$/i); return m ? m[1] + "/" + m[2] : null; };
let phyloBuild = null;
async function pj(path) {
  const r = await fetch("/scipack/phylo/api/" + path);
  if (!r.ok) throw new Error("PhyloPic: " + r.status);
  return r.json();
}
/** بحث PhyloPic: الاسم ← العقدة ← صورها (الملكية العامة وCC0 بس). */
export async function phyloSearch(q) {
  const name = phyloName(q);
  if (!name) throw new Error("اكتب اسم الكائن (مثل: قطة، نحلة) أو اسمه العلمي بالإنجليزية");
  phyloBuild ||= (await pj("")).build;
  const b = phyloBuild;
  const nodes = await pj(`nodes?build=${b}&filter_name=${encodeURIComponent(name)}&page=0&embed_items=true`);
  const node = nodes?._embedded?.items?.[0];
  if (!node) return [];
  let imgs;
  try { imgs = await pj(`images?build=${b}&filter_clade=${node.uuid}&filter_license_nc=false&filter_license_sa=false&filter_license_by=false&embed_items=true&page=0`); }
  catch (e) { imgs = await pj(`images?build=${b}&filter_clade=${node.uuid}&embed_items=true&page=0`); }
  const out = [];
  for (const it of imgs?._embedded?.items || []) {
    const L = it._links || {};
    if (!PD(L.license?.href)) continue;
    const vec = tail(L.vectorFile?.href), th = tail((L.thumbnailFiles || []).at(-1)?.href || (L.thumbnailFiles || [])[0]?.href);
    if (!vec) continue;
    out.push({ src: "p", path: vec, thumb: th ? "/scipack/phylo/img/" + th : null, name: L.specificNode?.title || node.names?.[0]?.map?.(x => x.text).join(" ") || name, lic: "pd", author: L.contributor?.title || it.attribution || "" });
  }
  return out;
}
