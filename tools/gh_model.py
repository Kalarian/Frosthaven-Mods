"""The 3D model of a ported Gloomhaven class: the one Gloomhaven asset bundle the plugin needs, copied from the player's
OWN Gloomhaven install into the content pack (mods\\gloomhaven\\bundles\\), and the "model" entry for pack.json that tells
the plugin what is inside, read from the bundle itself with UnityPy (no hand-typed bone or texture names).
Used by gh_port.py: export(gh, "Brute", OUT) -> dict for pack.json["model"]["Brute"].

Gloomhaven's heroes are Addressables: catalog.json maps "Assets/_AssetBundles/heroes/<Class>.prefab" to the bundle
holding the hero wrapper, the model (Assets/Content/Characters/Heroes/HE_<Class>/HE_<Class>_PR.prefab), its skeleton,
textures, animation clips, controller and weapons, plus 24 dependency bundles (particle materials, the built-in-pipeline
shader bundles). The plugin rebuilds every material on Frosthaven's own URP character shader and never plays Gloomhaven's
VFX, so it needs the hero bundle alone (83 MB for the Brute, 70 MB of which are its own textures). The dependency list
still goes into the entry ("not_shipped") for reference.

What the plugin does with the entry (GHModel.cs): the model's Animator object becomes the visual of a copy of the
Drifter's hero wrapper; the Drifter's animator controller drives it with every Drifter clip swapped for the clip this
module picked from the Gloomhaven controller ("animation.clips"), its hit/fire moments scaled to the new clip lengths
("animation.states"); the materials are rebuilt from the textures named here ("materials", with flat mask values where
Gloomhaven's mask would blacken the model); the sword and shield are re-equipped on the bones named here ("weapons").
Only the two prefabs have addresses in the bundle; clips, textures and weapon objects are reached through them.

Needs the Frosthaven install too (the Drifter's bundle, for her controller's states and clips): fh_mods.GAME.
"""

import base64
import hashlib
import json
import shutil
import struct
import sys
from pathlib import Path

try:
    import UnityPy
except ImportError as e:  # pragma: no cover
    raise ImportError("gh_model needs UnityPy (pip install UnityPy)") from e

BUNDLES = Path("GH_Data") / "StreamingAssets" / "aa" / "StandaloneWindows64"
CATALOG = Path("GH_Data") / "StreamingAssets" / "aa" / "catalog.json"
RUNTIME_PATH = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}"
FH_BUNDLES = Path("Frosthaven_Data") / "StreamingAssets" / "AssetBundles" / "Base"
BASE_HERO = "Drifter"  # the Frosthaven hero whose wrapper, animator controller and material template the plugin borrows
WEAPON_TYPES = {0: "Melee", 1: "Projectile", 2: "Cosmetic"}  # Weapon.WeaponTypes, the same enum in both games
TEXTURE_SLOTS = {"_Diffuse": "albedo", "_MRAO": "mask", "_NormalMap": "normal", "_EmissiveMap": "emissive"}
CUTOUT_FRACTION = 0.05  # an albedo with more than this share of texels below half alpha is a cutout (fur cards)
# Gloomhaven's mask textures (R metallic, G smoothness, B occlusion) are not always what Frosthaven's shader expects:
# the Brute's has R = G = 0 everywhere and a B channel that is black over large areas, which Frosthaven multiplies into
# the albedo (a near-black hero). A mask whose B is darker than this on average, or black over more than this share of
# texels, is replaced by flat values; a sane one (the weapons') is used as it is.
AO_MEAN_MIN, AO_DARK_MAX, SMOOTHNESS_MIN = 0.7, 0.1, 0.3
# Per-material look tweaks, by Gloomhaven material name: "brightness" multiplies the albedo (the shader's _Value, 1 =
# as authored). Gloomhaven's shader lit the Brute far lighter than his albedo texture; tune after an in-game look.
MATERIAL_TWEAKS = {
    "HE_Brute_MAT": {"brightness": 1.0},
    "HE_Brute_MAT_2Side": {"brightness": 1.0},
}

# Frosthaven's Drifter states -> the Gloomhaven states whose clip fits, tried in order; "@idle" and "@run" are the two
# halves of Gloomhaven's Idle-Run blend. A state not listed tries its own name, then idles. Several Drifter states share
# one clip (PowerUp, Heal, Push, Pull and DangerArea all play Drifter_Cast), and a clip can only be swapped once, so the
# first state in the controller that uses a clip decides (PowerUp comes before the others).
STATE_ALIASES = {
    "Idle-Run": ["@idle"],
    "Run": ["@run"],
    "RareIdle": ["@idle"], "AlertRareIdle": ["@idle"], "RareIdle-Poison": ["@idle"], "RareIdle-Wound": ["@idle"],
    "AlertRareIdle-Poison": ["@idle"], "AlertRareIdle-Wound": ["@idle"],
    "Selection Screen": ["Selection Screen", "@idle"],
    "Attack_Enhanced": ["Attack"],
    "Attack_AOE_Cone": ["HeavyMeleeAttack", "Attack"], "Attack_AOE_Cone_Enhanced": ["HeavyMeleeAttack", "Attack"],
    "Attack_AOE_Radial": ["RadialAttack", "HeavyMeleeAttack", "Attack"],
    "Attack_AOE_Radial_Enhanced": ["RadialAttack", "HeavyMeleeAttack", "Attack"],
    "RangedAttack_Enhanced": ["RangedAttack"],
    "RangedAttack_AOE": ["RangedAreaAttack", "RangedAttack"], "RangedAttack_AOE_Enhanced": ["RangedAreaAttack", "RangedAttack"],
    "RangedAttack_AOE_NonArea": ["RangedAreaAttack", "RangedAttack"],
    "RangedAttack_AOE_NonArea_Enhanced": ["RangedAreaAttack", "RangedAttack"],
    "PowerUp_No_VFX": ["PowerUp"],
    "Heal": ["Heal", "PowerUp"], "Heal_Enhanced": ["Heal", "PowerUp"],
    "Push": ["Push", "TargetedShout", "PowerUp"], "Pull": ["Pull", "Push", "PowerUp"],
    "DangerArea": ["RadialShout", "PowerUp"],
    "Persistent Buff": ["PowerUpShield", "PowerUp"],
    "PushPull": ["PushPull", "Hit"],
    "Death": ["Death"], "Defeat": ["Defeat", "Death"],
    "Celebration": ["Celebration", "RadialShout", "PowerUp"],
}
# Drifter sound events the plugin blanks on the ported hero: her voice (hurt, exhausted), not the generic foley.
VOICE_WORDS = ("Hurt", "Exhaust", "Celebrat", "Voice", "Grunt")


# ---------------------------------------------------------------- the Addressables catalog

def _catalog(gh):
    """(internal ids, [(key, [entry index...])], [entry tuples]) from catalog.json, as Unity.ResourceManager's
    ContentCatalogData lays them out (key data, bucket data, 28-byte entries)."""
    d = json.loads((gh / CATALOG).read_text(encoding="utf-8"))
    kd = base64.b64decode(d["m_KeyDataString"])
    bd = base64.b64decode(d["m_BucketDataString"])
    ed = base64.b64decode(d["m_EntryDataString"])

    def key(off):
        t = kd[off]
        off += 1
        if t in (0, 1):  # ascii / utf-16 string
            n = struct.unpack_from("<i", kd, off)[0]
            raw = kd[off + 4:off + 4 + n]
            return raw.decode("ascii", "replace") if t == 0 else raw.decode("utf-16-le")
        return None  # numbers and hashes: never a hero key

    buckets, off = [], 4
    for _ in range(struct.unpack_from("<i", bd, 0)[0]):
        data_off, count = struct.unpack_from("<ii", bd, off)
        off += 8
        buckets.append((key(data_off), list(struct.unpack_from(f"<{count}i", bd, off))))
        off += 4 * count
    entries = [struct.unpack_from("<7i", ed, 4 + 28 * i) for i in range(struct.unpack_from("<i", ed, 0)[0])]
    return d["m_InternalIds"], buckets, entries


def bundles_for(gh, cls):
    """The bundle files the catalog lists for the class's hero prefab (the hero bundle among them)."""
    ids, buckets, entries = _catalog(gh)
    want = f"Assets/_AssetBundles/heroes/{cls}.prefab"
    for k, ents in buckets:
        if k == want:
            entry = entries[ents[0]]
            if entry[2] < 0:
                raise ValueError(f"{want}: no dependency list in catalog.json")
            return [Path(ids[entries[dep][0]].replace(RUNTIME_PATH, "")).name for dep in buckets[entry[2]][1]]
    raise ValueError(f"no Gloomhaven hero {cls!r} in catalog.json (looked for {want})")


# ---------------------------------------------------------------- reading a bundle

class Bundle:
    """A UnityPy view of one bundle with the lookups this module needs."""

    def __init__(self, path):
        self.path = Path(path)
        self.env = UnityPy.load(str(path))
        self.byid = {o.path_id: o for o in self.env.objects}
        self.scripts = {o.path_id: o.read().m_ClassName for o in self.env.objects if o.type.name == "MonoScript"}
        self.container = dict(self.env.container)

    def obj(self, pp):
        return self.byid.get(pp["m_PathID"]) if pp and pp["m_PathID"] and pp["m_FileID"] == 0 else None

    def name(self, pp):
        """The name of a referenced object, or of the GameObject a referenced component sits on."""
        o = self.obj(pp)
        if o is None:
            return None
        t = o.read_typetree()
        if o.type.name != "GameObject" and "m_GameObject" in t:
            go = self.obj(t["m_GameObject"])
            return go.read_typetree()["m_Name"] if go is not None else None
        return t.get("m_Name")

    def components(self, go):
        """(class name, type tree) for each component of a GameObject object."""
        for c in go.read_typetree()["m_Component"]:
            co = self.obj(c["component"])
            if co is None:
                continue
            t = co.read_typetree()
            if co.type.name == "MonoBehaviour":
                cls = self.scripts.get(t["m_Script"]["m_PathID"]) if t["m_Script"]["m_FileID"] == 0 else None
                yield cls or _guess_class(t), t
            else:
                yield co.type.name, t

    def walk(self, root_go):
        """Every GameObject object under (and including) a root GameObject object, depth first."""
        tr = next((t for n, t in self.components(root_go) if n == "Transform"), None)
        stack = [(root_go, tr)]
        while stack:
            go, tr = stack.pop()
            yield go
            if tr is None:
                continue
            for ch in tr["m_Children"]:
                cho = self.obj(ch)
                if cho is None:
                    continue
                ct = cho.read_typetree()
                cgo = self.obj(ct["m_GameObject"])
                if cgo is not None:
                    stack.append((cgo, ct))

    def asset_bundle_name(self):
        for o in self.env.objects:
            if o.type.name == "AssetBundle":
                return o.read().m_Name
        return None


def _guess_class(t):
    """A MonoBehaviour whose script is external (Frosthaven's Assembly-CSharp): told apart by its fields."""
    if "impactWeaponName" in t or "TargetHexEffects" in t:
        return "CastEffectsSMB"
    if "FireTime" in t and "Projectile" in t:
        return "FireEquippedProjectileSMB"
    if "AudioEvent" in t and "AudioDelay" in t and len(t) < 8:
        return "AudioSMB"
    return "?"


def controllers(b):
    """Every AnimatorController in the bundle: {name: {"params": [...], "states": [{name, fullPath, clips, lengths,
    smbs: [{class, fields}]}]}}, with clip lengths from the AnimationClip objects."""
    lengths = {}
    for o in b.env.objects:
        if o.type.name == "AnimationClip":
            t = o.read_typetree()
            mc = t["m_MuscleClip"]
            lengths[t["m_Name"]] = round(mc["m_StopTime"] - mc["m_StartTime"], 4)
    out = {}
    for o in b.env.objects:
        if o.type.name != "AnimatorController":
            continue
        t = o.read_typetree()
        tos = {int(k): v for k, v in t["m_TOS"]}
        ctrl = t["m_Controller"]
        clipnames = [b.name(c) or "?" for c in t["m_AnimationClips"]]
        smbs = {}
        desc = t.get("m_StateMachineBehaviourVectorDescription", {})
        for key, rng in desc.get("m_StateMachineBehaviourRanges", []):
            found = []
            for i in range(rng["m_StartIndex"], rng["m_StartIndex"] + rng["m_Count"]):
                mb = b.obj(t["m_StateMachineBehaviours"][desc["m_StateMachineBehaviourIndices"][i]])
                if mb is None:
                    continue
                mt = mb.read_typetree()
                cls = b.scripts.get(mt["m_Script"]["m_PathID"]) if mt["m_Script"]["m_FileID"] == 0 else None
                found.append({"class": cls or _guess_class(mt), "fields": _plain(b, mt)})
            smbs[tos.get(key["m_StateID"], key["m_StateID"])] = found
        states = []
        for sm in ctrl["m_StateMachineArray"]:
            for st in sm["data"]["m_StateConstantArray"]:
                s = st["data"]
                name, full = tos.get(s["m_NameID"], str(s["m_NameID"])), tos.get(s["m_FullPathID"], "")
                clips = []
                for bt in s["m_BlendTreeConstantArray"]:
                    for n in bt["data"]["m_NodeArray"]:
                        ci = n["data"]["m_ClipID"]
                        if ci != 4294967295 and ci < len(clipnames):
                            clips.append(clipnames[ci])
                states.append({"name": name, "fullPath": full, "clips": clips,
                               "lengths": [lengths.get(c) for c in clips], "smbs": smbs.get(full) or smbs.get(name) or []})
        out[t["m_Name"]] = {"states": states, "clips": sorted({c for s in states for c in s["clips"]})}
    return out, lengths


def _plain(b, v):
    """A type tree with object references replaced by names (PPtr -> the referenced object's name)."""
    if isinstance(v, dict):
        if set(v) == {"m_FileID", "m_PathID"}:
            return b.name(v) if v["m_PathID"] else None
        return {k: _plain(b, x) for k, x in v.items() if not k.startswith("m_")}
    if isinstance(v, list):
        return [_plain(b, x) for x in v]
    return v


# ---------------------------------------------------------------- the model entry

def describe(bundle_path, cls, fh_bundle_path):
    """What the plugin needs to know about the bundle, read from it: prefab paths, the Animator object, bones, height,
    weapons, materials and their textures, and the Drifter-clip -> Gloomhaven-clip table with the per-state timings."""
    b = Bundle(bundle_path)
    hero_path = f"Assets/_AssetBundles/heroes/{cls}.prefab"
    model_path = next((p for p in b.container if p.lower().endswith(f"/he_{cls.lower()}_pr.prefab")), None)
    if hero_path not in b.container or model_path is None:
        raise ValueError(f"{bundle_path.name} lacks {hero_path} or HE_{cls}_PR.prefab; has {sorted(b.container)}")
    hero_go, model_go = b.container[hero_path], b.container[model_path]

    cm = next(t for n, t in b.components(hero_go) if n == "CharacterManager")
    weapons = []
    for wl in cm["WeaponLists"]:
        root = b.obj(wl["Weapons"][0]) if wl["Weapons"] else None
        if root is None:
            continue
        wtype = next((t["WeaponType"] for n, t in b.components(root) if n == "Weapon"), 2)
        weapons.append({"list": wl["name"], "bone": wl["AttachToBone"], "root": root.read_typetree()["m_Name"],
                        "type": WEAPON_TYPES.get(wtype, "Cosmetic"), "_go": root})

    # the model: the Animator's GameObject, its VFX anchors, the skinned meshes, their materials and textures
    animator_go, body, top, trail_points, materials, bones = None, (None, -1), 0.0, [], {}, set()
    for go in b.walk(model_go):
        gname = go.read_typetree()["m_Name"]
        bones.add(gname)
        for n, t in b.components(go):
            if n == "Animator" and animator_go is None:
                animator_go = gname
            elif n == "VFXLookup":
                trail_points = [x for x in (b.name(p) for p in t.get("TrailPoints", [])) if x]
            elif n == "SkinnedMeshRenderer":
                mesh = b.obj(t["m_Mesh"])
                if mesh is None:
                    continue
                mt = mesh.read_typetree()
                if not mt["m_VertexData"]["m_VertexCount"]:
                    continue  # an empty LOD placeholder
                if len(t["m_Bones"]) > body[1]:
                    body = (gname, len(t["m_Bones"]))
                aabb = mt["m_LocalAABB"]
                top = max(top, aabb["m_Center"]["y"] + aabb["m_Extent"]["y"])
                _materials(b, t["m_Materials"], materials)
    if animator_go is None:
        raise ValueError(f"{cls}: no Animator under {model_path}")
    kept = []
    for w in weapons:
        if w["type"] == "Projectile":
            continue  # the thrown knife: its material lives in another bundle, and Frosthaven throws its own projectile
        external = False
        for go in b.walk(w.pop("_go")):
            for n, t in b.components(go):
                if n in ("SkinnedMeshRenderer", "MeshRenderer"):
                    external |= not _materials(b, t["m_Materials"], materials)
        if external:
            print(f"warning: {cls}: weapon {w['root']} uses textures from another bundle, not equipped")
            continue
        kept.append(w)
    melee_bone = next((w["bone"] for w in kept if w["type"] == "Melee"), None)
    anchor = next((n for n in sorted(bones) if "weaponPropHook" in n and n.startswith("R_")), None) \
        or next((n for n in sorted(bones) if "weaponPropHook" in n), None) or melee_bone or ""

    animation = _animation(b, fh_bundle_path)
    return {
        "prefab": model_path, "hero_prefab": hero_path, "animator": animator_go, "body_mesh": body[0],
        "target_bone": cm["TargetBone"], "gh_height": round(cm["Height"], 3), "height": round(top, 2),
        "hit_radius": round(cm["HitRadius"], 3), "trail_points": trail_points, "projectile_anchor": anchor,
        "weapons": kept, "materials": materials, "animation": animation,
    }


def _materials(b, pptrs, out):
    """Adds the materials behind a renderer's slots to <out> (name -> texture names, cutout, floats). False when one of
    their body textures lives in another bundle (the plugin would see a null texture and render white)."""
    ok = True
    for m in pptrs:
        mat = b.obj(m)
        if mat is None:
            ok = False
            continue
        mt = mat.read_typetree()
        if mt["m_Name"] in out:
            continue
        props = mt["m_SavedProperties"]
        texs = {}
        for slot, v in props["m_TexEnvs"]:
            p = v["m_Texture"]
            if slot not in TEXTURE_SLOTS or not p["m_PathID"]:
                continue
            if p["m_FileID"] != 0 or p["m_PathID"] not in b.byid:
                ok = False
                continue
            texs[slot] = b.byid[p["m_PathID"]]
        floats = dict(props["m_Floats"])
        entry = {TEXTURE_SLOTS[s]: (texs[s].read_typetree()["m_Name"] if s in texs else None) for s in TEXTURE_SLOTS}
        if not floats.get("_UseEmissiveMap", 1.0):
            entry["emissive"] = None  # the material had an emissive map it never used
        entry["cutout"] = _is_cutout(texs.get("_Diffuse"))
        entry["cutoff"] = round(floats.get("_Cutoff", 0.5), 3)
        entry["emissive_boost"] = round(floats.get("_EmissiveMapBoost", 1.0), 3)
        entry["two_sided"] = mt["m_Name"].endswith("_2Side")
        entry.update(_mask_mode(texs.get("_MRAO"), mt["m_Name"]))
        entry.update(MATERIAL_TWEAKS.get(mt["m_Name"], {}))
        out[mt["m_Name"]] = entry
    return ok


def _mask_mode(tex, material):
    """Whether the plugin uses the mask texture or flat values (see AO_MEAN_MIN): {"mask_mode": "texture"} or
    {"mask_mode": "flat", "metallic": r, "smoothness": g, "ao": 1}."""
    if tex is None:
        return {"mask_mode": "flat", "metallic": 0.0, "smoothness": SMOOTHNESS_MIN, "ao": 1.0}
    try:
        im = tex.read().image.convert("RGB").resize((256, 256))
    except Exception as e:  # noqa: BLE001
        print(f"warning: could not read the mask of {material}: {e}")
        return {"mask_mode": "texture"}
    means = [sum(h[i] * i for i in range(256)) / (256 * 256 * 255) for h in (im.getchannel(c).histogram() for c in "RGB")]
    dark = sum(im.getchannel("B").histogram()[:26]) / (256 * 256)
    if means[2] >= AO_MEAN_MIN and dark <= AO_DARK_MAX:
        return {"mask_mode": "texture"}
    print(f"note: {material}: mask occlusion averages {means[2]:.2f} with {dark:.0%} black texels; using flat values instead")
    return {"mask_mode": "flat", "metallic": round(means[0], 3), "smoothness": round(max(means[1], SMOOTHNESS_MIN), 3), "ao": 1.0}


def _is_cutout(tex):
    """True when the albedo's alpha is a cutout mask (fur cards): the plugin then uses Frosthaven's alpha-clipping
    character shader for that material."""
    if tex is None:
        return False
    try:
        im = tex.read().image
        if im.mode != "RGBA":
            return False
        hist = im.getchannel("A").histogram()
        return sum(hist[:128]) / (im.size[0] * im.size[1]) > CUTOUT_FRACTION
    except Exception as e:  # noqa: BLE001 - a texture that won't decode is treated as opaque
        print(f"warning: could not read the alpha of {tex.read_typetree().get('m_Name')}: {e}")
        return False


def _animation(b, fh_bundle_path):
    """The Drifter-clip -> Gloomhaven-clip table and per-state timings: Frosthaven's Drifter controller (her bundle) is
    the one the plugin drives the model with, each of her clips replaced by the Gloomhaven clip of the matching state."""
    fh = Bundle(fh_bundle_path)
    fh_ctrls, fh_len = controllers(fh)
    gh_ctrls, gh_len = controllers(b)
    fh_ctrl = fh_ctrls.get(f"HE_{BASE_HERO}_Controller") or next(iter(fh_ctrls.values()))
    gh_ctrl = next(iter(gh_ctrls.values()))
    gh_by_name = {s["name"]: s for s in gh_ctrl["states"]}
    idle_state = gh_by_name.get("Idle-Run") or gh_ctrl["states"][0]
    gh_idle = idle_state["clips"][0]
    gh_run = next((c for c in idle_state["clips"][1:] if "run" in c.lower()), idle_state["clips"][-1])

    def resolve(state):
        for cand in STATE_ALIASES.get(state["name"], [state["name"]]) + ["@idle"]:
            if cand == "@idle":
                return gh_idle, idle_state
            if cand == "@run":
                return gh_run, idle_state
            g = gh_by_name.get(cand)
            if g is not None and g["clips"]:
                return g["clips"][0], g
        return gh_idle, idle_state

    clips, states, silence, projectiles, melee_weapon = {}, {}, set(), [], None
    for s in fh_ctrl["states"]:
        target, g = resolve(s)
        for c in s["clips"]:
            clips.setdefault(c, target)
        if not s["clips"]:
            continue
        playing = clips[s["clips"][0]]  # what this state really plays (another state may have claimed the clip first)
        fh_length, gh_length = fh_len.get(s["clips"][0]), gh_len.get(playing)
        timing = {"scale": round(gh_length / fh_length, 4) if fh_length and gh_length else 1.0}
        gh_values = _smb_times(g["smbs"]) if g is not None and playing == target else {}
        if gh_values:
            timing["gh"] = gh_values
        fh_values = _smb_times(s["smbs"])
        if "fire" in fh_values:
            timing["fh"] = {"fire": fh_values["fire"]}
        gh_sound = _smb_sound(g["smbs"]) if g is not None and playing == target else None
        if gh_sound:
            timing["audio"] = gh_sound   # the Gloomhaven event this state's clip played with (GHSound registers it)
        states[s["fullPath"]] = timing
        for smb in s["smbs"]:
            f = smb["fields"]
            if smb["class"] == "CastEffectsSMB":
                ev = f.get("AbilityAudioEvent") or ""
                if ev.startswith(BASE_HERO + "_") and any(w in ev for w in VOICE_WORDS):
                    silence.add(ev)
                if s["name"] == "Attack" and f.get("impactWeaponName"):
                    melee_weapon = f["impactWeaponName"]
            elif smb["class"] == "FireEquippedProjectileSMB" and f.get("Projectile") and f["Projectile"] not in projectiles:
                projectiles.append(f["Projectile"])
            elif smb["class"] == "AudioSMB":
                ev = f.get("AudioEvent") or ""
                if ev.startswith(BASE_HERO + "_") and any(w in ev for w in VOICE_WORDS):
                    silence.add(ev)
    missing = sorted(c for c in fh_ctrl["clips"] if c not in clips)
    if missing:
        print(f"warning: Drifter clips with no Gloomhaven clip (they will idle): {', '.join(missing)}")
    # for card animations the Drifter's animator has no state for (a Gloomhaven card's "TargetedShout"): the clip each
    # Gloomhaven state plays, and the clip each of her states plays, so the plugin can lend the Gloomhaven clip to the
    # state the game falls back to, for that one play (GHModel.LendClip)
    state_clips = {s["name"]: s["clips"][0] for s in fh_ctrl["states"] if s["clips"]}
    gh_states = {s["name"]: s["clips"][0] for s in gh_ctrl["states"] if s["clips"] and s["name"] not in state_clips}
    gh_audio = {s["name"]: _smb_sound(s["smbs"]) for s in gh_ctrl["states"] if s["name"] in gh_states and _smb_sound(s["smbs"])}
    return {"base": BASE_HERO, "clips": clips, "states": states, "projectiles": projectiles,
            "melee_weapon": melee_weapon, "silence": sorted(silence), "state_clips": state_clips, "gh_states": gh_states,
            "gh_audio": gh_audio}


def _smb_sound(smbs):
    """The audio event a state's behaviours play (its cast event, else its first AudioSMB event), or None."""
    for smb in smbs:
        if smb["class"] == "CastEffectsSMB" and smb["fields"].get("AbilityAudioEvent"):
            return smb["fields"]["AbilityAudioEvent"]
    for smb in smbs:
        if smb["class"] == "AudioSMB" and smb["fields"].get("AudioEvent"):
            return smb["fields"]["AudioEvent"]
    return None


def _smb_times(smbs):
    """The moments a state's behaviours act at: hit (impact VFX and damage), progress (the ability's effect), fire (a
    projectile leaves the hand); only the ones that are set."""
    out = {}
    for smb in smbs:
        f = smb["fields"]
        if smb["class"] == "CastEffectsSMB":
            hits = [(f.get(k) or {}).get("HitTime") or 0 for k in ("TargetHexEffects", "AreaHexEffects", "AreaCentralHexEffects")]
            if max(hits) > 0:
                out["hit"] = round(max(hits), 3)
            progress = [p.get("ProgressTime") or 0 for p in f.get("ProgressChoreographer") or []]
            if progress and max(progress) > 0:
                out["progress"] = round(max(progress), 3)
        elif smb["class"] == "FireEquippedProjectileSMB" and (f.get("FireTime") or 0) > 0:
            out["fire"] = round(f["FireTime"], 3)
    return out


# ---------------------------------------------------------------- the pack build step

def export(gh, fh, cls, out):
    """Copy the class's hero bundle into <out>/bundles and return the pack.json "model" entry for the class."""
    names = bundles_for(gh, cls)
    hero = next((n for n in names if n.startswith(f"hero_{cls.lower()}_")), None)
    if hero is None:
        raise ValueError(f"{cls}: catalog lists no hero_{cls.lower()}_* bundle among {names}")
    src = gh / BUNDLES / hero
    if not src.exists():
        raise FileNotFoundError(f"{cls}: Gloomhaven bundle missing: {src}")
    fh_bundle = fh / FH_BUNDLES / f"hero_{BASE_HERO.lower()}"
    if not fh_bundle.exists():
        raise FileNotFoundError(f"Frosthaven's {BASE_HERO} bundle missing: {fh_bundle}")
    entry = describe(src, cls, fh_bundle)
    dst = out / "bundles" / hero
    dst.parent.mkdir(parents=True, exist_ok=True)
    if not dst.exists() or dst.stat().st_size != src.stat().st_size:
        shutil.copyfile(src, dst)
    entry.update({
        "bundle": hero, "bundle_name": Bundle(src).asset_bundle_name(), "size": src.stat().st_size,
        "sha256": hashlib.sha256(src.read_bytes()).hexdigest(),
        "not_shipped": sorted(n for n in names if n != hero),  # shaders (built-in pipeline), VFX materials, the knife
    })
    return entry


if __name__ == "__main__":
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import fh_mods  # noqa: E402
    import gh_port  # noqa: E402

    gh_dir, _ = gh_port.find_gloomhaven()
    for cls in sys.argv[1:] or ["Brute"]:
        print(json.dumps(export(gh_dir, fh_mods.GAME, cls, Path("scratch_model")), indent=1))
