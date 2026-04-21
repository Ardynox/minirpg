"""
B11 本地兜底：与 `entity_render.json` 中带 `facility_*` 键的设施一一对应，在
`Assets/Art/Generated/facilities_blueprint/` 写入蓝图线稿占位 PNG（256×1024，4 行同正式 4dir sheet）。

若文件已存在且未 --force 则跳过。运行时见 `IsometricVoxelRenderer`：设施处于 Blueprint 阶段且
`ResourceLoader.Exists` 该路径时优先于常规 facility_*_4dir 贴图，且不叠蓝色 stage tint。

    python Tools/banana/b11_facility_blueprint_fill_local.py
    python Tools/banana/b11_facility_blueprint_fill_local.py --force
"""
from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ENTITY_RENDER = ROOT / "Data" / "entity_render.json"
OUT_DIR = ROOT / "Assets" / "Art" / "Generated" / "facilities_blueprint"
LOG = Path(__file__).resolve().parent / ".cache" / "b11_facility_blueprint_fill.log"

FRAME_W, FRAME_H = 256, 256
ROWS = 4
W, H = FRAME_W, FRAME_H * ROWS


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        fh.write(f"{datetime.now(timezone.utc).isoformat()}  {line}\n")
    print(line, flush=True)


def _facility_ids() -> list[str]:
    data = json.loads(ENTITY_RENDER.read_text(encoding="utf-8"))
    ids: list[str] = []
    for key in sorted(data.keys()):
        if key.startswith("facility_"):
            ids.append(key[len("facility_") :])
    return ids


def _draw(path: Path, facility_id: str) -> None:
    from PIL import Image, ImageDraw, ImageFont

    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    # 与 Renderer 中 FacilityStage.Blueprint 调色板呼应的浅色线稿
    line = (120, 200, 255, 255)
    dim = (40, 55, 70, 220)

    for row in range(ROWS):
        y0 = row * FRAME_H
        pad = 12
        dr.rectangle(
            [pad, y0 + pad, W - pad - 1, y0 + FRAME_H - pad - 1],
            outline=line,
            width=3,
        )
        dr.line([pad, y0 + FRAME_H // 2, W - pad, y0 + FRAME_H // 2], fill=dim, width=2)
        dr.line([W // 2, y0 + pad, W // 2, y0 + FRAME_H - pad], fill=dim, width=2)

    label = facility_id[:18]
    try:
        font = ImageFont.truetype("arial.ttf", size=22)
    except OSError:
        font = ImageFont.load_default()
    bbox = dr.textbbox((0, 0), label, font=font)
    tw = bbox[2] - bbox[0]
    dr.text(((W - tw) // 2, 8), label, fill=line, font=font)

    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()

    ids = _facility_ids()
    _log(f"===== b11_facility_blueprint_fill_local: {len(ids)} facility keys =====")
    n = 0
    for fid in ids:
        p = OUT_DIR / f"facility_{fid}_blueprint.png"
        if p.is_file() and not args.force:
            _log(f"[skip] {p.relative_to(ROOT)}")
            continue
        _draw(p, fid)
        _log(f"[ok]   {p.relative_to(ROOT)}")
        n += 1
    _log(f"===== done wrote={n} / {len(ids)} =====")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
