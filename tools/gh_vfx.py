"""A ported Gloomhaven class's own ability effects (the particle prefabs its animator states play), from the player's
OWN Gloomhaven install, for the plugin to play in place of the Drifter's (GHVfx.cs). Used by gh_port.py after
gh_model (needs UnityPy).

The prefabs ride in the hero bundle already (loading the model brings them into memory: they are in its preload
table), but they can't be used as they are:
  - their materials sit on Gloomhaven's built-in-pipeline shaders, which Frosthaven's URP can't draw. Most are
    Frosthaven's own effect shaders by name (the two games share their VFX library), so the plugin rebuilds each
    material on Frosthaven's copy of its shader from the property values recorded here ("effect_materials"), with
    stand-ins for the few Gloomhaven-only ones (Unity's legacy particle shaders, which Frosthaven still carries);
  - many of the materials live in other Gloomhaven bundles (one small Addressables bundle per material, referenced
    directly or loaded by a MaterialLoader script): those bundles are copied into the pack ("effect_bundles") and each
    renderer's material names are recorded, so the plugin can fill the slots that come up empty;
  - the scripts on the prefabs are Gloomhaven's (missing in Frosthaven); the ones Frosthaven has too, with the same
    fields (lifetime, a sound on enable, light curves, turbulence), are recorded field by field for the plugin to
    re-add. Left off: LevelUseParticles (a quality setting), MaterialLoader (the plugin assigns the materials itself),
    RFX4_CameraShake (an empty class in Frosthaven), AraTrail and Director_* (weapon trails, which Frosthaven draws
    its own way).
Which effect each state plays, and when, is recorded by gh_model._animation ("animation.states[*].effects",
"animation.gh_effects").

    effects:          {"P_Brute_Shout": {"objects": {"<path under the prefab>": {"materials": [names or null],
                       "trail_material": name, "loaded": {"materials": [names], "keep": bool},
                       "components": [{"type": "SFXOnEnable", "fields": {...}}]}}}}
    effect_materials: {"<material>": {"shader", "bundle" (null: the hero bundle), "floats", "colors", "textures",
                       "keywords", "queue"}}
    effect_bundles:   [{"file", "name", "size", "sha256"}]
"""

import hashlib
import shutil
from pathlib import Path

try:
    import UnityPy
except ImportError as e:  # pragma: no cover
    raise ImportError("gh_vfx needs UnityPy (pip install UnityPy)") from e

import gh_model
from gh_model import BUNDLES, RUNTIME_PATH

# Gloomhaven effect scripts Frosthaven has too, with the same serialized fields
CARRIED = ("EffectOnEnable", "ParticleSystem_OnEnable_Default", "SFXOnEnable", "OffsetTowardsCamera",
           "RFX4_LightCurves", "RFX4_Turbulence")
RENDERERS = ("ParticleSystemRenderer", "MeshRenderer", "TrailRenderer", "LineRenderer", "SkinnedMeshRenderer")
HEX_LISTS = ("target", "area", "central", "aura")


def _pairs(seq):
    for entry in seq or []:
        if isinstance(entry, (list, tuple)):
            yield entry[0], entry[1]
        else:
            yield entry.get("first"), entry.get("second")


class World:
    """One UnityPy view over the hero bundle and every bundle it may reach, with references followed across files."""

    def __init__(self, gh, bundle_files):
        self.bundle_files = list(bundle_files)
        self.env = UnityPy.load(*[str(gh / BUNDLES / n) for n in self.bundle_files])
        self.files = {}          # CAB name -> SerializedFile
        self.file_bundle = {}    # CAB name -> bundle file name
        for key, f in self.env.files.items():
            self._collect(key, f, Path(key).name)
        self.scripts = {}
        for o in self.env.objects:
            if o.type.name == "MonoScript":
                self.scripts[(o.assets_file, o.path_id)] = o.read_typetree().get("m_ClassName")

    def _collect(self, key, f, top):
        name = str(getattr(f, "name", None) or key).lower().split("/")[-1]
        if getattr(f, "objects", None) is not None and hasattr(f, "externals"):
            self.files[name] = f
            self.file_bundle[name] = top
        for k, ch in (getattr(f, "files", None) or {}).items():
            self._collect(k, ch, top)

    @staticmethod
    def tree(o):
        return o.read_typetree()

    def obj(self, o, pp):
        """The object a PPtr in <o> points at, or None (not set, or in a bundle that isn't loaded)."""
        if not pp or not isinstance(pp, dict) or not pp.get("m_PathID"):
            return None
        fid = pp["m_FileID"]
        f = o.assets_file
        if fid == 0:
            return f.objects.get(pp["m_PathID"])
        if fid - 1 >= len(f.externals):
            return None
        ext = f.externals[fid - 1]
        cab = str(getattr(ext, "path", None) or getattr(ext, "name", None) or "").lower().split("/")[-1]
        tf = self.files.get(cab)
        return tf.objects.get(pp["m_PathID"]) if tf is not None else None

    def bundle_of(self, o):
        return self.file_bundle.get(str(o.assets_file.name).lower().split("/")[-1])

    def name(self, o):
        """An asset's name, a component's GameObject's name, a shader's name."""
        if o is None:
            return None
        t = self.tree(o)
        if o.type.name == "Shader":
            return t.get("m_ParsedForm", {}).get("m_Name") or t.get("m_Name")
        if t.get("m_Name"):
            return t["m_Name"]
        if "m_GameObject" in t:
            return self.name(self.obj(o, t["m_GameObject"]))
        return None

    def cls(self, mb, t):
        """A MonoBehaviour's class name (its script may sit in another loaded bundle)."""
        s = t.get("m_Script") or {}
        if s.get("m_FileID") == 0:
            return self.scripts.get((mb.assets_file, s.get("m_PathID")))
        so = self.obj(mb, s)
        return self.tree(so).get("m_ClassName") if so is not None else None

    def components(self, go):
        """(class name, object, type tree) for each component of a GameObject object."""
        for c in self.tree(go).get("m_Component", []):
            co = self.obj(go, c["component"])
            if co is None:
                continue
            t = self.tree(co)
            yield (self.cls(co, t) if co.type.name == "MonoBehaviour" else co.type.name), co, t

    def transform(self, go):
        for cname, co, t in self.components(go):
            if cname in ("Transform", "RectTransform"):
                return co, t
        return None, None

    def children(self, go):
        tr, t = self.transform(go)
        if tr is None:
            return
        for ch in t.get("m_Children", []):
            cho = self.obj(tr, ch)
            if cho is None:
                continue
            cgo = self.obj(cho, self.tree(cho)["m_GameObject"])
            if cgo is not None:
                yield cgo

    def is_root(self, go):
        tr, t = self.transform(go)
        return tr is None or not (t.get("m_Father") or {}).get("m_PathID")

    def roots(self, bundle_file):
        """Root GameObject objects of a bundle, by name (the first of a name wins; duplicates are reported)."""
        out, dupes = {}, set()
        for cab, f in self.files.items():
            if self.file_bundle.get(cab) != bundle_file:
                continue
            for o in f.objects.values():
                if o.type.name != "GameObject" or not self.is_root(o):
                    continue
                n = self.tree(o).get("m_Name")
                if n in out:
                    dupes.add(n)
                else:
                    out[n] = o
        return out, dupes

    def plain(self, o, v):
        """A type tree with object references replaced by names and Unity's own bookkeeping dropped."""
        if isinstance(v, dict):
            if set(v) == {"m_FileID", "m_PathID"}:
                return self.name(self.obj(o, v)) if v["m_PathID"] else None
            return {k: self.plain(o, x) for k, x in v.items()
                    if k not in ("m_GameObject", "m_Enabled", "m_Script", "m_Name", "m_EditorHideFlags", "m_EditorClassIdentifier")}
        if isinstance(v, list):
            return [self.plain(o, x) for x in v]
        return v


# ---------------------------------------------------------------- the catalog, for MaterialLoader references

def _loader_table(gh, guids):
    """{GUID: (material name, bundle file)} for Addressables asset GUIDs, from catalog.json; a GUID the catalog lacks
    is left out."""
    ids, buckets, entries = gh_model._catalog(gh)
    index = {k: ents for k, ents in buckets if isinstance(k, str)}
    out = {}
    for guid in guids:
        ents = index.get(guid)
        if not ents:
            continue
        entry = entries[ents[0]]
        name = buckets[entry[5]][0] if 0 <= entry[5] < len(buckets) else None
        bundle = None
        if 0 <= entry[2] < len(buckets):
            for dep in buckets[entry[2]][1]:
                f = Path(ids[entries[dep][0]].replace(RUNTIME_PATH, "")).name
                if f.endswith(".bundle") and "shader" not in f:
                    bundle = f
                    break
        if name and bundle:
            out[guid] = (name, bundle)
    return out


def _loader_guids(world, hero):
    """Every material GUID the hero bundle's MaterialLoader scripts name."""
    guids = set()
    for cab, f in world.files.items():
        if world.file_bundle.get(cab) != hero:
            continue
        for o in f.objects.values():
            if o.type.name != "MonoBehaviour":
                continue
            t = world.tree(o)
            if "LoadersData" not in t or world.cls(o, t) != "MaterialLoader":
                continue
            for ld in t["LoadersData"]:
                for ref in ld.get("MaterialReferences", []):
                    if ref.get("m_AssetGUID"):
                        guids.add(ref["m_AssetGUID"])
    return guids


# ---------------------------------------------------------------- materials

def _material_spec(world, mo, hero):
    t = world.tree(mo)
    sp = t.get("m_SavedProperties", {})
    textures = {}
    for k, v in _pairs(sp.get("m_TexEnvs")):
        if (v.get("m_Texture") or {}).get("m_PathID"):
            textures[k] = world.name(world.obj(mo, v["m_Texture"]))
    keywords = (t.get("m_ShaderKeywords") or "").split() or [k for k in (t.get("m_ValidKeywords") or []) if k]
    bundle = world.bundle_of(mo)
    return {
        "shader": world.name(world.obj(mo, t.get("m_Shader"))),
        "bundle": None if bundle == hero else bundle,
        "floats": {k: round(v, 5) for k, v in _pairs(sp.get("m_Floats"))},
        "colors": {k: [round(v[c], 5) for c in "rgba"] for k, v in _pairs(sp.get("m_Colors"))},
        "textures": textures,
        "keywords": keywords,
        "queue": t.get("m_CustomRenderQueue", -1),
    }


class Collector:
    """Gathers material specs and the bundles to ship while effects are described."""

    def __init__(self, world, hero, warn):
        self.world, self.hero, self.warn = world, hero, warn
        self.materials, self.shipped = {}, set()

    def material(self, o, pp):
        """Record the material a PPtr points at; its name, or None when it can't be reached."""
        mo = self.world.obj(o, pp)
        if mo is None:
            if pp and pp.get("m_PathID"):
                self.warn("an effect material sits in a bundle that was not loaded; that slot stays empty")
            return None
        return self.material_object(mo)

    def material_object(self, mo):
        name = self.world.name(mo)
        if name not in self.materials:
            spec = _material_spec(self.world, mo, self.hero)
            if spec["shader"] is None:
                self.warn(f"material {name}: its shader is in a bundle that was not loaded")
            self.materials[name] = spec
            if spec["bundle"]:
                self.shipped.add(spec["bundle"])
        return name

    def material_named(self, name, bundle):
        """A material by name from a (loaded) bundle: for MaterialLoader references."""
        for cab, f in self.world.files.items():
            if self.world.file_bundle.get(cab) != bundle:
                continue
            for o in f.objects.values():
                if o.type.name == "Material" and self.world.tree(o).get("m_Name") == name:
                    return self.material_object(o)
        self.warn(f"material {name} not found in {bundle}")
        return None


# ---------------------------------------------------------------- one effect prefab

def describe(world, root, collector, loader_table):
    """The prefab's objects that need the plugin's attention: renderers (material names), loader-fed renderers, and
    the carried scripts' fields; keyed by path under the prefab ("" for the root)."""
    paths, order = {}, []

    def walk(go, path):
        paths[go.path_id] = path
        order.append((go, path))
        for ch in world.children(go):
            n = world.tree(ch).get("m_Name") or "?"
            walk(ch, f"{path}/{n}" if path else n)

    walk(root, "")
    objects = {}

    def rec(path):
        return objects.setdefault(path, {})

    for go, path in order:
        for cname, co, t in world.components(go):
            if cname in RENDERERS:
                r = rec(path)
                r["materials"] = [collector.material(co, pp) for pp in t.get("m_Materials", [])]
                tm = t.get("m_TrailMaterial")
                if tm and tm.get("m_PathID"):
                    r["trail_material"] = collector.material(co, tm)
            elif cname in CARRIED:
                rec(path).setdefault("components", []).append({"type": cname, "fields": world.plain(co, t)})
            elif cname == "MaterialLoader":
                for ld in t.get("LoadersData", []):
                    ro = world.obj(co, ld.get("Renderer"))
                    if ro is None:
                        continue
                    rgo = world.obj(ro, world.tree(ro)["m_GameObject"])
                    rpath = paths.get(rgo.path_id) if rgo is not None else None
                    if rpath is None:
                        continue
                    names = []
                    for ref in ld.get("MaterialReferences", []):
                        hit = loader_table.get(ref.get("m_AssetGUID"))
                        if hit:
                            names.append(collector.material_named(*hit))
                        else:
                            collector.warn(f"{root_name(world, root)}/{rpath}: material {ref.get('m_AssetGUID')} is not in the catalog")
                    rec(rpath)["loaded"] = {"materials": names, "keep": bool(ld.get("IsSaveExistedMaterials"))}
    return {"objects": {p: v for p, v in objects.items() if v}}


def root_name(world, root):
    return world.tree(root).get("m_Name")


def _effects_in(play):
    for c in play.get("caster") or []:
        yield c["effect"]
    for key in HEX_LISTS:
        for x in (play.get(key) or {}).get("effects") or []:
            yield x["effect"]


def _effect_sound(effects, play):
    """The audio event a play's caster effects carry (an SFXOnEnable script on any of their objects), or None."""
    for c in play.get("caster") or []:
        for o in (effects.get(c["effect"]) or {}).get("objects", {}).values():
            for comp in o.get("components", []):
                if comp.get("type") == "SFXOnEnable" and comp.get("fields", {}).get("m_AudioEvent"):
                    return comp["fields"]["m_AudioEvent"]
    return None


def _assign_sounds(anim, effects):
    """Gloomhaven plays some of a class's sounds from the effect prefab (a sound script on the Brute's roar and heal
    effects), not from the animator state. Give those states the event, so the plugin plays it the way it plays every
    other state sound (GHModel), and the plugin's copy of the effect's sound script steps aside (GHVfx skips the script
    for an event the state plays itself). Returns how many states gained a sound."""
    n = 0
    for timing in anim["states"].values():
        if not timing.get("audio") and timing.get("effects"):
            event = _effect_sound(effects, timing["effects"])
            if event:
                timing["audio"] = event
                n += 1
    for lend in anim.get("lent_states", {}).values():
        if not lend.get("audio") and lend.get("effects"):
            event = _effect_sound(effects, lend["effects"])
            if event:
                lend["audio"] = event
                n += 1
    gh_audio = anim.setdefault("gh_audio", {})
    for state, play in anim.get("gh_effects", {}).items():
        if not gh_audio.get(state):
            event = _effect_sound(effects, play)
            if event:
                gh_audio[state] = event
                n += 1
    return n


# ---------------------------------------------------------------- the pack build step

def export(gh, cls, out, entry, warn=print, wanted=None):
    """Describe the effects the class's states play (entry["animation"]) and copy the bundles their materials need
    into <out>/bundles; returns the pack.json additions (empty when the states play none). <wanted>: the animation
    names the class's cards ask for; Gloomhaven-only states no card names are dropped from the entry (their effects
    would only add bundles)."""
    anim = entry["animation"]
    if wanted is not None:
        anim["gh_effects"] = {k: v for k, v in anim.get("gh_effects", {}).items() if k in wanted}
    plays = ([t["effects"] for t in anim["states"].values() if t.get("effects")] + list(anim.get("gh_effects", {}).values())
             + [v["effects"] for v in anim.get("lent_states", {}).values() if v.get("effects")])
    names = sorted({e for p in plays for e in _effects_in(p)})
    if not names:
        return {}
    hero = entry["bundle"]
    deps = gh_model.bundles_for(gh, cls)
    world = World(gh, deps)
    loader_table = _loader_table(gh, _loader_guids(world, hero))
    extra = sorted({b for _, b in loader_table.values()} - set(deps))
    if extra:
        world = World(gh, deps + extra)
    roots, dupes = world.roots(hero)
    collector = Collector(world, hero, lambda m: warn(f"{cls} effects: {m}"))
    effects = {}
    for name in names:
        root = roots.get(name)
        if root is None:
            warn(f"{cls} effects: {name} is not in {hero}; the Drifter's effect stays")
            continue
        if name in dupes:
            warn(f"{cls} effects: {hero} holds more than one prefab named {name}; the first is used")
        effects[name] = describe(world, root, collector, loader_table)
    sounds = _assign_sounds(anim, effects)
    if sounds:
        print(f"{cls}: {sounds} state sounds taken from the effects' own sound scripts")
    bundles = []
    for b in sorted(collector.shipped):
        src = gh / BUNDLES / b
        dst = out / "bundles" / b
        dst.parent.mkdir(parents=True, exist_ok=True)
        if not dst.exists() or dst.stat().st_size != src.stat().st_size:
            shutil.copyfile(src, dst)
        inner = next((Path(k).name for k in world.env.files if Path(k).name == b), b)
        name = None
        for cab, f in world.files.items():
            if world.file_bundle.get(cab) == b:
                for o in f.objects.values():
                    if o.type.name == "AssetBundle":
                        name = world.tree(o).get("m_Name")
                        break
        bundles.append({"file": inner, "name": name, "size": src.stat().st_size,
                        "sha256": hashlib.sha256(src.read_bytes()).hexdigest()})
    entry["not_shipped"] = [n for n in entry.get("not_shipped", []) if n not in collector.shipped]
    return {"effects": effects, "effect_materials": collector.materials, "effect_bundles": bundles}
