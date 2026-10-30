#!/usr/bin/env python3
"""Re-balance music stem gains toward LUFS targets using the preview table (run the preview first).

usage: python3 calibrate.py preview_output.txt   (edits StemGain() in the song sources in place)
"""
import re, sys, os
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '../../BeamRunner/Assets/BeamRunner/Scripts/Core/Audio/Music')
TARGETS = {
    'Run': {'Drums': -19.0, 'Bass': -20.5, 'Pads': -22.5, 'Arp': -22.5, 'Lead': -20.5},
    'Menu': {'Pads': -19.2, 'Arp': -21.2, 'Bass': -21.7, 'Drums': -22.7},
    'GameOver': {'Pads': -16.0},
}
FILES = {'Run': 'RunSong.cs', 'Menu': 'MenuSong.cs', 'GameOver': 'GameOverSong.cs'}
measured = {}
for line in open(sys.argv[1]):
    m = re.match(r'\s+Music_(\w+?)_(\w+)\s+[\d.]+\s+\d+\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)', line)
    if m:
        measured[(m.group(1), m.group(2))] = float(m.group(5))
for mode, targets in TARGETS.items():
    path = os.path.join(ROOT, FILES[mode])
    s = open(path).read()
    a = s.index('public override float StemGain(MusicStem stem)')
    b = s.index('public override void Render(')
    seg = s[a:b]
    for stem, tgt in targets.items():
        lufs = measured[(mode, stem)]
        k = 10 ** ((tgt - lufs) / 20)
        if mode == 'GameOver':
            m = re.search(r'return ([0-9.]+)f;', seg)
            old = float(m.group(1)); new = old * k
            seg = seg.replace(m.group(0), 'return %.3ff;' % new, 1)
        else:
            pat = r'(case MusicStem\.%s:\s*return )([0-9.]+)f;' % stem
            m = re.search(pat, seg)
            old = float(m.group(2)); new = old * k
            seg = re.sub(pat, lambda mm: mm.group(1) + '%.3ff;' % new, seg)
        print(f'{mode:9s} {stem:6s} {lufs:6.1f} -> {tgt:6.1f} LUFS: gain {old:.3f} -> {new:.3f}')
    open(path, 'w').write(s[:a] + seg + s[b:])
