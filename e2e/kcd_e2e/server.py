"""ローカルの WebGL ビルド（build/WebGL）を GitHub Pages と同じ条件で配信する。

Pages は .unityweb / .gz に Content-Encoding を付けない。ビルドは「復元フォールバック」で
ローダーが自分で展開する前提なので、既定ではここでも付けない。Pages で起動しないビルドを
ローカルで先に落とすためである。content_encoding=True にすると、.gz / .br に
Content-Encoding を付ける（サーバーを正しく設定した場合の挙動）。
"""

from __future__ import annotations

import contextlib
import sys
import threading
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Iterator
from urllib.parse import urlsplit

ENCODINGS = {".gz": "gzip", ".br": "br"}
TYPES = {
    ".unityweb": "application/octet-stream",
    ".wasm": "application/wasm",
    ".js": "application/javascript",
    ".data": "application/octet-stream",
    ".json": "application/json",
}


def strip_encoding(name: str) -> tuple[str, str | None]:
    """"a.wasm.gz" -> ("a.wasm", "gzip")。圧縮の拡張子が無ければ (name, None)。"""
    for suffix, encoding in ENCODINGS.items():
        if name.endswith(suffix):
            return name[: -len(suffix)], encoding
    return name, None


def content_type(name: str) -> str | None:
    base, _ = strip_encoding(name)
    for suffix, ctype in TYPES.items():
        if base.endswith(suffix):
            return ctype
    return None


class BuildRequestHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, content_encoding: bool = False, **kwargs):
        self.content_encoding = content_encoding
        self._status = 0
        super().__init__(*args, **kwargs)

    def guess_type(self, path):  # noqa: D401 - SimpleHTTPRequestHandler の上書き
        return content_type(str(path)) or super().guess_type(path)

    def send_response(self, code, message=None):
        self._status = code
        super().send_response(code, message)

    def end_headers(self):
        if self.content_encoding and self._status == 200:
            _, encoding = strip_encoding(urlsplit(self.path).path)
            if encoding:
                self.send_header("Content-Encoding", encoding)
        # テストのたびに読み直させる。Pages は max-age=600 だが、ここはビルドを差し替えて回すので。
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, format, *args):  # noqa: A002 - 親のシグネチャに合わせる
        pass


class _BuildServer(ThreadingHTTPServer):
    daemon_threads = True

    def handle_error(self, request, client_address):
        # ブラウザが読み込みを取りやめると（ページを閉じた、Unity の起動が失敗した）接続が切れる。
        # それはサーバーの失敗ではないので、トレースバックを出さない。
        if isinstance(sys.exc_info()[1], ConnectionError):
            return
        super().handle_error(request, client_address)


@contextlib.contextmanager
def serve_build(root: Path, content_encoding: bool = False,
                host: str = "127.0.0.1", port: int = 0) -> Iterator[str]:
    """root を配信し、トップの URL（末尾 /）を返す。抜けるとサーバーを止める。"""
    root = Path(root).resolve()
    if not (root / "index.html").is_file():
        raise FileNotFoundError(f"WebGL ビルドが見つからない: {root / 'index.html'}")

    handler = partial(BuildRequestHandler, directory=str(root), content_encoding=content_encoding)
    server = _BuildServer((host, port), handler)
    thread = threading.Thread(target=server.serve_forever, name="kcd-e2e-serve", daemon=True)
    thread.start()
    try:
        bound_host, bound_port = server.server_address[:2]
        yield f"http://{bound_host}:{bound_port}/"
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)
