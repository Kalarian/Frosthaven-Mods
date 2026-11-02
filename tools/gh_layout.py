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
VALUE_REF = re.compile(r"\*[A-Za-z]\w*(\.\w+)?\*")   # an ability's value in text: "*BCDarkCurse.Range*"


def _pill_rank(m):
    s = re.match(r"<sprite name=(\w+)>", m)
    k = s.group(1) if s else ""
    return min(PILL_ORDER.index(k), 3) if k in PILL_ORDER[:5] else 4 if k == "Jump" else 5


def _pill_text(mods, after=False):
    """after: the pill continues a row before it (a slotted value ended that row), so a condition still takes its comma."""
    out = ""
    for i, m in enumerate(mods):
        if i or (after and _pill_rank(m) >= 5):
            out += " " if _pill_rank(m) < 5 else ", "
        # an icon and its number a space apart, as Frosthaven's pills write them ("◎ 2 ⟨⟩ 3")
        out += re.sub(r"(<sprite name=\w+>)(?=[*\d])", r"\1 ", m)
    return '"' + PILL_OPEN + out + PILL_CLOSE + '"'


def _pill_block(main, mods, self_box):
    main = main.strip('"')
    # Frosthaven boxes what could reach others but is yours (a heal, invisible, a condition); a shield or retaliate is
    # only ever yours and is never boxed (9 of 9 Frosthaven shield halves; Psychic Projection's was)
    if re.search(r"<sprite name=(?:Shield|Retaliate)>", main):
        self_box = False
    # a value in Frosthaven's self box is drawn at 20 (125%), not the main line's 24
    main = SELF_OPEN + main.replace(MAIN, "<size=125%>") + PILL_CLOSE if self_box else main
    if not mods:
        return Node("Row", f'"{main}"')
    mods = sorted(mods, key=_pill_rank)
    # Frosthaven draws a ranged attack with the arrow, a melee one with the sword: 248 of its 259 attack halves follow
    # "the line shows a range value" (the user, 2026-10-30); "all enemies within X" is the sword (Against All Odds)
    if (re.search(r"<sprite name=Attack>", main) and any(re.match(r"<sprite name=Range>", m) for m in mods)
            and not any(m.startswith("<sprite name=Target> all") for m in mods)):
        main = main.replace("<sprite name=Attack>", "<sprite name=RangedAttack>", 1)
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
    cols = [Node("Row", _pill_text(p, after=k > 0)) for k, p in enumerate(parts)]
    # a main line of several conditions and nothing else ("poison, wound, muddle": Grisly Trauma) keeps its own line, the
    # pill centred under it (the user, 2026-10-30): the narrow main column broke the conditions over two rows
    plain = re.sub(r"<[^>]+>", "", main)
    if len(re.findall(r"<sprite name=", main)) >= 2 and not re.search(r"[\w*@]", plain):
        return [Node("Row", f'"{main}"'), Node("Row", '"<size=40%> "'),
                Node("Row", children=[Node("ColumnWidths", STYLE_COLUMNS), Node("Column"), Node("Column", '"<size=1%> "'),
                                      Node("Column", children=cols), Node("Column")])]
    return Node("Row", children=[Node("ColumnWidths", STYLE_COLUMNS), Node("Column"), Node("Column", f'"{main}"'),
                                 Node("Column", children=cols), Node("Column")])


# a text row under a value that Frosthaven writes as pill entries ("Target all enemies within <Range> 3" is "◎ all ⟨⟩ 3")
_TEXT_TARGETS = {"Target_AllAdjacentEnemies": TARGET_ALL_ADJACENT, "Target_OneAdjacentEnemy": None,
                 "Target_AllEnemiesWithin": "<sprite name=Target> all",
                 "Target_AllAlliesWithin": "<sprite name=Target> all <sprite name=Allies>",
                 "Retaliate": None,
                 # a jump under a move is its pill's icon (Black Lance: Move 4 [jump])
                 "Jump": "<sprite name=Jump>", "FHS_Jump": "<sprite name=Jump>"}


def _text_modifiers(t):
    """The pill entries a text row under a value stands for, or None when it is a sentence of its own."""
    # the port's own keys and the word "Range" read the same ("$FHS_Target_AllEnemiesWithin$ $FHS_Range$ 3": Flurry of Axes)
    t = re.sub(r"</?nobr>", "", t)
    t = re.sub(r"\$(?:FHS_)?Range\$", "<sprite name=Range>", t)
    t = re.sub(r"\$FHS_(\w+)\$", r"$\1$", t)
    t = re.sub(r"<b></b>\s*", "", t).strip()
    t = re.sub(r"\$within\$\s*", "", t)   # "Affect all allies $within$ <Range> 3" (Echoing Aria)
    # targets with "self" or a qualifier, written in Frosthaven's pill words (Rest in the Shade: "◎ all <ally> on hexes
    # with X and self"): Sap Warmth, Virulent Strain (the user's challenge, 2026-10-30)
    q = re.fullmatch(r"\$Target_SelfAndAllAlliesWithin\$\s*(<sprite name=Range>.*)", t)
    if q:
        return ["<sprite name=Target> all <sprite name=Allies> and self", _clean(q.group(1))]
    q = re.fullmatch(r"\$Target_AllEnemiesAdjacentToEnemiesWith\$\s*(.+)", t)
    if q:
        return [f"<sprite name=Target> all <sprite name=Enemy> adjacent to <sprite name=Enemy> with {q.group(1).strip()}"]
    # "Affect all (adjacent) allies [with X]" under an ability is Frosthaven's pill target ("◎ all <ally> ... and self":
    # Rest in the Shade; Succumb to the Gift's bless crowded its text line, the user 2026-10-30)
    a = re.fullmatch(r"\$Affect_(SelfAnd)?(\w+?)(Allies|Ally)(MovedThrough|Within)?(?:Range(\d+))?\$\s*(.*?)\s*", t)
    if a:
        who = " ".join(w.lower() for w in re.findall(r"[A-Z][a-z]*", a.group(2)))
        tail = {"MovedThrough": " moved through", "Within": ""}.get(a.group(4) or "", "")
        rest = re.sub(r"\$(?:FHS_)?with\$", "with", a.group(6)).strip()
        entry = f"<sprite name=Target> {who} <sprite name=Allies>{tail}" + (f" {rest}" if rest and not rest.startswith("<sprite name=Range>") and not rest.startswith("<nobr><sprite name=Range>") else "") + (" and self" if a.group(1) else "")
        out = [entry]
        rng = re.search(r"<sprite name=Range>\s*([^<]+)", a.group(6))
        if a.group(5): out.append(f"<sprite name=Range> {a.group(5)}")
        elif rng: out.append(f"<sprite name=Range> {rng.group(1).strip()}")
        return out
    m = re.fullmatch(r"\$(\w+)\$\s*(?:<nobr>)?\s*(<sprite name=Range>.*?)?(?:</nobr>)?\s*", t)
    if not m or m.group(1) not in _TEXT_TARGETS:
        return None
    # "Retaliate" and "... within" stand for a pill only with their range
    if (m.group(1) == "Retaliate" or m.group(1).endswith("Within")) and not m.group(2):
        return None
    if m.group(1).endswith("Adjacent") and m.group(2):
        return None
    return [x for x in (_TEXT_TARGETS[m.group(1)], m.group(2) and _clean(m.group(2))) if x]


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
            # target text rows and modifier rows in any order: a condition after "all adjacent enemies" stayed on a row
            # of its own under the pill (Freezing Nova, Long Con, Dust Cloud)
            while j < len(items) and is_row(items[j]):
                tm = _text_modifiers(items[j].text())
                if tm is not None:
                    mods += tm
                elif is_modifier(items[j]):
                    mods.append(_clean(items[j].text()))
                else:
                    break
                j += 1
            pb = _pill_block(main, mods, self_box)
            out.extend(pb if isinstance(pb, list) else [pb])
            i = j
            continue
        if n.key == "Consume" and n.dash:
            # the reward on one line, as Frosthaven writes it ("+1 <Attack>, <Push>1"); Gloomhaven stacked its parts
            for c in n.children:
                if c.key == "Text" and c.children:
                    parts = [r.text().strip() for r in c.children if is_row(r) and not is_spacer(r)]
                    # an area picture ("£Area£") is a row of its own, never text; a value of the consume's own ability
                    # ("*BCDarkCurse.Range*") is found only from its row (Suggestion: "Unable to find ability", 2026-10-30)
                    if parts and len(parts) == len([r for r in c.children if not is_spacer(r)]) and not any("£" in x or VALUE_REF.search(x) for x in parts):
                        c.children = []
                        c.value = '"' + " ".join(parts) + '"'
                    # the same reward laid out in columns, one part per row (Frozen Night: "+2 <Move>" over <Invisible>):
                    # one line, its parts comma-separated, without the stacked rows' sizes
                    elif {m.key for m in _flat(c.children)} <= {"Column", "ColumnWidths", "ColumnSpacing", "Row"}:
                        cols = [m.text() for m in _flat(c.children) if m.key in ("Row", "Column") and not m.children
                                and m.text().strip() and not SPACER.match(m.text())]
                        cols = [re.sub(r"^(?:<(?:size|line-height)=[^>]*>|</?b>)+|</?b>", "", x).strip() for x in cols]
                        if cols and not any("£" in x or VALUE_REF.search(x) for x in cols):
                            # the pill's order rule: a target, range, pierce, push or pull follows its value with a space
                            line = cols[0]
                            for x in cols[1:]:
                                mod = re.match(r"(?:<nobr>)?\s*(?:<sprite name=(?:Range|Target|Pierce|Push|Pull)>|\$(?:FHS_)?Target_)", x)
                                line += (" " if mod else ", ") + x
                            c.children = []
                            c.value = '"' + line + '"'
            # a reward that can't be text (an ability value, an area, an active bonus) stays a layout, the plugin draws
            # the dashed box round it (GHCardStyle.BareConsumes); here it goes on one line at the box's size
            for c in n.children:
                if c.key == "Text" and c.children:
                    c.children = _bare_reward(c.children)
                elif c.key == "Text" and c.value and not c.value.startswith('"$'):
                    # one size in the box, the XP last ("-1 <move>, XP <size=150%><attack> 2" read oddly: Resigned
                    # Frenzy, the user 2026-10-30)
                    # (a voffset in front of the XP stayed behind as an empty item: "Move 2, , XP 1", Bonded Might
                    # bottom, the 2026-11-01 visual review)
                    t = re.sub(r"</?voffset[^>]*>", "", _SIZES.sub("", c.text()))
                    xp = re.findall(r"<sprite name=XP\d+>", t)
                    if xp:
                        t = re.sub(r",?\s*<sprite name=XP\d+>\s*,?", " ", t).strip()
                        t = re.sub(r"(\S)\s+(<nobr><sprite name=(?:Attack|Move|Heal|Shield)>)", r"\1, \2", t)
                        t = t.rstrip(", ") + ", " + xp[0]
                    c.set_text(t)
            # only a reward of text goes in the dashed box: the box's layout looks up every row as text, and an area,
            # an infusion or an ability in it stops the game at load ("an error within one of the Rule Libraries":
            # Shield of the Desert, Burn Away the Dark, 2026-10-30)
            text = next((c for c in n.children if c.key == "Text"), None)
            out.append(Node("Conditional", children=[Node("Layout", dash=False, children=[n])])
                       if text is not None and not text.children and "£" not in (text.value or "")
                       and not VALUE_REF.search(text.value or "") else n)
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
            # "STUN <Stun>": the icon alone (rule R3)
            n.value = re.sub(r"\$(\w+?)Short\$\s*(<sprite name=\1>)", r"\2", n.value)
            n.value = _KEY.sub(lambda m: m.group(0) if m.group(1).startswith(("FH", "ABILITY_CARD")) else f"$FHS_{m.group(1)}$", n.value)
        _restyle_values(n.children)


SELF_DEFAULT = re.compile(r"<sprite name=(?:Heal|Shield|Retaliate|Invisible)>")
SELF_WORD = re.compile(r"\s*,?\s*(?:<nobr>)?\s*\$(?:FHS_)?Self\$\s*(?:</nobr>)?")


def _drop_self(items):
    """Heal, shield, retaliate and invisible are yours unless the card says otherwise (the user, 2026-10-30: drop "Self"
    on Crackling Air, Hardened Spikes, Frozen Night): a "Self" row under one goes, and ", Self" after one."""
    out = []
    for n in items:
        t = n.text()
        if (n.key == "Row" and not n.children and re.sub(r"<[^>]+>", "", t).strip() in ("$Self$", "$FHS_Self$")
                and out and SELF_DEFAULT.search(out[-1].text() or "")):
            continue
        # a retaliate's range is the range icon alone ("<Retaliate> 2, Retaliate <Range> 2": Echoing Aria, Boiling Arc)
        if t and re.search(r"\$(?:FHS_)?Retaliate\$\s*(?:<nobr>)?<sprite name=Range>", t):
            t = re.sub(r"\$(?:FHS_)?Retaliate\$\s*((?:<nobr>)?<sprite name=Range>)", r"\1", t)
            n.set_text(t)
        # no full stop after a closing icon ("instead, <XP>.": Spirit of Doom's box)
        if t and re.search(r"(<sprite name=\w+>(?:</nobr>)?)\.\s*$", t):
            t = re.sub(r"(<sprite name=\w+>(?:</nobr>)?)\.\s*$", r"\1", t)
            n.set_text(t)
        # a word naming the icon after it goes, as in the card texts (Vicious Blood: "Retaliate <Retaliate> 2")
        if t and re.search(r"\b([A-Z][a-z]+)\s+(<sprite name=\1>)", t):
            t = re.sub(r"\b([A-Z][a-z]+)\s+(<sprite name=\1>)", r"\2", t)
            n.set_text(t)
        if t and SELF_DEFAULT.search(t) and SELF_WORD.search(t):
            n.set_text(re.sub(r",\s*(</nobr>)\s*$", r"\1", SELF_WORD.sub("", t).rstrip(", ")))
        if n.children:
            n.children = _drop_self(n.children)
        out.append(n)
    return out


_SIZES = re.compile(r"<(?:size|line-height)=[^>]*>|</?b>")


def _join_parts(parts):
    """Reward parts on one line: a target, range, pierce, push or pull after a space, the rest after a comma."""
    line = parts[0]
    for x in parts[1:]:
        x = re.sub(r"^\$(?:FHS_)?Retaliate\$\s*", "", x)   # "Retaliate <Range> 2" is the range alone (Boiling Arc)
        mod = re.match(r"(?:<nobr>)?\s*(?:<sprite name=(?:Range|Target|Pierce|Push|Pull)>|\$(?:FHS_)?Target_)", x)
        line += (" " if mod else ", ") + x
    return line


def _bare_reward(items):
    """A consume reward the dashed box can't hold as text, on one line (the user, 2026-10-30: Frosthaven boxes these
    too - Bone Dagger, Flesh Shield, Lacerating Eruption): column wrappers go, stacked rows join ("<Retaliate> 2" over
    "<Range> 2" is "<Retaliate> 2 <Range> 2"), the main line's 150% goes; an area stays a row of its own, after the
    line, and an active bonus keeps its node with its rows joined."""
    flat = []
    for n in items:
        if n.key in ("ColumnWidths", "ColumnSpacing"):
            continue
        if n.key == "Column":
            flat += _bare_reward(n.children) if n.children else ([Node("Row", n.value)] if n.text().strip() else [])
            continue
        flat.append(n)
    rows = [n for n in flat if is_row(n) and not is_spacer(n) and "£" not in n.text()]
    areas = [n for n in flat if is_row(n) and "£" in n.text()]
    other = [n for n in flat if not is_row(n)]
    for a in other:
        if a.key == "Active":
            for lay in a.children:
                if lay.key == "Layout" and lay.children:
                    parts = [_SIZES.sub("", r.text()).strip() for r in lay.children if is_row(r) and not is_spacer(r)]
                    if parts:
                        lay.children = [Node("Row", '"' + _join_parts(parts) + '"')]
    # an area first, then the rest after a comma, as Lacerating Eruption's "[area], XP"
    out = list(other) + areas
    if rows:
        line = _join_parts([_SIZES.sub("", r.text()).strip() for r in rows])
        # "shield 1, XP" (Flesh Shield); a range after a retaliate follows with a space ("<Retaliate> 2 <Range> 2")
        mod = re.match(r"(?:<nobr>)?\s*<sprite name=(?:Range|Target|Pierce|Push|Pull)>", line)
        out.append(Node("Row", '"' + ((" " if mod else ", ") + line if areas or other else line) + '"'))
    return out


COMMAND_KEY = re.compile(r'^"?\$(?:FHS_)?GH_Command\$"?$')


def _commands(items):
    """The Beast Tyrant's "Command" label (a row of its own or a narrow column beside the bear's abilities) goes; the
    abilities follow the normal layout rules and the plugin tags the half "Command" at its top left, as Mindthief's
    augments and Soothsinger's songs (the user, 2026-10-30). Returns (items, found)."""
    found = False

    def walk(nodes):
        nonlocal found
        out = []
        for n in nodes:
            if n.key in ("Row", "Column") and not n.children and COMMAND_KEY.match(n.value.strip()):
                found = True
                if n.key == "Column":
                    out.append(Node("Column"))   # keeps the column count
                continue
            if n.children:
                n.children = walk(n.children)
                cols = [c for c in n.children if c.key == "Column"]
                full = [c for c in cols if c.children or c.text().strip()]
                # a row of columns with one column left in use is that column's content
                if n.key == "Row" and cols and len(full) == 1 and not full[0].text().strip():
                    out.extend(full[0].children)
                    continue
            out.append(n)
        return out

    items = walk(items)
    if found:
        marker = Node("Row", '"<size=1%><link=fhcommand> </link>"')
        cols = [c for c in items if c.key == "Column"]
        full = [c for c in cols if c.children or c.text().strip()]
        if cols and len(full) == 1 and not full[0].text().strip():
            items = full[0].children          # a half split into columns with one in use (Jaws of Death bottom)
            cols = []
        if cols:
            # still columns (an area beside the bear's attack: Forceful Swipe): the marker goes in the first one
            first = next((c for c in full if c.children), None)
            if first is not None:
                first.children.insert(0, marker)
        else:
            items.insert(0, marker)
    return items


def _doom_rows(items, zero_tracker):
    """A Doom's reminder line at the text's size, and a Doom turn counter that gives no XP not drawn (Inescapable Fate
    bottom: three green boxes over the text, the reminder at 70%; the user, 2026-10-30)."""
    out = []
    for n in items:
        if n.key == "XPTracker" and "Doom" in n.value and zero_tracker:
            continue
        if is_row(n) and "Doom_DiscardReminderText" in n.text():
            n.set_text(_SIZES.sub("", n.text()))
        if n.children:
            n.children = _doom_rows(n.children, zero_tracker)
        out.append(n)
    return out


def _data_rows(items):
    """A row written as "Data:" plus "RowProperties:" (Provoke Terror's stun) is an ordinary row for the style pass, so
    its range joins it in a pill (the user, 2026-10-30)."""
    for n in items:
        if n.key == "Row" and n.children and any(c.key == "Data" for c in n.children):
            data = next(c for c in n.children if c.key == "Data")
            n.value, n.children = data.value, []
        elif n.children:
            _data_rows(n.children)
    return items


def _augment_lines(items):
    """An augment's effect on one line at one size (the user, 2026-10-30: Silent Scream's heal and range stacked;
    Frozen Mind's stun small and low beside a larger, raised consume icon)."""
    for n in _flat(items):
        if n.key == "AugmentAbilityLayout" and n.children and all(is_row(r) for r in n.children):
            parts = [re.sub(r"</?(?:nobr|voffset)[^>]*>", "", _SIZES.sub("", r.text())).strip()
                     for r in n.children if not is_spacer(r)]
            if parts:
                n.children = [Node("Row", '"<size=125%>' + _join_parts(parts) + '"')]
    return items


def _flat(items):
    return [m for n in items for m in [n] + _flat(n.children)]


def _drop_durations(items):
    """Gloomhaven drew a round or persistent icon beside the ability; Frosthaven's badge bar at the bottom right already
    shows it, so the card had it twice (Mech Suit, Winged Congregation). Frosthaven keeps one only inside a consume box
    (Shattering Blow), so those stay. A row left with only empty columns goes too."""
    out = []
    for n in items:
        if n.key == "Duration":
            continue
        # inside a consume only when its reward isn't the text the dashed box takes: a boxed one keeps its icon, as
        # Frosthaven's Shattering Blow; a bare one showed it twice (Crackling Air, Boiling Arc)
        boxed = n.key == "Consume" and any(c.key == "Text" and not c.children for c in n.children)
        if n.children and n.key != "Conditional" and not boxed:
            before = len(_flat(n.children))
            n.children = _drop_durations(n.children)
            if n.key == "Row" and len(_flat(n.children)) < before and all(c.key == "ColumnWidths" or (c.key == "Column" and not c.children and not c.text().strip())
                                      for c in n.children):
                continue
        out.append(n)
    return out


def _tracker_space(items):
    """A use tracker right under an active bonus's text: the bonus keeps the height the generator gave it in
    Gloomhaven's font, and in Frosthaven's its last line ran under the tracker (Shrapnel, Heart Seeker, Flame Shroud).
    The spacer before the tracker is a full line."""
    out = []
    for n in items:
        if n.key == "XPTracker" and any(o.key == "Active" for o in out[-2:]):
            if out and out[-1].key == "Row" and is_spacer(out[-1]):
                out[-1].set_text("<size=100%> ")
            else:
                out.append(Node("Row", '"<size=100%> "'))
        out.append(n)
    return out


def fh_style(s, warn=print):
    """A converted card file in Frosthaven's card style (see above): the Layout section only."""
    eol = "\r\n" if "\r\n" in s else "\n"
    lines = s.splitlines()
    try:
        at = lines.index("Layout:")
    except ValueError:
        return s
    # a hand-written layout (gh_port.CARD_FIXES) may open its main line the other way round, and a condition's main line
    # may start inside its <nobr>
    body = [(len(ln) - len(ln.lstrip(" ")), ln.replace("<size=130%><b>", MAIN).replace("<nobr>" + MAIN, MAIN + "<nobr>"))
            for ln in lines[at + 1:] if ln.strip() and not ln.lstrip().startswith("#")]
    try:
        halves = parse(body)
    except Unsupported as e:
        warn(f"Frosthaven style not applied ({e})")
        return s
    # a Doom turn counter with no XP on it (Inescapable Fate: XPTracker [0,0,0])
    trackers = re.findall(r"XPTracker: \[([\d, ]+)\]", s)
    zero_doom = bool(trackers) and all(set(x.replace(" ", "").split(",")) <= {"0"} for x in trackers)
    for half in halves:
        half.children = _tracker_space(_drop_durations(_commands(_data_rows(_doom_rows(half.children, zero_doom)))))
        half.children = _augment_lines(_drop_self(_style_items(half.children)))
    _restyle_values(halves)
    return eol.join(lines[:at + 1]) + eol + "".join(emit(halves, 1, eol))
