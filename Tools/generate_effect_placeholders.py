from __future__ import annotations

import math
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
SKY = (156, 220, 255, 232)
SKY_SOFT = (205, 242, 255, 182)
BACKGROUND = (25, 27, 34, 255)


def new_canvas() -> Image.Image:
    return Image.new("RGBA", LOGICAL_SIZE, TRANSPARENT)


def scale_up(image: Image.Image) -> Image.Image:
    return image.resize(CANVAS_SIZE, Image.Resampling.NEAREST)


def save_still(name: str, image: Image.Image) -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    scale_up(image).save(OUTPUT_ROOT / f"{name}.png")


def save_frames(name: str, frames: list[Image.Image]) -> None:
    folder = OUTPUT_ROOT / name
    folder.mkdir(parents=True, exist_ok=True)
    for index, frame in enumerate(frames):
        scale_up(frame).save(folder / f"{name}_{index:02d}.png")


def with_alpha(color: tuple[int, int, int, int], factor: float) -> tuple[int, int, int, int]:
    factor = max(0.0, min(factor, 1.0))
    return color[0], color[1], color[2], round(color[3] * factor)


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def ease_out(t: float) -> float:
    return 1.0 - (1.0 - t) * (1.0 - t)


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
    outer: tuple[int, int, int, int],
    inner: tuple[int, int, int, int],
) -> None:
    draw.polygon(spark_points(cx, cy, long_r, short_r), fill=outer)
    inner_long = max(1, long_r - 2)
    inner_short = max(1, short_r - 1)
    draw.polygon(spark_points(cx, cy, inner_long, inner_short), fill=inner)
    draw.point((cx, cy), WHITE)


def sample_arc(start_t: float, end_t: float, lift: float) -> list[tuple[int, int]]:
    start_t = max(0.0, min(start_t, 1.0))
    end_t = max(start_t + 0.05, min(end_t, 1.0))
    steps = max(4, round(18 * (end_t - start_t)))
    points: list[tuple[int, int]] = []
    for step in range(steps + 1):
        t = lerp(start_t, end_t, step / steps)
        x = round(lerp(13, 50, t))
        y = round(lerp(45, 13, t) - lift * (1 - (2 * t - 1) ** 2))
        points.append((x, y))
    return points


def radial_polygon(cx: int, cy: int, radii: list[float]) -> list[tuple[int, int]]:
    points: list[tuple[int, int]] = []
    for index, radius in enumerate(radii):
        angle = math.radians(index * (360.0 / len(radii)) - 90.0)
        x = round(cx + math.cos(angle) * radius)
        y = round(cy + math.sin(angle) * radius)
        points.append((x, y))
    return points


def slash_arc_frames(count: int = 6) -> list[Image.Image]:
    frames: list[Image.Image] = []
    for index in range(count):
        t = index / (count - 1)
        intensity = 1.0 - min(abs(t - 0.5) * 1.25, 0.85)
        start_t = max(0.0, t * 0.58 - 0.08)
        end_t = min(1.0, start_t + 0.42 + 0.18 * (1.0 - abs(t - 0.5) * 1.6))
        points = sample_arc(start_t, end_t, 8.5 + 2.0 * t)
        image = new_canvas()
        draw = ImageDraw.Draw(image)

        for width, color, factor in (
            (8, PALE_GOLD, 0.32),
            (6, GOLD, 0.64),
            (3, SOFT_WHITE, 0.92),
            (1, WHITE, 1.0),
        ):
            draw.line(points, fill=with_alpha(color, intensity * factor), width=width, joint="curve")

        tip_x, tip_y = points[-1]
        draw.polygon(
            ((tip_x - 1, tip_y - 1), (tip_x + 7, tip_y - 3), (tip_x + 1, tip_y + 4)),
            fill=with_alpha(WHITE, 0.9 * intensity),
        )
        trail = points[max(0, len(points) // 3 - 1)]
        draw.line(
            (trail[0] - 7, trail[1] + 2, trail[0] - 2, trail[1] + 4),
            fill=with_alpha(PALE_GOLD, 0.55 * intensity),
            width=2,
        )
        draw.line(
            (trail[0] - 3, trail[1] - 4, trail[0] - 7, trail[1] - 6),
            fill=with_alpha(SOFT_WHITE, 0.45 * intensity),
            width=1,
        )
        frames.append(image)
    return frames


def hit_blunt_frames(count: int = 6) -> list[Image.Image]:
    base = [8, 11, 18, 11, 16, 10, 17, 10, 19, 10, 17, 11, 16, 10, 18, 11]
    frames: list[Image.Image] = []
    for index in range(count):
        t = index / (count - 1)
        burst = 0.58 + ease_out(t) * 0.72
        glow = 1.0 - t * 0.35
        radii = [value * burst for value in base]
        image = new_canvas()
        draw = ImageDraw.Draw(image)
        outer = radial_polygon(32, 32, radii)
        middle = radial_polygon(32, 32, [radius * 0.72 for radius in radii])
        inner = radial_polygon(32, 32, [radius * 0.44 for radius in radii])

        draw.polygon(outer, fill=with_alpha(GREY_DARK, 0.85 - t * 0.25))
        draw.polygon(middle, fill=with_alpha(GREY, 0.95 - t * 0.12))
        draw.polygon(inner, fill=with_alpha(WHITE, glow))
        draw.ellipse((26 - t * 2, 26 - t * 2, 38 + t * 2, 38 + t * 2), fill=with_alpha(WHITE, 0.55 * glow))

        debris = [
            (225, 17 + t * 10, GREY_DARK),
            (315, 14 + t * 7, GREY),
            (25, 19 + t * 11, GREY),
            (125, 16 + t * 9, WHITE),
        ]
        for angle_deg, distance, color in debris:
            angle = math.radians(angle_deg)
            x = round(32 + math.cos(angle) * distance)
            y = round(32 + math.sin(angle) * distance)
            size = 2 if color == WHITE else 3
            draw.rectangle((x - size, y - size, x + size, y + size), fill=with_alpha(color, 0.88 - t * 0.35))

        frames.append(image)
    return frames


def arrow_projectile_frames(count: int = 5) -> list[Image.Image]:
    frames: list[Image.Image] = []
    for index in range(count):
        t = index / (count - 1)
        image = new_canvas()
        draw = ImageDraw.Draw(image)
        y = 31
        trail_start = 8
        trail_end = round(24 + t * 10)

        for offset, color, width in (
            (-2, with_alpha(WHITE, 0.18 + t * 0.08), 2),
            (0, with_alpha(SKY_SOFT, 0.3 + t * 0.15), 2),
            (2, with_alpha(FEATHER, 0.18 + t * 0.08), 2),
        ):
            draw.line((trail_start, y + offset, trail_end, y + offset), fill=color, width=width)

        draw.line((18, y, 46, y), fill=WOOD_DARK, width=4)
        draw.line((17, y, 45, y), fill=WOOD, width=2)
        draw.polygon(((46, 31), (54, 27), (54, 35)), fill=IRON_DARK)
        draw.polygon(((47, 31), (53, 28), (53, 34)), fill=IRON)
        draw.polygon(((18, 31), (12, 26), (15, 31), (12, 36)), fill=with_alpha((176, 78, 57, 220), 0.86))
        draw.polygon(((20, 31), (15, 27), (17, 31), (15, 35)), fill=with_alpha(FEATHER, 0.82))
        draw.line((28, 29, 44, 29), fill=with_alpha(WHITE, 0.2 + t * 0.25), width=1)
        frames.append(image)
    return frames


def poison_spit_frames(count: int = 6) -> list[Image.Image]:
    frames: list[Image.Image] = []
    for index in range(count):
        t = index / (count - 1)
        swell = 1.0 + math.sin(t * math.pi) * 0.18
        image = new_canvas()
        draw = ImageDraw.Draw(image)

        haze = [
            ((10, 24, 20, 34), with_alpha(SMOKE_GREEN, 0.8 - t * 0.15)),
            ((15, 20, 28, 32), with_alpha((106, 206, 100, 125), 0.9 - t * 0.1)),
            ((22, 23, 35, 35), with_alpha((126, 220, 96, 145), 0.95 - t * 0.08)),
        ]
        for box, color in haze:
            draw.ellipse(box, fill=color)

        core_box = (
            round(28 - swell),
            round(24 - swell * 0.6),
            round(43 + swell),
            round(39 + swell * 0.6),
        )
        inner_box = (
            round(30 - swell * 0.6),
            round(26 - swell * 0.4),
            round(41 + swell * 0.6),
            round(37 + swell * 0.4),
        )
        draw.ellipse(core_box, fill=GREEN_DARK)
        draw.ellipse(inner_box, fill=GREEN)
        draw.ellipse((33, 28, 38, 33), fill=with_alpha(LIME, 0.82 + t * 0.16))
        draw.polygon(((41, 30), (50, 27), (49, 36)), fill=GREEN_DARK)
        draw.polygon(((42, 30), (48, 28), (47, 35)), fill=GREEN)

        droplets = [
            ((34 + index // 2, 36, 38 + index // 2, 43), GREEN_DARK, GREEN),
            ((44 + index // 3, 34 + index // 2, 48 + index // 3, 41 + index // 2), GREEN_DARK, LIME),
            ((27 + index // 4, 38, 30 + index // 4, 44), GREEN_DARK, GREEN),
        ]
        for box, outer, inner in droplets:
            draw.ellipse(box, fill=outer)
            inset = (box[0] + 1, box[1] + 1, box[2] - 1, box[3] - 1)
            draw.ellipse(inset, fill=inner)

        frames.append(image)
    return frames


def buff_flash_frames(count: int = 6) -> list[Image.Image]:
    frames: list[Image.Image] = []
    for index in range(count):
        t = index / (count - 1)
        beam_width = round(4 + math.sin(t * math.pi) * 5)
        image = new_canvas()
        draw = ImageDraw.Draw(image)
        draw.polygon(
            ((32 - beam_width, 52), (32 + beam_width, 52), (40, 22), (32, 10), (24, 22)),
            fill=with_alpha(PALE_GOLD, 0.4 + (1.0 - abs(t - 0.5) * 2.0) * 0.35),
        )
        draw.polygon(
            ((32 - beam_width + 1, 52), (32 + beam_width - 1, 52), (37, 23), (32, 13), (27, 23)),
            fill=with_alpha(GOLD, 0.85),
        )
        draw.polygon(((30, 52), (34, 52), (35, 24), (32, 15), (29, 24)), fill=WHITE)

        for spark in (
            (21, 18, 4 + index % 2, 2),
            (43, 16, 4, 2),
            (19, 33, 3, 1),
            (45, 31, 3 + (index // 2), 1),
            (32, 8, 4, 2),
        ):
            draw_spark(draw, *spark, outer=with_alpha(PALE_GOLD, 0.7), inner=WHITE)

        draw.line((32, 18, 32, 9), fill=WHITE, width=2)
        draw.line((25, 28, 20, 20), fill=with_alpha(SKY, 0.55), width=2)
        draw.line((39, 28, 44, 20), fill=with_alpha(SKY, 0.55), width=2)
        frames.append(image)
    return frames


def pickup_glint_frames(count: int = 6) -> list[Image.Image]:
    frames: list[Image.Image] = []
    for index in range(count):
        t = index / (count - 1)
        pulse = 0.8 + math.sin(t * math.pi) * 0.55
        image = new_canvas()
        draw = ImageDraw.Draw(image)
        draw_spark(draw, 32, 32, round(8 + pulse * 5), round(3 + pulse * 2), outer=with_alpha(GOLD, 0.92), inner=WHITE)
        draw_spark(draw, 43, 22, round(3 + pulse * 2), 1, outer=with_alpha(PALE_GOLD, 0.74), inner=WHITE)
        draw_spark(draw, 22, 42, round(2 + pulse * 1.5), 1, outer=with_alpha((255, 236, 166, 160), 0.8), inner=SOFT_WHITE)
        draw.polygon(((32, 26), (38, 32), (32, 38), (26, 32)), fill=with_alpha((255, 245, 185, 255), 0.8 + t * 0.12))
        draw.polygon(((32, 28), (36, 32), (32, 36), (28, 32)), fill=WHITE)
        frames.append(image)
    return frames


def preview_sheet(images: list[tuple[str, Image.Image]]) -> Image.Image:
    cols = 3
    rows = math.ceil(len(images) / cols)
    preview = Image.new("RGBA", (cols * 256, rows * 256), BACKGROUND)
    for index, (_, image) in enumerate(images):
        x = (index % cols) * 256
        y = (index // cols) * 256
        preview.alpha_composite(scale_up(image), (x, y))
    return preview


def animation_sheet(animations: list[tuple[str, list[Image.Image]]]) -> Image.Image:
    cols = max(len(frames) for _, frames in animations)
    rows = len(animations)
    sheet = Image.new("RGBA", (cols * 256, rows * 256), BACKGROUND)
    for row, (_, frames) in enumerate(animations):
        for col, frame in enumerate(frames):
            sheet.alpha_composite(scale_up(frame), (col * 256, row * 256))
    return sheet


def main() -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    ARTIFACTS_ROOT.mkdir(parents=True, exist_ok=True)

    specs = [
        ("fx_slash_arc", slash_arc_frames()),
        ("fx_hit_blunt", hit_blunt_frames()),
        ("fx_arrow_projectile", arrow_projectile_frames()),
        ("fx_poison_spit", poison_spit_frames()),
        ("fx_buff_flash", buff_flash_frames()),
        ("fx_pickup_glint", pickup_glint_frames()),
    ]

    stills: list[tuple[str, Image.Image]] = []
    for name, frames in specs:
        save_frames(name, frames)
        still = frames[min(len(frames) // 2, len(frames) - 1)]
        save_still(name, still)
        stills.append((name, still))

    preview = preview_sheet(stills)
    preview.save(ARTIFACTS_ROOT / "fx_wave3b_preview_sheet.png")
    preview.save(ARTIFACTS_ROOT / "fx_wave4_preview_sheet.png")
    animation_sheet(specs).save(ARTIFACTS_ROOT / "fx_wave4_animation_sheet.png")
    print(f"Generated {len(specs)} animated effect sets in {OUTPUT_ROOT}")


if __name__ == "__main__":
    main()
