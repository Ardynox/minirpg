"""
B12 — `Placeholders/detail/` 20 张 + `mp/player_slot_*.png` 8 张

占位来源与 `b12_detail_mp_fill_local.py` 一致；本脚本用 Banana 出图后 **同路径覆盖**（先 rembg 再按目标像素缩放）。

尺寸与兜底脚本一致（脚印/光晕/血迹等各不相同）；绝大多数用 **1:1** 生成，`smoke_plume` 用 **1:2**。

运行：
    python Tools/banana/b12_detail_mp_driver.py --dry-run
    python Tools/banana/b12_detail_mp_driver.py --skip-existing
    python Tools/banana/b12_detail_mp_driver.py --only-detail footprint_mud_n.png --only-mp 0
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
DETAIL_ROOT = ROOT / "Assets" / "Art" / "Placeholders" / "detail"
MP_ROOT = ROOT / "Assets" / "Art" / "Placeholders" / "mp"
PREVIEW_DIR = ROOT / "Artifacts" / "preview" / "b12_detail_mp"
LOG = TOOLS_DIR / ".cache" / "b12_detail_mp_driver.log"

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

# (tw, th) 与 fill_local 默认一致
def _target_wh(fn: str) -> tuple[int, int]:
    if fn.startswith("footprint_"):
        return 96, 96
    if fn.startswith("glow_"):
        return 128, 128
    if fn == "blood_splatter_small.png":
        return 64, 64
    if fn in ("blood_splatter_large.png", "corpse_stain_humanoid.png", "corpse_stain_beast.png"):
        return 128, 128
    if fn == "door_open.png":
        return 96, 96
    if fn == "smoke_plume.png":
        return 64, 128
    return 96, 96


STYLE = (
    "top-down or slight-isometric game VFX decal, transparent background intent, semi-realistic, "
    "muted earthy palette, thin outline, no text, neutral grey for keying"
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
        return proc.returncode, (proc.stdout or "") + (proc.stderr or "")
    except subprocess.TimeoutExpired:
        return 124, "[timeout]"
    except Exception as e:
        return 1, f"{type(e).__name__}: {e}"


def _resize_rgba(src: Path, dst: Path, w: int, h: int) -> None:
    from PIL import Image

    im = Image.open(src).convert("RGBA")
    im = im.resize((w, h), Image.Resampling.LANCZOS)
    dst.parent.mkdir(parents=True, exist_ok=True)
    im.save(dst, format="PNG")


def _prompt_detail(fn: str) -> str:
    stem = fn.replace(".png", "").replace("_", " ")
    return f"single small game overlay decal: {stem}, {STYLE}"


def _prompt_mp(slot: int) -> str:
    return (
        f"multiplayer UI color band ribbon overlay for player slot {slot}, horizontal strip accent, "
        f"{STYLE}"
    )


def process_job(
    tag: str,
    prompt: str,
    out_final: Path,
    *,
    aspect: str,
    tw: int,
    th: int,
    skip_existing: bool,
    dry_run: bool,
    max_gen_retries: int,
    size: str,
) -> str:
    if skip_existing and out_final.exists():
        return f"[skip] {tag}"

    raw = PREVIEW_DIR / f"{tag}_raw.png"
    nobg = PREVIEW_DIR / f"{tag}_nobg.png"

    if dry_run:
        return f"[dry-run] {tag} -> {tw}x{th}"

    for attempt in range(1, max_gen_retries + 1):
        cmd = [
            sys.executable,
            str(TOOLS_DIR / "banana_gen.py"),
            "--protocol",
            "openai",
            "--prompt",
            prompt,
            "--aspect",
            aspect,
            "--size",
            size,
            "--count",
            "1",
            "--out",
            str(raw.relative_to(ROOT)),
        ]
        rc, out = run_cmd(cmd, timeout=720)
        if rc == 0:
            break
        _log(f"[retry {attempt}/{max_gen_retries}] {tag} rc={rc}")
        if attempt < max_gen_retries:
            time.sleep(12.0)
    else:
        return f"[FAIL-gen] {tag} {out[-280:]}"

    raw_real = next(
        (c for c in PREVIEW_DIR.glob(f"{tag}_raw.*") if c.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp"}),
        None,
    )
    if raw_real is None:
        return f"[FAIL-missing] {tag}"

    rc, _ = run_cmd(
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
        return f"[FAIL-rembg] {tag}"

    try:
        _resize_rgba(nobg, out_final, tw, th)
    except Exception as e:
        return f"[FAIL-resize] {tag} {e!s}"
    return f"[ok]   {tag}"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--max-gen-retries", type=int, default=5)
    ap.add_argument("--size", choices=("1K", "2K"), default="1K")
    ap.add_argument(
        "--only-detail",
        default="",
        help="comma filenames under detail/, e.g. footprint_mud_n.png",
    )
    ap.add_argument(
        "--only-mp",
        default="",
        help="comma slot indices 0-7 for player_slot_N.png",
    )
    ap.add_argument("--skip-detail", action="store_true", help="only run mp jobs")
    ap.add_argument("--skip-mp", action="store_true", help="only run detail jobs")
    args = ap.parse_args()

    jobs: list[tuple[str, Path, str, str, int, int]] = []

    detail_filter = {s.strip() for s in args.only_detail.split(",") if s.strip()}
    if not args.skip_detail:
        for fn in DETAIL_FILES:
            if detail_filter and fn not in detail_filter:
                continue
            tw, th = _target_wh(fn)
            tag = f"d__{fn.replace('.png', '')}"[:80]
            aspect = "1:2" if fn == "smoke_plume.png" else "1:1"
            jobs.append((tag, DETAIL_ROOT / fn, _prompt_detail(fn), aspect, tw, th))

    if not args.skip_mp:
        slots = list(range(8))
        if args.only_mp.strip():
            slots = [int(x.strip()) for x in args.only_mp.split(",") if x.strip().isdigit()]
        for i in slots:
            tag = f"mp__{i}"
            jobs.append((tag, MP_ROOT / f"player_slot_{i}.png", _prompt_mp(i), "1:1", 64, 64))

    if args.limit > 0:
        jobs = jobs[: args.limit]

    _log(f"===== B12 detail/mp driver: {len(jobs)} jobs =====")
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    results: list[str] = []
    for i, (tag, outp, pr, aspect, tw, th) in enumerate(jobs, 1):
        t1 = time.time()
        msg = process_job(
            tag,
            pr,
            outp,
            aspect=aspect,
            tw=tw,
            th=th,
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
