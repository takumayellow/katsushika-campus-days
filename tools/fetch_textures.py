"""キャンパスの面のテクスチャを ambientCG (CC0) から取り、KCD の配色に合わせて焼き直す (#78)。

`data/textures/surfaces.json` に書いた素材を 1K で落とし、Color と凹凸のマップを取り出して

1. 継ぎ目を保ったまま 512 px へ縮め、
2. 写真のままの彩度と明暗をトゥーン調に馴染む幅へ落とし、
3. AmbientOcclusion / Displacement を暗くするマスクとして焼き込み、
4. **平均色を MaterialLibrary.cs の CampusColors の宣言 hex に一致させて**

`unity/.../Assets/Textures/surfaces/<マテリアル名>.jpg` に書く。

3 は、ambientCG の Color マップが de-light 済みで、それだけでは平らな面に目地も粒も出ないため。
4 が要。キャンパスのマテリアルは今 URP Lit のベタ 1 色で、その hex は #51 / #55 で何度も直して
やっと落ち着いた値。写真をそのまま貼ると配色がまるごと変わる。平均色を宣言へ戻しておけば、
遠景の色は今までと同じまま、近くで見たときだけ目地やムラが出る。

配色の正は MaterialLibrary.cs 側に置いたまま、ここでは読むだけにする。パレットを 2 か所に
書くと #51 と同じ「直したのにゲームに届かない」が起きる。

使い方:

    python tools/fetch_textures.py              # 取得して焼く
    python tools/fetch_textures.py --check      # 焼かずに、今ある画像を測って報告するだけ
    python tools/fetch_textures.py --only grass # 一部だけ

zip は data/textures/.cache/ に残す（gitignore 済み）。2 回目からは落とし直さない。

1 枚が何 cm 四方か（manifest の tile_cm）は、同じフォルダの tiling.json に全マテリアル分を書く。
CampusSurfaces.cs がそれを読んで、タイリングを 100 / tile_cm にする。

画像の横に Unity の .meta が無ければ、パスから決まる GUID で書く。Unity を開いた
セッションがそれぞれ .meta を作ると GUID がばらばらになり、マテリアルからの参照が
どちらか片方で切れる。取り込み設定は既定（Default / Repeat / sRGB / ミップマップ有り）の
ままでよいので、.meta には GUID しか書かない。残りは Unity が取り込むときに書き足す。
tiling.json の .meta だけは、短い TextScriptImporter の既定まで書く。
"""

from __future__ import annotations

import argparse
import io
import json
import math
import re
import sys
import urllib.parse
import urllib.request
import uuid
import zipfile
from pathlib import Path

from PIL import Image, ImageChops, ImageFilter, ImageOps, ImageStat

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "data" / "textures" / "surfaces.json"
CACHE = ROOT / "data" / "textures" / ".cache"
UNITY_PROJECT = ROOT / "unity" / "KatsushikaCampusDays"
MATERIAL_LIBRARY = UNITY_PROJECT / "Assets" / "Scripts" / "Editor" / "MaterialLibrary.cs"

# .meta の GUID はこの文字列と Unity プロジェクトからの相対パスで決める（uuid5）。
# 同じパスなら誰が何度作っても同じ GUID になる。
META_GUID_SEED = "katsushika-campus-days/unity/"

# 縮める前に外周へ回り込ませる画素数（1024 px の元画像での値）。Lanczos は端で
# 折り返してくれないので、上下左右に元画像を巻き付けてから縮め、中央を切り出して
# タイル性を保つ。
WRAP = 64

# 焼いた画像の輝度の標準偏差がこれを下回ったら「模様が見えない」とみなす。
# 焼き込み前の asphalt / concrete_* が 1.6〜2.7 で、並べても無地と区別できなかった。
MIN_SPREAD = 4.0

# seam_ratio がこれを超えたら継ぎ目に段差があるとみなす。焼いた 10 枚は 0.51〜1.03、
# わざと継ぎ目を入れた芝・土・アスファルトは 1.19 以上だったので、その間に置く。
MAX_SEAM = 1.1

# 凹凸として焼き込むマップ。Color と一緒に zip から取り出す。
DETAIL_MAPS = ("AmbientOcclusion", "Displacement")

# 取りに行く先と、受け取る大きさの上限。1K-JPG の zip は 1 本 4〜11 MB、中の jpg は大きくて 2.3 MB。
SOURCE_HOST = "ambientcg.com"
MAX_BYTES = 64 * 2**20
ASSET_NAME = re.compile(r"[A-Za-z]+\d+")
VARIANT_NAME = re.compile(r"\d+K-(JPG|PNG)")
# マテリアル名は書き出すファイル名になる。CampusColors の名前と同じ書き方だけを通す
MATERIAL_NAME = re.compile(r"[a-z0-9_]+")


# ---------------------------------------------------------------------------
# sRGB と線形の行き来。平均色は線形で合わせないと、暗い面ほど狙いから外れる。
# ---------------------------------------------------------------------------


def to_linear(value: float) -> float:
    """sRGB の 0..1 を線形の 0..1 へ。"""
    return value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4


def to_srgb(value: float) -> float:
    """線形の 0..1 を sRGB の 0..1 へ。"""
    value = min(max(value, 0.0), 1.0)
    return value * 12.92 if value <= 0.0031308 else 1.055 * value ** (1 / 2.4) - 0.055


LINEAR_OF_BYTE = [to_linear(i / 255) for i in range(256)]


def linear_band_mean(counts: list[int]) -> float:
    """1 チャンネル分のヒストグラム（256 段）から、線形の平均を返す。"""
    return sum(count * value for count, value in zip(counts, LINEAR_OF_BYTE)) / sum(counts)


def linear_mean(image: Image.Image) -> tuple[float, float, float]:
    """画像の平均色を線形で返す。

    ImageStat は sRGB のまま平均してしまう。ヒストグラムの各段に線形の値を掛けて足せば、
    線形の値を 8 bit に丸めずに測れる（丸めると暗い色ほど狂う）。
    """
    histogram = image.convert("RGB").histogram()
    return tuple(linear_band_mean(histogram[band * 256 : band * 256 + 256]) for band in range(3))


def parse_hex(hex_text: str) -> tuple[float, float, float]:
    """"3C3C3C" を線形の RGB へ。"""
    return tuple(LINEAR_OF_BYTE[int(hex_text[i : i + 2], 16)] for i in (0, 2, 4))


# ---------------------------------------------------------------------------
# 配色は MaterialLibrary.cs から読む（ここには書かない）
# ---------------------------------------------------------------------------

CAMPUS_COLOR_LINE = re.compile(r'\{\s*"([a-z0-9_]+)"\s*,\s*"([0-9A-Fa-f]{6})"\s*\}')


def load_campus_colors() -> dict[str, str]:
    """MaterialLibrary.cs の CampusColors を {名前: hex} で読む。"""
    text = MATERIAL_LIBRARY.read_text(encoding="utf-8")
    start = text.find("CampusColors")
    if start < 0:
        raise SystemExit(f"CampusColors が見つからない: {MATERIAL_LIBRARY}")

    end = text.find("};", start)
    colors = {name: hex_text.upper() for name, hex_text in CAMPUS_COLOR_LINE.findall(text[start:end])}
    if not colors:
        raise SystemExit("CampusColors を読めなかった。MaterialLibrary.cs の書き方が変わっている")

    return colors


# ---------------------------------------------------------------------------
# 取得
# ---------------------------------------------------------------------------


def fetch(url: str, what: str) -> bytes:
    """ambientCG から 1 本取る。

    zip の場所は API の返事に書いてあるものをそのまま使うので、https の ambientcg.com で
    なければ取りに行かない（urllib は file:// も開けてしまう）。
    """
    parsed = urllib.parse.urlparse(url)
    host = parsed.hostname or ""
    if parsed.scheme != "https" or not (host == SOURCE_HOST or host.endswith("." + SOURCE_HOST)):
        raise SystemExit(f"{what} の URL が ambientCG ではない: {url}")

    request = urllib.request.Request(url, headers={"User-Agent": "katsushika-campus-days/fetch_textures"})
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            body = response.read(MAX_BYTES + 1)
    except OSError as error:
        raise SystemExit(f"{what} を取れなかった: {url}\n  {error}") from error

    if len(body) > MAX_BYTES:
        raise SystemExit(f"{what} が {MAX_BYTES // 2**20} MB を超えている: {url}")
    return body


def download_maps(asset: str, variant: str, wanted: list[str]) -> dict[str, Image.Image]:
    """ambientCG の zip を（無ければ）落として、欲しいマップだけを {名前: 画像} で返す。

    無いマップは黙って落とす。素材によっては AmbientOcclusion が入っていない
    （Concrete034 など）。
    """
    # 素材名はキャッシュのファイル名と API の問い合わせにそのまま入る
    if not ASSET_NAME.fullmatch(asset) or not VARIANT_NAME.fullmatch(variant):
        raise SystemExit(f"素材名か解像度の書き方が ambientCG のものと違う: {asset} / {variant}")

    CACHE.mkdir(parents=True, exist_ok=True)
    archive = CACHE / f"{asset}_{variant}.zip"

    if not archive.exists():
        api = (
            "https://ambientcg.com/api/v2/full_json"
            f"?type=Material&id={asset}&include=downloadData"
        )
        found = json.loads(fetch(api, f"{asset} の一覧"))["foundAssets"]
        if not found:
            raise SystemExit(f"ambientCG に {asset} が無い")

        link = None
        categories = found[0]["downloadFolders"]["default"]["downloadFiletypeCategories"]
        for category in categories.values():
            for entry in category["downloads"]:
                if entry["attribute"] == variant:
                    link = entry["downloadLink"]

        if link is None:
            raise SystemExit(f"{asset} に {variant} が無い")

        print(f"  落とす {asset}_{variant}.zip")
        archive.write_bytes(fetch(link, f"{asset} の {variant}"))

    maps: dict[str, Image.Image] = {}
    with zipfile.ZipFile(archive) as zipped:
        for info in zipped.infolist():
            name = info.filename
            for key in wanted:
                if name.lower().endswith(f"_{key.lower()}.jpg"):
                    if info.file_size > MAX_BYTES:
                        raise SystemExit(f"{archive.name} の {name} が {MAX_BYTES // 2**20} MB を超えている")
                    with zipped.open(info) as handle:
                        maps[key] = Image.open(io.BytesIO(handle.read()))
                        maps[key].load()

        if "Color" not in maps:
            raise SystemExit(f"{archive.name} に Color が無い: {zipped.namelist()}")

    return maps


# ---------------------------------------------------------------------------
# 加工
# ---------------------------------------------------------------------------


def square_tile(image: Image.Image) -> Image.Image:
    """長方形のタイルを短い辺の向きに並べて正方形にする。

    Concrete034 は 110 × 55 cm で 1024 × 512 px。正方形へ引き伸ばすと模様が縦に 2 倍に
    伸び、tile_cm の 110 cm とも合わなくなる。縦に 2 枚並べれば 110 × 110 cm の正方形に
    なり、継ぎ目も元のまま保たれる。
    """
    long_side, short_side = max(image.size), min(image.size)
    if long_side % short_side:
        raise ValueError(f"辺の比が整数でない画像は正方形に並べられない: {image.size}")

    square = Image.new(image.mode, (long_side, long_side))
    for i in range(long_side // short_side):
        square.paste(image, (0, i * short_side) if image.width > image.height else (i * short_side, 0))

    return square


def wrap_pad(image: Image.Image, margin: int) -> Image.Image:
    """正方形のタイルの外周 margin px に、反対側の端を巻き付ける（margin ≦ 1 辺）。

    リサイズもぼかしも端の外を 0 か端の画素の繰り返しとして扱うので、そのまま掛けると
    継ぎ目に段差ができる。先に巻き付けてから掛け、中央を切り出せばタイル性が保たれる。
    """
    side = image.width
    padded = Image.new(image.mode, (side + margin * 2, side + margin * 2))
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            padded.paste(image, (margin + dx * side, margin + dy * side))

    return padded


def resize_tileable(image: Image.Image, size: int) -> Image.Image:
    """継ぎ目を壊さずに正方形へ縮める。外周に元画像を巻き付けてから縮め、中央を切り出す。

    巻き付けは元の解像度のまま行う。先に別の大きさへ揃えてから巻くと、その揃える
    リサイズが端を折り返さないので、そこで継ぎ目が壊れる。
    """
    if image.width != image.height:
        image = square_tile(image)

    side = image.width
    wrap = max(1, WRAP * side // 1024)
    padded = wrap_pad(image, wrap)

    margin = round(wrap * size / side)
    shrunk = padded.resize((size + margin * 2, size + margin * 2), Image.LANCZOS)
    return shrunk.crop((margin, margin, margin + size, margin + size))


def shape(image: Image.Image, saturation: float, contrast: float) -> Image.Image:
    """写真のままの彩度と明暗の幅を、トゥーン調の面に馴染む幅へ落とす。

    saturation / contrast はどちらも 0..1 の残す割合（1.0 で写真のまま、0.0 で無地）。

    彩度を先に落としてからコントラストを詰める。逆にすると、先に広げた色の差を
    あとから潰すことになって色ノイズだけが残る。

    明暗を詰める基準は mid-gray ではなく**画像自身のチャンネル平均**に置く。
    mid-gray を基準にすると平均が 128 へ寄り、あとの retint が大きなゲインを
    掛けることになって階調が潰れる。自分の平均を基準にすれば平均は動かず、
    retint のゲインは 1.0 付近で済む。
    """
    gray = image.convert("L").convert("RGB")
    muted = Image.blend(gray, image, saturation)

    lut: list[int] = []
    for mean in ImageStat.Stat(muted).mean[:3]:
        lut += [round(min(max(mean + (i - mean) * contrast, 0), 255)) for i in range(256)]

    return muted.point(lut)


def highpass_tileable(gray: Image.Image, radius: float) -> Image.Image:
    """タイルより一回り小さい大きなムラを抜いて、細かい粒だけを残す。

    広い面に同じタイルを並べたとき、繰り返しが格子として見えてしまう原因は
    タイルの中の**低い周波数**（1/4 タイルくらいの大きなムラ）のほう。細かい粒は
    並べても均されて見えない。元画像からぼかしたものを引けば低い周波数だけが抜ける。

    ぼかしも端で巻き付けてから掛ける。ゼロで埋めてぼかすと外周が暗く引っ張られ、
    継ぎ目に段差ができる（seam_ratio が見張っている事故）。
    """
    side = gray.width
    blurred = wrap_pad(gray, side).filter(ImageFilter.GaussianBlur(radius)).crop((side, side, side * 2, side * 2))
    return ImageOps.autocontrast(ImageChops.subtract(gray, blurred, scale=1, offset=128), cutoff=1)


def bake_detail(
    color: Image.Image, maps: dict[str, Image.Image], size: int, depth: float, highpass: float = 0
) -> Image.Image:
    """凹凸マップをアルベドに焼き込んで、目地とムラを見えるようにする。

    ambientCG の Color マップは de-light 済みで、平らな面に貼ると何も見えない
    （Asphalt033 は Color の標準偏差 4.6、Concrete034 は 5.5）。目地や粒は
    AmbientOcclusion と Displacement 側に入っている（同 31.4 / 28.3）。
    KCD は URP Lit のベタ 1 色とトゥーンシェーダで陰影を作る作りで、法線マップも
    AO マップも配線していない。だから凹凸は**アルベドに焼き込む**しかない。

    AO は「くぼみに溜まる影」、Displacement は「高さ」で、どちらも低いところが
    暗い。両方あれば平均して使う。Concrete034 のように AO を同梱していない素材も
    あるので、無いものは黙って飛ばす。

    返すのは「暗くするだけ」のマスクを掛けた画像。255 = そのまま、低いほど暗い。
    ImageChops.multiply が a*b/255 = 暗くする方向専用なので素直に使える。
    暗くした分だけ平均が下がるが、あとの retint が宣言 hex まで引き戻すので
    ここで明るさを補正する必要はない。
    """
    layers = [
        # autocontrast で 0..255 へ伸ばす。素材ごとに凹凸マップの使っている幅が
        # まちまちで（Concrete034 の Displacement は目一杯、Asphalt033 は狭い）、
        # 生のまま使うと depth の意味が素材ごとに変わってしまう。
        # cutoff=1 は上下 1% の外れ値を無視するため。
        ImageOps.autocontrast(resize_tileable(maps[name].convert("L"), size), cutoff=1)
        for name in DETAIL_MAPS
        if name in maps
    ]
    if not layers or depth <= 0:
        return color

    detail = layers[0]
    for extra in layers[1:]:
        detail = ImageChops.blend(detail, extra, 0.5)

    if highpass > 0:
        detail = highpass_tileable(detail, highpass)

    # depth = 0 なら全面 255（無変化）、1 なら detail そのまま
    mask = detail.point([round(255 * (1 - depth * (1 - v / 255))) for v in range(256)])
    return ImageChops.multiply(color, mask.convert("RGB"))


def gain_lut(gain: float) -> list[int]:
    """sRGB の各段に線形のゲインを掛けた先の段。白飛びは 255 で止まる。"""
    return [round(to_srgb(LINEAR_OF_BYTE[i] * gain) * 255) for i in range(256)]


def retint(image: Image.Image, target_hex: str, steps: int = 20) -> Image.Image:
    """線形のゲインをチャンネルごとに掛けて、平均色を target_hex に合わせる。

    ゲインを上げれば平均は単調に明るくなるので、ゲインは二分法で探す。白飛びする
    画素が多くても、届く範囲の狙いなら必ず寄る。探すのはヒストグラムの上だけで行い、
    画像には見つけたゲインを 1 回だけ掛ける（8 bit へ丸めるのも 1 回で済む）。
    """
    histogram = image.histogram()
    lut: list[int] = []
    for band, target in enumerate(parse_hex(target_hex)):
        counts = histogram[band * 256 : band * 256 + 256]
        low, high = 1 / 64, 64.0
        for _ in range(steps):
            middle = math.sqrt(low * high)
            shifted = [0] * 256
            for step, count in zip(gain_lut(middle), counts):
                shifted[step] += count
            low, high = (middle, high) if linear_band_mean(shifted) < target else (low, middle)

        lut += gain_lut(math.sqrt(low * high))

    return image.point(lut)


# ---------------------------------------------------------------------------
# Unity の .meta
# ---------------------------------------------------------------------------


def meta_path(path: Path) -> Path:
    return path.with_name(path.name + ".meta")


def meta_guid(path: Path) -> str:
    """Unity プロジェクトからの相対パスで決まる 32 桁の GUID。"""
    relative = path.resolve().relative_to(UNITY_PROJECT.resolve()).as_posix()
    return uuid.uuid5(uuid.NAMESPACE_URL, META_GUID_SEED + relative).hex


def ensure_meta(path: Path, folder: bool = False, importer: str | None = None) -> bool:
    """.meta が無ければ書く。書いたら True。

    すでにある .meta は触らない。Unity が取り込むと取り込み設定を書き足すので、
    そちらを上書きすると設定が消える。

    importer を渡すと、その取り込み設定の既定の 4 行まで書く。TextScriptImporter のように
    短くて Unity の版で変わらないものは、ここで書き切っておけば Unity を開いても .meta が
    書き換わらない。画像の TextureImporter は長く版で変わるので、GUID だけにしておく。
    """
    meta = meta_path(path)
    if meta.exists():
        return False

    lines = ["fileFormatVersion: 2", f"guid: {meta_guid(path)}"]
    if folder:
        # Assets/Audio/Ambient.meta など、Unity がフォルダに書くものと同じ形
        lines.append("folderAsset: yes")
        importer = "DefaultImporter"
    if importer:
        # フォルダの DefaultImporter も、trees.json.meta の TextScriptImporter も同じ 4 行
        lines += [
            f"{importer}:",
            "  externalObjects: {}",
            "  userData: ",
            "  assetBundleName: ",
            "  assetBundleVariant: ",
        ]

    meta.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    return True


# ---------------------------------------------------------------------------
# 検証
# ---------------------------------------------------------------------------


def seam_ratio(image: Image.Image) -> float:
    """タイルの継ぎ目の目立ちやすさ。巻いたときの端どうしの差 ÷ 画像の中の強い境目の差。

    分母は、隣り合う列（行）どうしの差を全部並べた 99 パーセンタイル。敷石は 1 枚に
    目地が縦横 4 本ずつしか無く、目地の縁は全体の 1.6% しかない。95 パーセンタイルだと
    目地が分母から外れ、端がたまたま目地にかかっただけで比が 4 を超える。

    測れるのは**明るさの段差**だけ。1K をわざと中心から外して切り出した継ぎ目入りの
    画像で試すと、芝・土・アスファルトは 1.19〜1.69 で弾けるが、敷石・煉瓦は 0.6〜1.0 に
    なって見分けられない（段差ではなく目地の位置ずれとして出るため）。位置ずれは、
    ambientCG が素材をシームレスとして配っていることと、resize_tileable がずらしも
    切り出しもしないことで保っている。ここで見張るのは、縮小やぼかしで端に段差を
    作ってしまう事故のほう。
    """
    gray = image.convert("L")
    width, height = gray.size
    pixels = gray.load()

    def column_diff(a: int, b: int) -> float:
        return sum(abs(pixels[a, y] - pixels[b, y]) for y in range(height)) / height

    def row_diff(a: int, b: int) -> float:
        return sum(abs(pixels[x, a] - pixels[x, b]) for x in range(width)) / width

    inside = sorted(
        [column_diff(x, x + 1) for x in range(width - 1)] + [row_diff(y, y + 1) for y in range(height - 1)]
    )
    strong = inside[int(len(inside) * 0.99)]
    wrap = max(column_diff(width - 1, 0), row_diff(height - 1, 0))
    return wrap / strong if strong > 1e-6 else float("inf")


def report(name: str, path: Path, target_hex: str) -> bool:
    """書いた画像を測って 1 行で報告する。狙いどおりなら True。"""
    image = Image.open(path).convert("RGB")
    mean = linear_mean(image)
    got = "".join(f"{round(to_srgb(c) * 255):02X}" for c in mean)
    target = parse_hex(target_hex)
    error = max(abs(round(to_srgb(m) * 255) - round(to_srgb(t) * 255)) for m, t in zip(mean, target))
    ratio = seam_ratio(image)
    spread = ImageStat.Stat(image.convert("L")).stddev[0]
    size_kb = path.stat().st_size / 1024

    # spread は模様の濃さ（輝度の標準偏差）。4 を切ると平らな面に貼っても見分けが付かない。
    ok = error <= 2 and ratio <= MAX_SEAM and spread >= MIN_SPREAD
    print(
        f"  {'ok ' if ok else 'NG '}{name:<16} 平均 #{got} (狙い #{target_hex}, 差 {error}/255)"
        f"  模様 {spread:4.1f}  継ぎ目 {ratio:.2f}  {size_kb:.0f} KB"
    )
    return ok


# ---------------------------------------------------------------------------


def bake_surface(surface: dict, variant: str) -> Image.Image:
    """素材 1 つを落として、縮め・色味を落とし・凹凸を焼き込むところまで。色合わせはまだ。

    同じ素材を使うマテリアル（stone_light と stone_dark など）はここまでを共有して、
    retint の狙いだけを変える。
    """
    size = surface["size_px"]
    maps = download_maps(surface["asset"], variant, ["Color", *DETAIL_MAPS])
    color = resize_tileable(maps["Color"].convert("RGB"), size)
    return bake_detail(
        shape(color, surface["saturation"], surface["contrast"]),
        maps,
        size,
        surface["depth"],
        surface.get("highpass_px", 0),
    )


def finish_material(
    material: str, path: Path, target_hex: str | None, shaped: Image.Image | None, quality: int, in_unity: bool
) -> bool:
    """1 枚を色合わせして書き、測って報告する。狙いどおりなら True。

    shaped が None（--check）のときは書かずに、今ある画像と .meta を確かめて測るだけ。
    """
    if target_hex is None:
        print(f"  NG {material:<16} MaterialLibrary.cs の CampusColors に無い")
        return False

    if shaped is not None:
        retint(shaped, target_hex).save(path, quality=quality, subsampling=0)
        if in_unity:
            ensure_meta(path)
    elif not path.exists():
        print(f"  NG {material:<16} まだ焼かれていない: {path.relative_to(ROOT)}")
        return False
    elif in_unity and not meta_path(path).exists():
        print(f"  NG {material:<16} .meta が無い（--check を外して流すと書く）")
        return False

    return report(material, path, target_hex)


# ---------------------------------------------------------------------------
# tiling.json（1 枚が何 cm 四方か）。CampusSurfaces.cs が読んでタイリングを 100 / tile_cm にする
# ---------------------------------------------------------------------------

TILING_NAME = "tiling.json"


def tiling_text(manifest: dict) -> str:
    """manifest の全マテリアルの tile_cm を、CampusSurfaces.cs が読む形の JSON にする。

    --only とは関係なく全部を書く。CampusSurfaces は tiling.json に無いマテリアルから
    このフォルダの画像を外すので、一部だけを書くと残りの面が単色に戻る。
    並びはマテリアル名の順に固定し、manifest の書き順を変えただけで差分が出ないようにする。
    """
    entries = [
        {"material": material, "tile_cm": surface["tile_cm"]}
        for surface in manifest["surfaces"]
        for material in surface["materials"]
    ]
    entries.sort(key=lambda entry: entry["material"])
    return json.dumps({"surfaces": entries}, ensure_ascii=False, indent=2) + "\n"


def finish_tiling(path: Path, text: str, check: bool, in_unity: bool) -> bool:
    """tiling.json を書いて報告する。書けていれば True。

    check（--check）のときは書かずに、今あるものが text と同じかと .meta があるかを確かめる。
    読むときは改行を \\n に揃えて比べる（Windows で CRLF に変えて checkout されても NG にしない）。
    """
    if not check:
        path.write_text(text, encoding="utf-8", newline="\n")
        if in_unity:
            ensure_meta(path, importer="TextScriptImporter")
    elif not path.exists():
        print(f"  NG {path.name:<16} まだ書かれていない: {path.relative_to(ROOT)}")
        return False
    elif path.read_text(encoding="utf-8") != text:
        print(f"  NG {path.name:<16} manifest の tile_cm と違う（--check を外して流すと書き直す）")
        return False
    elif in_unity and not meta_path(path).exists():
        print(f"  NG {path.name:<16} .meta が無い（--check を外して流すと書く）")
        return False

    print(f"  ok {path.name:<16} {len(json.loads(text)['surfaces'])} マテリアルの tile_cm")
    return True


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="焼かずに、今ある画像を測って報告する")
    parser.add_argument("--only", action="append", default=[], help="このマテリアル名だけ（何度でも指定できる）")
    args = parser.parse_args(argv)

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    colors = load_campus_colors()
    output = ROOT / manifest["output"]["folder"]
    suffix = manifest["output"]["format"]
    quality = manifest["output"]["quality"]

    # 書き先もファイル名も manifest の文字列から作るので、リポジトリの外や別の名前へは書かない
    if not output.resolve().is_relative_to(ROOT.resolve()):
        raise SystemExit(f"output.folder がリポジトリの外: {manifest['output']['folder']}")
    odd = [m for s in manifest["surfaces"] for m in s["materials"] if not MATERIAL_NAME.fullmatch(m)]
    if odd:
        raise SystemExit(f"マテリアル名に a-z・0-9・_ 以外がある: {odd}")
    if suffix != "jpg":
        raise SystemExit(f"output.format は jpg だけ（CampusSurfaces が <マテリアル名>.jpg を読む）: {suffix}")

    # Unity の Assets の外へ書くよう指定されたら .meta は要らない
    in_unity = output.resolve().is_relative_to((UNITY_PROJECT / "Assets").resolve())
    if not args.check:
        output.mkdir(parents=True, exist_ok=True)
        if in_unity:
            ensure_meta(output, folder=True)

    print(f"{manifest['source']['site']} / {manifest['source']['license']}  →  {output.relative_to(ROOT)}")
    every_ok = True

    for surface in manifest["surfaces"]:
        wanted = [m for m in surface["materials"] if not args.only or m in args.only]
        if not wanted:
            continue

        print(f"{surface['asset']}  ({surface['tile_cm']} cm)")
        shaped = None if args.check else bake_surface(surface, manifest["download"]["variant"])
        for material in wanted:
            path = output / f"{material}.{suffix}"
            every_ok &= finish_material(material, path, colors.get(material), shaped, quality, in_unity)

    print("タイリング")
    every_ok &= finish_tiling(output / TILING_NAME, tiling_text(manifest), args.check, in_unity)

    print("すべて狙いどおり" if every_ok else "狙いから外れたものがある")
    return 0 if every_ok else 1


if __name__ == "__main__":
    sys.exit(main())
