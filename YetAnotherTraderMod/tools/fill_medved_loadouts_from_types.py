#!/usr/bin/env python3
r"""
fill_medved_loadouts_from_types.py

Builds SPT bot inventory.equipment + inventory.mods from the new Medved type files.

The type files use this friendly shape:

  {
    "equipment": {
      "FirstPrimaryWeapon": {
        "ak103 sokol commander": {
          "id": "5ac66d2e5acfc43b321d4b53",
          "chance": 40,
          "slots": {
            "mod_magazine": ["ak762_mags"],
            "mod_muzzle": ["ak_muzzles_762"]
          }
        }
      }
    },
    "categories": {
      "ak762_mags": {
        "ak103 30rd": { "id": "5ac66bea5acfc43b321d4aec", "chance": 45 }
      }
    }
  }

It outputs SPT-style loadout data. V2 also supports mirroring equipment from a full bot file's inventory.equipment into the generated loadout via --equipment-sources.

It outputs SPT-style loadout data:

  inventory.equipment[slot][tpl] = chance
  inventory.mods[itemTpl][slotName] = [tpl, tpl, ...]

Usage, generate standalone loadout blocks:

  python fill_medved_loadouts_from_types.py ^
    --roles bossMedvedSokol.json followerMedvedBuran.json followerMedvedKedr.json ^
    --libs ak_platforms.json sr3m.json svd.json t5000.json val_vss.json medved_common.json rpd.json shotguns.json ^
    --out-dir generated-loadouts

Usage, patch full existing SPT bot configs by matching file stem:

  python fill_medved_loadouts_from_types.py ^
    --roles types\bossMedvedSokol.json types\followerMedvedBuran.json types\followerMedvedKedr.json ^
    --libs types\ak_platforms.json types\sr3m.json types\svd.json types\t5000.json types\val_vss.json types\medved_common.json types\rpd.json types\shotguns.json ^
    --targets db\bots\types\bossmedvedsokol.json db\bots\types\followermedvedburan.json db\bots\types\followermedvedkedr.json ^
    --out-dir patched-bots

Recommended when you have SPT's templates/items.json:

  python fill_medved_loadouts_from_types.py ^
    --roles bossMedvedSokol.json followerMedvedBuran.json followerMedvedKedr.json ^
    --libs ak_platforms.json sr3m.json svd.json t5000.json val_vss.json medved_common.json rpd.json shotguns.json ^
    --items "C:\RealSPT\SPT_Data\Server\database\templates\items.json" ^
    --fill-missing-db-slots root ^
    --out-dir generated-loadouts

Why --items matters:
  Your type files are easier to read than raw SPT bot JSON, but SPT is strict about
  real item slot names. With --items, this script can remove slots that do not
  exist on an item and can fill missing real root slots from the database.
  It also has a special AK handguard migration: if a type file puts mod_handguard
  directly on an AK root, but the real item uses mod_gas_block -> mod_handguard,
  it moves the handguard pool under compatible gas blocks.
"""

from __future__ import annotations

import argparse
import copy
import json
import re
import shutil
import sys
from collections import OrderedDict, defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Set, Tuple

TPL_RE = re.compile(r"^[0-9a-f]{24}$", re.IGNORECASE)

JsonObj = Dict[str, Any]
Pool = Dict[str, List[str]]
Mods = Dict[str, Pool]
Equipment = Dict[str, Dict[str, int]]

WEAPON_EQUIPMENT_SLOTS = {"FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster"}


def get_inventory_equipment(data: JsonObj) -> Optional[Equipment]:
    inv = data.get("inventory") if isinstance(data, dict) else None
    if not isinstance(inv, dict):
        return None
    equipment = inv.get("equipment")
    if not isinstance(equipment, dict):
        return None
    return equipment


def get_inventory_mods(data: JsonObj) -> Optional[Mods]:
    inv = data.get("inventory") if isinstance(data, dict) else None
    if not isinstance(inv, dict):
        return None
    mods = inv.get("mods")
    if not isinstance(mods, dict):
        return None
    return mods


def normalize_equipment_pool(pool: Any) -> Dict[str, int]:
    out: Dict[str, int] = OrderedDict()
    if not isinstance(pool, dict):
        return out
    for tpl, weight in pool.items():
        if not is_tpl(tpl):
            continue
        try:
            w = int(weight)
        except Exception:
            w = 1
        out[tpl.lower()] = w
    return out


def collect_equipment_item_ids(equipment: Any, include_weapon_slots: bool = True) -> Set[str]:
    ids: Set[str] = set()
    if not isinstance(equipment, dict):
        return ids
    for equip_slot, pool in equipment.items():
        if not include_weapon_slots and equip_slot in WEAPON_EQUIPMENT_SLOTS:
            continue
        if not isinstance(pool, dict):
            continue
        for tpl in pool.keys():
            if is_tpl(tpl):
                ids.add(tpl.lower())
    return ids


def merge_mod_pool(dst_mods: Mods, item_id: str, source_pool: Any) -> None:
    if not is_tpl(item_id) or not isinstance(source_pool, dict):
        return
    dst_pool = dst_mods.setdefault(item_id.lower(), OrderedDict())
    for slot_name, values in source_pool.items():
        if not isinstance(slot_name, str) or not slot_name:
            continue
        if isinstance(values, list):
            clean_values = [v.lower() if is_tpl(v) else v for v in values if isinstance(v, str) and v]
        elif isinstance(values, str):
            clean_values = [values.lower() if is_tpl(values) else values]
        else:
            continue
        if not clean_values:
            continue
        merge_unique(dst_pool.setdefault(slot_name, []), clean_values)


def apply_equipment_source(
    loadout: Dict[str, Any],
    source_data: JsonObj,
    root_item_ids: Set[str],
    source_mods_mode: str,
) -> Tuple[Set[str], List[str]]:
    """Replace generated inventory.equipment with a full bot's inventory.equipment.

    source_mods_mode:
      none = copy only inventory.equipment
      gear = also copy source inventory.mods for non-weapon equipment items
      all  = also copy source inventory.mods for all mirrored equipment item ids
    """
    warnings: List[str] = []
    source_equipment = get_inventory_equipment(source_data)
    if source_equipment is None:
        warnings.append("Requested equipment source has no inventory.equipment object; skipped equipment mirror.")
        return root_item_ids, warnings

    mirrored: Equipment = OrderedDict()
    touched_slots: Set[str] = set()
    for equip_slot, pool in source_equipment.items():
        if not isinstance(equip_slot, str):
            continue
        mirrored[equip_slot] = normalize_equipment_pool(pool)
        touched_slots.add(equip_slot)

    inv = loadout.setdefault("inventory", {})
    inv["equipment"] = mirrored

    # Root ids used by items DB validation/fill must match the mirrored equipment now.
    root_item_ids.clear()
    root_item_ids.update(collect_equipment_item_ids(mirrored, include_weapon_slots=True))

    if source_mods_mode != "none":
        source_mods = get_inventory_mods(source_data)
        if source_mods is None:
            warnings.append("Equipment source had no inventory.mods object to copy from.")
        else:
            include_weapons = source_mods_mode == "all"
            ids_to_copy = collect_equipment_item_ids(mirrored, include_weapon_slots=include_weapons)
            dst_mods = inv.setdefault("mods", OrderedDict())
            copied = 0
            for item_id in sorted(ids_to_copy):
                pool = source_mods.get(item_id) or source_mods.get(item_id.lower()) or source_mods.get(item_id.upper())
                if isinstance(pool, dict):
                    before = len(dst_mods)
                    merge_mod_pool(dst_mods, item_id, pool)
                    copied += 1
            warnings.append(f"Mirrored equipment source pools: {len(root_item_ids)} equipment ids; copied source mod pools for {copied} item(s).")

    return touched_slots, warnings


def find_source_for_role(role_path: Path, sources: List[Path]) -> Optional[Path]:
    role_key = clean_bot_key(role_path.name)
    for source in sources:
        if clean_bot_key(source.name) == role_key:
            return source
    return None


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=4, ensure_ascii=False)
        f.write("\n")


def backup_file(path: Path) -> Path:
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = path.with_suffix(path.suffix + f".{stamp}.bak")
    shutil.copy2(path, backup)
    return backup


def is_tpl(value: Any) -> bool:
    return isinstance(value, str) and bool(TPL_RE.fullmatch(value))


def clean_bot_key(path_or_name: str) -> str:
    stem = Path(path_or_name).stem.lower()
    return re.sub(r"[^a-z0-9]", "", stem)


def dedupe_keep_order(values: Iterable[str]) -> List[str]:
    seen: Set[str] = set()
    out: List[str] = []
    for value in values:
        if not isinstance(value, str) or not value:
            continue
        if value in seen:
            continue
        seen.add(value)
        out.append(value)
    return out


def merge_unique(dst: List[str], src: Iterable[str]) -> List[str]:
    seen = set(dst)
    for value in src:
        if not isinstance(value, str) or not value:
            continue
        if value in seen:
            continue
        dst.append(value)
        seen.add(value)
    return dst


def as_slot_refs(value: Any) -> List[str]:
    if isinstance(value, str):
        return [value]
    if isinstance(value, list):
        return [x for x in value if isinstance(x, str)]
    return []


def extract_filter_list(filter_obj: Any) -> List[str]:
    if not isinstance(filter_obj, dict):
        return []
    values = filter_obj.get("Filter")
    if values is None:
        values = filter_obj.get("filter")
    if isinstance(values, list):
        return [x for x in values if isinstance(x, str)]
    return []


@dataclass
class DbSlot:
    allowed: List[str] = field(default_factory=list)
    required: bool = False


def load_items_db(path: Optional[Path]) -> Dict[str, Any]:
    if not path:
        return {}
    raw = load_json(path)

    if isinstance(raw, dict):
        if "items" in raw and isinstance(raw["items"], dict):
            return raw["items"]
        return raw

    if isinstance(raw, list):
        return {item.get("_id"): item for item in raw if isinstance(item, dict) and item.get("_id")}

    raise ValueError(f"Unsupported items DB shape in {path}")


def db_slots_for_item(items_db: Dict[str, Any], tpl: str) -> Dict[str, DbSlot]:
    item = items_db.get(tpl)
    if not isinstance(item, dict):
        return {}

    props = item.get("_props", {})
    if not isinstance(props, dict):
        props = {}

    slot_entries = props.get("Slots") or props.get("slots") or []
    if not isinstance(slot_entries, list):
        return {}

    out: Dict[str, DbSlot] = OrderedDict()
    for slot in slot_entries:
        if not isinstance(slot, dict):
            continue
        name = slot.get("_name") or slot.get("name")
        if not isinstance(name, str) or not name:
            continue

        slot_props = slot.get("_props") if isinstance(slot.get("_props"), dict) else {}
        filters = slot_props.get("filters") or slot_props.get("Filters") or []
        allowed: List[str] = []
        if isinstance(filters, list):
            for f in filters:
                allowed.extend(extract_filter_list(f))

        required = bool(
            slot.get("_required")
            or slot.get("required")
            or slot_props.get("required")
            or slot_props.get("Required")
        )
        out[name] = DbSlot(allowed=dedupe_keep_order(allowed), required=required)

    return out


@dataclass
class TypeDb:
    categories: Dict[str, Dict[str, JsonObj]] = field(default_factory=dict)
    category_sources: Dict[str, str] = field(default_factory=dict)
    item_defs_by_id: Dict[str, List[Tuple[str, JsonObj]]] = field(default_factory=lambda: defaultdict(list))
    warnings: List[str] = field(default_factory=list)

    def add_file(self, path: Path, data: JsonObj) -> None:
        source = path.name

        categories = data.get("categories", {})
        if isinstance(categories, dict):
            for cat_name, cat_items in categories.items():
                if not isinstance(cat_name, str) or not isinstance(cat_items, dict):
                    continue
                if cat_name in self.categories:
                    self.warnings.append(
                        f"Category {cat_name!r} from {source} overwrote earlier category from {self.category_sources.get(cat_name, 'unknown')}."
                    )
                self.categories[cat_name] = cat_items
                self.category_sources[cat_name] = source
                for display_name, item in cat_items.items():
                    if isinstance(item, dict) and is_tpl(item.get("id")):
                        self.item_defs_by_id[item["id"].lower()].append((f"{source}:{cat_name}:{display_name}", item))
                    elif isinstance(item, dict):
                        self.warnings.append(
                            f"Category {cat_name!r} item {display_name!r} in {source} has no valid id and will be skipped when referenced."
                        )

        equipment = data.get("equipment", {})
        if isinstance(equipment, dict):
            for equip_slot, entries in equipment.items():
                if not isinstance(entries, dict):
                    continue
                for display_name, item in entries.items():
                    if isinstance(item, dict) and is_tpl(item.get("id")):
                        self.item_defs_by_id[item["id"].lower()].append((f"{source}:equipment:{equip_slot}:{display_name}", item))
                    elif isinstance(item, dict):
                        self.warnings.append(
                            f"Equipment slot {equip_slot!r} item {display_name!r} in {source} has no valid id and will be skipped."
                        )

    def resolve_refs(
        self,
        refs: Iterable[str],
        mods: Mods,
        stack: Tuple[str, ...] = (),
        context: str = "",
    ) -> List[str]:
        out: List[str] = []
        for ref in refs:
            if is_tpl(ref):
                out.append(ref.lower())
                # If this raw TPL also has a type definition somewhere, add its child pools.
                for _, item in self.item_defs_by_id.get(ref.lower(), []):
                    self.add_item_slots(item, mods, stack=stack, context=f"raw tpl {ref} from {context}")
                continue

            cat = self.categories.get(ref)
            if cat is None:
                self.warnings.append(f"Unknown category/reference {ref!r} used in {context}; skipped.")
                continue

            if ref in stack:
                self.warnings.append(f"Category cycle detected: {' -> '.join(stack + (ref,))}; skipped.")
                continue

            for display_name, item in cat.items():
                if not isinstance(item, dict):
                    continue
                item_id = item.get("id")
                if not is_tpl(item_id):
                    self.warnings.append(f"Category {ref!r} item {display_name!r} has no valid id; skipped.")
                    continue
                item_id = item_id.lower()
                out.append(item_id)
                self.add_item_slots(item, mods, stack=stack + (ref,), context=f"{ref}.{display_name}")

        return dedupe_keep_order(out)

    def add_item_slots(
        self,
        item: JsonObj,
        mods: Mods,
        stack: Tuple[str, ...] = (),
        context: str = "",
    ) -> None:
        item_id = item.get("id")
        if not is_tpl(item_id):
            return
        item_id = item_id.lower()

        slots = item.get("slots", {})
        if not isinstance(slots, dict) or not slots:
            return

        pool = mods.setdefault(item_id, OrderedDict())
        for slot_name, refs_value in slots.items():
            if not isinstance(slot_name, str) or not slot_name:
                continue
            refs = as_slot_refs(refs_value)
            ids = self.resolve_refs(refs, mods, stack=stack, context=f"{context}:{item_id}:{slot_name}")
            if not ids:
                self.warnings.append(f"Slot {slot_name!r} on {item_id} from {context} resolved to no items.")
                continue
            current = pool.setdefault(slot_name, [])
            merge_unique(current, ids)


def build_loadout(role_path: Path, role_data: JsonObj, db: TypeDb) -> Tuple[Dict[str, Any], List[str], Set[str], Set[str]]:
    warnings_before = len(db.warnings)
    equipment_out: Equipment = OrderedDict()
    mods: Mods = OrderedDict()
    root_item_ids: Set[str] = set()
    touched_equipment_slots: Set[str] = set()

    equipment = role_data.get("equipment", {})
    direct_inventory_equipment = False
    if not isinstance(equipment, dict) or not equipment:
        inv_equipment = get_inventory_equipment(role_data)
        if isinstance(inv_equipment, dict):
            equipment = inv_equipment
            direct_inventory_equipment = True
        else:
            raise ValueError(f"{role_path} has no object top-level 'equipment' and no inventory.equipment.")

    if direct_inventory_equipment:
        for equip_slot, pool in equipment.items():
            if not isinstance(equip_slot, str):
                continue
            slot_out = normalize_equipment_pool(pool)
            touched_equipment_slots.add(equip_slot)
            equipment_out[equip_slot] = slot_out
            root_item_ids.update(slot_out.keys())

        # If any mirrored item has a friendly type definition in libs, use it to build child mod pools.
        for item_id in sorted(root_item_ids):
            for source_name, item_def in db.item_defs_by_id.get(item_id.lower(), []):
                db.add_item_slots(item_def, mods, context=f"{role_path.name}:inventory.equipment:{source_name}")

        for item_id, pool in list(mods.items()):
            for slot_name, values in list(pool.items()):
                clean = dedupe_keep_order(values)
                if clean:
                    pool[slot_name] = clean
                else:
                    del pool[slot_name]
            if not pool:
                del mods[item_id]

        warnings = db.warnings[warnings_before:]
        return {"inventory": {"equipment": equipment_out, "mods": mods}}, warnings, root_item_ids, touched_equipment_slots

    for equip_slot, entries in equipment.items():
        if not isinstance(equip_slot, str) or not isinstance(entries, dict):
            continue
        slot_out: Dict[str, int] = OrderedDict()
        touched_equipment_slots.add(equip_slot)
        for display_name, item in entries.items():
            if not isinstance(item, dict):
                continue
            item_id = item.get("id")
            if not is_tpl(item_id):
                db.warnings.append(f"{role_path.name}:{equip_slot}:{display_name} has no valid id; skipped.")
                continue
            item_id = item_id.lower()
            chance = item.get("chance", 1)
            if not isinstance(chance, int):
                try:
                    chance = int(chance)
                except Exception:
                    chance = 1
            slot_out[item_id] = chance
            root_item_ids.add(item_id)
            db.add_item_slots(item, mods, context=f"{role_path.name}:equipment:{equip_slot}:{display_name}")
        equipment_out[equip_slot] = slot_out

    # Clean slot value arrays after all recursive merges.
    for item_id, pool in list(mods.items()):
        for slot_name, values in list(pool.items()):
            clean = dedupe_keep_order(values)
            if clean:
                pool[slot_name] = clean
            else:
                del pool[slot_name]
        if not pool:
            del mods[item_id]

    warnings = db.warnings[warnings_before:]
    return {"inventory": {"equipment": equipment_out, "mods": mods}}, warnings, root_item_ids, touched_equipment_slots


def validate_and_fix_with_items_db(
    loadout: Dict[str, Any],
    items_db: Dict[str, Any],
    root_item_ids: Set[str],
    fill_missing_db_slots: str,
) -> List[str]:
    warnings: List[str] = []
    if not items_db:
        return warnings

    mods: Mods = loadout.setdefault("inventory", {}).setdefault("mods", {})

    changed = True
    passes = 0
    while changed and passes < 8:
        changed = False
        passes += 1

        for item_id in list(mods.keys()):
            if not is_tpl(item_id):
                continue
            actual = db_slots_for_item(items_db, item_id)
            if not actual:
                warnings.append(f"No item DB entry/slots found for {item_id}; could not validate its mod pool.")
                continue

            pool = mods.get(item_id, {})
            if not isinstance(pool, dict):
                continue

            # Remove or migrate slots that do not actually exist on the item.
            for slot_name in list(pool.keys()):
                if slot_name in actual:
                    continue

                # Common EFT AK layout: handguards hang from the gas block, not the weapon root.
                if slot_name == "mod_handguard" and "mod_gas_block" in actual:
                    handguard_ids = dedupe_keep_order(pool.pop(slot_name))
                    gas_block_ids = actual["mod_gas_block"].allowed
                    if gas_block_ids:
                        merge_unique(pool.setdefault("mod_gas_block", []), gas_block_ids)
                        warnings.append(
                            f"Migrated {item_id}.mod_handguard under compatible mod_gas_block items from items DB."
                        )
                        for gas_id in gas_block_ids:
                            gas_slots = db_slots_for_item(items_db, gas_id)
                            if "mod_handguard" not in gas_slots:
                                continue
                            allowed_handguards = set(gas_slots["mod_handguard"].allowed)
                            compatible = [x for x in handguard_ids if not allowed_handguards or x in allowed_handguards]
                            if not compatible:
                                compatible = handguard_ids
                            merge_unique(mods.setdefault(gas_id.lower(), {}).setdefault("mod_handguard", []), compatible)
                        changed = True
                    else:
                        warnings.append(f"Removed {item_id}.mod_handguard because items DB has mod_gas_block but no allowed gas blocks.")
                    continue

                removed = pool.pop(slot_name, None)
                warnings.append(f"Removed invalid slot {item_id}.{slot_name}; not present in items DB.")
                if removed is not None:
                    changed = True

            # Optionally fill missing actual DB slots.
            if fill_missing_db_slots != "none":
                should_fill_root = fill_missing_db_slots in {"root", "all"} and item_id in root_item_ids
                should_fill_all = fill_missing_db_slots == "all"
                should_fill_required = fill_missing_db_slots == "required"

                for slot_name, db_slot in actual.items():
                    if slot_name in pool:
                        continue
                    if not db_slot.allowed:
                        continue
                    do_fill = should_fill_all or should_fill_root or (should_fill_required and db_slot.required)
                    if not do_fill:
                        continue
                    pool[slot_name] = list(db_slot.allowed)
                    warnings.append(f"Filled missing {item_id}.{slot_name} from items DB.")
                    changed = True

            # Clean arrays after changes.
            for slot_name, values in list(pool.items()):
                if not isinstance(values, list):
                    del pool[slot_name]
                    changed = True
                    continue
                clean = dedupe_keep_order([x.lower() if is_tpl(x) else x for x in values])
                if clean != values:
                    pool[slot_name] = clean
                    changed = True
                if not clean:
                    del pool[slot_name]
                    changed = True

    if passes >= 8:
        warnings.append("Stopped recursive items DB validation after 8 passes to avoid a possible cycle.")

    return warnings


def patch_target_bot(
    target_data: JsonObj,
    generated_loadout: Dict[str, Any],
    touched_equipment_slots: Set[str],
    mode: str,
) -> JsonObj:
    out = copy.deepcopy(target_data)
    inv = out.setdefault("inventory", {})
    if not isinstance(inv, dict):
        raise ValueError("Target bot inventory is not an object")

    gen_inv = generated_loadout.get("inventory", {})
    gen_equipment = gen_inv.get("equipment", {})
    gen_mods = gen_inv.get("mods", {})

    if mode == "replace":
        equip = inv.setdefault("equipment", {})
        if not isinstance(equip, dict):
            equip = {}
            inv["equipment"] = equip
        for slot in touched_equipment_slots:
            equip[slot] = copy.deepcopy(gen_equipment.get(slot, {}))
        inv["mods"] = copy.deepcopy(gen_mods)
        return out

    if mode == "merge":
        equip = inv.setdefault("equipment", {})
        if not isinstance(equip, dict):
            equip = {}
            inv["equipment"] = equip
        for slot, pool in gen_equipment.items():
            dest = equip.setdefault(slot, {})
            if not isinstance(dest, dict):
                dest = {}
                equip[slot] = dest
            dest.update(copy.deepcopy(pool))

        mods = inv.setdefault("mods", {})
        if not isinstance(mods, dict):
            mods = {}
            inv["mods"] = mods
        for item_id, pool in gen_mods.items():
            dest_pool = mods.setdefault(item_id, {})
            if not isinstance(dest_pool, dict):
                dest_pool = {}
                mods[item_id] = dest_pool
            for slot, values in pool.items():
                dest_values = dest_pool.setdefault(slot, [])
                if not isinstance(dest_values, list):
                    dest_values = []
                    dest_pool[slot] = dest_values
                merge_unique(dest_values, values)
        return out

    raise ValueError(f"Unsupported patch mode: {mode}")


def find_target_for_role(role_path: Path, targets: List[Path]) -> Optional[Path]:
    role_key = clean_bot_key(role_path.name)
    for target in targets:
        if clean_bot_key(target.name) == role_key:
            return target
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate SPT Medved bot loadouts from type/category JSON files.")
    parser.add_argument("--roles", nargs="+", required=True, type=Path, help="Role type files with top-level equipment, e.g. bossMedvedSokol.json.")
    parser.add_argument("--libs", nargs="*", default=[], type=Path, help="Shared type/category files, e.g. ak_platforms.json medved_common.json.")
    parser.add_argument("--targets", nargs="*", default=[], type=Path, help="Optional full SPT bot JSONs to patch. Matched to roles by filename stem.")
    parser.add_argument("--equipment-sources", nargs="*", default=[], type=Path, help="Optional full SPT bot JSONs. Their inventory.equipment replaces the generated loadout equipment, matched by filename stem.")
    parser.add_argument(
        "--source-mods",
        choices=["none", "gear", "all"],
        default="gear",
        help="When using --equipment-sources or a full bot as a role, copy source inventory.mods: none, gear/non-weapon equipment only, or all mirrored equipment including weapons. Default: gear.",
    )
    parser.add_argument("--out-dir", type=Path, default=Path("generated-medved-loadouts"), help="Output directory.")
    parser.add_argument("--items", type=Path, default=None, help="Optional SPT templates/items.json for real slot validation/fill.")
    parser.add_argument(
        "--fill-missing-db-slots",
        choices=["none", "required", "root", "all"],
        default="none",
        help="With --items, fill missing slots from DB. root is recommended for weapon roots. Default: none.",
    )
    parser.add_argument("--patch-mode", choices=["replace", "merge"], default="replace", help="How to patch target bot inventories. Default: replace generated equipment slots and inventory.mods.")
    parser.add_argument("--write", action="store_true", help="Patch target files in-place instead of writing to --out-dir. Backups are created.")
    parser.add_argument("--report", action="store_true", help="Write a .report.txt beside every output JSON.")
    args = parser.parse_args()

    for path in args.roles + args.libs + args.targets + args.equipment_sources:
        if not path.exists():
            raise FileNotFoundError(path)

    type_db = TypeDb()
    # Load libraries first, then roles. Role-local category definitions can override libs if desired.
    for path in args.libs + args.roles:
        data = load_json(path)
        if not isinstance(data, dict):
            raise ValueError(f"{path} does not contain a JSON object")
        type_db.add_file(path, data)

    items_db = load_items_db(args.items) if args.items else {}

    global_warnings = list(type_db.warnings)
    args.out_dir.mkdir(parents=True, exist_ok=True)

    made = 0
    for role_path in args.roles:
        role_data = load_json(role_path)
        if not isinstance(role_data, dict):
            raise ValueError(f"{role_path} does not contain a JSON object")

        loadout, build_warnings, root_item_ids, touched_slots = build_loadout(role_path, role_data, type_db)

        source_warnings: List[str] = []
        source_path = find_source_for_role(role_path, args.equipment_sources)
        source_data: Optional[JsonObj] = None
        if source_path:
            source_data = load_json(source_path)
            if not isinstance(source_data, dict):
                raise ValueError(f"{source_path} does not contain a JSON object")
        elif get_inventory_equipment(role_data) is not None:
            # The role itself is a full SPT bot file. Use its own equipment/mod pools as the source.
            source_data = role_data

        if source_data is not None:
            touched_slots, source_warnings = apply_equipment_source(loadout, source_data, root_item_ids, args.source_mods)

        db_warnings = validate_and_fix_with_items_db(loadout, items_db, root_item_ids, args.fill_missing_db_slots)

        target_path = find_target_for_role(role_path, args.targets)
        if target_path:
            target_data = load_json(target_path)
            if not isinstance(target_data, dict):
                raise ValueError(f"{target_path} does not contain a JSON object")
            output_data = patch_target_bot(target_data, loadout, touched_slots, args.patch_mode)
            output_name = target_path.name
        else:
            output_data = loadout
            output_name = f"{role_path.stem}.loadout.json"

        if args.write:
            if not target_path:
                raise ValueError("--write requires --targets so the script knows which full bot files to patch in-place.")
            backup = backup_file(target_path)
            write_json(target_path, output_data)
            output_path = target_path
            print(f"patched {target_path}  backup={backup.name}")
        else:
            output_path = args.out_dir / output_name
            write_json(output_path, output_data)
            print(f"wrote {output_path}")

        warnings = global_warnings + build_warnings + source_warnings + db_warnings
        if args.report or warnings:
            report_path = (output_path.with_suffix(output_path.suffix + ".report.txt") if args.write else args.out_dir / f"{output_name}.report.txt")
            lines = [
                f"Role: {role_path}",
                f"Target: {target_path or '(standalone loadout)'}",
                f"Equipment slots: {', '.join(sorted(touched_slots))}",
                f"Root items: {len(root_item_ids)}",
                f"Generated mod pools: {len(loadout.get('inventory', {}).get('mods', {}))}",
                "",
                "Warnings:",
            ]
            if warnings:
                lines.extend(f"- {w}" for w in warnings)
            else:
                lines.append("- none")
            report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        made += 1

    print(f"done: generated {made} loadout(s)")
    if global_warnings:
        print(f"note: {len(global_warnings)} type warning(s). See report files.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1)
