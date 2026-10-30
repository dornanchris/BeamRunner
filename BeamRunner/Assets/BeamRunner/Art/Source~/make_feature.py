"""2048x1024 promo / feature graphic: the BEAMRUNNER logo on the left, the gate scene on the right.
Usage: python3 make_feature.py OUT.png TITLE_FONT.ttf BODY_FONT.ttf [SUPERSAMPLE_WIDTH]
"""
import sys, math
import numpy as np
from PIL import Image, ImageFont
from scipy.ndimage import minimum_filter
from fieldart import *

OUT = sys.argv[1]
FONT_TITLE = sys.argv[2]
FONT_BODY = sys.argv[3]
W = int(sys.argv[4]) if len(sys.argv) > 4 else 3072   # 1.5x supersample of 2048x1024
H = W // 2
rng = np.random.default_rng(5)

sc = Scene(W, H)
cam = Camera(sc, (0.0, 10.2, 0.0), yaw=0.0, pitch=1.0, roll=-2.5, fov=50.0, center=(W * 0.5, H * 0.5))

# The main gate sits right of centre; the sun shines through its opening.
GATE = cam.at(0.715, 0.455, 22.0)
to_gate = GATE - cam.pos
environment(sc, cam, rng, floor_y=0.0, grid=4.0, sun_az=math.degrees(math.atan2(to_gate[0], to_gate[2])),
            sun_radius=6.0, sun_gain=1.2, stars=900, grid_gain=0.6, sky_gain=0.85,
            grid_color=lerp(MAGENTA, VIOLET, 0.45))

gate = Gate(GATE, open_cells=((4, 4),), yaw=30.0, color=MAGENTA, rim=YELLOW, width=0.10, rim_width=0.17,
            gain=0.55, rim_gain=3.0, frame_gain=2.0, jitter=0.25, focus=(0.30, 2.4), grid_core=0.35,
            bevel=0.09, spill=(0.05, 1.3))
items = [
    gate,
    # the next gate down the field, peeking out behind the first one
    Gate(cam.at(0.515, 0.47, 80.0), open_cells=((2, 5),), yaw=8.0, color=MAGENTA, rim=YELLOW, width=0.24,
         rim_width=0.36, gain=0.6, rim_gain=2.6, frame_gain=1.8, focus=(0.35, 2.0), grid_core=0.35, seed=2),
    # cubes: near and large on the right, scattered and small further away (kept clear of the logo)
    Cube(cam.at(0.93, 0.17, 9.0), 1.7, rot=(40, 12, -18), color=CYAN, width=0.06),
    Cube(cam.at(0.575, 0.13, 13.0), 1.2, rot=(24, 38, 14), color=YELLOW, width=0.055),
    Cube(cam.at(0.95, 0.80, 8.0), 1.5, rot=(-18, 28, 32), color=MAGENTA, width=0.06),
    Cube(cam.at(0.545, 0.72, 14.0), 1.0, rot=(30, 20, 50), color=CYAN, width=0.055),
    Cube(cam.at(0.47, 0.84, 22.0), 1.4, rot=(12, 30, 20), color=MAGENTA, width=0.07),
    Cube(cam.at(0.24, 0.80, 30.0), 2.0, rot=(35, 40, 20), color=YELLOW, width=0.10),
    Cube(cam.at(0.05, 0.10, 60.0), 3.0, rot=(-15, 25, 40), color=CYAN, width=0.14),
    Cube(cam.at(0.06, 0.70, 26.0), 1.8, rot=(5, 45, 10), color=MAGENTA, width=0.09),
    Cube(cam.at(0.43, 0.07, 45.0), 2.2, rot=(20, -30, 15), color=MAGENTA, width=0.11),
]
draw_items(sc, cam, items, fog_fn(14.0, 140.0, 0.3))
hole = sc.draw(lambda d: [d.polygon(cam.clip_poly(q), fill=255) for q in gate.hole_quads()])
light_rays(sc, hole, cam.project(GATE)[0], steps=36, reach=2.4, decay=0.92, gain=0.30, soft=0.008)
gate.hole_glow(sc, cam, gain=0.10)

# darken the left side so the title reads cleanly
fade = np.clip((sc.xx / W - 0.30) / 0.30, 0, 1) ** 1.2
keep = (0.22 + 0.78 * fade)[..., None]
sc.emit *= keep
sc.base *= keep

hull, inner = ship(sc, W * 0.715, H * 0.775, H * 0.150, H * 0.112, roll=math.radians(-3))

# Logo: "BEAMRUNNER" (white-hot core with cyan/magenta ghosts, like the in-game NeonTitle) over a yellow "3D",
# a fading divider and the tagline, centred in the left column.
col_c = W * 0.225
big = ImageFont.truetype(FONT_TITLE, int(H * 0.118))
mid = ImageFont.truetype(FONT_TITLE, int(H * 0.120))
body = ImageFont.truetype(FONT_BODY, int(H * 0.038))
t_big, t_mid, t_body = int(H * 0.004), int(H * 0.020), int(H * 0.0075)
TITLE, SUB, TAG = "BEAMRUNNER", "3D", "DODGE THE CUBES · THREAD THE GATES"

w_big = text_width(TITLE, big, t_big)
m_cube, _ = text_mask(sc, TITLE, big, (col_c - w_big / 2, H * 0.115), tracking=t_big)
neon_title(sc, m_cube, CYAN, MAGENTA, H * 0.006, gain=1.0)

w_mid = text_width(SUB, mid, t_mid)
m_3d, _ = text_mask(sc, SUB, mid, (col_c - w_mid / 2, H * 0.265), tracking=t_mid)
sc.base *= (1 - 0.7 * np.clip(blur(m_3d, H * 0.01) * 1.5, 0, 1))[..., None]
sc.emit *= (1 - 0.7 * np.clip(blur(m_3d, H * 0.01) * 1.5, 0, 1))[..., None]
sc.add(m_3d, YELLOW, 1.5)
sc.add(minimum_filter(m_3d, size=int(H * 0.008) | 1), WHITE, 0.35)

line_y = H * 0.445
half = w_big * 0.42
line = sc.draw(lambda d: d.line([(col_c - half, line_y), (col_c + half, line_y)], fill=255, width=max(2, int(H * 0.003))))
sc.add(line * np.clip(1 - np.abs(sc.xx - col_c) / half, 0, 1) ** 0.7, MAGENTA, 1.4)

w_tag = text_width(TAG, body, t_body)
tag, _ = text_mask(sc, TAG, body, (col_c - w_tag / 2, H * 0.465), tracking=t_body)
sc.add(tag, lerp(CYAN, WHITE, 0.55), 1.0)

lin = render(sc, bloom=1.0, inner=inner, inner_dark=0.5, vignette=0.30, vcenter=(W * 0.55, H * 0.5))
img = dither(to_srgb8(lin), rng, 0.8)
Image.fromarray(img, "RGB").resize((2048, 1024), Image.LANCZOS).save(OUT, optimize=True)
print("wrote", OUT)
