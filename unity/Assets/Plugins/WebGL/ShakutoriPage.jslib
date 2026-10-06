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
});
