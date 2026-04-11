from __future__ import annotations

import json
from pathlib import Path

from PIL import Image, ImageDraw


BASE = 32
SIZE = 128
ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = ROOT / "Assets" / "Art" / "Placeholders" / "item_icons"
MANIFEST = ROOT / "Assets" / "Art" / "Placeholders" / "manifest.json"
ITEMS = ROOT / "Data" / "items.json"

OUTLINE = (38, 24, 28, 255)
SHADOW = (0, 0, 0, 64)
WHITE = (245, 240, 235, 255)
STEEL = (153, 162, 177, 255)
STEEL_DARK = (91, 101, 118, 255)
WOOD = (143, 97, 63, 255)
WOOD_DARK = (87, 56, 36, 255)
LEATHER = (146, 99, 71, 255)
LEATHER_DARK = (89, 57, 40, 255)
STONE = (122, 124, 130, 255)
STONE_DARK = (77, 80, 88, 255)
GOLD = (224, 186, 88, 255)
SILVER = (205, 208, 215, 255)
PLASTEEL = (113, 196, 222, 255)
URANIUM = (102, 184, 117, 255)
JADE = (85, 180, 150, 255)
BLUE = (106, 128, 179, 255)
BLUE_DARK = (58, 75, 109, 255)
GREEN = (104, 142, 112, 255)
GREEN_DARK = (56, 87, 64, 255)
RED = (152, 72, 88, 255)
RED_DARK = (89, 40, 51, 255)
BRASS = (196, 160, 90, 255)

FOOD_BADGE = ((82, 61, 40, 212), (124, 94, 64, 255))
WEAPON_BADGE = ((78, 48, 45, 212), (122, 74, 68, 255))
ARMOR_BADGE = ((56, 64, 80, 212), (87, 98, 124, 255))
CLOTH_BADGE = ((67, 62, 90, 212), (108, 99, 139, 255))
MATERIAL_BADGE = ((61, 74, 68, 212), (94, 118, 108, 255))
TOOL_BADGE = ((85, 74, 52, 212), (128, 111, 80, 255))
CHEM_BADGE = ((58, 82, 52, 212), (90, 127, 76, 255))
GUN_BADGE = ((54, 69, 86, 212), (84, 109, 138, 255))
AMMO_BADGE = ((92, 84, 48, 212), (142, 128, 74, 255))
MISC_BADGE = ((71, 64, 50, 212), (108, 96, 77, 255))
CORPSE_BADGE = ((64, 53, 58, 220), (103, 81, 90, 255))

ORDER = [
    "go_juice",
    "meal_fine",
    "meal_lavish",
    "pemmican",
    "berries",
    "sword_plasteel",
    "club_wood",
    "mace_iron",
    "war_hammer_uranium",
    "dagger",
    "spear_wood",
    "spear_steel",
    "bow_great",
    "shield_steel",
    "shield_wood",
    "cloth_shirt",
    "cloth_pants",
    "tuque",
    "duster",
    "parka",
    "devilstrand_duster",
    "leather_vest",
    "chainmail",
    "plate_armor",
    "flak_vest",
    "iron_helmet",
    "steel_helmet",
    "leather_boots",
    "steel_greaves",
    "gauntlets_iron",
    "pickaxe",
    "hammer_smith",
    "knife_butcher",
    "mat_wood",
    "mat_coal",
    "mat_copper",
    "mat_crystal",
    "mat_stone",
    "mat_steel",
    "mat_gold",
    "mat_silver",
    "mat_plasteel",
    "mat_uranium",
    "mat_jade",
    "mat_leather",
    "mat_cloth",
    "mat_silk",
    "mat_devilstrand",
    "mat_chitin",
    "chest_wooden",
    "backpack",
    "bedroll",
    "rope",
    "industrial_medicine",
    "surgery_kit",
    "sidearm_revolver",
    "bolt_rifle",
    "pump_shotgun",
    "ammo_revolver",
    "ammo_rifle",
    "ammo_shell",
    "corpse_human",
    "corpse_beast_small",
    "corpse_beast_large",
]


def canvas(colors):
    image = Image.new("RGBA", (BASE, BASE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.ellipse((4, 4, 28, 28), fill=colors[1])
    draw.ellipse((5, 5, 27, 27), fill=colors[0])
    draw.ellipse((9, 8, 24, 16), fill=(255, 255, 255, 20))
    return image, draw


def shadow(draw, box):
    draw.ellipse(box, fill=SHADOW)


def save(image, item_id):
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    image.resize((SIZE, SIZE), Image.Resampling.NEAREST).save(OUT_DIR / f"item_{item_id}.png")


def titleize(item_id):
    return " ".join(part.capitalize() for part in item_id.split("_"))


def tags(item):
    result = ["item", "icon", item.get("category", "misc")]
    for part in item["id"].split("_"):
        if part not in result:
            result.append(part)
    sub = item.get("subCategory")
    if sub:
        for part in sub.split("_"):
            if part not in result:
                result.append(part)
    return result


def manifest_entry(item):
    item_id = item["id"]
    q = json.dumps
    lines = [
        "    {",
        f'      "id": {q(f"item_{item_id}")},',
        f'      "name": {q(f"{titleize(item_id)} icon")},',
        f'      "description": {q(f"Placeholder icon for {item_id.replace("_", " ")}.")},',
        '      "category": "item_icons",',
        '      "status": "done",',
        '      "canvasPresetId": "icon_128",',
        '      "width": 128,',
        '      "height": 128,',
        '      "frames": [',
        "        {",
        f'          "imagePath": {q(f"item_icons/item_{item_id}.png")}',
        "        }",
        "      ],",
        '      "tags": [',
    ]
    item_tags = tags(item)
    for i, tag in enumerate(item_tags):
        lines.append(f"        {q(tag)}{',' if i < len(item_tags) - 1 else ''}")
    lines.extend(["      ]", "    },"])
    return "\n".join(lines) + "\n"


def sync_manifest(item_lookup):
    text = MANIFEST.read_text(encoding="utf-8")
    existing = {entry["id"] for entry in json.loads(text)["entries"]}
    missing = [item_id for item_id in ORDER if f"item_{item_id}" not in existing]
    if not missing:
        return
    marker = '    {\n      "id": "fx_slash_arc",'
    block = "".join(manifest_entry(item_lookup[item_id]) for item_id in missing)
    MANIFEST.write_text(text.replace(marker, block + marker, 1), encoding="utf-8", newline="\n")


def bottle(fluid, label):
    image, draw = canvas(CHEM_BADGE)
    shadow(draw, (10, 22, 22, 26))
    draw.rectangle((14, 7, 18, 10), fill=BRASS, outline=OUTLINE)
    draw.rectangle((12, 10, 20, 23), fill=WHITE, outline=OUTLINE)
    draw.rectangle((13, 16, 19, 22), fill=fluid)
    if label == "bolt":
        draw.polygon(((16, 12), (14, 16), (16, 16), (15, 20), (19, 15), (17, 15), (18, 12)), fill=fluid, outline=OUTLINE)
    else:
        draw.rectangle((14, 14, 18, 20), fill=WHITE, outline=OUTLINE)
        draw.rectangle((15, 13, 17, 21), fill=fluid)
        draw.rectangle((13, 15, 19, 19), fill=fluid)
    return image


def meal(luxury=False):
    image, draw = canvas(FOOD_BADGE)
    shadow(draw, (8, 22, 24, 26))
    draw.ellipse((8, 12, 24, 22), fill=(227, 220, 206, 255), outline=OUTLINE)
    draw.ellipse((10, 14, 22, 20), fill=(134, 88, 58, 255), outline=OUTLINE)
    draw.ellipse((12, 15, 18, 19), fill=(227, 164, 88, 255) if luxury else (205, 133, 88, 255), outline=OUTLINE)
    draw.ellipse((17, 14, 21, 18), fill=(86, 138, 69, 255), outline=OUTLINE)
    if luxury:
        draw.line((10, 11, 13, 8), fill=GOLD, width=2)
        draw.line((22, 11, 19, 8), fill=GOLD, width=2)
        draw.line((16, 10, 16, 7), fill=GOLD, width=2)
    return image


def ration():
    image, draw = canvas(FOOD_BADGE)
    shadow(draw, (8, 22, 24, 26))
    draw.polygon(((10, 11), (22, 11), (24, 15), (19, 22), (12, 22), (8, 16)), fill=(157, 88, 59, 255), outline=OUTLINE)
    draw.rectangle((12, 14, 20, 18), fill=(210, 164, 106, 255), outline=OUTLINE)
    return image


def berry_cluster():
    image, draw = canvas(FOOD_BADGE)
    shadow(draw, (9, 22, 23, 26))
    for box in ((10, 14, 15, 19), (14, 11, 19, 16), (18, 14, 23, 19), (13, 17, 18, 22), (17, 17, 22, 22)):
        draw.ellipse(box, fill=(170, 61, 112, 255), outline=OUTLINE)
    draw.line((16, 8, 13, 12), fill=(82, 141, 74, 255), width=2)
    draw.line((16, 8, 20, 11), fill=(82, 141, 74, 255), width=2)
    return image


def sword(blade):
    image, draw = canvas(WEAPON_BADGE)
    shadow(draw, (9, 22, 23, 26))
    draw.polygon(((16, 7), (18, 15), (17, 24), (15, 24), (14, 15)), fill=blade, outline=OUTLINE)
    draw.rectangle((12, 15, 20, 17), fill=BRASS, outline=OUTLINE)
    draw.rectangle((15, 17, 17, 23), fill=WOOD, outline=OUTLINE)
    return image


def club():
    image, draw = canvas(WEAPON_BADGE)
    shadow(draw, (9, 22, 23, 26))
    draw.polygon(((11, 23), (14, 23), (18, 14), (19, 9), (16, 7), (13, 10), (13, 15)), fill=WOOD, outline=OUTLINE)
    return image


def mace():
    image, draw = canvas(WEAPON_BADGE)
    shadow(draw, (9, 22, 23, 26))
    draw.rectangle((15, 10, 17, 24), fill=WOOD, outline=OUTLINE)
    draw.ellipse((11, 6, 21, 14), fill=STEEL, outline=OUTLINE)
    return image


def hammer(head):
    image, draw = canvas(WEAPON_BADGE)
    shadow(draw, (9, 22, 23, 26))
    draw.rectangle((15, 9, 17, 24), fill=WOOD, outline=OUTLINE)
    draw.rectangle((10, 8, 22, 13), fill=head, outline=OUTLINE)
    return image


def dagger():
    image, draw = canvas(WEAPON_BADGE)
    shadow(draw, (9, 22, 23, 26))
    draw.polygon(((16, 7), (19, 16), (17, 23), (15, 23), (13, 16)), fill=SILVER, outline=OUTLINE)
    draw.rectangle((12, 16, 20, 18), fill=BRASS, outline=OUTLINE)
    draw.rectangle((15, 18, 17, 24), fill=LEATHER, outline=OUTLINE)
    return image


def spear(head):
    image, draw = canvas(WEAPON_BADGE)
    shadow(draw, (9, 22, 23, 26))
    draw.line((11, 23, 20, 8), fill=WOOD, width=2)
    draw.polygon(((19, 8), (23, 6), (22, 12)), fill=head, outline=OUTLINE)
    return image


def bow():
    image, draw = canvas(WEAPON_BADGE)
    shadow(draw, (8, 22, 24, 26))
    draw.arc((9, 7, 21, 24), start=270, end=90, fill=WOOD, width=2)
    draw.line((21, 8, 21, 23), fill=WHITE, width=1)
    draw.line((14, 16, 23, 16), fill=(180, 148, 100, 255), width=2)
    return image


def shield(wooden=False):
    image, draw = canvas(ARMOR_BADGE)
    shadow(draw, (8, 22, 24, 26))
    outer = WOOD_DARK if wooden else STEEL_DARK
    inner = WOOD if wooden else STEEL
    draw.polygon(((16, 8), (23, 12), (21, 23), (11, 23), (9, 12)), fill=outer, outline=OUTLINE)
    draw.polygon(((16, 10), (21, 13), (20, 21), (12, 21), (11, 13)), fill=inner, outline=OUTLINE)
    if wooden:
        draw.line((16, 11, 16, 20), fill=(195, 150, 99, 255), width=2)
    else:
        draw.ellipse((14, 14, 18, 18), fill=BRASS, outline=OUTLINE)
    return image


def vest(kind):
    image, draw = canvas(ARMOR_BADGE)
    shadow(draw, (8, 22, 24, 26))
    body = LEATHER if kind == "leather" else GREEN if kind == "flak" else STEEL
    draw.polygon(((12, 9), (20, 9), (23, 15), (21, 24), (11, 24), (9, 15)), fill=body, outline=OUTLINE)
    draw.rectangle((14, 9, 18, 14), fill=(30, 24, 30, 180), outline=OUTLINE)
    if kind == "chain":
        for y in range(14, 23, 3):
            draw.line((12, y, 20, y), fill=SILVER, width=1)
    elif kind == "plate":
        draw.rectangle((12, 15, 20, 18), fill=STEEL, outline=OUTLINE)
        draw.rectangle((13, 19, 19, 22), fill=STEEL, outline=OUTLINE)
    elif kind == "flak":
        draw.rectangle((12, 15, 20, 22), fill=(150, 133, 81, 255), outline=OUTLINE)
    return image


def helmet(open_face=False):
    image, draw = canvas(ARMOR_BADGE)
    shadow(draw, (9, 22, 23, 26))
    body = (132, 132, 140, 255) if open_face else STEEL
    draw.arc((9, 8, 23, 21), start=180, end=360, fill=body, width=5)
    draw.rectangle((10, 15, 22, 20), fill=body, outline=OUTLINE)
    draw.rectangle((13, 15, 19, 18), fill=WHITE if open_face else STEEL_DARK, outline=OUTLINE)
    return image


def boot(armored=False):
    image, draw = canvas(ARMOR_BADGE if armored else CLOTH_BADGE)
    shadow(draw, (9, 22, 23, 26))
    body = STEEL if armored else LEATHER
    draw.polygon(((13, 8), (19, 8), (19, 18), (23, 20), (23, 24), (11, 24), (11, 18), (13, 18)), fill=body, outline=OUTLINE)
    return image


def gauntlet():
    image, draw = canvas(ARMOR_BADGE)
    shadow(draw, (9, 22, 23, 26))
    draw.polygon(((11, 12), (18, 10), (22, 14), (20, 23), (13, 24), (10, 18)), fill=STEEL, outline=OUTLINE)
    return image


def shirt():
    image, draw = canvas(CLOTH_BADGE)
    shadow(draw, (8, 22, 24, 26))
    draw.polygon(((12, 10), (15, 8), (17, 8), (20, 10), (23, 14), (20, 16), (20, 24), (12, 24), (12, 16), (9, 14)), fill=BLUE, outline=OUTLINE)
    return image


def pants():
    image, draw = canvas(CLOTH_BADGE)
    shadow(draw, (8, 22, 24, 26))
    draw.polygon(((11, 8), (21, 8), (22, 15), (18, 15), (17, 24), (13, 24), (12, 15), (10, 15)), fill=BLUE, outline=OUTLINE)
    return image


def hat():
    image, draw = canvas(CLOTH_BADGE)
    shadow(draw, (8, 22, 24, 26))
    draw.arc((9, 10, 23, 22), start=180, end=360, fill=RED, width=5)
    draw.rectangle((10, 17, 22, 22), fill=RED, outline=OUTLINE)
    draw.ellipse((15, 7, 18, 10), fill=WHITE, outline=OUTLINE)
    return image


def coat(body, trim):
    image, draw = canvas(CLOTH_BADGE)
    shadow(draw, (8, 22, 24, 26))
    draw.polygon(((12, 8), (20, 8), (23, 15), (20, 24), (17, 24), (16, 19), (15, 24), (12, 24), (9, 15)), fill=body, outline=OUTLINE)
    draw.line((16, 9, 16, 24), fill=trim, width=2)
    return image


def pickaxe():
    image, draw = canvas(TOOL_BADGE)
    shadow(draw, (9, 22, 23, 26))
    draw.line((15, 24, 18, 8), fill=WOOD, width=2)
    draw.line((11, 12, 22, 9), fill=STEEL, width=3)
    return image


def smith_hammer():
    image, draw = canvas(TOOL_BADGE)
    shadow(draw, (9, 22, 23, 26))
    draw.line((15, 24, 18, 9), fill=WOOD, width=2)
    draw.rectangle((11, 8, 21, 13), fill=STEEL, outline=OUTLINE)
    return image


def butcher_knife():
    image, draw = canvas(TOOL_BADGE)
    shadow(draw, (9, 22, 23, 26))
    draw.polygon(((11, 13), (22, 10), (22, 18), (12, 20)), fill=STEEL, outline=OUTLINE)
    draw.rectangle((9, 16, 13, 22), fill=WOOD, outline=OUTLINE)
    return image


def material(kind):
    image, draw = canvas(MATERIAL_BADGE)
    shadow(draw, (8, 22, 24, 26))
    if kind == "wood":
        draw.rectangle((9, 13, 23, 21), fill=WOOD, outline=OUTLINE)
        draw.ellipse((7, 13, 13, 21), fill=(174, 128, 82, 255), outline=OUTLINE)
    elif kind == "stone":
        draw.polygon(((10, 11), (20, 9), (24, 15), (22, 22), (12, 23), (8, 17)), fill=STONE, outline=OUTLINE)
    elif kind == "coal":
        draw.polygon(((10, 11), (19, 9), (24, 14), (21, 23), (11, 24), (8, 17)), fill=(66, 70, 81, 255), outline=OUTLINE)
        draw.line((11, 12, 20, 21), fill=(112, 118, 132, 255), width=2)
    elif kind == "copper":
        draw.polygon(((10, 12), (22, 12), (24, 17), (12, 17)), fill=(201, 118, 72, 255), outline=OUTLINE)
        draw.polygon(((12, 17), (24, 17), (21, 23), (9, 23)), fill=(181, 96, 61, 255), outline=OUTLINE)
        draw.line((11, 14, 21, 14), fill=(231, 158, 98, 255), width=1)
    elif kind == "crystal":
        draw.polygon(((16, 8), (22, 12), (20, 23), (12, 24), (9, 14)), fill=(110, 208, 233, 255), outline=OUTLINE)
        draw.polygon(((16, 10), (20, 13), (18, 20), (13, 21), (11, 14)), fill=(191, 247, 255, 180), outline=OUTLINE)
        draw.line((16, 9, 16, 22), fill=(235, 255, 255, 255), width=1)
    elif kind in {"steel", "gold", "silver", "plasteel", "uranium"}:
        fills = {"steel": STEEL, "gold": GOLD, "silver": SILVER, "plasteel": PLASTEEL, "uranium": URANIUM}
        fill = fills[kind]
        draw.polygon(((10, 12), (22, 12), (24, 17), (12, 17)), fill=fill, outline=OUTLINE)
        draw.polygon(((12, 17), (24, 17), (21, 23), (9, 23)), fill=fill, outline=OUTLINE)
    elif kind == "jade":
        draw.polygon(((16, 8), (22, 13), (20, 22), (12, 24), (9, 15)), fill=JADE, outline=OUTLINE)
    elif kind == "leather":
        draw.polygon(((11, 10), (19, 9), (23, 14), (21, 23), (16, 21), (11, 24), (8, 17)), fill=LEATHER, outline=OUTLINE)
    elif kind == "cloth":
        draw.polygon(((10, 10), (22, 10), (24, 16), (21, 24), (9, 24), (8, 16)), fill=BLUE, outline=OUTLINE)
    elif kind == "silk":
        draw.polygon(((10, 10), (22, 10), (24, 16), (21, 24), (9, 24), (8, 16)), fill=WHITE, outline=OUTLINE)
    elif kind == "devilstrand":
        for x in (11, 14, 17, 20):
            draw.line((x, 10, x - 2, 23), fill=RED, width=2)
    else:
        draw.polygon(((10, 12), (17, 9), (23, 13), (21, 22), (12, 23), (8, 18)), fill=(183, 154, 95, 255), outline=OUTLINE)
    return image


def misc(kind):
    image, draw = canvas(MISC_BADGE)
    shadow(draw, (8, 22, 24, 26))
    if kind == "chest":
        draw.rectangle((9, 13, 23, 23), fill=WOOD, outline=OUTLINE)
        draw.polygon(((9, 13), (16, 9), (23, 13), (16, 17)), fill=(164, 113, 74, 255), outline=OUTLINE)
        draw.rectangle((14, 16, 18, 20), fill=BRASS, outline=OUTLINE)
    elif kind == "backpack":
        draw.rounded_rectangle((10, 10, 22, 24), radius=3, fill=LEATHER, outline=OUTLINE)
        draw.line((13, 9, 11, 13), fill=LEATHER_DARK, width=2)
        draw.line((19, 9, 21, 13), fill=LEATHER_DARK, width=2)
    elif kind == "bedroll":
        draw.rectangle((9, 14, 23, 20), fill=BLUE, outline=OUTLINE)
        draw.ellipse((8, 13, 14, 21), fill=BLUE, outline=OUTLINE)
        draw.ellipse((18, 13, 24, 21), fill=BLUE, outline=OUTLINE)
    else:
        draw.ellipse((9, 11, 23, 23), outline=WOOD, width=3)
        draw.ellipse((12, 14, 20, 20), outline=WOOD_DARK, width=2)
    return image


def firearm(kind):
    image, draw = canvas(GUN_BADGE)
    shadow(draw, (8, 22, 24, 26))
    if kind == "revolver":
        draw.rectangle((10, 12, 19, 16), fill=STEEL, outline=OUTLINE)
        draw.rectangle((18, 13, 23, 15), fill=STEEL_DARK, outline=OUTLINE)
        draw.ellipse((13, 10, 18, 15), fill=STEEL_DARK, outline=OUTLINE)
        draw.polygon(((12, 16), (17, 16), (16, 24), (11, 24)), fill=WOOD, outline=OUTLINE)
    else:
        draw.rectangle((8, 13, 23, 16), fill=STEEL, outline=OUTLINE)
        draw.rectangle((21, 12, 25, 14), fill=STEEL_DARK, outline=OUTLINE)
        draw.rectangle((10, 16, 16, 18), fill=WOOD, outline=OUTLINE)
        draw.polygon(((11, 18), (16, 18), (18, 23), (12, 24)), fill=WOOD, outline=OUTLINE)
        draw.rectangle((17, 16, 20, 18), fill=WOOD_DARK if kind == "shotgun" else STEEL_DARK, outline=OUTLINE)
    return image


def ammo(kind):
    image, draw = canvas(AMMO_BADGE)
    shadow(draw, (8, 22, 24, 26))
    if kind == "revolver":
        for offset in (10, 14, 18):
            draw.rectangle((offset, 13, offset + 3, 22), fill=BRASS, outline=OUTLINE)
            draw.polygon(((offset, 13), (offset + 3, 13), (offset + 1, 9)), fill=RED, outline=OUTLINE)
    elif kind == "rifle":
        for box in ((11, 11, 14, 23), (17, 9, 20, 23)):
            draw.rectangle(box, fill=BRASS, outline=OUTLINE)
            draw.polygon(((box[0], box[1]), (box[2], box[1]), ((box[0] + box[2]) // 2, box[1] - 4)), fill=RED, outline=OUTLINE)
    else:
        for x in (11, 17):
            draw.rectangle((x, 12, x + 4, 21), fill=RED, outline=OUTLINE)
            draw.rectangle((x, 18, x + 4, 22), fill=BRASS, outline=OUTLINE)
    return image


def corpse(kind):
    image, draw = canvas(CORPSE_BADGE)
    shadow(draw, (8, 22, 24, 26))
    body = (11, 10, 21, 23) if kind == "beast_large" else (12, 11, 20, 23)
    draw.rounded_rectangle(body, radius=3, fill=STONE, outline=OUTLINE)
    draw.line((body[0] + 2, body[1] + 2, body[2] - 2, body[3] - 2), fill=STONE_DARK, width=2)
    if kind.startswith("beast"):
        draw.polygon(((body[0] + 2, body[1] + 2), (body[0] - 1, body[1]), (body[0] + 2, body[1] + 5)), fill=STONE_DARK, outline=OUTLINE)
        draw.polygon(((body[2] - 2, body[1] + 2), (body[2] + 1, body[1]), (body[2] - 2, body[1] + 5)), fill=STONE_DARK, outline=OUTLINE)
    else:
        draw.ellipse((14, 7, 18, 11), fill=STONE, outline=OUTLINE)
    return image


def render(item_id):
    if item_id == "go_juice":
        return bottle((134, 227, 91, 255), "bolt")
    if item_id == "industrial_medicine":
        return bottle((117, 196, 233, 255), "cross")
    if item_id == "meal_fine":
        return meal(False)
    if item_id == "meal_lavish":
        return meal(True)
    if item_id == "pemmican":
        return ration()
    if item_id == "berries":
        return berry_cluster()
    if item_id == "sword_plasteel":
        return sword(PLASTEEL)
    if item_id == "club_wood":
        return club()
    if item_id == "mace_iron":
        return mace()
    if item_id == "war_hammer_uranium":
        return hammer(URANIUM)
    if item_id == "dagger":
        return dagger()
    if item_id == "spear_wood":
        return spear(WHITE)
    if item_id == "spear_steel":
        return spear(STEEL)
    if item_id == "bow_great":
        return bow()
    if item_id == "shield_steel":
        return shield(False)
    if item_id == "shield_wood":
        return shield(True)
    if item_id == "cloth_shirt":
        return shirt()
    if item_id == "cloth_pants":
        return pants()
    if item_id == "tuque":
        return hat()
    if item_id == "duster":
        return coat(LEATHER, (190, 163, 131, 255))
    if item_id == "parka":
        return coat(BLUE, WHITE)
    if item_id == "devilstrand_duster":
        return coat(RED, (212, 150, 164, 255))
    if item_id == "leather_vest":
        return vest("leather")
    if item_id == "chainmail":
        return vest("chain")
    if item_id == "plate_armor":
        return vest("plate")
    if item_id == "flak_vest":
        return vest("flak")
    if item_id == "iron_helmet":
        return helmet(True)
    if item_id == "steel_helmet":
        return helmet(False)
    if item_id == "leather_boots":
        return boot(False)
    if item_id == "steel_greaves":
        return boot(True)
    if item_id == "gauntlets_iron":
        return gauntlet()
    if item_id == "pickaxe":
        return pickaxe()
    if item_id == "hammer_smith":
        return smith_hammer()
    if item_id == "knife_butcher":
        return butcher_knife()
    if item_id.startswith("mat_"):
        return material(item_id[4:])
    if item_id == "chest_wooden":
        return misc("chest")
    if item_id == "backpack":
        return misc("backpack")
    if item_id == "bedroll":
        return misc("bedroll")
    if item_id == "rope":
        return misc("rope")
    if item_id == "surgery_kit":
        return bottle((201, 73, 84, 255), "cross")
    if item_id == "sidearm_revolver":
        return firearm("revolver")
    if item_id == "bolt_rifle":
        return firearm("rifle")
    if item_id == "pump_shotgun":
        return firearm("shotgun")
    if item_id == "ammo_revolver":
        return ammo("revolver")
    if item_id == "ammo_rifle":
        return ammo("rifle")
    if item_id == "ammo_shell":
        return ammo("shell")
    if item_id == "corpse_human":
        return corpse("human")
    if item_id == "corpse_beast_small":
        return corpse("beast_small")
    if item_id == "corpse_beast_large":
        return corpse("beast_large")
    raise KeyError(item_id)


def main():
    item_lookup = {item["id"]: item for item in json.loads(ITEMS.read_text(encoding="utf-8", errors="replace"))}
    for item_id in ORDER:
        save(render(item_id), item_id)
    sync_manifest(item_lookup)


if __name__ == "__main__":
    main()
