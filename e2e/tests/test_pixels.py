import io
import random

from PIL import Image, ImageDraw

from kcd_e2e import pixels


def png_of(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, "PNG")
    return buffer.getvalue()


def solid(color, size=(640, 360)) -> Image.Image:
    return Image.new("RGB", size, color)


def scene(size=(640, 360), seed=1) -> Image.Image:
    """色の多い、描かれた画面の代わり。"""
    rng = random.Random(seed)
    data = bytes(rng.randrange(256) for _ in range(size[0] * size[1] * 3))
    return Image.frombytes("RGB", size, data)


def test_single_color_frames_are_not_drawn():
    for color in [(0, 0, 0), (35, 80, 160), (255, 255, 255)]:
        stats = pixels.frame_stats(png_of(solid(color)))
        assert not stats.drawn, color
        assert stats.dominant_fraction == 1.0


def test_loading_bar_on_black_is_not_drawn():
    image = solid((0, 0, 0))
    ImageDraw.Draw(image).rectangle((200, 170, 440, 190), fill=(255, 255, 255))
    assert not pixels.frame_stats(png_of(image)).drawn


def test_busy_frame_is_drawn():
    stats = pixels.frame_stats(png_of(scene()))
    assert stats.drawn
    assert stats.width == 640 and stats.height == 360
    assert stats.as_dict()["drawn"] is True


def test_changed_fraction_same_and_opposite():
    black = png_of(solid((0, 0, 0)))
    white = png_of(solid((255, 255, 255)))
    assert pixels.changed_fraction(black, black) == 0.0
    assert pixels.changed_fraction(black, white) == 1.0


def test_changed_fraction_ignores_small_differences():
    a = png_of(solid((100, 100, 100)))
    b = png_of(solid((100 + pixels.PIXEL_DIFF_LEVEL, 100, 100)))
    assert pixels.changed_fraction(a, b) == 0.0


def test_changed_fraction_of_different_sizes_is_full_change():
    assert pixels.changed_fraction(png_of(solid((0, 0, 0))),
                                   png_of(solid((0, 0, 0), (320, 180)))) == 1.0


def blinking(on: bool) -> Image.Image:
    image = scene(seed=7)
    if on:
        ImageDraw.Draw(image).rectangle((240, 300, 400, 330), fill=(255, 255, 255))
    else:
        ImageDraw.Draw(image).rectangle((240, 300, 400, 330), fill=(0, 0, 0))
    return image


def test_noise_mask_excludes_blinking_pixels():
    frames = [png_of(blinking(True)), png_of(blinking(False)), png_of(blinking(True))]
    mask = pixels.noise_mask(frames)
    assert mask is not None
    # 点滅だけの差は、マスクなしでは変化、マスクありでは 0。
    assert pixels.changed_fraction(frames[0], frames[1]) > 0.01
    assert pixels.changed_fraction(frames[0], frames[1], ignore=mask) == 0.0


def test_noise_mask_keeps_changes_elsewhere():
    frames = [png_of(blinking(True)), png_of(blinking(False))]
    mask = pixels.noise_mask(frames)
    changed = blinking(True)
    ImageDraw.Draw(changed).rectangle((0, 0, 320, 120), fill=(20, 200, 20))
    fraction = pixels.changed_fraction(frames[0], png_of(changed), ignore=mask)
    assert 0.1 < fraction < 0.2


def test_noise_mask_needs_two_frames():
    assert pixels.noise_mask([png_of(scene())]) is None
    assert pixels.noise_mask([]) is None


def test_save_jpeg_shrinks_to_max_side(tmp_path):
    path = tmp_path / "big.jpg"
    pixels.save_jpeg(png_of(scene((2400, 1200))), path, max_side=1600)
    with Image.open(path) as saved:
        assert saved.format == "JPEG"
        assert saved.size == (1600, 800)


def test_save_jpeg_does_not_enlarge(tmp_path):
    path = tmp_path / "small.jpg"
    pixels.save_jpeg(png_of(solid((10, 20, 30), (400, 300))), path)
    with Image.open(path) as saved:
        assert saved.size == (400, 300)
