#!/usr/bin/env python3
"""Generate missing Unity `.meta` files with deterministic GUIDs.

Unity needs a `.meta` next to every asset and folder under `Assets/`. This project is edited
without a running Editor, so new files are given metas by this script:

  * The GUID is deterministic: md5 of the project-relative path (e.g.
    "Assets/BeamRunner/Scripts/Core/Rng.cs"), so re-running is stable and two people
    generating metas for the same new file get the same GUID.
  * Existing metas are NEVER modified or overwritten (hand-written metas with fixed GUIDs,
    e.g. GameBootstrap.cs.meta, stay intact).
  * Importer templates are modelled on metas written by Unity itself; the Editor upgrades
    them silently on first import. Unknown extensions get a DefaultImporter (Unity keeps the
    GUID and switches the importer type if it knows better).

Usage (from anywhere; paths may be relative to the cwd or to the repository root):
  tools/gen-metas.py                          # default root: BeamRunner/Assets/BeamRunner
  tools/gen-metas.py PATH [PATH...]           # one or more roots (folders or files) under Assets/
  tools/gen-metas.py --check [PATH...]        # report missing/orphaned/duplicate, exit 1 if any
  tools/gen-metas.py --dry-run [PATH...]      # show what would be written
  tools/gen-metas.py --prune [PATH...]        # also delete orphaned metas
  tools/gen-metas.py --no-parents PATH        # don't create metas for ancestor folders of PATH

Files Unity ignores are skipped: names starting with '.', ending with '~', named 'cvs',
or with the '.tmp' extension (and everything inside such folders).
"""
import argparse
import hashlib
import os
import re
import struct
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_ROOT = os.path.join("BeamRunner", "Assets", "BeamRunner")

TAIL = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


# ----------------------------------------------------------------------------------------------
# Templates
# ----------------------------------------------------------------------------------------------

def simple_importer(name, extra=""):
    return name + ":\n  externalObjects: {}\n" + extra + TAIL


def folder_meta():
    return "folderAsset: yes\n" + simple_importer("DefaultImporter")


def mono_meta():
    return ("MonoImporter:\n"
            "  externalObjects: {}\n"
            "  serializedVersion: 2\n"
            "  defaultReferences: []\n"
            "  executionOrder: 0\n"
            "  icon: {instanceID: 0}\n" + TAIL)


def shader_meta():
    return simple_importer("ShaderImporter", "  defaultTextures: []\n  nonModifiableTextures: []\n")


def native_meta(main_object_file_id):
    return simple_importer("NativeFormatImporter", "  mainObjectFileID: %d\n" % main_object_file_id)


def font_meta(path):
    names = font_family_names(path)
    lines = ["TrueTypeFontImporter:",
             "  externalObjects: {}",
             "  serializedVersion: 4",
             "  fontSize: 16",
             "  forceTextureCase: -2",          # -2 = Dynamic
             "  characterSpacing: 0",
             "  characterPadding: 1",
             "  includeFontData: 1"]
    if names:
        lines.append("  fontName: " + names[0])
        lines.append("  fontNames:")
        lines.extend("  - " + n for n in names)
    else:
        lines.append("  fontNames: []")
    lines += ["  fallbackFontReferences: []",
              "  customCharacters: ",
              "  fontRenderingMode: 0",
              "  ascentCalculationMode: 1",
              "  useLegacyBoundsCalculation: 0",
              "  shouldRoundAdvanceValue: 1"]
    return "\n".join(lines) + "\n" + TAIL


def texture_meta(max_size=2048, mipmaps=1, srgb=1, compression=1, alpha_usage=1,
                 alpha_is_transparency=0, wrap=0, filter_mode=1, aniso=1, npot_scale=1,
                 texture_type=0, readable=0):
    """TextureImporter (serializedVersion 11, as written by Unity 2021/2022; Unity 6 upgrades it).

    compression: 0 = uncompressed, 1 = normal (compressed), 2 = high quality, 3 = low quality.
    alpha_usage: 0 = none, 1 = from input, 2 = from grayscale.
    """
    return """TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 11
  mipmaps:
    mipMapMode: 0
    enableMipMap: {mip}
    sRGBTexture: {srgb}
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: {readable}
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMasterTextureLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: {size}
  textureSettings:
    serializedVersion: 2
    filterMode: {filt}
    aniso: {aniso}
    mipBias: 0
    wrapU: {wrap}
    wrapV: {wrap}
    wrapW: {wrap}
  nPOTScale: {npot}
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: {alpha}
  alphaIsTransparency: {ait}
  spriteTessellationDetail: -1
  textureType: {ttype}
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: {comp}
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  spritePackingTag:
  pSDRemoveMatte: 0
""".format(mip=mipmaps, srgb=srgb, readable=readable, size=max_size, filt=filter_mode, aniso=aniso,
           wrap=wrap, npot=npot_scale, alpha=alpha_usage, ait=alpha_is_transparency, ttype=texture_type,
           comp=compression) + TAIL


TEXT_EXTS = {".json", ".txt", ".md", ".csv", ".xml", ".yaml", ".yml", ".bytes", ".html", ".htm", ".fnt"}
TEXTURE_EXTS = {".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".exr", ".hdr", ".bmp", ".gif"}
NATIVE_MAIN_IDS = {
    ".mat": 2100000, ".asset": 11400000, ".anim": 7400000, ".controller": 9100000,
    ".overrideController": 22100000, ".mask": 31900000, ".physicMaterial": 13400000,
    ".physicsMaterial2D": 6200000, ".renderTexture": 8400000, ".mixer": 24100000,
    ".flare": 12100000, ".cubemap": 8900000, ".guiskin": 11400000, ".fontsettings": 1300000,
}


def importer_for(path):
    """Returns the importer section (everything after the guid line) for a file."""
    if os.path.isdir(path):
        return folder_meta()
    ext = os.path.splitext(path)[1]
    lext = ext.lower()
    if lext == ".cs":
        return mono_meta()
    if lext == ".shader":
        return shader_meta()
    if lext in (".cginc", ".hlsl", ".glslinc"):
        return simple_importer("ShaderIncludeImporter")
    if lext == ".compute":
        return simple_importer("ComputeShaderImporter", "  preprocessorOverride: 0\n")
    if lext in (".ttf", ".otf"):
        return font_meta(path)
    if lext in TEXT_EXTS:
        return simple_importer("TextScriptImporter")
    if lext == ".asmdef":
        return simple_importer("AssemblyDefinitionImporter")
    if lext == ".asmref":
        return simple_importer("AssemblyDefinitionReferenceImporter")
    if lext in TEXTURE_EXTS:
        return texture_meta()
    if lext == ".prefab":
        return simple_importer("PrefabImporter")
    if ext in NATIVE_MAIN_IDS:
        return native_meta(NATIVE_MAIN_IDS[ext])
    return simple_importer("DefaultImporter")   # .unity and everything else


# ----------------------------------------------------------------------------------------------
# Font family names (TrueType/OpenType 'name' table, no dependencies)
# ----------------------------------------------------------------------------------------------

def font_family_names(path):
    try:
        with open(path, "rb") as f:
            data = f.read()
        num_tables = struct.unpack(">H", data[4:6])[0]
        name_off = None
        for i in range(num_tables):
            rec = data[12 + 16 * i: 28 + 16 * i]
            tag, _, off, _ = struct.unpack(">4sIII", rec)
            if tag == b"name":
                name_off = off
                break
        if name_off is None:
            return []
        _, count, str_off = struct.unpack(">HHH", data[name_off:name_off + 6])
        found = {}
        for i in range(count):
            p = name_off + 6 + 12 * i
            pid, eid, lid, nid, length, off = struct.unpack(">HHHHHH", data[p:p + 12])
            if nid != 1:  # 1 = font family
                continue
            raw = data[name_off + str_off + off: name_off + str_off + off + length]
            if pid == 3 or pid == 0:
                s = raw.decode("utf-16-be", "ignore")
                rank = 0 if (pid == 3 and lid == 0x409) else 1
            elif pid == 1:
                s = raw.decode("mac_roman", "ignore")
                rank = 2
            else:
                continue
            s = s.strip()
            if s and (rank not in found):
                found[rank] = s
        if not found:
            return []
        return [found[min(found)]]
    except Exception:  # malformed font: leave names empty, Unity fills them on import
        return []


# ----------------------------------------------------------------------------------------------
# Walking
# ----------------------------------------------------------------------------------------------

def is_ignored(name):
    return (name.startswith(".") or name.endswith("~") or name.lower() == "cvs"
            or name.lower().endswith(".tmp"))


def find_project(path):
    """Returns (project_dir, assets_dir) for a path inside <project>/Assets."""
    p = os.path.abspath(path)
    while True:
        parent = os.path.dirname(p)
        if os.path.basename(p) == "Assets" and os.path.isdir(os.path.join(parent, "ProjectSettings")):
            return parent, p
        if parent == p:
            raise SystemExit("error: %s is not inside a Unity project's Assets folder" % path)
        p = parent


def project_relative(project, path):
    return os.path.relpath(os.path.abspath(path), project).replace(os.sep, "/")


def guid_for(project, path):
    return hashlib.md5(project_relative(project, path).encode("utf-8")).hexdigest()


def meta_text(project, path):
    return "fileFormatVersion: 2\nguid: %s\n%s" % (guid_for(project, path), importer_for(path))


def iter_assets(root):
    """Yields every non-ignored file and folder under root (root itself included)."""
    if os.path.isfile(root):
        if not root.endswith(".meta"):
            yield root
        return
    yield root
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = sorted(d for d in dirnames if not is_ignored(d))
        for d in dirnames:
            yield os.path.join(dirpath, d)
        for f in sorted(filenames):
            if f.endswith(".meta") or is_ignored(f):
                continue
            yield os.path.join(dirpath, f)


def iter_metas(root):
    if os.path.isfile(root):
        return
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if not is_ignored(d)]
        for f in filenames:
            if f.endswith(".meta") and not f.startswith("."):
                yield os.path.join(dirpath, f)


def ancestors(assets_dir, path):
    """Folders strictly between Assets/ and path (outermost first)."""
    out = []
    p = os.path.dirname(os.path.abspath(path))
    while len(p) > len(assets_dir) and p.startswith(assets_dir):
        out.append(p)
        p = os.path.dirname(p)
    return list(reversed(out))


def read_guid(meta_path):
    try:
        with open(meta_path, "r", encoding="utf-8", errors="replace") as f:
            m = re.search(r"^guid:\s*([0-9a-fA-F]{32})\s*$", f.read(), re.M)
        return m.group(1).lower() if m else None
    except OSError:
        return None


def resolve(path):
    if os.path.exists(path):
        return os.path.abspath(path)
    alt = os.path.join(REPO_ROOT, path)
    if os.path.exists(alt):
        return os.path.abspath(alt)
    raise SystemExit("error: path not found: %s" % path)


def main():
    ap = argparse.ArgumentParser(description="Generate missing Unity .meta files with deterministic GUIDs.")
    ap.add_argument("roots", nargs="*", help="folders/files under <project>/Assets (default: %s)" % DEFAULT_ROOT)
    ap.add_argument("--check", action="store_true", help="only report missing/orphaned/duplicate metas; exit 1 if any")
    ap.add_argument("--dry-run", action="store_true", help="print what would be written without writing")
    ap.add_argument("--prune", action="store_true", help="delete orphaned metas (not in --check mode)")
    ap.add_argument("--no-parents", action="store_true", help="don't create metas for ancestor folders of the roots")
    ap.add_argument("-q", "--quiet", action="store_true")
    args = ap.parse_args()

    roots = [resolve(r) for r in (args.roots or [DEFAULT_ROOT])]
    missing, orphans, created = [], [], []
    project = assets_dir = None

    for root in roots:
        project, assets_dir = find_project(root)
        targets = []
        if not args.no_parents:
            targets += ancestors(assets_dir, root)
        if os.path.abspath(root) != assets_dir:
            targets += list(iter_assets(root))
        else:
            targets += [p for p in iter_assets(root) if os.path.abspath(p) != assets_dir]

        for path in targets:
            meta = path + ".meta"
            if os.path.exists(meta):
                continue
            missing.append(path)
            if args.check:
                continue
            text = meta_text(project, path)
            if args.dry_run:
                print("would write %s (guid %s)" % (project_relative(project, meta), guid_for(project, path)))
                continue
            with open(meta, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
            created.append(meta)
            if not args.quiet:
                print("wrote %s" % project_relative(project, meta))

        for meta in iter_metas(root):
            target = meta[:-5]
            if not os.path.exists(target):
                orphans.append(meta)

    # Duplicate GUIDs anywhere under Assets/ (checked in every mode; cheap and catastrophic if present).
    dupes = {}
    if assets_dir:
        seen = {}
        for meta in iter_metas(assets_dir):
            g = read_guid(meta)
            if g:
                seen.setdefault(g, []).append(meta)
        dupes = {g: ms for g, ms in seen.items() if len(ms) > 1}

    rel = (lambda p: project_relative(project, p)) if project else (lambda p: p)
    if args.check:
        for p in missing:
            print("MISSING  %s.meta" % rel(p))
    for m in orphans:
        if args.prune and not args.check and not args.dry_run:
            os.remove(m)
            print("PRUNED   %s" % rel(m))
        else:
            print("ORPHAN   %s" % rel(m))
    for g, ms in sorted(dupes.items()):
        print("DUPLICATE guid %s: %s" % (g, ", ".join(rel(m) for m in ms)))

    if args.check:
        ok = not missing and not orphans and not dupes
        if not args.quiet:
            print("gen-metas: %s (%d missing, %d orphaned, %d duplicate guids)"
                  % ("OK" if ok else "FAIL", len(missing), len(orphans), len(dupes)))
        return 0 if ok else 1

    if not args.quiet:
        verb = "would create" if args.dry_run else "created"
        print("gen-metas: %s %d meta(s); %d orphaned; %d duplicate guids"
              % (verb, len(missing) if args.dry_run else len(created), len(orphans), len(dupes)))
    return 1 if dupes else 0


if __name__ == "__main__":
    sys.exit(main())
