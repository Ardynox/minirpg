"""
B1 — 战斗特效单图 fallback + 天气平铺贴图 + 主菜单品牌图

对齐 `Artifacts/素材需求清单_2026-04-20.md` §1.1 P0：
- `Data/combat_fx.json` 引用的 `resourceId` 对应 `CombatFxPlayer` 单图 fallback：
  `res://Assets/Art/Placeholders/effects/<id>.png`（多帧目录由 `resource_catalog.json` 优先；缺帧时才走单图）。
- `WeatherFxController.WeatherAssetIds` → `Assets/Art/Placeholders/weather/<id>.png`（目标 1024×512 可平铺）。
- 主菜单：`branding/logo_main.png`、`branding/menu_background_1920x1080.png`。

运行：
    python Tools/banana/b1_placeholders_driver.py --dry-run
    python Tools/banana/b1_placeholders_driver.py --skip-existing
    python Tools/banana/b1_placeholders_driver.py --only effect --limit 2
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = Path(__file__).resolve().parent
PREVIEW_DIR = ROOT / "Artifacts" / "preview" / "b1_placeholders"
OUT_EFFECTS = ROOT / "Assets" / "Art" / "Placeholders" / "effects"
OUT_WEATHER = ROOT / "Assets" / "Art" / "Placeholders" / "weather"
OUT_BRANDING = ROOT / "Assets" / "Art" / "Placeholders" / "branding"
LOG = TOOLS_DIR / ".cache" / "b1_placeholders_driver.log"

# 与 Module/Render/WeatherFxController.cs WeatherAssetIds 一致
WEATHER_IDS = [
    "snow_cover",
    "sand_cover",
    "ice_gloss",
    "wet_gloss",
    "rain",
    "fog",
    "snow",
    "dust",
    "lightning",
]

STYLE_FX = (
    "2D combat VFX sprite, hand-painted semi-realistic, muted low-saturation, soft overcast lighting, "
    "no text, no watermark, solid neutral flat gray background for easy keying, crisp readable silhouette"
)

STYLE_WEATHER = (
    "seamless horizontal tiling texture strip for 2D game weather overlay, hand-painted semi-realistic, "
    "muted earthy palette, soft overcast, no hard cast shadow, no text, no watermark, "
    "solid neutral flat gray background for easy keying"
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


def _resource_ids_from_combat_fx() -> list[str]:
    data = json.loads((ROOT / "Data" / "combat_fx.json").read_text(encoding="utf-8-sig"))
    effects = data.get("effects") or {}
    ids: set[str] = set()
    for fx in effects.values():
        if not isinstance(fx, dict):
            continue
        for layer in fx.values():
            if isinstance(layer, dict) and layer.get("kind") in ("sprite", "projectile"):
                rid = layer.get("resourceId")
                if isinstance(rid, str) and rid.startswith("fx_"):
                    ids.add(rid)
    # 清单 §1.1 单列的 lightning / pickup（地图或其它路径可能引用）
    ids.update({"fx_lightning_strike", "fx_pickup_glint"})
    return sorted(ids)


def _effect_prompt(rid: str) -> str:
    slug = rid.replace("fx_", "").replace("_", " ")
    return f"single {slug} combat effect element, centered, {STYLE_FX}"


def _weather_prompt(wid: str) -> str:
    return f"weather overlay texture: {wid.replace('_', ' ')}, subtle particles or surface variation, {STYLE_WEATHER}"


def _letterbox_resize_rgba(src: Path, dest: Path, w: int, h: int) -> None:
    from PIL import Image

    im = Image.open(src).convert("RGBA")
    im.thumbnail((w, h), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ox = (w - im.width) // 2
    oy = (h - im.height) // 2
    canvas.paste(im, (ox, oy))
    dest.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(dest)


def _resize_cover_rgb(src: Path, dest: Path, w: int, h: int) -> None:
    from PIL import Image

    im = Image.open(src).convert("RGB")
    im = im.resize((w, h), Image.Resampling.LANCZOS)
    dest.parent.mkdir(parents=True, exist_ok=True)
    im.save(dest)


@dataclass(frozen=True)
class Job:
    category: str
    file_id: str
    prompt: str
    out_path: Path
    aspect: str
    image_size: str
    use_style: bool
    use_rembg: bool
    square_crop: int | None  # None = skip crop_square_resize
    letterbox: tuple[int, int] | None  # after rembg, before final


def build_jobs(only: set[str]) -> list[Job]:
    jobs: list[Job] = []

    if "effect" in only:
        for rid in _resource_ids_from_combat_fx():
            jobs.append(
                Job(
                    category="effect",
                    file_id=rid,
                    prompt=_effect_prompt(rid),
                    out_path=OUT_EFFECTS / f"{rid}.png",
                    aspect="1:1",
                    image_size="1K",
                    use_style=True,
                    use_rembg=True,
                    square_crop=256,
                    letterbox=None,
                )
            )

    if "weather" in only:
        for wid in WEATHER_IDS:
            jobs.append(
                Job(
                    category="weather",
                    file_id=wid,
                    prompt=_weather_prompt(wid),
                    out_path=OUT_WEATHER / f"{wid}.png",
                    aspect="2:1",
                    image_size="2K",
                    use_style=True,
                    use_rembg=True,
                    square_crop=None,
                    letterbox=(1024, 512),
                )
            )

    if "branding" in only:
        jobs.append(
            Job(
                category="branding",
                file_id="logo_main",
                prompt=(
                    "game logo wordmark for fantasy survival RPG, hand-painted semi-realistic title treatment, "
                    "muted earthy palette, no real-world franchise, no text beyond stylized title letters OK, "
                    "solid neutral flat gray background for easy keying"
                ),
                out_path=OUT_BRANDING / "logo_main.png",
                aspect="1:1",
                image_size="1K",
                use_style=True,
                use_rembg=True,
                square_crop=512,
                letterbox=None,
            )
        )
        jobs.append(
            Job(
                category="branding",
                file_id="menu_background_1920x1080",
                prompt=(
                    "wide atmospheric main menu background, distant low-poly hills and muted sky, "
                    "hand-painted semi-realistic, desaturated earthy tones, no UI, no characters, no watermark"
                ),
                out_path=OUT_BRANDING / "menu_background_1920x1080.png",
                aspect="16:9",
                image_size="2K",
                use_style=True,
                use_rembg=False,
                square_crop=None,
                letterbox=None,
            )
        )

    return jobs


def process_job(job: Job, *, skip_existing: bool, dry_run: bool, max_gen_retries: int) -> str:
    if skip_existing and job.out_path.exists():
        return f"[skip] {job.file_id} (existing)"

    raw = PREVIEW_DIR / f"{job.file_id}.png"
    nobg = PREVIEW_DIR / f"{job.file_id}_nobg.png"

    if dry_run:
        return f"[dry-run] {job.category}/{job.file_id} aspect={job.aspect}"

    for attempt in range(1, max_gen_retries + 1):
        if job.use_style:
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
                job.prompt,
                "--aspect",
                job.aspect,
                "--size",
                job.image_size,
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
                job.prompt,
                "--aspect",
                job.aspect,
                "--size",
                job.image_size,
                "--count",
                "1",
                "--out",
                str(raw.relative_to(ROOT)),
            ]
        rc, out = run_cmd(cmd, timeout=900)
        if rc == 0:
            break
        _log(f"[retry {attempt}/{max_gen_retries}] {job.file_id} banana_gen rc={rc}")
        if attempt < max_gen_retries:
            time.sleep(12.0)
    else:
        return f"[FAIL-gen] {job.file_id} rc={rc}  {out[-280:]}"

    candidates = list(PREVIEW_DIR.glob(f"{job.file_id}.*"))
    raw_real = None
    for c in candidates:
        if c.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp"} and c.name != nobg.name:
            raw_real = c
            break
    if raw_real is None:
        return f"[FAIL-gen-missing] {job.file_id}"

    work_path = raw_real
    if job.use_rembg:
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
            return f"[FAIL-rembg] {job.file_id} rc={rc}  {out[-200:]}"
        work_path = nobg

    if job.square_crop is not None:
        cmd = [
            sys.executable,
            str(TOOLS_DIR / "crop_square_resize.py"),
            "--in",
            str(work_path.relative_to(ROOT)),
            "--out",
            str(job.out_path.relative_to(ROOT)),
            "--size",
            str(job.square_crop),
            "--margin",
            "0.06",
        ]
        rc, out = run_cmd(cmd, timeout=120)
        if rc != 0:
            return f"[FAIL-crop] {job.file_id} rc={rc}  {out[-200:]}"
    elif job.letterbox:
        try:
            _letterbox_resize_rgba(work_path, job.out_path, job.letterbox[0], job.letterbox[1])
        except Exception as e:
            return f"[FAIL-letterbox] {job.file_id} {type(e).__name__}: {e}"
    elif job.category == "branding" and job.file_id == "menu_background_1920x1080":
        try:
            _resize_cover_rgb(raw_real, job.out_path, 1920, 1080)
        except Exception as e:
            return f"[FAIL-resize-bg] {job.file_id} {type(e).__name__}: {e}"
    else:
        job.out_path.parent.mkdir(parents=True, exist_ok=True)
        data = work_path.read_bytes()
        job.out_path.write_bytes(data)

    return f"[ok]   {job.category}/{job.file_id}"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="effect,weather,branding", help="comma: effect,weather,branding")
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--limit", type=int, default=0, help="max jobs (0=all)")
    ap.add_argument("--max-gen-retries", type=int, default=3)
    args = ap.parse_args()

    selected = {s.strip() for s in args.only.split(",") if s.strip()}
    jobs = build_jobs(selected)
    if args.limit > 0:
        jobs = jobs[: args.limit]

    _log(f"===== B1 placeholders driver: {len(jobs)} jobs, only={sorted(selected)} =====")
    OUT_EFFECTS.mkdir(parents=True, exist_ok=True)
    OUT_WEATHER.mkdir(parents=True, exist_ok=True)
    OUT_BRANDING.mkdir(parents=True, exist_ok=True)
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    results: list[str] = []
    for i, job in enumerate(jobs, 1):
        t1 = time.time()
        msg = process_job(
            job,
            skip_existing=args.skip_existing,
            dry_run=args.dry_run,
            max_gen_retries=args.max_gen_retries,
        )
        _log(f"[{i:>3}/{len(jobs)}] {msg}  [+{time.time() - t1:.0f}s]")
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
