"""Generates every Layer Herder icon and logo size from the masters in assets/brand.

Masters (as supplied by Chris, 2026-10-08):
  logo.png         full logo: border collie herding layer-stack sheep + wordmark
  app-icon.png     mascot sheep, used for the installer, exe and app icons
  ribbon-icon.png  white sheep glyph on slate, used for the AutoCAD ribbon

Run from anywhere (needs Pillow): python scripts/generate-brand-assets.py
Outputs are committed, so a normal build never runs this.
"""

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
BRAND = ROOT / "assets" / "brand"
INSTALLER = ROOT / "installer" / "assets"
RIBBON = ROOT / "src" / "AcLayerStandardizer" / "Resources"
RUST_UI = ROOT / "rust" / "crates" / "acad_layer_ui" / "assets"
DOCS = ROOT / "docs" / "assets"

ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]


def content_bbox(img, threshold=240):
    """Bounding box of everything darker than near-white."""
    gray = img.convert("L").point(lambda v: 255 if v < threshold else 0)
    return gray.getbbox()


def pad_to_square(img, margin_ratio, fill=(0, 0, 0, 0)):
    side = int(max(img.size) * (1 + 2 * margin_ratio))
    canvas = Image.new("RGBA", (side, side), fill)
    canvas.paste(img, ((side - img.width) // 2, (side - img.height) // 2), img)
    return canvas


def app_icon_master(size=1024):
    """Mascot sheep on a white rounded tile with a light border.

    The supplied art is a white tile on a white page, so the tile edge is
    redrawn here: the border keeps it readable on dark taskbars and the
    dark installer wizard as well as on light Explorer views.
    """
    src = Image.open(BRAND / "app-icon.png").convert("RGBA")
    sheep = src.crop(content_bbox(src, threshold=235))
    # The supplied art's own white is (253-254); make it pure white so it
    # matches the redrawn tile instead of showing a faint box.
    sheep = sheep.point(lambda v: 255 if v >= 250 else v)

    tile = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(tile)
    inset = size * 0.02
    radius = size * 0.2
    draw.rounded_rectangle(
        [inset, inset, size - inset - 1, size - inset - 1],
        radius=radius,
        fill=(255, 255, 255, 255),
        outline=(196, 201, 209, 255),
        width=max(2, int(size * 0.018)),
    )

    target = int(size * 0.78)
    scale = target / max(sheep.size)
    sheep = sheep.resize((int(sheep.width * scale), int(sheep.height * scale)), Image.LANCZOS)
    tile.alpha_composite(sheep, ((size - sheep.width) // 2, (size - sheep.height) // 2))
    return tile


def ribbon_glyph_master():
    """White sheep glyph on transparent, cut from the slate ribbon art.

    Same treatment as the old ribbon icon: white line art on transparent,
    because AutoCAD's default ribbon is dark. The "Layer Herder" caption
    under the glyph is dropped; AutoCAD draws the button label itself.
    """
    src = Image.open(BRAND / "ribbon-icon.png").convert("RGB")
    bg = src.getpixel((5, 5))
    bg_lum = 0.3 * bg[0] + 0.59 * bg[1] + 0.11 * bg[2]
    glyph_area = src.crop((0, 0, src.width, int(src.height * 0.6)))

    lum = glyph_area.convert("L")
    alpha = lum.point(lambda v: max(0, min(255, round((v - bg_lum) / (255 - bg_lum) * 255))))
    white = Image.new("RGBA", glyph_area.size, (255, 255, 255, 0))
    white.putalpha(alpha)
    glyph = white.crop(alpha.point(lambda v: 255 if v > 40 else 0).getbbox())
    return pad_to_square(glyph, margin_ratio=0.04)


def save_ico(master, path):
    master.save(path, format="ICO", sizes=[(s, s) for s in ICO_SIZES])


def resized(img, size):
    return img.resize(size, Image.LANCZOS)


def sidebar(logo, width, height):
    """Installer sidebar: the full logo on a white panel.

    The wizard runs in "modern dark"; the logo's black wordmark needs a
    light ground, so the panel is white rather than transparent.
    """
    panel = Image.new("RGBA", (width, height), (255, 255, 255, 255))
    target_w = int(width * 0.9)
    scaled = resized(logo, (target_w, int(logo.height * target_w / logo.width)))
    panel.alpha_composite(scaled, ((width - scaled.width) // 2, (height - scaled.height) // 2))
    return panel


def main():
    icon = app_icon_master()
    glyph = ribbon_glyph_master()
    logo_src = Image.open(BRAND / "logo.png").convert("RGBA")
    logo = logo_src.crop(content_bbox(logo_src, threshold=235))
    # Same near-white cleanup as the app icon, so padding doesn't show a box.
    logo = logo.point(lambda v: 255 if v >= 250 else v)
    logo = pad_to_square(logo, margin_ratio=0.04, fill=(255, 255, 255, 255))

    # AutoCAD ribbon button (embedded resources; file names are referenced
    # by RibbonSetup.cs and the csproj).
    resized(glyph, (32, 32)).save(RIBBON / "ribbon32.png")
    resized(glyph, (16, 16)).save(RIBBON / "ribbon16.png")

    # Installer: setup exe icon, uninstall entry icon, wizard images.
    save_ico(icon, INSTALLER / "LayerHerder.ico")
    resized(icon, (220, 220)).save(INSTALLER / "LayerHerder_header.png")
    sidebar(logo, 328, 628).save(INSTALLER / "LayerHerder_sidebar.png")

    # Rust UI: exe icon (embedded by build.rs) and window icon (raw RGBA,
    # so the crate needs no image decoder).
    RUST_UI.mkdir(parents=True, exist_ok=True)
    save_ico(icon, RUST_UI / "app_icon.ico")
    (RUST_UI / "app_icon_64.rgba").write_bytes(resized(icon, (64, 64)).tobytes())

    # Docs site.
    resized(logo, (600, 600)).save(DOCS / "logo.png", optimize=True)
    resized(icon, (256, 256)).save(DOCS / "app-icon.png", optimize=True)


if __name__ == "__main__":
    main()
