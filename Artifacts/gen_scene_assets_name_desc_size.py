import csv
import json
import struct
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

# 地形逻辑 ID -> 简短中文（顶面贴图，等距格用）
TERRAIN_ZH = {
    "crystal_vein": "水晶矿脉地表顶面",
    "wall_soil": "土墙顶面",
    "wall_obsidian": "黑曜石墙顶面",
    "wall_iron": "铁墙顶面",
    "wall_granite": "花岗岩墙顶面",
    "tree": "树木占据格顶面",
    "swamp": "沼泽地表顶面",
    "stone": "石头地表顶面",
    "snow": "雪地顶面",
    "sand": "沙地顶面",
    "rubble": "碎石地表顶面",
    "ore_iron": "铁矿脉顶面",
    "ore_gold": "金矿脉顶面",
    "ore_crystal": "水晶矿顶面（与矿脉同图）",
    "ore_copper": "铜矿顶面",
    "ore_coal": "煤矿顶面",
    "mountain": "山体顶面",
    "marsh": "湿地/沼泽变体顶面",
    "lava": "熔岩顶面",
    "ice": "冰面顶面",
    "gravel": "砂砾地面顶面",
    "grass_block": "草方块顶面",
    "grass": "草地顶面",
    "fungus": "菌类覆盖顶面",
    "floor": "室内/石板地面顶面",
    "dirt": "泥土顶面",
    "wall_stone": "石墙顶面",
    "water": "水面顶面",
}

FIXTURE_ZH = {
    "stair_down": "向下楼梯（家具贴图）",
    "market_stall": "市场摊位/柜台",
    "herbal_bench": "草药工作台",
    "loom": "织布机",
    "smithy": "锻造炉/铁匠台",
    "butcher_table": "屠宰/加工高桌",
    "stove": "炉灶",
    "dormitory_bed": "集体床铺",
    "bed": "单人床",
    "fire_brazier": "火盆",
    "shelf": "架子",
    "ladder": "梯子",
    "house": "房屋屋顶占位",
    "door": "门",
    "nest": "巢穴占位",
    "stair_up": "向上楼梯",
    "campfire": "营火（静态首帧）",
    "fire": "火焰源（静态首帧）",
}

ITEM_ZH = {
    "container": "地上容器默认外观（tile）",
    "drop": "掉落物/杂物堆默认外观（tile）",
}

FX_ZH = {
    "fire_small": "小火焰动画",
    "fire_medium": "中火焰动画",
    "fire_large": "大火焰动画",
}

PLACEHOLDER_ZH = {
    "blood_splatter_large": "地面大血迹贴花",
    "blood_splatter_small": "地面小血迹贴花",
    "corpse_stain_beast": "野兽尸体压痕",
    "corpse_stain_humanoid": "人形尸体压痕",
    "door_open": "门开启状态示意",
    "footprint_blood_n": "血脚印朝北",
    "footprint_blood_s": "血脚印朝南",
    "footprint_mud_e": "泥脚印朝东",
    "footprint_mud_n": "泥脚印朝北",
    "footprint_mud_s": "泥脚印朝南",
    "footprint_mud_w": "泥脚印朝西",
    "footprint_snow_e": "雪脚印朝东",
    "footprint_snow_n": "雪脚印朝北",
    "footprint_snow_s": "雪脚印朝南",
    "footprint_snow_w": "雪脚印朝西",
    "glow_campfire": "营火光晕",
    "glow_candle": "烛光光晕",
    "glow_lantern": "提灯光晕",
    "glow_torch": "火把光晕",
    "smoke_plume": "烟柱",
    "category_ammo": "地上物品分类默认图·弹药",
    "category_armor": "地上物品分类默认图·护甲",
    "category_clothing": "地上物品分类默认图·衣物",
    "category_consumable": "地上物品分类默认图·消耗品",
    "category_food": "地上物品分类默认图·食物",
    "category_material": "地上物品分类默认图·材料",
    "category_misc": "地上物品分类默认图·杂项",
    "category_tool": "地上物品分类默认图·工具",
    "category_weapon": "地上物品分类默认图·武器",
    "mat_bark_stack": "地上材料堆·树皮",
    "mat_bone_stack": "地上材料堆·骨头",
    "mat_chitin_stack": "地上材料堆·甲壳",
    "mat_cloth_stack": "地上材料堆·布料",
    "mat_crystal_stack": "地上材料堆·水晶",
    "mat_devilstrand_stack": "地上材料堆·魔丝",
    "mat_flesh_stack": "地上材料堆·肉/组织",
    "mat_gold_stack": "地上材料堆·金",
    "mat_granite_stack": "地上材料堆·花岗岩",
    "mat_horn_stack": "地上材料堆·角",
    "mat_iron_stack": "地上材料堆·铁",
    "mat_jade_stack": "地上材料堆·玉",
    "mat_leather_stack": "地上材料堆·皮革",
    "mat_obsidian_stack": "地上材料堆·黑曜石",
    "mat_plasteel_stack": "地上材料堆·塑钢",
    "mat_scale_stack": "地上材料堆·鳞片",
    "mat_silk_stack": "地上材料堆·丝绸",
    "mat_silver_stack": "地上材料堆·银",
    "mat_slime_stack": "地上材料堆·黏液",
    "mat_soil_stack": "地上材料堆·土壤",
    "mat_steel_stack": "地上材料堆·钢",
    "mat_stone_stack": "地上材料堆·石块",
    "mat_uranium_stack": "地上材料堆·铀",
    "mat_wood_stack": "地上材料堆·木材",
}


def png_size(p: Path):
    try:
        with open(p, "rb") as f:
            if f.read(8) != b"\x89PNG\r\n\x1a\n":
                return None
            ln = struct.unpack(">I", f.read(4))[0]
            typ = f.read(4)
            if typ != b"IHDR":
                return None
            data = f.read(ln)
            w, h = struct.unpack(">II", data[:8])
            return w, h
    except OSError:
        return None


def _terrain_desc(ids: list[str]) -> str:
    parts = [TERRAIN_ZH.get(i, i) for i in sorted(ids)]
    return "等距地形顶面贴图，用于：" + "；".join(parts)


def _fixture_desc(ids: list[str]) -> str:
    parts = [FIXTURE_ZH.get(i, i) for i in sorted(ids)]
    return "场景家具/设施贴图：" + "；".join(parts)


def _fx_desc(fx_id: str, frame: int) -> str:
    base = FX_ZH.get(fx_id, fx_id)
    return f"{base} 第{frame}帧（共4帧需风格与轮廓一致）"


def build_rows() -> list[dict[str, str]]:
    with open(ROOT / "Data/pz_world_visual_registry.json", "r", encoding="utf-8") as f:
        pz = json.load(f)
    with open(ROOT / "Data/tile_mapping.json", "r", encoding="utf-8") as f:
        tm = json.load(f)

    labels: list[tuple[str, str, str]] = []
    for t in pz["terrain"]:
        labels.append(("terrain", t["terrainId"], t["topPath"]))
    for k, v in sorted(tm["fixture"].items()):
        if str(v).startswith("res://"):
            labels.append(("fixture", k, v))
    for k, v in sorted(tm["item"].items()):
        if str(v).startswith("res://"):
            labels.append(("item", k, v))
    for x in pz["fx"]:
        for i, p in enumerate(x.get("paths") or []):
            labels.append(("fx_frame", f"{x['id']}:{i}", p))

    by_path: dict[str, list[tuple[str, str]]] = defaultdict(list)
    for kind, lid, rp in labels:
        rel = rp.replace("res://Assets/Art/", "")
        fp = ROOT / "Assets" / "Art" / rel
        by_path[str(fp)].append((kind, lid))

    ph_roots = [
        ROOT / "Assets/Art/Placeholders/detail",
        ROOT / "Assets/Art/Placeholders/item_world",
        ROOT / "Assets/Art/Placeholders/material_world",
    ]
    for folder in ph_roots:
        if not folder.is_dir():
            continue
        for p in sorted(folder.glob("*.png")):
            by_path[str(p)].append(("placeholder", p.stem))

    rows: list[dict[str, str]] = []
    for fp_str in sorted(by_path.keys()):
        fp = Path(fp_str)
        wh = png_size(fp)
        size_s = f"{wh[0]}×{wh[1]}" if wh else "（文件缺失或无法读取）"
        kinds = {k for k, _ in by_path[fp_str]}
        lids = [lid for k, lid in by_path[fp_str]]

        if "terrain" in kinds:
            tids = sorted({lid for k, lid in by_path[fp_str] if k == "terrain"})
            name = "地形顶面_" + "_".join(tids)
            desc = _terrain_desc(tids)
        elif "fixture" in kinds and "fx_frame" not in kinds:
            fids = sorted({lid for k, lid in by_path[fp_str] if k == "fixture"})
            name = "场景家具_" + "_".join(fids)
            desc = _fixture_desc(fids)
        elif "fx_frame" in kinds:
            fx_ids = sorted({lid.split(":")[0] for k, lid in by_path[fp_str] if k == "fx_frame"})
            fids = sorted({lid for k, lid in by_path[fp_str] if k == "fixture"})
            frames = sorted(
                int(lid.split(":")[1])
                for k, lid in by_path[fp_str]
                if k == "fx_frame" and ":" in lid
            )
            frame = frames[0] if frames else 0
            fx_one = fx_ids[0] if fx_ids else "fire"
            if fids:
                name = f"火与设施_{'_'.join(fids)}_{fp.stem}"
                desc = _fixture_desc(fids) + "；同时 " + _fx_desc(fx_one, frame).replace(
                    f"第{frame}帧", "序列含多帧"
                )
            else:
                name = f"场景FX_{fx_one}_{fp.stem}"
                desc = _fx_desc(fx_one, frame)
        elif "item" in kinds:
            iids = sorted({lid for k, lid in by_path[fp_str] if k == "item"})
            name = "地图物品tile_" + "_".join(iids)
            desc = "；".join(ITEM_ZH.get(i, i) for i in iids)
        elif "placeholder" in kinds:
            stem = lids[-1] if lids else fp.stem
            name = f"占位_{stem}"
            desc = PLACEHOLDER_ZH.get(stem, f"场景占位图（{fp.parent.name}/{stem}）")
        else:
            name = fp.stem
            desc = "未分类场景相关贴图"

        rows.append(
            {
                "resource_name": name,
                "description": desc,
                "size_px": size_s,
            }
        )
    return rows


def main() -> None:
    rows = build_rows()
    out = ROOT / "Artifacts/scene_assets_name_desc_size.csv"
    with open(out, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["resource_name", "description", "size_px"])
        w.writeheader()
        w.writerows(rows)
    print("wrote", out, "rows", len(rows))


if __name__ == "__main__":
    main()
