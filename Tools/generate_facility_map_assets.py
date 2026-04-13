from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
DATA_PATH = ROOT / "Data" / "facilities.json"
OUTPUT_ROOT = ROOT / "Assets" / "Art" / "Generated" / "facilities"
ARTIFACTS_ROOT = ROOT / "Artifacts"

LOW_RES = (64, 64)
SCALE = 4
FRAME_SIZE = (LOW_RES[0] * SCALE, LOW_RES[1] * SCALE)
PREVIEW_COLUMNS = 5
PREVIEW_ROWS = 2
PREVIEW_SIZE = (FRAME_SIZE[0] * PREVIEW_COLUMNS, FRAME_SIZE[1] * PREVIEW_ROWS)
ROTATIONS = ("north", "east", "south", "west")

ORIGIN_X = 32
ORIGIN_Y = 36
CELL_HALF_W = 10
CELL_HALF_H = 5
BLOCK_HEIGHT = 6

Color = tuple[int, int, int, int]
Point = tuple[float, float]

TRANSPARENT: Color = (0, 0, 0, 0)
OUTLINE: Color = (44, 30, 24, 255)
SHADOW: Color = (0, 0, 0, 72)
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
GREEN: Color = (84, 136, 86, 255)
GREEN_DARK: Color = (54, 90, 55, 255)
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

LONG_AXIS = {
    "north": (CELL_HALF_W, CELL_HALF_H),
    "east": (-CELL_HALF_W, CELL_HALF_H),
    "south": (-CELL_HALF_W, -CELL_HALF_H),
    "west": (CELL_HALF_W, -CELL_HALF_H),
}

CROSS_AXIS = {
    "north": (-CELL_HALF_W, CELL_HALF_H),
    "east": (-CELL_HALF_W, -CELL_HALF_H),
    "south": (CELL_HALF_W, -CELL_HALF_H),
    "west": (CELL_HALF_W, CELL_HALF_H),
}

FACILITY_DATA = {
    entry["id"]: entry
    for entry in json.loads(DATA_PATH.read_text(encoding="utf-8"))
}


@dataclass(frozen=True)
class FacilitySpec:
    facility_id: str
    drawer: Callable[[ImageDraw.ImageDraw, "FacilityLayout", str], None]


@dataclass(frozen=True)
class RenderCell:
    index: int
    grid_x: float
    grid_y: float
    screen_x: float
    screen_y: float


@dataclass(frozen=True)
class FacilityLayout:
    cells: list[RenderCell]
    anchor: RenderCell
    center_x: float
    center_y: float
    min_x: float
    max_x: float
    min_y: float
    max_y: float

    @property
    def center(self) -> Point:
        return (self.center_x, self.center_y)

    @property
    def anchor_screen(self) -> Point:
        return (self.anchor.screen_x, self.anchor.screen_y)


def new_canvas() -> Image.Image:
    return Image.new("RGBA", LOW_RES, TRANSPARENT)


def upscale(image: Image.Image) -> Image.Image:
    return image.resize(FRAME_SIZE, Image.Resampling.NEAREST)


def rect(
    draw: ImageDraw.ImageDraw,
    xy: tuple[float, float, float, float],
    fill: Color,
    outline: Color = OUTLINE,
) -> None:
    draw.rectangle(tuple(round(v) for v in xy), fill=fill, outline=outline)


def ellipse(
    draw: ImageDraw.ImageDraw,
    xy: tuple[float, float, float, float],
    fill: Color,
    outline: Color | None = OUTLINE,
) -> None:
    if outline is None:
        draw.ellipse(tuple(round(v) for v in xy), fill=fill)
        return
    draw.ellipse(tuple(round(v) for v in xy), fill=fill, outline=outline)


def poly(
    draw: ImageDraw.ImageDraw,
    points: list[Point],
    fill: Color,
    outline: Color = OUTLINE,
) -> None:
    draw.polygon([(round(x), round(y)) for x, y in points], fill=fill, outline=outline)


def line(
    draw: ImageDraw.ImageDraw,
    points: tuple[float, float, float, float] | list[Point],
    fill: Color,
    width: int = 1,
) -> None:
    if isinstance(points, tuple):
        draw.line(tuple(round(v) for v in points), fill=fill, width=width)
        return
    draw.line([(round(x), round(y)) for x, y in points], fill=fill, width=width)


def add(a: Point, b: Point) -> Point:
    return (a[0] + b[0], a[1] + b[1])


def scale(v: Point, amount: float) -> Point:
    return (v[0] * amount, v[1] * amount)


def shift(point: Point, dx: float = 0.0, dy: float = 0.0) -> Point:
    return (point[0] + dx, point[1] + dy)


def rotate_offset(x: int, y: int, rotation: str) -> tuple[int, int]:
    return {
        "east": (-y, x),
        "south": (-x, -y),
        "west": (y, -x),
    }.get(rotation, (x, y))


def project_local(x: float, y: float) -> Point:
    return (
        ORIGIN_X + (x - y) * CELL_HALF_W,
        ORIGIN_Y + (x + y) * CELL_HALF_H,
    )


def build_layout(facility_id: str, rotation: str) -> FacilityLayout:
    definition = FACILITY_DATA[facility_id]
    footprint = definition["footprint"]
    anchor_x = footprint[0]["x"]
    anchor_y = footprint[0]["y"]

    rotated: list[tuple[int, int, int]] = []
    for index, cell in enumerate(footprint):
        rx, ry = rotate_offset(cell["x"] - anchor_x, cell["y"] - anchor_y, rotation)
        rotated.append((index, rx, ry))

    min_x = min(x for _, x, _ in rotated)
    max_x = max(x for _, x, _ in rotated)
    min_y = min(y for _, _, y in rotated)
    max_y = max(y for _, _, y in rotated)
    center_x = (min_x + max_x) * 0.5
    center_y = (min_y + max_y) * 0.5

    cells: list[RenderCell] = []
    for index, x, y in rotated:
        sx, sy = project_local(x - center_x, y - center_y)
        cells.append(RenderCell(index, x, y, sx, sy))

    cells.sort(key=lambda cell: (cell.grid_x + cell.grid_y, cell.screen_y, cell.screen_x))
    anchor = next(cell for cell in cells if cell.index == 0)
    return FacilityLayout(
        cells=cells,
        anchor=anchor,
        center_x=ORIGIN_X,
        center_y=ORIGIN_Y,
        min_x=min(cell.screen_x for cell in cells),
        max_x=max(cell.screen_x for cell in cells),
        min_y=min(cell.screen_y for cell in cells),
        max_y=max(cell.screen_y for cell in cells),
    )


def get_cell(layout: FacilityLayout, index: int) -> RenderCell:
    return next(cell for cell in layout.cells if cell.index == index)


def get_farthest_cell(layout: FacilityLayout) -> RenderCell:
    farthest = layout.anchor
    farthest_distance = -1.0
    for cell in layout.cells:
        distance = abs(cell.grid_x - layout.anchor.grid_x) + abs(cell.grid_y - layout.anchor.grid_y)
        if distance > farthest_distance:
            farthest = cell
            farthest_distance = distance
    return farthest


def prism_geometry(center: Point, height: float) -> tuple[list[Point], list[Point], list[Point]]:
    cx, cy = center
    top = (cx, cy - CELL_HALF_H)
    right = (cx + CELL_HALF_W, cy)
    bottom = (cx, cy + CELL_HALF_H)
    left = (cx - CELL_HALF_W, cy)
    lower_right = (right[0], right[1] + height)
    lower_bottom = (bottom[0], bottom[1] + height)
    lower_left = (left[0], left[1] + height)
    top_face = [top, right, bottom, left]
    left_face = [left, bottom, lower_bottom, lower_left]
    right_face = [bottom, right, lower_right, lower_bottom]
    return top_face, left_face, right_face


def draw_prism(
    draw: ImageDraw.ImageDraw,
    center: Point,
    height: float,
    top_fill: Color,
    left_fill: Color,
    right_fill: Color,
) -> None:
    top_face, left_face, right_face = prism_geometry(center, height)
    poly(draw, left_face, left_fill)
    poly(draw, right_face, right_fill)
    poly(draw, top_face, top_fill)


def draw_layout_shadow(draw: ImageDraw.ImageDraw, layout: FacilityLayout, extra_width: float = 6.0) -> None:
    ellipse(
        draw,
        (
            layout.min_x - CELL_HALF_W - extra_width,
            layout.max_y + BLOCK_HEIGHT + 5,
            layout.max_x + CELL_HALF_W + extra_width,
            layout.max_y + BLOCK_HEIGHT + 11,
        ),
        SHADOW,
        outline=None,
    )


def panel_points(
    center: Point,
    rotation: str,
    along: float,
    across: float,
    lift: float = 0.0,
) -> list[Point]:
    long_vec = LONG_AXIS[rotation]
    cross_vec = CROSS_AXIS[rotation]
    lifted_center = shift(center, dy=-lift)
    return [
        add(add(lifted_center, scale(long_vec, -along)), scale(cross_vec, -across)),
        add(add(lifted_center, scale(long_vec, along)), scale(cross_vec, -across)),
        add(add(lifted_center, scale(long_vec, along)), scale(cross_vec, across)),
        add(add(lifted_center, scale(long_vec, -along)), scale(cross_vec, across)),
    ]


def draw_panel(
    draw: ImageDraw.ImageDraw,
    center: Point,
    rotation: str,
    along: float,
    across: float,
    fill: Color,
    outline: Color = OUTLINE,
    lift: float = 0.0,
) -> list[Point]:
    points = panel_points(center, rotation, along, across, lift=lift)
    poly(draw, points, fill, outline)
    return points


def draw_post(
    draw: ImageDraw.ImageDraw,
    base: Point,
    height: float,
    fill: Color,
    width: int = 2,
) -> None:
    x, y = base
    rect(draw, (x - width * 0.5, y - height, x + width * 0.5, y), fill, OUTLINE)


def draw_flame(draw: ImageDraw.ImageDraw, center: Point, size: float = 1.0) -> None:
    x, y = center
    poly(
        draw,
        [
            (x - 2 * size, y + 5 * size),
            (x, y - 4 * size),
            (x + 2.5 * size, y + 5 * size),
            (x, y + 8 * size),
        ],
        FIRE_RED,
        RED_DARK,
    )
    poly(
        draw,
        [
            (x - 1.0 * size, y + 5 * size),
            (x, y - 1.5 * size),
            (x + 1.2 * size, y + 5 * size),
            (x, y + 6 * size),
        ],
        FIRE_YELLOW,
        EMBER,
    )


def draw_bottle(draw: ImageDraw.ImageDraw, center: Point, fill: Color) -> None:
    x, y = center
    rect(draw, (x - 1, y - 4, x + 1, y), fill, WHITE)
    rect(draw, (x - 0.5, y - 5, x + 0.5, y - 4), WHITE, OUTLINE)


def draw_crate(draw: ImageDraw.ImageDraw, center: Point, size: float = 4.0) -> None:
    x, y = center
    rect(draw, (x - size, y - size, x + size, y + size), WOOD, WOOD_DARK)
    line(draw, (x - size + 1, y - size + 1, x + size - 1, y + size - 1), WOOD_LIGHT)
    line(draw, (x - size + 1, y + size - 1, x + size - 1, y - size + 1), WOOD_LIGHT)


def draw_worktable(
    draw: ImageDraw.ImageDraw,
    layout: FacilityLayout,
    rotation: str,
    top_fill: Color,
    accent: Callable[[ImageDraw.ImageDraw], None] | None = None,
) -> None:
    draw_layout_shadow(draw, layout)
    panel = draw_panel(draw, shift(layout.center, dy=2), rotation, 0.95, 0.42, top_fill, lift=4)
    for base in (panel[2], panel[3]):
        draw_post(draw, shift(base, dy=10), 10, WOOD_DARK)
    if accent is not None:
        accent(draw)


def draw_shelf(draw: ImageDraw.ImageDraw, layout: FacilityLayout, rotation: str) -> None:
    draw_layout_shadow(draw, layout)
    draw_prism(draw, layout.anchor_screen, 8, WOOD_LIGHT, WOOD_DARK, WOOD)
    left_post = shift(layout.anchor_screen, dx=-6, dy=-1)
    right_post = shift(layout.anchor_screen, dx=6, dy=-1)
    draw_post(draw, shift(left_post, dy=4), 18, WOOD_DARK)
    draw_post(draw, shift(right_post, dy=4), 18, WOOD_DARK)
    for shelf_y in (-10, -5, 0):
        line(draw, [(left_post[0], left_post[1] + shelf_y), (right_post[0], right_post[1] + shelf_y)], WOOD_LIGHT)
    draw_crate(draw, shift(layout.anchor_screen, dx=-2, dy=-5), 3)
    draw_bottle(draw, shift(layout.anchor_screen, dx=4, dy=-10), GLASS)
    draw_bottle(draw, shift(layout.anchor_screen, dx=8, dy=-10), BLUE)
    bag_shift = {"north": (-11, -2), "east": (-9, 2), "south": (11, 2), "west": (10, -2)}[rotation]
    rect(
        draw,
        (
            layout.anchor_screen[0] + bag_shift[0] - 2,
            layout.anchor_screen[1] + bag_shift[1] - 5,
            layout.anchor_screen[0] + bag_shift[0] + 2,
            layout.anchor_screen[1] + bag_shift[1] + 4,
        ),
        LEATHER,
        LEATHER_DARK,
    )


def draw_brazier(draw: ImageDraw.ImageDraw, layout: FacilityLayout, rotation: str) -> None:
    del rotation
    draw_layout_shadow(draw, layout)
    draw_prism(draw, layout.anchor_screen, 5, STONE_LIGHT, STONE_DARK, STONE)
    bowl = draw_panel(draw, shift(layout.anchor_screen, dy=-1), "north", 0.36, 0.32, STONE, lift=5)
    line(draw, [bowl[0], bowl[1]], STONE_DARK)
    draw_flame(draw, shift(layout.anchor_screen, dx=-2, dy=-8), 1.0)
    draw_flame(draw, shift(layout.anchor_screen, dx=3, dy=-7), 0.9)
    draw_flame(draw, shift(layout.anchor_screen, dx=0, dy=-11), 1.05)


def draw_bed(draw: ImageDraw.ImageDraw, layout: FacilityLayout, rotation: str) -> None:
    draw_layout_shadow(draw, layout, extra_width=8)
    far = get_farthest_cell(layout)
    for cell in layout.cells:
        draw_prism(draw, (cell.screen_x, cell.screen_y), 4, WOOD_LIGHT, WOOD_DARK, WOOD)
    draw_panel(draw, shift(layout.center, dy=1), rotation, 0.98, 0.40, CLOTH, lift=5)
    draw_panel(draw, shift((layout.anchor.screen_x, layout.anchor.screen_y), dy=-1), rotation, 0.26, 0.28, WHITE, lift=6)
    draw_panel(draw, shift((far.screen_x, far.screen_y), dy=1), rotation, 0.26, 0.34, RED, lift=5)
    head_panel = panel_points((layout.anchor.screen_x, layout.anchor.screen_y), rotation, 0.46, 0.44, lift=8)
    for base in (head_panel[0], head_panel[3]):
        draw_post(draw, shift(base, dy=2), 8, WOOD_DARK)


def draw_dormitory_bed(draw: ImageDraw.ImageDraw, layout: FacilityLayout, rotation: str) -> None:
    draw_layout_shadow(draw, layout)
    draw_prism(draw, layout.anchor_screen, 3, IRON, IRON_DARK, IRON)
    draw_panel(draw, shift(layout.anchor_screen, dy=1), rotation, 0.40, 0.28, CLOTH, lift=4)
    draw_panel(draw, shift(layout.anchor_screen, dy=-1), rotation, 0.18, 0.18, WHITE, lift=5)
    head_panel = panel_points(layout.anchor_screen, rotation, 0.34, 0.30, lift=8)
    for base in (head_panel[0], head_panel[3]):
        draw_post(draw, shift(base, dy=1), 7, IRON_DARK)


def draw_stove(draw: ImageDraw.ImageDraw, layout: FacilityLayout, rotation: str) -> None:
    draw_layout_shadow(draw, layout, extra_width=8)
    far = get_farthest_cell(layout)
    for cell in layout.cells:
        draw_prism(draw, (cell.screen_x, cell.screen_y), 8, STONE_LIGHT, STONE_DARK, STONE)
    draw_panel(draw, shift(layout.center, dy=1), rotation, 0.92, 0.34, STONE, lift=8)
    draw_panel(draw, shift(layout.anchor_screen, dx=0, dy=0), rotation, 0.24, 0.18, EMBER, lift=9)
    chimney_base = shift((far.screen_x, far.screen_y), dx=3 if rotation in {"north", "west"} else -3, dy=-2)
    rect(draw, (chimney_base[0] - 2, chimney_base[1] - 18, chimney_base[0] + 2, chimney_base[1] + 2), STONE_DARK)
    rect(draw, (chimney_base[0] - 1, chimney_base[1] - 20, chimney_base[0] + 1, chimney_base[1] - 18), STONE_LIGHT)
    handle = shift(layout.anchor_screen, dx=-9 if rotation in {"north", "west"} else 9, dy=-2)
    line(draw, (handle[0] - 2, handle[1] - 2, handle[0] + 2, handle[1] + 1), WOOD_DARK, 2)


def draw_butcher_table(draw: ImageDraw.ImageDraw, layout: FacilityLayout, rotation: str) -> None:
    def accent(canvas: ImageDraw.ImageDraw) -> None:
        draw_panel(canvas, shift(layout.center, dx=-3, dy=-2), rotation, 0.22, 0.14, MEAT, lift=6)
        knife_base = shift(layout.center, dx=9 if rotation in {"north", "west"} else -9, dy=-9)
        line(canvas, (knife_base[0] - 4, knife_base[1] + 6, knife_base[0] + 3, knife_base[1]), STEEL_DARK, 2)
        poly(canvas, [(knife_base[0] + 3, knife_base[1]), (knife_base[0] + 8, knife_base[1] + 2), (knife_base[0] + 2, knife_base[1] + 5)], STEEL, STEEL_DARK)

    draw_worktable(draw, layout, rotation, WOOD_LIGHT, accent)


def draw_smithy(draw: ImageDraw.ImageDraw, layout: FacilityLayout, rotation: str) -> None:
    draw_layout_shadow(draw, layout, extra_width=8)
    far = get_farthest_cell(layout)
    for cell in layout.cells:
        draw_prism(draw, (cell.screen_x, cell.screen_y), 8, STONE, STONE_DARK, STONE_DARK)
    draw_panel(draw, shift(layout.center, dy=1), rotation, 0.88, 0.30, EMBER, lift=8)
    chimney_base = shift((far.screen_x, far.screen_y), dx=4 if rotation in {"north", "west"} else -4, dy=-3)
    rect(draw, (chimney_base[0] - 2, chimney_base[1] - 20, chimney_base[0] + 2, chimney_base[1] + 2), STONE_DARK)
    rect(draw, (chimney_base[0] - 1, chimney_base[1] - 22, chimney_base[0] + 1, chimney_base[1] - 20), STONE_LIGHT)
    anvil = shift(layout.anchor_screen, dx=-10 if rotation in {"north", "west"} else 10, dy=-1)
    poly(draw, [(anvil[0] - 4, anvil[1]), (anvil[0] + 4, anvil[1]), (anvil[0] + 1, anvil[1] + 3), (anvil[0] - 2, anvil[1] + 3)], STEEL_DARK)
    rect(draw, (anvil[0] - 2, anvil[1] - 3, anvil[0] + 2, anvil[1]), STEEL, STEEL_DARK)
    hammer = shift(layout.center, dx=8 if rotation in {"north", "west"} else -8, dy=-10)
    line(draw, (hammer[0] - 1, hammer[1] + 5, hammer[0] + 3, hammer[1]), WOOD_DARK, 2)
    rect(draw, (hammer[0] - 4, hammer[1] - 1, hammer[0] + 1, hammer[1] + 1), STEEL, STEEL_DARK)


def draw_loom(draw: ImageDraw.ImageDraw, layout: FacilityLayout, rotation: str) -> None:
    draw_layout_shadow(draw, layout, extra_width=7)
    frame = draw_panel(draw, shift(layout.center, dy=1), rotation, 0.95, 0.34, WOOD_LIGHT, lift=3)
    for base in frame:
        draw_post(draw, shift(base, dy=12), 19 if base in (frame[0], frame[1]) else 14, WOOD_DARK)
    top_bar = [shift(point, dy=-18) for point in frame[:2]]
    line(draw, top_bar, WOOD)
    cloth = panel_points(shift(layout.center, dy=-8), rotation, 0.50, 0.30, lift=8)
    poly(draw, cloth, PURPLE, PURPLE_DARK)
    line(draw, [shift(cloth[0], dx=1, dy=1), shift(cloth[2], dx=-1, dy=-1)], ROPE)
    line(draw, [shift(cloth[1], dx=-1, dy=1), shift(cloth[3], dx=1, dy=-1)], ROPE)


def draw_herbal_bench(draw: ImageDraw.ImageDraw, layout: FacilityLayout, rotation: str) -> None:
    def accent(canvas: ImageDraw.ImageDraw) -> None:
        draw_bottle(canvas, shift(layout.center, dx=-7, dy=-8), GLASS)
        draw_bottle(canvas, shift(layout.center, dx=-2, dy=-9), BLUE)
        poly(
            canvas,
            [
                shift(layout.center, dx=1, dy=-1),
                shift(layout.center, dx=6, dy=-4),
                shift(layout.center, dx=11, dy=0),
                shift(layout.center, dx=5, dy=3),
            ],
            HERB,
            HERB_DARK,
        )
        ellipse(canvas, (layout.center_x + 7, layout.center_y - 6, layout.center_x + 13, layout.center_y - 1), STONE_LIGHT, STONE_DARK)

    draw_worktable(draw, layout, rotation, WOOD_LIGHT, accent)


def draw_market_stall(draw: ImageDraw.ImageDraw, layout: FacilityLayout, rotation: str) -> None:
    draw_layout_shadow(draw, layout, extra_width=12)
    for cell in layout.cells:
        draw_prism(draw, (cell.screen_x, cell.screen_y), 6, WOOD_LIGHT, WOOD_DARK, WOOD)
    counter = draw_panel(draw, shift(layout.center, dy=2), rotation, 0.98, 0.98, WOOD, lift=7)
    post_bases = [
        shift(counter[0], dy=7),
        shift(counter[1], dy=7),
        shift(counter[2], dy=7),
        shift(counter[3], dy=7),
    ]
    for base in post_bases:
        draw_post(draw, shift(base, dy=12), 24, WOOD_DARK)
    canopy = draw_panel(draw, shift(layout.center, dy=-18), rotation, 1.08, 1.10, RED, RED_DARK, lift=6)
    line(draw, [canopy[0], canopy[1]], CANVAS, 2)
    line(draw, [shift(canopy[0], dx=4, dy=2), shift(canopy[1], dx=-4, dy=2)], CANVAS, 2)
    line(draw, [shift(canopy[0], dx=8, dy=4), shift(canopy[1], dx=-8, dy=4)], CANVAS, 2)
    rect(draw, (layout.center_x - 10, layout.center_y - 1, layout.center_x - 4, layout.center_y + 5), GLASS, WHITE)
    rect(draw, (layout.center_x - 1, layout.center_y - 1, layout.center_x + 5, layout.center_y + 5), PURPLE, PURPLE_DARK)
    rect(draw, (layout.center_x + 8, layout.center_y - 1, layout.center_x + 15, layout.center_y + 5), GREEN, GREEN_DARK)
    draw_crate(draw, shift((layout.min_x, layout.max_y), dx=-5, dy=9), 3)
    draw_crate(draw, shift((layout.max_x, layout.max_y), dx=5, dy=9), 3)
    sign_base = shift((layout.center_x, layout.center_y), dx=11 if rotation in {"north", "south"} else -11, dy=-23)
    draw_post(draw, shift(sign_base, dy=6), 8, WOOD_DARK, width=1)
    rect(draw, (sign_base[0] - 3, sign_base[1] - 2, sign_base[0] + 3, sign_base[1] + 2), CANVAS_DARK)


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
        layout = build_layout(spec.facility_id, rotation)
        spec.drawer(draw, layout, rotation)
        frame = upscale(low_res)
        sheet.alpha_composite(frame, (0, index * FRAME_SIZE[1]))
    return sheet


def build_preview(sheets: list[tuple[FacilitySpec, Image.Image]]) -> Image.Image:
    preview = Image.new("RGBA", PREVIEW_SIZE, (25, 27, 34, 255))
    for index, (spec, sheet) in enumerate(sheets):
        row = index // PREVIEW_COLUMNS
        col = index % PREVIEW_COLUMNS
        x = col * FRAME_SIZE[0]
        y = row * FRAME_SIZE[1]
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
