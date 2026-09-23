// 葛飾キャンパスデイズ 3D モデルビューア
//
// build/viewer/models/index.json を読んでモデル一覧を作り、選ばれた GLB を three.js で表示する。
// GLB は Draco 圧縮されているので DRACOLoader を噛ませる。CDN は jsDelivr の three パッケージだけ。

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { DRACOLoader } from 'three/addons/loaders/DRACOLoader.js';

const THREE_CDN = 'https://cdn.jsdelivr.net/npm/three@0.160.0/';
const SKY = 0x9ec9f0;

// キャンパス全景でカメラのフィット計算から外すもの（地面と周辺市街は広すぎる）。
const FIT_SKIP = ['site_ground', 'bld_background'];
// 建物内部で「壁と天井を隠す」が拾うノード名の接頭辞。
const SHELL_PREFIX = ['wall_', 'ceil'];

const GROUPS = [
  { kind: 'campus', title: 'キャンパス全景', gallery: 'キャンパス全景' },
  { kind: 'interior', title: '建物内部', gallery: '建物内部' },
  { kind: 'character', title: 'キャラクター', gallery: 'キャラクター' },
];

const el = (id) => document.getElementById(id);
const dom = {
  wrap: el('canvas-wrap'),
  list: el('model-list'),
  meta: el('build-meta'),
  hudName: el('hud-name'),
  hudNote: el('hud-note'),
  hudStats: el('hud-stats'),
  animCtl: el('anim-ctl'),
  animSelect: el('anim-select'),
  wallCtl: el('wall-ctl'),
  wallToggle: el('wall-toggle'),
  resetView: el('reset-view'),
  loading: el('loading'),
  loadingText: el('loading-text'),
  error: el('error'),
  errorText: el('error-text'),
  errorRetry: el('error-retry'),
  chips: el('gallery-chips'),
  thumbs: el('thumbs'),
  lightbox: el('lightbox'),
  lightboxImg: el('lightbox-img'),
  lightboxCap: el('lightbox-cap'),
  lightboxClose: el('lightbox-close'),
};

// ------------------------------------------------------------------ three.js
const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.05;
dom.wrap.appendChild(renderer.domElement);

const scene = new THREE.Scene();
scene.background = new THREE.Color(SKY);

const camera = new THREE.PerspectiveCamera(45, 16 / 10, 0.1, 5000);
camera.position.set(6, 4, 8);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.08;
controls.maxPolarAngle = Math.PI * 0.495;

scene.add(new THREE.HemisphereLight(0xdfefff, 0x4a4438, 2.0));
const sun = new THREE.DirectionalLight(0xfff3df, 2.4);
sun.position.set(1, 2.2, 1.4);
scene.add(sun);
const fill = new THREE.DirectionalLight(0xbfd8ff, 0.7);
fill.position.set(-1.4, 0.8, -1.2);
scene.add(fill);

const dracoLoader = new DRACOLoader();
dracoLoader.setDecoderPath(THREE_CDN + 'examples/jsm/libs/draco/');
const gltfLoader = new GLTFLoader();
gltfLoader.setDRACOLoader(dracoLoader);

// -------------------------------------------------------------------- state
const state = {
  index: null,
  current: null,     // 選択中の index.json エントリ
  root: null,        // シーンに載っている THREE.Group
  mixer: null,
  action: null,
  clips: [],
  shells: [],        // 壁・天井ノード
  fitBox: new THREE.Box3(),
  ready: false,
  errors: [],
};
window.KCDViewer = state;

// -------------------------------------------------------------------- utils
function fmtBytes(n) {
  return n >= 1e6 ? (n / 1e6).toFixed(2) + ' MB' : Math.round(n / 1e3) + ' KB';
}

function fmtInt(n) {
  return n.toLocaleString('ja-JP');
}

function showLoading(text) {
  dom.loadingText.textContent = text;
  dom.loading.hidden = false;
  dom.error.hidden = true;
}

function showError(message) {
  dom.loading.hidden = true;
  dom.errorText.textContent = message;
  dom.error.hidden = false;
}

function resize() {
  const w = dom.wrap.clientWidth;
  const h = dom.wrap.clientHeight;
  if (!w || !h) return;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}

// モデルの一部だけを含む境界箱。名前が skip に前方一致するノードは除く。
function measure(root, skip) {
  const box = new THREE.Box3();
  root.updateWorldMatrix(true, true);
  root.traverse((node) => {
    if (!node.isMesh || !node.visible) return;
    let name = node.name || '';
    for (let p = node.parent; p && p !== root; p = p.parent) {
      if (!p.visible) return;      // 隠した壁や天井は画角の計算に入れない
      if (name === '') name = p.name || '';
    }
    if (skip.some((s) => name.startsWith(s))) return;
    box.expandByObject(node);
  });
  if (box.isEmpty()) box.setFromObject(root);
  return box;
}

// 境界箱がちょうど画面に収まる位置へカメラを置く。
// 球で見積もった距離から始めて、箱の 8 頂点を投影しながら 3 回詰める
// （平たく横長なモデルだと球の見積もりは大きく外れる）。
function frame(box, elevation = 0.42, azimuth = 0.9) {
  const size = box.getSize(new THREE.Vector3());
  const center = box.getCenter(new THREE.Vector3());
  const radius = Math.max(size.length() * 0.5, 0.001);
  const dir = new THREE.Vector3(
    Math.cos(elevation) * Math.sin(azimuth),
    Math.sin(elevation),
    Math.cos(elevation) * Math.cos(azimuth),
  );
  const corners = [];
  for (const x of [box.min.x, box.max.x]) {
    for (const y of [box.min.y, box.max.y]) {
      for (const z of [box.min.z, box.max.z]) corners.push(new THREE.Vector3(x, y, z));
    }
  }
  camera.near = Math.max(radius / 5000, 0.01);
  camera.far = radius * 400;
  let dist = radius / Math.sin(THREE.MathUtils.degToRad(camera.fov) * 0.5) * 1.1;
  const camPt = new THREE.Vector3();
  const right = new THREE.Vector3();
  const up = new THREE.Vector3();
  const focal = 1 / Math.tan(THREE.MathUtils.degToRad(camera.fov) * 0.5);
  // 箱を投影した矩形を見ながら、距離と注視点を交互に詰める。距離だけ合わせると
  // 奥行きのある形（体育館の床など）が画面の下に寄って上半分が空になる。
  for (let i = 0; i < 6; i += 1) {
    camera.position.copy(center).addScaledVector(dir, dist);
    camera.lookAt(center);
    camera.updateMatrixWorld(true);
    let minX = Infinity;
    let maxX = -Infinity;
    let minY = Infinity;
    let maxY = -Infinity;
    let behind = false;
    for (const corner of corners) {
      camPt.copy(corner).applyMatrix4(camera.matrixWorldInverse);
      const depth = -camPt.z;
      // カメラの後ろ（または直前）に回った頂点は投影が反転するので使わない。
      if (!(depth > camera.near)) { behind = true; continue; }
      const px = (focal / camera.aspect) * camPt.x / depth;
      const py = focal * camPt.y / depth;
      minX = Math.min(minX, px);
      maxX = Math.max(maxX, px);
      minY = Math.min(minY, py);
      maxY = Math.max(maxY, py);
    }
    if (behind) { dist *= 1.8; continue; }
    if (!Number.isFinite(minX) || !Number.isFinite(minY)) break;
    const halfH = dist / focal;
    const halfW = halfH * camera.aspect;
    right.setFromMatrixColumn(camera.matrixWorld, 0);
    up.setFromMatrixColumn(camera.matrixWorld, 1);
    center.addScaledVector(right, (minX + maxX) * 0.5 * halfW)
      .addScaledVector(up, (minY + maxY) * 0.5 * halfH);
    const span = Math.max((maxX - minX) * 0.5, (maxY - minY) * 0.5);
    if (span <= 0) break;
    dist *= span / 0.88;   // 画面の 88 % に収める
  }
  camera.position.copy(center).addScaledVector(dir, dist);
  camera.near = Math.max(dist / 4000, 0.01);
  camera.far = Math.max(dist * 40, radius * 80);
  camera.updateProjectionMatrix();
  controls.target.copy(center);
  controls.minDistance = radius * 0.05;
  controls.maxDistance = dist * 8;
  controls.update();
  // 太陽の向きはモデルの大きさに合わせて置き直す。
  sun.position.copy(center).add(new THREE.Vector3(1, 2.2, 1.4).multiplyScalar(radius));
  sun.target.position.copy(center);
  scene.add(sun.target);
}

function disposeCurrent() {
  if (!state.root) return;
  scene.remove(state.root);
  state.root.traverse((node) => {
    if (node.isMesh) {
      node.geometry.dispose();
      const mats = Array.isArray(node.material) ? node.material : [node.material];
      mats.forEach((m) => m && m.dispose());
    }
  });
  state.root = null;
  state.mixer = null;
  state.action = null;
  state.clips = [];
  state.shells = [];
}

// ------------------------------------------------------------- model loading
function loadModel(entry) {
  state.ready = false;
  state.current = entry;
  updateList();
  updateHud(entry);
  showLoading(entry.name + ' を読み込み中…');

  return new Promise((resolve, reject) => {
    gltfLoader.load(
      'models/' + entry.file,
      (gltf) => {
        disposeCurrent();
        const root = gltf.scene;
        scene.add(root);
        state.root = root;

        state.shells = [];
        root.traverse((node) => {
          if (!node.isMesh) return;
          node.frustumCulled = true;
          if (SHELL_PREFIX.some((p) => (node.name || '').startsWith(p))) {
            state.shells.push(node);
          }
        });

        state.clips = gltf.animations || [];
        state.mixer = state.clips.length ? new THREE.AnimationMixer(root) : null;
        setupAnimUI(entry);
        setupWallUI(entry);

        refit();
        frame(state.fitBox, elevationFor(entry.kind));

        dom.loading.hidden = true;
        state.ready = true;
        resolve(entry);
      },
      (ev) => {
        if (ev && ev.total) {
          const pct = Math.min(100, Math.round((ev.loaded / ev.total) * 100));
          dom.loadingText.textContent = entry.name + ' を読み込み中… ' + pct + '%';
        }
      },
      (err) => {
        showError(entry.name + ' の読み込みに失敗しました（' + entry.file + '）。'
          + '通信状態を確認して再試行してください。');
        state.errors.push(String(err && err.message ? err.message : err));
        reject(err);
      },
    );
  });
}

function setupAnimUI(entry) {
  const clips = state.clips;
  dom.animCtl.hidden = clips.length === 0;
  dom.animSelect.replaceChildren();
  if (!clips.length) return;
  const labels = (state.index && state.index.anim_labels) || {};
  const order = entry.animations && entry.animations.length ? entry.animations : clips.map((c) => c.name);
  const sorted = clips.slice().sort((a, b) => order.indexOf(a.name) - order.indexOf(b.name));
  sorted.forEach((clip) => {
    const opt = document.createElement('option');
    opt.value = clip.name;
    opt.textContent = labels[clip.name] ? labels[clip.name] + '（' + clip.name + '）' : clip.name;
    dom.animSelect.appendChild(opt);
  });
  playClip(sorted[0].name);
}

function playClip(name) {
  if (!state.mixer) return;
  const clip = state.clips.find((c) => c.name === name) || state.clips[0];
  if (!clip) return;
  const next = state.mixer.clipAction(clip);
  next.reset().setLoop(THREE.LoopRepeat, Infinity).play();
  if (state.action && state.action !== next) {
    state.action.crossFadeTo(next, 0.25, false);
  }
  state.action = next;
  dom.animSelect.value = clip.name;
}

function setupWallUI(entry) {
  const usable = entry.kind === 'interior' && state.shells.length > 0;
  dom.wallCtl.hidden = !usable;
  if (usable) dom.wallToggle.checked = true;
  applyWallToggle();
}

function applyWallToggle() {
  const hide = !dom.wallCtl.hidden && dom.wallToggle.checked;
  state.shells.forEach((node) => { node.visible = !hide; });
}

// 見えているものだけで画角の基準になる箱を取り直す（カメラは動かさない）。
function refit() {
  if (!state.root || !state.current) return;
  state.fitBox = measure(state.root,
    state.current.kind === 'campus' ? FIT_SKIP : []);
}

function elevationFor(kind) {
  if (kind === 'character') return 0.18;
  if (kind === 'interior') return 0.58;
  return 0.55;
}

function updateHud(entry) {
  dom.hudName.textContent = entry.name;
  dom.hudNote.textContent = entry.note || '';
  dom.hudStats.replaceChildren();
  const rows = [
    ['三角形', fmtInt(entry.tris)],
    ['サイズ', fmtBytes(entry.bytes)],
  ];
  if (entry.animations && entry.animations.length) {
    rows.push(['アニメ', entry.animations.length + ' 本']);
  }
  rows.forEach(([k, v]) => {
    const box = document.createElement('div');
    const dt = document.createElement('dt');
    dt.textContent = k;
    const dd = document.createElement('dd');
    dd.textContent = v;
    box.append(dt, dd);
    dom.hudStats.appendChild(box);
  });
}

// ---------------------------------------------------------------- model list
function updateList() {
  dom.list.querySelectorAll('.item').forEach((btn) => {
    btn.setAttribute('aria-current', String(btn.dataset.id === (state.current && state.current.id)));
  });
}

function buildList(index) {
  dom.list.replaceChildren();
  GROUPS.forEach((group) => {
    const models = index.models.filter((m) => m.kind === group.kind);
    if (!models.length) return;
    const title = document.createElement('p');
    title.className = 'group-title';
    title.textContent = group.title + '（' + models.length + '）';
    dom.list.appendChild(title);
    models.forEach((entry) => {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'item';
      btn.dataset.id = entry.id;
      btn.setAttribute('aria-current', 'false');
      const name = document.createElement('strong');
      name.textContent = entry.name;
      const sub = document.createElement('span');
      sub.className = 'sub';
      sub.textContent = fmtInt(entry.tris) + ' tris ・ ' + fmtBytes(entry.bytes);
      btn.append(name, sub);
      btn.addEventListener('click', () => {
        if (state.current && state.current.id === entry.id) return;
        selectGallery(group.gallery);
        loadModel(entry).catch(() => {});
      });
      dom.list.appendChild(btn);
    });
  });
}

// --------------------------------------------------------------------
function buildGallery(index) {
  const groups = ['すべて', ...new Set(index.previews.map((p) => p.group))];
  dom.chips.replaceChildren();
  groups.forEach((g) => {
    const chip = document.createElement('button');
    chip.type = 'button';
    chip.className = 'chip';
    chip.dataset.group = g;
    chip.textContent = g;
    chip.setAttribute('aria-pressed', 'false');
    chip.addEventListener('click', () => selectGallery(g));
    dom.chips.appendChild(chip);
  });
  selectGallery('すべて');
}

function selectGallery(group) {
  const index = state.index;
  if (!index) return;
  const chips = [...dom.chips.querySelectorAll('.chip')];
  const known = chips.some((c) => c.dataset.group === group);
  const active = known ? group : 'すべて';
  chips.forEach((c) => c.setAttribute('aria-pressed', String(c.dataset.group === active)));
  const items = active === 'すべて'
    ? index.previews
    : index.previews.filter((p) => p.group === active);
  dom.thumbs.replaceChildren();
  items.forEach((p) => {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'thumb';
    const img = document.createElement('img');
    img.src = 'previews/' + (p.thumb || p.file);
    img.alt = p.label;
    img.loading = 'lazy';
    img.decoding = 'async';
    const cap = document.createElement('span');
    cap.textContent = p.label;
    btn.append(img, cap);
    btn.addEventListener('click', () => openLightbox(p));
    dom.thumbs.appendChild(btn);
  });
}

function openLightbox(p) {
  dom.lightboxImg.src = 'previews/' + p.file;
  dom.lightboxImg.alt = p.label;
  dom.lightboxCap.textContent = p.label + '（' + p.file + '）';
  dom.lightbox.hidden = false;
}

function closeLightbox() {
  dom.lightbox.hidden = true;
  dom.lightboxImg.removeAttribute('src');
}

// --------------------------------------------------------------------- boot
dom.animSelect.addEventListener('change', () => playClip(dom.animSelect.value));
dom.wallToggle.addEventListener('change', () => {
  applyWallToggle();
  refit();
});
dom.resetView.addEventListener('click', () => {
  if (state.root) frame(state.fitBox, elevationFor(state.current.kind));
});
dom.errorRetry.addEventListener('click', () => {
  if (state.current) loadModel(state.current).catch(() => {});
});
dom.lightboxClose.addEventListener('click', closeLightbox);
dom.lightbox.addEventListener('click', (ev) => {
  if (ev.target === dom.lightbox) closeLightbox();
});
window.addEventListener('keydown', (ev) => {
  if (ev.key === 'Escape') closeLightbox();
});
window.addEventListener('resize', resize);

const clock = new THREE.Clock();
renderer.setAnimationLoop(() => {
  const dt = clock.getDelta();
  if (state.mixer) state.mixer.update(dt);
  controls.update();
  renderer.render(scene, camera);
});

resize();
// レイアウト確定後にもう一度（aspect-ratio の解決が 1 フレーム遅れることがある）。
requestAnimationFrame(resize);

showLoading('モデル一覧を読み込み中…');
fetch('models/index.json', { cache: 'no-cache' })
  .then((res) => {
    if (!res.ok) throw new Error('HTTP ' + res.status);
    return res.json();
  })
  .then((index) => {
    state.index = index;
    dom.meta.textContent = 'モデル ' + index.models.length + ' 件 ・ 合計 '
      + fmtBytes(index.models.reduce((a, m) => a + m.bytes, 0))
      + ' ・ 生成 ' + index.generated.replace('T', ' ');
    buildList(index);
    buildGallery(index);
    const first = index.models[0];
    if (!first) throw new Error('index.json にモデルがありません');
    return loadModel(first);
  })
  .catch((err) => {
    showError('モデル一覧を読み込めませんでした: ' + (err && err.message ? err.message : err)
      + '。models/index.json を配信できているか確認してください。');
    state.errors.push(String(err));
  });
