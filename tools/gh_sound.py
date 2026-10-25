"""A ported Gloomhaven class's own sounds, from the player's OWN Gloomhaven install. Used by gh_port.py (needs UnityPy,
fmod_toolkit, numpy and soundfile: pip install UnityPy soundfile).

Both games play sounds through the same audio toolkit (ClockStone's AudioController): an animator state's behaviour
names an audio event ("PlaySound_BruteStandardMeleeAttack"), the controller plays one of that event's clips. Frosthaven
has none of Gloomhaven's events, so the class's clips are exported here and the plugin (GHSound.cs) registers them
under their Gloomhaven event names at runtime; gh_model.py records which event each animator state plays.

Gloomhaven's event -> clip table (its AudioController, in GH_Data/resources.assets) is stored without type information;
it can be read with a type tree generated from the game's assemblies (UnityPy's TypeTreeGenerator), which is how the
volumes noted at LEVELLING were checked, but the events are matched to the clips by name, per class (SOUNDS). The clips
are written as mono 16-bit WAV at 32 kHz (sounds\\<Class>\\<clip>.wav): small, and read by the plugin synchronously,
with no decoder.

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

# LEVELLING. Gloomhaven's own table plays the Brute's attack events at item volume 1.0 in its Brute_SFX category (0.76;
# heal 0.84, cast positive 0.71, on-hit 0.60), i.e. the clips at their natural levels, which sit anywhere between peak
# 0.35 (the shield power-up) and 0.8. Played like that at one volume in Frosthaven, the quiet ones (the roars, the
# shield power-up) were lost under the game's own hit and step sounds, so every clip is levelled. Levelling to one PEAK
# left the dull, sustained sounds (the heavy swing, the quake, the throw) 2-4 dB under the bright ones (the roars, the
# shield), which the in-game comparison with Gloomhaven confirmed; so the clips are levelled by loudness instead:
# K-weighted (ITU-R BS.1770) over the loudest 400 ms, to TARGET_LUFS, with a look-ahead limiter holding the peaks
# under CEILING. The gain is bounded (GAIN_DB) so a near-silent clip is never blown up.
TARGET_LUFS = -12.5
CEILING = 0.95
GAIN_DB = (-12.0, 12.0)


def _biquad(x, b0, b1, b2, a1, a2):
    """A direct-form biquad over a float32 array (no scipy here)."""
    out, x1, x2, y1, y2 = [], 0.0, 0.0, 0.0, 0.0
    for v in x.tolist():
        y = b0 * v + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, v, y1, y
        out.append(y)
    return np.asarray(out, dtype="float32")


def _k_weighting(rate):
    """The two BS.1770 pre-filters for <rate>: a +4 dB high shelf (the head) and a 38 Hz high-pass (RLB)."""
    import math
    A, w0, q = 10 ** (3.99 / 40), 2 * math.pi * 1681.97 / rate, 0.7072
    cw, sw = math.cos(w0), math.sin(w0)
    alpha = sw / (2 * q)
    a0 = (A + 1) - (A - 1) * cw + 2 * math.sqrt(A) * alpha
    shelf = (A * ((A + 1) + (A - 1) * cw + 2 * math.sqrt(A) * alpha) / a0, -2 * A * ((A - 1) + (A + 1) * cw) / a0,
             A * ((A + 1) + (A - 1) * cw - 2 * math.sqrt(A) * alpha) / a0,
             2 * ((A - 1) - (A + 1) * cw) / a0, ((A + 1) - (A - 1) * cw - 2 * math.sqrt(A) * alpha) / a0)
    w0, q = 2 * math.pi * 38.13 / rate, 0.5003
    cw, sw = math.cos(w0), math.sin(w0)
    alpha = sw / (2 * q)
    a0 = 1 + alpha
    highpass = ((1 + cw) / 2 / a0, -(1 + cw) / a0, (1 + cw) / 2 / a0, -2 * cw / a0, (1 - alpha) / a0)
    return shelf, highpass


def loudness(mono, rate=RATE):
    """K-weighted loudness of the loudest 400 ms of <mono>, in LUFS (-99 for silence)."""
    import math
    shelf, highpass = _k_weighting(rate)
    k = _biquad(_biquad(mono, *shelf), *highpass)
    sq = k.astype("float64") ** 2
    win = int(0.4 * rate)
    if len(sq) <= win:
        ms = float(sq.mean()) if len(sq) else 0.0
    else:
        c = np.concatenate(([0.0], np.cumsum(sq)))
        ms = float((c[win:] - c[:-win]).max()) / win
    return -0.691 + 10 * math.log10(ms) if ms > 0 else -99.0


def _limit(x, ceiling=CEILING, lookahead=0.003, release=0.08, rate=RATE):
    """A look-ahead peak limiter: the gain is already down when a peak arrives and recovers over <release> seconds."""
    need = np.minimum(1.0, ceiling / np.maximum(np.abs(x), 1e-9))
    env = need.copy()
    for k in range(1, max(1, int(lookahead * rate)) + 1):   # the minimum over the next <lookahead> samples
        env[:-k] = np.minimum(env[:-k], need[k:])
    r = 1.0 - np.exp(-1.0 / (release * rate))
    gain, g = [], 1.0
    for e in env.tolist():
        g = e if e < g else g + (1.0 - g) * r
        gain.append(g)
    return np.clip(x * np.asarray(gain, dtype="float32"), -1.0, 1.0).astype("float32")


def _mono(wav_bytes):
    data, rate = soundfile.read(io.BytesIO(wav_bytes), dtype="float32", always_2d=True)
    mono = data.mean(axis=1)
    if rate != RATE:
        n = int(round(len(mono) * RATE / rate))
        mono = np.interp(np.linspace(0, len(mono) - 1, n), np.arange(len(mono)), mono).astype("float32")
    if len(mono) == 0:
        return mono
    gain_db = min(GAIN_DB[1], max(GAIN_DB[0], TARGET_LUFS - loudness(mono)))
    return _limit(mono * np.float32(10 ** (gain_db / 20)))


def events_of(animation):
    """The sound events a class's animator states name, from gh_model's animation table: the states' own, the
    Gloomhaven-only states' and the lent states'."""
    animation = animation or {}
    events = {t["audio"] for t in animation.get("states", {}).values() if t.get("audio")}
    events.update(v for v in animation.get("gh_audio", {}).values() if v)
    events.update(l["audio"] for l in animation.get("lent_states", {}).values() if l.get("audio"))
    return sorted(events)


def table_for(gh, cls, events):
    """{event: [clip names]} for the class: the hand-typed SOUNDS table when there is one, else Gloomhaven's own audio
    table (gh_audio.py) for the events the class's animations name."""
    if cls in SOUNDS:
        return SOUNDS[cls]
    if not events:
        return {}
    import gh_audio
    items = gh_audio.read_items(gh)
    table, missing = {}, []
    for event in events:
        item = items.get(event)
        if item and item["clips"]:
            table[event] = [name for name, _ in item["clips"]]
        else:
            missing.append(event)
    if missing:
        print(f"warning: {cls}: sound events not in Gloomhaven's audio table: {', '.join(missing)}")
    return table


def export(gh, cls, out, events=None):
    """Write the class's clips to <out>/sounds/<cls>/ and return {event: [file names]} (empty without a table).
    <events>: the events the class's animations name (events_of); without them only a SOUNDS table serves."""
    table = table_for(gh, cls, events)
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
