"""
B4 — 人脸部件 PNG（与 `Data/FaceParts/*.json` 非空 `imagePath` 一一对应）

流水线：`banana_gen`（1:1）→ `rembg` → `crop_square_resize` → `Assets/Faces/**`（默认 **256×256**）。

运行：
    python Tools/banana/b4_faces_driver.py --dry-run
    python Tools/banana/b4_faces_driver.py --skip-existing
    python Tools/banana/b4_faces_driver.py --category hair --limit 3
    python Tools/banana/b4_faces_driver.py --only Hair/long_flowing.png

依赖：`Tools/banana/.env`；中国大陆需代理。无 API 时用 `b4_faces_fill_local.py` 先填满路径。
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
FACEPART_DIR = ROOT / "Data" / "FaceParts"
PREVIEW_DIR = ROOT / "Artifacts" / "preview" / "b4_faces"
LOG = TOOLS_DIR / ".cache" / "b4_faces_driver.log"

STYLE = (
    "RPG character face customization part for layered portrait UI: "
    "front-facing, centered, clean alpha silhouette, semi-realistic hand-painted, "
    "muted earthy palette, soft ambient light, thin brown outline, "
    "no text, no watermark, neutral grey studio background for easy keying"
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
        out = (proc.stdout or "") + (proc.stderr or "")
        return proc.returncode, out
    except subprocess.TimeoutExpired:
        return 124, "[timeout]"
    except Exception as e:
        return 1, f"[exception] {type(e).__name__}: {e}"


def res_to_fs(res: str) -> Path:
    rel = res[len("res://") :].lstrip("/")
    return ROOT / rel


def _iter_jobs() -> list[tuple[str, str, str, Path]]:
    """(category, entry_id, prompt_fragment, fs_out)."""
    jobs: list[tuple[str, str, str, Path]] = []
    for path in sorted(FACEPART_DIR.glob("*.json")):
        category = path.stem
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        if not isinstance(data, list):
            continue
        for entry in data:
            if not isinstance(entry, dict):
                continue
            p = entry.get("imagePath")
            if not isinstance(p, str) or not p.startswith("res://"):
                continue
            eid = str(entry.get("id", ""))
            name = str(entry.get("displayName", eid))
            fs = res_to_fs(p)
            # 英文主体：id 用空格替换下划线 + 类型提示
            frag = f"{category.replace('_', ' ')} — {eid.replace('_', ' ')} ({name})"
            jobs.append((category, eid, frag, fs))
    return jobs


def _face_prompt(frag: str) -> str:
    return (
        f"{frag}. {STYLE}"
    )


def process_one(
    job_key: str,
    fragment: str,
    out_final: Path,
    *,
    crop: int,
    skip_existing: bool,
    dry_run: bool,
    max_gen_retries: int,
    size: str,
) -> str:
    if skip_existing and out_final.exists():
        return f"[skip] {job_key} (existing)"

    raw = PREVIEW_DIR / f"{job_key}_raw.png"
    nobg = PREVIEW_DIR / f"{job_key}_nobg.png"
    full_prompt = _face_prompt(fragment)

    if dry_run:
        return f"[dry-run] {job_key} crop={crop}"

    for attempt in range(1, max_gen_retries + 1):
        cmd = [
            sys.executable,
            str(TOOLS_DIR / "banana_gen.py"),
            "--protocol",
            "openai",
            "--prompt",
            full_prompt,
            "--aspect",
            "1:1",
            "--size",
            size,
            "--count",
            "1",
            "--out",
            str(raw.relative_to(ROOT)),
        ]
        rc, out = run_cmd(cmd, timeout=840)
        if rc == 0:
            break
        _log(f"[retry {attempt}/{max_gen_retries}] {job_key} banana_gen rc={rc}")
        if attempt < max_gen_retries:
            time.sleep(12.0)
    else:
        return f"[FAIL-gen] {job_key} rc={rc}  {out[-380:]}"

    candidates = list(PREVIEW_DIR.glob(f"{job_key}_raw.*"))
    raw_real = None
    for c in candidates:
        if c.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp"}:
            raw_real = c
            break
    if raw_real is None:
        return f"[FAIL-gen-missing] {job_key}"

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
        return f"[FAIL-rembg] {job_key} rc={rc}  {out[-200:]}"

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
        "0.06",
    ]
    rc, out = run_cmd(cmd, timeout=60)
    if rc != 0:
        return f"[FAIL-crop] {job_key} rc={rc}  {out[-200:]}"

    return f"[ok]   {job_key}"


def _safe_key(fs: Path) -> str:
    rel = fs.relative_to(ROOT / "Assets" / "Faces").as_posix().replace("/", "__").replace(".png", "")
    return rel[:120]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--limit", type=int, default=0, help="max jobs (0=all)")
    ap.add_argument("--max-gen-retries", type=int, default=3)
    ap.add_argument("--size", choices=("1K", "2K"), default="1K")
    ap.add_argument(
        "--crop",
        type=int,
        default=256,
        help="square edge after crop_square_resize (PortraitComposer / identity B4)",
    )
    ap.add_argument(
        "--category",
        default="",
        help="comma-separated FaceParts json stem(s): hair,eyes,…",
    )
    ap.add_argument(
        "--only",
        default="",
        help="comma path fragments under Assets/Faces, e.g. Hair/long_flowing.png",
    )
    args = ap.parse_args()
    crop = max(64, min(512, args.crop))

    all_jobs = _iter_jobs()
    sel_cat = {s.strip().lower() for s in args.category.split(",") if s.strip()}
    only_frag = {s.strip().replace("\\", "/") for s in args.only.split(",") if s.strip()}

    filtered: list[tuple[str, str, str, Path]] = []
    for category, eid, frag, fs in all_jobs:
        if sel_cat and category.lower() not in sel_cat:
            continue
        rel_posix = fs.relative_to(ROOT).as_posix()
        try:
            tail = fs.relative_to(ROOT / "Assets" / "Faces").as_posix()
        except ValueError:
            tail = fs.name
        if only_frag and not any(f in tail.replace("\\", "/") for f in only_frag):
            continue
        filtered.append((category, eid, frag, fs))

    if args.limit > 0:
        filtered = filtered[: args.limit]

    _log(
        f"===== B4 faces driver: {len(filtered)} jobs "
        f"(of {len(all_jobs)} defined with imagePath) crop={crop} =====",
    )
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    results: list[str] = []
    for i, (category, eid, frag, fs) in enumerate(filtered, 1):
        job_key = _safe_key(fs)
        prompt = frag
        t1 = time.time()
        msg = process_one(
            job_key,
            prompt,
            fs,
            crop=crop,
            skip_existing=args.skip_existing,
            dry_run=args.dry_run,
            max_gen_retries=args.max_gen_retries,
            size=args.size,
        )
        _log(f"[{i:>3}/{len(filtered)}] ({category:<14}) {msg}  [+{time.time() - t1:.0f}s]")
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
