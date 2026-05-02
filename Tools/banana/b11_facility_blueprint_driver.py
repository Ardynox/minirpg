"""
B11 — facility 蓝图 4 行纵表（10 张）

与 `entity_render.json` 中每条 `facility_*` 对应，产出：
    Assets/Art/Generated/facilities_blueprint/facility_<id>_blueprint.png
尺寸 **256×1024**（4×256 行），与 `b11_facility_blueprint_fill_local.py` 及
`IsometricVoxelRenderer.ResolveFacilitySpriteRegion` 一致。

流水线：banana_gen（1:4 竖条）→ rembg → 精确 resize 到 256×1024。

运行：
    python Tools/banana/b11_facility_blueprint_driver.py --dry-run
    python Tools/banana/b11_facility_blueprint_driver.py --skip-existing
    python Tools/banana/b11_facility_blueprint_driver.py --only bed,smithy --max-gen-retries 5

依赖：`Tools/banana/.env` 中计费可用的 image API（见 README）；中国大陆需代理。
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = Path(__file__).resolve().parent
ENTITY_RENDER = ROOT / "Data" / "entity_render.json"
PREVIEW_DIR = ROOT / "Artifacts" / "preview" / "b11_blueprint"
OUT_DIR = ROOT / "Assets" / "Art" / "Generated" / "facilities_blueprint"
LOG = TOOLS_DIR / ".cache" / "b11_blueprint_driver.log"

# 与 fill_local / 渲染帧一致
SHEET_W, SHEET_H = 256, 1024

# 身份卡 / 玩法可读：facility id → 英文描述主体（喂给 style_base subject）
_FACILITY_SUBJECT: dict[str, str] = {
    "bed": "simple wooden bed with blanket, rustic",
    "dormitory_bed": "bunk or dormitory bed frame, compact",
    "stove": "metal stove with pipe, cooking",
    "butcher_table": "butcher block table, hooks",
    "smithy": "anvil with forge basin, blacksmith workstation",
    "loom": "weaving loom frame with threads",
    "herbal_bench": "herbalist workbench with mortar and jars",
    "market_stall": "wooden market stall counter with awning",
    "shelf": "storage shelf unit",
    "fire_brazier": "metal fire brazier bowl with grate",
}


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        fh.write(f"{datetime.now(timezone.utc).isoformat()}  {line}\n")
    try:
        print(line, flush=True)
    except UnicodeEncodeError:
        enc = getattr(sys.stdout, "encoding", None) or "utf-8"
        print(line.encode(enc, errors="replace").decode(enc, errors="replace"), flush=True)


def run_cmd(cmd: list[str], timeout: int = 900) -> tuple[int, str]:
    try:
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
            cwd=str(ROOT),
            encoding="utf-8",
            errors="replace",
        )
        out = (proc.stdout or "") + (proc.stderr or "")
        return proc.returncode, out
    except subprocess.TimeoutExpired:
        return 124, "[timeout]"
    except Exception as e:
        return 1, f"[exception] {type(e).__name__}: {e}"


def _facility_ids() -> list[str]:
    data = json.loads(ENTITY_RENDER.read_text(encoding="utf-8"))
    ids: list[str] = []
    for key in sorted(data.keys()):
        if key.startswith("facility_"):
            ids.append(key[len("facility_") :])
    return ids


def _blueprint_prompt(facility_id: str) -> str:
    subj = _FACILITY_SUBJECT.get(facility_id, facility_id.replace("_", " "))
    # 行序与 ResolveFacilityRotationRow：0=N ortho row top … 与四向等距剪影
    return (
        f"single vertical game sprite sheet strip, exactly four equal horizontal bands stacked top to bottom, "
        f"each band height one quarter of image, isometric 3/4 view construction blueprint of {subj}, "
        f"cyan and ice-blue technical line art on transparent background, ghost wireframe schematic, "
        f"thin crisp lines, no solid fill, no text, no watermark, "
        f"top row north-facing, then east, south, west rotation variants, muted low saturation"
    )


def _resize_sheet_to_target(src: Path, dst: Path) -> None:
    from PIL import Image

    im = Image.open(src).convert("RGBA")
    im = im.resize((SHEET_W, SHEET_H), Image.Resampling.LANCZOS)
    dst.parent.mkdir(parents=True, exist_ok=True)
    im.save(dst, format="PNG")


def process_one(
    facility_id: str,
    *,
    skip_existing: bool,
    dry_run: bool,
    max_gen_retries: int,
    size: str,
) -> str:
    out_final = OUT_DIR / f"facility_{facility_id}_blueprint.png"
    if skip_existing and out_final.exists():
        return f"[skip] {facility_id} (existing)"

    raw = PREVIEW_DIR / f"facility_{facility_id}_raw.png"
    nobg = PREVIEW_DIR / f"facility_{facility_id}_nobg.png"

    prompt = _blueprint_prompt(facility_id)

    if dry_run:
        return f"[dry-run] {facility_id} aspect=1:4 size={size}"

    for attempt in range(1, max_gen_retries + 1):
        cmd = [
            sys.executable,
            str(TOOLS_DIR / "banana_gen.py"),
            "--protocol",
            "openai",
            "--prompt",
            prompt,
            "--aspect",
            "1:4",
            "--size",
            size,
            "--count",
            "1",
            "--out",
            str(raw.relative_to(ROOT)),
        ]
        rc, out = run_cmd(cmd, timeout=900)
        if rc == 0:
            break
        _log(f"[retry {attempt}/{max_gen_retries}] {facility_id} banana_gen rc={rc}")
        if attempt < max_gen_retries:
            time.sleep(15.0)
    else:
        return f"[FAIL-gen] {facility_id} rc={rc}  {out[-380:]}"

    candidates = list(PREVIEW_DIR.glob(f"facility_{facility_id}_raw.*"))
    raw_real = None
    for c in candidates:
        if c.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp"}:
            raw_real = c
            break
    if raw_real is None:
        return f"[FAIL-gen-missing] {facility_id}"

    cmd = [
        sys.executable,
        str(TOOLS_DIR / "rembg_cutout.py"),
        "--in",
        str(raw_real.relative_to(ROOT)),
        "--out",
        str(nobg.relative_to(ROOT)),
    ]
    rc, out = run_cmd(cmd, timeout=180)
    if rc != 0:
        return f"[FAIL-rembg] {facility_id} rc={rc}  {out[-200:]}"

    try:
        _resize_sheet_to_target(nobg, out_final)
    except Exception as e:
        return f"[FAIL-resize] {facility_id} {e!s}"

    return f"[ok]   {facility_id}"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument(
        "--only",
        default="",
        help="comma-separated facility ids (e.g. bed,smithy); default=all from entity_render",
    )
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--max-gen-retries", type=int, default=3)
    ap.add_argument(
        "--size",
        choices=("1K", "2K"),
        default="1K",
        help="image tier (2K costs ~2× 1K; blueprint detail may benefit from 2K before resize)",
    )
    args = ap.parse_args()
    size: str = args.size

    all_ids = _facility_ids()
    if args.only.strip():
        want = {s.strip() for s in args.only.split(",") if s.strip()}
        ids = [i for i in all_ids if i in want]
        missing = want - set(all_ids)
        if missing:
            _log(f"[warn] unknown --only ids ignored: {sorted(missing)}")
    else:
        ids = all_ids

    _log(f"===== B11 blueprint driver: {len(ids)} jobs (of {len(all_ids)} facility_*) =====")
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    results: list[str] = []
    for i, fid in enumerate(ids, 1):
        t1 = time.time()
        msg = process_one(
            fid,
            skip_existing=args.skip_existing,
            dry_run=args.dry_run,
            max_gen_retries=args.max_gen_retries,
            size=size,
        )
        _log(f"[{i:>2}/{len(ids)}] {msg}  [+{time.time() - t1:.0f}s]")
        results.append(msg)

    total = time.time() - t0
    ok = sum(1 for r in results if r.startswith("[ok]"))
    fail = sum(1 for r in results if r.startswith("[FAIL"))
    skip = sum(1 for r in results if r.startswith("[skip]"))
    dr = sum(1 for r in results if r.startswith("[dry-run"))
    _log(f"===== done {total:.0f}s ok={ok} fail={fail} skip={skip} dry={dr} =====")
    if fail:
        for r in results:
            if r.startswith("[FAIL"):
                _log(f"  {r}")
    return 0 if fail == 0 else 2


if __name__ == "__main__":
    sys.exit(main())
