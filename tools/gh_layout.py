"""Rewrites a Gloomhaven ability card's Layout section in the form Frosthaven's own cards use. Used by gh_port.py.

Both games read the same layout elements (Row, Column, Active, Consume, Infuse...), but they write a card differently:

    Gloomhaven                                     Frosthaven (e.g. Global/Characters/Drifter/AbilityCards)
    "<b><size=130%>$Attack$ <sprite name=Attack>   "<b><size=130%><sprite name=Attack> *TAttack* ..." : icon and
     *TAttack* ..." : the word, then the icon      number only
    one centred row per modifier under the         the main ability in a column with its modifiers beside it
    ability (Range, Pierce, Push, Stun...)          (ColumnWidths "20,30,30,20"): fixed ones in an AddedEffect
                                                   pill, ones that take a card value (*TAttack.Range*) in a
                                                   highlighted text row, enhanceable ones (@...@) in a plain row
    "<size=50%> " spacer rows between abilities    "- Divider:" between abilities
    "- Duration: Round" icons                      nothing: the corner badge shows round / persistent
    active-bonus icons "Shield", "Retaliate"...    "Positive_Shield", "Positive_Retaliate"... (the game finds an
                                                   unknown name by substring, so "Shield" could show any shield icon)

Why the rows matter: a value row only resolves *refs* and draws enhancement slots (@...@) as a text row; an
AddedEffect pill shows its text as is (CreateLayout.CreateAddedEffect only localises it), so only fixed text goes there.

The section is parsed with a small reader for the subset these files use (indentation blocks of "key: value" and
"- key: value" lines, values kept as written) and written back the same way; anything it doesn't recognise leaves the
card's layout as Gloomhaven wrote it, with a warning.
"""

import re

INDENT = 4

# active-bonus icon names: Gloomhaven's -> the Frosthaven icon its own cards use for the same effect
ICONS = {
    "Shield": "Positive_Shield",
    "Retaliate": "Positive_Retaliate",
    "Immune": "Positive_DamagePrevent",
    "Damage": "Positive_AttackPlus",
    "Heal": "Positive_Heal",
    "Attack": "Positive_AttackPlus",
    # Jaws of the Lion (2026-10-27): a summon's active, the Voidwarden's curse-every-turn and poison-reaction actives,
    # her control; the condition icons are Frosthaven's plain ones
    "Summon": "Positive_Summon",
    "Curse": "Icon_Curse",
    "Poison": "Icon_Poison",
    "Control": "AA_Mind_Control",
}
# Gloomhaven also names an active after its class ("Sunkeeper": the class icon); Frosthaven has no such icon for a
# ported class, so gh_port's CLASS_ICONS gives each of those an icon for what the card does. Every icon written is
# checked against Frosthaven's list (fh_sprites.check_icons): the game looks an unknown name up by substring and draws
# whatever icon comes first, or a white box when nothing matches.
# conditions are drawn larger in an AddedEffect (Frosthaven: "<size=130%><sprite name=Poison></size>")
CONDITIONS = {"Poison", "Wound", "Muddle", "Immobilize", "Disarm", "Stun", "Curse", "Bless", "Strengthen", "Invisible",
              "Regenerate", "Ward", "Brittle", "Bane", "Impair", "Rupture"}
SELF_ONLY = {"Positive_Shield", "Positive_Retaliate"}  # Frosthaven never writes "self" on these

WORD_BEFORE_ICON = re.compile(r"\$(\w+)\$ ?(?=<sprite name=\1>)")
SPACER = re.compile(r"^<size=\d+%> ?$")
MAIN = "<b><size=130%>"
FH_SELF = "$FH/target_self$"
MODIFIER_COLUMNS = '"20,30,30,20"'


class Node:
    """One line of the section: '- key: value' (dash) or 'key: value', with the deeper lines under it."""

    def __init__(self, key, value="", dash=True, children=None):
        self.key, self.value, self.dash, self.children = key, value, dash, children or []

    def text(self):
        """The value without its quotes ('' for none)."""
        v = self.value
        return v[1:-1] if len(v) >= 2 and v[0] == v[-1] == '"' else v

    def set_text(self, t):
        self.value = f'"{t}"'


class Unsupported(Exception):
    pass


def parse(lines):
    """[(indent, line)] -> [Node]; raises Unsupported on anything outside the subset."""
    root, stack = [], [(-1, None)]  # (indent, node)
    for indent, ln in lines:
        m = re.match(r"^(- )?([A-Za-z_][\w]*):(?: (.*))?$", ln.strip())
        if not m:
            raise Unsupported(ln.strip())
        node = Node(m.group(2), (m.group(3) or "").strip(), bool(m.group(1)))
        while stack[-1][0] >= indent:
            stack.pop()
        parent = stack[-1][1]
        if parent is not None and indent != stack[-1][0] + INDENT:
            raise Unsupported(f"indent of {ln.strip()!r}")
        (root if parent is None else parent.children).append(node)
        stack.append((indent, node))
    return root


def emit(nodes, depth, eol):
    out = []
    for n in nodes:
        head = " " * (depth * INDENT) + ("- " if n.dash else "") + n.key + ":" + (" " + n.value if n.value else "")
        out.append(head + eol)
        out += emit(n.children, depth + 1, eol)
    return out


# ---------------------------------------------------------------- text of one row

def strip_words(nodes):
    """'$Attack$ <sprite name=Attack>' -> '<sprite name=Attack>' in every value, and 'A $and$ B' -> 'A, B'."""
    for n in nodes:
        if n.value:
            n.value = WORD_BEFORE_ICON.sub("", n.value).replace(" $and$ ", ", ")
        strip_words(n.children)


def is_row(n, pred=None):
    return n.key == "Row" and n.value and not n.children and (pred is None or pred(n.text()))


def is_spacer(n):
    return is_row(n, lambda t: bool(SPACER.match(t)))


def is_main(n):
    return is_row(n, lambda t: t.startswith(MAIN))


def is_modifier(n):
    """A non-bold row that adds to the ability above it: an icon (range, push, stun...) or 'Self'."""
    return is_row(n, lambda t: t.startswith("<sprite name=") or t == "$Self$")


def is_duration(n):
    if n.key == "Duration":
        return True
    # Gloomhaven's right-aligned duration: Row: [ColumnWidths, Column, Column, Column: [Duration]]
    return (n.key == "Row" and not n.value and n.children
            and all(c.key in ("ColumnWidths", "Column") for c in n.children)
            and any(is_duration(g) for c in n.children for g in c.children)
            and all(is_duration(g) for c in n.children for g in c.children))


def modifier_column(mods):
    """The Column node holding an ability's modifiers, Frosthaven style (see the module notes)."""
    col, pills = Node("Column"), []

    def flush():
        if pills:
            col.children.append(Node("AddedEffect", '"' + ", ".join(pills) + '"'))
            pills.clear()

    for m in mods:
        t = m.text().strip()
        if t == "$Self$":
            pills.append(FH_SELF)
        elif "@" in t:  # an enhancement slot: needs a text row
            flush()
            col.children.append(Node("Row", '"' + t.replace("> ", ">", 1) + '"'))
        elif "*" in t:  # a card value: a text row, highlighted like a pill
            flush()
            col.children.append(Node("Row", '"<mark=#80919922><size=100%>' + t.replace("> ", ">", 1) + '</mark>"'))
        else:
            icon = re.match(r"<sprite name=(\w+)>", t)
            if icon and icon.group(1) in CONDITIONS and t == icon.group(0):
                pills.append(f"<size=130%>{t}</size>")
            else:
                pills.append(t.replace("> ", ">", 1))
    flush()
    return col


def with_modifiers(main, mods):
    return Node("Row", children=[
        Node("ColumnWidths", MODIFIER_COLUMNS), Node("Column"), Node("Column", main.value), modifier_column(mods),
        Node("Column")])


# ---------------------------------------------------------------- one card half

def convert_active(a, icons):
    fields = {c.key: c for c in a.children}
    ability = fields.get("Ability")
    icon = fields.get("Icon")
    if icon is not None:
        name = icon.text()
        new = icons.get(ability.text() if ability else "", ICONS.get(name, name))
        icon.value = new
        if new in SELF_ONLY:
            layout = fields.get("Layout")
            if layout is not None:
                layout.children = [r for r in layout.children if not is_row(r, lambda t: t == "$Self$")]
            for key in ("ListLayout", "Layout"):
                f = fields.get(key)
                if f is not None and f.value:
                    f.value = f.value.replace(", $Self$", "")


def _actives(nodes):
    """Every Active under these nodes, at any depth (rows inside columns inside rows)."""
    for n in nodes:
        if n.key == "Active":
            yield n
        yield from _actives(n.children)


# Gloomhaven's tracker layout elements. Frosthaven's layout reader still knows Song, Doom and Augment, but its GUI
# bundle (misc_gui) only carries the AugmentContainer prefab: a Song or Doom element throws "Unable to find asset"
# while the cards are generated at load (found by the system probes, 2026-10-27). Those two become a plain Active
# element (the song / doom is a persistent bonus with the ability's own layout), which is how the card then reads in
# the hand; the in-play tracker is the game's own code. Augment keeps its element, only its class icon is mapped.
TRACKERS = {
    "Song": ("SongAbilityLayout", "Positive_Attack"),
    "Doom": ("DoomAbilityLayout", "Positive_AttackPlus"),
}


def convert_trackers(items, icons):
    out = []
    for n in items:
        if n.key in TRACKERS:
            fields = {c.key: c for c in n.children}
            layout_key, default_icon = TRACKERS[n.key]
            parent = fields.get("ParentAbility")
            active = Node("Active")
            active.children.append(Node("Ability", parent.value if parent else "", dash=False))
            active.children.append(Node("Icon", icons.get(parent.text() if parent else "", default_icon), dash=False))
            for src, dst in (("ActiveBonusLayout", "ListLayout"), (layout_key, "Layout")):
                f = fields.get(src)
                if f is not None:
                    active.children.append(Node(dst, f.value, dash=False, children=list(f.children)))
            out.append(active)
        elif n.key == "Augment":
            for c in n.children:
                if c.key == "AugmentIcon":
                    c.value = icons.get(c.text(), ICONS.get(c.text(), "Positive_Attack"))
            out.append(n)
        else:
            out.append(n)
    return out


def convert_half(items, icons):
    items = convert_trackers(items, icons)
    for a in list(_actives(items)):
        convert_active(a, icons)
    if any(n.key == "ColumnWidths" for n in items):
        return items  # the half itself is split into columns (an area picture beside the ability): leave its shape

    items = [n for n in items if not is_duration(n)]
    grouped = []  # (kind, node): kind "ability" for a main row (with modifiers), "active", "consume", "other"
    i = 0
    while i < len(items):
        n = items[i]
        if is_main(n):
            mods, j = [], i + 1
            while j < len(items):
                if is_modifier(items[j]):
                    mods.append(items[j])
                    j += 1
                elif is_spacer(items[j]) and j + 1 < len(items) and is_modifier(items[j + 1]):
                    j += 1  # a gap between two modifiers of the same ability
                else:
                    break
            grouped.append(("ability", with_modifiers(n, mods) if mods else n))
            i = j
            continue
        kind = {"Active": "active", "Consume": "consume"}.get(n.key, "spacer" if is_spacer(n) else "other")
        grouped.append((kind, n))
        i += 1

    while grouped and grouped[-1][0] == "spacer":  # nothing below the last ability
        grouped.pop()
    out = []
    for k, (kind, n) in enumerate(grouped):
        if kind == "spacer":
            before = grouped[k - 1][0] if k else None
            after = grouped[k + 1][0] if k + 1 < len(grouped) else None
            if before in ("ability", "active", "consume") and after in ("ability", "active"):
                out.append(Node("Divider"))
                continue
        out.append(n)
    return out


def convert(s, icons=None, warn=print):
    """A card file's text with its Layout section in Frosthaven's form; icons = {ability name: icon} for this
    class's actives that need a specific Frosthaven icon."""
    eol = "\r\n" if "\r\n" in s else "\n"
    lines = s.splitlines()
    try:
        at = lines.index("Layout:")
    except ValueError:
        return s
    body = []
    for ln in lines[at + 1:]:
        if not ln.strip() or ln.lstrip().startswith("#"):
            continue
        if not ln.startswith(" "):
            warn("a section after Layout: layout left as it is")
            return s
        body.append((len(ln) - len(ln.lstrip(" ")), ln))
    try:
        halves = parse(body)
    except Unsupported as e:
        warn(f"layout not converted ({e})")
        return s
    for half in halves:
        if half.key not in ("Top", "Bottom") or half.dash or half.value:
            warn(f"layout not converted (unexpected {half.key!r})")
            return s
    strip_words(halves)
    for half in halves:
        half.children = convert_half(half.children, icons or {})
    return eol.join(lines[:at + 1]) + eol + "".join(emit(halves, 1, eol))
