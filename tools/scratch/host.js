// تشغيل واجهة البرمجة بالكتل جوه «مسار» + واجهة صغيرة للنافذة الأم (التقاط المنصة، حفظ/فتح المشروع).
(function () {
  "use strict";
  var G = window.GUI, app = document.getElementById("app");
  G.setAppElement(app);
  var Wrapped = G.AppStateHOC(G.default);
  var ready = null, readyP = new Promise(function (r) { ready = r; });
  var api = window.masarScratch = {
    vm: null,
    ready: readyP,
    /** صورة المنصة الحالية (PNG) */
    snapshot: function () {
      return new Promise(function (res) {
        var vm = api.vm; if (!vm || !vm.renderer) return res(null);
        vm.renderer.requestSnapshot(function (url) { res(url); });
        vm.renderer.draw();
      });
    },
    /** المشروع كملف sb3 (ArrayBuffer) */
    save: function () { return api.vm.saveProjectSb3().then(function (r) { return r instanceof Blob ? r.arrayBuffer() : r; }); },
    /** فتح مشروع: ملف sb3 (ArrayBuffer أو base64). بننسخه لذاكرة الصفحة دي (ArrayBuffer من النافذة الأم مابيتعرفش عليه هنا) */
    load: function (data) {
      var u;
      if (typeof data === "string") { var s = atob(data); u = new Uint8Array(s.length); for (var i = 0; i < s.length; i++) u[i] = s.charCodeAt(i); }
      else { var src = new Uint8Array(data); u = new Uint8Array(src.length); u.set(src); }
      return api.vm.loadProject(u.buffer);
    },
    title: "",
    /** عدّاد التعديلات (بيزيد مع كل تغيير في المشروع) */
    changes: 0
  };
  var props = {
    assetHost: "assets", projectHost: ".", projectId: "0",
    canSave: false, canCreateNew: false, canEditTitle: true, canShare: false, canRemix: false, canCreateCopy: false,
    showComingSoon: false, enableCommunity: false, hasCloudPermission: false, isScratchDesktop: false, backpackVisible: false,
    onClickLogo: function () { }, onShowPrivacyPolicy: function () { },
    onUpdateProjectTitle: function (t) { api.title = t; },
    onVmInit: function (vm) { api.vm = vm; vm.on("PROJECT_CHANGED", function () { api.changes++; }); },
    // جاهز بعد ما المشروع الافتراضي يتحمّل (لو فتحنا مشروع قبلها، الافتراضي كان هيكتب فوقه)
    onProjectLoaded: function () {
      var l = document.getElementById("masar-loading"); if (l) l.remove();
      setTimeout(function () { ready(api); }, 50);
    }
  };
  ReactDOM.render(React.createElement(Wrapped, props), app);
})();
