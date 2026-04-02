#!/usr/bin/env python3
"""
Unity → Godot Tileset Converter

从 Unity .asset 文件解析 tile 定义，生成 Godot TileSet 所需的 JSON 映射。

用法：在项目根目录下运行
    python Tools/convert_unity_tileset.py

输出：Tools/tileset_mapping.json
"""

import os
import re
import json
import struct
from pathlib import Path

TILESET_DIR = "FantasyKingdomTileset_Godot"
OUTPUT_JSON = "Tools/tileset_mapping.json"

# ── PNG 工具 ──────────────────────────────────────────────────────────────────

def get_png_size(png_path):
    """从 PNG 文件头读取图像宽高，无需第三方库。"""
    try:
        with open(png_path, 'rb') as f:
            if f.read(8) != b'\x89PNG\r\n\x1a\n':
                return None
            f.read(4)  # IHDR chunk length
            if f.read(4) != b'IHDR':
                return None
            w = struct.unpack('>I', f.read(4))[0]
            h = struct.unpack('>I', f.read(4))[0]
            return w, h
    except Exception:
        return None


def flip_y(rect, img_h):
    """Unity 纹理坐标 Y 轴朝上，转换为 Godot（图像坐标，Y 轴朝下）。"""
    return {
        'x': int(rect['x']),
        'y': int(img_h - rect['y'] - rect['h']),
        'w': int(rect['w']),
        'h': int(rect['h']),
    }

# ── GUID 表 ───────────────────────────────────────────────────────────────────

def read_meta_guid(meta_path):
    try:
        with open(meta_path, 'r', encoding='utf-8', errors='ignore') as f:
            for line in f:
                m = re.match(r'^guid:\s*([a-f0-9]+)', line.strip())
                if m:
                    return m.group(1)
    except Exception:
        pass
    return None


def build_guid_table(root_dir):
    """扫描所有 .meta 文件，建立 GUID → 资源路径字典。"""
    table = {}
    for meta_path in Path(root_dir).rglob('*.meta'):
        asset_path = str(meta_path)[:-5]
        if os.path.exists(asset_path):
            guid = read_meta_guid(str(meta_path))
            if guid:
                table[guid] = str(asset_path).replace('\\', '/')
    return table

# ── PNG .meta 解析 ────────────────────────────────────────────────────────────

def parse_png_meta(meta_path):
    """
    解析 PNG .meta 中的 sprite 信息。
    返回：dict { internalID(str) → {rect, pivot} }，以及 sprite_mode。
    """
    try:
        with open(meta_path, 'r', encoding='utf-8', errors='ignore') as f:
            content = f.read()
    except Exception:
        return {}, 1

    mode_m = re.search(r'spriteMode:\s*(\d+)', content)
    sprite_mode = int(mode_m.group(1)) if mode_m else 1

    sprites = {}

    if sprite_mode != 2:
        # 单 sprite，整张图就是 tile
        sprites['21300000'] = {'rect': None, 'pivot': (0.5, 0.5)}
        return sprites, sprite_mode

    # 解析 spriteSheet.sprites 列表（每条以 "- serializedVersion:" 开头）
    sheet_m = re.search(r'spriteSheet:(.*)', content, re.DOTALL)
    if not sheet_m:
        return sprites, sprite_mode

    sheet = sheet_m.group(1)

    for block in re.split(r'\n\s+- serializedVersion:\s*\d+', sheet):
        name_m  = re.search(r'name:\s*(.+)', block)
        id_m    = re.search(r'internalID:\s*(-?\d+)', block)
        # rect 是嵌套结构：rect:\n  serializedVersion: 2\n  x: ...
        x_m = re.search(r'rect:.*?x:\s*([\d.]+)', block, re.DOTALL)
        y_m = re.search(r'rect:.*?y:\s*([\d.]+)', block, re.DOTALL)
        w_m = re.search(r'rect:.*?width:\s*([\d.]+)', block, re.DOTALL)
        h_m = re.search(r'rect:.*?height:\s*([\d.]+)', block, re.DOTALL)
        piv = re.search(r'pivot:\s*\{x:\s*([\d.]+),\s*y:\s*([\d.]+)\}', block)

        if id_m and x_m and y_m and w_m and h_m:
            sprites[id_m.group(1)] = {
                'rect': {
                    'x': float(x_m.group(1)),
                    'y': float(y_m.group(1)),  # Unity Y：从下到上
                    'w': float(w_m.group(1)),
                    'h': float(h_m.group(1)),
                },
                'pivot': (
                    float(piv.group(1)) if piv else 0.5,
                    float(piv.group(2)) if piv else 0.5,
                ),
            }

    return sprites, sprite_mode

# ── .asset 解析 ───────────────────────────────────────────────────────────────

def parse_static_tile(asset_path):
    """解析标准 Tile .asset（m_Script 必须是内置 Tile 类）。"""
    try:
        with open(asset_path, 'r', encoding='utf-8', errors='ignore') as f:
            content = f.read()
    except Exception:
        return None

    # 内置 Tile 的 script guid 固定为全零 e000...
    if 'guid: 0000000000000000e000000000000000' not in content:
        return None

    name_m    = re.search(r'm_Name:\s*(.+)', content)
    sprite_m  = re.search(r'm_Sprite:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([a-f0-9]+)', content)
    collider_m = re.search(r'm_ColliderType:\s*(\d+)', content)

    if not name_m or not sprite_m:
        return None

    return {
        'name':           name_m.group(1).strip(),
        'sprite_file_id': sprite_m.group(1),
        'sprite_guid':    sprite_m.group(2),
        'collider_type':  int(collider_m.group(1)) if collider_m else 0,
    }


def parse_animated_tile(asset_path):
    """解析 Animated Tile .asset。"""
    try:
        with open(asset_path, 'r', encoding='utf-8', errors='ignore') as f:
            content = f.read()
    except Exception:
        return None

    if 'm_AnimatedSprites' not in content:
        return None

    name_m = re.search(r'm_Name:\s*(.+)', content)
    if not name_m:
        return None

    # 提取帧列表（在 m_AnimatedSprites 和 m_MinSpeed 之间）
    frames_m = re.search(r'm_AnimatedSprites:(.*?)m_MinSpeed', content, re.DOTALL)
    frame_refs = re.findall(r'\{fileID:\s*(-?\d+),\s*guid:\s*([a-f0-9]+)', frames_m.group(1)) if frames_m else []

    fps_m      = re.search(r'm_MinSpeed:\s*([\d.]+)', content)
    collider_m = re.search(r'm_TileColliderType:\s*(\d+)', content)

    return {
        'name':          name_m.group(1).strip(),
        'frames':        [{'file_id': f[0], 'guid': f[1]} for f in frame_refs],
        'fps':           float(fps_m.group(1)) if fps_m else 10.0,
        'collider_type': int(collider_m.group(1)) if collider_m else 0,
    }

# ── GUID 解析 → 实际 PNG 路径 + rect ─────────────────────────────────────────

def resolve_sprite(file_id, guid, guid_table, png_cache):
    """
    把 Unity sprite 引用（fileID + guid）解析成 (png_path, godot_rect)。
    godot_rect = {x, y, w, h}，Y 坐标已翻转为图像坐标系。
    """
    if guid not in guid_table:
        return None, None

    png_path = guid_table[guid]
    if not png_path.lower().endswith('.png'):
        return None, None

    meta_path = png_path + '.meta'
    if not os.path.exists(meta_path):
        return png_path, None

    if png_path not in png_cache:
        sprites, mode = parse_png_meta(meta_path)
        size = get_png_size(png_path)
        png_cache[png_path] = {'sprites': sprites, 'size': size}

    cache    = png_cache[png_path]
    sprites  = cache['sprites']
    img_size = cache['size']

    sprite = sprites.get(file_id) or sprites.get('21300000')
    if sprite is None:
        return png_path, None

    rect = sprite['rect']
    if rect is None:
        # 整张图
        if img_size:
            return png_path, {'x': 0, 'y': 0, 'w': img_size[0], 'h': img_size[1]}
        return png_path, None

    if img_size:
        return png_path, flip_y(rect, img_size[1])

    # 无法获取图像高度时直接返回原始坐标（Y 未翻转）
    return png_path, {'x': int(rect['x']), 'y': int(rect['y']), 'w': int(rect['w']), 'h': int(rect['h'])}

# ── 主流程 ────────────────────────────────────────────────────────────────────

def convert():
    print(f"[1/4] 建立 GUID 索引：{TILESET_DIR}")
    guid_table = build_guid_table(TILESET_DIR)
    print(f"      找到 {len(guid_table)} 个资源")

    png_cache     = {}
    static_tiles  = []
    animated_tiles = []
    failed        = []

    # ── 静态 tile ────────────────────────────────────────
    tiles_dir  = os.path.join(TILESET_DIR, "Environment", "Tiles")
    tile_files = sorted(Path(tiles_dir).glob("*.asset"))
    print(f"\n[2/4] 解析 {len(tile_files)} 个静态 tile...")

    for tf in tile_files:
        data = parse_static_tile(str(tf))
        if not data:
            continue

        png, rect = resolve_sprite(data['sprite_file_id'], data['sprite_guid'], guid_table, png_cache)
        if png and rect:
            static_tiles.append({
                'name':          data['name'],
                'png':           png,
                'rect':          rect,
                'collider_type': data['collider_type'],
            })
        else:
            failed.append({'name': data['name'], 'reason': f'sprite 未解析（png={png}）'})

    # ── 动画 tile ─────────────────────────────────────────
    anim_dir = os.path.join(TILESET_DIR, "Environment", "Animated tiles")
    if os.path.isdir(anim_dir):
        anim_files = sorted(Path(anim_dir).glob("*.asset"))
        print(f"\n[3/4] 解析 {len(anim_files)} 个动画 tile...")

        for af in anim_files:
            data = parse_animated_tile(str(af))
            if not data:
                continue

            frames_out = []
            for frame in data['frames']:
                png, rect = resolve_sprite(frame['file_id'], frame['guid'], guid_table, png_cache)
                if png and rect:
                    frames_out.append({'png': png, 'rect': rect})

            if frames_out:
                animated_tiles.append({
                    'name':          data['name'],
                    'frames':        frames_out,
                    'fps':           data['fps'],
                    'collider_type': data['collider_type'],
                })
            else:
                failed.append({'name': data['name'], 'reason': '无帧数据'})

    # ── 写出 JSON ─────────────────────────────────────────
    print(f"\n[4/4] 写出 {OUTPUT_JSON}...")
    os.makedirs(os.path.dirname(OUTPUT_JSON), exist_ok=True)
    output = {
        'static_tiles':   static_tiles,
        'animated_tiles': animated_tiles,
        'failed':         failed,
    }
    with open(OUTPUT_JSON, 'w', encoding='utf-8') as f:
        json.dump(output, f, indent=2, ensure_ascii=False)

    print(f"\n完成！")
    print(f"  静态 tile：  {len(static_tiles)}")
    print(f"  动画 tile：  {len(animated_tiles)}")
    print(f"  失败：       {len(failed)}")
    if failed:
        for item in failed[:5]:
            print(f"    - {item['name']}: {item['reason']}")
        if len(failed) > 5:
            print(f"    ... 还有 {len(failed) - 5} 条，见 JSON failed 字段")


if __name__ == '__main__':
    convert()
