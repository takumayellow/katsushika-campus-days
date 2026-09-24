"""ブラウザの起動設定と、WebGL が実 GPU で動いているかの判定。

2026-09-24 にこのマシン（Windows 11 / RTX 5070 Ti）で about:blank の WebGL 2 を調べた結果:

- 同梱 Chromium の headless（Playwright の既定 = headless shell）は SwiftShader（CPU 描画）。
- 同梱 Chromium の headless に --enable-gpu --use-angle=d3d11 を渡すと D3D11 の実 GPU。
- Edge（channel=msedge）の headless は新しい headless なので、何も渡さなくても実 GPU。
- headed はどちらも実 GPU。

そこで Windows の既定は Edge の headless にし、gl="gpu" では GPU を使う引数を足したうえで、
描画がソフトウェアなら失敗にする。GitHub Actions の Linux には GPU が無いので gl="software"
（SwiftShader を明示）で走らせる。
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field

BROWSERS = ("msedge", "chrome", "chromium")
GL_MODES = ("gpu", "software", "any")

SOFTWARE_RENDERER = re.compile(r"swiftshader|llvmpipe|softpipe|lavapipe|basic render|software",
                               re.IGNORECASE)


def default_browser(platform: str = sys.platform) -> str:
    return "msedge" if platform == "win32" else "chromium"


@dataclass(frozen=True)
class LaunchConfig:
    browser: str
    headless: bool
    gl: str
    args: tuple[str, ...] = field(default_factory=tuple)

    def launch_kwargs(self) -> dict:
        kwargs: dict = {"headless": self.headless, "args": list(self.args)}
        if self.browser != "chromium":
            kwargs["channel"] = self.browser
        return kwargs


def launch_config(browser: str, headless: bool, gl: str,
                  platform: str = sys.platform) -> LaunchConfig:
    if browser not in BROWSERS:
        raise ValueError(f"browser は {BROWSERS} のどれか: {browser}")
    if gl not in GL_MODES:
        raise ValueError(f"gl は {GL_MODES} のどれか: {gl}")

    # performance.memory を丸めずに返させる（ヒープの記録用）。
    args = ["--enable-precise-memory-info"]
    if gl == "gpu":
        args += ["--enable-gpu", "--ignore-gpu-blocklist"]
        if platform == "win32":
            args.append("--use-angle=d3d11")
    elif gl == "software":
        args += ["--use-angle=swiftshader", "--enable-unsafe-swiftshader"]
    return LaunchConfig(browser=browser, headless=headless, gl=gl, args=tuple(args))


def is_software_renderer(renderer: str | None) -> bool:
    """UNMASKED_RENDERER の文字列がソフトウェア描画か。取れなければ判断できないので False。"""
    return bool(renderer) and SOFTWARE_RENDERER.search(renderer) is not None


# ページの中で WebGL 2 のレンダラー名を取る。Unity のキャンバスとは別の小さなキャンバスで調べる。
RENDERER_JS = """() => {
  const canvas = document.createElement('canvas');
  const gl = canvas.getContext('webgl2');
  if (!gl) return { webgl2: false, vendor: null, renderer: null };
  const ext = gl.getExtension('WEBGL_debug_renderer_info');
  const result = {
    webgl2: true,
    vendor: ext ? gl.getParameter(ext.UNMASKED_VENDOR_WEBGL) : gl.getParameter(gl.VENDOR),
    renderer: ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER),
  };
  const lose = gl.getExtension('WEBGL_lose_context');
  if (lose) lose.loseContext();
  return result;
}"""
