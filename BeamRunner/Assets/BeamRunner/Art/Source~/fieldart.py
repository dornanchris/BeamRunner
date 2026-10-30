"""Procedural key art for BeamRunner (app icon, adaptive icon layers, feature graphic).

Composition model: a dark `base` layer plus an additive HDR `emit` layer of sharp neon lines.
Glow comes from a multi-scale bloom of the emissive layer, then a filmic tone-map to sRGB.
Rendered supersampled and downsampled for anti-aliasing.

3D: a pinhole `Camera` projects world geometry onto the Scene. World axes are the game's (x right, y up, z forward,
metres; the field is 9 x 9 cells of 2 m, gates are 1 m deep). Solids are drawn back to front (painter's order): their
dark faces occlude whatever is behind them, then their neon edges are added as tapered tubes whose width follows the
perspective. `environment()` paints the synthwave backdrop: starry sky, striped sun on the horizon, neon grid floor.
Everything is deterministic for a given seed.
"""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy.ndimage import gaussian_filter, minimum_filter


def hex_lin(h):
    """sRGB hex colour -> linear RGB (the renderer works in linear light; to_srgb8 encodes at the end)."""
    h = h.lstrip("#")
    c = np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4).astype(np.float32)


# Game palette (UITheme accents / level-1 ZoneTheme), linear RGB.
MAGENTA = hex_lin("#FF2BD6")
CYAN = hex_lin("#00E5FF")
YELLOW = hex_lin("#FFE45C")
BASE = hex_lin("#07030F")
FOG = hex_lin("#1A0630")
# Supporting tones.
PINK = np.array([1.00, 0.06, 0.35], np.float32)
ORANGE = np.array([1.00, 0.30, 0.04], np.float32)
VIOLET = np.array([0.46, 0.03, 1.00], np.float32)
BLUE = np.array([0.08, 0.28, 1.00], np.float32)
WHITE = np.array([1.0, 1.0, 1.0], np.float32)


def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def mask_draw(w, h):
    img = Image.new("L", (w, h), 0)
    return img, ImageDraw.Draw(img)


def as_f(img):
    return np.asarray(img, np.float32) / 255.0


def _blur2d(a, sigma):
    a = np.ascontiguousarray(a, np.float32)
    if sigma < 6:
        return gaussian_filter(a, sigma=sigma, mode="constant", truncate=3.0)
    # Wide kernels: blur a box-downsampled copy and scale it back up (same look, a fraction of the cost).
    k = max(1, int(sigma / 3))
    h, w = a.shape
    small = np.asarray(Image.fromarray(a, "F").resize((max(1, w // k), max(1, h // k)), Image.BOX))
    small = gaussian_filter(small, sigma=sigma / k, mode="constant", truncate=3.0)
    return np.asarray(Image.fromarray(np.ascontiguousarray(small, np.float32), "F").resize((w, h), Image.BILINEAR))


def blur(a, sigma):
    if sigma <= 0:
        return a
    if a.ndim == 3:
        return np.dstack([_blur2d(a[..., i], sigma) for i in range(a.shape[2])])
    return _blur2d(a, sigma)


class Scene:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.s = max(w, h)
        self.base = np.zeros((h, w, 3), np.float32)
        self.emit = np.zeros((h, w, 3), np.float32)
        self.cover = np.zeros((h, w), np.float32)    # opaque solid coverage (alpha for transparent layers)
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        self.xx, self.yy = xx, yy

    def add(self, mask, color, gain=1.0, layer="emit"):
        arr = self.emit if layer == "emit" else self.base
        arr += mask[..., None] * (np.asarray(color, np.float32) * gain)[None, None, :]

    def occlude(self, mask, color):
        m = mask[..., None]
        self.emit *= (1 - m)
        self.base = self.base * (1 - m) + m * np.asarray(color, np.float32)[None, None, :]

    def draw(self, fn):
        img, d = mask_draw(self.w, self.h)
        fn(d)
        return as_f(img)

    def radial(self, c):
        return np.sqrt((self.xx - c[0]) ** 2 + (self.yy - c[1]) ** 2)


# ---------------------------------------------------------------------------------------------- camera

class Camera:
    """Pinhole camera. Angles in degrees: yaw turns right, pitch looks up, roll banks the view (horizon tilts).
    `fov` is the vertical field of view; `center` the principal point on screen (default: image centre)."""

    def __init__(self, sc, pos, yaw=0.0, pitch=0.0, roll=0.0, fov=60.0, center=None, near=0.25):
        self.sc = sc
        self.pos = np.asarray(pos, np.float64)
        y, p, r = (math.radians(v) for v in (yaw, pitch, roll))
        fwd = np.array([math.sin(y) * math.cos(p), math.sin(p), math.cos(y) * math.cos(p)])
        right = np.cross([0.0, 1.0, 0.0], fwd)
        right /= np.linalg.norm(right)
        up = np.cross(fwd, right)
        self.fwd = fwd
        self.right = right * math.cos(r) + up * math.sin(r)
        self.up = up * math.cos(r) - right * math.sin(r)
        self.M = np.stack([self.right, self.up, self.fwd])
        self.f = sc.h / (2 * math.tan(math.radians(fov) / 2))
        self.c = center if center is not None else (sc.w / 2, sc.h / 2)
        self.near = near

    def to_cam(self, p):
        return self.M @ (np.asarray(p, np.float64) - self.pos)

    def to_screen(self, q):
        return (self.c[0] + self.f * q[0] / q[2], self.c[1] - self.f * q[1] / q[2])

    def project(self, p):
        q = self.to_cam(p)
        return self.to_screen(q), q[2]

    def depth(self, p):
        return float(self.to_cam(p)[2])

    def at(self, fx, fy, z):
        """World point at camera depth `z` that projects to (fx, fy), given as fractions of the image size."""
        sx, sy = fx * self.sc.w, fy * self.sc.h
        return self.pos + self.right * ((sx - self.c[0]) / self.f * z) + self.up * ((self.c[1] - sy) / self.f * z) \
            + self.fwd * z

    def clip_poly(self, pts):
        """World polygon -> screen polygon clipped to the near plane (None if fully behind)."""
        q = [self.to_cam(p) for p in pts]
        out = []
        for i in range(len(q)):
            a, b = q[i], q[(i + 1) % len(q)]
            ina, inb = a[2] >= self.near, b[2] >= self.near
            if ina:
                out.append(a)
            if ina != inb:
                out.append(a + (b - a) * ((self.near - a[2]) / (b[2] - a[2])))
        if len(out) < 3:
            return None
        return [self.to_screen(v) for v in out]

    def clip_seg(self, a, b, margin=None):
        """World segment -> camera-space segment clipped to the near plane and the (slightly widened) view
        frustum, or None."""
        qa, qb = self.to_cam(a), self.to_cam(b)
        m = self.sc.s * 0.08 if margin is None else margin
        f, (cx, cy), W, H = self.f, self.c, self.sc.w, self.sc.h
        planes = ((np.array([0.0, 0.0, 1.0]), -self.near),
                  (np.array([f, 0.0, cx + m]), 0.0), (np.array([-f, 0.0, W + m - cx]), 0.0),
                  (np.array([0.0, -f, cy + m]), 0.0), (np.array([0.0, f, H + m - cy]), 0.0))
        for n, off in planes:
            da, db = float(n @ qa) + off, float(n @ qb) + off
            if da < 0 and db < 0:
                return None
            if da < 0:
                qa = qa + (qb - qa) * (da / (da - db))
            elif db < 0:
                qb = qb + (qa - qb) * (db / (db - da))
        return qa, qb

    def rays(self):
        """Per-pixel unit view directions (world), as three float32 arrays."""
        sc = self.sc
        dx = (sc.xx - self.c[0]) / self.f
        dy = (self.c[1] - sc.yy) / self.f
        r, u, f = (v.astype(np.float32) for v in (self.right, self.up, self.fwd))
        X = dx * r[0] + dy * u[0] + f[0]
        Y = dx * r[1] + dy * u[1] + f[1]
        Z = dx * r[2] + dy * u[2] + f[2]
        n = np.sqrt(X * X + Y * Y + Z * Z)
        return X / n, Y / n, Z / n


# ---------------------------------------------------------------------------------------------- neon lines

def neon_lines(sc, cam, segs, color, width=0.10, gain=1.0, core=0.38, core_gain=0.9, min_px=1.3,
               fade=None, chunks=1, caps=True, max_px=None):
    """Glowing tubes along world segments. `segs`: (a, b) or (a, b, k) with an intensity factor k.
    Width is in metres (perspective); below `min_px` a tube keeps min_px and dims instead (no shimmer).
    `fade(z)` -> intensity factor by camera depth (fog / density fade). Long segments are split into
    `chunks` screen-uniform pieces so width and fade follow the depth along them."""
    tube_img, td = mask_draw(sc.w, sc.h)
    core_img, cd = mask_draw(sc.w, sc.h)
    any_drawn = False
    for sg in segs:
        a, b = sg[0], sg[1]
        k = sg[2] if len(sg) > 2 else 1.0
        cl = cam.clip_seg(a, b)
        if cl is None:
            continue
        qa, qb = cl
        ia, ib = 1.0 / qa[2], 1.0 / qb[2]
        pts = []
        for i in range(chunks + 1):
            s = i / chunks
            w = (1 - s) * ia + s * ib                       # 1/z is linear in screen space
            q = (qa * (1 - s) * ia + qb * s * ib) / w
            pts.append((cam.to_screen(q), q[2]))
        for (p0, z0), (p1, z1) in zip(pts[:-1], pts[1:]):
            zm = 0.5 * (z0 + z1)
            inten = k * (fade(zm) if fade else 1.0)
            rr = []
            for z in (z0, z1):
                px = cam.f * width / z
                if max_px:
                    px = min(px, max_px)
                rr.append(max(px, min_px) * 0.5)
            px_m = cam.f * width / zm
            if px_m < min_px:
                inten *= px_m / min_px
            v = int(round(255 * min(1.0, inten)))
            if v <= 0:
                continue
            dx, dy = p1[0] - p0[0], p1[1] - p0[1]
            ln = math.hypot(dx, dy)
            if ln < 1e-6:
                continue
            nx, ny = -dy / ln, dx / ln
            for d, frac in ((td, 1.0), (cd, core)):
                r0, r1 = rr[0] * frac, rr[1] * frac
                if frac < 1.0 and r0 < 0.45 and r1 < 0.45:
                    continue
                d.polygon([(p0[0] + nx * r0, p0[1] + ny * r0), (p1[0] + nx * r1, p1[1] + ny * r1),
                           (p1[0] - nx * r1, p1[1] - ny * r1), (p0[0] - nx * r0, p0[1] - ny * r0)], fill=v)
                if caps:
                    for (x, y), r in ((p0, r0), (p1, r1)):
                        d.ellipse([x - r, y - r, x + r, y + r], fill=v)
            any_drawn = True
    if not any_drawn:
        return
    sc.add(as_f(tube_img), color, gain)
    sc.add(as_f(core_img), lerp(np.asarray(color, np.float32), WHITE, 0.62), gain * core_gain)


def fill_polys(sc, polys):
    """Opaque faces: list of (screen_polygon, linear_rgb). Later polygons cover earlier ones.
    The faces replace the base and cut the emissive layer behind them. Returns the coverage mask."""
    img = Image.new("I", (sc.w, sc.h), 0)
    d = ImageDraw.Draw(img)
    lut = [np.zeros(3, np.float32)]
    for pts, col in polys:
        if pts is None:
            continue
        lut.append(np.asarray(col, np.float32))
        d.polygon(pts, fill=len(lut) - 1)
    if len(lut) == 1:
        return np.zeros((sc.h, sc.w), np.float32)
    idx = np.asarray(img).astype(np.int32)
    m = (idx > 0).astype(np.float32)
    col = np.stack(lut).astype(np.float32)[idx]
    sc.emit *= (1 - m)[..., None]
    sc.base = sc.base * (1 - m)[..., None] + col * m[..., None]
    sc.cover = np.maximum(sc.cover, m)
    return m


def euler(rx, ry, rz):
    """Rotation matrix from Euler angles in degrees (applied x, then y, then z)."""
    x, y, z = (math.radians(v) for v in (rx, ry, rz))
    Rx = np.array([[1, 0, 0], [0, math.cos(x), -math.sin(x)], [0, math.sin(x), math.cos(x)]])
    Ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    Rz = np.array([[math.cos(z), -math.sin(z), 0], [math.sin(z), math.cos(z), 0], [0, 0, 1]])
    return Rz @ Ry @ Rx


LIGHT = np.array([0.35, 0.80, -0.48])
LIGHT /= np.linalg.norm(LIGHT)
FACE = np.array([0.010, 0.006, 0.026], np.float32)   # dark violet-navy solid body


def face_color(normal, edge_color, tint=0.012, dark=1.0):
    lam = max(0.0, float(np.dot(normal, LIGHT)))
    return (FACE * (0.55 + 0.9 * lam) + np.asarray(edge_color, np.float32) * tint) * dark


def fog_fn(start, length, floor=0.0):
    return lambda z: floor + (1 - floor) * math.exp(-max(0.0, z - start) / length)


class Cube:
    """Neon-edged solid cube (the field's obstacles): dark faces, glowing edges on the visible faces."""

    def __init__(self, center, size, rot=(0, 0, 0), color=MAGENTA, gain=1.0, width=0.09, tint=0.014):
        self.center = np.asarray(center, np.float64)
        self.size, self.color, self.gain, self.width, self.tint = size, color, gain, width, tint
        R = euler(*rot)
        h = size / 2
        self.faces = []
        for axis in range(3):
            for sgn in (-1, 1):
                b, c = [i for i in range(3) if i != axis]
                quad = []
                for u, v in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                    lp = np.zeros(3)
                    lp[axis], lp[b], lp[c] = sgn, u, v
                    quad.append(self.center + R @ (lp * h))
                n = np.zeros(3)
                n[axis] = sgn
                self.faces.append((quad, R @ n))

    def depth(self, cam):
        return cam.depth(self.center)

    def draw(self, sc, cam, fade=None):
        vis = [(q, n) for q, n in self.faces if np.dot(n, cam.pos - np.mean(q, axis=0)) > 1e-6]
        k = fade(self.depth(cam)) if fade else 1.0
        fill_polys(sc, [(cam.clip_poly(q), face_color(n, self.color, self.tint)) for q, n in vis])
        edges = {}
        for q, _ in vis:
            for i in range(4):
                a, b = q[i], q[(i + 1) % 4]
                key = tuple(sorted((tuple(np.round(a, 6)), tuple(np.round(b, 6)))))
                edges[key] = (a, b, k)
        neon_lines(sc, cam, list(edges.values()), self.color, self.width, self.gain)


class Gate:
    """A gate: wall of n x n blocks (cell metres square, `depth` deep) with open cells to fly through.
    `center` is the centre of the front face; `yaw` (degrees) turns the wall about the vertical axis.
    Open cells are (col, row) with (0, 0) bottom-left. The rim of each opening glows in `rim` colour and its
    inner side walls are lit by the light coming through."""

    def __init__(self, center, cell=2.0, n=9, depth=1.0, open_cells=((4, 4),), yaw=0.0, color=MAGENTA,
                 rim=YELLOW, gain=1.0, rim_gain=1.6, width=0.10, rim_width=0.14, frame_gain=1.4, jitter=0.0,
                 seed=1, focus=None, grid_core=0.9, bevel=0.0, spill=None):
        self.center = np.asarray(center, np.float64)
        y = math.radians(yaw)
        self.U = np.array([math.cos(y), 0.0, -math.sin(y)])
        self.V = np.array([0.0, 1.0, 0.0])
        self.W = np.array([math.sin(y), 0.0, math.cos(y)])
        self.cell, self.n, self.d = cell, n, depth
        self.open = set(open_cells)
        self.color, self.rim, self.gain, self.rim_gain = color, rim, gain, rim_gain
        self.width, self.rim_width, self.frame_gain = width, rim_width, frame_gain
        self.rng = np.random.default_rng(seed)
        self.jitter = jitter
        self.focus = focus          # (floor, cells): edge brightness falls off away from the openings
        self.grid_core = grid_core  # white-hot core strength of the interior grid lines
        self.bevel = bevel          # bevel width on the block faces, in cells (0 = flat)
        self.spill = spill          # (gain, cells): light from the openings washing over the wall

    def _k(self, k, u, v):
        if not self.focus or not self.open:
            return k
        fl, cells = self.focus
        d = min(math.hypot(u - (c + 0.5), v - (r + 0.5)) for c, r in self.open)
        return k * (fl + (1 - fl) * math.exp(-max(0.0, d - 0.5) / cells))

    def P(self, u, v, w=0.0):
        """Gate-local (u, v in cells from the bottom-left corner, w in metres back) -> world."""
        h = self.n / 2
        return self.center + self.U * ((u - h) * self.cell) + self.V * ((v - h) * self.cell) + self.W * w

    def blocked(self, c, r):
        return 0 <= c < self.n and 0 <= r < self.n and (c, r) not in self.open

    def depth(self, cam):
        return cam.depth(self.P(self.n / 2, self.n / 2, self.d / 2))

    def hole_quads(self):
        return [[self.P(c, r), self.P(c + 1, r), self.P(c + 1, r + 1), self.P(c, r + 1)] for (c, r) in self.open]

    def draw(self, sc, cam, fade=None):
        k = fade(self.depth(cam)) if fade else 1.0
        D = self.d
        # Side walls: every block side facing an open cell or the outside of the wall.
        sides, side_edges_rim, side_edges = [], [], []
        nbrs = ((-1, 0, (0, 0), (0, 1), -self.U), (1, 0, (1, 0), (1, 1), self.U),
                (0, -1, (0, 0), (1, 0), -self.V), (0, 1, (0, 1), (1, 1), self.V))
        for c in range(self.n):
            for r in range(self.n):
                if not self.blocked(c, r):
                    continue
                for dc, dr, e0, e1, nrm in nbrs:
                    oc, orr = c + dc, r + dr
                    if self.blocked(oc, orr):
                        continue
                    a, b = (c + e0[0], r + e0[1]), (c + e1[0], r + e1[1])
                    quad = [self.P(*a), self.P(*b), self.P(*b, D), self.P(*a, D)]
                    if np.dot(nrm, cam.pos - np.mean(quad, axis=0)) <= 1e-6:
                        continue
                    inner = 0 <= oc < self.n and 0 <= orr < self.n
                    col = (face_color(nrm, self.rim, 0.10, 1.0) + np.asarray(self.rim) * 0.05) if inner \
                        else face_color(nrm, self.color, 0.02)
                    sides.append((cam.clip_poly(quad), col))
                    lst = side_edges_rim if inner else side_edges
                    kk = self._k(k, (a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
                    lst += [(quad[1], quad[2], kk), (quad[2], quad[3], kk), (quad[3], quad[0], kk)]
        fill_polys(sc, sides)
        neon_lines(sc, cam, side_edges, self.color, self.width, self.gain * self.frame_gain)
        neon_lines(sc, cam, side_edges_rim, self.rim, self.rim_width * 0.8, self.gain * self.rim_gain * 0.8)

        # Front faces: per-block variation and a shallow bevel (lit top/left, shaded bottom/right) so the wall
        # reads as separate blocks; light from the openings spills over the faces around them.
        fronts = []
        b = self.bevel
        for c in range(self.n):
            for r in range(self.n):
                if not self.blocked(c, r):
                    continue
                j = 1.0 + self.jitter * float(self.rng.uniform(-1, 1))
                base = face_color(-self.W, self.color, 0.016, j)
                o = [(c, r), (c + 1, r), (c + 1, r + 1), (c, r + 1)]
                i = [(c + b, r + b), (c + 1 - b, r + b), (c + 1 - b, r + 1 - b), (c + b, r + 1 - b)]
                if b > 0:
                    for k0, shade in ((0, 0.45), (1, 0.65), (2, 2.6), (3, 1.8)):   # bottom, right, top, left
                        k1 = (k0 + 1) % 4
                        q = [self.P(*o[k0]), self.P(*o[k1]), self.P(*i[k1]), self.P(*i[k0])]
                        fronts.append((cam.clip_poly(q), base * shade))
                fronts.append((cam.clip_poly([self.P(*p) for p in i] if b > 0 else [self.P(*p) for p in o]), base))
        wall = fill_polys(sc, fronts)
        if self.spill and self.open:
            gain, cells = self.spill
            for (c, r) in self.open:
                (hx, hy), hz = cam.project(self.P(c + 0.5, r + 0.5))
                px = cam.f * self.cell / hz
                d = np.sqrt((sc.xx - hx) ** 2 + (sc.yy - hy) ** 2) / px
                sc.base += (wall * np.exp(-np.maximum(d - 0.5, 0) / cells) * gain * k)[..., None] \
                    * lerp(np.asarray(self.rim), np.asarray(self.color), 0.35)[None, None, :]

        # Grid edges on the front: interior lines, the outer frame, and the hot rim around each opening.
        grid, frame, rim = [], [], []
        for c in range(self.n + 1):
            for r in range(self.n):
                left, right = self.blocked(c - 1, r), self.blocked(c, r)
                if not (left or right):
                    continue
                seg = (self.P(c, r), self.P(c, r + 1), self._k(k, c, r + 0.5))
                if (c - 1, r) in self.open or (c, r) in self.open:
                    rim.append(seg)
                elif c in (0, self.n):
                    frame.append(seg)
                else:
                    grid.append(seg)
        for r in range(self.n + 1):
            for c in range(self.n):
                below, above = self.blocked(c, r - 1), self.blocked(c, r)
                if not (below or above):
                    continue
                seg = (self.P(c, r), self.P(c + 1, r), self._k(k, c + 0.5, r))
                if (c, r - 1) in self.open or (c, r) in self.open:
                    rim.append(seg)
                elif r in (0, self.n):
                    frame.append(seg)
                else:
                    grid.append(seg)
        neon_lines(sc, cam, grid, self.color, self.width, self.gain, core_gain=self.grid_core)
        neon_lines(sc, cam, frame, self.color, self.width * 1.25, self.gain * self.frame_gain)
        neon_lines(sc, cam, rim, self.rim, self.rim_width, self.gain * self.rim_gain)

    def hole_glow(self, sc, cam, color=None, gain=0.5, soft=0.006):
        """Light pouring through the openings (additive haze over the hole, brightest at its rim)."""
        col = self.rim if color is None else color
        m = sc.draw(lambda d: [d.polygon(cam.clip_poly(q), fill=255) for q in self.hole_quads()
                               if cam.clip_poly(q) is not None])
        sc.add(blur(m, sc.s * soft), lerp(np.asarray(col), WHITE, 0.45), gain)


def light_rays(sc, src, center, steps=28, reach=0.9, decay=0.93, color=None, gain=1.0, soft=0.003):
    """Volumetric shafts: `src` (a mask of the light source) smeared outward from `center` by repeated zooms."""
    img = Image.fromarray(np.ascontiguousarray(src, np.float32), "F")
    acc = np.zeros_like(src, np.float32)
    w = 1.0
    tot = 0.0
    for i in range(1, steps + 1):
        z = 1.0 + reach * i / steps
        cx, cy = center
        # output (x, y) samples input ((x - cx) / z + cx, (y - cy) / z + cy)
        a = (1 / z, 0, cx - cx / z, 0, 1 / z, cy - cy / z)
        acc += np.asarray(img.transform((sc.w, sc.h), Image.AFFINE, a, resample=Image.BILINEAR)) * w
        tot += w
        w *= decay
    col = lerp(YELLOW, WHITE, 0.4) if color is None else color
    sc.add(blur(acc / tot, sc.s * soft), col, gain)


def draw_items(sc, cam, items, fade=None):
    """Painter's order: farthest first."""
    for it in sorted(items, key=lambda o: -o.depth(cam)):
        it.draw(sc, cam, fade)


# ---------------------------------------------------------------------------------------------- backdrop

def environment(sc, cam, rng, floor_y=0.0, grid=4.0, sun_az=0.0, sun_radius=9.0, sun_gain=1.0, stars=520,
                grid_gain=1.0, grid_width=0.07, horizon_gain=1.0, grid_color=None, sky_gain=1.0, sun_stripes=True):
    """Synthwave backdrop: starry violet sky, a striped sun sitting on the horizon (az / radius in degrees),
    and an endless neon grid floor at `floor_y` fading into a glowing horizon."""
    X, Y, Z = cam.rays()
    elev = np.arcsin(np.clip(Y, -1, 1))
    sky = elev > 0
    e = np.abs(elev)

    # --- sky / floor body (base layer)
    skyc = (BASE[None, None, :]
            + np.exp(-e / 0.30)[..., None] * np.array([0.010, 0.002, 0.030], np.float32) * sky_gain
            + np.exp(-e / 0.075)[..., None] * np.array([0.060, 0.005, 0.070], np.float32) * sky_gain)
    flo = (np.array([0.0025, 0.0008, 0.0065], np.float32)[None, None, :]
           + np.exp(-e / 0.045)[..., None] * np.array([0.050, 0.004, 0.060], np.float32))
    sc.base += np.where(sky[..., None], skyc, flo)

    # --- stars (sky only, thinning towards the horizon haze)
    def star_fn(d):
        for _ in range(stars):
            x, y = rng.uniform(0, sc.w), rng.uniform(0, sc.h)
            r = sc.s * 0.00045 * (1 + 2.6 * rng.random() ** 4)
            v = int(255 * (0.25 + 0.75 * rng.random() ** 2.2))
            d.ellipse([x - r, y - r, x + r, y + r], fill=v)
    st = sc.draw(star_fn)
    st *= smoothstep(0.02, 0.16, elev)
    sc.add(st, lerp(WHITE, np.array([0.75, 0.80, 1.0], np.float32), 0.5), 0.9)

    def flare_fn(d):
        for _ in range(9):
            x, y = rng.uniform(0, sc.w), rng.uniform(0, sc.h)
            L = sc.s * rng.uniform(0.006, 0.014)
            w = max(1, int(sc.s * 0.0007))
            d.line([(x - L, y), (x + L, y)], fill=200, width=w)
            d.line([(x, y - L), (x, y + L)], fill=200, width=w)
            r = sc.s * 0.0013
            d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    fl = blur(sc.draw(flare_fn), 0.6) * smoothstep(0.10, 0.3, elev)
    sc.add(fl, lerp(CYAN, WHITE, 0.6), 1.1)

    # --- sun: disc on the horizon, yellow top to magenta bottom, retro stripes cut into its lower half
    R = math.radians(sun_radius)
    az = math.radians(sun_az)
    sd = np.array([math.sin(az), 0.0, math.cos(az)], np.float32)
    ang = np.arccos(np.clip(X * sd[0] + Y * sd[1] + Z * sd[2], -1, 1))
    aa = 1.5 / cam.f
    disc = smoothstep(R + aa, R - aa, ang) * sky
    v = np.clip(elev / R, 0, 1)
    sun_col = lerp(PINK[None, None, :] * 1.0, lerp(YELLOW, ORANGE, 0.25)[None, None, :], (v ** 0.7)[..., None])
    sun_col = lerp(sun_col, YELLOW[None, None, :], np.clip((v - 0.65) / 0.35, 0, 1)[..., None] * 0.8)
    if sun_stripes:
        g = np.clip((0.62 - v) / 0.62, 0, 1)
        band = np.mod(v * 10.5 + 0.15, 1.0)
        cut = smoothstep(g * 0.75 - 0.04, g * 0.75 + 0.04, band)    # gap where band < g*0.75
        cut = np.where(g > 0, cut, 1.0)
        disc = disc * cut
    sc.emit += disc[..., None] * sun_col * (1.15 * sun_gain)
    # halo around the sun and its reflection streak on the floor
    halo = np.exp(-np.maximum(ang - R * 0.6, 0) / (R * 0.9)) * sky
    sc.emit += halo[..., None] * lerp(PINK, MAGENTA, 0.5)[None, None, :] * (0.10 * sun_gain)
    azp = np.arctan2(X, Z)
    dz = np.angle(np.exp(1j * (azp - az)))
    refl = np.exp(-(dz / (R * 0.55)) ** 2) * np.exp(-e / 0.05) * (~sky)
    sc.emit += refl[..., None] * lerp(PINK, MAGENTA, 0.6)[None, None, :] * (0.22 * sun_gain)

    # --- horizon: a hot thin line plus a wider haze
    hz = np.exp(-(elev / 0.0045) ** 2) * 0.9 + np.exp(-e / 0.025) * 0.20
    sc.emit += hz[..., None] * lerp(MAGENTA, PINK, 0.35)[None, None, :] * horizon_gain

    # --- neon grid floor
    gc = MAGENTA if grid_color is None else grid_color
    h = cam.pos[1] - floor_y
    zfar = 900.0
    span = 420.0
    x0 = math.floor((cam.pos[0] - span) / grid) * grid
    xl = [((x, floor_y, cam.pos[2] - 4.0), (x, floor_y, cam.pos[2] + zfar)) for x in np.arange(x0, cam.pos[0] + span, grid)]
    zl = [((cam.pos[0] - span, floor_y, z), (cam.pos[0] + span, floor_y, z))
          for z in np.arange(math.ceil(cam.pos[2] / grid) * grid, cam.pos[2] + 160.0, grid)]
    min_sep = sc.s * 0.006
    fog = lambda z: math.exp(-z / 260.0)
    neon_lines(sc, cam, xl, gc, grid_width, 0.85 * grid_gain, chunks=36, caps=False, min_px=1.1,
               fade=lambda z: fog(z) * float(np.clip((cam.f * grid / z - min_sep) / (min_sep * 2), 0, 1)))
    neon_lines(sc, cam, zl, gc, grid_width, 0.85 * grid_gain, chunks=24, caps=False, min_px=1.1,
               fade=lambda z: fog(z) * float(np.clip((cam.f * grid * h / (z * z) - min_sep) / (min_sep * 2), 0, 1)))
    return elev


# ---------------------------------------------------------------------------------------------- ship

SHIP = [  # arrowhead ship, nose up; unit coords (x right, y down)
    (0.00, -1.00),
    (0.15, -0.38),
    (1.00, 0.52),    # wing tip
    (0.94, 0.62),
    (0.40, 0.40),    # trailing edge
    (0.23, 0.63),    # engine pod
    (0.08, 0.63),
    (0.00, 0.46),    # tail notch
]


def ship(sc, cx, cy, sw, sh, gain=1.0, trail=True, trail_end=None, roll=0.0, underglow=0.55, color=None,
         line_scale=1.0):
    """Draws the ship seen from behind (banked by `roll` radians). Returns (hull_mask, inner_mask)."""
    s = sc.s * line_scale
    col = CYAN if color is None else np.asarray(color, np.float32)
    cr, sr = math.cos(roll), math.sin(roll)

    def P(x, y):  # local unit coords -> screen
        px, py = x * sw, y * sh
        return (cx + px * cr - py * sr, cy + px * sr + py * cr)

    full = SHIP + [(-x, y) for (x, y) in reversed(SHIP[1:-1])]
    poly = [P(x, y) for (x, y) in full]
    ey = 0.63
    engines = (-0.155, 0.155)
    trail_end = trail_end if trail_end is not None else sc.h + 20

    if trail:
        def trail_fn(d):
            for ex in engines:
                x0, y0 = P(ex, ey)
                d.polygon([P(ex - 0.06, ey), P(ex + 0.06, ey), (x0 + 0.16 * sw, trail_end), (x0 - 0.16 * sw, trail_end)], fill=255)
        tr = sc.draw(trail_fn)
        y_eng = P(0, ey)[1]
        fade = np.clip(1 - (sc.yy - y_eng) / max(1.0, trail_end - y_eng), 0, 1) ** 2.2
        sc.add(blur(tr, s * 0.010) * fade, col, 0.9 * gain)

        def jet_fn(d):
            for ex in engines:
                x0, y0 = P(ex, ey)
                d.line([(x0, y0), (x0, y0 + (trail_end - y0) * 0.55)], fill=255, width=max(2, int(s * 0.006)))
        sc.add(blur(sc.draw(jet_fn), s * 0.002) * fade, lerp(col, WHITE, 0.6), 1.2 * gain)

    hull = blur(sc.draw(lambda d: d.polygon(poly, fill=255)), 0.8)
    if underglow > 0:
        sc.add(blur(hull, s * 0.030), col, underglow * gain)

    # hull: deep navy with a subtle cool gradient (lit from ahead)
    top_y = P(0, -1)[1]
    ty = np.clip((sc.yy - top_y) / (2 * sh), 0, 1)[..., None]
    top = np.array([0.050, 0.060, 0.140], np.float32)
    bot = np.array([0.018, 0.016, 0.050], np.float32)
    body = top[None, None, :] * (1 - ty) + bot[None, None, :] * ty
    m = hull[..., None]
    sc.emit *= (1 - m)
    sc.base = sc.base * (1 - m) + m * body

    lw = max(2, int(s * 0.0026))
    sc.add(sc.draw(lambda d: d.line([P(0, -0.93), P(0, 0.40)], fill=255, width=max(1, lw // 2))), BLUE, 0.9 * gain)
    sc.add(sc.draw(lambda d: d.polygon([P(0, -0.66), P(0.075, -0.16), P(0, -0.02), P(-0.075, -0.16)], fill=255)),
           lerp(col, WHITE, 0.45), 1.3 * gain)

    def strakes(d):
        for sx in (-1, 1):
            d.line([P(sx * 0.18, -0.20), P(sx * 0.80, 0.44)], fill=150, width=max(1, lw // 2))
    sc.add(sc.draw(strakes), col, 0.8 * gain)

    closed = poly + [poly[0], poly[1]]
    sc.add(sc.draw(lambda d: d.line(closed, fill=255, width=max(2, int(s * 0.0048)), joint="curve")), col, 1.6 * gain)
    sc.add(sc.draw(lambda d: d.line(closed, fill=255, width=max(1, int(s * 0.0016)), joint="curve")), WHITE, 1.2 * gain)

    def tips(d):
        for sx in (-1, 1):
            x, y = P(sx * 0.965, 0.565)
            rr = s * 0.0055
            d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=255)
    sc.add(sc.draw(tips), lerp(MAGENTA, WHITE, 0.25), 2.6 * gain)

    def eng(d):
        for ex in engines:
            x, y = P(ex, ey)
            rx, ry = 0.070 * sw, 0.030 * sh
            d.ellipse([x - rx, y - ry, x + rx, y + ry], fill=255)
    sc.add(sc.draw(eng), lerp(col, WHITE, 0.7), 4.0 * gain)

    inner = minimum_filter(hull, size=int(s * 0.012) | 1)
    return hull, inner


# ---------------------------------------------------------------------------------------------- text

def text_mask(sc, txt, font, xy, tracking=0):
    """Text coverage mask drawn glyph by glyph with extra tracking. Returns (mask, x_end)."""
    img, d = mask_draw(sc.w, sc.h)
    x, y = xy
    for ch in txt:
        d.text((x, y), ch, font=font, fill=255)
        x += d.textlength(ch, font=font) + tracking
    return as_f(img), x - tracking


def text_width(txt, font, tracking=0):
    d = ImageDraw.Draw(Image.new("L", (1, 1)))
    return sum(d.textlength(ch, font=font) for ch in txt) + tracking * (len(txt) - 1)


def neon_title(sc, m, color_a, color_b, ghost_px, gain=1.0, darken=0.75):
    """The in-game NeonTitle look: chromatic ghosts (color_a left, color_b right) and a white-hot core."""
    sc.base *= (1 - darken * np.clip(blur(m, ghost_px * 1.5) * 1.5, 0, 1))[..., None]
    sc.emit *= (1 - darken * np.clip(blur(m, ghost_px * 1.5) * 1.5, 0, 1))[..., None]
    g = int(round(ghost_px))
    sc.emit += np.roll(m, -g, axis=1)[..., None] * np.asarray(color_a)[None, None, :] * (0.9 * gain)
    sc.emit += np.roll(m, g, axis=1)[..., None] * np.asarray(color_b)[None, None, :] * (0.9 * gain)
    sc.emit *= (1 - m)[..., None]
    sc.base *= (1 - m)[..., None]
    sc.emit += m[..., None] * lerp(lerp(np.asarray(color_a), np.asarray(color_b), 0.5), WHITE, 0.82)[None, None, :] * (1.6 * gain)


# ---------------------------------------------------------------------------------------------- output

def render(sc, bloom=1.0, exposure=1.0, inner=None, inner_dark=0.55, vignette=0.4, vcenter=None):
    e = sc.emit
    s = sc.s
    glow = (blur(e, s * 0.0025) * 0.65 + blur(e, s * 0.008) * 0.60 + blur(e, s * 0.022) * 0.34 + blur(e, s * 0.06) * 0.12)
    img = sc.base + e + glow * bloom
    if inner is not None:
        img *= (1 - inner_dark * inner)[..., None]
    if vignette > 0:
        c = vcenter or (sc.w / 2, sc.h / 2)
        d = np.sqrt(((sc.xx - c[0]) / sc.w) ** 2 + ((sc.yy - c[1]) / sc.h) ** 2) * 1.414
        img *= (1 - vignette * np.clip(d, 0, 1) ** 2.0)[..., None]
    return tonemap(img * exposure)


def tonemap(x):
    # exponential roll-off per channel keeps neon hues; very hot values bleed to white
    mapped = 1.0 - np.exp(-x * 1.25)
    lum = x.max(axis=2, keepdims=True)
    hot = np.clip((lum - 1.4) / 2.5, 0, 1) * 0.6
    white = 1.0 - np.exp(-lum * 1.25)
    return np.clip(mapped * (1 - hot) + white * hot, 0, 1)


def to_srgb8(lin):
    a = np.clip(lin, 0, 1)
    s = np.where(a <= 0.0031308, a * 12.92, 1.055 * np.power(a, 1 / 2.4) - 0.055)
    return (np.clip(s, 0, 1) * 255.0 + 0.5).astype(np.uint8)


def dither(rgb8, rng, amount=1.0):
    n = rng.normal(0, amount, rgb8.shape[:2])[..., None]
    return np.clip(rgb8.astype(np.float32) + n, 0, 255).astype(np.uint8)
