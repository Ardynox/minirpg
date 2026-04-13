from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUTPUT_ROOT = ROOT / "Assets" / "Art" / "Generated" / "facilities"
ARTIFACTS_ROOT = ROOT / "Artifacts"

LOW_RES = (64, 64)
SCALE = 4
FRAME_SIZE = (LOW_RES[0] * SCALE, LOW_RES[1] * SCALE)
PREVIEW_COLUMNS = 5
PREVIEW_ROWS = 2
PREVIEW_SIZE = (FRAME_SIZE[0] * PREVIEW_COLUMNS, FRAME_SIZE[1] * PREVIEW_ROWS)
ROTATIONS = ("north", "east", "south", "west")

Color = tuple[int, int, int, int]

TRANSPARENT: Color = (0, 0, 0, 0)
OUTLINE: Color = (44, 30, 24, 255)
SHADOW: Color = (0, 0, 0, 64)
WHITE: Color = (242, 238, 232, 255)
CREAM: Color = (228, 216, 186, 255)
WOOD: Color = (140, 92, 56, 255)
WOOD_LIGHT: Color = (171, 122, 76, 255)
WOOD_DARK: Color = (82, 53, 35, 255)
WOOD_RED: Color = (132, 72, 54, 255)
STONE: Color = (126, 128, 136, 255)
STONE_LIGHT: Color = (166, 170, 178, 255)
STONE_DARK: Color = (82, 85, 94, 255)
IRON: Color = (152, 159, 174, 255)
IRON_DARK: Color = (95, 103, 119, 255)
STEEL: Color = (172, 180, 194, 255)
STEEL_DARK: Color = (112, 120, 136, 255)
GOLD: Color = (214, 180, 82, 255)
GOLD_DARK: Color = (142, 116, 54, 255)
GREEN: Color = (84, 136, 86, 255)
GREEN_DARK: Color = (54, 90, 55, 255)
MOSS: Color = (112, 146, 92, 255)
RED: Color = (168, 86, 78, 255)
RED_DARK: Color = (108, 50, 48, 255)
BLUE: Color = (82, 118, 168, 255)
BLUE_DARK: Color = (50, 76, 116, 255)
PURPLE: Color = (120, 92, 150, 255)
PURPLE_DARK: Color = (73, 56, 100, 255)
TAN: Color = (182, 144, 94, 255)
LEATHER: Color = (156, 107, 72, 255)
LEATHER_DARK: Color = (98, 62, 41, 255)
CLOTH: Color = (196, 188, 166, 255)
EMBER: Color = (236, 160, 78, 255)
EMBER_BRIGHT: Color = (255, 220, 110, 255)
FIRE_RED: Color = (226, 104, 60, 255)
FIRE_YELLOW: Color = (255, 216, 102, 255)
HERB: Color = (114, 170, 98, 255)
HERB_DARK: Color = (64, 104, 57, 255)
GLASS: Color = (126, 200, 208, 255)
ROPE: Color = (171, 140, 84, 255)
CANVAS: Color = (220, 205, 162, 255)
CANVAS_DARK: Color = (168, 148, 108, 255)
MEAT: Color = (172, 92, 82, 255)
MEAT_DARK: Color = (110, 52, 52, 255)


@dataclass(frozen=True)
class FacilitySpec:
    facility_id: str
    drawer: Callable[[ImageDraw.ImageDraw, str], None]


def new_canvas() -> Image.Image:
    return Image.new("RGBA", LOW_RES, TRANSPARENT)


def upscale(image: Image.Image) -> Image.Image:
    return image.resize(FRAME_SIZE, Image.Resampling.NEAREST)


def shadow(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int]) -> None:
    draw.ellipse(box, fill=SHADOW)


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


def hrect(draw: ImageDraw.ImageDraw, x: int, y: int, width: int, height: int, fill: Color, outline: Color = OUTLINE) -> None:
    rect(draw, (x, y, x + width, y + height), fill, outline)


def badge(draw: ImageDraw.ImageDraw, x: int, y: int, fill: Color, outline: Color = OUTLINE) -> None:
    ellipse(draw, (x, y, x + 3, y + 3), fill, outline)


def direction_shift(rotation: str) -> int:
    return {
        "north": -2,
        "east": 3,
        "south": 1,
        "west": -3,
    }[rotation]


def direction_pair(rotation: str) -> tuple[int, int]:
    shift = direction_shift(rotation)
    return shift, -shift


def draw_plank_stack(draw: ImageDraw.ImageDraw, x: int, y: int, count: int) -> None:
    for index in range(count):
        hrect(draw, x - index, y + index * 2, 10, 2, WOOD_LIGHT, WOOD_DARK)


def draw_bottle_row(draw: ImageDraw.ImageDraw, x: int, y: int) -> None:
    for offset, color in ((0, GLASS), (3, RED), (6, BLUE), (9, HERB)):
        hrect(draw, x + offset, y + 1, 1, 3, color, WHITE)
        draw.point((x + offset, y), fill=WHITE)


def draw_crate(draw: ImageDraw.ImageDraw, x: int, y: int, width: int = 8, height: int = 6) -> None:
    hrect(draw, x, y, width, height, WOOD, WOOD_DARK)
    line(draw, (x + 1, y + 1, x + width - 1, y + height - 1), WOOD_LIGHT)
    line(draw, (x + width - 1, y + 1, x + 1, y + height - 1), WOOD_LIGHT)


def draw_hanging_sign(draw: ImageDraw.ImageDraw, x: int, y: int) -> None:
    line(draw, (x + 3, y - 4, x + 3, y), WOOD_DARK)
    hrect(draw, x, y, 6, 5, CANVAS_DARK, WOOD_DARK)
    hrect(draw, x + 1, y + 1, 4, 3, CANVAS, WOOD_DARK)


def draw_flame(draw: ImageDraw.ImageDraw, x: int, y: int) -> None:
    poly(draw, [(x, y + 8), (x + 4, y + 2), (x + 7, y + 8), (x + 4, y + 13)], FIRE_RED, RED_DARK)
    poly(draw, [(x + 2, y + 8), (x + 4, y + 4), (x + 5, y + 8), (x + 4, y + 11)], FIRE_YELLOW, EMBER)


def draw_shelf(draw: ImageDraw.ImageDraw, rotation: str) -> None:
    shift, opposite = direction_pair(rotation)
    shadow(draw, (18, 52, 46, 59))
    hrect(draw, 20, 14, 24, 34, WOOD_DARK)
    hrect(draw, 22, 16, 20, 5, WOOD_LIGHT)
    hrect(draw, 22, 27, 20, 5, WOOD)
    hrect(draw, 22, 38, 20, 5, WOOD_LIGHT)
    hrect(draw, 21, 20, 3, 28, WOOD_DARK)
    hrect(draw, 40, 20, 3, 28, WOOD_DARK)
    draw_crate(draw, 24 + shift, 18, 7, 5)
    draw_bottle_row(draw, 32 + opposite, 29)
    draw_crate(draw, 30 + shift, 39, 8, 5)
    hrect(draw, 15 + shift, 22, 4, 12, LEATHER, LEATHER_DARK)
    hrect(draw, 16 + shift, 23, 2, 6, CANVAS, WOOD_DARK)


def draw_brazier(draw: ImageDraw.ImageDraw, rotation: str) -> None:
    shift = direction_shift(rotation)
    shadow(draw, (20, 50, 44, 58))
    poly(draw, [(24, 46), (40, 46), (44, 52), (20, 52)], STONE_DARK)
    poly(draw, [(22, 35), (42, 35), (46, 46), (18, 46)], STONE, STONE_DARK)
    hrect(draw, 26, 22, 12, 7, STONE_LIGHT, STONE_DARK)
    hrect(draw, 24, 29, 4, 15, STONE_DARK)
    hrect(draw, 36, 29, 4, 15, STONE_DARK)
    draw_flame(draw, 27 + shift, 24)
    draw_flame(draw, 23 + shift // 2, 28)
    draw_flame(draw, 31 - shift // 2, 29)


def draw_bed(draw: ImageDraw.ImageDraw, rotation: str) -> None:
    shift = direction_shift(rotation)
    shadow(draw, (12, 52, 50, 58))
    hrect(draw, 14, 20, 36, 24, WOOD_DARK)
    hrect(draw, 16, 23, 32, 18, CLOTH, WOOD_DARK)
    hrect(draw, 17, 24, 30, 6, WHITE, OUTLINE)
    hrect(draw, 18 + shift, 31, 26, 8, RED, RED_DARK)
    hrect(draw, 14, 16, 36, 4, WOOD, WOOD_DARK)
    hrect(draw, 14, 44, 36, 3, WOOD, WOOD_DARK)
    hrect(draw, 16, 45, 2, 6, WOOD_DARK)
    hrect(draw, 46, 45, 2, 6, WOOD_DARK)
    hrect(draw, 19 + shift, 24, 8, 4, BLUE, BLUE_DARK)


def draw_dormitory_bed(draw: ImageDraw.ImageDraw, rotation: str) -> None:
    shift = direction_shift(rotation)
    shadow(draw, (18, 52, 44, 58))
    hrect(draw, 18, 24, 28, 16, IRON_DARK)
    hrect(draw, 20, 25, 24, 12, CLOTH, IRON_DARK)
    hrect(draw, 21 + shift, 26, 9, 3, WHITE, OUTLINE)
    hrect(draw, 22 + shift, 31, 16, 4, BLUE, BLUE_DARK)
    hrect(draw, 17, 22, 2, 22, IRON)
    hrect(draw, 45, 22, 2, 22, IRON)
    hrect(draw, 20, 40, 2, 7, IRON_DARK)
    hrect(draw, 42, 40, 2, 7, IRON_DARK)


def draw_stove(draw: ImageDraw.ImageDraw, rotation: str) -> None:
    shift = direction_shift(rotation)
    shadow(draw, (15, 51, 49, 58))
    hrect(draw, 18, 24, 28, 24, STONE, STONE_DARK)
    hrect(draw, 24, 29, 16, 13, STONE_LIGHT, STONE_DARK)
    hrect(draw, 28, 31, 8, 8, EMBER, STONE_DARK)
    line(draw, (30, 42, 34, 42), STONE_DARK)
    hrect(draw, 40 + shift, 10, 6, 20, STONE_DARK)
    hrect(draw, 41 + shift, 8, 4, 3, STONE_LIGHT)
    hrect(draw, 20 - shift, 19, 6, 4, IRON, IRON_DARK)
    hrect(draw, 22 - shift, 17, 2, 2, WOOD_DARK)
    hrect(draw, 13 + shift, 38, 5, 9, WOOD, WOOD_DARK)


def draw_butcher_table(draw: ImageDraw.ImageDraw, rotation: str) -> None:
    shift = direction_shift(rotation)
    shadow(draw, (14, 52, 50, 58))
    hrect(draw, 16, 26, 32, 8, WOOD_LIGHT, WOOD_DARK)
    hrect(draw, 18, 34, 4, 15, WOOD_DARK)
    hrect(draw, 42, 34, 4, 15, WOOD_DARK)
    hrect(draw, 23, 30, 18, 2, MEAT_DARK)
    ellipse(draw, (24 + shift, 28, 31 + shift, 34), MEAT, MEAT_DARK)
    line(draw, (38 - shift, 22, 44 - shift, 15), IRON_DARK, 2)
    poly(draw, [(42 - shift, 14), (48 - shift, 17), (41 - shift, 20)], STEEL, STEEL_DARK)
    hrect(draw, 12 + shift, 22, 2, 12, IRON, IRON_DARK)
    badge(draw, 13 + shift, 21, GOLD)


def draw_smithy(draw: ImageDraw.ImageDraw, rotation: str) -> None:
    shift = direction_shift(rotation)
    shadow(draw, (12, 52, 50, 58))
    hrect(draw, 18, 22, 26, 24, STONE_DARK)
    hrect(draw, 20, 25, 22, 18, STONE, STONE_DARK)
    hrect(draw, 24, 30, 14, 9, EMBER, STONE_DARK)
    hrect(draw, 39 + shift, 9, 6, 21, STONE_DARK)
    hrect(draw, 40 + shift, 7, 4, 3, STONE_LIGHT)
    hrect(draw, 11 - shift, 34, 8, 6, STEEL, STEEL_DARK)
    poly(draw, [(10 - shift, 40), (20 - shift, 40), (16 - shift, 45), (12 - shift, 45)], STEEL_DARK)
    line(draw, (15 - shift, 28, 11 - shift, 22), WOOD_DARK, 2)
    line(draw, (15 - shift, 28, 19 - shift, 23), WOOD_DARK, 2)
    badge(draw, 29, 28, EMBER_BRIGHT, EMBER)


def draw_loom(draw: ImageDraw.ImageDraw, rotation: str) -> None:
    shift = direction_shift(rotation)
    shadow(draw, (14, 52, 50, 58))
    hrect(draw, 18, 16, 4, 32, WOOD_DARK)
    hrect(draw, 42, 16, 4, 32, WOOD_DARK)
    hrect(draw, 18, 16, 28, 4, WOOD)
    hrect(draw, 18, 44, 28, 4, WOOD)
    for row in range(22, 42, 3):
        line(draw, (24, row, 40, row), CANVAS)
    hrect(draw, 24, 21, 16, 18, PURPLE, PURPLE_DARK)
    line(draw, (22, 19, 42, 43), ROPE)
    line(draw, (42, 19, 22, 43), ROPE)
    hrect(draw, 10 + shift, 30, 8, 3, WOOD_LIGHT, WOOD_DARK)
    badge(draw, 17 + shift, 29, GOLD)


def draw_herbal_bench(draw: ImageDraw.ImageDraw, rotation: str) -> None:
    shift = direction_shift(rotation)
    shadow(draw, (14, 52, 50, 58))
    hrect(draw, 16, 27, 32, 8, WOOD_LIGHT, WOOD_DARK)
    hrect(draw, 19, 35, 4, 13, WOOD_DARK)
    hrect(draw, 41, 35, 4, 13, WOOD_DARK)
    draw_bottle_row(draw, 20 - shift, 22)
    poly(draw, [(29, 28), (34, 24), (38, 30), (33, 34)], HERB, HERB_DARK)
    poly(draw, [(23 + shift, 34), (28 + shift, 31), (30 + shift, 37), (24 + shift, 39)], HERB, HERB_DARK)
    ellipse(draw, (35 - shift, 33, 41 - shift, 38), STONE_LIGHT, STONE_DARK)
    hrect(draw, 38 - shift, 28, 2, 7, WOOD_DARK)
    hrect(draw, 11 + shift, 21, 5, 9, LEATHER, LEATHER_DARK)


def draw_market_stall(draw: ImageDraw.ImageDraw, rotation: str) -> None:
    shift = direction_shift(rotation)
    shadow(draw, (8, 53, 56, 60))
    hrect(draw, 10, 18, 4, 31, WOOD_DARK)
    hrect(draw, 50, 18, 4, 31, WOOD_DARK)
    hrect(draw, 13, 16, 38, 6, WOOD)
    poly(draw, [(10, 20), (54, 20), (48, 31), (16, 31)], RED, RED_DARK)
    for stripe_x in range(16, 49, 8):
        hrect(draw, stripe_x, 21, 4, 9, CANVAS, OUTLINE)
    hrect(draw, 14, 33, 36, 12, WOOD_LIGHT, WOOD_DARK)
    hrect(draw, 16, 35, 8, 8, GLASS, WHITE)
    hrect(draw, 26, 35, 7, 8, PURPLE, PURPLE_DARK)
    hrect(draw, 35, 35, 10, 8, GREEN, GREEN_DARK)
    draw_crate(draw, 8 + shift, 39, 7, 8)
    draw_crate(draw, 49 - shift, 39, 7, 8)
    draw_hanging_sign(draw, 28 + shift, 9)
    line(draw, (18, 31, 18, 45), ROPE)
    line(draw, (46, 31, 46, 45), ROPE)


SPECS = [
    FacilitySpec("shelf", draw_shelf),
    FacilitySpec("fire_brazier", draw_brazier),
    FacilitySpec("bed", draw_bed),
    FacilitySpec("dormitory_bed", draw_dormitory_bed),
    FacilitySpec("stove", draw_stove),
    FacilitySpec("butcher_table", draw_butcher_table),
    FacilitySpec("smithy", draw_smithy),
    FacilitySpec("loom", draw_loom),
    FacilitySpec("herbal_bench", draw_herbal_bench),
    FacilitySpec("market_stall", draw_market_stall),
]


def build_sheet(spec: FacilitySpec) -> Image.Image:
    sheet = Image.new("RGBA", (FRAME_SIZE[0], FRAME_SIZE[1] * len(ROTATIONS)), TRANSPARENT)
    for index, rotation in enumerate(ROTATIONS):
        low_res = new_canvas()
        draw = ImageDraw.Draw(low_res)
        spec.drawer(draw, rotation)
        frame = upscale(low_res)
        sheet.alpha_composite(frame, (0, index * FRAME_SIZE[1]))
    return sheet


def build_preview(sheets: list[tuple[FacilitySpec, Image.Image]]) -> Image.Image:
    preview = Image.new("RGBA", PREVIEW_SIZE, (25, 27, 34, 255))
    for index, (spec, sheet) in enumerate(sheets):
        row = index // PREVIEW_COLUMNS
        col = index % PREVIEW_COLUMNS
        y = row * FRAME_SIZE[1]
        x = col * FRAME_SIZE[0]
        north_frame = sheet.crop((0, 0, FRAME_SIZE[0], FRAME_SIZE[1]))
        preview.alpha_composite(north_frame, (x, y))

        label = spec.facility_id.replace("_", " ")
        draw = ImageDraw.Draw(preview)
        text_x = x + 16
        text_y = y + FRAME_SIZE[1] - 24
        draw.rectangle((text_x - 6, text_y - 4, text_x + len(label) * 6 + 6, text_y + 12), fill=(0, 0, 0, 110))
        draw.text((text_x, text_y), label, fill=WHITE)
    return preview


def main() -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    ARTIFACTS_ROOT.mkdir(parents=True, exist_ok=True)

    sheets: list[tuple[FacilitySpec, Image.Image]] = []
    for spec in SPECS:
        sheet = build_sheet(spec)
        sheets.append((spec, sheet))
        sheet.save(OUTPUT_ROOT / f"facility_{spec.facility_id}_4dir.png")

    preview = build_preview(sheets)
    preview.save(ARTIFACTS_ROOT / "facility_map_preview_sheet.png")


if __name__ == "__main__":
    main()
