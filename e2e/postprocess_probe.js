// URP の後処理が Web 版で本当に描かれているかを、WebGL の呼び出しから数える（#72）。
// Playwright の add_init_script でページより先に読み込む。window.__kcdPostProbe.start() で数え始め、
// stop() で結果を返す。判定は e2e/postprocess_check.py の evaluate() が行う。
//
// プログラムはアクティブな uniform 名で見分ける。どの名前も URP 17.6 のシェーダ 1 つにしか無い。
//   _Vignette_Params2 : UberPost（Vignette は UberPost の中で実行時に分岐するので、強さは値で見る）
//   _Bloom_Params     : UberPost の Bloom 合成の変種
//   _SourceTexLowMip  : Bloom.shader の Upsample パス（Gaussian。Bloom 専用のパスが走った証拠）
//   _HueSatCon        : LutBuilderLdr / LutBuilderHdr（Tonemapping・ColorAdjustments・WhiteBalance を LUT に焼く）
(() => {
  if (window.__kcdPostProbe) {
    return;
  }

  const TAGS = {
    uber: "_Vignette_Params2",
    uberBloom: "_Bloom_Params",
    bloomUpsample: "_SourceTexLowMip",
    lutBuilder: "_HueSatCon",
  };
  const WATCHED = new Set(["_Vignette_Params2", "_Bloom_Params"]);

  const programInfo = new WeakMap(); // WebGLProgram -> { tags, blockIndex, values }
  const locationInfo = new WeakMap(); // WebGLUniformLocation -> { program, name }
  const currentProgram = new WeakMap(); // context -> WebGLProgram
  const contexts = new Set();

  const state = {
    tick: 0,
    counting: false,
    lastDrawTick: -1,
    ticks: 0,
    renderedFrames: 0,
    draws: {},
    drawnUber: new Set(),
  };

  function resetCounts() {
    state.lastDrawTick = -1;
    state.ticks = 0;
    state.renderedFrames = 0;
    state.draws = { any: 0 };
    for (const tag of Object.keys(TAGS)) {
      state.draws[tag] = 0;
    }
    state.drawnUber = new Set();
  }
  resetCounts();

  function baseName(name) {
    return name.endsWith("[0]") ? name.slice(0, -3) : name;
  }

  function classify(gl, program) {
    let info = programInfo.get(program);
    if (info && info.classified) {
      return info;
    }
    info = info || { values: {} };
    info.tags = new Set();
    info.blockIndex = {};
    const count = gl.getProgramParameter(program, gl.ACTIVE_UNIFORMS) || 0;
    for (let i = 0; i < count; i++) {
      const active = gl.getActiveUniform(program, i);
      if (!active) {
        continue;
      }
      const name = baseName(active.name);
      for (const [tag, uniform] of Object.entries(TAGS)) {
        if (name === uniform) {
          info.tags.add(tag);
        }
      }
      if (WATCHED.has(name) && typeof gl.getActiveUniforms === "function") {
        // -1 なら素の uniform（uniform4fv で値が来る）。0 以上なら UBO の中。
        info.blockIndex[name] = gl.getActiveUniforms(program, [i], gl.UNIFORM_BLOCK_INDEX)[0];
      }
    }
    info.classified = true;
    programInfo.set(program, info);
    return info;
  }

  function recordValue(location, values) {
    const loc = location && locationInfo.get(location);
    if (!loc || !WATCHED.has(loc.name)) {
      return;
    }
    const info = programInfo.get(loc.program) || { values: {} };
    info.values[loc.name] = values;
    programInfo.set(loc.program, info);
  }

  function onDraw(gl) {
    if (!state.counting) {
      return;
    }
    const program = currentProgram.get(gl);
    if (!program) {
      return;
    }
    if (state.lastDrawTick !== state.tick) {
      state.lastDrawTick = state.tick;
      state.renderedFrames++;
    }
    state.draws.any++;
    const info = classify(gl, program);
    for (const tag of info.tags) {
      state.draws[tag]++;
    }
    if (info.tags.has("uber")) {
      state.drawnUber.add(program);
    }
  }

  function wrap(proto, name, after) {
    const original = proto[name];
    if (typeof original !== "function") {
      return;
    }
    proto[name] = function (...args) {
      const result = original.apply(this, args);
      after(this, args, result);
      return result;
    };
  }

  function hook(proto) {
    wrap(proto, "useProgram", (gl, args) => {
      contexts.add(gl);
      currentProgram.set(gl, args[0]);
    });
    wrap(proto, "linkProgram", (gl, args) => {
      // 作り直したプログラムは uniform が変わるので分類をやり直す。
      const info = programInfo.get(args[0]);
      if (info) {
        info.classified = false;
      }
    });
    wrap(proto, "getUniformLocation", (gl, args, location) => {
      if (location) {
        locationInfo.set(location, { program: args[0], name: baseName(String(args[1])) });
      }
    });
    wrap(proto, "uniform4fv", (gl, args) => {
      const data = args[1];
      const offset = args[2] || 0;
      if (data && data.length >= offset + 4) {
        recordValue(args[0], [data[offset], data[offset + 1], data[offset + 2], data[offset + 3]]);
      }
    });
    wrap(proto, "uniform4f", (gl, args) => {
      recordValue(args[0], [args[1], args[2], args[3], args[4]]);
    });
    for (const draw of ["drawArrays", "drawElements", "drawArraysInstanced", "drawElementsInstanced", "drawRangeElements"]) {
      wrap(proto, draw, (gl) => onDraw(gl));
    }
  }

  if (window.WebGL2RenderingContext) {
    hook(WebGL2RenderingContext.prototype);
  }
  if (window.WebGLRenderingContext) {
    hook(WebGLRenderingContext.prototype);
  }

  function onFrame() {
    state.tick++;
    if (state.counting) {
      state.ticks++;
    }
    requestAnimationFrame(onFrame);
  }
  requestAnimationFrame(onFrame);

  window.__kcdPostProbe = {
    start() {
      resetCounts();
      state.counting = true;
    },
    stop() {
      state.counting = false;
      const uber = [];
      for (const program of state.drawnUber) {
        const info = programInfo.get(program) || { tags: new Set(), values: {}, blockIndex: {} };
        uber.push({
          bloomVariant: info.tags.has("uberBloom"),
          vignetteParams2: info.values._Vignette_Params2 || null,
          bloomParams: info.values._Bloom_Params || null,
          vignetteBlockIndex: info.blockIndex._Vignette_Params2 ?? null,
        });
      }
      return {
        contexts: contexts.size,
        ticks: state.ticks,
        renderedFrames: state.renderedFrames,
        draws: { ...state.draws },
        uber,
      };
    },
  };
})();
