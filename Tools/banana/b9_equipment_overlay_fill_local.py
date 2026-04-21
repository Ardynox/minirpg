"""
B9 本地兜底：在 `Assets/Art/Generated/equipment_overlays/` 写入 88 张 256×256 透明 PNG
（5 武器 + 3 披风 + 3 头盔）× 8 向，与 `EquipmentOverlayPaths` / `MapSpriteRuntimeFactory` 命名一致。

占位为透明底 + 土色线框小图形（每类不同剪影），便于验收 B9 管线；后续可用 Banana 按
`Artifacts/style_locks_B9.md` 逐张替换。

    python Tools/banana/b9_equipment_overlay_fill_local.py
    python Tools/banana/b9_equipment_overlay_fill_local.py --force
"""
from __future__ import annotations

import argparse
import hashlib
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "Assets" / "Art" / "Generated" / "equipment_overlays"
LOG = Path(__file__).resolve().parent / ".cache" / "b9_equipment_overlay_fill.log"
SIZE = 256

# 与 EquipmentOverlayPaths / DirectionalSpriteHelper 行序一致
DIRS = ["s", "sw", "w", "nw", "n", "ne", "e", "se"]


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        fh.write(f"{datetime.now(timezone.utc).isoformat()}  {line}\n")
    print(line, flush=True)


def _palette(stem: str) -> tuple[tuple[int, int, int, int], tuple[int, int, int, int]]:
    h = int(hashlib.sha256(stem.encode()).hexdigest(), 16)
    fill = (140 + (h % 50), 120 + ((h >> 8) % 45), 95 + ((h >> 16) % 50), 200)
    stroke = (52, 42, 34, 255)
    return fill, stroke


def _draw_weapon(path: Path, slug: str, d: str) -> None:
    from PIL import Image, ImageDraw

    stem = f"weapon_{slug}_{d}"
    fill, stroke = _palette(stem)
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    cx, cy = SIZE // 2, SIZE // 2 + 24
    if slug == "sword":
        dr.rectangle([cx + 28, cy - 48, cx + 34, cy + 8], outline=stroke, width=2, fill=fill)
        dr.rectangle([cx + 24, cy - 8, cx + 38, cy - 4], outline=stroke, width=2, fill=fill)
    elif slug == "axe":
        dr.rectangle([cx + 28, cy - 40, cx + 32, cy + 4], outline=stroke, width=2, fill=fill)
        dr.polygon([(cx + 32, cy - 44), (cx + 48, cy - 36), (cx + 32, cy - 28)], outline=stroke, width=2, fill=fill)
    elif slug == "bow":
        dr.arc([cx + 10, cy - 52, cx + 46, cy - 8], 200, 340, fill=fill, width=3)
        dr.line([(cx + 28, cy - 48), (cx + 28, cy + 4)], fill=stroke, width=2)
    elif slug == "staff":
        dr.rectangle([cx + 30, cy - 52, cx + 34, cy + 12], outline=stroke, width=2, fill=fill)
        dr.ellipse([cx + 26, cy - 58, cx + 38, cy - 46], outline=stroke, width=2, fill=fill)
    else:  # mace
        dr.rectangle([cx + 30, cy - 36, cx + 34, cy + 6], outline=stroke, width=2, fill=fill)
        dr.ellipse([cx + 24, cy - 44, cx + 40, cy - 28], outline=stroke, width=2, fill=fill)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def _draw_cloak(path: Path, slug: str, d: str) -> None:
    from PIL import Image, ImageDraw

    stem = f"cloak_{slug}_{d}"
    fill, stroke = _palette(stem)
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    cx = SIZE // 2
    body_top, body_bot = 72, 200
    if slug == "short":
        dr.rounded_rectangle([cx - 44, body_top, cx + 44, body_top + 56], radius=6, outline=stroke, width=2, fill=fill)
    elif slug == "long":
        dr.rounded_rectangle([cx - 46, body_top, cx + 46, body_bot], radius=8, outline=stroke, width=2, fill=fill)
    else:  # hooded
        dr.ellipse([cx - 36, body_top - 28, cx + 36, body_top + 36], outline=stroke, width=2, fill=fill)
        dr.rounded_rectangle([cx - 48, body_top + 8, cx + 48, body_bot], radius=8, outline=stroke, width=2, fill=fill)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def _draw_helmet(path: Path, slug: str, d: str) -> None:
    from PIL import Image, ImageDraw

    stem = f"helmet_{slug}_{d}"
    fill, stroke = _palette(stem)
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    cx, hc = SIZE // 2, 108
    if slug == "cap":
        dr.ellipse([cx - 40, hc - 36, cx + 40, hc + 8], outline=stroke, width=2, fill=fill)
    elif slug == "full":
        dr.ellipse([cx - 42, hc - 40, cx + 42, hc + 20], outline=stroke, width=2, fill=fill)
        dr.rectangle([cx - 8, hc + 4, cx + 8, hc + 10], fill=(20, 18, 16, 220))
    else:  # crown
        dr.rectangle([cx - 28, hc - 52, cx + 28, hc - 36], outline=stroke, width=2, fill=fill)
        for x in range(-20, 24, 10):
            dr.polygon([(cx + x, hc - 52), (cx + x + 5, hc - 52), (cx + x + 2, hc - 62)], outline=stroke, width=2, fill=fill)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def expected_files() -> list[Path]:
    paths: list[Path] = []
    for slug in ("sword", "axe", "bow", "staff", "mace"):
        for d in DIRS:
            paths.append(OUT_DIR / f"equip_weapon_{slug}_{d}.png")
    for slug in ("short", "long", "hooded"):
        for d in DIRS:
            paths.append(OUT_DIR / f"equip_cloak_{slug}_{d}.png")
    for slug in ("cap", "full", "crown"):
        for d in DIRS:
            paths.append(OUT_DIR / f"equip_helmet_{slug}_{d}.png")
    return paths


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()

    _log(f"===== b9_equipment_overlay_fill_local SIZE={SIZE} =====")
    wrote = 0
    for slug in ("sword", "axe", "bow", "staff", "mace"):
        for d in DIRS:
            p = OUT_DIR / f"equip_weapon_{slug}_{d}.png"
            if p.is_file() and not args.force:
                _log(f"[skip] {p.relative_to(ROOT)}")
                continue
            _draw_weapon(p, slug, d)
            _log(f"[ok]   {p.relative_to(ROOT)}")
            wrote += 1
    for slug in ("short", "long", "hooded"):
        for d in DIRS:
            p = OUT_DIR / f"equip_cloak_{slug}_{d}.png"
            if p.is_file() and not args.force:
                _log(f"[skip] {p.relative_to(ROOT)}")
                continue
            _draw_cloak(p, slug, d)
            _log(f"[ok]   {p.relative_to(ROOT)}")
            wrote += 1
    for slug in ("cap", "full", "crown"):
        for d in DIRS:
            p = OUT_DIR / f"equip_helmet_{slug}_{d}.png"
            if p.is_file() and not args.force:
                _log(f"[skip] {p.relative_to(ROOT)}")
                continue
            _draw_helmet(p, slug, d)
            _log(f"[ok]   {p.relative_to(ROOT)}")
            wrote += 1
    _log(f"===== done wrote={wrote} expected={len(expected_files())} =====")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
