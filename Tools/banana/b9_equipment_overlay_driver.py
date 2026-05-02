"""
B9 — 装备 overlay **88** 张（与 `EquipmentOverlayPaths` / `b9_equipment_overlay_fill_local.py` 同名）

流水线：`banana_gen` 1:1 → `rembg` → `crop_square_resize` **256**（与 `MapSpriteRuntimeFactory` 一致）。

运行：
    python Tools/banana/b9_equipment_overlay_driver.py --dry-run
    python Tools/banana/b9_equipment_overlay_driver.py --skip-existing
    python Tools/banana/b9_equipment_overlay_driver.py --only-weapon sword --limit 2
"""
from __future__ import annotations

import argparse
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = Path(__file__).resolve().parent
OUT_DIR = ROOT / "Assets" / "Art" / "Generated" / "equipment_overlays"
PREVIEW_DIR = ROOT / "Artifacts" / "preview" / "b9_overlay"
LOG = TOOLS_DIR / ".cache" / "b9_overlay_driver.log"

DIRS = ["s", "sw", "w", "nw", "n", "ne", "e", "se"]

STYLE = (
    "single small RPG equipment overlay sprite tile for isometric map stacking on humanoid base, "
    "transparent background intent, semi-realistic hand-painted, muted earthy palette, thin brown outline, "
    "no text, no watermark, neutral grey studio backdrop for keying"
)


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        fh.write(f"{datetime.now(timezone.utc).isoformat()}  {line}\n")
    try:
        print(line, flush=True)
    except UnicodeEncodeError:
        enc = getattr(sys.stdout, "encoding", None) or "utf-8"
        print(line.encode(enc, errors="replace").decode(enc, errors="replace"), flush=True)


def run_cmd(cmd: list[str], timeout: int = 720) -> tuple[int, str]:
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
        return proc.returncode, (proc.stdout or "") + (proc.stderr or "")
    except subprocess.TimeoutExpired:
        return 124, "[timeout]"
    except Exception as e:
        return 1, f"{type(e).__name__}: {e}"


def _prompt_weapon(slug: str, d: str) -> str:
    return (
        f"RPG equipment overlay: {slug} weapon (blade/head/handle silhouette) for south-isometric character sheet, "
        f"compass direction hint {d}, held at side/back for layering, {STYLE}"
    )


def _prompt_cloak(slug: str, d: str) -> str:
    return (
        f"RPG equipment overlay: {slug} cloak or mantle drape behind shoulders, isometric, direction {d}, "
        f"soft fabric folds, {STYLE}"
    )


def _prompt_helmet(slug: str, d: str) -> str:
    return (
        f"RPG equipment overlay: {slug} head armor or crown above hair line, isometric, direction {d}, {STYLE}"
    )


def build_jobs(
    only_weapon: set[str] | None,
    only_cloak: set[str] | None,
    only_helmet: set[str] | None,
) -> list[tuple[str, Path, str]]:
    jobs: list[tuple[str, Path, str]] = []
    for slug in ("sword", "axe", "bow", "staff", "mace"):
        if only_weapon is not None and slug not in only_weapon:
            continue
        for d in DIRS:
            fname = f"equip_weapon_{slug}_{d}.png"
            jobs.append((f"w-{slug}-{d}", OUT_DIR / fname, _prompt_weapon(slug, d)))
    for slug in ("short", "long", "hooded"):
        if only_cloak is not None and slug not in only_cloak:
            continue
        for d in DIRS:
            fname = f"equip_cloak_{slug}_{d}.png"
            jobs.append((f"c-{slug}-{d}", OUT_DIR / fname, _prompt_cloak(slug, d)))
    for slug in ("cap", "full", "crown"):
        if only_helmet is not None and slug not in only_helmet:
            continue
        for d in DIRS:
            fname = f"equip_helmet_{slug}_{d}.png"
            jobs.append((f"h-{slug}-{d}", OUT_DIR / fname, _prompt_helmet(slug, d)))
    return jobs


def process_one(
    job_key: str,
    prompt: str,
    out_final: Path,
    *,
    skip_existing: bool,
    dry_run: bool,
    max_gen_retries: int,
    size: str,
) -> str:
    if skip_existing and out_final.exists():
        return f"[skip] {out_final.name}"

    raw = PREVIEW_DIR / f"{job_key}_raw.png"
    nobg = PREVIEW_DIR / f"{job_key}_nobg.png"

    if dry_run:
        return f"[dry-run] {job_key}"

    for attempt in range(1, max_gen_retries + 1):
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
            size,
            "--count",
            "1",
            "--out",
            str(raw.relative_to(ROOT)),
        ]
        rc, out = run_cmd(cmd, timeout=900)
        if rc == 0:
            break
        _log(f"[retry {attempt}/{max_gen_retries}] {job_key} rc={rc}")
        if attempt < max_gen_retries:
            time.sleep(14.0)
    else:
        return f"[FAIL-gen] {job_key} {out[-300:]}"

    candidates = list(PREVIEW_DIR.glob(f"{job_key}_raw.*"))
    raw_real = next((c for c in candidates if c.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp"}), None)
    if raw_real is None:
        return f"[FAIL-gen-missing] {job_key}"

    rc, out = run_cmd(
        [
            sys.executable,
            str(TOOLS_DIR / "rembg_cutout.py"),
            "--in",
            str(raw_real.relative_to(ROOT)),
            "--out",
            str(nobg.relative_to(ROOT)),
        ],
        timeout=120,
    )
    if rc != 0:
        return f"[FAIL-rembg] {job_key}"

    rc, _ = run_cmd(
        [
            sys.executable,
            str(TOOLS_DIR / "crop_square_resize.py"),
            "--in",
            str(nobg.relative_to(ROOT)),
            "--out",
            str(out_final.relative_to(ROOT)),
            "--size",
            "256",
            "--margin",
            "0.08",
        ],
        timeout=90,
    )
    if rc != 0:
        return f"[FAIL-crop] {job_key}"
    return f"[ok]   {job_key}"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--max-gen-retries", type=int, default=5)
    ap.add_argument("--size", choices=("1K", "2K"), default="1K")
    ap.add_argument(
        "--only-weapon",
        default="",
        help="comma: sword,axe,bow,staff,mace — omit for all weapons",
    )
    ap.add_argument("--only-cloak", default="", help="comma: short,long,hooded")
    ap.add_argument("--only-helmet", default="", help="comma: cap,full,crown")
    args = ap.parse_args()

    def parse_set(s: str) -> set[str] | None:
        if not s.strip():
            return None
        return {x.strip() for x in s.split(",") if x.strip()}

    jobs = build_jobs(parse_set(args.only_weapon), parse_set(args.only_cloak), parse_set(args.only_helmet))
    if args.limit > 0:
        jobs = jobs[: args.limit]

    _log(f"===== B9 overlay driver: {len(jobs)} jobs =====")
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    results: list[str] = []
    for i, (jk, outp, pr) in enumerate(jobs, 1):
        t1 = time.time()
        msg = process_one(
            jk,
            pr,
            outp,
            skip_existing=args.skip_existing,
            dry_run=args.dry_run,
            max_gen_retries=args.max_gen_retries,
            size=args.size,
        )
        _log(f"[{i:>3}/{len(jobs)}] {msg}  [+{time.time() - t1:.0f}s]")
        results.append(msg)

    ok = sum(1 for r in results if r.startswith("[ok]"))
    fail = sum(1 for r in results if r.startswith("[FAIL"))
    _log(f"===== done {time.time() - t0:.0f}s ok={ok} fail={fail} =====")
    return 0 if fail == 0 else 2


if __name__ == "__main__":
    sys.exit(main())
