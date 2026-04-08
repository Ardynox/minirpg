from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
TILESET_ROOT = ROOT / "Assets" / "Art" / "Tilesets" / "FantasyKingdom" / "FantasyKingdomTileset_Godot" / "Characters"
OUTPUT_ROOT = ROOT / "Assets" / "Art" / "Placeholders" / "npc_map"
ARTIFACTS_ROOT = ROOT / "Artifacts"

CELL_SIZE = 128
FRAME_COL = 2
FRAME_ROW = 2
LOGICAL_SIZE = (40, 44)
SCALE = 5
CANVAS_SIZE = (256, 256)
FOOT_Y = 224


TRANSPARENT = (0, 0, 0, 0)
OUTLINE = (42, 27, 18, 255)
LEATHER = (112, 72, 44, 255)
LEATHER_DARK = (76, 47, 28, 255)
WHITE = (235, 230, 220, 255)
CREAM = (221, 212, 180, 255)
GRAY = (132, 136, 145, 255)
GRAY_DARK = (90, 92, 102, 255)
BLUE = (55, 75, 140, 255)
BLUE_DARK = (34, 44, 86, 255)
GREEN = (66, 117, 64, 255)
GREEN_DARK = (40, 73, 39, 255)
RED = (157, 74, 56, 255)
GOLD = (205, 170, 80, 255)
SKIN = (198, 154, 120, 255)
PINK = (200, 110, 110, 255)
BLACK = (24, 24, 28, 255)
PURPLE = (110, 82, 140, 255)
CYAN = (110, 196, 212, 255)
YELLOW = (215, 196, 76, 255)
ORANGE = (194, 118, 50, 255)


@dataclass(frozen=True)
class RoleSpec:
    output_name: str
    source_sheet: str
    draw_role: str


ROLE_SPECS = [
    RoleSpec("npc_merchant.png", "NPC1", "merchant"),
    RoleSpec("npc_elder.png", "NPC1", "elder"),
    RoleSpec("npc_villager.png", "NPC2", "villager"),
    RoleSpec("npc_blacksmith_npc.png", "NPC2", "blacksmith"),
    RoleSpec("npc_herbalist_npc.png", "NPC1", "herbalist"),
    RoleSpec("npc_cook_npc.png", "NPC1", "cook"),
    RoleSpec("npc_guard_npc.png", "NPC3", "guard"),
    RoleSpec("npc_tailor_npc.png", "NPC2", "tailor"),
]


def sprite_bbox(image: Image.Image) -> tuple[int, int, int, int]:
    alpha = image.getchannel("A")
    bbox = alpha.getbbox()
    if bbox is None:
        raise ValueError("sprite frame is empty")
    return bbox


def extract_base_sprite(sheet_name: str) -> tuple[Image.Image, tuple[int, int]]:
    sheet = Image.open(TILESET_ROOT / sheet_name / "Idle.png").convert("RGBA")
    frame = sheet.crop(
        (
            FRAME_COL * CELL_SIZE,
            FRAME_ROW * CELL_SIZE,
            (FRAME_COL + 1) * CELL_SIZE,
            (FRAME_ROW + 1) * CELL_SIZE,
        )
    )
    bbox = sprite_bbox(frame)
    cropped = frame.crop(bbox)
    x = (LOGICAL_SIZE[0] - cropped.width) // 2
    y = LOGICAL_SIZE[1] - cropped.height
    return cropped, (x, y)


def new_layer() -> Image.Image:
    return Image.new("RGBA", LOGICAL_SIZE, TRANSPARENT)


def rect(draw: ImageDraw.ImageDraw, xy: tuple[int, int, int, int], fill, outline=OUTLINE) -> None:
    draw.rectangle(xy, fill=fill, outline=outline)


def ellipse(draw: ImageDraw.ImageDraw, xy: tuple[int, int, int, int], fill, outline=OUTLINE) -> None:
    draw.ellipse(xy, fill=fill, outline=outline)


def line(draw: ImageDraw.ImageDraw, points, fill, width=1) -> None:
    draw.line(points, fill=fill, width=width)


def draw_gold_trim(draw: ImageDraw.ImageDraw, x: int, top: int, bottom: int) -> None:
    line(draw, (x, top, x, bottom), GOLD)
    draw.point((x - 1, top + 4), GOLD)
    draw.point((x + 1, top + 8), GOLD)


def draw_hat(draw: ImageDraw.ImageDraw, brim_xy, crown_xy, brim_fill=LEATHER, crown_fill=LEATHER_DARK) -> None:
    rect(draw, brim_xy, brim_fill, outline=OUTLINE)
    rect(draw, crown_xy, crown_fill, outline=OUTLINE)


def draw_staff(draw: ImageDraw.ImageDraw, x: int, y1: int, y2: int, glow=False) -> None:
    line(draw, (x, y1, x + 1, y2), LEATHER_DARK)
    if glow:
        ellipse(draw, (x - 1, y1 - 2, x + 2, y1 + 1), CYAN, outline=WHITE)


def draw_spear(draw: ImageDraw.ImageDraw, x: int, y1: int, y2: int) -> None:
    line(draw, (x, y1, x, y2), LEATHER_DARK)
    draw.polygon([(x - 1, y1), (x + 1, y1), (x, y1 - 3)], fill=GRAY, outline=WHITE)


def draw_bundle(draw: ImageDraw.ImageDraw, x: int, y: int, stalks=4) -> None:
    for i in range(stalks):
        line(draw, (x + i, y + 3, x + i, y), YELLOW)
    line(draw, (x, y + 3, x + stalks - 1, y + 3), LEATHER_DARK)


def draw_basket(draw: ImageDraw.ImageDraw, x: int, y: int) -> None:
    rect(draw, (x, y, x + 5, y + 4), LEATHER, outline=LEATHER_DARK)
    line(draw, (x + 1, y, x + 4, y), CREAM)


def draw_bottles(draw: ImageDraw.ImageDraw, x: int, y: int) -> None:
    for offset, color in ((0, CYAN), (2, RED), (4, GREEN)):
        rect(draw, (x + offset, y, x + offset + 1, y + 2), color, outline=WHITE)


def draw_scissors(draw: ImageDraw.ImageDraw, x: int, y: int) -> None:
    ellipse(draw, (x, y + 1, x + 1, y + 2), TRANSPARENT, outline=GRAY)
    ellipse(draw, (x + 2, y + 1, x + 3, y + 2), TRANSPARENT, outline=GRAY)
    line(draw, (x + 1, y + 1, x + 4, y - 2), GRAY)
    line(draw, (x + 2, y + 1, x + 5, y - 1), GRAY)


def draw_ladle(draw: ImageDraw.ImageDraw, x: int, y: int) -> None:
    line(draw, (x, y, x + 4, y + 6), LEATHER_DARK)
    ellipse(draw, (x - 2, y - 2, x + 1, y + 1), LEATHER, outline=LEATHER_DARK)


def merchant_layers(pos: tuple[int, int], sprite: Image.Image) -> tuple[Image.Image, Image.Image]:
    x, y = pos
    back = new_layer()
    front = new_layer()
    b = ImageDraw.Draw(back)
    f = ImageDraw.Draw(front)

    rect(b, (x - 5, y + 9, x + 2, y + 25), LEATHER, outline=LEATHER_DARK)
    rect(b, (x - 7, y + 12, x - 4, y + 16), CREAM, outline=LEATHER_DARK)
    ellipse(b, (x - 6, y + 18, x - 3, y + 21), GRAY, outline=LEATHER_DARK)
    rect(b, (x + sprite.width - 1, y + 12, x + sprite.width + 4, y + 18), LEATHER, outline=LEATHER_DARK)
    rect(b, (x + sprite.width + 2, y + 12, x + sprite.width + 5, y + 15), CREAM, outline=LEATHER_DARK)

    draw_hat(f, (x + 2, y - 1, x + sprite.width - 2, y + 1), (x + 7, y - 3, x + sprite.width - 7, y))
    rect(f, (x + 7, y + 10, x + sprite.width - 7, y + 21), LEATHER, outline=LEATHER_DARK)
    rect(f, (x + 10, y + 23, x + sprite.width - 10, y + 28), LEATHER_DARK, outline=LEATHER_DARK)
    rect(f, (x + sprite.width - 3, y + 21, x + sprite.width + 1, y + 25), GOLD, outline=LEATHER_DARK)
    return back, front


def elder_layers(pos: tuple[int, int], sprite: Image.Image) -> tuple[Image.Image, Image.Image]:
    x, y = pos
    back = new_layer()
    front = new_layer()
    b = ImageDraw.Draw(back)
    f = ImageDraw.Draw(front)

    draw_staff(b, x + sprite.width + 2, y + 4, y + sprite.height - 1, glow=True)
    b.polygon(
        [(x + 3, y + 6), (x + sprite.width - 3, y + 6), (x + sprite.width - 1, y + sprite.height - 1), (x + 1, y + sprite.height - 1)],
        fill=BLUE,
        outline=BLUE_DARK,
    )
    draw_gold_trim(b, x + sprite.width // 2, y + 10, y + sprite.height - 2)
    rect(f, (x + sprite.width // 2 - 2, y + 7, x + sprite.width // 2 + 2, y + 15), WHITE, outline=OUTLINE)
    return back, front


def villager_layers(pos: tuple[int, int], sprite: Image.Image) -> tuple[Image.Image, Image.Image]:
    x, y = pos
    back = new_layer()
    front = new_layer()
    f = ImageDraw.Draw(front)

    rect(f, (x + 8, y + 12, x + sprite.width - 8, y + 21), LEATHER, outline=LEATHER_DARK)
    line(f, (x + sprite.width // 2, y + 12, x + sprite.width // 2, y + 21), CREAM)
    rect(f, (x + 10, y + 22, x + sprite.width - 10, y + 28), LEATHER_DARK, outline=LEATHER_DARK)
    draw_basket(f, x - 6, y + 22)
    draw_bundle(f, x + sprite.width + 1, y + 18, stalks=5)
    return back, front


def blacksmith_layers(pos: tuple[int, int], sprite: Image.Image) -> tuple[Image.Image, Image.Image]:
    x, y = pos
    back = new_layer()
    front = new_layer()
    f = ImageDraw.Draw(front)

    rect(f, (x + 7, y + 11, x + sprite.width - 7, y + 28), LEATHER, outline=LEATHER_DARK)
    rect(f, (x + 10, y + 11, x + sprite.width - 10, y + 23), CREAM, outline=LEATHER_DARK)
    rect(f, (x + sprite.width // 2 - 1, y + 24, x + sprite.width // 2 + 1, y + 26), GRAY, outline=WHITE)
    rect(f, (x - 2, y + 14, x + 1, y + 17), SKIN, outline=OUTLINE)
    rect(f, (x + sprite.width - 1, y + 14, x + sprite.width + 2, y + 17), SKIN, outline=OUTLINE)
    rect(f, (x + sprite.width + 1, y + 14, x + sprite.width + 6, y + 18), GRAY_DARK, outline=WHITE)
    rect(f, (x + sprite.width + 3, y + 9, x + sprite.width + 4, y + 14), LEATHER_DARK, outline=OUTLINE)
    rect(f, (x + 8, y, x + sprite.width - 8, y + 1), BLACK, outline=BLACK)
    f.point((x + sprite.width // 2 - 2, y + 8), BLACK)
    f.point((x + sprite.width // 2 + 2, y + 9), BLACK)
    return back, front


def herbalist_layers(pos: tuple[int, int], sprite: Image.Image) -> tuple[Image.Image, Image.Image]:
    x, y = pos
    back = new_layer()
    front = new_layer()
    b = ImageDraw.Draw(back)
    f = ImageDraw.Draw(front)

    b.polygon(
        [(x + 2, y + 4), (x + sprite.width - 2, y + 4), (x + sprite.width + 1, y + sprite.height - 1), (x - 1, y + sprite.height - 1)],
        fill=GREEN,
        outline=GREEN_DARK,
    )
    b.polygon(
        [(x + 8, y - 1), (x + sprite.width - 8, y - 1), (x + sprite.width // 2 + 3, y + 8), (x + sprite.width // 2 - 3, y + 8)],
        fill=GREEN_DARK,
        outline=OUTLINE,
    )
    draw_bottles(f, x + 8, y + 24)
    rect(f, (x + sprite.width - 1, y + 18, x + sprite.width + 4, y + 24), LEATHER, outline=LEATHER_DARK)
    draw_bundle(f, x + sprite.width + 1, y + 13, stalks=3)
    return back, front


def cook_layers(pos: tuple[int, int], sprite: Image.Image) -> tuple[Image.Image, Image.Image]:
    x, y = pos
    back = new_layer()
    front = new_layer()
    f = ImageDraw.Draw(front)

    rect(f, (x + 7, y - 4, x + sprite.width - 7, y + 2), WHITE, outline=OUTLINE)
    rect(f, (x + 9, y - 8, x + sprite.width - 9, y - 4), WHITE, outline=OUTLINE)
    rect(f, (x + 4, y + 11, x + sprite.width - 4, y + 31), CREAM, outline=OUTLINE)
    rect(f, (x + sprite.width - 2, y + 14, x + sprite.width + 2, y + 16), WHITE, outline=OUTLINE)
    draw_ladle(f, x + sprite.width + 3, y + 17)
    f.point((x + sprite.width // 2 - 3, y + 10), PINK)
    f.point((x + sprite.width // 2 + 3, y + 10), PINK)
    return back, front


def guard_layers(pos: tuple[int, int], sprite: Image.Image) -> tuple[Image.Image, Image.Image]:
    x, y = pos
    back = new_layer()
    front = new_layer()
    b = ImageDraw.Draw(back)
    f = ImageDraw.Draw(front)

    ellipse(b, (x - 6, y + 13, x, y + 19), GRAY, outline=WHITE)
    draw_spear(f, x + sprite.width + 3, y - 2, y + sprite.height - 1)
    rect(f, (x + 7, y + 1, x + sprite.width - 7, y + 4), GRAY, outline=WHITE)
    rect(f, (x + 5, y + 10, x + sprite.width - 5, y + 22), GRAY, outline=GRAY_DARK)
    for check_x in range(x + 6, x + sprite.width - 4, 3):
        for check_y in range(y + 12, y + 22, 3):
            f.point((check_x, check_y), WHITE)
    rect(f, (x + sprite.width // 2 - 2, y + 15, x + sprite.width // 2 + 2, y + 31), RED, outline=OUTLINE)
    return back, front


def tailor_layers(pos: tuple[int, int], sprite: Image.Image) -> tuple[Image.Image, Image.Image]:
    x, y = pos
    back = new_layer()
    front = new_layer()
    f = ImageDraw.Draw(front)

    rect(f, (x + 6, y + 10, x + sprite.width - 6, y + 23), PURPLE, outline=OUTLINE)
    rect(f, (x + 8, y + 12, x + 10, y + 15), GOLD, outline=OUTLINE)
    rect(f, (x + 12, y + 16, x + 14, y + 19), GREEN, outline=OUTLINE)
    rect(f, (x + 16, y + 12, x + 18, y + 14), RED, outline=OUTLINE)
    line(f, (x + 9, y + 9, x + sprite.width - 9, y + 9), YELLOW)
    rect(f, (x - 5, y + 17, x, y + 22), RED, outline=OUTLINE)
    rect(f, (x - 7, y + 18, x - 5, y + 21), CREAM, outline=OUTLINE)
    draw_scissors(f, x + sprite.width + 1, y + 17)
    return back, front


DRAWERS = {
    "merchant": merchant_layers,
    "elder": elder_layers,
    "villager": villager_layers,
    "blacksmith": blacksmith_layers,
    "herbalist": herbalist_layers,
    "cook": cook_layers,
    "guard": guard_layers,
    "tailor": tailor_layers,
}


def compose_role(spec: RoleSpec) -> Image.Image:
    sprite, pos = extract_base_sprite(spec.source_sheet)
    back, front = DRAWERS[spec.draw_role](pos, sprite)

    low_res = new_layer()
    low_res.alpha_composite(back)
    low_res.alpha_composite(sprite, pos)
    low_res.alpha_composite(front)

    scaled = low_res.resize((LOGICAL_SIZE[0] * SCALE, LOGICAL_SIZE[1] * SCALE), Image.Resampling.NEAREST)
    final = Image.new("RGBA", CANVAS_SIZE, TRANSPARENT)
    x = (CANVAS_SIZE[0] - scaled.width) // 2
    y = FOOT_Y - scaled.height
    final.alpha_composite(scaled, (x, y))
    return final


def build_preview(images: list[tuple[str, Image.Image]]) -> Image.Image:
    cols = 4
    rows = 2
    preview = Image.new("RGBA", (cols * 256, rows * 256), (34, 34, 42, 255))
    for index, (_, image) in enumerate(images):
        x = (index % cols) * 256
        y = (index // cols) * 256
        preview.alpha_composite(image, (x, y))
    return preview


def main() -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    ARTIFACTS_ROOT.mkdir(parents=True, exist_ok=True)

    generated: list[tuple[str, Image.Image]] = []
    for spec in ROLE_SPECS:
        image = compose_role(spec)
        image.save(OUTPUT_ROOT / spec.output_name)
        generated.append((spec.output_name, image))

    build_preview(generated).save(ARTIFACTS_ROOT / "npc_map_preview_sheet.png")
    print(f"Generated {len(generated)} NPC map sprites in {OUTPUT_ROOT}")


if __name__ == "__main__":
    main()
