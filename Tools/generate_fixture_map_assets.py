from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUTPUT_ROOT = ROOT / "Assets" / "Art" / "Generated" / "fixtures"
ARTIFACTS_ROOT = ROOT / "Artifacts"

LOW_RES = (64, 64)
SCALE = 4
FRAME_SIZE = (LOW_RES[0] * SCALE, LOW_RES[1] * SCALE)
PREVIEW_COLUMNS = 3
PREVIEW_ROWS = 2
PREVIEW_SIZE = (FRAME_SIZE[0] * PREVIEW_COLUMNS, FRAME_SIZE[1] * PREVIEW_ROWS)

Color = tuple[int, int, int, int]

TRANSPARENT: Color = (0, 0, 0, 0)
OUTLINE: Color = (44, 30, 24, 255)
SHADOW: Color = (0, 0, 0, 64)
WHITE: Color = (242, 238, 232, 255)
STONE: Color = (128, 132, 144, 255)
STONE_LIGHT: Color = (172, 176, 188, 255)
STONE_DARK: Color = (82, 86, 96, 255)
WOOD: Color = (144, 98, 62, 255)
WOOD_LIGHT: Color = (180, 132, 84, 255)
WOOD_DARK: Color = (84, 56, 38, 255)
LEATHER: Color = (152, 108, 72, 255)
LEATHER_DARK: Color = (96, 66, 44, 255)
EMBER: Color = (236, 156, 78, 255)
EMBER_BRIGHT: Color = (255, 226, 124, 255)
FIRE_RED: Color = (228, 102, 64, 255)
FIRE_YELLOW: Color = (255, 216, 96, 255)
ASH: Color = (114, 102, 94, 255)
GREEN: Color = (92, 134, 82, 255)
GREEN_DARK: Color = (58, 92, 54, 255)
MOSS: Color = (132, 160, 92, 255)
ROPE: Color = (174, 144, 94, 255)
BLUE: Color = (86, 122, 172, 255)
BLUE_DARK: Color = (52, 78, 118, 255)
CREAM: Color = (226, 216, 184, 255)


@dataclass(frozen=True)
class FixtureSpec:
    fixture_id: str
    drawer: Callable[[ImageDraw.ImageDraw], None]


def new_canvas() -> Image.Image:
    return Image.new("RGBA", LOW_RES, TRANSPARENT)


def upscale(image: Image.Image) -> Image.Image:
    return image.resize(FRAME_SIZE, Image.Resampling.NEAREST)


def rect(
    draw: ImageDraw.ImageDraw,
    xy: tuple[int, int, int, int],
    fill: Color,
    outline: Color = OUTLINE,
) -> None:
    draw.rectangle(xy, fill=fill, outline=outline)


def ellipse(
    draw: ImageDraw.ImageDraw,
    xy: tuple[int, int, int, int],
    fill: Color,
    outline: Color = OUTLINE,
) -> None:
    draw.ellipse(xy, fill=fill, outline=outline)


def poly(
    draw: ImageDraw.ImageDraw,
    points: list[tuple[int, int]],
    fill: Color,
    outline: Color = OUTLINE,
) -> None:
    draw.polygon(points, fill=fill, outline=outline)


def line(
    draw: ImageDraw.ImageDraw,
    points: tuple[int, int, int, int] | list[tuple[int, int]],
    fill: Color,
    width: int = 1,
) -> None:
    draw.line(points, fill=fill, width=width)


def shadow(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int]) -> None:
    draw.ellipse(box, fill=SHADOW)


def draw_flame(draw: ImageDraw.ImageDraw, x: int, y: int, size: int = 0) -> None:
    poly(
        draw,
        [(x, y + 12 - size), (x + 5, y + 4 - size), (x + 10, y + 11 - size), (x + 5, y + 18 - size)],
        FIRE_RED,
        WOOD_DARK,
    )
    poly(
        draw,
        [(x + 2, y + 12 - size), (x + 5, y + 6 - size), (x + 7, y + 12 - size), (x + 5, y + 16 - size)],
        FIRE_YELLOW,
        EMBER,
    )


def draw_stair_down(draw: ImageDraw.ImageDraw) -> None:
    shadow(draw, (16, 49, 48, 57))
    poly(draw, [(18, 24), (46, 24), (50, 47), (14, 47)], STONE_DARK)
    poly(draw, [(20, 19), (44, 19), (48, 24), (16, 24)], STONE_LIGHT, STONE_DARK)
    for step in range(4):
        x0 = 22 + step * 4
        y0 = 25 + step * 4
        rect(draw, (x0, y0, 42 - step * 2, y0 + 4), STONE, STONE_DARK)
    poly(draw, [(28, 28), (38, 28), (33, 39)], BLUE, BLUE_DARK)


def draw_stair_up(draw: ImageDraw.ImageDraw) -> None:
    shadow(draw, (16, 49, 48, 57))
    poly(draw, [(16, 24), (48, 24), (44, 47), (20, 47)], STONE_DARK)
    poly(draw, [(18, 19), (46, 19), (48, 24), (16, 24)], STONE_LIGHT, STONE_DARK)
    for step in range(4):
        x0 = 22 - step
        y0 = 41 - step * 4
        rect(draw, (x0, y0, 42 + step, y0 + 4), STONE, STONE_DARK)
    poly(draw, [(28, 37), (38, 37), (33, 26)], BLUE, BLUE_DARK)


def draw_fire(draw: ImageDraw.ImageDraw) -> None:
    shadow(draw, (18, 50, 46, 57))
    ellipse(draw, (22, 44, 42, 49), ASH, STONE_DARK)
    draw_flame(draw, 23, 26, 0)
    draw_flame(draw, 18, 31, 2)
    draw_flame(draw, 29, 30, 1)
    line(draw, (20, 46, 44, 46), ASH)


def draw_nest(draw: ImageDraw.ImageDraw) -> None:
    shadow(draw, (16, 50, 48, 58))
    for offset in range(0, 5):
        line(draw, (20 + offset, 42 - offset, 44 - offset, 47 + offset), WOOD_DARK)
        line(draw, (20 + offset, 47 + offset, 44 - offset, 42 - offset), WOOD)
    ellipse(draw, (20, 36, 44, 50), LEATHER, LEATHER_DARK)
    ellipse(draw, (24, 39, 40, 48), ASH, LEATHER_DARK)
    ellipse(draw, (25, 39, 31, 45), CREAM, OUTLINE)
    ellipse(draw, (33, 40, 39, 46), MOSS, GREEN_DARK)
    ellipse(draw, (29, 42, 35, 48), CREAM, OUTLINE)


def draw_door(draw: ImageDraw.ImageDraw) -> None:
    shadow(draw, (16, 50, 48, 58))
    rect(draw, (18, 12, 46, 48), WOOD_DARK, WOOD_DARK)
    rect(draw, (21, 14, 43, 46), WOOD, WOOD_DARK)
    rect(draw, (25, 19, 39, 40), WOOD_LIGHT, WOOD_DARK)
    line(draw, (32, 14, 32, 46), WOOD_DARK)
    rect(draw, (22, 13, 27, 17), STONE_LIGHT, STONE_DARK)
    rect(draw, (37, 13, 42, 17), STONE_LIGHT, STONE_DARK)
    ellipse(draw, (37, 29, 40, 32), EMBER_BRIGHT, OUTLINE)


def draw_campfire(draw: ImageDraw.ImageDraw) -> None:
    shadow(draw, (16, 50, 48, 58))
    line(draw, (20, 46, 32, 36), WOOD_DARK, 3)
    line(draw, (44, 46, 32, 36), WOOD_DARK, 3)
    line(draw, (24, 47, 38, 34), WOOD, 2)
    line(draw, (40, 47, 26, 34), WOOD, 2)
    ellipse(draw, (22, 44, 42, 49), ASH, STONE_DARK)
    draw_flame(draw, 24, 24, 0)
    draw_flame(draw, 29, 28, 2)
    draw_flame(draw, 20, 29, 2)
    line(draw, (18, 48, 46, 48), ASH)


SPECS = [
    FixtureSpec("stair_down", draw_stair_down),
    FixtureSpec("stair_up", draw_stair_up),
    FixtureSpec("fire", draw_fire),
    FixtureSpec("nest", draw_nest),
    FixtureSpec("door", draw_door),
    FixtureSpec("campfire", draw_campfire),
]


def build_preview(images: list[tuple[FixtureSpec, Image.Image]]) -> Image.Image:
    preview = Image.new("RGBA", PREVIEW_SIZE, (25, 27, 34, 255))
    draw = ImageDraw.Draw(preview)
    for index, (spec, image) in enumerate(images):
        row = index // PREVIEW_COLUMNS
        col = index % PREVIEW_COLUMNS
        x = col * FRAME_SIZE[0]
        y = row * FRAME_SIZE[1]
        preview.alpha_composite(image, (x, y))

        label = spec.fixture_id.replace("_", " ")
        text_x = x + 18
        text_y = y + FRAME_SIZE[1] - 24
        draw.rectangle((text_x - 6, text_y - 4, text_x + len(label) * 6 + 6, text_y + 12), fill=(0, 0, 0, 110))
        draw.text((text_x, text_y), label, fill=WHITE)
    return preview


def main() -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    ARTIFACTS_ROOT.mkdir(parents=True, exist_ok=True)

    images: list[tuple[FixtureSpec, Image.Image]] = []
    for spec in SPECS:
        low_res = new_canvas()
        draw = ImageDraw.Draw(low_res)
        spec.drawer(draw)
        image = upscale(low_res)
        images.append((spec, image))
        image.save(OUTPUT_ROOT / f"{spec.fixture_id}.png")

    preview = build_preview(images)
    preview.save(ARTIFACTS_ROOT / "fixture_map_preview_sheet.png")


if __name__ == "__main__":
    main()
