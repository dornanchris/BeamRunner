"""Portrait splash art for the loading screen (1152x2496, about 9:19.5; no text: the screen sets the logo, progress
bar and tips on top). The app icon's view (make_icon.scene) in portrait: the ship riding three layers of light rails, a
bridge snapping into the gap ahead, a ramp, gems and a cube, the synthwave sun at the vanishing point just above the
middle (the push-in focus, SplashScreen.ArtFocus). The top third is starry sky (the logo goes there), the bottom fifth
calm (progress bar and tips). Key elements stay inside the middle 70 % of the width, so an aspect-fill crop on wider
screens (tablets) keeps them.
Usage: python3 make_splash.py OUT.png [SUPERSAMPLE_WIDTH]
"""
import sys
import numpy as np
from PIL import Image
from fieldart import *
from make_icon import scene

OUT = sys.argv[1] if len(sys.argv) > 1 else "splash.png"
OUT_W, OUT_H = 1152, 2496
W = int(sys.argv[2]) if len(sys.argv) > 2 else 1728      # 1.5x supersample
H = W * OUT_H // OUT_W

sc, cam, inner, hull = scene(W, H=H, horizon=0.47, ship_y=0.715, fov=70.0, stars=900, seed=13)

# Keep the logo band (top) and the progress / tips band (bottom) calm: dim the scene there a little.
top = smoothstep(0.30, 0.10, sc.yy / H)
bottom = smoothstep(0.80, 0.97, sc.yy / H)
keep = (1.0 - 0.45 * top - 0.35 * bottom)[..., None]
sc.emit *= keep
sc.base *= keep

lin = render(sc, bloom=1.0, inner=inner, inner_dark=0.5, vignette=0.32, vcenter=(W * 0.5, H * 0.52))
img = dither(to_srgb8(lin), np.random.default_rng(13), 0.8)
Image.fromarray(img, "RGB").resize((OUT_W, OUT_H), Image.LANCZOS).save(OUT, optimize=True)
print("wrote", OUT)
