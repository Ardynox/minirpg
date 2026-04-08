from pathlib import Path

from PIL import Image, ImageDraw


BASE_SIZE = 32
EXPORT_SIZE = 128
ROOT = Path(__file__).resolve().parents[1]
OUTPUT_DIR = ROOT / "Assets" / "Art" / "Placeholders" / "item_icons"

OUTLINE = (38, 24, 28, 255)
SHADOW = (0, 0, 0, 64)
WHITE = (245, 240, 235, 255)
PINK = (225, 164, 164, 255)
RED = (186, 67, 76, 255)
RED_DARK = (117, 36, 49, 255)
MEAT = (214, 166, 138, 255)
MEAT_DARK = (153, 97, 78, 255)
BONE = (232, 221, 196, 255)
STEEL = (153, 162, 177, 255)
STEEL_DARK = (91, 101, 118, 255)
BRASS = (196, 160, 90, 255)
GLOW = (95, 220, 224, 255)
GLOW_DARK = (43, 113, 129, 255)
CYBER = (74, 93, 124, 255)

ORGAN_BADGE = ((98, 44, 50, 210), (132, 62, 71, 255))
LIMB_BADGE = ((92, 67, 50, 210), (134, 96, 72, 255))
PROSTHETIC_BADGE = ((57, 68, 88, 210), (92, 104, 126, 255))
BIONIC_BADGE = ((26, 54, 76, 220), (42, 95, 133, 255))


def new_canvas():
    return Image.new("RGBA", (BASE_SIZE, BASE_SIZE), (0, 0, 0, 0))


def badge(draw: ImageDraw.ImageDraw, fill, rim):
    draw.ellipse((4, 4, 28, 28), fill=rim)
    draw.ellipse((5, 5, 27, 27), fill=fill)
    draw.ellipse((9, 8, 24, 16), fill=(255, 255, 255, 20))


def shadow(draw: ImageDraw.ImageDraw, left: int, top: int, right: int, bottom: int):
    draw.ellipse((left, top, right, bottom), fill=SHADOW)


def export(image: Image.Image, name: str):
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    scaled = image.resize((EXPORT_SIZE, EXPORT_SIZE), Image.Resampling.NEAREST)
    scaled.save(OUTPUT_DIR / name)


def draw_heart():
    image = new_canvas()
    draw = ImageDraw.Draw(image)
    badge(draw, *ORGAN_BADGE)
    shadow(draw, 9, 21, 23, 26)
    draw.ellipse((9, 9, 16, 16), fill=RED, outline=OUTLINE)
    draw.ellipse((15, 9, 22, 16), fill=RED, outline=OUTLINE)
    draw.polygon(((8, 14), (23, 14), (16, 25)), fill=RED, outline=OUTLINE)
    draw.line((16, 8, 15, 4), fill=RED_DARK, width=2)
    draw.line((15, 5, 12, 6), fill=RED_DARK, width=2)
    draw.line((15, 5, 18, 7), fill=RED_DARK, width=2)
    draw.line((12, 15, 14, 13), fill=(234, 152, 152, 255))
    draw.line((18, 15, 20, 14), fill=(234, 152, 152, 255))
    return image


def draw_lung():
    image = new_canvas()
    draw = ImageDraw.Draw(image)
    badge(draw, *ORGAN_BADGE)
    shadow(draw, 8, 21, 24, 26)
    draw.rectangle((15, 6, 17, 12), fill=BONE, outline=OUTLINE)
    draw.line((16, 12, 12, 16), fill=BONE, width=2)
    draw.line((16, 12, 20, 16), fill=BONE, width=2)
    draw.ellipse((8, 11, 15, 23), fill=PINK, outline=OUTLINE)
    draw.ellipse((17, 11, 24, 23), fill=PINK, outline=OUTLINE)
    draw.line((10, 13, 12, 18), fill=(244, 202, 202, 255))
    draw.line((22, 13, 20, 18), fill=(244, 202, 202, 255))
    return image


def draw_eye():
    image = new_canvas()
    draw = ImageDraw.Draw(image)
    badge(draw, *ORGAN_BADGE)
    shadow(draw, 8, 21, 24, 25)
    draw.ellipse((7, 11, 25, 21), fill=WHITE, outline=OUTLINE)
    draw.ellipse((12, 12, 20, 20), fill=(91, 136, 185, 255), outline=OUTLINE)
    draw.ellipse((15, 14, 17, 18), fill=OUTLINE)
    draw.line((9, 15, 6, 13), fill=(194, 100, 104, 255))
    draw.line((23, 15, 26, 13), fill=(194, 100, 104, 255))
    draw.line((8, 11, 13, 9), fill=(225, 192, 192, 255))
    draw.line((24, 11, 19, 9), fill=(225, 192, 192, 255))
    return image


def draw_arm(fill, badge_colors, cyber=False):
    image = new_canvas()
    draw = ImageDraw.Draw(image)
    badge(draw, *badge_colors)
    shadow(draw, 8, 22, 23, 26)
    draw.polygon(
        ((11, 9), (16, 11), (20, 19), (18, 24), (13, 24), (11, 16)),
        fill=fill,
        outline=OUTLINE,
    )
    draw.rectangle((9, 9, 12, 12), fill=WHITE if not cyber else GLOW, outline=OUTLINE)
    draw.rectangle((17, 22, 21, 24), fill=fill, outline=OUTLINE)
    draw.rectangle((20, 20, 22, 22), fill=fill, outline=OUTLINE)
    if cyber:
        draw.line((12, 13, 18, 20), fill=GLOW, width=2)
        draw.ellipse((14, 15, 17, 18), fill=GLOW_DARK, outline=OUTLINE)
    elif fill == STEEL:
        draw.ellipse((14, 15, 17, 18), fill=BRASS, outline=OUTLINE)
        draw.line((12, 13, 18, 20), fill=STEEL_DARK, width=2)
    else:
        draw.line((12, 13, 18, 20), fill=(236, 194, 174, 255), width=2)
        draw.rectangle((9, 10, 12, 11), fill=(196, 78, 78, 255))
    return image


def draw_leg(fill, badge_colors, cyber=False):
    image = new_canvas()
    draw = ImageDraw.Draw(image)
    badge(draw, *badge_colors)
    shadow(draw, 9, 22, 24, 26)
    draw.polygon(
        ((13, 8), (18, 8), (20, 16), (17, 23), (12, 23), (14, 16)),
        fill=fill,
        outline=OUTLINE,
    )
    draw.rectangle((11, 23, 19, 25), fill=fill, outline=OUTLINE)
    draw.rectangle((13, 7, 18, 10), fill=WHITE if not cyber else GLOW, outline=OUTLINE)
    if cyber:
        draw.ellipse((15, 15, 18, 18), fill=GLOW_DARK, outline=OUTLINE)
        draw.line((15, 10, 18, 22), fill=GLOW, width=2)
    elif fill == STEEL:
        draw.ellipse((15, 15, 18, 18), fill=BRASS, outline=OUTLINE)
        draw.line((15, 10, 18, 22), fill=STEEL_DARK, width=2)
    else:
        draw.line((15, 10, 18, 22), fill=(236, 194, 174, 255), width=2)
        draw.rectangle((13, 8, 18, 9), fill=(196, 78, 78, 255))
    return image


def draw_prosthetic_eye(fill, badge_colors, cyber=False):
    image = new_canvas()
    draw = ImageDraw.Draw(image)
    badge(draw, *badge_colors)
    shadow(draw, 8, 21, 24, 25)
    draw.ellipse((8, 10, 24, 22), fill=fill, outline=OUTLINE)
    draw.ellipse((11, 12, 21, 20), fill=WHITE, outline=OUTLINE)
    draw.ellipse((13, 13, 19, 19), fill=GLOW if cyber else BRASS, outline=OUTLINE)
    draw.ellipse((15, 15, 17, 17), fill=OUTLINE)
    if cyber:
        draw.line((8, 16, 5, 16), fill=GLOW, width=2)
        draw.line((24, 16, 27, 16), fill=GLOW, width=2)
    else:
        draw.line((9, 12, 7, 10), fill=STEEL_DARK, width=2)
        draw.line((23, 20, 25, 22), fill=STEEL_DARK, width=2)
    return image


def main():
    icons = {
        "item_organ_heart.png": draw_heart(),
        "item_organ_lung.png": draw_lung(),
        "item_organ_eye.png": draw_eye(),
        "item_limb_arm.png": draw_arm(MEAT, LIMB_BADGE),
        "item_limb_leg.png": draw_leg(MEAT, LIMB_BADGE),
        "item_prosthetic_arm.png": draw_arm(STEEL, PROSTHETIC_BADGE),
        "item_prosthetic_leg.png": draw_leg(STEEL, PROSTHETIC_BADGE),
        "item_prosthetic_eye.png": draw_prosthetic_eye(STEEL, PROSTHETIC_BADGE),
        "item_bionic_arm.png": draw_arm(CYBER, BIONIC_BADGE, cyber=True),
        "item_bionic_leg.png": draw_leg(CYBER, BIONIC_BADGE, cyber=True),
        "item_bionic_eye.png": draw_prosthetic_eye(CYBER, BIONIC_BADGE, cyber=True),
    }

    for name, image in icons.items():
        export(image, name)


if __name__ == "__main__":
    main()
