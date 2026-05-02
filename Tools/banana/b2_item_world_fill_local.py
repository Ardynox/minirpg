"""
B2 本地兜底：API 不可用时写入 `category_*.png`（64×64，透明底 + 土色块 + 首字母），
与 `Data/item_world_render.json` 的 texture 路径一致。

    python Tools/banana/b2_item_world_fill_local.py
    python Tools/banana/b2_item_world_fill_local.py --force
"""
from __future__ import annotations

import argparse
import hashlib
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "Assets" / "Art" / "Placeholders" / "item_world"
LOG = Path(__file__).resolve().parent / ".cache" / "b2_item_world_fill.log"

CATEGORIES = [
    "ammo",
    "armor",
    "clothing",
    "consumable",
    "food",
    "material",
    "misc",
    "tool",
    "weapon",
]


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        fh.write(f"{datetime.now(timezone.utc).isoformat()}  {line}\n")
    print(line, flush=True)


def _draw(path: Path, cat: str, size: int = 64) -> None:
    from PIL import Image, ImageDraw, ImageFont

    h = int(hashlib.sha256(cat.encode()).hexdigest(), 16)
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    stroke = (52, 42, 34, 255)
    margin = max(2, size // 16)
    dr.rounded_rectangle(
        [margin, margin, size - margin - 1, size - margin - 1],
        radius=max(3, size // 14),
        outline=stroke,
        width=max(2, size // 32),
        fill=(140 + (h % 60), 120 + ((h >> 8) % 50), 95 + ((h >> 16) % 40), 220),
    )
    letter = cat[:1].upper()
    try:
        font = ImageFont.truetype("arial.ttf", size=size // 2)
    except OSError:
        font = ImageFont.load_default()
    bbox = dr.textbbox((0, 0), letter, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    dr.text(
        ((size - tw) // 2, (size - th) // 2 - 2),
        letter,
        fill=(36, 28, 22, 255),
        font=font,
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()

    _log("===== b2_item_world_fill_local =====")
    for cat in CATEGORIES:
        p = OUT_DIR / f"category_{cat}.png"
        if p.exists() and not args.force:
            _log(f"[skip] {p.name}")
            continue
        _draw(p, cat)
        _log(f"[ok]   {p.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
