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
    "Flying": "AA_Flying",   # Winged Congregation: gain flying (white box until 2026-10-28)
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
    # Groups 4-6 (seen in the 2026-10-27 trial port): a doom marks an enemy, the Summoner's damage prevention
    "Doom": "Positive_Attack_Curse",
    "Prevent": "Positive_DamagePrevent",
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
        # Gloomhaven's own files are not always indented by four (the Elementalist's "- Infuse: Ice" sits 23 deep):
        # anything deeper than its parent is the parent's child; the emitted layout is re-indented evenly
        if parent is not None and indent <= stack[-1][0]:
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
    "Song": ("SongAbilityLayout", "Positive_AttackPlus"),
    "Doom": ("DoomAbilityLayout", "Positive_AttackPlus"),
}


def convert_trackers(items, icons):
    out = []
    command = False
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
            if n.key == "Doom":
                # the Doom element drew its own header and discard line (Gloomhaven's CreateLayout: Doom_MarkText,
                # Doom_DiscardReminderText); a plain Active has only the card's text, and every doom card face lost
                # "Doom: mark any one enemy" (the user's manual test, 2026-10-29). The Active's Layout takes rows.
                layout = next((c for c in active.children if c.key == "Layout"), None)
                if layout is not None:
                    rows = [Node("Row", layout.value)] if layout.value else list(layout.children)
                    layout.value = ""
                    layout.children = ([Node("Row", '"$Doom_MarkText$"')] + rows
                                       + [Node("Row", '"<size=70%>$Doom_DiscardReminderText$"')])
            out.append(active)
        elif n.key == "Command":
            # Gloomhaven's "Command" banner (the Beast Tyrant orders the bear: the half is a ControlActor on the
            # companion). Frosthaven's layout reader has no such element and rejects the whole card (rulecheck,
            # 2026-10-29: 19 Beast Tyrant cards): a text row in its place
            command = True
        elif n.key == "Augment":
            for c in n.children:
                if c.key == "AugmentIcon":
                    c.value = icons.get(c.text(), ICONS.get(c.text(), "Positive_AttackPlus"))
            out.append(n)
        else:
            out.append(n)
    if command:
        # Rows and Columns can't share a level (the game's warning): a half split into columns gets the text at the
        # top of its first column
        row = Node("Row", '"$GH_Command$"')
        col = next((n for n in out if n.key == "Column"), None)
        if any(n.key == "ColumnWidths" for n in out) and col is not None:
            col.children.insert(0, row)
        else:
            out.insert(0, row)
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


# ---------------------------------------------------------------- Frosthaven's card style (classes in FH_STYLE)
#
# Frosthaven's own cards are hand-made prefabs; these rules make a generated card look like them (the user's layout
# review, 2026-10-29: docs in tools/layout_review_page.py RULES). The data side is here; the plugin (GHCardStyle.cs)
# does what the layout can't say: Frosthaven's font and icon art, the pill and self backgrounds behind the rows marked
# below, and the infusions at the bottom of the half.
#   - a main line is Frosthaven's 24 (150% of the card's 16), not bold;
#   - all of an ability's modifiers (target, range, pierce, push, pull, jump, conditions) are one pill after the
#     value, marked <link=fhpill>, in Frosthaven's order (PILL_ORDER); a slot follows its own value, so the pill is
#     cut into rows after each slotted value (a row draws one slot line) and the plugin joins them;
#   - "Target all adjacent enemies" is the pill's target entry; "Target one adjacent enemy" (the default) goes;
#   - a self-targeted value loses its "self" label and is marked <link=fhself> (Frosthaven's outlined box);
#   - a consume sits in Frosthaven's dashed box (Conditional);
#   - every text key of the layout is the class's Frosthaven-worded copy, FHS_<key> (gh_port.fh_text).

PILL_OPEN, PILL_CLOSE = "<link=fhpill>", "</link>"
SELF_OPEN = "<link=fhself>"
FH_MAIN = "<size=150%>"
STYLE_COLUMNS = '"10,35,45,10"'
TARGET_ALL_ADJACENT = "<sprite name=Target> all adjacent <sprite name=Enemy>"
_KEY = re.compile(r"\$([A-Za-z0-9_]+)\$")


def _main_text(n):
    """The main value's text of an ability block (a bare main row, or with_modifiers' Row), else None."""
    if is_main(n):
        return n.value
    if (n.key == "Row" and not n.value and len(n.children) == 5 and n.children[0].key == "ColumnWidths"
            and n.children[0].value == MODIFIER_COLUMNS and n.children[2].value.strip('"').startswith(MAIN)):
        return n.children[2].value
    return None


def _modifiers(n):
    if n.key != "Row" or not n.children:
        return []
    out = []
    for c in n.children[3].children:
        t = c.text().strip()
        if c.key == "AddedEffect":
            out += [p.strip() for p in t.split(", ") if p.strip()]
        elif t:
            out.append(t)
    return out


def _clean(t):
    t = re.sub(r"</?mark[^>]*>", "", t)
    t = re.sub(r"<size=\d+%>|</size>", "", t)
    return t.strip()


# Frosthaven's order inside a pill (all 118 of its halves with two or more modifiers, 2026-10-29): target with its
# qualifiers, range, pierce, push or pull, then the rest (conditions, elements, XP) in the card's own order.
# The first four are set apart by a space ("◎2 ⟨⟩3 ⇣2"), the rest by a comma ("◎all ⟨⟩2, ★").
PILL_ORDER = ("Target", "Range", "Pierce", "Push", "Pull", "Jump")


def _pill_rank(m):
    s = re.match(r"<sprite name=(\w+)>", m)
    k = s.group(1) if s else ""
    return min(PILL_ORDER.index(k), 3) if k in PILL_ORDER[:5] else 4 if k == "Jump" else 5


def _pill_text(mods):
    out = ""
    for i, m in enumerate(mods):
        if i:
            out += " " if _pill_rank(m) < 5 else ", "
        # an icon and its number a space apart, as Frosthaven's pills write them ("◎ 2 ⟨⟩ 3")
        out += re.sub(r"(<sprite name=\w+>)(?=[*\d])", r"\1 ", m)
    return '"' + PILL_OPEN + out + PILL_CLOSE + '"'


def _pill_block(main, mods, self_box):
    main = main.strip('"')
    # a value in Frosthaven's self box is drawn at 20 (125%), not the main line's 24
    main = SELF_OPEN + main.replace(MAIN, "<size=125%>") + PILL_CLOSE if self_box else main
    if not mods:
        return Node("Row", f'"{main}"')
    mods = sorted(mods, key=_pill_rank)
    # a slot sits right after its value, as Frosthaven's do ("◎2 [slot] ⟨⟩1"); a row draws one slot line, at its end,
    # so the pill is cut after each slotted value: one row each, the plugin joins them into one pill
    parts, cur = [], []
    for m in mods:
        cur.append(m)
        if "@" in m:
            parts.append(cur)
            cur = []
    if cur:
        parts.append(cur)
    cols = [Node("Row", _pill_text(p)) for p in parts]
    return Node("Row", children=[Node("ColumnWidths", STYLE_COLUMNS), Node("Column"), Node("Column", f'"{main}"'),
                                 Node("Column", children=cols), Node("Column")])


def _style_items(items):
    out, i = [], 0
    while i < len(items):
        n = items[i]
        main = _main_text(n)
        if main is not None:
            mods = [_clean(m) for m in _modifiers(n)]
            j = i + 1
            # modifier rows convert_half left ungrouped (an area attack's column: Brute Force's Muddle under its Attack)
            while j < len(items) and _main_text(items[j]) is None and (is_modifier(items[j]) or (
                    is_spacer(items[j]) and j + 1 < len(items) and is_modifier(items[j + 1]))):
                if is_modifier(items[j]):
                    mods.append(_clean(items[j].text()))
                j += 1
            self_box = any(m in ("$Self$", FH_SELF) for m in mods)
            mods = [m for m in mods if m not in ("$Self$", FH_SELF)]
            while j < len(items) and is_row(items[j], lambda t: t.startswith("$Target_")):
                t = items[j].text()
                if t == "$Target_AllAdjacentEnemies$":
                    mods.insert(0, TARGET_ALL_ADJACENT)
                elif t == "$Target_OneAdjacentEnemy$":
                    pass
                else:
                    break
                j += 1
            out.append(_pill_block(main, mods, self_box))
            i = j
            continue
        if n.key == "Consume" and n.dash:
            # the reward on one line, as Frosthaven writes it ("+1 <Attack>, <Push>1"); Gloomhaven stacked its parts
            for c in n.children:
                if c.key == "Text" and c.children:
                    parts = [r.text().strip() for r in c.children if is_row(r) and not is_spacer(r)]
                    if parts and len(parts) == len([r for r in c.children if not is_spacer(r)]):
                        c.children = []
                        c.value = '"' + " ".join(parts) + '"'
            out.append(Node("Conditional", children=[Node("Layout", dash=False, children=[n])]))
            i += 1
            continue
        if n.children and n.key not in ("Consume",):
            n.children = _style_items(n.children) if any(c.dash for c in n.children) else n.children
            for c in n.children:
                if c.key == "Layout" and c.children:
                    c.children = _style_items(c.children)
        out.append(n)
        i += 1
    return out


def _restyle_values(nodes):
    for n in nodes:
        if n.value:
            n.value = re.sub(r"<b>\s*<size=130%>", FH_MAIN, n.value)
            n.value = _KEY.sub(lambda m: m.group(0) if m.group(1).startswith(("FH", "ABILITY_CARD")) else f"$FHS_{m.group(1)}$", n.value)
        _restyle_values(n.children)


def fh_style(s, warn=print):
    """A converted card file in Frosthaven's card style (see above): the Layout section only."""
    eol = "\r\n" if "\r\n" in s else "\n"
    lines = s.splitlines()
    try:
        at = lines.index("Layout:")
    except ValueError:
        return s
    body = [(len(ln) - len(ln.lstrip(" ")), ln) for ln in lines[at + 1:] if ln.strip() and not ln.lstrip().startswith("#")]
    try:
        halves = parse(body)
    except Unsupported as e:
        warn(f"Frosthaven style not applied ({e})")
        return s
    for half in halves:
        half.children = _style_items(half.children)
    _restyle_values(halves)
    return eol.join(lines[:at + 1]) + eol + "".join(emit(halves, 1, eol))
