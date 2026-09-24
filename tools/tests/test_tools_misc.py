"""tools/ の小物: Unity ログの検査と、配布 zip の作り方 (#68)。"""

import io
import zipfile

import check_unity_log
import deploy_pages
import package_zip


# ---- check_unity_log ----

def _scan(tmp_path, text, encoding="utf-8"):
    path = tmp_path / "unity.log"
    path.write_bytes(text.encode(encoding))
    out = io.StringIO()
    rc = check_unity_log.scan(path, out)
    return rc, out.getvalue()


def test_scan_missing_log(tmp_path):
    out = io.StringIO()
    assert check_unity_log.scan(tmp_path / "nope.log", out) == 2
    assert "ログがありません" in out.getvalue()


def test_scan_clean_log(tmp_path):
    log = "\n".join([
        "[KCD] SceneBuilder: campus を組み立てた",
        "Saved scene: Assets/Scenes/Campus.unity",
        "ビルド結果: Succeeded",
        "Assets/Scripts/Foo.cs(3,5): warning CS0618: 'X' is obsolete",
        # Unity が正常系で出す、例外とは無関係な行
        "Native Crash Reporting: ExceptionHandler installed",
        "UnityEngine.StackTraceUtility:ExtractStackTrace ()",
        "Command line: Unity.exe -batchmode -executeMethod KCD.Editor.Foo.ThrowIfException",
        "Used memory by Exceptions 12 kb\t(1 allocations)",
    ])
    rc, out = _scan(tmp_path, log)
    assert rc == 0, out
    assert "error CS : 0" in out and "Exception: 0" in out
    assert "[SCENE] Assets/Scenes/Campus.unity" in out
    assert "[BUILD] Succeeded" in out
    assert "[KCD] SceneBuilder" in out


def test_scan_compile_error(tmp_path):
    rc, out = _scan(tmp_path, "Assets/Scripts/Foo.cs(10,3): error CS0103: The name 'x' does not exist\n")
    assert rc == 1
    assert "error CS : 1" in out and "[CS]" in out


def test_scan_runtime_exception(tmp_path):
    rc, out = _scan(tmp_path, "NullReferenceException: Object reference not set\n  at KCD.Foo.Bar ()\n")
    assert rc == 1
    assert "Exception: 1" in out and "[EX] NullReferenceException" in out


def test_scan_failed_build(tmp_path):
    rc, out = _scan(tmp_path, "ビルド結果: Failed\n")
    assert rc == 1
    assert "[BUILD] Failed" in out


def test_scan_reads_cp932_log(tmp_path):
    rc, out = _scan(tmp_path, "[KCD] 保存しました\nビルド結果: Succeeded\n", encoding="cp932")
    assert rc == 0
    assert "[KCD] 保存しました" in out


def test_main_without_logs_prints_usage(capsys):
    assert check_unity_log.main(["check_unity_log.py"]) == 2
    assert "check_unity_log.py" in capsys.readouterr().err


# ---- package_zip ----

def _fake_windows_build(root):
    files = {
        "KatsushikaCampusDays.exe": b"exe",
        "UnityPlayer.dll": b"dll",
        "KatsushikaCampusDays_Data/level0": b"scene",
        "KatsushikaCampusDays_Data/Managed/KCD.Runtime.dll": b"managed",
        "KatsushikaCampusDays_BackUpThisFolder_ButDontShipItWithYourGame/il2cpp.cpp": b"x",
        "KatsushikaCampusDays_BurstDebugInformation_DoNotShip/lib.pdb": b"x",
    }
    for rel, data in files.items():
        path = root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    return root


def test_is_excluded(tmp_path):
    build = tmp_path / "build"
    assert package_zip.is_excluded(build / "KCD_BackUpThisFolder_ButDontShipItWithYourGame" / "a.cpp", build)
    assert package_zip.is_excluded(build / "KCD_BurstDebugInformation_DoNotShip" / "sub" / "b.pdb", build)
    assert not package_zip.is_excluded(build / "KatsushikaCampusDays_Data" / "level0", build)
    # build_dir より上の名前は見ない
    odd = tmp_path / "x_BurstDebugInformation_DoNotShip" / "build"
    assert not package_zip.is_excluded(odd / "UnityPlayer.dll", odd)


def test_build_zip(tmp_path, capsys):
    build = _fake_windows_build(tmp_path / "Windows")
    readme = tmp_path / "HOW_TO_PLAY.md"
    readme.write_bytes("# 遊び方\n".encode("utf-8"))
    out = tmp_path / "dist" / "game.zip"
    assert package_zip.build_zip(build, out, readme) == 0
    with zipfile.ZipFile(out) as zf:
        names = zf.namelist()
        assert names[0] == "KatsushikaCampusDays/README.md"
        # 並びは Path の比較順（Windows は大文字小文字を区別しない）なので、ここでは中身だけ見る
        assert sorted(names[1:]) == [
            "KatsushikaCampusDays/KatsushikaCampusDays.exe",
            "KatsushikaCampusDays/KatsushikaCampusDays_Data/Managed/KCD.Runtime.dll",
            "KatsushikaCampusDays/KatsushikaCampusDays_Data/level0",
            "KatsushikaCampusDays/UnityPlayer.dll",
        ]
        assert zf.read("KatsushikaCampusDays/README.md").decode("utf-8") == "# 遊び方\n"
        assert all(i.compress_type == zipfile.ZIP_DEFLATED for i in zf.infolist())
    assert "4 files" in capsys.readouterr().out


def test_build_zip_without_exe(tmp_path):
    (tmp_path / "Windows").mkdir()
    assert package_zip.build_zip(tmp_path / "Windows", tmp_path / "out.zip", tmp_path / "none.md") == 1
    assert not (tmp_path / "out.zip").exists()


# ---- deploy_pages ----

def test_make_zip_stores_webgl_build(tmp_path, capsys):
    build = tmp_path / "WebGL"
    (build / "Build").mkdir(parents=True)
    (build / "index.html").write_text("<html></html>", encoding="utf-8")
    (build / "Build" / "WebGL.wasm.gz").write_bytes(b"\x1f\x8b" + b"0" * 64)
    out = tmp_path / "dist" / "webgl.zip"
    assert deploy_pages.make_zip(build, out) == 0
    with zipfile.ZipFile(out) as zf:
        assert zf.namelist() == ["Build/WebGL.wasm.gz", "index.html"]
        assert all(i.compress_type == zipfile.ZIP_STORED for i in zf.infolist())
    assert "2 files" in capsys.readouterr().out


def test_make_zip_without_index(tmp_path):
    (tmp_path / "WebGL").mkdir()
    assert deploy_pages.make_zip(tmp_path / "WebGL", tmp_path / "webgl.zip") == 1
    assert not (tmp_path / "webgl.zip").exists()
