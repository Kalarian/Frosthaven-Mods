"""Pictures for a ported Gloomhaven class, from the player's OWN Gloomhaven install, in the shapes Frosthaven's UI
expects. Used by gh_port.py (needs UnityPy and Pillow: pip install UnityPy Pillow); nothing here is shipped.

Frosthaven keeps one set of UI pictures per hero (CharacterConfigUI). Gloomhaven has the same roles with different
framing, so each picture is composed, not copied:

    illustration.png        1200x820  class column, outpost header, scenario banner: a cut-out bust on transparency;
                                      the class column shows only a tall strip of it (about x 425-775, y 0-650), so
                                      the tall Gloomhaven portrait (party-assembly ring painted out) is scaled to fill
                                      that strip with the face centred
    InfoIllustration.png    888x1048  info panels and results: the square bust, scaled up, edges faded like Frosthaven's
    newAdventurePortrait.png 300x290  Gloomhaven's own portrait for that slot (<Class>_Normal, same layout in both games)
    cardBanner.png          600x158   the ability cards' banner strip (GHCardFace): eyes and brow of the square bust
    scenarioPortrait.png    196x278   scenario HUD and roster: the tall portrait cut to Frosthaven's pennant shape
    iconClass.png, iconClassHighlight.png, iconClassGold.png, marker.png, tabNormal.png, tabSelected.png,
    activeAbility.png                 Gloomhaven's own icons (the class glyph in white, glow and gold, the map marker,
                                      the initiative tokens, the active-ability icon), same designs in both games

The plugin (GHClasses.cs) loads whatever files exist and keeps the Drifter's picture for any that are missing.
"""

import io
import sys
from pathlib import Path

try:
    import UnityPy
    from PIL import Image, ImageChops, ImageDraw, ImageFilter
except ImportError as e:  # pragma: no cover
    raise ImportError("gh_art needs UnityPy and Pillow (pip install UnityPy Pillow)") from e

BUNDLES = Path("GH_Data") / "StreamingAssets" / "aa" / "StandaloneWindows64"
SHARED = Path("GH_Data") / "sharedassets1.assets"

# the class-select column shows this window of the 1200x820 illustration (measured on Frosthaven's own heroes)
STRIP_LEFT, STRIP_RIGHT, STRIP_BOTTOM = 425, 775, 650
# the ability cards' banner (GHCardFace: 300 x 79 on the card, drawn from twice that), centred on the eyes in InfoIllustration
BANNER, BANNER_EYES = (600, 158), 430


def _sprites(env, names):
    """{name: PIL image on its full logical rect} for the sprites with these names in a loaded UnityPy env.
    A name may appear more than once (Gloomhaven has two Brute_Normal sprites); the taller one wins."""
    out = {}
    for obj in env.objects:
        if obj.type.name != "Sprite":
            continue
        tree = obj.read_typetree()
        name = tree["m_Name"]
        if name not in names:
            continue
        try:
            part = obj.read().image.convert("RGBA")
        except Exception as e:  # noqa: BLE001
            print(f"warning: could not export sprite {name}: {e}")
            continue
        rect = tree["m_Rect"]
        off = tree["m_RD"]["textureRectOffset"]
        w, h = round(rect["width"]), round(rect["height"])
        full = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        # the exported part is the trimmed texture rect; Unity's offset is measured from the bottom-left
        full.paste(part, (round(off["x"]), h - round(off["y"]) - part.height))
        if name not in out or full.height > out[name].height:
            out[name] = full
    return out


def load_gh_pictures(gh, cls):
    """Gloomhaven's pictures for a class, by role. Missing ones are simply absent."""
    low = cls.lower()
    pics = {}
    bundles = {
        f"character_gui_classicons_assets_{low}_icon.bundle": {f"{cls}_Icon": "iconClass"},
        f"character_gui_classicons_assets_{low}glow_icon.bundle": {f"{cls}Glow_Icon": "iconClassHighlight"},
        f"character_gui_classicons_assets_assembleparty_{low}icon.bundle": {f"AssembleParty_{cls}Icon": "iconClassGold"},
        "misc_characterportraits_assets_all.bundle": {low: "bust"},
    }
    missing = {}   # sprite name -> role, for the DLC bundles (Jaws of the Lion keeps every picture of its four classes in one)
    for bundle, wanted in bundles.items():
        path = gh / BUNDLES / bundle
        if not path.exists():
            missing.update(wanted)
            continue
        for name, img in _sprites(UnityPy.load(str(path)), set(wanted)).items():
            pics[wanted[name]] = img
    shared = gh / SHARED
    wanted = {low: "tall", f"{cls}_Normal": "adventure", f"Counter_{cls}": "tabNormal",
              f"Counter_{cls}_Selected": "tabSelected", f"MapMarker_{cls}": "marker", f"Ability_{cls}_Icon": "activeAbility"}
    if shared.exists():
        for name, img in _sprites(UnityPy.load(str(shared)), set(wanted)).items():
            if name == f"{cls}_Normal" and img.height < 200:
                continue  # the 588x91 preview plate of the same name
            pics[wanted[name]] = img
    else:
        print(f"warning: no {shared}")
    missing.update({name: role for name, role in wanted.items() if role not in pics})
    for dlc in sorted((gh / BUNDLES).glob("dlc_*_misc_gui_assets_all*.bundle")):
        if not missing:
            break
        for name, img in _sprites(UnityPy.load(str(dlc)), set(missing)).items():
            if name == f"{cls}_Normal" and img.height < 200:
                continue
            pics[missing.pop(name)] = img
    for name, role in missing.items():
        if role in ("iconClass", "iconClassHighlight", "iconClassGold", "bust"):
            print(f"warning: no Gloomhaven picture {name} ({role})")
    return pics


def _ramp(length, fade_start, fade_end):
    """A 1-pixel-wide L strip of `length` pixels: 0 at the ends, 255 inside, easing over fade_start / fade_end."""
    strip = Image.new("L", (length, 1), 255)
    px = strip.load()
    for i in range(length):
        v = min(1.0, (i + 1) / fade_start if fade_start else 1.0, (length - i) / fade_end if fade_end else 1.0)
        px[i, 0] = int(255 * v * v)  # ease in
    return strip


def _feather(img, left=0, right=0, top=0, bottom=0):
    """Fade the picture's edges to transparent over the given widths."""
    w, h = img.size
    horizontal = _ramp(w, left, right).resize((w, h), Image.NEAREST)
    vertical = _ramp(h, top, bottom).transpose(Image.Transpose.ROTATE_270).resize((w, h), Image.NEAREST)
    mask = ImageChops.multiply(horizontal, vertical)
    img = img.copy()
    img.putalpha(_min_alpha(img.split()[3], mask))
    return img


def _min_alpha(a, b):
    """Pixel-wise minimum of two L images."""
    return ImageChops.darker(a, b)


def painting_without_ring(tall):
    """Gloomhaven's tall portrait has its party-assembly ring token baked into the top-left corner, over the
    painting and over the transparent margin around it. Returns the painting alone (its opaque rectangle), the ring
    area filled with the background colour sampled from the painting just right of it."""
    alpha = tall.split()[3]
    bbox_rows = [y for y in range(tall.height) if any(alpha.getpixel((x, y)) > 200 for x in range(tall.width // 2, tall.width))]
    top, bottom = bbox_rows[0], bbox_rows[-1]
    # left edge: the painting's leftmost opaque column well below the ring
    probe_y = min(bottom, top + int((bottom - top) * 0.6))
    left = next(x for x in range(tall.width) if alpha.getpixel((x, probe_y)) > 200)
    right = max(x for x in range(tall.width) if alpha.getpixel((x, probe_y)) > 200)
    trim = 4  # the portrait carries a thin gold frame line along its edges
    painting = tall.crop((left + trim, top + trim, right + 1 - trim, bottom + 1 - trim))
    # The ring is a circle whose centre sits above-left of the painting's corner (its top and left parts are over the
    # transparent margin, cropped away above). Its centre and radius, measured on Gloomhaven's portraits: the token
    # is centred about 63 px from the sprite's top-left corner with an outer radius of about 66 px, including the thin
    # halo lines. It overlaps the left horn by a few pixels, which are repainted as background too.
    cx, cy, r = 63 - left - trim, 63 - top - trim, 66
    # fill colour: the background just below the ring at the painting's left edge (dark, like the corner it replaces)
    px = painting.load()
    samples = [px[x, y][:3] for x in range(0, 8) for y in range(cy + r + 2, min(painting.height, cy + r + 14))]
    colour = tuple(sum(c[i] for c in samples) // len(samples) for i in range(3)) + (255,)
    fill = Image.new("RGBA", painting.size, colour)
    # blend the filled circle in through a blurred mask so the seam is soft
    mask = Image.new("L", painting.size, 0)
    ImageDraw.Draw(mask).ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(3))
    return Image.composite(fill, painting, mask)


def compose(pics, out):
    """Write Frosthaven-shaped pictures from Gloomhaven's. Returns the list of files written."""
    out.mkdir(parents=True, exist_ok=True)
    written = []

    def save(name, img):
        img.save(out / f"{name}.png")
        written.append(name)

    def fit(img, w, h):
        """img scaled to fit inside w x h, centred on a transparent canvas."""
        s = min(w / img.width, h / img.height)
        scaled = img.resize((max(1, round(img.width * s)), max(1, round(img.height * s))), Image.LANCZOS)
        canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        canvas.paste(scaled, ((w - scaled.width) // 2, (h - scaled.height) // 2))
        return canvas

    if "tall" in pics:
        painting = painting_without_ring(pics["tall"])
        # class column strip: fill its height, face (about 57% across the painting) at the strip's centre
        s = STRIP_BOTTOM / painting.height
        scaled = painting.resize((round(painting.width * s), STRIP_BOTTOM), Image.LANCZOS)
        scaled = _feather(scaled, left=28, right=28, bottom=40)
        canvas = Image.new("RGBA", (1200, 820), (0, 0, 0, 0))
        face_x = round(scaled.width * 0.57)
        canvas.paste(scaled, ((STRIP_LEFT + STRIP_RIGHT) // 2 - face_x, 0), scaled)
        save("illustration", canvas)
        # scenario HUD / roster portrait: Frosthaven's is a 196x278 pennant (square top, pointed bottom)
        s = 196 / painting.width
        tall = painting.resize((196, round(painting.height * s)), Image.LANCZOS)
        if tall.height < 278:
            s = 278 / painting.height
            tall = painting.resize((round(painting.width * s), 278), Image.LANCZOS)
        pennant = tall.crop(((tall.width - 196) // 2, 0, (tall.width - 196) // 2 + 196, 278))
        mask = Image.new("L", (196 * 4, 278 * 4), 0)
        ImageDraw.Draw(mask).polygon([(4, 0), (196 * 4 - 5, 0), (196 * 4 - 5, 214 * 4), (97 * 4, 278 * 4 - 1), (4, 214 * 4)], fill=255)
        mask = mask.resize((196, 278), Image.LANCZOS)
        pennant.putalpha(_min_alpha(pennant.split()[3], mask))
        save("scenarioPortrait", pennant)
    if "bust" in pics:
        bust = pics["bust"]
        canvas = Image.new("RGBA", (888, 1048), (0, 0, 0, 0))
        big = bust.resize((888, round(bust.height * 888 / bust.width)), Image.LANCZOS)
        big = _feather(big, left=70, right=70, top=50, bottom=90)
        canvas.paste(big, (0, 56), big)
        save("InfoIllustration", canvas)
        # ability card banner: Frosthaven's cards open with a 300 x 79 picture strip; a strip across the eyes and brow of
        # the square bust (inside its faded edges), at twice the on-card size, on dark where the painting is faded
        w = 740
        h = round(w * BANNER[1] / BANNER[0])
        strip = canvas.crop(((888 - w) // 2, BANNER_EYES - h // 2, (888 - w) // 2 + w, BANNER_EYES - h // 2 + h))
        banner = Image.new("RGBA", strip.size, (20, 20, 24, 255))
        banner.alpha_composite(strip)
        save("cardBanner", banner.resize(BANNER, Image.LANCZOS))
    if "adventure" in pics:
        save("newAdventurePortrait", fit(pics["adventure"], 300, 290))
    for role, size in (("iconClass", 128), ("iconClassGold", 216), ("marker", 140), ("tabNormal", 124), ("tabSelected", 124), ("activeAbility", 64)):
        if role in pics:
            save(role, fit(pics[role], size, size))
    if "iconClassHighlight" in pics:
        save("iconClassHighlight", fit(pics["iconClassHighlight"], 512, 512))
    return written


def export(gh, cls, out):
    pics = load_gh_pictures(Path(gh), cls)
    written = compose(pics, Path(out))
    return written, sorted(pics)


if __name__ == "__main__":
    gh_dir, cls, out_dir = sys.argv[1], sys.argv[2], sys.argv[3]
    files, roles = export(gh_dir, cls, out_dir)
    print(f"{cls}: Gloomhaven pictures {roles}; wrote {files} to {out_dir}")
