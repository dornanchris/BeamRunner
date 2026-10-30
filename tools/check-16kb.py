#!/usr/bin/env python3
"""Check an Android build for 16 KB memory-page-size compatibility (Google Play requirement for apps
targeting Android 15+ since 1 Nov 2025).

  tools/check-16kb.py BeamRunner/Builds/Android/BeamRunner.aab
  tools/check-16kb.py app.apk [more.apk|lib.so|extracted-dir ...]

For every native library of a 64-bit ABI (arm64-v8a, x86_64) it checks that each PT_LOAD segment is aligned to
at least 16 KB (ELF p_align >= 0x4000). In an .apk it also checks that uncompressed .so entries start at a
16 KB-aligned offset (for .aab files Google Play does the packaging). 32-bit libraries are listed but never
fail: 16 KB page sizes only exist on 64-bit devices. Unity 6.3 builds its own libraries 16 KB-aligned; this
catches third-party native plug-ins (e.g. ad-network SDKs) that aren't.

Exit code: 0 = compatible, 1 = at least one 64-bit library is not, 2 = usage error / nothing to check.
"""
import os
import struct
import sys
import zipfile

PAGE = 16 * 1024
ABIS_64 = ("arm64-v8a", "x86_64")
PT_LOAD = 1


def load_alignments(data):
    """Returns the p_align of every PT_LOAD segment, or raises ValueError for non-ELF input."""
    if len(data) < 52 or data[:4] != b"\x7fELF":
        raise ValueError("not an ELF file")
    elf_class, elf_data = data[4], data[5]
    if elf_data not in (1, 2) or elf_class not in (1, 2):
        raise ValueError("unsupported ELF header")
    end = "<" if elf_data == 1 else ">"
    if elf_class == 2:
        phoff, = struct.unpack_from(end + "Q", data, 0x20)
        phentsize, phnum = struct.unpack_from(end + "HH", data, 0x36)
        entry, type_off, align_off = end + "I", 0, 0x30
        align_fmt = end + "Q"
    else:
        phoff, = struct.unpack_from(end + "I", data, 0x1C)
        phentsize, phnum = struct.unpack_from(end + "HH", data, 0x2A)
        entry, type_off, align_off = end + "I", 0, 0x1C
        align_fmt = end + "I"
    aligns = []
    for i in range(phnum):
        base = phoff + i * phentsize
        if base + phentsize > len(data):
            raise ValueError("truncated program header table")
        p_type, = struct.unpack_from(entry, data, base + type_off)
        if p_type == PT_LOAD:
            aligns.append(struct.unpack_from(align_fmt, data, base + align_off)[0])
    return aligns


def abi_of(path):
    parts = path.replace("\\", "/").split("/")
    for i, part in enumerate(parts[:-1]):
        if part == "lib" and i + 1 < len(parts) - 1:
            return parts[i + 1]
    return None


def data_offset(archive, info):
    """Offset of the entry's data in the zip file (local header + name + extra)."""
    archive.fp.seek(info.header_offset)
    header = archive.fp.read(30)
    name_len, extra_len = struct.unpack_from("<HH", header, 26)
    return info.header_offset + 30 + name_len + extra_len


class Result:
    def __init__(self):
        self.rows = []
        self.failures = 0

    def add(self, name, abi, ok, detail, counts):
        self.rows.append((("OK  " if ok else "FAIL") if counts else "info", abi or "?", name, detail))
        if counts and not ok:
            self.failures += 1


def check_library(result, name, abi, data, zip_note=None):
    counts = abi in ABIS_64
    try:
        aligns = load_alignments(data)
    except ValueError as e:
        result.add(name, abi, False, str(e), counts)
        return
    smallest = min(aligns) if aligns else 0
    ok = smallest >= PAGE
    detail = "LOAD align %s" % (("%d KB" % (smallest // 1024)) if smallest >= 1024 else "%d B" % smallest)
    if zip_note:
        ok = ok and zip_note[0]
        detail += "; " + zip_note[1]
    if not counts:
        detail += " (32-bit: not required)"
    result.add(name, abi, ok, detail, counts)


def check_archive(result, path):
    is_apk = path.lower().endswith(".apk")
    with zipfile.ZipFile(path) as archive:
        found = False
        for info in archive.infolist():
            if not info.filename.endswith(".so"):
                continue
            abi = abi_of(info.filename)
            if abi is None:
                continue
            found = True
            zip_note = None
            if is_apk:
                if info.compress_type == zipfile.ZIP_STORED:
                    offset = data_offset(archive, info)
                    zip_note = (offset % PAGE == 0, "stored at offset %% 16 KB = %d" % (offset % PAGE))
                else:
                    zip_note = (True, "compressed in APK (extracted at install)")
            check_library(result, info.filename, abi, archive.read(info), zip_note)
        return found


def main(argv):
    if len(argv) < 2 or argv[1] in ("-h", "--help"):
        print(__doc__.strip())
        return 2
    result = Result()
    checked = False
    for target in argv[1:]:
        if os.path.isdir(target):
            for root, _, files in os.walk(target):
                for name in files:
                    if name.endswith(".so"):
                        full = os.path.join(root, name)
                        with open(full, "rb") as f:
                            check_library(result, os.path.relpath(full, target), abi_of(full), f.read())
                        checked = True
        elif target.endswith(".so"):
            with open(target, "rb") as f:
                check_library(result, target, abi_of(os.path.abspath(target)), f.read())
            checked = True
        elif zipfile.is_zipfile(target):
            checked = check_archive(result, target) or checked
        else:
            print("check-16kb: not an .apk/.aab/.so/directory: " + target, file=sys.stderr)
            return 2
    if not checked:
        print("check-16kb: no native libraries found under lib/<abi>/", file=sys.stderr)
        return 2
    for status, abi, name, detail in sorted(result.rows, key=lambda r: (r[1], r[2])):
        print("%s  %-12s %s  (%s)" % (status, abi, name, detail))
    if result.failures:
        print("check-16kb: %d 64-bit librar%s NOT 16 KB compatible - update or remove the plug-in that ships "
              "them (Google Play rejects the upload)." % (result.failures, "y is" if result.failures == 1 else "ies are"))
        return 1
    print("check-16kb: all 64-bit native libraries are 16 KB page-size compatible.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
