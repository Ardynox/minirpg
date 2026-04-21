"""
B4 本地兜底：`Data/FaceParts/*.json` 里每条非空 imagePath 在仓库落盘为 PNG。
API 不可用或与 Banana 并行时使用；风格为透明底 + 土色线框 + 按 id hash 的柔和填充，
与 `b2_item_world_fill_local` / `b7_ui_icons_fill_local` 同系的「能进游戏」占位。

    python Tools/banana/b4_faces_fill_local.py
    python Tools/banana/b4_faces_fill_local.py --force
    python Tools/banana/b4_faces_fill_local.py --size 256
"""
from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FACEPART_DIR = ROOT / "Data" / "FaceParts"
LOG = Path(__file__).resolve().parent / ".cache" / "b4_faces_fill.log"


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        fh.write(f"{datetime.now(timezone.utc).isoformat()}  {line}\n")
    print(line, flush=True)


def _iter_image_paths() -> list[str]:
    out: list[str] = []
    for path in sorted(FACEPART_DIR.glob("*.json")):
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        if not isinstance(data, list):
            continue
        for entry in data:
            if not isinstance(entry, dict):
                continue
            p = entry.get("imagePath")
            if isinstance(p, str) and p.startswith("res://"):
                out.append(p)
    return out


def res_to_fs(res: str) -> Path:
    rel = res[len("res://") :].lstrip("/")
    return ROOT / rel


def _draw(path: Path, stem: str, size: int) -> None:
    from PIL import Image, ImageDraw, ImageFont

    h = int(hashlib.sha256(stem.encode()).hexdigest(), 16)
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    stroke = (52, 42, 34, 255)
    margin = max(4, size // 12)
    dr.rounded_rectangle(
        [margin, margin, size - margin - 1, size - margin - 1],
        radius=max(6, size // 10),
        outline=stroke,
        width=max(2, size // 32),
        fill=(140 + (h % 50), 120 + ((h >> 8) % 45), 95 + ((h >> 16) % 50), 225),
    )
    label = stem[:10] if len(stem) <= 10 else stem[:7] + "."
    try:
        font = ImageFont.truetype("arial.ttf", size=max(10, size // 10))
    except OSError:
        font = ImageFont.load_default()
    bbox = dr.textbbox((0, 0), label, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    dr.text(
        ((size - tw) // 2, (size - th) // 2 - 2),
        label,
        fill=(28, 22, 18, 255),
        font=font,
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true")
    ap.add_argument(
        "--size",
        type=int,
        default=256,
        help="Square canvas edge (identity card + PortraitComposer use 256).",
    )
    args = ap.parse_args()
    size = max(32, min(512, args.size))

    paths = _iter_image_paths()
    _log(f"===== b4_faces_fill_local: {len(paths)} targets size={size} =====")
    ok = 0
    for res in paths:
        fs = res_to_fs(res)
        if fs.is_file() and not args.force:
            _log(f"[skip] {fs.relative_to(ROOT)}")
            continue
        stem = fs.stem
        _draw(fs, stem, size=size)
        _log(f"[ok]   {fs.relative_to(ROOT)}")
        ok += 1
    _log(f"===== done wrote={ok} total_defined={len(paths)} =====")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
