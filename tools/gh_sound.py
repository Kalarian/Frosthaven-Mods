"""A ported Gloomhaven class's own sounds, from the player's OWN Gloomhaven install. Used by gh_port.py (needs UnityPy,
fmod_toolkit, numpy and soundfile: pip install UnityPy soundfile).

Both games play sounds through the same audio toolkit (ClockStone's AudioController): an animator state's behaviour
names an audio event ("PlaySound_BruteStandardMeleeAttack"), the controller plays one of that event's clips. Frosthaven
has none of Gloomhaven's events, so the class's clips are exported here and the plugin (GHSound.cs) registers them
under their Gloomhaven event names at runtime; gh_model.py records which event each animator state plays.

Gloomhaven's event -> clip table (its AudioController) is stored without type information and can't be read, so the
events are matched to the clips by name, per class (SOUNDS). The clips are written as mono 16-bit WAV at 32 kHz
(sounds\\<Class>\\<clip>.wav): small, and read by the plugin synchronously, with no decoder.

    sounds.json   {"<event>": ["<clip>.wav", ...]}  (several clips: the toolkit picks one at random)
"""

import io
import json
import sys
from pathlib import Path

try:
    import numpy as np
    import soundfile
    import UnityPy
except ImportError as e:  # pragma: no cover
    raise ImportError("gh_sound needs UnityPy, fmod_toolkit, numpy and soundfile (pip install UnityPy soundfile)") from e

RESOURCES = Path("GH_Data") / "resources.assets"
RATE = 32000

# event -> Gloomhaven clip names. The events are the ones the class's animator states name (gh_model.py), plus
# STATE_EVENTS for states that name none.
SOUNDS = {
    "Brute": {
        "PlaySound_BruteStandardMeleeAttack": ["Brute_Melee_Attackv2"],
        "PlaySound_BruteOverwhelmingAssault": ["Brute_Overwhelming_Assault_v2"],
        "PlaySound_BruteEarthquakeHex": ["Brute_Earthquake_-_new_timing"],
        "PlaySound_BruteStandardRangedAttack_Throw": ["Gloomhaven_Brute_Ranged_Weapon_Trail_THROW_FINAL3"],
        "PlaySound_BruteCastPositive": ["Brute_Cast_Positive"],
        "PlaySound_BruteCastHeal": ["BruteCastHeal"],
        "PlaySound_BruteShieldPowerUp": ["Brute_Shield_PowerUp_Final", "Brute_Shield_PowerUp__Variation_2",
                                         "Brute_Shield_PowerUp__Variation_3", "Brute_Shield_PowerUp__Variation_4"],
        "PlaySound_BruteShoutRadialAttack": ["Brute_Shout_Radial_Attack_v2"],
        "PlaySound_BruteShoutAttack": ["Brute_Shout_Attack_v2"],
        "PlaySound_BruteUseItem": ["Brute_ItemUse_no_grunt"],
        "PlaySound_BruteOnHit": ["Brute_On_Hit_V1", "Brute_On_Hit_V2"],
        "PlaySound_BruteOnExhausted": ["Brute_On_Exhausted_v2"],
    },
}

# Gloomhaven states whose behaviours name no sound, by class: state -> event (from SOUNDS). The Brute's targeted
# shout is not here: its roar is on the effect itself (SFXOnEnable on P_Brute_Shout), which the plugin re-adds.
STATE_EVENTS = {}

# Every clip is levelled to this peak. Gloomhaven's per-event volume settings are in its unreadable audio table, and
# its files sit anywhere between 0.35 (the shield power-up) and 0.8; played as they are at one volume, the quiet ones
# (the roars, the shield power-up) were lost under the game's own hit and step sounds.
PEAK = 0.9


def _mono(wav_bytes):
    data, rate = soundfile.read(io.BytesIO(wav_bytes), dtype="float32", always_2d=True)
    mono = data.mean(axis=1)
    if rate != RATE:
        n = int(round(len(mono) * RATE / rate))
        mono = np.interp(np.linspace(0, len(mono) - 1, n), np.arange(len(mono)), mono).astype("float32")
    peak = float(np.abs(mono).max()) if len(mono) else 0.0
    if peak > 0.0:
        mono *= PEAK / peak
    return mono


def export(gh, cls, out):
    """Write the class's clips to <out>/sounds/<cls>/ and return {event: [file names]} (empty without a table)."""
    table = SOUNDS.get(cls)
    if not table:
        return {}
    wanted = {c for clips in table.values() for c in clips}
    env = UnityPy.load(str(Path(gh) / RESOURCES))
    folder = Path(out) / "sounds" / cls
    folder.mkdir(parents=True, exist_ok=True)
    for old in folder.glob("*.wav"):
        old.unlink()
    written = set()
    for o in env.objects:
        if o.type.name != "AudioClip":
            continue
        name = o.read_typetree()["m_Name"]
        if name not in wanted or name in written:
            continue
        for _, wav in o.read().samples.items():
            soundfile.write(folder / f"{name}.wav", _mono(wav), RATE, subtype="PCM_16")
            written.add(name)
            break
    missing = sorted(wanted - written)
    if missing:
        print(f"warning: {cls}: Gloomhaven clips not found: {', '.join(missing)}")
    events = {ev: [f"{c}.wav" for c in clips if c in written] for ev, clips in table.items()}
    events = {ev: files for ev, files in events.items() if files}
    (folder / "sounds.json").write_text(json.dumps(events, indent=1) + "\n", encoding="utf-8")
    return events


if __name__ == "__main__":
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import gh_port  # noqa: E402

    gh_dir, _ = gh_port.find_gloomhaven()
    for c in sys.argv[1:] or ["Brute"]:
        print(json.dumps(export(gh_dir, c, Path("scratch_sound")), indent=1))
