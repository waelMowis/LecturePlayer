// حفظ تلقائي لشغل الأدوات الكبيرة (محاكي أردوينو، البرمجة بالكتل) على الجهاز (IndexedDB في بروفايل «مسار»):
// لكل سبورة مفتاح، ولكل عنصر مفتوح من السبورة مفتاح، عشان الشغل مايضيعش لو النافذة اتقفلت أو البرنامج وقع.
const DB = "masar-tools", ST = "kv";
let dbp = null;
const db = () => dbp ||= new Promise((res, rej) => {
  const r = indexedDB.open(DB, 1);
  r.onupgradeneeded = () => r.result.createObjectStore(ST);
  r.onsuccess = () => res(r.result);
  r.onerror = () => { dbp = null; rej(r.error); };
});
const tx = async (mode, fn) => {
  const d = await db();
  return new Promise((res, rej) => {
    const t = d.transaction(ST, mode), req = fn(t.objectStore(ST));
    t.oncomplete = () => res(req?.result);
    t.onerror = () => rej(t.error); t.onabort = () => rej(t.error);
  });
};
export const ToolStore = {
  get: k => tx("readonly", s => s.get(k)).catch(() => undefined),
  set: (k, v) => tx("readwrite", s => s.put({ ...v, at: Date.now() }, k)).catch(() => { }),
  del: k => tx("readwrite", s => s.delete(k)).catch(() => { }),
};
/** بصمة نص (لمقارنة النسخ من غير تخزين النسخة كلها) */
export function hash(s) {
  s = String(s ?? ""); let h = 2166136261;
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
  return (h >>> 0).toString(36) + ":" + s.length;
}
