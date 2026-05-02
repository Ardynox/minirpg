"""
B6 UI icons — profession(14) + race(12) + interaction(43) + incident(10) = 79

流水线：banana_gen -> rembg -> crop_square_resize -> Assets/Art/Placeholders/ui_icons/

规格对齐 Artifacts/素材需求清单_2026-04-20.md §3.4–3.5、§3.11：
- profession_*.png / race_*.png：圆盾徽记风，输出 128
- interaction_*.png：白描线稿风，输出 48（用整段 --prompt，不拼 style_base）
- incident_*.png：事件缩略图带轻微情绪色，输出 96

运行：
    python Tools/banana/b6_ui_icons_driver.py --dry-run
    python Tools/banana/b6_ui_icons_driver.py --skip-existing
    python Tools/banana/b6_ui_icons_driver.py --only profession --limit 2
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
PREVIEW_DIR = ROOT / "Artifacts" / "preview" / "b6_ui_icons"
OUT_DIR = ROOT / "Assets" / "Art" / "Placeholders" / "ui_icons"
LOG = TOOLS_DIR / ".cache" / "b6_driver.log"

SHIELD_TAIL = (
    "circular wooden shield emblem frame, centered symbol, fills ~78% of canvas, "
    "thin dark-brown outline, muted earthy low-saturation hand-painted game badge, "
    "no text, no watermark, solid neutral flat gray background for easy keying"
)


def _shield(subject: str) -> str:
    return f"single flat game UI badge: {subject}, {SHIELD_TAIL}"


INTERACTION_HINTS: dict[str, str] = {
    "melee_attack": "two crossed swords with motion slash",
    "heavy_strike": "heavy overhead maul or warhammer strike arc",
    "block": "raised rectangular shield blocking stance",
    "poison_spit": "droplet with skull vapor curl",
    "undead_drain": "ghostly hand reaching with wisp drain",
    "bite": "open fanged jaw bite mark",
    "claw_strike": "three curved claw slashes",
    "sting": "curved scorpion tail stinger",
    "web_spit": "spider web spiral corner strands",
    "trunk_slam": "thick tree trunk slamming downward wedge",
    "spear_thrust": "spear head thrust forward silhouette",
    "bow_shoot": "bow with arrow nocked horizontal",
    "bow_shoot_heavy": "longbow drawn heavy arrow large",
    "move": "walking boot prints with arrow forward",
    "look": "eye with magnifying glance rays",
    "identify": "small lens over question mark silhouette",
    "talk": "two speech bubbles facing each other",
    "trade": "balanced scales with coin stack",
    "tame": "open palm with small heart and leash curl",
    "dig": "shovel blade in dirt mound",
    "chop": "axe embedded in wood stump wedge",
    "mine": "pickaxe striking rock crack",
    "light_fire": "small campfire sparking ignite",
    "extinguish_fire": "bucket pouring over flame douse",
    "sword_slash": "single arcing sword slash trail",
    "sword_pommel": "sword held vertical pommel strike down",
    "sword_parry": "two blades crossed parry X",
    "shield_bash": "shield edge forward bash impact lines",
    "hammer_strike": "smith hammer diagonal strike",
    "hammer_crush": "heavy hammer on cracked surface crush",
    "dagger_stab": "short dagger thrust stab trail",
    "dagger_slash": "curved dagger slash arc",
    "axe_chop": "broad axe chop into log split",
    "tend_self": "bandage wrap around arm self-care",
    "tend_other": "two hands bandaging another silhouette",
    "reload_firearm": "revolver cylinder open with cartridges",
    "operate": "gear cog with wrench turning",
    "revolver_shot": "revolver side profile muzzle flash small",
    "bolt_rifle_shot": "rifle bolt action with muzzle line",
    "shotgun_blast": "shotgun barrel spread pellet cone",
    "strip_corpse": "silhouette figure stripping garment from prone outline",
    "butcher_corpse": "cleaver and meat hook over carcass outline",
    "harvest_corpse": "scalpel or knife with organ jar outline",
}

INCIDENT_SUBJECT: dict[str, str] = {
    "raid_goblin": "tiny chaotic raid scene, crude clubs and green-brown goblin silhouettes, dust puff, muted sickly green accent only on weapons",
    "raid_bandit": "hooded bandit silhouettes with daggers, brown dust raid tension",
    "raid_beast": "beast claw marks and fanged shadow silhouette threat",
    "wanderer_join": "open door light beam with traveler staff silhouette welcoming",
    "trader_visit": "small covered wagon with trade sacks neutral warm beige",
    "resource_drop": "crate under parachute or rope lowering supply drop",
    "cold_snap": "large snowflake over frosted ground crack, cool blue-gray vignette mood",
    "heat_wave": "relentless sun disk over cracked dry soil, warm orange haze mood",
    "traveler_passing": "walking staff and bedroll bundle silhouette moving along path",
    "refugee_arrival": "small huddled figures near tiny campfire smoke pleading silhouette",
}

PROFESSION_SUBJECT: dict[str, str] = {
    "merchant": "balance scales with coin stack at center of shield",
    "elder": "rolled parchment scroll and small quill at center",
    "miner": "crossed pickaxe and ore nugget chunk",
    "lumberjack": "crossed hand axe and split log wedge",
    "hunter": "bow with single arrow nocked silhouette",
    "blacksmith": "hammer over small anvil silhouette",
    "herbalist": "mortar pestle with leaf sprig",
    "cook": "stew pot with ladle steam wisp",
    "soldier": "short sword over heater shield silhouette",
    "scout": "compass rose with small eye mark",
    "tailor": "threaded needle through cloth swatch",
    "farmer": "sheaf of wheat stalks",
    "guard": "halberd head upright polearm",
    "shaman": "small ritual mask with two feathers and bone bead",
}

RACE_SUBJECT: dict[str, str] = {
    "human": "minimal human bust profile neutral calm",
    "goblin": "goblin pointed ears and sharp grin mask shape",
    "slime": "blob droplet with inner core dot gelatinous",
    "undead": "cracked skull sideways hollow eyes",
    "elf": "pair of curved leaf-shapes suggesting elegant ears",
    "orc": "tusked lower jaw silhouette fierce",
    "spider": "simplified eight legs radial spider body",
    "scorpion": "curved tail with barbed stinger arched",
    "treant": "gnarled branch antlers like small crown",
    "wolf": "wolf head profile howling moonward",
    "bear": "bear paw pad slash marks",
    "rat": "crouched rat silhouette long tail",
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


def _interaction_full_prompt(hint: str) -> str:
    return (
        "Classic isometric RPG ground-action HUD glyph. STRICT RULES: "
        "ONLY off-white / bone-white ink strokes as linework on a solid neutral mid-gray square background; "
        "NO rainbow, NO saturated fills, NO photo realism; shapes read as white line-art icon. "
        "Single centered readable symbol. "
        f"Concept: {hint}. "
        "No text, no watermark, no UI chrome frame."
    )


def _incident_prompt(incident_id: str) -> str:
    subj = INCIDENT_SUBJECT.get(
        incident_id,
        f"small narrative vignette icon for event {incident_id}, muted earthy survival tone",
    )
    return (
        f"single square game event notification thumbnail: {subj}, "
        "soft vignette, transparent edges preferred but if not then solid neutral gray keyed center, "
        "hand-painted semi-realistic muted palette, no text, no watermark"
    )


def build_jobs(only: set[str]) -> list[tuple[str, str, str, int, bool]]:
    """(category, file_id, prompt_or_subject, crop_size, use_style_shield)"""
    jobs: list[tuple[str, str, str, int, bool]] = []

    if "profession" in only:
        for pid in _load_ids(ROOT / "Data" / "professions.json"):
            subj = PROFESSION_SUBJECT.get(
                pid, f"profession emblem symbol for role {pid}, simple recognizable tool"
            )
            jobs.append(("profession", f"profession_{pid}", _shield(subj), 128, True))

    if "race" in only:
        for rid in _load_ids(ROOT / "Data" / "races.json"):
            subj = RACE_SUBJECT.get(rid, f"species emblem for {rid}, simple silhouette")
            jobs.append(("race", f"race_{rid}", _shield(subj), 128, True))

    if "interaction" in only:
        for iid in _load_ids(ROOT / "Data" / "interactions.json"):
            hint = INTERACTION_HINTS.get(
                iid, f"abstract action glyph for {iid.replace('_', ' ')}, clear readable shape"
            )
            jobs.append(
                ("interaction", f"interaction_{iid}", _interaction_full_prompt(hint), 48, False)
            )

    if "incident" in only:
        for nid in _load_ids(ROOT / "Data" / "storyteller_incidents.json"):
            jobs.append(("incident", f"incident_{nid}", _incident_prompt(nid), 96, True))

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
        default="profession,race,interaction,incident",
        help="comma: profession,race,interaction,incident",
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

    _log(f"===== B6 driver: {len(jobs)} jobs, only={sorted(selected)} =====")
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
        _log(f"[{i:>3}/{len(jobs)}] ({cat:<12}) {msg}  [+{time.time() - t1:.0f}s]")
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
