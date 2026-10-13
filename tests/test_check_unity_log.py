"""tools/check_unity_log.py が失敗の証拠を拾い、正常系の行を拾わないこと。"""

import io

import check_unity_log


def scan_text(tmp_path, text):
    log = tmp_path / "unity.log"
    log.write_text(text, encoding="utf-8")
    out = io.StringIO()
    return check_unity_log.scan(log, out), out.getvalue()


def test_clean_log_passes(tmp_path):
    code, text = scan_text(tmp_path, "Loading scene\n[KCD] 入口トリガーを 9 個置きました。\n")
    assert code == 0
    assert "NavMesh  : 0" in text
    assert "[KCD] 入口トリガーを 9 個置きました。" in text


def test_navmesh_agent_failure_is_reported(tmp_path):
    # #63: Campus を開くと 4 体の NPC がこの行を出して NavMesh に載れない。
    lines = ["Failed to create agent because there is no valid NavMesh"] * 4
    code, text = scan_text(tmp_path, "\n".join(lines) + "\n")
    assert code == 1
    assert "NavMesh  : 4" in text
    assert text.count("[NAV] Failed to create agent") == 4


def test_inactive_agent_calls_are_reported(tmp_path):
    line = '"SetDestination" can only be called on an active agent that has been placed on a NavMesh.\n'
    code, text = scan_text(tmp_path, line)
    assert code == 1
    assert "[NAV]" in text


def test_compile_errors_and_exceptions_still_fail(tmp_path):
    code, text = scan_text(tmp_path, "Assets/A.cs(1,1): error CS0103: x\nNullReferenceException: y\n")
    assert code == 1
    assert "[CS]" in text and "[EX]" in text


def test_ignored_tokens_do_not_count_as_exceptions(tmp_path):
    code, _ = scan_text(tmp_path, "UnityEngine.StackTraceUtility:ExtractStackTrace () ExceptionHandler\n")
    assert code == 0


def test_missing_log_is_unreadable(tmp_path):
    out = io.StringIO()
    assert check_unity_log.scan(tmp_path / "none.log", out) == 2


def test_test_framework_frames_under_an_expected_warning_are_not_exceptions(tmp_path):
    # EditMode のテストが LogAssert.Expect で待つ警告にも、Test Framework のフレームが付く。
    frame = (
        "UnityEngine.TestRunner.NUnitExtensions.Runner.UnityLogCheckDelegatingCommand:CaptureException "
        "(NUnit.Framework.Internal.TestResult,System.Action) (at ./Library/PackageCache/"
        "com.unity.test-framework@7a3849e09bd0/UnityEngine.TestRunner/NUnitExtensions/Runner/"
        "UnityLogCheckDelegatingCommand.cs:93)\n"
    )
    code, text = scan_text(tmp_path, "TreeChunkCombiner: 1 個はまとめずに 1 つずつ描く（odd）\n" + frame)
    assert code == 0
    assert "Exception: 0" in text


def test_exception_thrown_inside_a_test_still_fails(tmp_path):
    frame = "UnityEngine.TestRunner.NUnitExtensions.Runner.UnityLogCheckDelegatingCommand:CaptureException (x)\n"
    code, text = scan_text(tmp_path, "NullReferenceException: Object reference not set\n" + frame)
    assert code == 1
    assert "Exception: 1" in text


# ---- #64: 警告・シェーダの欠け・編集時の複製・テスト XML ----

# unity/logs/scene.log に 11 行（建物の名札 11 枚）出ていた行。
LEAK_LINE = (
    "Instantiating material due to calling renderer.material during edit mode. "
    "This will leak materials into the scene. You most likely want to use renderer.sharedMaterial instead.\n"
)

# unity/logs/webgl.log に出ていた、パッケージの Terrain 用テンプレートの行（1 回の取り込みで 8 行）。
TERRAIN_TEMPLATE_LINES = [
    "Shader 'Hidden/Terrain/Terrain Standard 4 Layers URP': dependency 'AddPassShader' "
    "shader 'Hidden/Hidden/Terrain/Terrain Standard 4 Layers URP_AddPass' not found",
    "Shader 'Hidden/Terrain/Terrain Standard 4 Layers URP': dependency 'BaseMapGenShader' "
    "shader 'Hidden/Hidden/Terrain/Terrain Standard 4 Layers URP_BaseMapGen' not found",
    "Shader 'Hidden/Terrain/Terrain Simple': dependency 'AddPassShader' "
    "shader 'Hidden/Hidden/Terrain/Terrain Simple_AddPass' not found",
    "Shader 'Hidden/Terrain/Terrain Simple': dependency 'BaseMapGenShader' "
    "shader 'Hidden/Hidden/Terrain/Terrain Simple_BaseMapGen' not found",
] * 2

# unity/logs/import_wt.log（新しい Library の初回取り込み）だけに出ていた行。
FIRST_IMPORT_LINES = [
    "Shader 'Universal Render Pipeline/Terrain/Lit': dependency 'AddPassShader' "
    "shader 'Hidden/Universal Render Pipeline/Terrain/Lit (Add Pass)' not found",
    "Shader 'Universal Render Pipeline/Terrain/Lit': dependency 'BaseMapShader' "
    "shader 'Hidden/Universal Render Pipeline/Terrain/Lit (Base Pass)' not found",
    "Shader 'Universal Render Pipeline/Terrain/Lit': dependency 'BaseMapGenShader' "
    "shader 'Hidden/Universal Render Pipeline/Terrain/Lit (Basemap Gen)' not found",
    "Shader 'Hidden/Terrain/Terrain Simple': dependency 'BaseMapShader' "
    "shader 'Hidden/Universal Render Pipeline/Terrain/Lit (Base Pass)' not found",
    "Shader 'Universal Render Pipeline/Nature/SpeedTree7': dependency 'BillboardShader' "
    "shader 'Universal Render Pipeline/Nature/SpeedTree7 Billboard' not found",
]


def test_compile_warnings_fail(tmp_path):
    # CS0618（非推奨の FindObjectsSortMode）と CS0108（Object.DestroyObject を隠す）が警告のまま残っていた。
    log = (
        "Assets/Scripts/Runtime/UI/Minimap.cs(154,33): warning CS0618: 'FindObjectsSortMode' is obsolete\n"
        "Assets/Scripts/Runtime/World/TreeChunkCombiner.cs(160,29): warning CS0108: "
        "'TreeChunkCombiner.DestroyObject(Object)' hides inherited member 'Object.DestroyObject(Object)'\n"
    )
    code, text = scan_text(tmp_path, log)
    assert code == 1
    assert "warn CS  : 2" in text
    assert text.count("[WARN]") == 2


def test_material_copied_in_edit_mode_fails(tmp_path):
    code, text = scan_text(tmp_path, LEAK_LINE * 11)
    assert code == 1
    assert "Leak     : 11" in text
    assert "[LEAK] Instantiating material due to calling renderer.material" in text


def test_missing_shaders_fail(tmp_path):
    log = (
        "Shader 'KCD/Toon': dependency 'OutlineShader' shader 'KCD/Outline' not found\n"
        "[KCD] シェーダ KCD/Sky が見つかりません。Assets/Materials/Sky.mat を作れませんでした。\n"
    )
    code, text = scan_text(tmp_path, log)
    assert code == 1
    assert "Shader   : 2" in text
    assert text.count("[SHADER]") == 2


def test_unsupported_shader_on_this_gpu_is_not_a_missing_shader(tmp_path):
    # どのバッチログにも 2 行ずつ出る。見つからないのではなく、GPU が対応していないだけ。
    line = "Shader Hidden/ChartRasterizerHardware is not supported: GPU does not support conservative rasterization\n"
    code, text = scan_text(tmp_path, line * 2)
    assert code == 0
    assert "Shader   : 0" in text


def test_known_terrain_template_lines_are_listed_but_do_not_fail(tmp_path):
    code, text = scan_text(tmp_path, "\n".join(TERRAIN_TEMPLATE_LINES) + "\nビルド結果: Succeeded\n")
    assert code == 0, text
    assert "Shader   : 0" in text
    assert "Known    : 8" in text
    assert "[KNOWN] 8 行: " in text


def test_known_first_import_lines_do_not_fail(tmp_path):
    code, text = scan_text(tmp_path, "\n".join(FIRST_IMPORT_LINES) + "\n")
    assert code == 0, text
    assert "Known    : 5" in text


def test_known_lines_do_not_hide_other_missing_shaders(tmp_path):
    # 同じ Hidden/Hidden の形でも、パッケージの Terrain テンプレート以外は失敗のまま。
    log = TERRAIN_TEMPLATE_LINES[0] + "\n" + (
        "Shader 'Hidden/KCD/Water': dependency 'AddPassShader' shader 'Hidden/Hidden/KCD/Water_AddPass' not found\n"
    )
    code, text = scan_text(tmp_path, log)
    assert code == 1
    assert "Shader   : 1" in text
    assert "Known    : 1" in text
    assert "[SHADER] Shader 'Hidden/KCD/Water'" in text


def test_every_known_line_says_why():
    assert check_unity_log.KNOWN_LINES
    for known in check_unity_log.KNOWN_LINES:
        assert len(known.reason) >= 40, known.pattern.pattern
        assert "(#" in known.reason, known.pattern.pattern


# ---- NUnit の結果 XML ----

PASSED_XML = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="2" result="Passed" total="2" passed="2" failed="0" inconclusive="0" skipped="0">
  <test-suite type="TestFixture" name="A" fullname="KCD.Tests.A" result="Passed">
    <test-case name="One" fullname="KCD.Tests.A.One" result="Passed" />
    <test-case name="Two" fullname="KCD.Tests.A.Two" result="Passed" />
  </test-suite>
</test-run>
"""

FAILED_XML = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="2" result="Failed(Child)" total="2" passed="1" failed="1" inconclusive="0" skipped="0">
  <test-suite type="TestFixture" name="Labels" fullname="KCD.Tests.Labels" result="Failed" site="Child">
    <test-case name="Shared" fullname="KCD.Tests.Labels.Shared" result="Failed">
      <failure><message><![CDATA[名札が共有マテリアルでない]]></message></failure>
    </test-case>
    <test-case name="Passing" fullname="KCD.Tests.Labels.Passing" result="Passed" />
  </test-suite>
  <test-suite type="TestFixture" name="Setup" fullname="KCD.Tests.Setup" result="Failed" site="SetUp">
    <failure><message><![CDATA[OneTimeSetUp: シーンが読めない]]></message></failure>
  </test-suite>
</test-run>
"""


def scan_xml(tmp_path, text):
    path = tmp_path / "tests_editmode.xml"
    path.write_text(text, encoding="utf-8")
    out = io.StringIO()
    return check_unity_log.scan(path, out), out.getvalue()


def test_passing_test_results_pass(tmp_path):
    code, text = scan_xml(tmp_path, PASSED_XML)
    assert code == 0, text
    assert "テスト 2 本" in text
    assert "Failed   : 0" in text


def test_failed_tests_and_fixture_setup_fail(tmp_path):
    code, text = scan_xml(tmp_path, FAILED_XML)
    assert code == 1
    assert "Failed   : 2" in text
    assert "[FAIL] KCD.Tests.Labels.Shared" in text
    assert "[FAIL] KCD.Tests.Setup (SetUp)" in text


def test_failed_run_without_a_failed_case_still_fails(tmp_path):
    xml = PASSED_XML.replace('result="Passed" total="2"', 'result="Failed" total="2"')
    code, text = scan_xml(tmp_path, xml)
    assert code == 1
    assert "失敗したテストが見つかりません" in text


def test_results_without_any_test_fail(tmp_path):
    xml = '<?xml version="1.0" encoding="utf-8"?>\n<test-run id="2" result="Passed" total="0" />\n'
    code, text = scan_xml(tmp_path, xml)
    assert code == 1
    assert "テストが 1 本も走っていません" in text


def test_broken_results_are_unreadable(tmp_path):
    code, text = scan_xml(tmp_path, "<test-run")
    assert code == 2
    assert "結果 XML を読めません" in text
