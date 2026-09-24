import pytest

from kcd_e2e.browser import default_browser, is_software_renderer, launch_config

RTX = "ANGLE (NVIDIA, NVIDIA GeForce RTX 5070 Ti (0x00002C05) Direct3D11 vs_5_0 ps_5_0, D3D11)"
SWIFTSHADER = ("ANGLE (Google, Vulkan 1.3.0 (SwiftShader Device (Subzero) (0x0000C0DE)), "
               "SwiftShader driver)")


def test_default_browser_per_platform():
    assert default_browser("win32") == "msedge"
    assert default_browser("linux") == "chromium"
    assert default_browser("darwin") == "chromium"


def test_gpu_mode_on_windows_uses_d3d11():
    config = launch_config("msedge", headless=True, gl="gpu", platform="win32")
    assert "--use-angle=d3d11" in config.args
    assert "--enable-gpu" in config.args
    assert "--enable-precise-memory-info" in config.args
    assert config.launch_kwargs() == {"headless": True, "args": list(config.args),
                                      "channel": "msedge"}


def test_gpu_mode_elsewhere_does_not_force_an_angle_backend():
    config = launch_config("chromium", headless=True, gl="gpu", platform="linux")
    assert not any(arg.startswith("--use-angle") for arg in config.args)
    assert "channel" not in config.launch_kwargs()


def test_software_mode_uses_swiftshader():
    config = launch_config("chromium", headless=True, gl="software", platform="linux")
    assert "--use-angle=swiftshader" in config.args
    assert "--enable-unsafe-swiftshader" in config.args
    assert "--enable-gpu" not in config.args


def test_any_mode_adds_only_memory_info():
    assert launch_config("chrome", headless=False, gl="any").args == (
        "--enable-precise-memory-info",)


@pytest.mark.parametrize("browser, gl", [("firefox", "gpu"), ("msedge", "vulkan")])
def test_rejects_unknown_values(browser, gl):
    with pytest.raises(ValueError):
        launch_config(browser, headless=True, gl=gl)


@pytest.mark.parametrize("renderer, software", [
    (RTX, False),
    (SWIFTSHADER, True),
    ("llvmpipe (LLVM 15.0.7, 256 bits)", True),
    ("ANGLE (Microsoft, Microsoft Basic Render Driver Direct3D11)", True),
    (None, False),
    ("", False),
])
def test_is_software_renderer(renderer, software):
    assert is_software_renderer(renderer) is software
