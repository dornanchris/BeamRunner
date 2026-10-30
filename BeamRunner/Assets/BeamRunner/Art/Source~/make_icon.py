"""App icon (1024x1024, no text): the ship riding the middle layer of three layers of light rails toward the horizon,
a white-hot bridge snapping into the outlined gap just ahead of it, a ramp climbing to the top layer, a gem trail up
there, a neon cube on a rail and the synthwave sun over a grid floor far below the void.
Usage: python3 make_icon.py OUT.png [SUPERSAMPLE_SIZE]
`scene()` is shared with make_adaptive.py (which renders the same view zoomed out into the adaptive safe zone).
"""
import sys, math
import numpy as np
from PIL import Image
from fieldart import *

TILE = 8.0
LANES = (-3.0, 0.0, 3.0)
LAYERS = (-3.0, 0.0, 3.0)          # rail heights: bottom, middle (the ship's), top
FAR = 150.0
LAYER_COLOR = (MAGENTA, CYAN, YELLOW)
LAYER_GAIN = (0.45, 1.3, 0.9)
FIRST_TILE = (0, 0, 1)             # the top layer starts a tile ahead

# Gaps (layer, lane, tile); the ship's lane has a gap on tile 1, being bridged.
GAPS = {(1, 1, 1), (1, 0, 3), (1, 0, 4), (0, 2, 2), (0, 2, 3), (2, 1, 5), (2, 2, 4), (0, 1, 6)}
RAMP_UP = {(1, 0, 2)}               # middle-left lane climbs to the top layer on tile 2
CUBE = (1, 2, 4)                    # a cube on the middle-right rail, tile 4


def P(x, y, z):
    return np.array([x, y, z], np.float32)


def scene(S, zoom=1.0, env=True, track=True, ship_layer=True, seed=11, H=None, horizon=0.40, ship_y=0.80, fov=62.0,
          stars=380):
    """Renders the icon view into a new Scene (S wide, H tall; square by default). `zoom` < 1 shrinks the view toward
    the centre (adaptive safe zone). Returns (scene, camera, inner_mask or None, hull_mask or None)."""
    rng = np.random.default_rng(seed)
    H = S if H is None else H
    sc = Scene(S, H)
    fov = 2.0 * math.degrees(math.atan(math.tan(math.radians(fov * 0.5)) / zoom))
    cy = 0.5 + (horizon - 0.5) * zoom
    cam = Camera(sc, (0.0, 2.2, -5.5), yaw=0.0, pitch=7.0, roll=-3.0, fov=fov, center=(S * 0.5, H * cy))
    if env:
        environment(sc, cam, rng, floor_y=-34.0, grid=8.0, sun_az=0.0, sun_radius=9.5, sun_gain=1.25, stars=stars,
                    grid_gain=0.3, sky_gain=0.85, grid_color=lerp(MAGENTA, VIOLET, 0.5))
    fade = fog_fn(10.0, 140.0, 0.12)
    if track:
        for layer in range(3):
            segs = []
            ticks = []
            for lane in range(3):
                tile = FIRST_TILE[layer]
                z = tile * TILE
                while z < FAR:
                    x = LANES[lane]
                    y = LAYERS[layer]
                    if (layer, lane, tile) in RAMP_UP:
                        segs.append((P(x, y, z), P(x, y + 3.0, z + TILE)))
                    elif (layer, lane, tile) not in GAPS:
                        segs.append((P(x, y, z), P(x, y, z + TILE)))
                        ticks.append((P(x - 0.55, y, z), P(x + 0.55, y, z), 0.6))
                    z += TILE
                    tile += 1
            neon_lines(sc, cam, segs, LAYER_COLOR[layer], width=0.16, gain=LAYER_GAIN[layer], fade=fade, chunks=12)
            neon_lines(sc, cam, ticks, LAYER_COLOR[layer], width=0.07, gain=LAYER_GAIN[layer] * 0.6, fade=fade)

        # The bridge snapping into the gap ahead (white-hot), inside the ghost outline of the tile being built.
        bridge = [(P(0.0, 0.0, TILE), P(0.0, 0.0, TILE * 1.82))]
        neon_lines(sc, cam, bridge, lerp(WHITE, CYAN, 0.25), width=0.22, gain=2.6, fade=fade, chunks=6)
        gx0, gx1, gz0, gz1 = -0.95, 0.95, TILE + 0.15, TILE * 2 - 0.15
        outline = [(P(gx0, 0.02, gz0), P(gx0, 0.02, gz1)), (P(gx1, 0.02, gz0), P(gx1, 0.02, gz1)),
                   (P(gx0, 0.02, gz0), P(gx1, 0.02, gz0)), (P(gx0, 0.02, gz1), P(gx1, 0.02, gz1))]
        neon_lines(sc, cam, outline, lerp(YELLOW, WHITE, 0.3), width=0.07, gain=1.3, fade=fade)

        items = []
        for i in range(7):
            z = TILE * 2 + 2.0 + i * 4.0
            items.append(Cube(P(LANES[0], LAYERS[2] + 0.9, z), 0.42, rot=(45, 35 + i * 9, 0), color=YELLOW, width=0.07))
        items.append(Cube(P(LANES[CUBE[1]], LAYERS[CUBE[0]] + 1.2, CUBE[2] * TILE + TILE * 0.5), 2.4, color=MAGENTA,
                          width=0.10))
        draw_items(sc, cam, items, fade)

    inner = None
    hull = None
    if ship_layer:
        k = zoom
        hull, inner = ship(sc, S * 0.50, H * (0.5 + (ship_y - 0.5) * k), S * 0.215 * k, S * 0.155 * k,
                          roll=math.radians(-3), trail_end=H * (0.5 + (ship_y - 0.5 + 0.18) * k))
    return sc, cam, inner, hull


if __name__ == "__main__":
    OUT = sys.argv[1] if len(sys.argv) > 1 else "icon.png"
    S = int(sys.argv[2]) if len(sys.argv) > 2 else 2048  # supersampled; downsampled to 1024
    sc, cam, inner, _ = scene(S)
    lin = render(sc, bloom=1.05, exposure=1.0, inner=inner, inner_dark=0.5, vignette=0.32, vcenter=(S * 0.5, S * 0.45))
    img = dither(to_srgb8(lin), np.random.default_rng(7), 0.8)
    Image.fromarray(img, "RGB").resize((1024, 1024), Image.LANCZOS).save(OUT, optimize=True)
    print("wrote", OUT)
