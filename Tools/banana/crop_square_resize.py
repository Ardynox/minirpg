"""
Banana 后处理：按 alpha 非零 bbox 裁方 + 可选边距 + resize 到目标尺寸。

典型用法：
  # 单图：抠图后的 PNG → 256×256 占位素材
  python Tools/banana/crop_square_resize.py \
      --in Artifacts/preview/b1_effects/fx_arrow_projectile_rembg.png \
      --out Assets/Art/Placeholders/effects/fx_arrow_projectile.png \
      --size 256 --margin 0.06

  # 批处理：同一目录下所有 *_rembg.png → 对应目标目录
  python Tools/banana/crop_square_resize.py \
      --in-dir Artifacts/preview/b1_effects \
      --out-dir Assets/Art/Placeholders/effects \
      --size 256 --margin 0.06 --suffix _rembg

规则（与 banana_gen.py / rembg_cutout.py 一致）：
- --out / --out-dir 必须在 Artifacts/ 或 Assets/ 下。
- 输入必须已有真 alpha（建议先跑 rembg_cutout.py）。无 alpha 时直接拷贝原图但会警告。
- 不动 .png.import：Godot 会按 png 头自适应。
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
MAX_OUT_REL_DEPTH = 8
TOP_LEVEL_WHITELIST = {"Artifacts", "Assets"}


def _validate_out_path(out: Path) -> None:
    try:
        rel = out.resolve().relative_to(ROOT)
    except ValueError:
        sys.exit(f"[crop] --out 必须在仓库内: {out}")
    top = rel.parts[0] if rel.parts else ""
    if top not in TOP_LEVEL_WHITELIST:
        sys.exit(f"[crop] --out 必须在 Artifacts/ 或 Assets/ 下: {rel}")
    if len(rel.parts) > MAX_OUT_REL_DEPTH:
        sys.exit(f"[crop] --out 路径太深: {rel}")


def _crop_to_square(im: Image.Image, margin: float) -> Image.Image:
    if im.mode != "RGBA":
        im = im.convert("RGBA")
    bbox = im.getbbox()
    if bbox is None:
        print("[crop] 警告：整张都是透明，跳过裁剪", file=sys.stderr)
        return im
    x0, y0, x1, y1 = bbox
    w, h = x1 - x0, y1 - y0
    side = max(w, h)
    pad = int(side * margin)
    side += pad * 2
    cx = (x0 + x1) / 2
    cy = (y0 + y1) / 2
    half = side // 2
    nx0 = int(round(cx - half))
    ny0 = int(round(cy - half))
    nx1 = nx0 + side
    ny1 = ny0 + side

    W, H = im.size
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    paste_x = max(0, -nx0)
    paste_y = max(0, -ny0)
    src_x0 = max(0, nx0)
    src_y0 = max(0, ny0)
    src_x1 = min(W, nx1)
    src_y1 = min(H, ny1)
    if src_x1 > src_x0 and src_y1 > src_y0:
        canvas.paste(im.crop((src_x0, src_y0, src_x1, src_y1)), (paste_x, paste_y))
    return canvas


def _process_one(src: Path, dst: Path, size: int, margin: float) -> None:
    im = Image.open(src)
    if im.mode != "RGBA":
        print(f"[crop] 警告：{src.name} 不是 RGBA（mode={im.mode}），会被强转，但建议先跑 rembg")
        im = im.convert("RGBA")
    squared = _crop_to_square(im, margin)
    resized = squared.resize((size, size), Image.LANCZOS)
    dst.parent.mkdir(parents=True, exist_ok=True)
    resized.save(dst, format="PNG", optimize=True)
    print(f"[crop] ok: {src} -> {dst} ({size}x{size})")


def main() -> int:
    ap = argparse.ArgumentParser(description="按 alpha 非零 bbox 裁方 + resize")
    ap.add_argument("--in", dest="inp", help="单图输入 PNG")
    ap.add_argument("--out", help="单图输出 PNG")
    ap.add_argument("--in-dir", help="批处理输入目录")
    ap.add_argument("--out-dir", help="批处理输出目录")
    ap.add_argument("--size", type=int, default=256)
    ap.add_argument(
        "--margin",
        type=float,
        default=0.06,
        help="主体外留白比例（按最大边），默认 0.06",
    )
    ap.add_argument(
        "--suffix",
        default="",
        help="批处理时源文件名后缀过滤（例如 _rembg，只处理 *_rembg.png）；输出会自动去掉该后缀",
    )
    args = ap.parse_args()

    if args.inp and args.out:
        src = Path(args.inp)
        dst = Path(args.out)
        if not src.is_file():
            sys.exit(f"[crop] --in 不是文件: {src}")
        _validate_out_path(dst)
        _process_one(src, dst, args.size, args.margin)
        return 0

    if args.in_dir and args.out_dir:
        src_dir = Path(args.in_dir)
        dst_dir = Path(args.out_dir)
        if not src_dir.is_dir():
            sys.exit(f"[crop] --in-dir 不是目录: {src_dir}")
        _validate_out_path(dst_dir)
        dst_dir.mkdir(parents=True, exist_ok=True)
        pattern = f"*{args.suffix}.png" if args.suffix else "*.png"
        files = sorted(src_dir.glob(pattern))
        if not files:
            sys.exit(f"[crop] --in-dir 下没有匹配 {pattern} 的文件")
        for src in files:
            stem = src.stem
            if args.suffix and stem.endswith(args.suffix):
                stem = stem[: -len(args.suffix)]
            dst = dst_dir / f"{stem}.png"
            _process_one(src, dst, args.size, args.margin)
        return 0

    sys.exit("[crop] 需要 --in/--out 或 --in-dir/--out-dir")


if __name__ == "__main__":
    sys.exit(main())
