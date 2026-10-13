"""postprocess_check の判定の単体テスト（ブラウザは使わない）。

PRODUCTION は 2026-09-24 に本番のタイトルを Edge headless（RTX 5070 Ti / D3D11）で 3 秒測った値。
"""

from __future__ import annotations

import copy
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import postprocess_check as check  # noqa: E402

PRODUCTION = {
    "contexts": 1,
    "ticks": 181,
    "renderedFrames": 181,
    "draws": {"any": 28417, "uber": 181, "uberBloom": 181, "bloomUpsample": 905, "lutBuilder": 181},
    "uber": [
        {
            "bloomVariant": True,
            "vignetteParams2": [0.5, 0.5, 0.66, 2.25],
            "bloomParams": [0.35, 1.0, 1.0, 1.0],
            "vignetteBlockIndex": -1,
        }
    ],
}

EASU = (
    "Shader 'Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling' is not supported "
    "or has been stripped from the build (in 'Blit FSR Upscaling'). PostProcessing render passes will not execute."
)
PRODUCTION_CONSOLE = [
    {"type": "log", "text": "[KCD] quality=Mobile pipeline=Mobile_RPAsset"},
    {
        "type": "log",
        "text": "Shader 'Hidden/Universal Render Pipeline/GaussianDepthOfField' is not supported or has been "
        "stripped from the build (in 'DepthOfFieldGaussianPostProcessPass'). PostProcessing render passes will not execute.",
    },
    {
        "type": "log",
        "text": "Shader 'Hidden/Universal Render Pipeline/PaniniProjection' is not supported or has been "
        "stripped from the build (in 'PaniniProjectionPostProcessPass'). PostProcessing render passes will not execute.",
    },
    {"type": "warning", "text": EASU},
]


def with_changes(**changes):
    result = copy.deepcopy(PRODUCTION)
    for key, value in changes.items():
        if key in result["draws"]:
            result["draws"][key] = value
        elif key in result["uber"][0]:
            result["uber"][0][key] = value
        else:
            result[key] = value
    return result


def test_production_measurement_passes():
    assert check.evaluate(PRODUCTION, PRODUCTION_CONSOLE) == []


def test_bloom_passes_missing_fails_even_when_uber_runs():
    failures = check.check_draws(with_changes(bloomUpsample=0))
    assert len(failures) == 1
    assert "Bloom Upsample" in failures[0]


def test_uber_without_bloom_variant_fails():
    failures = check.check_draws(with_changes(bloomVariant=False, bloomParams=None, uberBloom=0))
    assert any("Bloom を合成しない変種" in f for f in failures)


def test_zero_bloom_intensity_fails():
    failures = check.check_draws(with_changes(bloomParams=[0.0, 1.0, 1.0, 1.0]))
    assert any("Bloom の強さが 0" in f for f in failures)


def test_zero_vignette_intensity_fails():
    failures = check.check_draws(with_changes(vignetteParams2=[0.5, 0.5, 0.0, 2.25]))
    assert any("Vignette の強さが 0" in f for f in failures)


def test_unreadable_vignette_value_fails():
    failures = check.check_draws(with_changes(vignetteParams2=None, vignetteBlockIndex=0))
    assert any("_Vignette_Params2 の値を読めなかった" in f for f in failures)


def test_missing_uber_and_lut_fail():
    result = with_changes(uber=[])
    result["draws"].update(uber=0, uberBloom=0, lutBuilder=0)
    failures = check.check_draws(result)
    assert any("UberPost" in f for f in failures)
    assert any("LutBuilder" in f for f in failures)


def test_too_few_frames_fails_instead_of_passing_vacuously():
    failures = check.check_draws(with_changes(renderedFrames=0))
    assert failures and "フレーム" in failures[0]


def test_easu_warning_is_allowed_even_if_repeated():
    assert check.check_console([{"type": "warning", "text": EASU}] * 2) == []


def test_other_stripped_shader_warning_fails():
    text = (
        "Shader 'Hidden/Universal Render Pipeline/UberPost' is not supported or has been stripped from the "
        "build (in 'UberPostProcessPass'). PostProcessing render passes will not execute."
    )
    failures = check.check_console([{"type": "warning", "text": text}])
    assert len(failures) == 1 and "UberPost" in failures[0]


def test_missing_shader_reference_error_fails():
    text = (
        "Missing shader (in 'UberPostProcessPass'). PostProcessing render passes will not execute. "
        "Check for missing reference in the Renderer and/or PostProcessData resources."
    )
    assert len(check.check_console([{"type": "error", "text": text}])) == 1


def test_any_console_error_or_page_error_fails():
    messages = [{"type": "error", "text": "something broke"}, {"type": "pageerror", "text": "TypeError: x"}]
    assert len(check.check_console(messages)) == 2


def test_stripped_bloom_logs_at_log_level_and_is_caught_by_draw_count():
    stripped = (
        "Shader 'Hidden/Universal Render Pipeline/Bloom' is not supported or has been stripped from the "
        "build (in 'BloomPostProcessPass'). PostProcessing render passes will not execute."
    )
    messages = PRODUCTION_CONSOLE + [{"type": "log", "text": stripped}]
    assert check.check_console(messages) == []
    result = with_changes(bloomUpsample=0, bloomVariant=False, bloomParams=None, uberBloom=0)
    failures = check.evaluate(result, messages)
    assert any("Bloom Upsample" in f for f in failures)
