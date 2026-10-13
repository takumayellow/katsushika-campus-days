// 画質の段ごとの計測 (#70)。fps・1 フレームの処理・描画コール・三角形を, フレームごとに記録する。
// Playwright の add_init_script でページより先に読み込む。window.__kcdPerf.start() で記録を始め,
// stop() で記録を止めて, フレームの配列を返す。集計は e2e/kcd_e2e/perf.py の summarize() が行う。
//
// フレームの区切りは requestAnimationFrame のタイムスタンプ。同じタイムスタンプのコールバックは同じフレームに
// まとめ, 次のフレームが始まるまでの draw* を, そのフレームの分として数える。
//   t         : requestAnimationFrame のタイムスタンプ（ms）。間隔から fps を出す。
//   work      : そのフレームのコールバックにかかった時間の和（ms）。
//   draws     : draw* を呼んだ回数（影のパスとポストプロセスのパスも入る）。
//   triangles : 描いた三角形の数（TRIANGLES は頂点数 / 3, STRIP と FAN は頂点数 − 2, インスタンスの数を掛ける）。
(() => {
  if (window.__kcdPerf) {
    return;
  }

  const TRIANGLES = 4;
  const TRIANGLE_STRIP = 5;
  const TRIANGLE_FAN = 6;

  const state = {
    recording: false,
    frames: [],
    current: null,
  };

  function triangles(mode, count, instances) {
    let perInstance = 0;
    if (mode === TRIANGLES) {
      perInstance = Math.floor(count / 3);
    } else if (mode === TRIANGLE_STRIP || mode === TRIANGLE_FAN) {
      perInstance = Math.max(0, count - 2);
    }
    return perInstance * Math.max(0, instances);
  }

  function countDraw(mode, count, instances) {
    const frame = state.current;
    if (!state.recording || frame === null) {
      return;
    }
    frame.draws += 1;
    frame.triangles += triangles(mode, count, instances);
  }

  // 引数の並び: [mode の位置, count の位置, instances の位置（無ければ -1）]
  const DRAWS = {
    drawArrays: [0, 2, -1],
    drawElements: [0, 1, -1],
    drawArraysInstanced: [0, 2, 3],
    drawElementsInstanced: [0, 1, 4],
    drawRangeElements: [0, 3, -1],
  };

  function hook(proto) {
    if (!proto) {
      return;
    }
    for (const [name, [modeAt, countAt, instancesAt]] of Object.entries(DRAWS)) {
      const original = proto[name];
      if (typeof original !== "function") {
        continue;
      }
      proto[name] = function (...args) {
        countDraw(args[modeAt], args[countAt], instancesAt < 0 ? 1 : args[instancesAt]);
        return original.apply(this, args);
      };
    }
  }
  hook(window.WebGLRenderingContext && window.WebGLRenderingContext.prototype);
  hook(window.WebGL2RenderingContext && window.WebGL2RenderingContext.prototype);

  function closeFrame() {
    if (state.current !== null) {
      state.frames.push(state.current);
      state.current = null;
    }
  }

  const originalRaf = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = function (callback) {
    return originalRaf((timestamp) => {
      if (state.recording && (state.current === null || state.current.t !== timestamp)) {
        closeFrame();
        state.current = { t: timestamp, work: 0, draws: 0, triangles: 0 };
      }
      const started = performance.now();
      try {
        return callback(timestamp);
      } finally {
        if (state.recording && state.current !== null && state.current.t === timestamp) {
          state.current.work += performance.now() - started;
        }
      }
    });
  };

  window.__kcdPerf = {
    start() {
      state.frames = [];
      state.current = null;
      state.recording = true;
    },
    stop() {
      if (state.recording) {
        closeFrame();
      }
      state.recording = false;
      const frames = state.frames;
      state.frames = [];
      return frames;
    },
  };
})();
