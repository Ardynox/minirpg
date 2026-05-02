"""
B8 — 作物三阶段（7×3=21）+ 材料世界堆（24）= 45

流水线：banana_gen -> rembg -> crop_square_resize

对齐 Artifacts/素材需求清单_2026-04-20.md §3.6–3.8、§5 B8：
- crop_<id>_stage{0,1,2}.png → Assets/Art/Placeholders/crops/  等距俯视小作物
- mat_<id>_stack.png → Assets/Art/Placeholders/material_world/  地上原料小堆

运行：
    python Tools/banana/b8_ui_icons_driver.py --dry-run
    python Tools/banana/b8_ui_icons_driver.py --skip-existing
    python Tools/banana/b8_ui_icons_driver.py --only crop --limit 3
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
PREVIEW_DIR = ROOT / "Artifacts" / "preview" / "b8_icons"
OUT_CROP = ROOT / "Assets" / "Art" / "Placeholders" / "crops"
OUT_MAT = ROOT / "Assets" / "Art" / "Placeholders" / "material_world"
LOG = TOOLS_DIR / ".cache" / "b8_driver.log"

STYLE_ISO = (
    "single isometric 3/4 top-down game prop sprite, hand-painted semi-realistic, muted earthy low-saturation, "
    "soft overcast lighting, no hard cast shadow, thin brown outline, no text, no watermark, "
    "solid neutral flat gray background for easy keying"
)


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        ts = datetime.now(timezone.utc).isoformat()
        fh.write(f"{ts}  {line}\n")
    print(line, flush=True)


def run_cmd(cmd: list[str], timeout: int = 600) -> tuple[int, str]:
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


def _load_ids(path: Path) -> list[str]:
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    return [str(x["id"]) for x in data if isinstance(x, dict) and x.get("id")]


def _crop_stage_prompt(crop_id: str, stage: int) -> str:
    stage_desc = (
        "tiny sprout seedling only 2 leaves, sparse soil",
        "mid-growth bush or stalk, clearly forming crop silhouette",
        "fully mature harvest-ready plant with visible fruit/grain/ears OR for final stage slightly wilted dry stalk if root crop",
    )[min(stage, 2)]
    return (
        f"small field crop plot icon: {crop_id.replace('_', ' ')} plant, {stage_desc}, "
        f"{STYLE_ISO}"
    )


def _mat_stack_prompt(mat_id: str) -> str:
    return (
        f"small ground pile of raw material: {mat_id.replace('_', ' ')}, chunky heap seen from 3/4 above, "
        f"readable material texture, {STYLE_ISO}"
    )


def build_jobs(only: set[str]) -> list[tuple[str, str, str, Path, int, bool]]:
    """(category, file_id, prompt, out_dir, crop_size, use_style)"""
    jobs: list[tuple[str, str, str, Path, int, bool]] = []

    if "crop" in only:
        for cid in _load_ids(ROOT / "Data" / "crops.json"):
            for st in (0, 1, 2):
                fid = f"crop_{cid}_stage{st}"
                jobs.append(("crop", fid, _crop_stage_prompt(cid, st), OUT_CROP, 96, True))

    if "material" in only:
        for mid in _load_ids(ROOT / "Data" / "materials.json"):
            fid = f"mat_{mid}_stack"
            jobs.append(("material", fid, _mat_stack_prompt(mid), OUT_MAT, 96, True))

    return jobs


def process_one(
    icon_id: str,
    prompt: str,
    out_dir: Path,
    crop: int,
    use_style: bool,
    *,
    skip_existing: bool,
    dry_run: bool,
    max_gen_retries: int,
) -> str:
    out_final = out_dir / f"{icon_id}.png"
    if skip_existing and out_final.exists():
        return f"[skip] {icon_id} (existing)"

    raw = PREVIEW_DIR / f"{icon_id}.png"
    nobg = PREVIEW_DIR / f"{icon_id}_nobg.png"

    if dry_run:
        return f"[dry-run] {icon_id} crop={crop} style={use_style}"

    for attempt in range(1, max_gen_retries + 1):
        if use_style:
            cmd = [
                sys.executable,
                str(TOOLS_DIR / "banana_gen.py"),
                "--protocol",
                "openai",
                "--style",
                "style_base",
                "--negatives",
                "standard",
                "--subject",
                prompt,
                "--aspect",
                "1:1",
                "--size",
                "1K",
                "--count",
                "1",
                "--out",
                str(raw.relative_to(ROOT)),
            ]
        else:
            cmd = [
                sys.executable,
                str(TOOLS_DIR / "banana_gen.py"),
                "--protocol",
                "openai",
                "--prompt",
                prompt,
                "--aspect",
                "1:1",
                "--size",
                "1K",
                "--count",
                "1",
                "--out",
                str(raw.relative_to(ROOT)),
            ]
        rc, out = run_cmd(cmd, timeout=720)
        if rc == 0:
            break
        _log(f"[retry {attempt}/{max_gen_retries}] {icon_id} banana_gen rc={rc}")
        if attempt < max_gen_retries:
            time.sleep(12.0)
    else:
        return f"[FAIL-gen] {icon_id} rc={rc}  {out[-280:]}"

    candidates = list(PREVIEW_DIR.glob(f"{icon_id}.*"))
    raw_real = None
    for c in candidates:
        if c.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp"} and c.name != nobg.name:
            raw_real = c
            break
    if raw_real is None:
        return f"[FAIL-gen-missing] {icon_id}"

    cmd = [
        sys.executable,
        str(TOOLS_DIR / "rembg_cutout.py"),
        "--in",
        str(raw_real.relative_to(ROOT)),
        "--out",
        str(nobg.relative_to(ROOT)),
    ]
    rc, out = run_cmd(cmd, timeout=120)
    if rc != 0:
        return f"[FAIL-rembg] {icon_id} rc={rc}  {out[-200:]}"

    cmd = [
        sys.executable,
        str(TOOLS_DIR / "crop_square_resize.py"),
        "--in",
        str(nobg.relative_to(ROOT)),
        "--out",
        str(out_final.relative_to(ROOT)),
        "--size",
        str(crop),
        "--margin",
        "0.08",
    ]
    rc, out = run_cmd(cmd, timeout=60)
    if rc != 0:
        return f"[FAIL-crop] {icon_id} rc={rc}  {out[-200:]}"

    return f"[ok]   {icon_id}"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="crop,material", help="comma: crop,material")
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--limit", type=int, default=0, help="max jobs (0=all)")
    ap.add_argument("--max-gen-retries", type=int, default=3)
    args = ap.parse_args()

    selected = {s.strip() for s in args.only.split(",") if s.strip()}
    jobs = build_jobs(selected)
    if args.limit > 0:
        jobs = jobs[: args.limit]

    _log(f"===== B8 driver: {len(jobs)} jobs, only={sorted(selected)} =====")
    OUT_CROP.mkdir(parents=True, exist_ok=True)
    OUT_MAT.mkdir(parents=True, exist_ok=True)
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    results: list[str] = []
    for i, (cat, fid, prompt, out_dir, crop, use_style) in enumerate(jobs, 1):
        t1 = time.time()
        msg = process_one(
            fid,
            prompt,
            out_dir,
            crop,
            use_style,
            skip_existing=args.skip_existing,
            dry_run=args.dry_run,
            max_gen_retries=args.max_gen_retries,
        )
        _log(f"[{i:>3}/{len(jobs)}] ({cat:<8}) {msg}  [+{time.time() - t1:.0f}s]")
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
