import json
import re
from pathlib import Path
from copy import deepcopy

ITEM_ID_RE = re.compile(r"^[0-9a-fA-F]{24}$")

ROLES = [
    "bossMedvedSokol",
    "followerMedvedBuran",
    "followerMedvedKedr",
]

BOT_DIR = Path("db/bots")
LOADOUT_DIR = BOT_DIR / "loadouts"
COMMON_DIR = LOADOUT_DIR / "common"
TYPE_DIR = BOT_DIR / "types"


def is_item_id(value):
    return isinstance(value, str) and ITEM_ID_RE.fullmatch(value) is not None


def read_json(path):
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def write_json(path, data):
    with path.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=4)
        f.write("\n")


def merge_deep(dst, src):
    for key, value in src.items():
        if isinstance(value, dict):
            node = dst.setdefault(key, {})
            if isinstance(node, dict):
                merge_deep(node, value)
            else:
                dst[key] = deepcopy(value)

        elif isinstance(value, list):
            node = dst.setdefault(key, [])
            if not isinstance(node, list):
                dst[key] = deepcopy(value)
                continue

            for item in value:
                if item not in node:
                    node.append(item)

        else:
            dst[key] = deepcopy(value)


def load_common_packages():
    packages = {}

    if not COMMON_DIR.exists():
        return packages

    for path in COMMON_DIR.glob("*.json"):
        data = read_json(path)

        if not isinstance(data, dict):
            continue

        for key, value in data.items():
            if isinstance(key, str) and not is_item_id(key):
                packages[key] = value

    return packages


def collect_package_refs(data, packages):
    refs = set()

    def walk(value):
        if isinstance(value, dict):
            for k, v in value.items():
                if isinstance(k, str) and k in packages:
                    refs.add(k)

                if isinstance(v, str) and v in packages:
                    refs.add(v)

                if isinstance(v, dict):
                    ref_id = v.get("id")
                    if isinstance(ref_id, str) and ref_id in packages:
                        refs.add(ref_id)

                walk(v)

        elif isinstance(value, list):
            for item in value:
                walk(item)

        elif isinstance(value, str) and value in packages:
            refs.add(value)

    walk(data)
    return refs


def collect_item_ids(value):
    ids = []

    def walk(node):
        if isinstance(node, dict):
            for k, v in node.items():
                if is_item_id(k):
                    ids.append(k)

                if is_item_id(v):
                    ids.append(v)

                walk(v)

        elif isinstance(node, list):
            for item in node:
                walk(item)

        elif is_item_id(node):
            ids.append(node)

    walk(value)

    seen = set()
    result = []

    for item_id in ids:
        if item_id not in seen:
            seen.add(item_id)
            result.append(item_id)

    return result


def get_package_root_ids(package):
    roots = []

    if isinstance(package, dict):
        package_id = package.get("id")
        if is_item_id(package_id):
            roots.append(package_id)

        for key in package.keys():
            if is_item_id(key):
                roots.append(key)

    if roots:
        return list(dict.fromkeys(roots))

    return collect_item_ids(package)


def expand_equipment_pool(pool, packages):
    output = {}

    if not isinstance(pool, dict):
        return output

    for key, value in pool.items():
        weight = 1

        if isinstance(value, (int, float)):
            weight = value

        if isinstance(value, dict):
            weight = value.get("chance", value.get("weight", 1))
            ref = value.get("id")

            if is_item_id(ref):
                output[ref] = weight
                continue

            if isinstance(ref, str) and ref in packages:
                for root_id in get_package_root_ids(packages[ref]):
                    output[root_id] = weight
                continue

        if is_item_id(key):
            output[key] = weight
            continue

        if isinstance(key, str) and key in packages:
            for root_id in get_package_root_ids(packages[key]):
                output[root_id] = weight
            continue

        if is_item_id(value):
            output[value] = weight
            continue

        if isinstance(value, str) and value in packages:
            for root_id in get_package_root_ids(packages[value]):
                output[root_id] = weight

    return output


def normalize_equipment(equipment, packages):
    output = {}

    if not isinstance(equipment, dict):
        return output

    for slot, pool in equipment.items():
        output[slot] = expand_equipment_pool(pool, packages)

    return output


def looks_like_root_mods_block(value):
    if not isinstance(value, dict):
        return False

    return any(is_item_id(key) and isinstance(val, dict) for key, val in value.items())


def normalize_mods(mods, packages, root_ids=None):
    output = {}

    if not isinstance(mods, dict):
        return output

    # Normal type-style mods:
    # "5ac66d2e5acfc43b321d4b53": { "mod_magazine": [...] }
    if looks_like_root_mods_block(mods):
        for root_id, slots in mods.items():
            if not is_item_id(root_id) or not isinstance(slots, dict):
                continue

            root_output = output.setdefault(root_id, {})
            merge_deep(root_output, slots)

        return output

    # Package-style mods:
    # "mods": { "mod_magazine": [...] }
    if root_ids:
        for root_id in root_ids:
            root_output = output.setdefault(root_id, {})
            merge_deep(root_output, mods)

    return output


def get_package_mods(package, packages):
    output = {}

    if not isinstance(package, dict):
        return output

    root_ids = get_package_root_ids(package)

    if "mods" in package and isinstance(package["mods"], dict):
        merge_deep(output, normalize_mods(package["mods"], packages, root_ids))
        return output

    if looks_like_root_mods_block(package):
        merge_deep(output, normalize_mods(package, packages))
        return output

    return output


def sync_role(role, packages):
    loadout_path = LOADOUT_DIR / f"{role}.json"
    type_path = TYPE_DIR / f"{role}.json"

    if not loadout_path.exists():
        print(f"Missing loadout: {loadout_path}")
        return

    if not type_path.exists():
        print(f"Missing type: {type_path}")
        return

    loadout = read_json(loadout_path)
    type_data = read_json(type_path)

    source = loadout.get("inventory", loadout)
    inventory = type_data.setdefault("inventory", {})

    # Only sync these three sections.
    if "equipment" in source:
        inventory.setdefault("equipment", {})
        merge_deep(
            inventory["equipment"],
            normalize_equipment(source["equipment"], packages)
        )

    if "Ammo" in source:
        inventory.setdefault("Ammo", {})
        merge_deep(inventory["Ammo"], source["Ammo"])

    if "mods" in source:
        inventory.setdefault("mods", {})
        merge_deep(
            inventory["mods"],
            normalize_mods(source["mods"], packages)
        )

    # Pull mods from common packages referenced by this role loadout.
    for package_name in collect_package_refs(loadout, packages):
        package_mods = get_package_mods(packages[package_name], packages)

        if package_mods:
            inventory.setdefault("mods", {})
            merge_deep(inventory["mods"], package_mods)

    write_json(type_path, type_data)
    print(f"Synced equipment / Ammo / mods for {role}")


def main():
    packages = load_common_packages()

    for role in ROLES:
        sync_role(role, packages)


if __name__ == "__main__":
    main()