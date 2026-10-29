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
//   shadow_draws, shadow_triangles : draws と triangles のうち, 影のマップに描いた分 (#15)。
//
// 影のマップは, 深度の付け先が DEPTH_COMPONENT16 のテクスチャの framebuffer として見分ける。URP は影のマップを
// 16 ビットの深度のテクスチャ 1 枚に描く（High の 4 カスケードも同じ 1 枚に並べる）。ゲームが作る RenderTexture の
// 深度は 24 ビットなので, 16 ビットの深度のテクスチャに描くのは影のマップだけになる。
(() => {
  if (window.__kcdPerf) {
    return;
  }

  const TRIANGLES = 4;
  const TRIANGLE_STRIP = 5;
  const TRIANGLE_FAN = 6;
  const TEXTURE_2D = 0x0de1;
  const TEXTURE_BINDING_2D = 0x8069;
  const DEPTH_COMPONENT16 = 0x81a5;
  const FRAMEBUFFER = 0x8d40;
  const DRAW_FRAMEBUFFER = 0x8ca9;
  const READ_FRAMEBUFFER = 0x8ca8;
  const FRAMEBUFFER_BINDING = 0x8ca6;
  const READ_FRAMEBUFFER_BINDING = 0x8caa;
  const DEPTH_ATTACHMENT = 0x8d00;
  const DEPTH_STENCIL_ATTACHMENT = 0x821a;

  const state = {
    recording: false,
    frames: [],
    current: null,
  };
  // テクスチャの内部形式, framebuffer の深度の付け先の形式, 文脈ごとに今描いている framebuffer。
  const textureFormats = new WeakMap();
  const depthFormats = new WeakMap();
  const drawTargets = new WeakMap();

  function triangles(mode, count, instances) {
    let perInstance = 0;
    if (mode === TRIANGLES) {
      perInstance = Math.floor(count / 3);
    } else if (mode === TRIANGLE_STRIP || mode === TRIANGLE_FAN) {
      perInstance = Math.max(0, count - 2);
    }
    return perInstance * Math.max(0, instances);
  }

  function drawsToShadowMap(gl) {
    const target = drawTargets.get(gl);
    return !!target && depthFormats.get(target) === DEPTH_COMPONENT16;
  }

  function countDraw(gl, mode, count, instances) {
    const frame = state.current;
    if (!state.recording || frame === null) {
      return;
    }
    const drawn = triangles(mode, count, instances);
    frame.draws += 1;
    frame.triangles += drawn;
    if (drawsToShadowMap(gl)) {
      frame.shadow_draws += 1;
      frame.shadow_triangles += drawn;
    }
  }

  // 引数の並び: [mode の位置, count の位置, instances の位置（無ければ -1）]
  const DRAWS = {
    drawArrays: [0, 2, -1],
    drawElements: [0, 1, -1],
    drawArraysInstanced: [0, 2, 3],
    drawElementsInstanced: [0, 1, 4],
    drawRangeElements: [0, 3, -1],
  };

  function wrap(proto, name, before) {
    const original = proto[name];
    if (typeof original !== "function") {
      return;
    }
    proto[name] = function (...args) {
      before(this, args);
      return original.apply(this, args);
    };
  }

  // テクスチャの形式は, 確保するときに TEXTURE_2D に結んであるテクスチャに記録する。
  function recordTextureFormat(gl, args) {
    if (args[0] !== TEXTURE_2D) {
      return;
    }
    const texture = gl.getParameter(TEXTURE_BINDING_2D);
    if (texture) {
      textureFormats.set(texture, args[2]);
    }
  }

  function boundFramebuffer(gl, target) {
    return gl.getParameter(target === READ_FRAMEBUFFER ? READ_FRAMEBUFFER_BINDING : FRAMEBUFFER_BINDING);
  }

  function isDepth(attachment) {
    return attachment === DEPTH_ATTACHMENT || attachment === DEPTH_STENCIL_ATTACHMENT;
  }

  function hook(proto) {
    if (!proto) {
      return;
    }
    for (const [name, [modeAt, countAt, instancesAt]] of Object.entries(DRAWS)) {
      wrap(proto, name, (gl, args) => {
        countDraw(gl, args[modeAt], args[countAt], instancesAt < 0 ? 1 : args[instancesAt]);
      });
    }
    wrap(proto, "texStorage2D", recordTextureFormat);
    wrap(proto, "texImage2D", recordTextureFormat);
    wrap(proto, "bindFramebuffer", (gl, [target, framebuffer]) => {
      if (target === FRAMEBUFFER || target === DRAW_FRAMEBUFFER) {
        drawTargets.set(gl, framebuffer);
      }
    });
    wrap(proto, "framebufferTexture2D", (gl, [target, attachment, , texture]) => {
      const framebuffer = boundFramebuffer(gl, target);
      if (framebuffer && isDepth(attachment)) {
        depthFormats.set(framebuffer, texture ? textureFormats.get(texture) : undefined);
      }
    });
    // 深度を renderbuffer に付け替えたら, 影のマップではなくなる。
    wrap(proto, "framebufferRenderbuffer", (gl, [target, attachment]) => {
      const framebuffer = boundFramebuffer(gl, target);
      if (framebuffer && isDepth(attachment)) {
        depthFormats.delete(framebuffer);
      }
    });
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
        state.current = { t: timestamp, work: 0, draws: 0, triangles: 0, shadow_draws: 0, shadow_triangles: 0 };
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
