// =====================================================================
//  مصادر العلوم الأونلاين (المرحلة الخامسة):
//   • PhET وGeoGebra وDesmos: بتتفتح من مواقعها الرسمية في نافذة جوه «مسار» (البرنامج بيفتحها)، و«التقط إلى السبورة».
//   • PubChem (ملكية عامة): بحث مركب ← الصيغة والكتلة واسم IUPAC وصورة البنية ثنائية الأبعاد.
//   • Wikimedia Commons: بحث صور بترخيص ومؤلف كل صورة، وبيتكتبوا تحتها على السبورة.
//  البحث بيعدّي على البرنامج (/scipack) — مفيش اتصال مباشر من الصفحة بأي موقع.
// =====================================================================
import { queryTerms } from "./sci-lib.js";
import { text } from "./sci-svg.js";

const phet = id => `https://phet.colorado.edu/sims/html/${id}/latest/${id}_all.html?locale=ar`;
/** [المعرّف، الاسم بالعربي] */
export const PHET = {
  phys: [["forces-and-motion-basics", "القوى والحركة: أساسيات"], ["friction", "الاحتكاك"], ["projectile-motion", "حركة المقذوفات"], ["collision-lab", "مختبر التصادمات"],
    ["energy-skate-park-basics", "حديقة التزلج: الطاقة"], ["pendulum-lab", "مختبر البندول"], ["masses-and-springs", "الكتل والنوابض"], ["hookes-law", "قانون هوك"],
    ["balancing-act", "الاتزان والعزم"], ["gravity-and-orbits", "الجاذبية والمدارات"], ["density", "الكثافة"], ["buoyancy", "الطفو"], ["under-pressure", "الضغط في السوائل"],
    ["circuit-construction-kit-dc", "بناء الدوائر الكهربائية (مستمر)"], ["ohms-law", "قانون أوم"], ["resistance-in-a-wire", "المقاومة في سلك"], ["capacitor-lab-basics", "المكثف"],
    ["coulombs-law", "قانون كولوم"], ["charges-and-fields", "الشحنات والمجالات"], ["faradays-law", "قانون فاراداي"], ["wave-on-a-string", "موجة على حبل"], ["waves-intro", "مقدمة في الموجات"],
    ["wave-interference", "تداخل الموجات"], ["bending-light", "انكسار الضوء"], ["geometric-optics", "البصريات الهندسية"], ["color-vision", "رؤية الألوان"], ["vector-addition", "جمع المتجهات"],
    ["gas-properties", "خصائص الغازات"], ["states-of-matter", "حالات المادة"]],
  chem: [["build-an-atom", "ابنِ ذرة"], ["isotopes-and-atomic-mass", "النظائر والكتلة الذرية"], ["balancing-chemical-equations", "موازنة المعادلات الكيميائية"], ["molecule-shapes", "أشكال الجزيئات"],
    ["molecule-polarity", "قطبية الجزيئات"], ["build-a-molecule", "ابنِ جزيئًا"], ["ph-scale", "مقياس الأس الهيدروجيني"], ["acid-base-solutions", "محاليل الأحماض والقواعد"], ["concentration", "التركيز"],
    ["molarity", "المولارية"], ["beers-law-lab", "قانون بير"], ["reactants-products-and-leftovers", "المتفاعلات والنواتج والفائض"], ["states-of-matter-basics", "حالات المادة: أساسيات"],
    ["atomic-interactions", "التفاعلات بين الذرات"], ["diffusion", "الانتشار"], ["gas-properties", "خصائص الغازات"]],
  math: [["fractions-intro", "مقدمة في الكسور"], ["fraction-matcher", "مطابقة الكسور"], ["arithmetic", "العمليات الحسابية"], ["make-a-ten", "كوّن عشرة"], ["number-line-integers", "خط الأعداد الصحيحة"],
    ["area-builder", "بناء المساحات"], ["area-model-multiplication", "الضرب بنموذج المساحة"], ["equality-explorer", "مستكشف المساواة"], ["graphing-lines", "تمثيل المستقيمات"],
    ["graphing-quadratics", "تمثيل الدوال التربيعية"], ["function-builder", "بناء الدوال"], ["trig-tour", "جولة في المثلثات"], ["unit-rates", "معدل الوحدة"], ["proportion-playground", "ملعب التناسب"],
    ["mean-share-and-balance", "الوسط الحسابي"], ["plinko-probability", "الاحتمالات"], ["curve-fitting", "مطابقة المنحنيات"], ["vector-addition", "جمع المتجهات"]],
  bio: [["natural-selection", "الانتخاب الطبيعي"], ["gene-expression-essentials", "التعبير الجيني"], ["neuron", "الخلية العصبية"], ["diffusion", "الانتشار"], ["color-vision", "رؤية الألوان"]]
};
export const SITES = {
  math: [["geogebra", "https://www.geogebra.org/graphing", "GeoGebra — الحاسبة البيانية", "📈"], ["geogebra", "https://www.geogebra.org/geometry", "GeoGebra — الهندسة", "📐"],
    ["geogebra", "https://www.geogebra.org/3d", "GeoGebra — ثلاثي الأبعاد", "🧊"], ["desmos", "https://www.desmos.com/calculator?lang=ar", "Desmos — الحاسبة البيانية", "∿"]],
  phys: [["geogebra", "https://www.geogebra.org/graphing", "GeoGebra — الحاسبة البيانية", "📈"]]
};
export const phetUrl = phet;
export const CREDIT = {
  phet: "PhET Interactive Simulations, University of Colorado Boulder — phet.colorado.edu (CC BY 4.0)",
  geogebra: "GeoGebra — geogebra.org", desmos: "Desmos — desmos.com"
};

/** لقطة من نافذة المصدر ← SVG فيه اللقطة وتحتها المصدر. */
export async function captionedShot(img, credit) {
  const [w, hh] = await new Promise((res, rej) => { const i = new Image(); i.onload = () => res([i.naturalWidth, i.naturalHeight]); i.onerror = () => rej(new Error("اللقطة تالفة")); i.src = img; });
  const W = 1200, H = Math.round(W * hh / w);
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H + 40}" width="${W}" height="${H + 40}"><rect width="${W}" height="${H + 40}" fill="#fff"/><image href="${img}" x="0" y="0" width="${W}" height="${H}"/>${text(W / 2, H + 22, credit, { size: 18, fill: "#495057" })}</svg>`;
  return "data:image/svg+xml;base64," + btoa(unescape(encodeURIComponent(svg)));
}

// ---------------------------------------------------------------- PubChem
const pc = path => fetch("/scipack/pubchem/compound/" + path);
/** بحث مركب بالاسم (إنجليزي) أو الصيغة. */
export async function pubchemCompound(q) {
  q = String(q || "").trim();
  if (!q) throw new Error("اكتب اسم المركب");
  if (/[؀-ۿ]/.test(q)) {
    const g = queryTerms(q).filter(x => !x[0].startsWith("\u0000"));
    if (!g.length) throw new Error("اكتب اسم المركب بالإنجليزية (مثل aspirin أو sodium chloride)");
    q = g.map(x => x[0]).join(" ");
  }
  const kind = /^([A-Z][a-z]?\d*)+$/.test(q) && /\d/.test(q) ? "fastformula" : "name";
  const r = await pc(`${kind}/${encodeURIComponent(q)}/property/MolecularFormula,MolecularWeight,IUPACName,Title/JSON`);
  if (r.status === 404) throw new Error("لا يوجد مركب بهذا الاسم في PubChem");
  if (!r.ok) throw new Error("تعذّر الاتصال بـ PubChem");
  const d = await r.json();
  const p = d?.PropertyTable?.Properties?.[0];
  if (!p) throw new Error("لا يوجد مركب بهذا الاسم في PubChem");
  return { cid: p.CID, title: p.Title || q, formula: p.MolecularFormula, weight: p.MolecularWeight, iupac: p.IUPACName, png: `/scipack/pubchem/compound/cid/${p.CID}/PNG?image_size=500x500` };
}
/** صورة البنية + الاسم والصيغة تحتها. */
export async function pubchemSvg(c) {
  const r = await fetch(c.png);
  if (!r.ok) throw new Error("تعذّر تنزيل صورة البنية");
  const data = await new Promise(async res => { const fr = new FileReader(); fr.onload = () => res(fr.result); fr.readAsDataURL(await r.blob()); });
  const sub = String(c.formula || "").replace(/\d/g, d => "₀₁₂₃₄₅₆₇₈₉"[d]);
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 500 580" width="500" height="580"><rect width="500" height="580" fill="#fff"/><image href="${data}" x="0" y="0" width="500" height="500"/>`
    + text(250, 518, `${c.title} — ${sub}`, { size: 22, bold: true, fill: "#111" }) + text(250, 556, `PubChem CID ${c.cid} (ملكية عامة)`, { size: 14, fill: "#868e96" }) + `</svg>`;
  return "data:image/svg+xml;base64," + btoa(unescape(encodeURIComponent(svg)));
}

// ---------------------------------------------------------------- Wikimedia Commons
const strip = s => String(s || "").replace(/<[^>]*>/g, "").replace(/\s+/g, " ").trim();
const imgPath = url => { const m = String(url || "").match(/upload\.wikimedia\.org\/wikipedia\/commons\/(.+)$/); return m ? "/scipack/wiki/img/" + m[1] : null; };
/** بحث صور (رسم أو صورة) بالعربي أو الإنجليزي. */
export async function commonsSearch(q) {
  q = String(q || "").trim();
  if (!q) return [];
  let en = q;
  if (/[؀-ۿ]/.test(q)) { const g = queryTerms(q).filter(x => !x[0].startsWith("\u0000")); if (g.length) en = g.map(x => x[0]).join(" "); }
  const params = new URLSearchParams({ action: "query", format: "json", generator: "search", gsrnamespace: "6", gsrlimit: "30", gsrsearch: `${en} filetype:bitmap|drawing`,
    prop: "imageinfo", iiprop: "url|extmetadata|size|mime", iiurlwidth: "320", iiextmetadatafilter: "LicenseShortName|Artist|ObjectName|UsageTerms" });
  const r = await fetch("/scipack/wiki/api?" + params);
  if (!r.ok) throw new Error("تعذّر الاتصال بـ Wikimedia Commons");
  const d = await r.json();
  const pages = Object.values(d?.query?.pages || {}).sort((a, b) => (a.index || 0) - (b.index || 0));
  const out = [];
  for (const p of pages) {
    const ii = p.imageinfo?.[0]; if (!ii) continue;
    const m = ii.extmetadata || {};
    const lic = strip(m.LicenseShortName?.value) || "غير محدد";
    if (/\bNC\b|non-?commercial|fair use/i.test(lic)) continue;
    const thumb = imgPath(ii.thumburl), big = imgPath(ii.thumburl?.replace(/\/\d+px-/, "/1200px-"));
    if (!thumb || !big) continue;
    out.push({ title: strip(m.ObjectName?.value) || p.title.replace(/^File:/, "").replace(/\.[a-z]+$/i, ""), author: strip(m.Artist?.value).slice(0, 80) || "—", lic, thumb, big: ii.width > 1200 ? big : imgPath(ii.url) || big, page: ii.descriptionurl, w: ii.width, h: ii.height });
  }
  return out;
}
export async function commonsSvg(it) {
  const r = await fetch(it.big);
  if (!r.ok) throw new Error("تعذّر تنزيل الصورة");
  const blob = await r.blob();
  const data = await new Promise(res => { const fr = new FileReader(); fr.onload = () => res(fr.result); fr.readAsDataURL(blob); });
  const [w, hh] = await new Promise((res, rej) => { const i = new Image(); i.onload = () => res([i.naturalWidth, i.naturalHeight]); i.onerror = () => rej(new Error("الصورة تالفة")); i.src = data; });
  const W = 800, H = Math.round(W * hh / w);
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H + 34}" width="${W}" height="${H + 34}"><rect width="${W}" height="${H + 34}" fill="#fff"/><image href="${data}" x="0" y="0" width="${W}" height="${H}"/>`
    + text(W / 2, H + 20, `${it.author} — ${it.lic} — Wikimedia Commons`, { size: 15, fill: "#495057" }) + `</svg>`;
  return "data:image/svg+xml;base64," + btoa(unescape(encodeURIComponent(svg)));
}
