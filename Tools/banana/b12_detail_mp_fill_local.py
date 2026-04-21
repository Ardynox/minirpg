"""
B12 本地占位：`Assets/Art/Placeholders/detail/` 20 张 + `mp/` 8 张（身份卡 §B12）。
无 Godot API；透明底 + 土色线框风，与 b7/b9 兜底一致。运行时接线可后续再接 `res://` 路径。

    python Tools/banana/b12_detail_mp_fill_local.py
    python Tools/banana/b12_detail_mp_fill_local.py --force
"""
from __future__ import annotations

import argparse
import hashlib
import math
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DETAIL = ROOT / "Assets" / "Art" / "Placeholders" / "detail"
MP = ROOT / "Assets" / "Art" / "Placeholders" / "mp"
LOG = Path(__file__).resolve().parent / ".cache" / "b12_detail_mp_fill.log"

DETAIL_FILES = [
    "footprint_mud_n.png",
    "footprint_mud_s.png",
    "footprint_mud_e.png",
    "footprint_mud_w.png",
    "footprint_snow_n.png",
    "footprint_snow_s.png",
    "footprint_snow_e.png",
    "footprint_snow_w.png",
    "footprint_blood_n.png",
    "footprint_blood_s.png",
    "blood_splatter_small.png",
    "blood_splatter_large.png",
    "corpse_stain_humanoid.png",
    "corpse_stain_beast.png",
    "glow_campfire.png",
    "glow_candle.png",
    "glow_lantern.png",
    "glow_torch.png",
    "door_open.png",
    "smoke_plume.png",
]


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        fh.write(f"{datetime.now(timezone.utc).isoformat()}  {line}\n")
    print(line, flush=True)


def _palette(stem: str) -> tuple[tuple[int, int, int, int], tuple[int, int, int, int]]:
    h = int(hashlib.sha256(stem.encode()).hexdigest(), 16)
    fill = (120 + (h % 60), 95 + ((h >> 8) % 50), 80 + ((h >> 16) % 45), 200)
    stroke = (48, 40, 34, 255)
    return fill, stroke


def _draw_footprint(path: Path, name: str, size: int = 96) -> None:
    from PIL import Image, ImageDraw

    fill, stroke = _palette(name)
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    cx, cy = size // 2, size // 2
    if "_n" in name:
        pts = [(cx, cy - 18), (cx - 10, cy + 14), (cx + 10, cy + 14)]
    elif "_s" in name:
        pts = [(cx, cy + 18), (cx - 10, cy - 14), (cx + 10, cy - 14)]
    elif "_e" in name:
        pts = [(cx + 18, cy), (cx - 14, cy - 10), (cx - 14, cy + 10)]
    else:
        pts = [(cx - 18, cy), (cx + 14, cy - 10), (cx + 14, cy + 10)]
    dr.polygon(pts, outline=stroke, width=2, fill=fill)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def _draw_radial_glow(path: Path, stem: str, size: int = 128) -> None:
    from PIL import Image, ImageDraw

    fill, _ = _palette(stem)
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    cx = cy = size // 2
    for r in range(size // 2, 2, -2):
        a = int(220 * (1.0 - r / (size / 2)))
        c = (fill[0], fill[1], fill[2], min(240, a + 40))
        dr.ellipse([cx - r, cy - r, cx + r, cy + r], fill=c)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def _draw_blob(path: Path, stem: str, large: bool) -> None:
    from PIL import Image, ImageDraw

    fill, stroke = _palette(stem)
    size = 128 if large else 64
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    margin = 8
    dr.ellipse(
        [margin, margin, size - margin, size - margin],
        outline=stroke,
        width=2,
        fill=(fill[0], fill[1], fill[2], 180 if not large else 200),
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def _draw_door_open(path: Path) -> None:
    from PIL import Image, ImageDraw

    fill, stroke = _palette("door_open")
    w, h = 96, 96
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    dr.rectangle([20, 16, 76, 80], outline=stroke, width=2, fill=(fill[0], fill[1], fill[2], 120))
    dr.line([(48, 16), (62, 48), (48, 80)], fill=stroke, width=2)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def _draw_smoke(path: Path) -> None:
    from PIL import Image, ImageDraw

    fill, stroke = _palette("smoke_plume")
    w, h = 64, 128
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    for i in range(5):
        y = h - 20 - i * 18
        o = 50 + i * 35
        dr.ellipse([16 - i * 2, y, 48 + i * 4, y + 24], outline=stroke, fill=(fill[0], fill[1], fill[2], min(200, o)))
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def _draw_mp_slot(path: Path, slot: int) -> None:
    from PIL import Image, ImageDraw

    palette = [
        (200, 72, 72, 220),
        (72, 120, 200, 220),
        (96, 180, 96, 220),
        (220, 200, 72, 220),
        (160, 96, 200, 220),
        (220, 140, 72, 220),
        (72, 200, 200, 220),
        (160, 160, 170, 220),
    ]
    size = 64
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    c = palette[slot % len(palette)]
    dr.rounded_rectangle([4, 24, size - 4, 40], radius=6, fill=c, outline=(40, 36, 32, 255), width=2)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()

    _log("===== b12_detail_mp_fill_local =====")
    wrote = 0

    for fn in DETAIL_FILES:
        p = DETAIL / fn
        if p.is_file() and not args.force:
            _log(f"[skip] {p.relative_to(ROOT)}")
            continue
        stem = fn.replace(".png", "")
        if fn.startswith("footprint_"):
            _draw_footprint(p, stem)
        elif fn.startswith("glow_"):
            _draw_radial_glow(p, stem)
        elif fn == "blood_splatter_small.png":
            _draw_blob(p, stem, large=False)
        elif fn == "blood_splatter_large.png":
            _draw_blob(p, stem, large=True)
        elif fn.startswith("corpse_stain"):
            _draw_blob(p, stem, large=True)
        elif fn == "door_open.png":
            _draw_door_open(p)
        elif fn == "smoke_plume.png":
            _draw_smoke(p)
        else:
            _draw_blob(p, stem, large=False)
        _log(f"[ok]   {p.relative_to(ROOT)}")
        wrote += 1

    for i in range(8):
        p = MP / f"player_slot_{i}.png"
        if p.is_file() and not args.force:
            _log(f"[skip] {p.relative_to(ROOT)}")
            continue
        _draw_mp_slot(p, i)
        _log(f"[ok]   {p.relative_to(ROOT)}")
        wrote += 1

    _log(f"===== done wrote={wrote} (detail={len(DETAIL_FILES)} + mp=8) =====")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
