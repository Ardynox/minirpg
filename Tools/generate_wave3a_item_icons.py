from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


BASE = 32
SIZE = 128
ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = ROOT / "Assets" / "Art" / "Placeholders" / "item_icons"
PREVIEW_PATH = ROOT / "Artifacts" / "wave3a_item_icons_preview.png"

OUTLINE = (38, 24, 28, 255)
SHADOW = (0, 0, 0, 68)
WHITE = (245, 240, 235, 255)
STEEL = (163, 171, 186, 255)
STEEL_DARK = (94, 103, 118, 255)
STEEL_LIGHT = (219, 226, 236, 255)
IRON = (127, 133, 145, 255)
IRON_DARK = (74, 79, 90, 255)
WOOD = (147, 99, 65, 255)
WOOD_DARK = (88, 56, 35, 255)
LEATHER = (138, 93, 65, 255)
CLAY = (140, 98, 73, 255)
CLAY_DARK = (95, 61, 43, 255)
BRASS = (195, 158, 88, 255)
GOLD = (232, 194, 91, 255)
GREEN = (97, 148, 91, 255)
GREEN_DARK = (57, 92, 54, 255)
GREEN_BRIGHT = (120, 198, 98, 255)
RED = (180, 63, 67, 255)
RED_DARK = (114, 36, 40, 255)
ORANGE = (214, 137, 57, 255)
ORANGE_DARK = (146, 82, 33, 255)
YELLOW = (241, 209, 110, 255)
AMBER = (226, 173, 69, 255)
BREAD = (188, 133, 75, 255)
CHEESE = (235, 206, 103, 255)
APPLE = (183, 62, 48, 255)
HERB = (78, 152, 89, 255)
HERB_DARK = (41, 95, 51, 255)
MEAT = (177, 67, 73, 255)
MEAT_DARK = (121, 41, 46, 255)
BOARD = (154, 111, 74, 255)
BOARD_DARK = (99, 68, 44, 255)
GLASS = (236, 243, 250, 220)
GLASS_EDGE = (189, 202, 214, 255)
FLAME_OUTER = (235, 133, 43, 255)
FLAME_INNER = (255, 220, 118, 255)
GLOW = (255, 214, 114, 180)
TWINE = (181, 153, 96, 255)

ORDER = [
    "item_potion_hp.png",
    "item_potion_str.png",
    "item_herbal_medicine.png",
    "item_antidote.png",
    "item_bandage.png",
    "item_torch.png",
    "item_lantern.png",
    "item_sword_iron.png",
    "item_sword_steel.png",
    "item_shield_iron.png",
    "item_bow_short.png",
    "item_pickaxe_steel.png",
    "item_wood_axe.png",
    "item_war_hammer.png",
    "item_meal_simple.png",
    "item_raw_meat.png",
    "item_mat_iron.png",
    "item_mat_herb.png",
]


def new_canvas() -> tuple[Image.Image, ImageDraw.ImageDraw]:
    image = Image.new("RGBA", (BASE, BASE), (0, 0, 0, 0))
    return image, ImageDraw.Draw(image)


def shadow(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int]) -> None:
    draw.ellipse(box, fill=SHADOW)


def add_highlight(draw: ImageDraw.ImageDraw, points: list[tuple[int, int]], fill: tuple[int, int, int, int] = STEEL_LIGHT) -> None:
    draw.line(points, fill=fill, width=1)


def save(image: Image.Image, filename: str) -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    fit_icon(image).save(OUT_DIR / filename)


def fit_icon(image: Image.Image, target_box: int = 104) -> Image.Image:
    upscaled = image.resize((SIZE, SIZE), Image.Resampling.NEAREST)
    alpha = upscaled.getchannel("A")
    bbox = alpha.getbbox()
    if bbox is None:
        return upscaled

    cropped = upscaled.crop(bbox)
    width, height = cropped.size
    scale = min(target_box / width, target_box / height)
    scaled = cropped.resize(
        (max(1, round(width * scale)), max(1, round(height * scale))),
        Image.Resampling.NEAREST,
    )

    result = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    left = (SIZE - scaled.width) // 2
    top = (SIZE - scaled.height) // 2
    result.alpha_composite(scaled, (left, top))
    return result


def potion_hp() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (10, 24, 22, 27))
    draw.rectangle((14, 4, 18, 7), fill=WOOD, outline=OUTLINE)
    draw.rectangle((13, 7, 19, 10), fill=GLASS, outline=GLASS_EDGE)
    draw.ellipse((9, 9, 23, 24), fill=GLASS, outline=GLASS_EDGE)
    draw.pieslice((10, 13, 22, 24), start=0, end=180, fill=RED_DARK, outline=RED_DARK)
    draw.rectangle((10, 16, 22, 21), fill=RED, outline=RED_DARK)
    add_highlight(draw, [(12, 11), (12, 19), (13, 21)], fill=WHITE)
    add_highlight(draw, [(15, 8), (17, 8)], fill=WHITE)
    return image


def potion_str() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (9, 24, 23, 27))
    draw.rectangle((14, 4, 18, 7), fill=WOOD, outline=OUTLINE)
    body = [(12, 8), (20, 8), (23, 16), (18, 24), (14, 24), (9, 16)]
    fill = [(13, 9), (19, 9), (21, 15), (17, 22), (15, 22), (11, 15)]
    draw.polygon(body, fill=GLASS, outline=GLASS_EDGE)
    draw.polygon(fill, fill=ORANGE, outline=ORANGE_DARK)
    draw.polygon([(15, 10), (18, 10), (19, 13), (16, 20), (14, 20), (12, 13)], fill=YELLOW)
    add_highlight(draw, [(13, 10), (12, 14), (13, 18)], fill=WHITE)
    return image


def herbal_medicine() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (8, 24, 24, 27))
    draw.rectangle((10, 12, 22, 23), fill=CLAY, outline=OUTLINE)
    draw.rectangle((11, 14, 21, 22), fill=CLAY_DARK)
    draw.rectangle((9, 10, 23, 14), fill=GREEN, outline=OUTLINE)
    draw.polygon([(12, 8), (16, 5), (20, 8), (18, 11), (14, 11)], fill=HERB, outline=OUTLINE)
    draw.line((15, 7, 18, 10), fill=HERB_DARK, width=1)
    add_highlight(draw, [(11, 11), (15, 11)], fill=(162, 214, 156, 255))
    return image


def antidote() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (11, 24, 21, 27))
    draw.rectangle((14, 4, 18, 7), fill=WOOD, outline=OUTLINE)
    body = [(16, 8), (21, 15), (17, 24), (15, 24), (11, 15)]
    liquid = [(16, 12), (19, 16), (17, 22), (15, 22), (13, 16)]
    draw.polygon(body, fill=GLASS, outline=GLASS_EDGE)
    draw.polygon(liquid, fill=GREEN_BRIGHT, outline=GREEN_DARK)
    add_highlight(draw, [(15, 11), (14, 16), (15, 20)], fill=WHITE)
    return image


def bandage() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (8, 24, 24, 27))
    draw.rounded_rectangle((8, 12, 24, 22), radius=4, fill=WHITE, outline=OUTLINE)
    for x in (11, 15, 19):
        draw.line((x, 13, x - 2, 21), fill=(214, 208, 198, 255), width=1)
    draw.rectangle((14, 15, 18, 19), fill=RED, outline=RED_DARK)
    draw.rectangle((15, 14, 17, 20), fill=RED, outline=RED_DARK)
    add_highlight(draw, [(10, 13), (14, 13)], fill=(255, 255, 255, 255))
    return image


def torch() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (11, 25, 22, 28))
    draw.polygon([(15, 9), (18, 10), (19, 25), (16, 26), (13, 11)], fill=WOOD, outline=OUTLINE)
    draw.line((14, 12, 18, 12), fill=TWINE, width=1)
    draw.polygon([(16, 3), (20, 8), (19, 13), (16, 15), (13, 12), (12, 7)], fill=FLAME_OUTER, outline=OUTLINE)
    draw.polygon([(16, 6), (18, 9), (17, 12), (15, 12), (14, 9)], fill=FLAME_INNER, outline=ORANGE_DARK)
    return image


def lantern() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (9, 24, 23, 27))
    draw.arc((13, 4, 19, 10), start=180, end=360, fill=OUTLINE, width=1)
    draw.rectangle((14, 8, 18, 10), fill=BRASS, outline=OUTLINE)
    draw.polygon([(11, 10), (21, 10), (23, 15), (19, 24), (13, 24), (9, 15)], fill=(72, 76, 88, 255), outline=OUTLINE)
    draw.polygon([(13, 12), (19, 12), (20, 15), (18, 21), (14, 21), (12, 15)], fill=GLOW, outline=(214, 176, 85, 255))
    draw.line((16, 12, 16, 22), fill=OUTLINE, width=1)
    draw.line((13, 15, 19, 15), fill=OUTLINE, width=1)
    return image


def sword_iron() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (8, 24, 24, 27))
    draw.polygon([(8, 23), (11, 26), (24, 13), (21, 10)], fill=IRON, outline=OUTLINE)
    add_highlight(draw, [(10, 23), (21, 12)], fill=STEEL_LIGHT)
    draw.polygon([(17, 14), (19, 16), (16, 19), (13, 16)], fill=BRASS, outline=OUTLINE)
    draw.polygon([(12, 18), (14, 20), (10, 24), (8, 22)], fill=WOOD, outline=OUTLINE)
    draw.line((10, 21, 13, 24), fill=LEATHER, width=1)
    return image


def sword_steel() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (8, 24, 24, 27))
    draw.polygon([(8, 23), (11, 26), (24, 13), (21, 10)], fill=STEEL, outline=OUTLINE)
    add_highlight(draw, [(10, 22), (22, 11)], fill=WHITE)
    guard = [(16, 13), (20, 15), (17, 18), (15, 16), (12, 18), (13, 14)]
    draw.polygon(guard, fill=BRASS, outline=OUTLINE)
    draw.ellipse((15, 14, 17, 16), fill=GOLD, outline=OUTLINE)
    draw.polygon([(12, 18), (14, 20), (10, 24), (8, 22)], fill=WOOD_DARK, outline=OUTLINE)
    add_highlight(draw, [(11, 19), (13, 21)], fill=(191, 142, 92, 255))
    return image


def shield_iron() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (8, 24, 24, 27))
    draw.ellipse((8, 7, 24, 23), fill=IRON, outline=OUTLINE)
    draw.ellipse((10, 9, 22, 21), fill=IRON_DARK, outline=(108, 114, 126, 255))
    draw.ellipse((14, 13, 18, 17), fill=STEEL_LIGHT, outline=OUTLINE)
    for box in ((11, 10, 13, 12), (19, 10, 21, 12), (11, 18, 13, 20), (19, 18, 21, 20)):
        draw.ellipse(box, fill=BRASS, outline=OUTLINE)
    draw.line((16, 11, 16, 20), fill=(171, 176, 187, 255), width=1)
    draw.line((12, 16, 20, 16), fill=(171, 176, 187, 255), width=1)
    add_highlight(draw, [(11, 10), (14, 9), (17, 9)], fill=STEEL_LIGHT)
    return image


def bow_short() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (10, 24, 22, 27))
    bow_color = (154, 111, 67, 255)
    draw.line((10, 24, 15, 16), fill=bow_color, width=2)
    draw.line((15, 16, 18, 10), fill=bow_color, width=2)
    draw.line((18, 10, 22, 6), fill=bow_color, width=2)
    draw.line((10, 24, 12, 21), fill=WOOD_DARK, width=1)
    draw.line((22, 6, 10, 24), fill=WHITE, width=1)
    draw.line((14, 18, 17, 15), fill=TWINE, width=1)
    return image


def pickaxe_steel() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (8, 24, 24, 27))
    draw.polygon([(9, 24), (11, 26), (22, 15), (20, 13)], fill=WOOD, outline=OUTLINE)
    head = [(17, 10), (24, 11), (20, 15), (14, 14)]
    spike = [(16, 11), (12, 8), (14, 13)]
    draw.polygon(head, fill=STEEL, outline=OUTLINE)
    draw.polygon(spike, fill=STEEL_DARK, outline=OUTLINE)
    add_highlight(draw, [(17, 11), (21, 12)], fill=WHITE)
    return image


def wood_axe() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (8, 24, 24, 27))
    draw.polygon([(9, 24), (11, 26), (22, 15), (20, 13)], fill=WOOD, outline=OUTLINE)
    blade = [(17, 10), (23, 12), (22, 18), (16, 16), (14, 13)]
    draw.polygon(blade, fill=STEEL, outline=OUTLINE)
    draw.polygon([(16, 12), (20, 13), (19, 16), (16, 15)], fill=STEEL_LIGHT, outline=STEEL_DARK)
    return image


def war_hammer() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (7, 24, 25, 27))
    draw.polygon([(8, 24), (10, 26), (22, 14), (20, 12)], fill=WOOD_DARK, outline=OUTLINE)
    draw.rectangle((16, 9, 23, 15), fill=STEEL, outline=OUTLINE)
    draw.rectangle((13, 11, 17, 13), fill=STEEL_DARK, outline=OUTLINE)
    draw.rectangle((17, 10, 22, 11), fill=STEEL_LIGHT)
    draw.line((10, 22, 12, 24), fill=LEATHER, width=1)
    return image


def meal_simple() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (7, 24, 25, 28))
    draw.ellipse((7, 14, 25, 24), fill=BOARD, outline=OUTLINE)
    draw.ellipse((9, 15, 23, 22), fill=(186, 136, 88, 255), outline=(121, 82, 54, 255))
    draw.polygon([(11, 16), (15, 14), (18, 17), (16, 20), (11, 20), (9, 18)], fill=BREAD, outline=OUTLINE)
    draw.polygon([(17, 16), (21, 15), (22, 19), (18, 20)], fill=CHEESE, outline=OUTLINE)
    draw.ellipse((18, 17, 22, 21), fill=APPLE, outline=OUTLINE)
    draw.line((20, 16, 19, 17), fill=GREEN_DARK, width=1)
    add_highlight(draw, [(11, 16), (14, 15)], fill=(228, 182, 118, 255))
    return image


def raw_meat() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (8, 24, 24, 28))
    draw.polygon([(8, 15), (23, 15), (25, 24), (10, 24)], fill=BOARD, outline=OUTLINE)
    draw.line((11, 17, 22, 17), fill=BOARD_DARK, width=1)
    meat = [(11, 13), (18, 11), (23, 15), (20, 21), (13, 22), (9, 18)]
    draw.polygon(meat, fill=MEAT, outline=OUTLINE)
    draw.polygon([(13, 14), (18, 13), (20, 15), (18, 19), (13, 20), (11, 17)], fill=MEAT_DARK, outline=(151, 54, 60, 255))
    add_highlight(draw, [(13, 14), (16, 13)], fill=(221, 109, 114, 255))
    return image


def mat_iron() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (8, 24, 24, 27))
    rock = [(11, 10), (20, 8), (24, 14), (22, 22), (14, 24), (8, 17)]
    draw.polygon(rock, fill=IRON, outline=OUTLINE)
    draw.polygon([(13, 11), (18, 10), (21, 13), (19, 17), (14, 18), (11, 15)], fill=IRON_DARK, outline=(102, 108, 120, 255))
    draw.polygon([(18, 11), (21, 12), (19, 14)], fill=STEEL_LIGHT, outline=WHITE)
    add_highlight(draw, [(12, 12), (14, 11)], fill=(184, 191, 202, 255))
    return image


def mat_herb() -> Image.Image:
    image, draw = new_canvas()
    shadow(draw, (8, 24, 24, 27))
    for stem in ((11, 23, 15, 10), (14, 24, 16, 9), (17, 24, 18, 11), (20, 23, 20, 12)):
        draw.line(stem, fill=HERB_DARK, width=1)
    leaves = [
        [(9, 14), (13, 12), (15, 15), (12, 18)],
        [(12, 10), (16, 8), (18, 11), (15, 14)],
        [(16, 12), (20, 10), (22, 13), (19, 17)],
        [(14, 15), (18, 14), (20, 18), (16, 20)],
        [(10, 18), (14, 17), (16, 21), (12, 22)],
    ]
    for leaf in leaves:
        draw.polygon(leaf, fill=HERB, outline=OUTLINE)
    draw.rectangle((13, 19, 19, 21), fill=TWINE, outline=OUTLINE)
    add_highlight(draw, [(13, 11), (15, 10)], fill=(150, 219, 138, 255))
    return image


RENDERERS = {
    "item_potion_hp.png": potion_hp,
    "item_potion_str.png": potion_str,
    "item_herbal_medicine.png": herbal_medicine,
    "item_antidote.png": antidote,
    "item_bandage.png": bandage,
    "item_torch.png": torch,
    "item_lantern.png": lantern,
    "item_sword_iron.png": sword_iron,
    "item_sword_steel.png": sword_steel,
    "item_shield_iron.png": shield_iron,
    "item_bow_short.png": bow_short,
    "item_pickaxe_steel.png": pickaxe_steel,
    "item_wood_axe.png": wood_axe,
    "item_war_hammer.png": war_hammer,
    "item_meal_simple.png": meal_simple,
    "item_raw_meat.png": raw_meat,
    "item_mat_iron.png": mat_iron,
    "item_mat_herb.png": mat_herb,
}


def build_preview() -> None:
    cols = 3
    rows = 6
    cell_w = 160
    cell_h = 170
    preview = Image.new("RGBA", (cols * cell_w, rows * cell_h), (24, 24, 24, 255))
    draw = ImageDraw.Draw(preview)
    font = ImageFont.load_default()

    for index, filename in enumerate(ORDER):
        col = index % cols
        row = index // cols
        x = col * cell_w
        y = row * cell_h
        draw.rectangle((x + 8, y + 8, x + cell_w - 8, y + 134), fill=(54, 54, 54, 255))
        for cy in range(y + 8, y + 134, 16):
            for cx in range(x + 8, x + cell_w - 8, 16):
                tone = (72, 72, 72, 255) if ((cx + cy) // 16) % 2 == 0 else (92, 92, 92, 255)
                draw.rectangle((cx, cy, cx + 15, cy + 15), fill=tone)
        icon = Image.open(OUT_DIR / filename).convert("RGBA")
        preview.alpha_composite(icon, (x + 16, y + 8))
        label = filename.replace(".png", "")
        draw.text((x + 12, y + 142), label, fill=(232, 232, 232, 255), font=font)

    PREVIEW_PATH.parent.mkdir(parents=True, exist_ok=True)
    preview.save(PREVIEW_PATH)


def validate() -> None:
    for filename in ORDER:
        bbox = Image.open(OUT_DIR / filename).convert("RGBA").getchannel("A").getbbox()
        if bbox is None:
            raise ValueError(f"{filename} rendered empty")
        left, top, right, bottom = bbox
        margins = (left, top, SIZE - right, SIZE - bottom)
        if min(margins) < 8:
            raise ValueError(f"{filename} margin too small: {margins}")


def main() -> None:
    for filename in ORDER:
        save(RENDERERS[filename](), filename)
    build_preview()
    validate()


if __name__ == "__main__":
    main()
