"""Derives the Desktop Kit's plate sprites from one material swatch.

Usage: make_kit_art.py <swatch.png> <out dir> [radius units]

Every plate shares one rounded silhouette with a soft painted rim. A state is the same
material graded toward a token colour. Sprites are drawn at 4x and reduced, and colour is
bled under the transparent pixels so bilinear filtering leaves no dark fringe.
"""
import sys
import numpy as np
from PIL import Image, ImageFilter

SS = 4                      # supersampling
PX_PER_UNIT = 3.0           # sprite pixels per canvas unit at control-md
H_UNITS = 36                # control-md
W_UNITS = 64                # wide enough for a centre that stretches
PAD_UNITS = 0.5             # transparent room around the silhouette for its soft edge


def hex_rgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32) / 255


def rounded_mask(w, h, r):
    """Signed distance to a rounded rectangle, in pixels (negative inside)."""
    y, x = np.mgrid[0:h, 0:w].astype(np.float32) + 0.5
    qx = np.abs(x - w / 2) - (w / 2 - r)
    qy = np.abs(y - h / 2) - (h / 2 - r)
    outside = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0))
    inside = np.minimum(np.maximum(qx, qy), 0)
    return outside + inside - r


def smooth(d, width):
    return np.clip(0.5 - d / width, 0, 1)


def material(swatch, w, h, target, contrast):
    """A crop of the swatch re-graded so its mean is the target colour."""
    crop = swatch[:h, :w, :]
    lum = crop.mean(axis=2, keepdims=True)
    lum = (lum - lum.mean()) / (lum.std() + 1e-6)
    return np.clip(target[None, None, :] * (1 + contrast * lum), 0, 1)


def plate(swatch, radius_units, fill, rim, rim_alpha, lift=0.10, contrast=0.10,
          w_units=W_UNITS, h_units=H_UNITS):
    w = int(w_units * PX_PER_UNIT * SS)
    h = int(h_units * PX_PER_UNIT * SS)
    r = radius_units * PX_PER_UNIT * SS
    pad = PAD_UNITS * PX_PER_UNIT * SS
    d = rounded_mask(w, h, r) + pad
    rgb = material(swatch, w, h, hex_rgb(fill), contrast)
    # light from above: a little lift at the top, a little weight at the bottom
    ramp = np.linspace(1 + lift, 1 - lift, h, dtype=np.float32)[:, None, None]
    rgb = np.clip(rgb * ramp, 0, 1)
    # the painted rim: a soft band just inside the edge, strongest at the top
    band = np.exp(-((d + 1.6 * PX_PER_UNIT * SS) / (0.9 * PX_PER_UNIT * SS)) ** 2)
    top = np.linspace(1.0, 0.55, h, dtype=np.float32)[:, None]
    a = (band * top * rim_alpha)[:, :, None]
    rgb = rgb * (1 - a) + hex_rgb(rim)[None, None, :] * a
    # a faint dark seat at the very edge so the plate sits on any ground
    seat = np.exp(-((d + 0.3 * PX_PER_UNIT * SS) / (0.5 * PX_PER_UNIT * SS)) ** 2)[:, :, None] * 0.35
    rgb = rgb * (1 - seat)
    alpha = smooth(d, 1.2 * SS)
    return finish(rgb, alpha)


def ring(radius_units, color, alpha_peak, width_units, w_units=W_UNITS, h_units=H_UNITS):
    """A frame with no centre: a soft line that follows the plate's silhouette."""
    w = int(w_units * PX_PER_UNIT * SS)
    h = int(h_units * PX_PER_UNIT * SS)
    r = radius_units * PX_PER_UNIT * SS
    pad = PAD_UNITS * PX_PER_UNIT * SS
    d = rounded_mask(w, h, r) + pad
    centre = -0.9 * PX_PER_UNIT * SS
    line = np.exp(-((d - centre) / (width_units * PX_PER_UNIT * SS)) ** 2)
    halo = np.exp(-((d - centre) / (2.6 * width_units * PX_PER_UNIT * SS)) ** 2) * 0.35
    top = np.linspace(1.0, 0.7, h, dtype=np.float32)[:, None]
    alpha = np.clip((line + halo) * top * alpha_peak, 0, 1) * smooth(d, 1.2 * SS)
    rgb = np.broadcast_to(hex_rgb(color)[None, None, :], (h, w, 3)).copy()
    return finish(rgb, alpha)


def solid(radius_units, w_units=W_UNITS, h_units=H_UNITS):
    w = int(w_units * PX_PER_UNIT * SS)
    h = int(h_units * PX_PER_UNIT * SS)
    d = rounded_mask(w, h, radius_units * PX_PER_UNIT * SS) + PAD_UNITS * PX_PER_UNIT * SS
    return finish(np.ones((h, w, 3), dtype=np.float32), smooth(d, 1.2 * SS))


def finish(rgb, alpha):
    """Reduce from the supersampled size and bleed colour under transparent pixels."""
    h, w = alpha.shape
    # premultiplied reduce, so edge colour is not darkened by transparent neighbours
    pm = np.dstack([rgb * alpha[:, :, None], alpha[:, :, None]])
    small = np.asarray(Image.fromarray((pm * 255).round().astype(np.uint8))
                       .resize((w // SS, h // SS), Image.LANCZOS)).astype(np.float32) / 255
    a = small[:, :, 3:]
    col = np.where(a > 1e-3, small[:, :, :3] / np.maximum(a, 1e-3), 0)
    # bleed: spread known colour outwards into the transparent area
    known = (a[:, :, 0] > 0.02).astype(np.float32)
    acc, wgt = col * known[:, :, None], known.copy()
    for _ in range(12):
        blur_c = np.asarray(Image.fromarray((np.clip(acc, 0, 1) * 255).astype(np.uint8))
                            .filter(ImageFilter.BoxBlur(1))).astype(np.float32) / 255
        blur_w = np.asarray(Image.fromarray((wgt * 255).astype(np.uint8))
                            .filter(ImageFilter.BoxBlur(1))).astype(np.float32) / 255
        fill = np.where(blur_w[:, :, None] > 1e-3, blur_c / np.maximum(blur_w[:, :, None], 1e-3), 0)
        acc = np.where(known[:, :, None] > 0, col, fill)
        wgt = np.maximum(known, (blur_w > 1e-3).astype(np.float32))
    out = np.dstack([np.clip(acc, 0, 1), a])
    return Image.fromarray((out * 255).round().astype(np.uint8))


def main():
    swatch = np.asarray(Image.open(sys.argv[1]).convert("RGB").resize((2048, 2048), Image.LANCZOS)
                        ).astype(np.float32) / 255
    out = sys.argv[2]
    r = float(sys.argv[3]) if len(sys.argv) > 3 else 6.0
    rim, gold = "#946e33", "#dba84c"
    sprites = {
        "shape": solid(r),
        "plate": plate(swatch, r, "#2a1c10", rim, 0.42),
        "plate-hover": plate(swatch, r, "#4a3418", gold, 0.50, contrast=0.07),
        "plate-pressed": plate(swatch, r, "#1b1209", rim, 0.30, lift=-0.06),
        "plate-primary": plate(swatch, r, "#6b4718", "#ffc759", 0.62, contrast=0.08),
        "plate-primary-hover": plate(swatch, r, "#8c6124", "#ffc759", 0.75, contrast=0.08),
        "plate-danger": plate(swatch, r, "#8f2f22", "#e6664f", 0.55),
        "plate-danger-hover": plate(swatch, r, "#ad3b2b", "#f08a72", 0.65),
        "plate-danger-pressed": plate(swatch, r, "#6f2218", "#e6664f", 0.40, lift=-0.06),
        "edge": ring(r, gold, 0.85, 0.42),
        "edge-bright": ring(r, "#ffc759", 1.0, 0.5),
        "ground": plate(swatch, r + 2, "#17120d", rim, 0.22, lift=0.03, contrast=0.06,
                        w_units=64, h_units=64),
    }
    for name, image in sprites.items():
        image.save(f"{out}/{name}.png", optimize=True)
    print(r, {n: i.size for n, i in sprites.items()})


if __name__ == "__main__":
    main()
