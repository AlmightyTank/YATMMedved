#!/usr/bin/env python3
r"""
cleanup_medved_bot_mods.py

Cleans SPT bot inventory.mods pools using your server error log and, optionally,
your local SPT templates/items.json.

What it fixes:
  1) Removes slot pools from items where the log says:
       Slot: mod_x does not exist for weapon: <tpl> ... on <bot>

  2) Adds missing inventory.mods entries where the log says:
       bot: <bot> lacks a mod slot pool for item: <tpl> ...
     It first copies the same tpl's pool from another bot file if available.
     If --items is provided, it can build the pool from the real item Slots filters.
     If --empty-fallback is set, it creates {} as a last resort.

  3) With --items, validates every inventory.mods[tpl] slot name against the real
     item template and can add missing slot pools from the item's real Slots filters.

Recommended use:
  python cleanup_medved_bot_mods.py ^
    --bots bossMedvedSokol.json followerMedvedBuran.json followerMedvedKedr.json ^
    --log server.log ^
    --items "C:\RealSPT\SPT_Data\Server\database\templates\items.json" ^
    --write

Dry run first:
  python cleanup_medved_bot_mods.py --bots bossMedvedSokol.json followerMedvedBuran.json followerMedvedKedr.json --log server.log --items items.json
"""

from __future__ import annotations

import argparse
import copy
import json
import os
import re
import shutil
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Set, Tuple

Tpl = str
SlotName = str
BotName = str
Pool = Dict[SlotName, List[Tpl]]

TPL_RE = re.compile(r"^[0-9a-f]{24}$", re.IGNORECASE)

MISSING_POOL_RE = re.compile(
    r"bot:\s*(?P<bot>[a-zA-Z0-9_]+)\s+lacks a mod slot pool for item:\s*"
    r"(?P<tpl>[0-9a-f]{24})\b(?:\s+(?P<name>[^\r\n]+))?",
    re.IGNORECASE,
)

BAD_SLOT_RE = re.compile(
    r"Slot:\s*(?P<slot>[a-zA-Z0-9_]+)\s+does not exist for weapon:\s*"
    r"(?P<tpl>[0-9a-f]{24})\b(?:\s+.*?)?\s+on\s+(?P<bot>[a-zA-Z0-9_]+)",
    re.IGNORECASE,
)

REQUIRED_EMPTY_RE = re.compile(
    r"Required slot\s+'(?P<slot>[a-zA-Z0-9_]+)'\s+on\s+(?P<item_name>.*?)\s+"
    r"(?P<equip_slot>[A-Za-z0-9_]+)\s+was empty on\s+(?P<bot>[a-zA-Z0-9_]+)",
    re.IGNORECASE,
)

WEAPON_FALLBACK_RE = re.compile(
    r"Weapon\s+(?P<tpl>[0-9a-f]{24})\s+-\s+(?P<name>.*?)\s+was generated incorrectly.*",
    re.IGNORECASE,
)


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def write_json(path: Path, data: Any) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=4, ensure_ascii=False)
        f.write("\n")


def norm_bot_name(value: str) -> str:
    return re.sub(r"[^a-z0-9]", "", value.lower())


def bot_name_from_path(path: Path) -> str:
    return norm_bot_name(path.stem)


def is_tpl(value: Any) -> bool:
    return isinstance(value, str) and bool(TPL_RE.match(value))


def dedupe_keep_order(values: Iterable[str]) -> List[str]:
    seen: Set[str] = set()
    out: List[str] = []
    for value in values:
        if not isinstance(value, str):
            continue
        if value in seen:
            continue
        seen.add(value)
        out.append(value)
    return out


def extract_filter_list(filter_obj: Any) -> List[str]:
    if not isinstance(filter_obj, dict):
        return []
    values = filter_obj.get("Filter")
    if values is None:
        values = filter_obj.get("filter")
    if values is None:
        values = filter_obj.get("filters")
    if isinstance(values, list):
        return [x for x in values if isinstance(x, str)]
    return []


def load_items_db(path: Optional[Path]) -> Dict[str, Any]:
    if not path:
        return {}
    raw = load_json(path)

    # SPT templates/items.json is usually a dict keyed by tpl.
    if isinstance(raw, dict):
        if all(isinstance(k, str) for k in raw.keys()):
            return raw
        if "items" in raw and isinstance(raw["items"], dict):
            return raw["items"]

    # Some exports are lists of item objects.
    if isinstance(raw, list):
        return {item.get("_id"): item for item in raw if isinstance(item, dict) and item.get("_id")}

    raise ValueError(f"Unsupported items DB shape in {path}")


def slots_from_items_db(items_db: Dict[str, Any], tpl: str) -> Pool:
    item = items_db.get(tpl)
    if not isinstance(item, dict):
        return {}

    props = item.get("_props", {})
    if not isinstance(props, dict):
        return {}

    slot_entries = props.get("Slots") or props.get("slots") or []
    if not isinstance(slot_entries, list):
        return {}

    pool: Pool = {}
    for slot in slot_entries:
        if not isinstance(slot, dict):
            continue
        name = slot.get("_name") or slot.get("name")
        if not isinstance(name, str) or not name:
            continue
        slot_props = slot.get("_props", {})
        if not isinstance(slot_props, dict):
            slot_props = {}
        filters = slot_props.get("filters") or slot_props.get("Filters") or []
        allowed: List[str] = []
        if isinstance(filters, list):
            for f in filters:
                allowed.extend(extract_filter_list(f))
        pool[name] = dedupe_keep_order(allowed)
    return pool


def collect_tpls_from_any(value: Any, out: Optional[Set[str]] = None) -> Set[str]:
    if out is None:
        out = set()
    if is_tpl(value):
        out.add(value.lower())
    elif isinstance(value, dict):
        for k, v in value.items():
            if is_tpl(k):
                out.add(k.lower())
            collect_tpls_from_any(v, out)
    elif isinstance(value, list):
        for item in value:
            collect_tpls_from_any(item, out)
    return out


def get_inventory_mods(bot_data: Dict[str, Any]) -> Dict[str, Any]:
    inv = bot_data.setdefault("inventory", {})
    if not isinstance(inv, dict):
        raise ValueError("Bot JSON inventory is not an object")
    mods = inv.setdefault("mods", {})
    if not isinstance(mods, dict):
        raise ValueError("Bot JSON inventory.mods is not an object")
    return mods


def build_pool_bank(all_bots: Dict[BotName, Dict[str, Any]]) -> Dict[Tpl, Pool]:
    """First non-empty pool for each tpl, from any supplied bot file."""
    bank: Dict[Tpl, Pool] = {}
    for bot_data in all_bots.values():
        mods = get_inventory_mods(bot_data)
        for tpl, pool in mods.items():
            if not is_tpl(tpl) or not isinstance(pool, dict):
                continue
            if tpl.lower() in bank:
                continue
            # Keep only slot -> list pools.
            clean_pool: Pool = {}
            for slot, values in pool.items():
                if isinstance(slot, str) and isinstance(values, list):
                    clean_pool[slot] = dedupe_keep_order(values)
            if clean_pool:
                bank[tpl.lower()] = clean_pool
    return bank


def parse_log(path: Optional[Path]) -> Dict[str, Any]:
    result = {
        "missing_pools": defaultdict(set),     # bot -> set(tpl)
        "missing_pool_names": {},             # tpl -> last text name
        "bad_slots": defaultdict(lambda: defaultdict(set)),  # bot -> tpl -> set(slot)
        "required_empty": defaultdict(set),    # bot -> set(slot)
        "weapon_fallbacks": set(),             # set(tpl)
        "counts": defaultdict(int),
    }
    if not path:
        return result

    text = path.read_text(encoding="utf-8", errors="replace")
    for line in text.splitlines():
        m = MISSING_POOL_RE.search(line)
        if m:
            bot = norm_bot_name(m.group("bot"))
            tpl = m.group("tpl").lower()
            result["missing_pools"][bot].add(tpl)
            if m.group("name"):
                result["missing_pool_names"][tpl] = m.group("name").strip()
            result["counts"]["missing_pool_lines"] += 1
            continue

        m = BAD_SLOT_RE.search(line)
        if m:
            bot = norm_bot_name(m.group("bot"))
            tpl = m.group("tpl").lower()
            slot = m.group("slot")
            result["bad_slots"][bot][tpl].add(slot)
            result["counts"]["bad_slot_lines"] += 1
            continue

        m = REQUIRED_EMPTY_RE.search(line)
        if m:
            bot = norm_bot_name(m.group("bot"))
            result["required_empty"][bot].add(m.group("slot"))
            result["counts"]["required_empty_lines"] += 1
            continue

        m = WEAPON_FALLBACK_RE.search(line)
        if m:
            result["weapon_fallbacks"].add(m.group("tpl").lower())
            result["counts"]["weapon_fallback_lines"] += 1

    return result


def pool_from_best_source(
    tpl: str,
    bank: Dict[Tpl, Pool],
    items_db: Dict[str, Any],
    empty_fallback: bool,
) -> Optional[Pool]:
    tpl_l = tpl.lower()
    if tpl_l in bank:
        return copy.deepcopy(bank[tpl_l])

    from_db = slots_from_items_db(items_db, tpl_l)
    if from_db:
        return from_db

    if empty_fallback:
        return {}

    return None


def validate_pool_against_db(
    tpl: str,
    pool: Dict[str, Any],
    items_db: Dict[str, Any],
    fill_missing_slots: bool,
    remove_invalid_slots: bool,
) -> Tuple[int, int]:
    """Returns (removed_invalid, added_missing)."""
    if not items_db or not isinstance(pool, dict):
        return 0, 0

    real_slots = slots_from_items_db(items_db, tpl)
    if not real_slots:
        return 0, 0

    removed = 0
    added = 0
    valid_names = set(real_slots.keys())

    if remove_invalid_slots:
        for slot_name in list(pool.keys()):
            if slot_name not in valid_names:
                del pool[slot_name]
                removed += 1

    if fill_missing_slots:
        for slot_name, allowed in real_slots.items():
            current = pool.get(slot_name)
            if current is None:
                pool[slot_name] = copy.deepcopy(allowed)
                added += 1
            elif isinstance(current, list) and not current and allowed:
                pool[slot_name] = copy.deepcopy(allowed)
                added += 1

    return removed, added


def collect_referenced_mod_children(mods: Dict[str, Any]) -> Set[str]:
    out: Set[str] = set()
    for tpl, pool in mods.items():
        if not isinstance(pool, dict):
            continue
        for values in pool.values():
            if isinstance(values, list):
                for value in values:
                    if is_tpl(value):
                        out.add(value.lower())
    return out


def clean_bot(
    bot_name: str,
    bot_data: Dict[str, Any],
    log_info: Dict[str, Any],
    bank: Dict[Tpl, Pool],
    items_db: Dict[str, Any],
    args: argparse.Namespace,
) -> Dict[str, int]:
    mods = get_inventory_mods(bot_data)
    stats: Dict[str, int] = defaultdict(int)

    # 1) Remove exact bad slots reported by the log.
    for tpl, bad_slots in log_info["bad_slots"].get(bot_name, {}).items():
        pool = mods.get(tpl)
        if not isinstance(pool, dict):
            continue
        for slot in sorted(bad_slots):
            if slot in pool:
                del pool[slot]
                stats["removed_log_bad_slots"] += 1

    # 2) Add exact missing pools from the log.
    for tpl in sorted(log_info["missing_pools"].get(bot_name, set())):
        if tpl in mods and isinstance(mods[tpl], dict):
            continue
        new_pool = pool_from_best_source(tpl, bank, items_db, args.empty_fallback)
        if new_pool is not None:
            mods[tpl] = new_pool
            stats["added_log_missing_pools"] += 1
        else:
            stats["skipped_log_missing_pools_no_source"] += 1

    # 3) With items DB, clean every existing pool against real Slots.
    if items_db:
        for tpl, pool in list(mods.items()):
            if not is_tpl(tpl) or not isinstance(pool, dict):
                continue
            removed, added = validate_pool_against_db(
                tpl.lower(),
                pool,
                items_db,
                fill_missing_slots=args.fill_missing_slots,
                remove_invalid_slots=args.remove_invalid_slots,
            )
            stats["removed_db_invalid_slots"] += removed
            stats["added_db_missing_slots"] += added

    # 4) With items DB, add pools for inventory equipment/items and referenced children.
    if items_db and args.add_missing_pools:
        seed_tpls = collect_tpls_from_any(bot_data.get("inventory", {}))
        for _ in range(max(args.recursive_passes, 1)):
            before = len(seed_tpls)
            seed_tpls.update(collect_referenced_mod_children(mods))
            if len(seed_tpls) == before:
                break

        for tpl in sorted(seed_tpls):
            if tpl in mods and isinstance(mods[tpl], dict):
                continue
            real_pool = slots_from_items_db(items_db, tpl)
            if real_pool:
                mods[tpl] = real_pool
                stats["added_db_missing_pools"] += 1

    # 5) Final dedupe/cleanup of every slot list.
    for tpl, pool in list(mods.items()):
        if not isinstance(pool, dict):
            continue
        for slot, values in list(pool.items()):
            if isinstance(values, list):
                new_values = dedupe_keep_order(values)
                if len(new_values) != len(values):
                    stats["deduped_slot_lists"] += 1
                pool[slot] = new_values
            else:
                # Bot mod pools should be slotName: [tpls]. Drop malformed entries.
                del pool[slot]
                stats["removed_malformed_slot_entries"] += 1

    return dict(stats)


def make_backup(path: Path) -> Path:
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    backup = path.with_suffix(path.suffix + f".bak_{stamp}")
    shutil.copy2(path, backup)
    return backup


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Clean SPT bot inventory.mods pools from logs and optional templates/items.json."
    )
    parser.add_argument("--bots", nargs="+", required=True, help="Bot JSON files to clean")
    parser.add_argument("--log", help="Server log / pasted error text file")
    parser.add_argument("--items", help="Optional SPT templates/items.json for real slot validation")
    parser.add_argument("--out-dir", help="Write cleaned files to this folder instead of overwriting")
    parser.add_argument("--write", action="store_true", help="Actually write files. Without this, dry-run only")
    parser.add_argument("--no-backup", action="store_true", help="Do not create .bak files when using --write in-place")

    parser.add_argument("--empty-fallback", action="store_true", help="Create {} pool if no copied/DB source exists")
    parser.add_argument("--add-missing-pools", action="store_true", default=True, help="With --items, add missing pools for inventory and referenced child tpls")
    parser.add_argument("--no-add-missing-pools", dest="add_missing_pools", action="store_false")
    parser.add_argument("--fill-missing-slots", action="store_true", default=True, help="With --items, add missing slot keys from real template filters")
    parser.add_argument("--no-fill-missing-slots", dest="fill_missing_slots", action="store_false")
    parser.add_argument("--remove-invalid-slots", action="store_true", default=True, help="With --items, remove slot keys that do not exist on the item")
    parser.add_argument("--no-remove-invalid-slots", dest="remove_invalid_slots", action="store_false")
    parser.add_argument("--recursive-passes", type=int, default=3, help="How many passes to add pools for referenced mod children")

    args = parser.parse_args()

    bot_paths = [Path(p) for p in args.bots]
    for p in bot_paths:
        if not p.exists():
            print(f"ERROR: bot file not found: {p}", file=sys.stderr)
            return 2

    log_path = Path(args.log) if args.log else None
    if log_path and not log_path.exists():
        print(f"ERROR: log file not found: {log_path}", file=sys.stderr)
        return 2

    items_path = Path(args.items) if args.items else None
    if items_path and not items_path.exists():
        print(f"ERROR: items DB not found: {items_path}", file=sys.stderr)
        return 2

    bot_files: Dict[BotName, Tuple[Path, Dict[str, Any]]] = {}
    for path in bot_paths:
        bot_files[bot_name_from_path(path)] = (path, load_json(path))

    all_bots = {name: data for name, (_, data) in bot_files.items()}
    bank = build_pool_bank(all_bots)
    log_info = parse_log(log_path)
    items_db = load_items_db(items_path)

    print("Medved bot mod-pool cleanup")
    print(f"  bots: {len(bot_files)}")
    print(f"  pool bank entries copied from bot files: {len(bank)}")
    print(f"  log: {log_path if log_path else 'not used'}")
    print(f"  items DB: {items_path if items_path else 'not used'}")
    print(f"  mode: {'WRITE' if args.write else 'DRY RUN'}")
    print()

    if log_path:
        counts = dict(log_info["counts"])
        print("Parsed log:")
        print(f"  missing pool lines: {counts.get('missing_pool_lines', 0)}")
        print(f"  bad slot lines: {counts.get('bad_slot_lines', 0)}")
        print(f"  required empty lines: {counts.get('required_empty_lines', 0)}")
        print(f"  weapon fallback lines: {counts.get('weapon_fallback_lines', 0)}")
        print()

    out_dir = Path(args.out_dir) if args.out_dir else None
    if out_dir and args.write:
        out_dir.mkdir(parents=True, exist_ok=True)

    grand: Dict[str, int] = defaultdict(int)
    outputs: List[Tuple[Path, Path, Dict[str, int]]] = []

    for bot_name, (source_path, data) in bot_files.items():
        stats = clean_bot(bot_name, data, log_info, bank, items_db, args)
        for k, v in stats.items():
            grand[k] += v

        target_path = out_dir / source_path.name if out_dir else source_path
        outputs.append((source_path, target_path, stats))

    for source_path, target_path, stats in outputs:
        print(source_path.name)
        if stats:
            for key in sorted(stats):
                print(f"  {key}: {stats[key]}")
        else:
            print("  no changes")

        if args.write:
            if target_path == source_path and not args.no_backup:
                backup = make_backup(source_path)
                print(f"  backup: {backup.name}")
            write_json(target_path, bot_files[bot_name_from_path(source_path)][1])
            print(f"  wrote: {target_path}")
        else:
            print("  not written; add --write to save")
        print()

    print("Total changes:")
    if grand:
        for key in sorted(grand):
            print(f"  {key}: {grand[key]}")
    else:
        print("  none")

    if not items_db:
        print("\nNOTE: You did not pass --items, so the script only uses log errors and copied pools from the bot files.")
        print("For the best cleanup, pass your SPT templates/items.json so invalid slots can be checked against real item Slots.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
