#!/usr/bin/env python3

import argparse
import json
import time
from pathlib import Path
from typing import Any

import requests


API_URL = "https://api.tarkov.dev/graphql"

QUERY = """
query Items($ids: [ID]) {
  items(ids: $ids, lang: en) {
    id
    name
    shortName
    properties {
      __typename

      ... on ItemPropertiesWeapon {
        slots { ...SlotFields }
      }

      ... on ItemPropertiesWeaponMod {
        slots { ...SlotFields }
      }

      ... on ItemPropertiesHelmet {
        slots { ...SlotFields }
        armorSlots { ...ArmorSlotFields }
      }

      ... on ItemPropertiesHeadwear {
        slots { ...SlotFields }
      }

      ... on ItemPropertiesArmorAttachment {
        slots { ...SlotFields }
      }

      ... on ItemPropertiesArmor {
        armorSlots { ...ArmorSlotFields }
      }

      ... on ItemPropertiesChestRig {
        armorSlots { ...ArmorSlotFields }
      }

      ... on ItemPropertiesBarrel {
        slots { ...SlotFields }
      }

      ... on ItemPropertiesMagazine {
        slots { ...SlotFields }
      }

      ... on ItemPropertiesScope {
        slots { ...SlotFields }
      }
    }
  }
}

fragment SlotFields on ItemSlot {
  name
  nameId
  filters {
    allowedItems {
      id
    }
    allowedCategories {
      id
    }
  }
}

fragment ArmorSlotFields on ItemArmorSlot {
  nameId

  ... on ItemArmorSlotOpen {
    allowedPlates {
      id
    }
  }
}
"""


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as file:
        return json.load(file)


def save_json(path: Path, data: Any) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as file:
        json.dump(data, file, indent=4, ensure_ascii=False)
        file.write("\n")


def chunked(values: list[str], size: int) -> list[list[str]]:
    return [values[index:index + size] for index in range(0, len(values), size)]


def normalize_slot_name(value: str | None) -> str | None:
    if not value:
        return None

    # tarkov.dev usually gives the SPT/Bsg slot name in nameId.
    # If it ever gives a display name instead, keep it JSON-safe.
    return value.strip().replace(" ", "_")


def get_mods_objects(data: Any, bot_name: str | None, all_bots: bool) -> list[tuple[str, dict[str, Any]]]:
    """
    Supports:
      1. A single bot file:
         { "inventory": { "mods": {} } }

      2. A direct mods file:
         { "5b40e1525acfc4771e1c6611": {} }

      3. A bots.types style file:
         { "bossmedvedsokol": { "inventory": { "mods": {} } } }

      4. A wrapper:
         { "types": { "bossmedvedsokol": { "inventory": { "mods": {} } } } }
    """

    if bot_name:
        if isinstance(data, dict) and bot_name in data:
            bot = data[bot_name]
            return [(bot_name, bot["inventory"]["mods"])]

        if isinstance(data, dict) and "types" in data and bot_name in data["types"]:
            bot = data["types"][bot_name]
            return [(bot_name, bot["inventory"]["mods"])]

        raise KeyError(f"Could not find bot '{bot_name}' in input JSON.")

    if isinstance(data, dict) and "inventory" in data and "mods" in data["inventory"]:
        return [("bot", data["inventory"]["mods"])]

    if isinstance(data, dict) and "mods" in data and isinstance(data["mods"], dict):
        return [("mods", data["mods"])]

    if all_bots:
        results: list[tuple[str, dict[str, Any]]] = []

        source = data.get("types", data) if isinstance(data, dict) else {}

        for key, value in source.items():
            if not isinstance(value, dict):
                continue

            inventory = value.get("inventory")
            if not isinstance(inventory, dict):
                continue

            mods = inventory.get("mods")
            if isinstance(mods, dict):
                results.append((key, mods))

        if results:
            return results

    # Last fallback: assume the input itself is inventory.mods.
    if isinstance(data, dict) and all(isinstance(key, str) for key in data.keys()):
        return [("mods", data)]

    raise ValueError("Could not find inventory.mods. Use --bot or --all-bots if this is a bots/types file.")


def load_cache(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}

    try:
        return load_json(path)
    except Exception:
        return {}


def fetch_items(ids: list[str], cache: dict[str, Any], batch_size: int, sleep_seconds: float) -> dict[str, Any]:
    missing_ids = [item_id for item_id in ids if item_id not in cache]

    if not missing_ids:
        return cache

    session = requests.Session()

    for batch in chunked(missing_ids, batch_size):
        response = session.post(
            API_URL,
            json={
                "query": QUERY,
                "variables": {
                    "ids": batch
                }
            },
            timeout=45
        )

        response.raise_for_status()
        payload = response.json()

        if "errors" in payload:
            raise RuntimeError(json.dumps(payload["errors"], indent=2))

        items = payload.get("data", {}).get("items", [])

        found_ids = set()

        for item in items:
            if not item:
                continue

            item_id = item["id"]
            found_ids.add(item_id)
            cache[item_id] = item

        for item_id in batch:
            if item_id not in found_ids:
                cache[item_id] = None

        if sleep_seconds > 0:
            time.sleep(sleep_seconds)

    return cache


def ids_from_slot(slot: dict[str, Any], include_category_ids: bool) -> list[str]:
    filters = slot.get("filters") or {}

    ids: list[str] = []

    for item in filters.get("allowedItems") or []:
        item_id = item.get("id")
        if item_id:
            ids.append(item_id)

    if include_category_ids:
        for category in filters.get("allowedCategories") or []:
            category_id = category.get("id")
            if category_id:
                ids.append(category_id)

    return dedupe(ids)


def ids_from_armor_slot(slot: dict[str, Any]) -> list[str]:
    ids: list[str] = []

    for item in slot.get("allowedPlates") or []:
        item_id = item.get("id")
        if item_id:
            ids.append(item_id)

    return dedupe(ids)


def dedupe(values: list[str]) -> list[str]:
    seen = set()
    output: list[str] = []

    for value in values:
        if value in seen:
            continue

        seen.add(value)
        output.append(value)

    return output


def build_mod_pool(item: dict[str, Any] | None, include_category_ids: bool) -> dict[str, list[str]]:
    if not item:
        return {}

    properties = item.get("properties") or {}

    output: dict[str, list[str]] = {}

    for slot in properties.get("slots") or []:
        slot_name = normalize_slot_name(slot.get("nameId") or slot.get("name"))

        if not slot_name:
            continue

        allowed_ids = ids_from_slot(slot, include_category_ids)

        if allowed_ids:
            output[slot_name] = allowed_ids

    for armor_slot in properties.get("armorSlots") or []:
        slot_name = normalize_slot_name(armor_slot.get("nameId"))

        if not slot_name:
            continue

        allowed_ids = ids_from_armor_slot(armor_slot)

        if allowed_ids:
            output[slot_name] = allowed_ids

    return output


def merge_pool(
    current_pool: Any,
    generated_pool: dict[str, list[str]],
    overwrite_item: bool,
    overwrite_slots: bool
) -> dict[str, Any]:
    if overwrite_item or not isinstance(current_pool, dict) or len(current_pool) == 0:
        return generated_pool

    for slot_name, allowed_ids in generated_pool.items():
        if overwrite_slots or slot_name not in current_pool or not current_pool[slot_name]:
            current_pool[slot_name] = allowed_ids

    return current_pool


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Fill SPT bot inventory.mods pools from tarkov.dev item slot filters."
    )

    parser.add_argument(
        "--input",
        required=True,
        help="Input bot JSON file."
    )

    parser.add_argument(
        "--output",
        help="Output JSON file. If omitted, overwrites the input file only when --in-place is used."
    )

    parser.add_argument(
        "--bot",
        help="Bot key to update, for example bossmedvedsokol or followermedvedkedr."
    )

    parser.add_argument(
        "--all-bots",
        action="store_true",
        help="Update every object in the file that has inventory.mods."
    )

    parser.add_argument(
        "--in-place",
        action="store_true",
        help="Overwrite the input file."
    )

    parser.add_argument(
        "--overwrite-items",
        action="store_true",
        help="Replace each whole item mod pool instead of only filling empty/missing pools."
    )

    parser.add_argument(
        "--overwrite-slots",
        action="store_true",
        help="Replace existing slot arrays inside non-empty item pools."
    )

    parser.add_argument(
        "--include-category-ids",
        action="store_true",
        help="Also include allowed category IDs from tarkov.dev filters."
    )

    parser.add_argument(
        "--cache",
        default=".tarkovdev_slot_cache.json",
        help="Cache file to avoid re-querying tarkov.dev."
    )

    parser.add_argument(
        "--batch-size",
        type=int,
        default=50,
        help="How many IDs to query at once."
    )

    parser.add_argument(
        "--sleep",
        type=float,
        default=0.15,
        help="Seconds to sleep between API batches."
    )

    args = parser.parse_args()

    input_path = Path(args.input)
    output_path = Path(args.output) if args.output else input_path
    cache_path = Path(args.cache)

    if not args.in_place and not args.output:
        raise SystemExit("Use --output or --in-place.")

    data = load_json(input_path)
    mods_objects = get_mods_objects(data, args.bot, args.all_bots)

    all_item_ids = sorted({
        item_id
        for _, mods in mods_objects
        for item_id in mods.keys()
        if isinstance(item_id, str) and len(item_id) == 24
    })

    cache = load_cache(cache_path)
    cache = fetch_items(all_item_ids, cache, args.batch_size, args.sleep)
    save_json(cache_path, cache)

    changed = 0
    skipped_no_item = 0
    skipped_no_slots = 0

    for bot_label, mods in mods_objects:
        for item_id in list(mods.keys()):
            if not isinstance(item_id, str) or len(item_id) != 24:
                continue

            item = cache.get(item_id)

            if item is None:
                skipped_no_item += 1
                print(f"[MISS] {bot_label}: tarkov.dev does not know item {item_id}")
                continue

            generated_pool = build_mod_pool(item, args.include_category_ids)

            if not generated_pool:
                skipped_no_slots += 1
                print(f"[SKIP] {bot_label}: {item_id} {item.get('shortName') or item.get('name')} has no usable slots")
                continue

            before = json.dumps(mods.get(item_id), sort_keys=True)

            mods[item_id] = merge_pool(
                mods.get(item_id),
                generated_pool,
                args.overwrite_items,
                args.overwrite_slots
            )

            after = json.dumps(mods.get(item_id), sort_keys=True)

            if before != after:
                changed += 1
                print(f"[ OK ] {bot_label}: filled {item_id} {item.get('shortName') or item.get('name')}")

    save_json(output_path, data)

    print()
    print(f"Updated file: {output_path}")
    print(f"Changed item pools: {changed}")
    print(f"Missing on tarkov.dev: {skipped_no_item}")
    print(f"No usable slots: {skipped_no_slots}")


if __name__ == "__main__":
    main()