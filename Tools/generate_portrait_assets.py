from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from PIL import Image, ImageChops, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUTPUT_ROOT = ROOT / "Assets" / "Art" / "Placeholders" / "portraits"
ARTIFACTS_ROOT = ROOT / "Artifacts"

LOW_RES = (128, 128)
SCALE = 4
FINAL_SIZE = (LOW_RES[0] * SCALE, LOW_RES[1] * SCALE)
PREVIEW_COLUMNS = 5
PREVIEW_ROWS = 2
PREVIEW_SIZE = (FINAL_SIZE[0] * PREVIEW_COLUMNS, FINAL_SIZE[1] * PREVIEW_ROWS)

Color = tuple[int, int, int, int]

TRANSPARENT: Color = (0, 0, 0, 0)
OUTLINE: Color = (46, 30, 24, 255)
SHADOW: Color = (0, 0, 0, 72)
WHITE: Color = (240, 237, 230, 255)
BLACK: Color = (28, 24, 24, 255)
WARM_LIGHT: Color = (255, 206, 128, 150)
WARM_EDGE: Color = (255, 196, 120, 255)
SKIN_LIGHT: Color = (220, 184, 152, 255)
SKIN_TAN: Color = (198, 154, 120, 255)
SKIN_DARK: Color = (146, 104, 78, 255)
HAIR_BROWN: Color = (92, 60, 38, 255)
HAIR_DARK: Color = (52, 38, 34, 255)
HAIR_SILVER: Color = (212, 214, 220, 255)
HAIR_WHITE: Color = (236, 236, 240, 255)
BLUE_ROBE: Color = (54, 78, 148, 255)
BLUE_DARK: Color = (31, 43, 90, 255)
GREEN_CLOTH: Color = (78, 130, 72, 255)
GREEN_DARK: Color = (42, 82, 44, 255)
RED_CLOTH: Color = (164, 72, 62, 255)
PURPLE_CLOTH: Color = (118, 82, 154, 255)
GOLD: Color = (216, 180, 84, 255)
LEATHER: Color = (120, 78, 48, 255)
LEATHER_DARK: Color = (80, 50, 30, 255)
CREAM: Color = (230, 220, 188, 255)
IRON: Color = (154, 160, 176, 255)
IRON_DARK: Color = (96, 103, 118, 255)
CHAIN: Color = (184, 190, 200, 255)
ORC_GREEN: Color = (100, 150, 78, 255)
ORC_GREEN_DARK: Color = (66, 108, 56, 255)
FUR: Color = (152, 124, 88, 255)
POTION_RED: Color = (180, 70, 80, 255)
POTION_BLUE: Color = (80, 156, 192, 255)
POTION_GREEN: Color = (94, 164, 96, 255)
ROSY: Color = (214, 126, 124, 255)
YELLOW: Color = (226, 202, 96, 255)


def clamp(value: float) -> int:
    return max(0, min(255, int(round(value))))


def lighten(color: Color, amount: float) -> Color:
    r, g, b, a = color
    return (
        clamp(r + (255 - r) * amount),
        clamp(g + (255 - g) * amount),
        clamp(b + (255 - b) * amount),
        a,
    )


def darken(color: Color, amount: float) -> Color:
    r, g, b, a = color
    return (
        clamp(r * (1 - amount)),
        clamp(g * (1 - amount)),
        clamp(b * (1 - amount)),
        a,
    )


def with_alpha(color: Color, alpha: int) -> Color:
    return (color[0], color[1], color[2], alpha)


def new_layer() -> Image.Image:
    return Image.new("RGBA", LOW_RES, TRANSPARENT)


def rect(draw: ImageDraw.ImageDraw, xy: tuple[int, int, int, int], fill: Color, outline: Color = OUTLINE) -> None:
    draw.rectangle(xy, fill=fill, outline=outline)


def ellipse(draw: ImageDraw.ImageDraw, xy: tuple[int, int, int, int], fill: Color, outline: Color = OUTLINE) -> None:
    draw.ellipse(xy, fill=fill, outline=outline)


def poly(draw: ImageDraw.ImageDraw, points: list[tuple[int, int]], fill: Color, outline: Color = OUTLINE) -> None:
    draw.polygon(points, fill=fill, outline=outline)


def line(draw: ImageDraw.ImageDraw, points: list[tuple[int, int]], fill: Color, width: int = 1) -> None:
    draw.line(points, fill=fill, width=width)


def face_base(
    draw: ImageDraw.ImageDraw,
    *,
    skin: Color,
    broad: bool = False,
    jaw: bool = False,
    elf: bool = False,
    orc: bool = False,
) -> None:
    if elf:
        poly(draw, [(81, 34), (95, 39), (84, 45)], lighten(skin, 0.05))
    else:
        ellipse(draw, (78, 35, 87, 46), lighten(skin, 0.03))

    head_box = (39, 18, 81, 63) if not broad else (36, 19, 84, 64)
    ellipse(draw, head_box, skin)

    if jaw:
        poly(draw, [(44, 52), (76, 52), (78, 62), (42, 62)], darken(skin, 0.06))
    if orc:
        poly(draw, [(38, 42), (30, 46), (38, 49)], darken(skin, 0.16))
    else:
        poly(draw, [(39, 43), (31, 47), (39, 49)], darken(skin, 0.12))

    poly(draw, [(41, 22), (55, 18), (44, 36)], lighten(skin, 0.18), outline=lighten(skin, 0.22))
    poly(draw, [(52, 46), (46, 49), (52, 52)], darken(skin, 0.1), outline=darken(skin, 0.1))


def face_features(
    draw: ImageDraw.ImageDraw,
    *,
    eye: Color = BLACK,
    smile: bool = True,
    stern: bool = False,
    wise: bool = False,
    rosy: bool = False,
    old: bool = False,
    orc: bool = False,
) -> None:
    brow = darken(eye, 0.2)
    if stern:
        line(draw, [(49, 36), (56, 35)], brow)
        line(draw, [(61, 35), (68, 36)], brow)
    else:
        line(draw, [(49, 35), (56, 36)], brow)
        line(draw, [(62, 36), (68, 35)], brow)

    line(draw, [(50, 41), (56, 41)], eye)
    line(draw, [(62, 40), (67, 40)], eye)
    draw.point((54, 42), fill=WHITE)
    draw.point((64, 41), fill=WHITE)

    if old:
        line(draw, [(47, 43), (50, 45)], darken(SKIN_LIGHT, 0.35))
        line(draw, [(68, 42), (70, 44)], darken(SKIN_LIGHT, 0.35))
    if wise:
        line(draw, [(47, 50), (51, 53)], darken(SKIN_LIGHT, 0.25))

    mouth = [(49, 55), (55, 57), (61, 56)] if smile else [(49, 56), (55, 56), (60, 55)]
    if stern:
        mouth = [(49, 56), (55, 56), (61, 55)]
    if orc:
        mouth = [(51, 56), (57, 57), (62, 56)]
    line(draw, mouth, RED_CLOTH)

    if rosy:
        draw.point((47, 49), fill=ROSY)
        draw.point((68, 48), fill=ROSY)


def long_hair_back(draw: ImageDraw.ImageDraw, color: Color, *, pointed: bool = False) -> None:
    poly(draw, [(37, 24), (80, 24), (85, 74), (32, 78)], color)
    if pointed:
        poly(draw, [(32, 50), (25, 75), (37, 75)], darken(color, 0.12))


def shoulder_shadow(draw: ImageDraw.ImageDraw, *, wide: bool = False) -> None:
    if wide:
        ellipse(draw, (20, 95, 106, 124), SHADOW, outline=with_alpha(SHADOW, 0))
    else:
        ellipse(draw, (24, 97, 100, 123), SHADOW, outline=with_alpha(SHADOW, 0))


def human_torso(
    draw: ImageDraw.ImageDraw,
    *,
    outer: Color,
    inner: Color,
    collar: Color | None = None,
    left: int = 24,
    right: int = 104,
    top: int = 70,
    bottom: int = 127,
) -> None:
    poly(draw, [(left, top), (right, top), (right + 8, bottom), (left - 8, bottom)], outer)
    poly(draw, [(44, top + 6), (84, top + 6), (90, bottom), (38, bottom)], inner)
    rect(draw, (56, 61, 69, 74), SKIN_TAN)
    if collar is not None:
        poly(draw, [(52, 70), (63, 80), (74, 70), (63, 67)], collar)
    line(draw, [(63, 66), (63, 126)], darken(inner, 0.18))


def warm_rim(draw: ImageDraw.ImageDraw, points: list[tuple[int, int]]) -> None:
    for x, y in points:
        draw.point((x, y), fill=WARM_EDGE)


def draw_pouches(draw: ImageDraw.ImageDraw, points: list[tuple[int, int]]) -> None:
    for x, y in points:
        rect(draw, (x, y, x + 8, y + 7), LEATHER, outline=LEATHER_DARK)
        draw.point((x + 4, y + 1), fill=GOLD)


def draw_potion_belt(draw: ImageDraw.ImageDraw) -> None:
    rect(draw, (44, 96, 82, 101), LEATHER_DARK, outline=LEATHER_DARK)
    rect(draw, (48, 90, 52, 97), POTION_BLUE, outline=WHITE)
    rect(draw, (56, 88, 60, 97), POTION_RED, outline=WHITE)
    rect(draw, (64, 90, 68, 97), POTION_GREEN, outline=WHITE)


def merchant(back: Image.Image, mid: Image.Image, front: Image.Image) -> None:
    bd = ImageDraw.Draw(back)
    md = ImageDraw.Draw(mid)
    fd = ImageDraw.Draw(front)

    shoulder_shadow(bd)
    human_torso(md, outer=darken(LEATHER, 0.08), inner=LEATHER, collar=CREAM)
    face_base(md, skin=SKIN_TAN)
    face_features(md, smile=True)

    poly(bd, [(44, 23), (74, 19), (82, 50), (36, 54)], HAIR_BROWN)
    ellipse(fd, (24, 18, 94, 32), LEATHER_DARK)
    rect(fd, (41, 10, 78, 25), LEATHER, outline=LEATHER_DARK)
    rect(fd, (49, 14, 70, 18), GOLD, outline=LEATHER_DARK)
    poly(fd, [(28, 76), (48, 74), (44, 126), (21, 126)], LEATHER_DARK)
    rect(fd, (26, 92, 37, 103), LEATHER, outline=LEATHER_DARK)
    draw_pouches(fd, [(82, 94)])
    line(fd, [(41, 86), (87, 86)], darken(LEATHER, 0.2))
    warm_rim(fd, [(28, 18), (31, 20), (34, 22), (37, 24), (42, 76), (35, 84)])


def elder(back: Image.Image, mid: Image.Image, front: Image.Image) -> None:
    bd = ImageDraw.Draw(back)
    md = ImageDraw.Draw(mid)
    fd = ImageDraw.Draw(front)

    shoulder_shadow(bd)
    line(bd, [(100, 24), (100, 124)], LEATHER_DARK, width=2)
    ellipse(bd, (95, 17, 104, 26), POTION_BLUE, outline=WHITE)
    poly(bd, [(34, 23), (80, 23), (88, 88), (28, 90)], HAIR_WHITE)
    human_torso(md, outer=BLUE_DARK, inner=BLUE_ROBE, collar=GOLD, left=21, right=107)
    face_base(md, skin=SKIN_LIGHT)
    face_features(md, smile=True, wise=True, old=True)

    poly(fd, [(42, 46), (74, 46), (83, 116), (36, 124)], HAIR_WHITE)
    line(fd, [(53, 78), (53, 126)], GOLD)
    line(fd, [(63, 76), (63, 126)], GOLD)
    line(fd, [(73, 80), (73, 126)], GOLD)
    warm_rim(fd, [(33, 24), (35, 26), (37, 30), (39, 34), (42, 48)])


def villager(back: Image.Image, mid: Image.Image, front: Image.Image) -> None:
    bd = ImageDraw.Draw(back)
    md = ImageDraw.Draw(mid)
    fd = ImageDraw.Draw(front)

    shoulder_shadow(bd)
    human_torso(md, outer=darken(LEATHER, 0.16), inner=LEATHER, collar=CREAM, left=24, right=102)
    face_base(md, skin=SKIN_TAN)
    face_features(md, smile=True)

    poly(fd, [(39, 19), (53, 16), (49, 28), (60, 17), (69, 18), (78, 29), (79, 43), (72, 37), (66, 34), (52, 34), (42, 37)], HAIR_BROWN)
    line(fd, [(47, 84), (80, 118)], LEATHER_DARK, width=2)
    rect(fd, (83, 95, 95, 109), LEATHER, outline=LEATHER_DARK)
    warm_rim(fd, [(37, 21), (40, 20), (43, 19), (45, 78)])


def blacksmith(back: Image.Image, mid: Image.Image, front: Image.Image) -> None:
    bd = ImageDraw.Draw(back)
    md = ImageDraw.Draw(mid)
    fd = ImageDraw.Draw(front)

    shoulder_shadow(bd, wide=True)
    rect(bd, (88, 36, 103, 47), IRON_DARK, outline=WHITE)
    rect(bd, (97, 24, 100, 47), LEATHER_DARK, outline=OUTLINE)
    human_torso(md, outer=darken(LEATHER, 0.18), inner=CREAM, collar=LEATHER, left=18, right=108)
    rect(md, (48, 72, 78, 127), LEATHER, outline=LEATHER_DARK)
    face_base(md, skin=SKIN_DARK, broad=True, jaw=True)
    face_features(md, smile=False, stern=True)

    poly(fd, [(40, 18), (80, 18), (82, 32), (38, 31)], HAIR_DARK)
    rect(fd, (19, 80, 30, 99), SKIN_DARK, outline=OUTLINE)
    rect(fd, (82, 82, 93, 100), SKIN_DARK, outline=OUTLINE)
    draw_pouches(fd, [(84, 108)])
    draw_pouches(fd, [(24, 104)])
    md.point((51, 48), fill=BLACK)
    md.point((55, 52), fill=BLACK)
    md.point((61, 46), fill=BLACK)
    warm_rim(fd, [(38, 19), (40, 21), (21, 82), (24, 85)])


def herbalist(back: Image.Image, mid: Image.Image, front: Image.Image) -> None:
    bd = ImageDraw.Draw(back)
    md = ImageDraw.Draw(mid)
    fd = ImageDraw.Draw(front)

    shoulder_shadow(bd)
    poly(bd, [(22, 70), (104, 70), (112, 127), (14, 127)], GREEN_DARK)
    poly(bd, [(33, 18), (80, 18), (91, 78), (25, 80)], lighten(GREEN_CLOTH, 0.04))
    human_torso(md, outer=GREEN_DARK, inner=GREEN_CLOTH, collar=CREAM, left=22, right=104)
    face_base(md, skin=SKIN_LIGHT)
    face_features(md, smile=True)

    poly(fd, [(35, 18), (47, 20), (45, 55), (37, 80), (31, 47)], GREEN_CLOTH)
    poly(fd, [(67, 18), (78, 20), (81, 46), (81, 80), (68, 71), (65, 56)], GREEN_CLOTH)
    poly(fd, [(46, 24), (61, 22), (59, 35), (49, 35)], HAIR_BROWN)
    line(fd, [(20, 98), (29, 88), (31, 103)], GREEN_DARK, width=2)
    line(fd, [(24, 97), (33, 85), (35, 102)], GOLD)
    draw_potion_belt(fd)
    rect(fd, (81, 94, 92, 101), LEATHER, outline=LEATHER_DARK)
    warm_rim(fd, [(33, 19), (35, 21), (37, 24), (21, 98)])


def cook(back: Image.Image, mid: Image.Image, front: Image.Image) -> None:
    bd = ImageDraw.Draw(back)
    md = ImageDraw.Draw(mid)
    fd = ImageDraw.Draw(front)

    shoulder_shadow(bd, wide=True)
    human_torso(md, outer=CREAM, inner=lighten(CREAM, 0.08), collar=RED_CLOTH, left=18, right=108)
    face_base(md, skin=SKIN_LIGHT, broad=True)
    face_features(md, smile=True, rosy=True)

    rect(fd, (41, 6, 77, 19), WHITE)
    rect(fd, (45, 0, 73, 10), WHITE)
    line(fd, [(37, 86), (87, 86)], with_alpha(WHITE, 140))
    line(fd, [(37, 96), (83, 96)], with_alpha(WHITE, 100))
    line(fd, [(94, 80), (110, 96)], LEATHER_DARK, width=2)
    ellipse(fd, (89, 74, 99, 84), LEATHER, outline=LEATHER_DARK)
    draw_pouches(fd, [(23, 105)])
    warm_rim(fd, [(41, 6), (44, 3), (95, 80)])


def guard(back: Image.Image, mid: Image.Image, front: Image.Image) -> None:
    bd = ImageDraw.Draw(back)
    md = ImageDraw.Draw(mid)
    fd = ImageDraw.Draw(front)

    shoulder_shadow(bd, wide=True)
    line(bd, [(103, 20), (103, 126)], LEATHER_DARK, width=2)
    poly(bd, [(101, 20), (108, 26), (103, 32)], IRON, outline=WHITE)
    human_torso(md, outer=IRON_DARK, inner=CHAIN, collar=RED_CLOTH, left=18, right=108)
    face_base(md, skin=SKIN_TAN, broad=True)
    face_features(md, smile=False, stern=True)

    rect(fd, (38, 16, 79, 31), IRON, outline=WHITE)
    rect(fd, (45, 31, 73, 40), IRON_DARK, outline=WHITE)
    rect(fd, (36, 77, 82, 107), CHAIN, outline=IRON_DARK)
    for x in range(39, 80, 6):
        for y in range(80, 104, 6):
            fd.point((x, y), fill=WHITE)
    poly(fd, [(52, 77), (74, 77), (79, 127), (47, 127)], RED_CLOTH)
    warm_rim(fd, [(38, 16), (41, 18), (52, 77)])


def tailor(back: Image.Image, mid: Image.Image, front: Image.Image) -> None:
    bd = ImageDraw.Draw(back)
    md = ImageDraw.Draw(mid)
    fd = ImageDraw.Draw(front)

    shoulder_shadow(bd)
    human_torso(md, outer=PURPLE_CLOTH, inner=lighten(PURPLE_CLOTH, 0.12), collar=CREAM, left=22, right=102)
    face_base(md, skin=SKIN_TAN)
    face_features(md, smile=True)

    poly(fd, [(39, 20), (76, 18), (82, 43), (72, 36), (63, 33), (53, 33), (44, 36)], HAIR_BROWN)
    rect(fd, (45, 82, 54, 91), GOLD)
    rect(fd, (55, 90, 65, 99), GREEN_CLOTH)
    rect(fd, (66, 82, 75, 90), RED_CLOTH)
    line(fd, [(46, 76), (79, 76)], YELLOW)
    line(fd, [(45, 77), (43, 99)], YELLOW)
    line(fd, [(79, 77), (83, 102)], YELLOW)
    ellipse(fd, (89, 93, 95, 100), TRANSPARENT, outline=IRON)
    ellipse(fd, (95, 96, 101, 103), TRANSPARENT, outline=IRON)
    line(fd, [(92, 97), (105, 89)], IRON)
    line(fd, [(98, 99), (108, 93)], IRON)
    warm_rim(fd, [(39, 21), (42, 20), (45, 76)])


def elf_trader(back: Image.Image, mid: Image.Image, front: Image.Image) -> None:
    bd = ImageDraw.Draw(back)
    md = ImageDraw.Draw(mid)
    fd = ImageDraw.Draw(front)

    shoulder_shadow(bd)
    long_hair_back(bd, HAIR_SILVER, pointed=True)
    human_torso(md, outer=GREEN_DARK, inner=GREEN_CLOTH, collar=GOLD, left=21, right=103)
    face_base(md, skin=SKIN_LIGHT, elf=True)
    face_features(md, smile=True, wise=True)

    poly(fd, [(41, 18), (55, 18), (52, 36), (45, 74), (37, 46)], HAIR_SILVER)
    poly(fd, [(66, 18), (76, 20), (78, 47), (70, 74), (64, 48)], HAIR_SILVER)
    line(fd, [(44, 30), (48, 46), (45, 67)], darken(HAIR_SILVER, 0.18))
    line(fd, [(70, 28), (72, 50), (67, 69)], darken(HAIR_SILVER, 0.18))
    line(fd, [(47, 81), (79, 81)], GOLD)
    line(fd, [(50, 92), (74, 92)], GOLD)
    ellipse(fd, (72, 84, 80, 92), GOLD, outline=WHITE)
    warm_rim(fd, [(39, 19), (42, 21), (44, 24), (47, 81)])


def orc_merchant(back: Image.Image, mid: Image.Image, front: Image.Image) -> None:
    bd = ImageDraw.Draw(back)
    md = ImageDraw.Draw(mid)
    fd = ImageDraw.Draw(front)

    shoulder_shadow(bd, wide=True)
    poly(bd, [(20, 72), (106, 72), (112, 127), (14, 127)], LEATHER_DARK)
    poly(bd, [(28, 72), (98, 72), (104, 127), (22, 127)], FUR)
    human_torso(md, outer=LEATHER_DARK, inner=LEATHER, collar=FUR, left=16, right=110)
    face_base(md, skin=ORC_GREEN, broad=True, jaw=True, orc=True)
    face_features(md, smile=False, stern=True, orc=True)

    poly(fd, [(40, 20), (79, 20), (83, 34), (70, 31), (56, 31), (43, 33)], HAIR_DARK)
    poly(fd, [(45, 57), (49, 63), (52, 57)], CREAM, outline=OUTLINE)
    poly(fd, [(58, 58), (62, 65), (65, 58)], CREAM, outline=OUTLINE)
    draw_pouches(fd, [(22, 103), (82, 100), (92, 109)])
    line(fd, [(34, 85), (94, 85)], FUR)
    line(fd, [(31, 87), (91, 87)], with_alpha(CREAM, 140))
    warm_rim(fd, [(40, 20), (43, 21), (24, 104)])


@dataclass(frozen=True)
class RoleSpec:
    output_name: str
    prompt: str
    drawer: Callable[[Image.Image, Image.Image, Image.Image], None]


ROLE_SPECS = [
    RoleSpec(
        "portrait_merchant.png",
        "A middle-aged human male traveling merchant with wide-brimmed hat, brown leather vest, friendly smile, coin pouch visible portrait, bust/half-body shot, 3/4 view facing slightly left. 512x512 pixels, transparent background. 2D digital painting with pixel-art influence, medieval fantasy style. Medium-high saturation, warm lighting. No text, no border, no frame.",
        merchant,
    ),
    RoleSpec(
        "portrait_elder.png",
        "A elderly human male village elder with long white beard, dark blue robe with gold trim, wise gentle eyes, wooden staff portrait, bust/half-body shot, 3/4 view facing slightly left. 512x512 pixels, transparent background. 2D digital painting with pixel-art influence, medieval fantasy style. Medium-high saturation, warm lighting. No text, no border, no frame.",
        elder,
    ),
    RoleSpec(
        "portrait_villager.png",
        "A young adult human villager in simple brown tunic, plain but friendly face, slightly messy hair portrait, bust/half-body shot, 3/4 view facing slightly left. 512x512 pixels, transparent background. 2D digital painting with pixel-art influence, medieval fantasy style. Medium-high saturation, warm lighting. No text, no border, no frame.",
        villager,
    ),
    RoleSpec(
        "portrait_blacksmith_npc.png",
        "A muscular human male blacksmith with leather apron, soot on face, strong jaw, short dark hair, smithing hammer resting on shoulder portrait, bust/half-body shot, 3/4 view facing slightly left. 512x512 pixels, transparent background. 2D digital painting with pixel-art influence, medieval fantasy style. Medium-high saturation, warm lighting. No text, no border, no frame.",
        blacksmith,
    ),
    RoleSpec(
        "portrait_herbalist_npc.png",
        "A slender human female herbalist with green hooded cloak, gentle eyes, holding dried herbs, small potion bottles on belt portrait, bust/half-body shot, 3/4 view facing slightly left. 512x512 pixels, transparent background. 2D digital painting with pixel-art influence, medieval fantasy style. Medium-high saturation, warm lighting. No text, no border, no frame.",
        herbalist,
    ),
    RoleSpec(
        "portrait_cook_npc.png",
        "A plump cheerful human cook with white chef hat, rosy cheeks, flour-dusted apron, holding wooden ladle portrait, bust/half-body shot, 3/4 view facing slightly left. 512x512 pixels, transparent background. 2D digital painting with pixel-art influence, medieval fantasy style. Medium-high saturation, warm lighting. No text, no border, no frame.",
        cook,
    ),
    RoleSpec(
        "portrait_guard_npc.png",
        "A stern human male town guard in iron chain armor with tabard, iron half-helmet, alert expression, spear visible portrait, bust/half-body shot, 3/4 view facing slightly left. 512x512 pixels, transparent background. 2D digital painting with pixel-art influence, medieval fantasy style. Medium-high saturation, warm lighting. No text, no border, no frame.",
        guard,
    ),
    RoleSpec(
        "portrait_tailor_npc.png",
        "A slim human tailor with colorful patchwork vest, measuring tape around neck, holding scissors, creative expression portrait, bust/half-body shot, 3/4 view facing slightly left. 512x512 pixels, transparent background. 2D digital painting with pixel-art influence, medieval fantasy style. Medium-high saturation, warm lighting. No text, no border, no frame.",
        tailor,
    ),
    RoleSpec(
        "portrait_elf_trader.png",
        "A tall slender elf with pointed ears, silver-blonde hair, elegant green and gold robes, calm refined expression portrait, bust/half-body shot, 3/4 view facing slightly left. 512x512 pixels, transparent background. 2D digital painting with pixel-art influence, medieval fantasy style. Medium-high saturation, warm lighting. No text, no border, no frame.",
        elf_trader,
    ),
    RoleSpec(
        "portrait_orc_merchant.png",
        "A broad green-skinned orc with small tusks, fur-trimmed merchant vest, gruff but not hostile expression, many pouches portrait, bust/half-body shot, 3/4 view facing slightly left. 512x512 pixels, transparent background. 2D digital painting with pixel-art influence, medieval fantasy style. Medium-high saturation, warm lighting. No text, no border, no frame.",
        orc_merchant,
    ),
]


def finalize_image(low_res: Image.Image) -> Image.Image:
    warm_overlay = new_layer()
    warm_draw = ImageDraw.Draw(warm_overlay)
    poly(
        warm_draw,
        [(16, 0), (74, 0), (57, 127), (5, 127)],
        with_alpha(WARM_LIGHT, 56),
        outline=with_alpha(WARM_LIGHT, 0),
    )
    warm_overlay.putalpha(ImageChops.multiply(warm_overlay.getchannel("A"), low_res.getchannel("A")))
    low_res.alpha_composite(warm_overlay)

    high_res = low_res.resize(FINAL_SIZE, Image.Resampling.NEAREST)
    return high_res


def compose(spec: RoleSpec) -> Image.Image:
    back = new_layer()
    mid = new_layer()
    front = new_layer()
    spec.drawer(back, mid, front)

    low_res = new_layer()
    low_res.alpha_composite(back)
    low_res.alpha_composite(mid)
    low_res.alpha_composite(front)
    return finalize_image(low_res)


def build_preview(images: list[Image.Image]) -> Image.Image:
    preview = Image.new("RGBA", PREVIEW_SIZE, (34, 34, 42, 255))
    draw = ImageDraw.Draw(preview)
    for row in range(PREVIEW_ROWS):
        for col in range(PREVIEW_COLUMNS):
            x = col * FINAL_SIZE[0]
            y = row * FINAL_SIZE[1]
            if (row + col) % 2 == 0:
                rect(draw, (x, y, x + FINAL_SIZE[0], y + FINAL_SIZE[1]), (41, 42, 54, 255), outline=(41, 42, 54, 255))

    for index, image in enumerate(images):
        x = (index % PREVIEW_COLUMNS) * FINAL_SIZE[0]
        y = (index // PREVIEW_COLUMNS) * FINAL_SIZE[1]
        preview.alpha_composite(image, (x, y))

    return preview


def main() -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    ARTIFACTS_ROOT.mkdir(parents=True, exist_ok=True)

    generated: list[Image.Image] = []
    for spec in ROLE_SPECS:
        portrait = compose(spec)
        portrait.save(OUTPUT_ROOT / spec.output_name)
        generated.append(portrait)

    build_preview(generated).save(ARTIFACTS_ROOT / "portrait_preview_sheet.png")
    print(f"Generated {len(generated)} portrait assets in {OUTPUT_ROOT}")


if __name__ == "__main__":
    main()
