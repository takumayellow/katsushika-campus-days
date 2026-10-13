// Playwright の add_init_script で、ページ自身のスクリプトより先に入れる計測 (#69)。
// ページ（WebGL テンプレート）は unityInstance を外に出していないので、ここで拾う。
//
// - createUnityInstance を包み、読み込みの進み具合・完了した時刻・Unity のインスタンスを残す。
//   ローダーは <script> で読まれ、onload で createUnityInstance を呼ぶ。script の load は
//   document の捕捉フェーズで先に受けられるので、そこで差し替える（関数宣言のグローバルは
//   書き込み可なので代入で置き換わる）。
// - WebAssembly.Memory を包み、Unity のヒープ（wasm のメモリ）の大きさを後から読めるようにする。
(() => {
  if (window.__kcdE2E) return;

  const state = {
    loaderLoadedAt: null,
    firstProgressAt: null,
    progressDoneAt: null,
    lastProgress: 0,
    readyAt: null,
    errorAt: null,
    error: null,
    instance: null,
    memories: [],
  };
  Object.defineProperty(window, "__kcdE2E", { value: state });

  const OriginalMemory = WebAssembly.Memory;
  WebAssembly.Memory = new Proxy(OriginalMemory, {
    construct(target, args) {
      const memory = Reflect.construct(target, args);
      state.memories.push(memory);
      return memory;
    },
  });

  // メモリを wasm 側が作って export する場合も拾う。
  const rememberExports = (result) => {
    try {
      const instance = result && result.instance ? result.instance : result;
      const exports = instance && instance.exports;
      if (exports) {
        for (const key of Object.keys(exports)) {
          if (exports[key] instanceof OriginalMemory) state.memories.push(exports[key]);
        }
      }
    } catch (_) {
      // 計測のための処理なので、失敗してもゲームの読み込みは止めない。
    }
    return result;
  };
  for (const name of ["instantiate", "instantiateStreaming"]) {
    const original = WebAssembly[name];
    if (typeof original !== "function") continue;
    WebAssembly[name] = function (...args) {
      return original.apply(this, args).then(rememberExports);
    };
  }

  document.addEventListener("load", () => {
    const create = window.createUnityInstance;
    if (typeof create !== "function" || create.__kcdWrapped) return;
    state.loaderLoadedAt = performance.now();

    const wrapped = function (canvas, config, onProgress) {
      const progress = (value) => {
        const now = performance.now();
        if (state.firstProgressAt === null) state.firstProgressAt = now;
        if (value >= 1 && state.progressDoneAt === null) state.progressDoneAt = now;
        state.lastProgress = value;
        if (typeof onProgress === "function") onProgress(value);
      };
      let promise;
      try {
        promise = create.call(this, canvas, config, progress);
      } catch (error) {
        state.error = String(error);
        state.errorAt = performance.now();
        throw error;
      }
      Promise.resolve(promise).then(
        (instance) => {
          state.instance = instance;
          state.readyAt = performance.now();
        },
        (error) => {
          state.error = String(error);
          state.errorAt = performance.now();
        });
      return promise;
    };
    wrapped.__kcdWrapped = true;
    window.createUnityInstance = wrapped;
  }, true);
})();
