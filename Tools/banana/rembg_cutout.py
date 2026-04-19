"""
rembg 抠图 CLI — 输入任意 PNG/JPG/WebP，输出带 alpha 的 PNG（通用后处理）。

依赖：pip install -r Tools/banana/requirements.txt

示例：

  python Tools/banana/rembg_cutout.py ^
    --in Artifacts/foo.jpg --out Artifacts/foo_nobg.png

  python Tools/banana/rembg_cutout.py ^
    --in Artifacts/foo.png --out Artifacts/foo_nobg.png --model isnet-general-use
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = Path(__file__).resolve().parent

# 与 banana_gen.py 一致：输出必须在 Artifacts/ 或 Assets/ 下
MAX_OUT_REL_DEPTH = 8

# rembg new_session() 接受的模型名（以当前 rembg 包为准，未知模型会在运行时失败并提示）
DEFAULT_MODEL = "u2net"


def _force_utf8_stdio() -> None:
    for stream_name in ("stdout", "stderr"):
        stream = getattr(sys, stream_name, None)
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is None:
            continue
        try:
            reconfigure(encoding="utf-8")
        except Exception:
            pass


def validate_out_path(out: Path) -> None:
    try:
        rel = out.resolve().relative_to(ROOT)
    except ValueError:
        sys.exit(f"[rembg] --out 必须在仓库内: {out}")
    top = rel.parts[0] if rel.parts else ""
    if top not in {"Artifacts", "Assets"}:
        sys.exit(
            f"[rembg] --out 必须在 Artifacts/ 或 Assets/ 下，当前: {rel}\n"
            "        避免把抠图结果写到源码目录"
        )
    if len(rel.parts) > MAX_OUT_REL_DEPTH:
        sys.exit(f"[rembg] --out 路径太深: {rel}")


def validate_in_path(path: Path) -> None:
    if not path.is_file():
        sys.exit(f"[rembg] --in 不是文件: {path}")
    try:
        path.resolve().relative_to(ROOT)
    except ValueError:
        sys.exit(f"[rembg] --in 必须在仓库根目录内: {path}")


def main() -> int:
    _force_utf8_stdio()

    parser = argparse.ArgumentParser(
        description="rembg 抠图：输出 PNG（透明背景）",
    )
    parser.add_argument("--in", dest="inp", required=True, help="输入图片路径（仓库内）")
    parser.add_argument("--out", required=True, help="输出 PNG 路径（Artifacts/ 或 Assets/）")
    parser.add_argument(
        "--model",
        default=DEFAULT_MODEL,
        help=f"rembg 模型名，默认 {DEFAULT_MODEL}；物体/道具常用 u2net 或 isnet-general-use",
    )
    parser.add_argument(
        "--alpha-matting",
        action="store_true",
        help="启用 alpha matting（更干净边缘，更慢，需额外依赖）",
    )
    args = parser.parse_args()

    in_path = Path(args.inp)
    out_path = Path(args.out)
    validate_in_path(in_path)
    validate_out_path(out_path)
    if out_path.suffix.lower() != ".png":
        sys.exit("[rembg] --out 请使用 .png 扩展名（输出为 RGBA）")

    try:
        from rembg import remove
        from rembg.session_factory import new_session
    except ImportError:
        sys.exit(
            "[rembg] 未安装 rembg，请执行：pip install -r Tools/banana/requirements.txt"
        )

    raw = in_path.read_bytes()
    try:
        session = new_session(args.model)
    except Exception as exc:
        sys.exit(f"[rembg] 模型 '{args.model}' 不可用: {type(exc).__name__}: {exc}")

    try:
        if args.alpha_matting:
            out_bytes = remove(raw, session=session, alpha_matting=True)
        else:
            out_bytes = remove(raw, session=session)
    except TypeError:
        # 旧版 rembg：无 alpha_matting
        out_bytes = remove(raw, session=session)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(out_bytes)
    print(f"[rembg] ok: {in_path} -> {out_path} (model={args.model})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
