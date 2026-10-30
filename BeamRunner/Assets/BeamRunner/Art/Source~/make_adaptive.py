"""Android adaptive icon layers (1024x1024): full-bleed backdrop + transparent ship foreground.

Both layers render the app icon's view (make_icon.scene) zoomed out so the visible part of the adaptive icon (the
central 72/108 of the layer) matches the regular icon: the background holds the sky, the sun, the floor grid far below
and the rails with the bridge snapping in; the foreground holds only the ship, inside the 61% safe circle.
"""
import sys, math
import numpy as np
from PIL import Image
from fieldart import *
from make_icon import scene

OUT_BG, OUT_FG = sys.argv[1], sys.argv[2]
S = int(sys.argv[3]) if len(sys.argv) > 3 else 2048
ZOOM = 72.0 / 108.0

# Background: everything but the ship.
sc, cam, _, _ = scene(S, ZOOM, env=True, track=True, ship_layer=False)
lin = render(sc, bloom=1.05, vignette=0.25, vcenter=(S * 0.5, S * 0.47))
rng = np.random.default_rng(11)
Image.fromarray(dither(to_srgb8(lin), rng, 0.8), "RGB").resize((1024, 1024), Image.LANCZOS).save(OUT_BG, optimize=True)

# Foreground: the ship on transparency (inside the safe circle).
sc, cam, inner, hull = scene(S, ZOOM, env=False, track=False, ship_layer=True)
lin = render(sc, bloom=1.0, inner=inner, inner_dark=0.5, vignette=0.0)
alpha = np.clip(np.maximum(np.maximum(hull, sc.cover), lin.max(axis=2) * 1.15), 0, 1)
solid = alpha > 0.5
r_max = float(np.sqrt((sc.xx[solid] - S / 2) ** 2 + (sc.yy[solid] - S / 2) ** 2).max()) / S if solid.any() else 0.0
print("foreground solids reach %.3f of the layer (safe circle: %.3f)" % (r_max, 33 / 108))
color = np.where(alpha[..., None] > 1e-4, lin / np.maximum(alpha[..., None], 1e-4), 0)
rgb8 = to_srgb8(np.clip(color, 0, 1))
a8 = (alpha * 255 + 0.5).astype(np.uint8)
Image.fromarray(np.dstack([rgb8, a8]), "RGBA").resize((1024, 1024), Image.LANCZOS).save(OUT_FG, optimize=True)
print("wrote", OUT_BG, OUT_FG)
