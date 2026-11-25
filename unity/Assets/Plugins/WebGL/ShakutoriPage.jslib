// しゃくとりの森：ブラウザのページの出来事をゲームに伝える（タブを閉じる・切りかえる前にセーブする）
mergeInto(LibraryManager.library, {
  ShakuRegisterPageEvents: function () {
    if (typeof document === "undefined" || window.__shakuPageEvents) return;
    window.__shakuPageEvents = true;
    var send = function (method) {
      try { SendMessage("Game", method); } catch (e) { }
    };
    document.addEventListener("visibilitychange", function () {
      send(document.hidden ? "OnPageHidden" : "OnPageVisible");
    });
    window.addEventListener("pagehide", function () { send("OnPageHidden"); });
    window.addEventListener("beforeunload", function () { send("OnPageHidden"); });
  },

  // スマホのノッチなど、画面の端の隠れる部分の幅（画面に対する割合）。side: 0 左 1 上 2 右 3 下
  ShakuSafeInset: function (side) {
    try {
      var el = window.__shakuSafeEl;
      if (!el) {
        el = document.createElement("div");
        el.style.cssText = "position:fixed;left:0;top:0;width:0;height:0;visibility:hidden;pointer-events:none;" +
          "padding-left:env(safe-area-inset-left);padding-top:env(safe-area-inset-top);" +
          "padding-right:env(safe-area-inset-right);padding-bottom:env(safe-area-inset-bottom);";
        document.body.appendChild(el);
        window.__shakuSafeEl = el;
      }
      var cs = window.getComputedStyle(el);
      var v = [cs.paddingLeft, cs.paddingTop, cs.paddingRight, cs.paddingBottom][side];
      var px = parseFloat(v) || 0;
      var total = side % 2 == 0 ? window.innerWidth : window.innerHeight;
      return total > 0 ? px / total : 0;
    } catch (e) { return 0; }
  },

  ShakuVibrate: function (ms) {
    try { if (navigator.vibrate) navigator.vibrate(ms); } catch (e) { }
  },

  // 記録をファイルに書き出す（テキスト）
  ShakuDownloadText: function (textPtr, namePtr) {
    try {
      var blob = new Blob([UTF8ToString(textPtr)], { type: "application/json" });
      var url = URL.createObjectURL(blob);
      var a = document.createElement("a");
      a.href = url;
      a.download = UTF8ToString(namePtr);
      document.body.appendChild(a);
      a.click();
      setTimeout(function () { URL.revokeObjectURL(url); a.remove(); }, 1500);
    } catch (e) { }
  },

  // 記録のファイルをえらんで読みこむ（読めたら Game.OnSaveFile に中身を送る）
  ShakuPickTextFile: function () {
    try {
      var input = document.createElement("input");
      input.type = "file";
      input.accept = ".json,application/json,text/plain";
      input.style.display = "none";
      input.onchange = function () {
        var f = input.files && input.files[0];
        if (!f) { input.remove(); return; }
        var r = new FileReader();
        r.onload = function () { try { SendMessage("Game", "OnSaveFile", String(r.result)); } catch (e) { } input.remove(); };
        r.readAsText(f);
      };
      document.body.appendChild(input);
      input.click();
    } catch (e) { }
  },

  // 写真モードでとった絵を、ブラウザのダウンロードで保存する（PNG）
  ShakuDownloadPng: function (ptr, len, namePtr) {
    try {
      var bytes = HEAPU8.slice(ptr, ptr + len);
      var blob = new Blob([bytes], { type: "image/png" });
      var url = URL.createObjectURL(blob);
      var a = document.createElement("a");
      a.href = url;
      a.download = UTF8ToString(namePtr);
      document.body.appendChild(a);
      a.click();
      setTimeout(function () { URL.revokeObjectURL(url); a.remove(); }, 1500);
    } catch (e) { }
  },
});
