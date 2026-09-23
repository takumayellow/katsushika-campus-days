import http.client
from urllib.parse import urlsplit

import pytest

from kcd_e2e.server import content_type, serve_build, strip_encoding


@pytest.fixture
def build(tmp_path):
    root = tmp_path / "WebGL"
    (root / "Build").mkdir(parents=True)
    (root / "index.html").write_text("<canvas id=unity-canvas></canvas>", encoding="utf-8")
    (root / "Build" / "WebGL.loader.js").write_text("// loader", encoding="utf-8")
    (root / "Build" / "WebGL.data.unityweb").write_bytes(b"\x1f\x8b data")
    (root / "Build" / "WebGL.wasm.gz").write_bytes(b"\x1f\x8b wasm")
    (tmp_path / "secret.txt").write_text("outside the build", encoding="utf-8")
    return root


def get(base: str, path: str) -> tuple[int, dict, bytes]:
    parts = urlsplit(base)
    connection = http.client.HTTPConnection(parts.hostname, parts.port, timeout=5)
    try:
        connection.request("GET", path)
        response = connection.getresponse()
        return response.status, {k.lower(): v for k, v in response.getheaders()}, response.read()
    finally:
        connection.close()


def test_strip_encoding():
    assert strip_encoding("a.wasm.gz") == ("a.wasm", "gzip")
    assert strip_encoding("a.data.br") == ("a.data", "br")
    assert strip_encoding("a.wasm.unityweb") == ("a.wasm.unityweb", None)


def test_content_type():
    assert content_type("WebGL.wasm.unityweb") == "application/octet-stream"
    assert content_type("WebGL.wasm.gz") == "application/wasm"
    assert content_type("WebGL.loader.js") == "application/javascript"
    assert content_type("index.html") is None


def test_serves_like_pages_without_content_encoding(build):
    with serve_build(build) as base:
        assert base.startswith("http://127.0.0.1:") and base.endswith("/")
        status, headers, body = get(base, "/")
        assert status == 200 and b"unity-canvas" in body
        status, headers, _ = get(base, "/Build/WebGL.data.unityweb")
        assert status == 200
        assert headers["content-type"] == "application/octet-stream"
        assert "content-encoding" not in headers
        assert headers["cache-control"] == "no-store"
        status, headers, _ = get(base, "/Build/WebGL.wasm.gz")
        assert status == 200 and "content-encoding" not in headers


def test_content_encoding_flag_marks_precompressed_files(build):
    with serve_build(build, content_encoding=True) as base:
        status, headers, _ = get(base, "/Build/WebGL.wasm.gz")
        assert status == 200
        assert headers["content-encoding"] == "gzip"
        assert headers["content-type"] == "application/wasm"
        _, headers, _ = get(base, "/Build/WebGL.loader.js")
        assert "content-encoding" not in headers
        status, headers, _ = get(base, "/Build/missing.wasm.gz")
        assert status == 404 and "content-encoding" not in headers


def test_missing_file_is_404(build):
    with serve_build(build) as base:
        assert get(base, "/Build/WebGL.framework.js.unityweb")[0] == 404


def test_does_not_serve_outside_the_build(build):
    with serve_build(build) as base:
        for path in ["/../secret.txt", "/Build/../../secret.txt", "/%2e%2e/secret.txt"]:
            status, _, body = get(base, path)
            assert b"outside the build" not in body, path
            assert status == 404, path


def test_requires_index_html(tmp_path):
    with pytest.raises(FileNotFoundError):
        with serve_build(tmp_path):
            pass


def test_server_stops_after_the_block(build):
    with serve_build(build) as base:
        pass
    with pytest.raises(OSError):
        get(base, "/")
