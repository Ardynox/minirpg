from __future__ import annotations

import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA_DIR = ROOT / "Data"
I18N_DIR = DATA_DIR / "I18n"

UNSUPPORTED_CHAR_REPLACEMENTS = {
    "\ufe0f": "",
    "⚔": "C",
    "⚙": "U",
    "✦": "S",
    "✗": "x",
    "⛏": "",
    "🛡": "",
}

ZH_OVERRIDES = {
    "ui.skill.summary": "[color=#ff6666]战斗: {combat}[/color]  [color=#66ccff]工具: {utility}[/color]  [color=#66ff88]社交: {social}[/color]  [color=#888888]总计: {total}[/color]",
    "ui.common.tab.all": "全部",
    "ui.skill.detail.power_inline": "威力 {value}",
    "ui.skill.detail.cooldown_inline": "冷却 {value}",
    "ui.skill.detail.range_inline": "射程 {value}",
    "ui.skill.detail.category_line": "{category}技能",
    "ui.skill.detail.power": "威力: {value}",
    "ui.skill.detail.cooldown": "冷却: {value} 回合",
    "ui.skill.detail.range": "射程: {value}",
    "ui.skill.detail.damage_type": "伤害类型: {value}",
    "ui.skill.detail.effect": "效果: {value}",
    "ui.skill.detail.requirements": "--- 属性需求 ---",
    "ui.skill.detail.capacity_requirements": "--- 能力需求 ---",
    "ui.skill.detail.source": "来源: {value}",
    "ui.skill.source.equipment": "已装备“{item}”",
    "ui.skill.source.innate": "天生能力",
    "ui.skill_manager.empty": "暂无技能",
    "ui.status.tab.limb": "肢体",
    "ui.status.tab.capacity": "能力",
    "ui.status.tab.tag": "标签",
    "ui.status.tab.equip": "装备",
    "ui.status.empty.limbs": "无肢体 - 致命状态",
    "ui.common.none": "无",
    "ui.status.capacity.effect.death_instant": "致命",
    "ui.status.capacity.effect.incapacitate": "昏迷",
    "ui.status.capacity.effect.death_slow": "濒死",
    "ui.status.buff.permanent": "永久",
    "ui.status.buff.turns": "{value} 回合",
    "ui.status.equip.section": "-- {limb} --",
    "ui.status.empty.equipment": "无装备",
    "ui.status.equip.weight": "负重: {current}/{max}kg",
    "ui.status.equip.overweight": "超重！",
    "ui.status.header.meta": "金币: {gold}G  深度: Z{floor}  回合: {turn}",
    "dig.failure.world_uninitialized": "世界尚未初始化",
    "dig.failure.not_solid": "目标不是实体地形",
    "dig.failure.indestructible": "该地形无法被破坏",
    "dig.failure.material_mismatch": "此技能无法作用于{material}地形",
    "dig.failure.no_power": "你的威力不足以破坏该地形",
    "render.view_mode.single_layer": "单层视图",
    "render.view_mode.multi_layer": "多层预览",
    "dialog.fallback.silence": "{name}: \"...\"",
    "log.save.loaded_short": "存档已加载",
    "log.inventory.empty": "背包为空",
    "ui.common.detail_hint.skill": "[color=#888888]选择一个技能查看详情[/color]",
    "ui.panel_chrome.button.settings": "设置",
    "ui.panel_chrome.button.close": "关闭",
    "ui.panel_chrome.popup.title": "面板设置",
    "ui.panel_chrome.popup.width": "宽度 (px)",
    "ui.panel_chrome.popup.height": "高度 (px)",
    "ui.panel_chrome.popup.button_scale": "按钮缩放",
    "ui.panel_chrome.popup.reset": "重置默认",
    "ui.inventory.header.meta": "金币: {gold}G  [color={weight_color}]负重: {current_weight}/{max_weight}kg[/color]  ",
}

EN_OVERRIDES = {
    "log.save.loaded_short": "Save loaded",
    "ui.panel_chrome.button.settings": "Settings",
    "ui.panel_chrome.button.close": "Close",
    "ui.panel_chrome.popup.title": "Panel Settings",
    "ui.panel_chrome.popup.width": "Width (px)",
    "ui.panel_chrome.popup.height": "Height (px)",
    "ui.panel_chrome.popup.button_scale": "Button Scale",
    "ui.panel_chrome.popup.reset": "Reset Default",
    "ui.inventory.header.meta": "Gold: {gold}G  [color={weight_color}]Weight: {current_weight}/{max_weight}kg[/color]  ",
}

TERRAIN_NAMES_ZH = {
    "void": "空无",
    "floor": "地板",
    "wall_soil": "土墙",
    "wall_stone": "石墙",
    "wall_granite": "花岗岩墙",
    "wall_obsidian": "黑曜石墙",
    "rubble": "碎石",
    "grass": "草地",
    "water": "水面",
    "tree": "树木",
    "lava": "熔岩",
    "sand": "沙地",
    "mountain": "山地",
    "swamp": "沼泽",
    "snow": "积雪",
    "ice": "冰面",
    "marsh": "湿地",
    "gravel": "砾地",
    "wall_iron": "铁墙",
    "fungus": "菌丛",
    "crystal_vein": "水晶矿脉",
}

TERRAIN_NAMES_EN = {
    "void": "Void",
    "floor": "Floor",
    "wall_soil": "Soil Wall",
    "wall_stone": "Stone Wall",
    "wall_granite": "Granite Wall",
    "wall_obsidian": "Obsidian Wall",
    "rubble": "Rubble",
    "grass": "Grass",
    "water": "Water",
    "tree": "Tree",
    "lava": "Lava",
    "sand": "Sand",
    "mountain": "Mountain",
    "swamp": "Swamp",
    "snow": "Snow",
    "ice": "Ice",
    "marsh": "Marsh",
    "gravel": "Gravel",
    "wall_iron": "Iron Wall",
    "fungus": "Fungus",
    "crystal_vein": "Crystal Vein",
}


def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, data: dict[str, str]) -> None:
    path.write_text(
        json.dumps(data, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


def sanitize_value(value: str) -> str:
    sanitized = value
    for original, replacement in UNSUPPORTED_CHAR_REPLACEMENTS.items():
        sanitized = sanitized.replace(original, replacement)

    sanitized = "".join(ch for ch in sanitized if ord(ch) <= 0xFFFF)
    sanitized = re.sub(r" {3,}", "  ", sanitized)
    return sanitized.rstrip()


def sync_named_values(target: dict[str, str], prefix: str, rows: list[dict], value_key: str) -> None:
    for row in rows:
        row_id = row.get("id")
        value = row.get(value_key)
        if not row_id or not value:
            continue
        target[f"{prefix}.{row_id}.{value_key.lower()}"] = value


def sync_items(target: dict[str, str]) -> None:
    for row in load_json(DATA_DIR / "items.json"):
        row_id = row.get("id")
        name = row.get("name")
        if row_id and name:
            target[f"data.item.{row_id}.name"] = name


def sync_actors(target: dict[str, str]) -> None:
    for row in load_json(DATA_DIR / "actors.json"):
        row_id = row.get("id")
        name = row.get("displayName")
        if row_id and name:
            target[f"data.actor.{row_id}.display_name"] = name


def sync_name_rows(target: dict[str, str], file_name: str, prefix: str) -> None:
    for row in load_json(DATA_DIR / file_name):
        row_id = row.get("id")
        name = row.get("name")
        if row_id and name:
            target[f"{prefix}.{row_id}.name"] = name


def sync_capacities(target: dict[str, str]) -> None:
    for row in load_json(DATA_DIR / "capacities.json"):
        row_id = row.get("id")
        name = row.get("name")
        description = row.get("description")
        if row_id and name:
            target[f"data.capacity.{row_id}.name"] = name
        if row_id and description:
            target[f"data.capacity.{row_id}.description"] = description


def sync_interactions(target: dict[str, str]) -> None:
    for row in load_json(DATA_DIR / "interactions.json"):
        row_id = row.get("id")
        name = row.get("name")
        description = row.get("description")
        if row_id and name:
            target[f"data.interaction.{row_id}.name"] = name
        if row_id and description:
            target[f"data.interaction.{row_id}.description"] = description


def sync_item_categories(target: dict[str, str]) -> None:
    for row in load_json(DATA_DIR / "item_categories.json"):
        row_id = row.get("id")
        name = row.get("name")
        if row_id and name:
            target[f"data.item_category.{row_id}.name"] = name


def sync_dialog_entries(target: dict[str, str]) -> None:
    for dialog_set in load_json(DATA_DIR / "dialogs.json"):
        for entry in dialog_set.get("entries", []):
            entry_id = entry.get("id")
            if not entry_id:
                continue
            template = entry.get("template")
            if template:
                target[f"dialog.entry.{entry_id}.template"] = template
            for index, option in enumerate(entry.get("options", [])):
                option_text = option.get("text")
                if option_text:
                    target[f"dialog.entry.{entry_id}.option.{index}"] = option_text


def sync_terrain_names(target: dict[str, str], names: dict[str, str]) -> None:
    for terrain_id, name in names.items():
        target[f"data.terrain.{terrain_id}.name"] = name


def sync_chinese_catalog(target: dict[str, str]) -> None:
    sync_items(target)
    sync_actors(target)
    sync_name_rows(target, "races.json", "data.race")
    sync_name_rows(target, "professions.json", "data.profession")
    sync_name_rows(target, "limbs.json", "data.limb")
    sync_name_rows(target, "materials.json", "data.material")
    sync_capacities(target)
    sync_interactions(target)
    sync_item_categories(target)
    sync_dialog_entries(target)
    sync_terrain_names(target, TERRAIN_NAMES_ZH)


def sync_english_catalog(target: dict[str, str]) -> None:
    sync_terrain_names(target, TERRAIN_NAMES_EN)


def apply_overrides(target: dict[str, str], overrides: dict[str, str]) -> None:
    for key, value in overrides.items():
        target[key] = value


def sanitize_catalog(target: dict[str, str]) -> None:
    for key, value in list(target.items()):
        target[key] = sanitize_value(value)


def main() -> int:
    zh_path = I18N_DIR / "zh_CN.json"
    en_path = I18N_DIR / "en.json"

    zh = load_json(zh_path)
    en = load_json(en_path)

    sync_chinese_catalog(zh)
    sync_english_catalog(en)
    apply_overrides(zh, ZH_OVERRIDES)
    apply_overrides(en, EN_OVERRIDES)
    sanitize_catalog(zh)
    sanitize_catalog(en)

    missing_in_zh = sorted(set(en) - set(zh))
    missing_in_en = sorted(set(zh) - set(en))
    if missing_in_zh or missing_in_en:
        print(f"Missing in zh_CN: {len(missing_in_zh)}")
        for key in missing_in_zh[:20]:
            print(f"  {key}")
        print(f"Missing in en: {len(missing_in_en)}")
        for key in missing_in_en[:20]:
            print(f"  {key}")
        return 1

    write_json(zh_path, zh)
    write_json(en_path, en)
    print(f"Synced {len(zh)} localization keys.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
