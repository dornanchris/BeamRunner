#!/usr/bin/env bash
# Regenerates the BeamRunner key art (app icon, Android adaptive layers, notification icon, promo graphics) and the
# loading screen's splash art.
# Scene: the ship riding three layers of light rails, a bridge snapping into the gap ahead inside its ghost outline,
# a ramp, a gem trail and a cube, under a starry synthwave sky with a striped sun (game palette #FF2BD6 / #00E5FF /
# #FFE45C on #07030F). fieldart.py is a tiny software renderer (pinhole camera, painter's-order solids with neon
# edges, bloom, tone-map); every image is deterministic (seeded RNGs). The feature graphic sets the logo in the
# bundled Audiowide / Rajdhani fonts. Takes about two minutes.
# Requires: python3 with pillow, numpy, scipy  (pip install pillow numpy scipy).
# This folder ends with '~' so Unity ignores it; outputs overwrite the PNGs in ../Icon, ../Promo and
# ../../Resources/BeamRunner/Splash (their .meta files are untouched, so GUIDs and import settings are kept).
set -euo pipefail
cd "$(dirname "$0")"
ART=..
FONTS=../../Resources/BeamRunner/Fonts
python3 make_icon.py "$ART/Icon/AppIcon.png"
python3 make_adaptive.py "$ART/Icon/AppIconAdaptiveBackground.png" "$ART/Icon/AppIconAdaptiveForeground.png"
python3 make_notification_icon.py "$ART/Notification/NotificationSmall.png"
python3 make_feature.py "$ART/Promo/FeatureGraphic.png" "$FONTS/Audiowide-Regular.ttf" "$FONTS/Rajdhani-SemiBold.ttf"
python3 make_splash.py "../../Resources/BeamRunner/Splash/SplashArt.png"
python3 - <<'PY'
from PIL import Image
im = Image.open("../Promo/FeatureGraphic.png").convert("RGB").resize((1024, 512), Image.LANCZOS).crop((0, 6, 1024, 506))
im.save("../Promo/FeatureGraphicPlay1024x500.png", optimize=True)
PY
echo "done"
