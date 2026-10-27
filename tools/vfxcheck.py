"""Static check of a pack's ability-effect materials: every shader an effect material names must be one Frosthaven
can draw, either as is (tools/fh_shaders.txt: the 57 shaders in its misc_shaders bundles, plus Unity's built-in
particle shaders and the render pipeline's lit shaders, which Shader.Find sees) or through a stand-in the plugin
substitutes (GHVfx.ShaderFor; the table below mirrors it and must be kept in step). A shader outside both draws as a
magenta block in game (the Cragheart's Avalanche rock, Group 2 pass 2026-10-27) and is reported here instead.

    python tools/vfxcheck.py [mods/gloomhaven]      -> one line per unresolvable material, exit 1 if any
Run by packcheck.py as part of the pack's checks.
"""

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FH_SHADERS = {l.strip() for l in (ROOT / "tools" / "fh_shaders.txt").read_text(encoding="utf-8").splitlines() if l.strip()}
# shaders that are not in the bundles but Shader.Find answers (Unity built-ins and the pipeline's own)
BUILT_IN = {"Legacy Shaders/Particles/Additive", "Legacy Shaders/Particles/Alpha Blended", "Legacy Shaders/Particles/Alpha Blended Premultiply",
            "Particles/Standard Unlit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Lit"}
# GHVfx.ShaderFor's substitutions (Gloomhaven shader -> what the plugin draws it with)
STAND_INS = {
    "KriptoFX/RFX4/Particle": "Legacy Shaders/Particles/Additive",
    "Particles/Standard Surface": "Particles/Standard Unlit",
    "VFX/ParticleMasterUnlitAdd_Shd": "Particles/Standard Unlit",
    "VFX/SimpleInvisibleShd": "Particles/Standard Unlit",
    "VFX/LitDissolveShd": "Universal Render Pipeline/Simple Lit",
    "Amp_Basic_Prop_Shader": "Amp_Basic_N_MRAO",
    "Amp_Char_Shader_2Side": "Amp_Char_Shader_Universal",
    "OmniDecalSimple_Shd": "Shader Graphs/URPProjectionDecal_FX",
    # Jaws of the Lion effects (2026-10-27): Unity's built-in Standard on blood splats and black mattes, the low-end
    # variants of the same two Gloomhaven surface shaders on the mech bomb and the rubble
    "Standard": "Universal Render Pipeline/Lit",
    "Amp_Low/Amp_CharShader_Low": "Amp_Char_Shader_Universal",
    "Amp_Char_Shader": "Amp_Char_Shader_Universal",   # props inside effects (the Tinkerer's grenade, a coin pile, a bear trap)
    "Amp_Low/Amp_Basic_Prop_Shader_Low": "Amp_Basic_N_MRAO",
    "CragHeartSummonShaftShd": "LightShaftShd",   # its own copy draws a solid pale cylinder (2026-10-27)
}


def check(pack):
    """[(class, shader, material, effects)] for every effect material whose shader Frosthaven can't draw."""
    manifest = json.loads((Path(pack) / "pack.json").read_text(encoding="utf-8"))
    out = []
    for cls, model in (manifest.get("model") or {}).items():
        users = {}
        for eff, e in (model.get("effects") or {}).items():
            for o in (e.get("objects") or {}).values():
                for mat in (o.get("materials") or []) + ((o.get("loaded") or {}).get("materials") or []):
                    if mat:
                        users.setdefault(mat, set()).add(eff)
        for name, spec in (model.get("effect_materials") or {}).items():
            shader = str(spec.get("shader"))
            drawn = STAND_INS.get(shader, shader)
            if drawn in FH_SHADERS or drawn in BUILT_IN:
                continue
            out.append((cls, shader, name, sorted(users.get(name, ()))))
    return out


def main():
    pack = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "mods" / "gloomhaven"
    bad = check(pack)
    for cls, shader, mat, effs in bad:
        print(f"PROBLEM {cls}: material {mat} on shader {shader!r}: Frosthaven has no such shader and the plugin no stand-in (effects: {', '.join(effs) or '?'})")
    print(f"vfxcheck: {len(bad)} material(s) Frosthaven can't draw")
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
