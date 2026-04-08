from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUTPUT_ROOT = ROOT / "Assets" / "Art" / "Placeholders" / "effects"
ARTIFACTS_ROOT = ROOT / "Artifacts"

LOGICAL_SIZE = (64, 64)
SCALE = 4
CANVAS_SIZE = (LOGICAL_SIZE[0] * SCALE, LOGICAL_SIZE[1] * SCALE)
TRANSPARENT = (0, 0, 0, 0)

WHITE = (255, 252, 240, 255)
SOFT_WHITE = (255, 250, 214, 215)
GOLD = (255, 219, 118, 240)
PALE_GOLD = (255, 239, 170, 176)
GREY = (231, 236, 242, 230)
GREY_DARK = (180, 186, 194, 180)
WOOD = (160, 108, 61, 255)
WOOD_DARK = (100, 62, 35, 255)
IRON = (213, 220, 228, 255)
IRON_DARK = (132, 142, 153, 255)
FEATHER = (214, 233, 242, 225)
GREEN = (102, 240, 113, 236)
GREEN_DARK = (43, 165, 72, 216)
LIME = (203, 255, 114, 210)
SMOKE_GREEN = (113, 193, 110, 110)


def new_canvas() -> Image.Image:
    return Image.new("RGBA", LOGICAL_SIZE, TRANSPARENT)


def scale_up(image: Image.Image) -> Image.Image:
    return image.resize(CANVAS_SIZE, Image.Resampling.NEAREST)


def save(image: Image.Image, filename: str) -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    scale_up(image).save(OUTPUT_ROOT / filename)


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def spark_points(cx: int, cy: int, long_r: int, short_r: int) -> list[tuple[int, int]]:
    return [
        (cx, cy - long_r),
        (cx + short_r, cy - short_r),
        (cx + long_r, cy),
        (cx + short_r, cy + short_r),
        (cx, cy + long_r),
        (cx - short_r, cy + short_r),
        (cx - long_r, cy),
        (cx - short_r, cy - short_r),
    ]


def draw_spark(
    draw: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    long_r: int,
    short_r: int,
    outer,
    inner,
) -> None:
    draw.polygon(spark_points(cx, cy, long_r, short_r), fill=outer)
    inner_long = max(1, long_r - 2)
    inner_short = max(1, short_r - 1)
    draw.polygon(spark_points(cx, cy, inner_long, inner_short), fill=inner)
    draw.point((cx, cy), WHITE)


def slash_arc() -> Image.Image:
    image = new_canvas()
    draw = ImageDraw.Draw(image)
    points: list[tuple[int, int]] = []
    for step in range(16):
        t = step / 15
        x = round(lerp(14, 49, t))
        y = round(lerp(44, 14, t) - 9 * (1 - (2 * t - 1) ** 2))
        points.append((x, y))

    for width, color in ((6, PALE_GOLD), (4, GOLD), (2, SOFT_WHITE), (1, WHITE)):
        draw.line(points, fill=color, width=width, joint="curve")

    # Tip flare and trailing shards keep the swing readable in motion.
    draw.polygon(((48, 14), (55, 12), (50, 18)), fill=WHITE)
    draw.line((18, 42, 12, 45), fill=PALE_GOLD, width=2)
    draw.line((23, 36, 17, 38), fill=GOLD, width=2)
    draw.line((29, 29, 24, 30), fill=SOFT_WHITE, width=1)
    return image


def hit_blunt() -> Image.Image:
    image = new_canvas()
    draw = ImageDraw.Draw(image)
    center = (32, 32)
    outer = [
        (32, 13),
        (37, 24),
        (50, 17),
        (42, 30),
        (54, 32),
        (42, 35),
        (49, 48),
        (36, 40),
        (32, 52),
        (28, 40),
        (15, 48),
        (22, 35),
        (10, 32),
        (22, 29),
        (14, 18),
        (27, 24),
    ]
    middle = [(round(lerp(center[0], x, 0.72)), round(lerp(center[1], y, 0.72))) for x, y in outer]
    inner = [(round(lerp(center[0], x, 0.48)), round(lerp(center[1], y, 0.48))) for x, y in outer]

    draw.polygon(outer, fill=GREY_DARK)
    draw.polygon(middle, fill=GREY)
    draw.polygon(inner, fill=WHITE)
    draw.ellipse((24, 24, 40, 40), fill=(255, 255, 255, 180))

    debris = [
        ((11, 22, 14, 25), GREY_DARK),
        ((17, 47, 20, 50), GREY),
        ((46, 17, 49, 20), GREY),
        ((50, 41, 53, 44), GREY_DARK),
        ((8, 33, 10, 35), WHITE),
        ((55, 29, 57, 31), WHITE),
    ]
    for box, color in debris:
        draw.rectangle(box, fill=color)
    return image


def arrow_projectile() -> Image.Image:
    image = new_canvas()
    draw = ImageDraw.Draw(image)
    y = 31

    for trail_y, color in ((y - 2, (255, 255, 255, 45)), (y, (255, 255, 255, 70)), (y + 2, (232, 238, 247, 45))):
        draw.line((9, trail_y, 27, trail_y), fill=color, width=2)

    draw.line((18, y, 46, y), fill=WOOD_DARK, width=4)
    draw.line((17, y, 45, y), fill=WOOD, width=2)
    draw.polygon(((46, 31), (54, 27), (54, 35)), fill=IRON_DARK)
    draw.polygon(((47, 31), (53, 28), (53, 34)), fill=IRON)
    draw.polygon(((18, 31), (12, 26), (15, 31), (12, 36)), fill=(176, 78, 57, 220))
    draw.polygon(((20, 31), (15, 27), (17, 31), (15, 35)), fill=FEATHER)
    draw.line((29, 29, 44, 29), fill=(255, 255, 255, 90), width=1)
    return image


def poison_spit() -> Image.Image:
    image = new_canvas()
    draw = ImageDraw.Draw(image)

    for box, color in (
        ((10, 24, 20, 34), SMOKE_GREEN),
        ((16, 20, 28, 32), (106, 206, 100, 125)),
        ((22, 23, 34, 35), (126, 220, 96, 145)),
    ):
        draw.ellipse(box, fill=color)

    draw.ellipse((28, 24, 43, 39), fill=GREEN_DARK)
    draw.ellipse((30, 26, 41, 37), fill=GREEN)
    draw.ellipse((33, 28, 38, 33), fill=LIME)
    draw.polygon(((41, 30), (50, 27), (49, 36)), fill=GREEN_DARK)
    draw.polygon(((42, 30), (48, 28), (47, 35)), fill=GREEN)

    droplets = [
        ((36, 37, 40, 44), GREEN_DARK, GREEN),
        ((44, 35, 48, 42), GREEN_DARK, LIME),
        ((28, 38, 31, 44), GREEN_DARK, GREEN),
    ]
    for box, outer, inner in droplets:
        draw.ellipse(box, fill=outer)
        inset = (box[0] + 1, box[1] + 1, box[2] - 1, box[3] - 1)
        draw.ellipse(inset, fill=inner)
    return image


def buff_flash() -> Image.Image:
    image = new_canvas()
    draw = ImageDraw.Draw(image)
    center_x = 32

    draw.polygon(((28, 52), (36, 52), (42, 22), (32, 10), (22, 22)), fill=(255, 219, 118, 120))
    draw.polygon(((29, 52), (35, 52), (38, 23), (32, 13), (26, 23)), fill=GOLD)
    draw.polygon(((30, 52), (34, 52), (35, 24), (32, 15), (29, 24)), fill=WHITE)

    draw.line((center_x, 18, center_x, 9), fill=WHITE, width=2)
    draw.line((center_x - 7, 28, center_x - 12, 20), fill=PALE_GOLD, width=2)
    draw.line((center_x + 7, 28, center_x + 12, 20), fill=PALE_GOLD, width=2)
    draw.line((center_x - 10, 40, center_x - 16, 34), fill=GOLD, width=2)
    draw.line((center_x + 10, 40, center_x + 16, 34), fill=GOLD, width=2)

    for args in (
        (21, 18, 4, 2),
        (43, 16, 4, 2),
        (19, 33, 3, 1),
        (45, 31, 3, 1),
        (32, 8, 4, 2),
    ):
        draw_spark(draw, *args, outer=PALE_GOLD, inner=WHITE)
    return image


def pickup_glint() -> Image.Image:
    image = new_canvas()
    draw = ImageDraw.Draw(image)
    draw_spark(draw, 32, 32, 11, 4, outer=GOLD, inner=WHITE)
    draw_spark(draw, 43, 22, 4, 1, outer=PALE_GOLD, inner=WHITE)
    draw_spark(draw, 22, 42, 3, 1, outer=(255, 236, 166, 160), inner=SOFT_WHITE)
    draw.polygon(((32, 26), (38, 32), (32, 38), (26, 32)), fill=(255, 245, 185, 255))
    draw.polygon(((32, 28), (36, 32), (32, 36), (28, 32)), fill=WHITE)
    return image


def preview_sheet(images: list[tuple[str, Image.Image]]) -> Image.Image:
    cols = 3
    rows = 2
    preview = Image.new("RGBA", (cols * 256, rows * 256), (25, 27, 34, 255))
    for index, (_, image) in enumerate(images):
        x = (index % cols) * 256
        y = (index // cols) * 256
        preview.alpha_composite(scale_up(image), (x, y))
    return preview


def main() -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    ARTIFACTS_ROOT.mkdir(parents=True, exist_ok=True)

    specs = [
        ("fx_slash_arc.png", slash_arc()),
        ("fx_hit_blunt.png", hit_blunt()),
        ("fx_arrow_projectile.png", arrow_projectile()),
        ("fx_poison_spit.png", poison_spit()),
        ("fx_buff_flash.png", buff_flash()),
        ("fx_pickup_glint.png", pickup_glint()),
    ]

    for filename, image in specs:
        save(image, filename)

    preview_sheet(specs).save(ARTIFACTS_ROOT / "fx_wave3b_preview_sheet.png")
    print(f"Generated {len(specs)} effect placeholders in {OUTPUT_ROOT}")


if __name__ == "__main__":
    main()
