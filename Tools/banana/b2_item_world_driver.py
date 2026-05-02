"""
B2 — 地面物品九类分类占位图（各 1 张）

对齐 `Artifacts/素材需求清单_2026-04-20.md` §3.2：
- `category_{ammo,armor,clothing,consumable,food,material,misc,tool,weapon}.png`
- 落盘 `Assets/Art/Placeholders/item_world/`
- 与 `Data/item_world_render.json` 的 `categories` / `default`（`kind: texture`）一致

运行：
    python Tools/banana/b2_item_world_driver.py --dry-run
    python Tools/banana/b2_item_world_driver.py --skip-existing
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
PREVIEW_DIR = ROOT / "Artifacts" / "preview" / "b2_item_world"
OUT_DIR = ROOT / "Assets" / "Art" / "Placeholders" / "item_world"
LOG = TOOLS_DIR / ".cache" / "b2_item_world_driver.log"

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

STYLE = (
    "single tiny isometric 3/4 top-down loot pile icon for game HUD, one readable silhouette, "
    "hand-painted semi-realistic, muted earthy low-saturation, soft overcast, thin brown outline, "
    "no text, no watermark, solid neutral flat gray background for easy keying"
)


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        ts = datetime.now(timezone.utc).isoformat()
        fh.write(f"{ts}  {line}\n")
    print(line, flush=True)


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
        out = (proc.stdout or "") + (proc.stderr or "")
        return proc.returncode, out
    except subprocess.TimeoutExpired:
        return 124, "[timeout]"
    except Exception as e:
        return 1, f"[exception] {type(e).__name__}: {e}"


def _prompt(cat: str) -> str:
    hint = {
        "ammo": "small pile of bullets and magazines",
        "armor": "folded chest armor piece",
        "clothing": "rolled cloth and shirt stack",
        "consumable": "small heap of vials and bandages",
        "food": "bread cheese and apple cluster",
        "material": "wood planks ore chunks and rope coil",
        "misc": "generic junk pile keys rope scraps",
        "tool": "hammer wrench and saw crossed",
        "weapon": "sword and axe crossed",
    }.get(cat, "loot pile")
    return f"inventory category icon for {cat}: {hint}, {STYLE}"


def process_one(fid: str, prompt: str, *, skip_existing: bool, dry_run: bool, max_gen_retries: int) -> str:
    out_final = OUT_DIR / f"{fid}.png"
    if skip_existing and out_final.exists():
        return f"[skip] {fid} (existing)"

    raw = PREVIEW_DIR / f"{fid}.png"
    nobg = PREVIEW_DIR / f"{fid}_nobg.png"

    if dry_run:
        return f"[dry-run] {fid}"

    for attempt in range(1, max_gen_retries + 1):
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
        rc, out = run_cmd(cmd, timeout=900)
        if rc == 0:
            break
        _log(f"[retry {attempt}/{max_gen_retries}] {fid} banana_gen rc={rc}")
        if attempt < max_gen_retries:
            time.sleep(12.0)
    else:
        return f"[FAIL-gen] {fid} rc={rc}  {out[-280:]}"

    candidates = list(PREVIEW_DIR.glob(f"{fid}.*"))
    raw_real = None
    for c in candidates:
        if c.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp"} and c.name != nobg.name:
            raw_real = c
            break
    if raw_real is None:
        return f"[FAIL-gen-missing] {fid}"

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
        return f"[FAIL-rembg] {fid} rc={rc}  {out[-200:]}"

    cmd = [
        sys.executable,
        str(TOOLS_DIR / "crop_square_resize.py"),
        "--in",
        str(nobg.relative_to(ROOT)),
        "--out",
        str(out_final.relative_to(ROOT)),
        "--size",
        "64",
        "--margin",
        "0.08",
    ]
    rc, out = run_cmd(cmd, timeout=120)
    if rc != 0:
        return f"[FAIL-crop] {fid} rc={rc}  {out[-200:]}"

    return f"[ok]   {fid}"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--max-gen-retries", type=int, default=3)
    args = ap.parse_args()

    jobs = [f"category_{c}" for c in CATEGORIES]
    if args.limit > 0:
        jobs = jobs[: args.limit]

    _log(f"===== B2 item_world: {len(jobs)} jobs =====")
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    results: list[str] = []
    for i, fid in enumerate(jobs, 1):
        cat = fid.replace("category_", "")
        t1 = time.time()
        msg = process_one(
            fid,
            _prompt(cat),
            skip_existing=args.skip_existing,
            dry_run=args.dry_run,
            max_gen_retries=args.max_gen_retries,
        )
        _log(f"[{i:>2}/{len(jobs)}] {msg}  [+{time.time() - t1:.0f}s]")
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
