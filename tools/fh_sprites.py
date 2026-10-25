"""Frosthaven's inline text icons (TextMesh Pro sprites) for ported text, and a check against the game's real list.

Card and perk text draws icons with <sprite name=X>. Frosthaven's ability cards read X from TMP_AbilityCardSprites
(misc_gui bundle; 284 sprites, falling back to TMP_OutpostSpriteAtlas and the controller sprites). A name that isn't
there is printed as raw text ("<sprite name=Aura>: Affect self and all adjacent allies"). Gloomhaven names a few
icons Frosthaven doesn't have or names differently, and writes element icons the way its own cards drew them; this
module rewrites ported text the way Frosthaven's own cards write it:

  - item slots: Gloomhaven's InvSmallItem / InvOneHand / InvTwoHands are Frosthaven's SmallItem / OneHandItem /
    TwoHandsItem (and InvHead / InvBody / InvLegs: HeadItem / BodyItem / FeetItem);
  - Gloomhaven's Aura icon (an effect around the character) has no Frosthaven counterpart: Frosthaven's cards say it
    in words ("Affect all adjacent allies"), which the ported text already does after the icon, so the icon and its
    colon go;
  - elements: Frosthaven's cards draw an element being infused as CreateX (the element with a plus), a consumed one
    as ConsumeX, and the bare element X only for its state ("when Light is not Strong"). Gloomhaven writes the bare
    element for infusions too (a consume's reward "+1 Attack, Light", "infuse Light"), so a bare element becomes
    CreateX unless the words after it are "is"/"are" (a state).

check() lists the sprite names a text uses that Frosthaven doesn't have, from the game's own sprite assets, so a
port warns instead of shipping raw markup. Used by gh_port.py on every converted file and string.
"""

import re
from functools import lru_cache
from pathlib import Path

ELEMENTS = ("Fire", "Ice", "Air", "Earth", "Light", "Dark")
RENAMED = {
    "InvSmallItem": "SmallItem", "InvOneHand": "OneHandItem", "InvTwoHands": "TwoHandsItem",
    "InvHead": "HeadItem", "InvBody": "BodyItem", "InvLegs": "FeetItem",
}
SPRITE = re.compile(r'<sprite name="?([^">]+)"?>')
AURA = re.compile(r'<sprite name="?Aura"?>\s*:?\s*')
BARE_ELEMENT = re.compile(r'<sprite name="?(' + "|".join(ELEMENTS) + r')"?>((?:\s*</?[a-z]+[^>]*>)*)(?!(?:\s*</?[a-z]+[^>]*>)*\s*(?:is|are)\b)')


def to_fh(text):
    """Ported text with Frosthaven's icons (see the module notes)."""
    if "<sprite" not in text:
        return text
    text = SPRITE.sub(lambda m: f"<sprite name={RENAMED[m.group(1)]}>" if m.group(1) in RENAMED else m.group(0), text)
    text = AURA.sub("", text)
    return BARE_ELEMENT.sub(lambda m: f"<sprite name=Create{m.group(1)}>{m.group(2)}", text)


@lru_cache(maxsize=1)
def fh_names(game=None):
    """Every sprite name Frosthaven's ability-card text can draw: TMP_AbilityCardSprites and its fallbacks, read from
    the game's misc_gui bundle. Empty when the game or UnityPy isn't available (then nothing is checked)."""
    try:
        import UnityPy
        import fh_mods
    except ImportError:
        return frozenset()
    bundle = Path(game or fh_mods.GAME) / "Frosthaven_Data" / "StreamingAssets" / "AssetBundles" / "Base" / "misc_gui"
    if not bundle.exists():
        return frozenset()
    names = set()
    for o in UnityPy.load(str(bundle)).objects:
        if o.type.name != "MonoBehaviour":
            continue
        try:
            mb = o.read(check_read=False)
            if mb.m_Script.read().m_ClassName != "TMP_SpriteAsset":
                continue
            names.update(c.get("m_Name") for c in o.read_typetree().get("m_SpriteCharacterTable", []))
        except Exception:
            continue
    return frozenset(n for n in names if n)


def check(text):
    """The sprite names in the text that Frosthaven can't draw (empty when the game's list isn't available)."""
    known = fh_names()
    if not known:
        return []
    return sorted({n for n in SPRITE.findall(text) if n not in known})
