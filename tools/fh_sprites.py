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
    CreateX unless the words after it are "is"/"are" (a state). Element icons drop Gloomhaven's enlarging
    (<size=150%><voffset=..>): Frosthaven draws them at the text's size. (A standalone infusion, "- Infuse: Light" in
    a layout, is the generator's element icon on both games' cards; an infusion inside a consume's reward is this text
    icon. Two different pictures for the two cases is Frosthaven's own design.)
  - recover: Frosthaven's text puts RecoverIcon in place of the word ("<icon> four lost cards"); Gloomhaven writes
    "Recover <icon>";
  - wording: Gloomhaven digital's "burned" cards are Frosthaven's "lost" cards.

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


ELEMENT_FAMILY = r'(?:Create|Consume)?(?:' + "|".join(ELEMENTS) + r'|Any|Wild)'
# Gloomhaven enlarges and nudges element icons in text (<size=150%><voffset=-1.5>...); Frosthaven's cards draw them at
# the text's own size (35 of its 36 infusions in consume text)
SIZED_ELEMENT = re.compile(r'<size=\d+%>\s*(?:<voffset=[^>]*>)?\s*(<sprite name="?' + ELEMENT_FAMILY + r'"?>)\s*(?:</voffset>)?\s*</size>')
# Gloomhaven writes "Recover [icon]"; Frosthaven's text uses the icon in place of the word ("[icon] four lost cards")
RECOVER = re.compile(r'(?:<nobr>)?[Rr]ecover\s*<sprite name="?Recover"?>(?:</nobr>)?')
# Gloomhaven digital's "burned" cards are Frosthaven's "lost" cards (its text never says burned)
BURNED = re.compile(r'\bburned(?=\s+cards?\b|\s+to\b)|(?<=\bbe )burned\b')


def to_fh(text):
    """Ported text with Frosthaven's icons and card wording (see the module notes)."""
    text = BURNED.sub("lost", text)
    if "<sprite" not in text:
        return text
    text = SPRITE.sub(lambda m: f"<sprite name={RENAMED[m.group(1)]}>" if m.group(1) in RENAMED else m.group(0), text)
    text = AURA.sub("", text)
    text = RECOVER.sub("<sprite name=RecoverIcon>", text)
    text = re.sub(r'<sprite name="?Recover"?>', "<sprite name=RecoverIcon>", text)
    text = BARE_ELEMENT.sub(lambda m: f"<sprite name=Create{m.group(1)}>{m.group(2)}", text)
    return SIZED_ELEMENT.sub(r"\1", text)


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


ICON_FIELDS = re.compile(r'^\s*(?:Icon|OverrideIcon):\s*"?\'?([A-Za-z0-9_]+)|ListLayoutOverride:\s*\[[^,\]]*,\s*([A-Za-z0-9_]+)\s*\]', re.M)


@lru_cache(maxsize=1)
def active_icons():
    """Frosthaven's active-ability icon names (ActiveAbilityDataSO in misc_globalsettings, saved in
    tools/data/fh_active_icons.json): what an active bonus's Icon, ListLayoutOverride icon and OverrideIcon can name.
    A perk toggle naming anything else drew a white box in the activate bar."""
    p = Path(__file__).resolve().parent / "data" / "fh_active_icons.json"
    import json
    return frozenset(json.loads(p.read_text(encoding="utf-8"))) if p.exists() else frozenset()


def check_icons(text):
    """The active-bonus icon names in a ruleset file that Frosthaven doesn't have (empty when the list is missing)."""
    known = active_icons()
    if not known:
        return []
    return sorted({n for m in ICON_FIELDS.finditer(text) for n in m.groups() if n and n not in known})


def check(text):
    """The sprite names in the text that Frosthaven can't draw (empty when the game's list isn't available)."""
    known = fh_names()
    if not known:
        return []
    return sorted({n for n in SPRITE.findall(text) if n not in known})
