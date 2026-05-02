"""
B7 本地兜底：为尚未落盘的 surgery/room/capacity/limb 占位 PNG 生成统一风格的简单矢量风图
（透明底 + 土色描边，无文字）。用于 API 余额不足或 banana 卡住时把批次补齐到 39/39。

    python Tools/banana/b7_ui_icons_fill_local.py
    python Tools/banana/b7_ui_icons_fill_local.py --force   # 覆盖已存在（慎用）
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "Assets" / "Art" / "Placeholders" / "ui_icons"
LOG = Path(__file__).resolve().parent / ".cache" / "b7_driver.log"

# 与 b7_ui_icons_driver 一致
LIMB_KEYS = [
    "head",
    "neck",
    "torso",
    "left_arm",
    "right_arm",
    "left_leg",
    "right_leg",
    "heart_lungs",
    "eyes",
    "hands",
]


def _load_ids(path: Path) -> list[str]:
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    return [str(x["id"]) for x in data if isinstance(x, dict) and x.get("id")]


def expected_paths() -> list[tuple[Path, int]]:
    rows: list[tuple[Path, int]] = []
    for oid in _load_ids(ROOT / "Data" / "surgery_operations.json"):
        rows.append((OUT_DIR / f"surgery_{oid}.png", 96))
    for rid in _load_ids(ROOT / "Data" / "room_roles.json"):
        rows.append((OUT_DIR / f"room_{rid}.png", 96))
    for cid in _load_ids(ROOT / "Data" / "capacities.json"):
        rows.append((OUT_DIR / f"capacity_{cid}.png", 64))
    for key in LIMB_KEYS:
        rows.append((OUT_DIR / f"limb_{key}.png", 96))
    return rows


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        fh.write(f"{datetime.now(timezone.utc).isoformat()}  {line}\n")
    print(line, flush=True)


def _draw_placeholder(path: Path, size: int, stem: str) -> None:
    from PIL import Image, ImageDraw

    h = int(hashlib.sha256(stem.encode()).hexdigest(), 16)
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    stroke = (52, 42, 34, 255)
    fill = (168, 138, 108, 230)
    margin = int(size * 0.08)
    dr.rounded_rectangle(
        [margin, margin, size - margin, size - margin],
        radius=max(4, size // 12),
        outline=stroke,
        width=max(2, size // 48),
        fill=(210, 198, 186, 120),
    )
    cx, cy = size // 2, size // 2
    r = size // 5
    n = 3 + (h % 5)
    for i in range(n):
        ang = (i / n) * math.tau + (h % 360) * 0.01
        x = cx + int(r * 0.7 * math.cos(ang))
        y = cy + int(r * 0.7 * math.sin(ang))
        dr.ellipse([x - r // 3, y - r // 3, x + r // 3, y + r // 3], fill=fill, outline=stroke, width=1)
    dr.line([cx - r, cy + r, cx + r, cy - r], fill=stroke, width=max(2, size // 32))
    img.save(path, format="PNG")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true", help="overwrite existing PNGs")
    args = ap.parse_args()

    try:
        from PIL import Image  # noqa: F401
    except ImportError:
        print("需要 Pillow：pip install Pillow", file=sys.stderr)
        return 1

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    filled = 0
    skipped = 0
    for path, size in expected_paths():
        if path.exists() and not args.force:
            skipped += 1
            continue
        _draw_placeholder(path, size, path.stem)
        filled += 1
        _log(f"[fill-local] wrote {path.relative_to(ROOT)} {size}px")

    _log(f"===== b7_ui_icons_fill_local: filled={filled} skipped_existing={skipped} force={args.force} =====")
    print(f"filled={filled} skipped={skipped}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
