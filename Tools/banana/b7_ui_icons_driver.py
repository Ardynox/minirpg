"""
B7 UI icons — surgery(13) + room(6) + capacity(10) + limb(10) = 39

流水线：banana_gen -> rembg -> crop_square_resize -> Assets/Art/Placeholders/ui_icons/

对齐 Artifacts/素材需求清单_2026-04-20.md §3.9–3.12、§5 B7：
- surgery_*：科普 / 手术示意小图，96
- room_*：小木牌 room role，96
- capacity_*：能力条抽象符号，64
- limb_*：仅 10 个大类线稿（非 limbs.json 全量）

运行：
    python Tools/banana/b7_ui_icons_driver.py --dry-run
    python Tools/banana/b7_ui_icons_driver.py --skip-existing
    python Tools/banana/b7_ui_icons_driver.py --only surgery --limit 2
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
PREVIEW_DIR = ROOT / "Artifacts" / "preview" / "b7_ui_icons"
OUT_DIR = ROOT / "Assets" / "Art" / "Placeholders" / "ui_icons"
LOG = TOOLS_DIR / ".cache" / "b7_driver.log"

STYLE_UI = (
    "single flat game UI icon, hand-painted semi-realistic, muted earthy low-saturation palette, "
    "thin dark-brown outline, no text, no watermark, solid neutral flat gray background for easy keying"
)

# 10 个大类（与清单 §3.12 一致，文件名 limb_<key>.png）
LIMB_KEYS: list[tuple[str, str]] = [
    ("head", "human head profile line-art anatomy diagram, frontal"),
    ("neck", "neck vertebrae simplified line-art"),
    ("torso", "ribcage and torso outline line-art"),
    ("left_arm", "left upper arm and forearm line-art"),
    ("right_arm", "right upper arm and forearm line-art"),
    ("left_leg", "left thigh and shin line-art"),
    ("right_leg", "right thigh and shin line-art"),
    ("heart_lungs", "heart and lungs simplified chest diagram"),
    ("eyes", "pair of eyes simplified medical icon"),
    ("hands", "open hands palms medical line-art"),
]

SURGERY_HINTS: dict[str, str] = {
    "harvest_heart": "surgical tray with heart organ outline, respectful medical illustration",
    "harvest_lung": "two lung lobes on tray with scalpel silhouette",
    "harvest_eye": "eyeball cross-section with fine forceps",
    "harvest_arm": "severed arm stump with tourniquet line-art",
    "harvest_leg": "leg amputation site with bandage wrap diagram",
    "live_harvest_heart": "open chest cavity with gloved hands holding heart, dramatic but clinical",
    "live_harvest_lung": "thoracic cavity exposing lungs, surgical clamps",
    "live_harvest_eye": "eye socket surgery with speculum",
    "install_heart": "chest sutured with new heart implant schematic",
    "install_lung": "rib spreader with lung graft placement",
    "install_eye": "ocular prosthesis insertion side view",
    "install_arm": "cybernetic arm attachment at shoulder joint schematic",
    "install_leg": "prosthetic leg socket fitting diagram",
}

ROOM_HINTS: dict[str, str] = {
    "storage": "small wooden hanging sign with crate and barrel silhouette",
    "kitchen": "wooden sign with pot steam and ladle",
    "workshop": "wooden sign with crossed hammer and wrench",
    "dormitory": "wooden sign with simple bed icon",
    "clinic": "wooden sign with herbal cross and mortar",
    "market": "wooden sign with awning and coin stack",
}

CAPACITY_HINTS: dict[str, str] = {
    "consciousness": "glowing brain outline with spark dots",
    "blood_circulation": "heart with pulsing arteries loop",
    "manipulation": "hand gripping small gear",
    "moving": "walking legs with motion arcs",
    "sight": "eye with light rays",
    "metabolism": "stomach with flame and leaf balance",
    "talking": "mouth with sound waves",
    "eating": "fork and spoon over bowl",
    "breathing": "lungs with air swirl arrows",
    "hearing": "ear with sound ripple rings",
}


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


def _surgery_prompt(op_id: str) -> str:
    hint = SURGERY_HINTS.get(
        op_id,
        f"clinical surgery education icon for operation {op_id.replace('_', ' ')}, muted, no gore pools",
    )
    return f"{hint}, {STYLE_UI}"


def _room_prompt(room_id: str) -> str:
    hint = ROOM_HINTS.get(room_id, f"small wooden tavern sign for room role {room_id}")
    return f"{hint}, {STYLE_UI}"


def _capacity_prompt(cap_id: str) -> str:
    hint = CAPACITY_HINTS.get(cap_id, f"abstract vital stat glyph for {cap_id}")
    return (
        f"compact RPG status bar glyph: {hint}, centered symbol only, "
        f"{STYLE_UI}"
    )


def _limb_prompt(key: str, subject: str) -> str:
    return (
        f"medical anatomy line diagram icon: {subject}, very clean ink lines, "
        f"no blood splatter, {STYLE_UI}"
    )


def build_jobs(only: set[str]) -> list[tuple[str, str, str, int, bool]]:
    """(category, file_id, prompt, crop_size, use_style_shield)"""
    jobs: list[tuple[str, str, str, int, bool]] = []

    if "surgery" in only:
        for oid in _load_ids(ROOT / "Data" / "surgery_operations.json"):
            jobs.append(("surgery", f"surgery_{oid}", _surgery_prompt(oid), 96, True))

    if "room" in only:
        for rid in _load_ids(ROOT / "Data" / "room_roles.json"):
            jobs.append(("room", f"room_{rid}", _room_prompt(rid), 96, True))

    if "capacity" in only:
        for cid in _load_ids(ROOT / "Data" / "capacities.json"):
            jobs.append(("capacity", f"capacity_{cid}", _capacity_prompt(cid), 64, True))

    if "limb" in only:
        for key, subj in LIMB_KEYS:
            jobs.append(("limb", f"limb_{key}", _limb_prompt(key, subj), 96, True))

    return jobs


def process_one(
    icon_id: str,
    prompt: str,
    crop: int,
    use_style: bool,
    *,
    skip_existing: bool,
    dry_run: bool,
    max_gen_retries: int,
) -> str:
    out_final = OUT_DIR / f"{icon_id}.png"
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
    ap.add_argument(
        "--only",
        default="surgery,room,capacity,limb",
        help="comma: surgery,room,capacity,limb",
    )
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--limit", type=int, default=0, help="max items (0=all)")
    ap.add_argument("--max-gen-retries", type=int, default=3)
    args = ap.parse_args()

    selected = {s.strip() for s in args.only.split(",") if s.strip()}
    jobs = build_jobs(selected)
    if args.limit > 0:
        jobs = jobs[: args.limit]

    _log(f"===== B7 driver: {len(jobs)} jobs, only={sorted(selected)} =====")
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    results: list[str] = []
    for i, (cat, fid, prompt, crop, use_style) in enumerate(jobs, 1):
        t1 = time.time()
        msg = process_one(
            fid,
            prompt,
            crop,
            use_style,
            skip_existing=args.skip_existing,
            dry_run=args.dry_run,
            max_gen_retries=args.max_gen_retries,
        )
        _log(f"[{i:>3}/{len(jobs)}] ({cat:<10}) {msg}  [+{time.time() - t1:.0f}s]")
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
