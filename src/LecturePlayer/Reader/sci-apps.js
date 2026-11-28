// =====================================================================
//  الأدوات التفاعلية الكبيرة (المرحلة الرابعة): كل أداة نافذة كبيرة فوق السبورة، ونتيجتها بتنزل صورة على الصفحة
//  ومعاها مصدرها، فالنقر المزدوج بيرجّعها للأداة بنفس الحالة:
//   • الجدول الدوري العربي التفاعلي   • عارض الجزيئات ثلاثي الأبعاد (3Dmol.js، BSD) + بحث PubChem
//   • محرر الجزيئات (Ketcher، Apache-2.0)   • محاكي البصريات (Ray Optics Simulation، Apache-2.0)
//   • مشهد الحركة (Matter.js، MIT)
//  Ketcher ومحاكي البصريات كبار، فبيتنزلوا أول مرة بس (من حزم «مسار» على /scipack) ويتحفظوا على الجهاز.
// =====================================================================
import { loadElements, SERIES, STATE, confText, cellOf } from "./sci-ptable.js";

const PACKS = "/scipack/gh/waelMowis/LecturePlayer/master/packs/";
const KETCHER = PACKS + "ketcher/v3.21/index.html";
const OPTICS = PACKS + "rayoptics/v5.5/simulator/index.html";
const AR_DIG = "٠١٢٣٤٥٦٧٨٩";
/** الصيغ كما في الكتب (RDKit بيكتب ترتيب هيل: H3N بدل NH3). */
const FORMULA = { "Ammonia": "NH3", "Hydrogen chloride": "HCl", "Sulfuric acid": "H2SO4", "Nitric acid": "HNO3", "Sulfur dioxide": "SO2", "Hydrogen sulfide": "H2S", "Hydrogen peroxide": "H2O2", "Carbon monoxide": "CO", "Carbon tetrachloride": "CCl4", "Chloroform": "CHCl3", "Water": "H2O", "Methanol": "CH3OH", "Ethanol": "C2H5OH", "Acetic acid": "CH3COOH", "Formic acid": "HCOOH", "Urea": "CO(NH2)2" };

const h = (tag, attrs = {}, ...kids) => {
  const e = document.createElement(tag);
  for (const k in attrs) {
    const v = attrs[k];
    if (v == null || v === false) continue;
    if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (k === "cls") e.className = v;
    else if (k === "style" && typeof v === "object") Object.assign(e.style, v);
    else e.setAttribute(k, v === true ? "" : v);
  }
  for (const c of kids.flat(Infinity)) if (c != null && c !== false) e.append(c.nodeType ? c : document.createTextNode(String(c)));
  return e;
};
const loadScript = (src, global) => new Promise((res, rej) => {
  if (window[global]) return res(window[global]);
  const s = document.createElement("script");
  s.src = src; s.onload = () => res(window[global]); s.onerror = () => rej(new Error("تعذّر تحميل الأداة"));
  document.head.appendChild(s);
});
const imgSize = url => new Promise((res, rej) => { const i = new Image(); i.onload = () => res([i.naturalWidth, i.naturalHeight]); i.onerror = () => rej(new Error("الصورة تالفة")); i.src = url; });

export const Apps = {
  sci: null, win: null, cleanup: null,
  init(sci) { this.sci = sci; },
  isOpen() { return !!this.win; },

  /** نافذة كبيرة: عنوان + أزرار + محتوى. */
  frame(title, icon, actions, body, credit) {
    this.close();
    document.body.classList.add("sci-on");
    const msg = h("span", { cls: "sci-win-msg", "aria-live": "polite" });
    const win = h("div", { cls: "sci-win", role: "dialog", "aria-modal": "true", "aria-label": title },
      h("div", { cls: "sci-win-h" },
        h("b", {}, icon + " " + title),
        msg,
        h("div", { cls: "sci-win-act" }, ...actions, h("button", { cls: "sci-x", title: "إغلاق (Esc)", onclick: () => this.close() }, "✕"))),
      h("div", { cls: "sci-win-b" }, body),
      credit ? h("div", { cls: "sci-win-credit" }, credit) : null);
    win.addEventListener("keydown", e => { if (e.key === "Escape" && !e.defaultPrevented) { e.preventDefault(); this.close(); } });
    document.body.appendChild(win);
    this.win = win;
    this.say = (t, kind) => { msg.textContent = t || ""; msg.title = t || ""; msg.className = "sci-win-msg " + (kind || ""); };
    return win;
  },
  close() {
    try { this.cleanup?.(); } catch (e) { }
    this.cleanup = null;
    if (this.win) { this.win.remove(); this.win = null; }
    if (!this.sci?.dlg) document.body.classList.remove("sci-on");
  },
  /** نتيجة الأداة ← عنصر على الصفحة (أو تحديث نفس العنصر). */
  async insert(src, url, edit, width = 480) {
    let [w, hh] = await imgSize(url);
    if (/^data:image\/(png|jpeg)/.test(url)) {
      // لقطة الأداة (خلفيتها مصمتة) ← JPEG بعرض 1400 بكسل على الأكثر (ملف الشرح مايتقلش)
      const k = Math.min(1, 1400 / w), im = new Image(); im.src = url; await im.decode();
      const c = document.createElement("canvas"); c.width = Math.round(w * k); c.height = Math.round(hh * k);
      const g = c.getContext("2d"); g.fillStyle = "#fff"; g.fillRect(0, 0, c.width, c.height); g.drawImage(im, 0, 0, c.width, c.height);
      url = c.toDataURL("image/jpeg", 0.9); w = c.width; hh = c.height;
    }
    // width = العرض على الصفحة (الصفحة عرضها 1000)، أو دالة من مقاس الصورة
    const tw = typeof width === "function" ? width(w, hh) : width;
    const it = await this.sci.place({ ...src, k: "app", __img: url, __w: tw, __h: tw * hh / w }, edit);
    if (it) this.close();
    return it;
  },

  // ---------------------------------------------------------------- الجدول الدوري
  async ptable(state = {}) {
    const els = await loadElements();
    const info = h("div", { cls: "pt-info" });
    const grid = h("div", { cls: "pt-grid", dir: "ltr" });
    let cur = els[(state.z || 6) - 1], cells = [];
    const show = e => {
      cur = e;
      for (const c of cells) c.classList.toggle("on", c.dataset.z == e.z);
      const row = (k, v) => v == null || v === "" ? null : h("div", { cls: "pt-row" }, h("span", {}, k), h("b", {}, v));
      const K = t => t == null ? null : `${Math.round((t - 273.15) * 10) / 10} °س`;
      info.textContent = "";
      const add = (...x) => info.append(...x.filter(Boolean));
      add(
        h("div", { cls: "pt-big", style: { background: SERIES[e.series]?.[1] || "#f1f3f5" } }, h("small", {}, e.z), h("b", {}, e.sym), h("span", {}, e.ar)),
        row("الاسم بالإنجليزية", e.en), row("العدد الذري", e.z), row("الكتلة الذرية", e.mass), row("المجموعة", e.group || "—"), row("الدورة", e.period),
        row("الفئة", e.block), row("السلسلة", SERIES[e.series]?.[0]), row("الحالة (25 °س)", STATE[e.state]), row("التوزيع الإلكتروني", confText(e.conf)),
        row("السالبية الكهربية (باولنج)", e.en_), row("الكثافة (جم/سم³)", e.density || null), row("درجة الانصهار", K(e.mp)), row("درجة الغليان", K(e.bp)), row("سنة الاكتشاف", e.year),
        h("div", { cls: "pt-ins" },
          h("button", { cls: "pri", onclick: () => this.placeShape("element", { z: e.z }) }, "أدرج بطاقة العنصر"),
          e.z <= 36 ? h("button", { onclick: () => this.placeShape("atom", { z: e.z }) }, "أدرج نموذج بور") : null,
          h("button", { onclick: () => this.placeShape("ptable", {}) }, "أدرج الجدول كاملًا")));
    };
    for (const e of els) {
      const [c, r] = cellOf(e);
      const b = h("button", { cls: "pt-el", "data-z": e.z, title: `${e.ar} (${e.en})`, style: { gridColumn: c, gridRow: r + (r >= 9 ? 1 : 0), background: SERIES[e.series]?.[1] || "#f8f9fa" }, onclick: () => show(e) },
        h("small", {}, e.z), h("b", {}, e.sym), h("span", {}, e.ar));
      cells.push(b); grid.append(b);
    }
    grid.append(h("div", { cls: "pt-gap", style: { gridColumn: "1 / 19", gridRow: 8 } }));
    const legend = h("div", { cls: "pt-legend" }, Object.values(SERIES).map(([n, c]) => h("span", {}, h("i", { style: { background: c } }), n)));
    const q = h("input", { type: "search", cls: "sci-lib-q", placeholder: "ابحث: حديد، Fe، 26", "aria-label": "ابحث عن عنصر",
      oninput: () => { const v = q.value.trim().toLowerCase(); const e = els.find(x => x.ar === v || x.sym.toLowerCase() === v || x.en.toLowerCase() === v || String(x.z) === v.replace(/[٠-٩]/g, d => AR_DIG.indexOf(d))) || els.find(x => v.length > 1 && (x.ar.includes(v) || x.en.toLowerCase().startsWith(v))); if (e) show(e); } });
    this.frame("الجدول الدوري", "▦", [], h("div", { cls: "pt-wrap" }, h("div", { cls: "pt-main" }, grid, legend), h("div", { cls: "pt-side" }, q, info)), "البيانات: mendeleev (MIT)");
    show(cur);
    setTimeout(() => q.focus(), 0);
  },
  async placeShape(type, p) {
    try { await this.sci.place({ k: "shape", type, p: { ...this.sci.defaults(type), ...p }, sc: type === "ptable" ? 0.8 : 0.6 }); this.close(); }
    catch (e) { this.say(e.message, "err"); }
  },

  // ---------------------------------------------------------------- الجزيئات ثلاثية الأبعاد
  async mol3d(state = {}, edit) {
    const box = h("div", { cls: "m3-view" });
    const list = h("div", { cls: "m3-list" });
    const $3D = await loadScript("lib/sci/3dmol/3Dmol-min.js", "$3Dmol");
    const mols = await fetch("lib/sci/molecules.json").then(r => r.json());
    let st = { name: state.name || mols[0][0], sdf: state.sdf || mols[0][3], style: state.style || "ballstick", labels: !!state.labels, view: state.view || null };
    const STY = { ballstick: ["كرات وعصي", { stick: { radius: 0.14 }, sphere: { scale: 0.28 } }], sphere: ["نموذج فراغي", { sphere: { scale: 1 } }], stick: ["عصي", { stick: { radius: 0.2 } }], line: ["خطوط", { line: { linewidth: 3 } }] };
    this.frame("عارض الجزيئات ثلاثي الأبعاد", "🧬", [
      h("button", { cls: "pri", onclick: async () => {
        try { v.render(); const url = v.pngURI(); await this.insert({ app: "mol3d", name: st.name, sdf: st.sdf, style: st.style, labels: st.labels ? 1 : 0, view: v.getView() }, url, edit, 440); }
        catch (e) { this.say(e.message, "err"); }
      } }, "⤓ أدرج في السبورة")], h("div", { cls: "m3-wrap" }, h("div", { cls: "m3-side" },
        h("input", { type: "search", cls: "sci-lib-q", placeholder: "ابحث في القائمة، أو اكتب اسمًا إنجليزيًا واضغط Enter للبحث في PubChem", "aria-label": "ابحث عن جزيء",
          oninput: e => { const t = e.target.value.trim().toLowerCase(); for (const b of list.children) b.hidden = !!t && !b.dataset.k.includes(t); },
          onkeydown: e => { if (e.key === "Enter") { e.preventDefault(); pubchem(e.target.value.trim()); } } }),
        list), h("div", { cls: "m3-main" },
        h("div", { cls: "m3-bar" }, Object.entries(STY).map(([k, [l]]) => h("button", { "data-sty": k, cls: st.style === k ? "on" : "", onclick: e => { st.style = k; for (const b of e.currentTarget.parentElement.querySelectorAll("[data-sty]")) b.classList.toggle("on", b.dataset.sty === k); draw(true); } }, l)),
          h("label", { cls: "sci-chk" }, h("input", { type: "checkbox", checked: st.labels ? true : null, onchange: e => { st.labels = e.target.checked; draw(true); } }), " رموز الذرات"),
          h("button", { onclick: () => { spin = !spin; v.spin(spin ? "y" : false); } }, "⟳ دوران")),
        box)), "3Dmol.js (BSD) · الإحداثيات: RDKit وPubChem");
    const v = $3D.createViewer(box, { backgroundColor: "white", antialias: true, preserveDrawingBuffer: true });
    let spin = false;
    const draw = keepView => {
      const view = keepView ? v.getView() : null;
      v.clear();
      try { v.addModel(st.sdf, "sdf"); } catch (e) { this.say("تعذّر قراءة الجزيء", "err"); return; }
      v.setStyle({}, STY[st.style][1]);
      if (st.labels) v.addPropertyLabels("elem", { not: { elem: "H" } }, { fontColor: "black", backgroundOpacity: 0, fontSize: 16, alignment: "center" });
      if (view) v.setView(view); else if (st.view) { v.setView(st.view); st.view = null; } else v.zoomTo();
      v.render();
    };
    const pick = (name, sdf) => { st.name = name; st.sdf = sdf; for (const b of list.children) b.classList.toggle("on", b.dataset.name === name); draw(false); this.say(""); };
    for (const [ar, en, f, sdf] of mols) list.append(h("button", { "data-name": ar, "data-k": (ar + " " + en + " " + f).toLowerCase(), cls: ar === st.name ? "on" : "", onclick: () => pick(ar, sdf) }, h("b", {}, ar), h("small", {}, String(FORMULA[en] || f).replace(/\d/g, d => "₀₁₂₃₄₅₆₇₈₉"[d]))));
    const pubchem = async name => {
      if (!name) return;
      const local = mols.find(m => m[0] === name || m[1].toLowerCase() === name.toLowerCase());
      if (local) return pick(local[0], local[3]);
      if (/[؀-ۿ]/.test(name)) { this.say("للبحث في PubChem اكتب الاسم بالإنجليزية (مثل caffeine)", "warn"); return; }
      this.say("جارٍ البحث في PubChem…");
      try {
        const r = await fetch(`/scipack/pubchem/compound/name/${encodeURIComponent(name)}/SDF?record_type=3d`);
        if (!r.ok) throw new Error(r.status === 404 ? "لا يوجد مركب بهذا الاسم في PubChem (أو لا توجد له بنية ثلاثية الأبعاد)" : "تعذّر الاتصال بـ PubChem");
        const sdf = await r.text();
        if (!/M\s+END/.test(sdf)) throw new Error("لا توجد بنية ثلاثية الأبعاد لهذا المركب");
        pick(name, sdf.split("$$$$")[0]);
        this.say("✓ من PubChem", "ok");
      } catch (e) { this.say(e.message, "err"); }
    };
    this.cleanup = () => { try { v.clear(); } catch (e) { } };
    draw(false);
    window.__m3 = v;
  },

  // ---------------------------------------------------------------- محرر الجزيئات (Ketcher)
  ketcher(state = {}, edit) {
    const frame = h("iframe", { cls: "sci-iframe", src: KETCHER, title: "محرر الجزيئات" });
    let ready = false, seq = 0;
    const waits = new Map();
    const call = (op, data) => new Promise((res, rej) => { const id = ++seq; waits.set(id, { res, rej }); frame.contentWindow.postMessage({ masarKetcher: 1, op, data, id }, "*"); setTimeout(() => { if (waits.delete(id)) rej(new Error("المحرر لم يستجب")); }, 20000); });
    const onMsg = async e => {
      const m = e.data;
      if (!m || !m.masarKetcher || e.source !== frame.contentWindow) return;
      if (m.ready) { ready = true; this.say(""); if (state.mol) { try { await call("set", state.mol); } catch (x) { } } return; }
      const w = waits.get(m.id); if (!w) return; waits.delete(m.id);
      m.ok ? w.res(m.data) : w.rej(new Error(m.data));
    };
    this.frame("محرر الجزيئات", "⌬", [
      h("button", { cls: "pri", onclick: async () => {
        if (!ready) { this.say("المحرر لم يكتمل تحميله بعد", "warn"); return; }
        try {
          const r = await call("get");
          if (r.empty || !r.svg) { this.say("ارسم الجزيء أولًا", "warn"); return; }
          const url = "data:image/svg+xml;base64," + btoa(unescape(encodeURIComponent(r.svg)));
          await this.insert({ app: "ketcher", mol: r.mol, smiles: r.smiles }, url, edit, (w) => Math.min(560, Math.max(240, w * 0.9)));
        } catch (e) { this.say(e.message, "err"); }
      } }, "⤓ أدرج في السبورة")], frame, "Ketcher (EPAM، Apache-2.0)");
    // بعد frame (اللي بيقفل أي نافذة قديمة وبيشغّل تنظيفها)
    window.addEventListener("message", onMsg);
    this.cleanup = () => window.removeEventListener("message", onMsg);
    this.say("جارٍ تحميل المحرر… (أول مرة يحتاج اتصالًا بالإنترنت)");
    window.__ketcherCall = call;
  },

  // ---------------------------------------------------------------- محاكي البصريات
  optics(state = {}, edit) {
    try { localStorage.setItem("rayOpticsAutoSyncUrl", "on"); localStorage.setItem("rayOpticsHelp", "off"); } catch (e) { }
    const frame = h("iframe", { cls: "sci-iframe", src: OPTICS + "?ar" + (state.hash ? "#" + state.hash : ""), title: "محاكي البصريات" });
    const EX = [["", "ابدأ من مثال جاهز…"], ["convex-lens", "عدسة محدبة"], ["concave-lens", "عدسة مقعرة"], ["lens-images", "تكوين الصور بالعدسات"], ["virtual-image-formation", "الصورة التقديرية"],
      ["images-formed-by-two-mirrors", "صور مرآتين متقاطعتين"], ["parabolic-mirror", "مرآة مقعرة (قطع مكافئ)"], ["periscope", "البيرسكوب"], ["internal-reflection", "الانعكاس الكلي الداخلي"],
      ["prisms", "المناشير"], ["chromatic-dispersion", "تحلل الضوء الأبيض"], ["rainbows", "قوس قزح"], ["apparent-depth", "العمق الظاهري"], ["broken-pencil", "القلم المكسور في الماء"],
      ["optics-of-human-eye", "العين البشرية"], ["camera-obscura", "الكاميرا ذات الثقب"], ["compound-microscope", "المجهر المركب"], ["telescope", "التلسكوب"], ["single-ray-demo", "شعاع واحد"]];
    const ex = h("select", { "aria-label": "مثال جاهز", onchange: e => { if (e.target.value) { frame.src = OPTICS + "?ar#" + e.target.value; this.say("جارٍ تحميل المثال…"); } } }, EX.map(([v, l]) => h("option", { value: v }, l)));
    frame.addEventListener("load", () => {
      // روابط المعرض والصفحة الرئيسية للموقع مش موجودة جوه «مسار»
      try { const st = frame.contentDocument.createElement("style"); st.textContent = "#welcome a{pointer-events:none;color:inherit!important;text-decoration:none} a[href*='gallery'],a[href$='/ray-optics/'],a[href*='github.com']{display:none!important}"; frame.contentDocument.head.appendChild(st); } catch (e) { }
    });
    this.frame("محاكي البصريات", "🔦", [ex,
      h("button", { cls: "pri", onclick: async () => {
        try {
          const d = frame.contentDocument;
          const ids = ["canvasGrid", "canvasBelowLight", "canvasLight", "canvasLightWebGL", "canvasLightWebGPU", "canvasAboveLight"];
          const cs = ids.map(i => d.getElementById(i)).filter(c => c && c.width && getComputedStyle(c).display !== "none");
          if (!cs.length) throw new Error("المحاكي لم يكتمل تحميله بعد");
          const out = document.createElement("canvas"); out.width = cs[0].width; out.height = cs[0].height;
          const g = out.getContext("2d"); g.fillStyle = "#000"; g.fillRect(0, 0, out.width, out.height);
          for (const c of cs) g.drawImage(c, 0, 0, out.width, out.height);
          const hash = (frame.contentWindow.location.hash || "").slice(1);
          await this.insert({ app: "optics", hash }, out.toDataURL("image/png"), edit, 640);
        } catch (e) { this.say(e.message, "err"); }
      } }, "⤓ التقط إلى السبورة")], frame, "Ray Optics Simulation (Yi-Ting Tu وآخرون، Apache-2.0)");
    this.say("جارٍ تحميل المحاكي… (أول مرة يحتاج اتصالًا بالإنترنت)");
    frame.addEventListener("load", () => this.say(""));
  },

  // ---------------------------------------------------------------- مشهد الحركة (Matter.js)
  async motion(state = {}, edit) {
    const M = await loadScript("lib/sci/matter/matter.min.js", "Matter");
    const { Engine, Render, Runner, Bodies, Composite, Mouse, MouseConstraint, Constraint, Body, Events } = M;
    const host = h("div", { cls: "mo-view" });
    let running = true, vectors = true;
    const COLORS = ["#1971c2", "#e03131", "#2f9e44", "#f08c00", "#7048e8"];
    let ci = 0;
    const col = () => COLORS[ci++ % COLORS.length];
    const btn = (label, fn, title) => h("button", { title, onclick: fn }, label);
    this.frame("مشهد الحركة", "⚽", [
      h("button", { cls: "pri", onclick: async () => { try { await this.insert({ app: "motion" }, render.canvas.toDataURL("image/png"), edit, 600); } catch (e) { this.say(e.message, "err"); } } }, "⤓ التقط إلى السبورة")],
      h("div", { cls: "mo-wrap" }, h("div", { cls: "mo-bar" },
        btn("⚪ كرة", () => add(Bodies.circle(rx(), 80, 24 + Math.random() * 16, { restitution: 0.75, render: { fillStyle: col() } }))),
        btn("◼ صندوق", () => add(Bodies.rectangle(rx(), 80, 60, 60, { render: { fillStyle: col() } }))),
        btn("◺ مستوى مائل", () => add(Bodies.rectangle(W0 * 0.35, H0 * 0.62, 420, 18, { isStatic: true, angle: 0.42, render: { fillStyle: "#868e96" } })), "سطح ثابت مائل"),
        btn("🕰 بندول", () => { const b = Bodies.circle(W0 * 0.7 + 160, 230, 26, { render: { fillStyle: col() } }); add(b, Constraint.create({ pointA: { x: W0 * 0.7, y: 40 }, bodyB: b, length: 230, stiffness: 1, render: { strokeStyle: "#495057", lineWidth: 2 } })); }),
        btn("〰 نابض", () => { const b = Bodies.rectangle(W0 * 0.5, 260, 70, 50, { render: { fillStyle: col() } }); add(b, Constraint.create({ pointA: { x: W0 * 0.5, y: 40 }, bodyB: b, length: 170, stiffness: 0.02, damping: 0.01, render: { strokeStyle: "#e03131", lineWidth: 3, type: "spring" } })); }),
        btn("⬤⬤ تصادم", () => { add(Bodies.circle(80, H0 - 60, 30, { frictionAir: 0, friction: 0, restitution: 1, render: { fillStyle: "#1971c2" } }), Bodies.circle(W0 - 80, H0 - 60, 30, { frictionAir: 0, friction: 0, restitution: 1, render: { fillStyle: "#e03131" } })); const bs = Composite.allBodies(engine.world).slice(-2); Body.setVelocity(bs[0], { x: 9, y: 0 }); Body.setVelocity(bs[1], { x: -9, y: 0 }); }, "كرتان متقابلتان"),
        h("span", { cls: "sep" }),
        btn("⏯ إيقاف/تشغيل", () => { running = !running; runner.enabled = running; }),
        btn("↻ إعادة", () => reset()),
        h("label", { cls: "sci-chk" }, h("input", { type: "checkbox", checked: true, onchange: e => { engine.gravity.y = e.target.checked ? 1 : 0; } }), " الجاذبية"),
        h("label", { cls: "sci-chk" }, h("input", { type: "checkbox", checked: true, onchange: e => { vectors = e.target.checked; } }), " متجهات السرعة")),
        host), "Matter.js (MIT) — اسحب الأجسام بالفأرة");
    const W0 = Math.max(600, Math.min(1400, innerWidth - 80)), H0 = Math.max(380, Math.min(760, innerHeight - 190));
    const engine = Engine.create();
    const render = Render.create({ element: host, engine, options: { width: W0, height: H0, wireframes: false, background: "#ffffff", pixelRatio: 1 } });
    const runner = Runner.create();
    const rx = () => 120 + Math.random() * (W0 - 240);
    const add = (...b) => Composite.add(engine.world, b);
    const walls = () => [Bodies.rectangle(W0 / 2, H0 + 25, W0 + 100, 50, { isStatic: true, render: { fillStyle: "#495057" } }), Bodies.rectangle(-25, H0 / 2, 50, H0 * 3, { isStatic: true }), Bodies.rectangle(W0 + 25, H0 / 2, 50, H0 * 3, { isStatic: true })];
    const reset = () => { Composite.clear(engine.world, false); add(...walls()); ci = 0; };
    reset();
    const mouse = Mouse.create(render.canvas);
    add(MouseConstraint.create(engine, { mouse, constraint: { stiffness: 0.2, render: { visible: false } } }));
    render.mouse = mouse;
    Events.on(render, "afterRender", () => {
      if (!vectors) return;
      const g = render.context;
      g.save(); g.strokeStyle = "#f08c00"; g.fillStyle = "#f08c00"; g.lineWidth = 2.5;
      for (const b of Composite.allBodies(engine.world)) {
        if (b.isStatic) continue;
        const vx = b.velocity.x * 6, vy = b.velocity.y * 6, L = Math.hypot(vx, vy);
        if (L < 6) continue;
        const x2 = b.position.x + vx, y2 = b.position.y + vy, a = Math.atan2(vy, vx);
        g.beginPath(); g.moveTo(b.position.x, b.position.y); g.lineTo(x2, y2); g.stroke();
        g.beginPath(); g.moveTo(x2, y2); g.lineTo(x2 - 10 * Math.cos(a - 0.4), y2 - 10 * Math.sin(a - 0.4)); g.lineTo(x2 - 10 * Math.cos(a + 0.4), y2 - 10 * Math.sin(a + 0.4)); g.fill();
      }
      g.restore();
    });
    Render.run(render); Runner.run(runner, engine);
    this.cleanup = () => { Render.stop(render); Runner.stop(runner); Engine.clear(engine); render.canvas.remove(); };
    window.__motion = { engine, render };
  },

  /** فتح أداة من مصدر عنصر (نقر مزدوج). */
  reopen(sci, edit) {
    if (sci.app === "mol3d") return this.mol3d(sci, edit);
    if (sci.app === "ketcher") return this.ketcher(sci, edit);
    if (sci.app === "optics") return this.optics(sci, edit);
    if (sci.app === "motion") return this.motion(sci, edit);
    if (sci.app === "web") return this.sci.web(sci.source, sci.url, sci.title, edit);
    if (sci.app === "arduino") return this.sci.ardu.open(sci, edit);
    if (sci.app === "scratch") return this.sci.scratch.open(sci, edit);
  }
};
