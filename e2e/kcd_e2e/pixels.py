"""キャンバスのスクリーンショット（PNG）から、描かれているか・画面が変わったかを数える。"""

from __future__ import annotations

import io
from dataclasses import dataclass

from PIL import Image, ImageChops, ImageFilter, ImageStat

# 縮小してから数える。1280x720 をそのまま数えるより速く、1 画素の揺らぎにも強い。
SAMPLE_SIZE = (320, 180)

# 「単色でない」の閾値。真っ黒・真っ青の一色塗りや、読み込み帯だけの画面を落とす。
MIN_LUMA_STD = 6.0
MAX_DOMINANT_FRACTION = 0.90
MIN_DISTINCT_COLORS = 32

# 画面が変わったとみなす、1 画素あたりの差（0-255、RGB の最大チャンネル）。
PIXEL_DIFF_LEVEL = 24


@dataclass(frozen=True)
class FrameStats:
    width: int
    height: int
    mean_luma: float
    luma_std: float
    dominant_fraction: float
    distinct_colors: int

    @property
    def drawn(self) -> bool:
        """一色塗りではなく、絵が描かれているか。"""
        return (self.luma_std >= MIN_LUMA_STD
                and self.dominant_fraction <= MAX_DOMINANT_FRACTION
                and self.distinct_colors >= MIN_DISTINCT_COLORS)

    def as_dict(self) -> dict:
        return {
            "width": self.width,
            "height": self.height,
            "mean_luma": round(self.mean_luma, 2),
            "luma_std": round(self.luma_std, 2),
            "dominant_fraction": round(self.dominant_fraction, 4),
            "distinct_colors": self.distinct_colors,
            "drawn": self.drawn,
        }


def _open(png: bytes) -> Image.Image:
    with Image.open(io.BytesIO(png)) as image:
        return image.convert("RGB")


def _sample(image: Image.Image) -> Image.Image:
    return image.resize(SAMPLE_SIZE, Image.Resampling.BILINEAR)


def frame_stats(png: bytes) -> FrameStats:
    image = _open(png)
    small = _sample(image)
    luma = small.convert("L")
    stat = ImageStat.Stat(luma)
    # 下位 3 bit を落として数える。圧縮やディザの 1 段差を別の色と数えない。
    quantized = small.point(lambda v: v & 0xF8)
    colors = quantized.getcolors(maxcolors=SAMPLE_SIZE[0] * SAMPLE_SIZE[1])
    total = SAMPLE_SIZE[0] * SAMPLE_SIZE[1]
    dominant = max(count for count, _ in colors) / total
    return FrameStats(
        width=image.width,
        height=image.height,
        mean_luma=stat.mean[0],
        luma_std=stat.stddev[0],
        dominant_fraction=dominant,
        distinct_colors=len(colors),
    )


def _peak_difference(a: Image.Image, b: Image.Image) -> Image.Image:
    """画素ごとの差（RGB のうち最も大きく変わったチャンネル）を L の画像で返す。"""
    channels = ImageChops.difference(a, b).split()
    return ImageChops.lighter(ImageChops.lighter(channels[0], channels[1]), channels[2])


def _change_mask(a: Image.Image, b: Image.Image, level: int) -> Image.Image:
    """level を超えて変わった画素を 255、それ以外を 0 にした L の画像。"""
    return _peak_difference(a, b).point(lambda v: 255 if v > level else 0)


def noise_mask(frames: list[bytes], level: int = PIXEL_DIFF_LEVEL) -> Image.Image | None:
    """同じ画面を続けて撮った数枚から、勝手に動いている画素（点滅する文字など）を集める。

    縁で取りこぼさないよう 1 画素ずつ太らせる。frames が 2 枚未満なら None。
    """
    sampled = [_sample(_open(png)) for png in frames]
    if len(sampled) < 2:
        return None
    mask = Image.new("L", SAMPLE_SIZE, 0)
    for i, first in enumerate(sampled):
        for second in sampled[i + 1:]:
            mask = ImageChops.lighter(mask, _change_mask(first, second, level))
    return mask.filter(ImageFilter.MaxFilter(3))


def changed_fraction(png_a: bytes, png_b: bytes, level: int = PIXEL_DIFF_LEVEL,
                     ignore: Image.Image | None = None) -> float:
    """2 枚のうち、level を超えて色が変わった画素の割合（0.0-1.0）。

    ignore（noise_mask の結果）で 255 の画素は数えない。元の大きさが違えば 1.0。
    """
    a = _open(png_a)
    b = _open(png_b)
    if a.size != b.size:
        return 1.0
    mask = _change_mask(_sample(a), _sample(b), level)
    if ignore is not None:
        mask = ImageChops.subtract(mask, ignore)
    total = SAMPLE_SIZE[0] * SAMPLE_SIZE[1]
    return mask.histogram()[255] / total


def save_jpeg(png: bytes, path, max_side: int = 1600, quality: int = 82) -> None:
    """Issue に貼る用。長辺 max_side 以下の JPEG にする。"""
    image = _open(png)
    scale = min(1.0, max_side / max(image.size))
    if scale < 1.0:
        image = image.resize((round(image.width * scale), round(image.height * scale)),
                             Image.Resampling.LANCZOS)
    image.save(path, "JPEG", quality=quality, optimize=True)
