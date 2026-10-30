"""Android small notification icon: the BeamRunner ship as a flat white silhouette on transparency.

Android draws status-bar/notification small icons from the alpha channel only (white on transparent, no colour,
24 dp with ~2 dp padding). The Mobile Notifications package scales this 256x256 source down to 18-48 px per density
at build time (Project Settings > Mobile Notifications > Android > Notification Icons, id "field_notification_small"),
so the shape is kept bold: the arrowhead hull with a cockpit cut-out and the tail notch, no thin lines.
Usage: python3 make_notification_icon.py OUT.png
"""
import sys
from PIL import Image, ImageDraw
from fieldart import SHIP

OUT = sys.argv[1] if len(sys.argv) > 1 else "NotificationSmall.png"
FINAL = 256
S = FINAL * 8           # supersampled, then downsampled (antialiased edges)
PAD = S * 2 // 24       # 2 dp of the 24 dp canvas
BOX = S - 2 * PAD       # live area

# Ship outline (unit coords, nose up) mirrored like fieldart.ship().
full = SHIP + [(-x, y) for (x, y) in reversed(SHIP[1:-1])]
xs = [p[0] for p in full]
ys = [p[1] for p in full]
w, h = max(xs) - min(xs), max(ys) - min(ys)
scale = BOX / max(w, h)
cx = S / 2 - (min(xs) + max(xs)) / 2 * scale
cy = S / 2 - (min(ys) + max(ys)) / 2 * scale


def P(x, y):
    return (cx + x * scale, cy + y * scale)


mask = Image.new("L", (S, S), 0)
d = ImageDraw.Draw(mask)
d.polygon([P(x, y) for (x, y) in full], fill=255)
# Cockpit cut-out (the canopy diamond of fieldart.ship), below the thin nose so both survive at 24 px.
d.polygon([P(0, -0.34), P(0.13, -0.02), P(0, 0.14), P(-0.13, -0.02)], fill=0)

alpha = mask.resize((FINAL, FINAL), Image.LANCZOS)
white = Image.new("L", (FINAL, FINAL), 255)
Image.merge("RGBA", (white, white, white, alpha)).save(OUT, optimize=True)
print("wrote", OUT)
