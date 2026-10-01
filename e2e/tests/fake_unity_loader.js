// テンプレートの読み込み処理を確かめるための偽のローダー (#102)。Build/*.loader.js の代わりに配る。
// 本物（Unity 6000.6 の UnityLoader.js）がテンプレートに見せる振る舞いだけを真似る:
//   - onProgress(0) で始まり、ダウンロードの間は 0 < p < 1、起動の直前に onProgress(1)。
//   - 起動できたら Promise を解決し、途中で失敗したら reject する。
//   - framework.js が取れないときは config.showBanner(msg, 'error') を出すだけで、reject しない。
// どの経路を通るかは、テストが add_init_script で入れる window.__kcdFakeScenario で決める。
function createUnityInstance(canvas, config, onProgress) {
  const scenario = window.__kcdFakeScenario || "success";
  const state = { ready: false, release: null };
  window.__kcdFake = state;
  // テストが途中の見た目を確かめるまで止める。state.release() で先へ進む。
  const paused = () => new Promise((resolve) => { state.release = resolve; });
  const instance = { SetFullscreen() {}, Quit() { return Promise.resolve(); } };

  return new Promise((resolve, reject) => {
    const ready = () => {
      onProgress(1);
      state.ready = true;
      resolve(instance);
    };
    onProgress(0);
    switch (scenario) {
      case "success":
        onProgress(0.5);
        paused().then(ready);
        break;
      case "reject":
        // data の取得が途中で切れたとき、本物は data を読む preRun で TypeError になり、
        // それが unhandledrejection から reject に回る。
        onProgress(0.35);
        setTimeout(() => reject("TypeError: Cannot read properties of undefined (reading 'subarray')"), 0);
        break;
      case "error-banner":
        // 本物は framework.js の <script> が失敗すると帯を出すだけで、Promise は決着しない。
        // ほかのファイルの取得はそのあとも進み、進み具合が届く。
        onProgress(0.35);
        config.showBanner("Unable to load file Build/kcd.framework.js.unityweb! Check that the file exists on the remote server.", "error");
        setTimeout(() => onProgress(0.6), 0);
        break;
      case "error-banner-then-reject":
        onProgress(0.35);
        config.showBanner("Unable to parse Build/kcd.framework.js.unityweb! The file is corrupt.", "error");
        setTimeout(() => reject("TypeError: results[0] is not a function"), 0);
        break;
      case "warning":
        onProgress(0.2);
        config.showBanner("You can reduce startup time if you configure your web server.", "warning");
        break;
      case "error-after-ready":
        ready();
        setTimeout(() => config.showBanner("An error occurred running the Unity content on this page.", "error"), 0);
        break;
      default:
        reject("unknown scenario: " + scenario);
    }
  });
}
