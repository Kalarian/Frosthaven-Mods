"""Gloomhaven digital's audio table (its ClockStone AudioController): sound event -> clips, with the volumes.

The table is a MonoBehaviour in GH_Data/resources.assets, serialized without type trees. UnityPy's own reader fails on
it (it chokes on the playlists after the categories), so the tree is generated from the game's assemblies
(UnityPy.helpers.TypeTreeGenerator, which needs the TypeTreeGeneratorAPI package) and walked by a small reader here, up
to and including the categories. The object is found by scanning MonoBehaviours for a known event name; the clip
names come from the AudioClip objects the sub-items point at (same file). Read once per run (cached).

    read_items(gh) -> {"PlaySound_BruteOverwhelmingAssault": {"category": "Brute_SFX", "category_volume": 0.76,
                        "volume": 1.0, "clips": [("Brute_Overwhelming_Assault_v2", 1.0)]}, ...}

Used by gh_sound.py to find a class's clips from the events its animator states name, in place of a hand-typed table.
The generator prints "Error generating tree nodes" for a few unrelated classes while loading; that is noise.
"""

import struct
from pathlib import Path

try:
    import UnityPy
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
except ImportError as e:  # pragma: no cover
    raise ImportError("gh_audio needs UnityPy and TypeTreeGeneratorAPI (pip install UnityPy TypeTreeGeneratorAPI)") from e

RESOURCES = Path("GH_Data") / "resources.assets"
MANAGED = Path("GH_Data") / "Managed"
UNITY_VERSION = "2021.3.5f1"
PROBE = b"PlaySound_BruteOverwhelmingAssault"   # an event every Gloomhaven install has: finds the controller's object

_SIMPLE = {"int": ("<i", 4), "SInt32": ("<i", 4), "unsigned int": ("<I", 4), "UInt32": ("<I", 4), "float": ("<f", 4),
           "SInt64": ("<q", 8), "UInt64": ("<Q", 8), "double": ("<d", 8), "SInt16": ("<h", 2), "UInt16": ("<H", 2),
           "UInt8": ("<B", 1), "bool": ("<?", 1), "char": ("<B", 1), "SInt8": ("<b", 1)}
_cache = {}


def _kids(node):
    return getattr(node, "m_Children", None) or []


def _read(node, data, pos):
    """One serialized value by its type-tree node: (value, next position). Strings and arrays carry a length; a
    node with the 0x4000 flag is followed by 4-byte alignment."""
    kind, children = node.m_Type, _kids(node)
    align = bool(getattr(node, "m_MetaFlag", 0) & 0x4000)
    if kind == "string":
        n = struct.unpack_from("<i", data, pos)[0]
        pos += 4
        value = data[pos:pos + n].decode("utf-8", "replace")
        pos = (pos + n + 3) & ~3
    elif children and children[0].m_Type == "Array":
        array, element = children[0], _kids(children[0])[1]
        n = struct.unpack_from("<i", data, pos)[0]
        pos += 4
        value = []
        for _ in range(n):
            item, pos = _read(element, data, pos)
            value.append(item)
        if getattr(array, "m_MetaFlag", 0) & 0x4000 or align:
            pos = (pos + 3) & ~3
    elif kind in _SIMPLE:
        fmt, size = _SIMPLE[kind]
        value = struct.unpack_from(fmt, data, pos)[0]
        pos += size
        if align:
            pos = (pos + 3) & ~3
    else:
        value = {}
        for child in children:
            value[child.m_Name], pos = _read(child, data, pos)
        if align:
            pos = (pos + 3) & ~3
    return value, pos


def read_items(gh):
    """Every audio item of Gloomhaven's AudioController, by event name (see the module docstring)."""
    gh = Path(gh)
    key = str(gh.resolve())
    if key in _cache:
        return _cache[key]
    env = UnityPy.load(str(gh / RESOURCES))
    generator = TypeTreeGenerator(UNITY_VERSION)
    generator.load_local_dll_folder(str(gh / MANAGED))
    root = generator.get_nodes_up("GH.Runtime.dll", "AudioController")
    clip_names = {o.path_id: o.read_typetree()["m_Name"] for o in env.objects if o.type.name == "AudioClip"}
    items = {}
    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        raw = o.get_raw_data()
        if PROBE not in raw:
            continue
        pos, fields = 0, {}
        for child in _kids(root):
            fields[child.m_Name], pos = _read(child, raw, pos)
            if child.m_Name == "AudioCategories":
                break
        for category in fields.get("AudioCategories", []):
            for item in category.get("AudioItems", []):
                clips = []
                for sub in item.get("subItems", []):
                    pptr = sub.get("Clip") or {}
                    name = clip_names.get(pptr.get("m_PathID")) if pptr.get("m_FileID", 0) == 0 else None
                    if name:
                        clips.append((name, float(sub.get("Volume", 1.0))))
                items[item["Name"]] = {"category": category.get("Name"), "category_volume": float(category.get("_volume", 1.0)),
                                       "volume": float(item.get("Volume", 1.0)), "clips": clips}
        break
    if not items:
        raise ValueError(f"no AudioController found in {gh / RESOURCES}")
    _cache[key] = items
    return items


if __name__ == "__main__":
    import json
    import sys
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import gh_port  # noqa: E402

    gh_dir, _ = gh_port.find_gloomhaven()
    table = read_items(gh_dir)
    wanted = sys.argv[1:]
    print(json.dumps({k: v for k, v in table.items() if not wanted or any(w.lower() in k.lower() for w in wanted)}, indent=1))
