#!/usr/bin/env python3
"""Unity 6 (6000.3.25f1) API audit - best-effort, text based.

Why: check.sh compiles against real UnityEngine 6000.0.58 player assemblies (so engine [Obsolete] APIs of 6000.0
already show up as CS0618/CS0619), but the project is on 6000.3.25f1, UnityEditor is only 2021.1 and the engine refs
are player builds. So
  (a) APIs that exist in the older refs but are [Obsolete] (warning or error) in 6000.3 can compile silently, and
  (b) APIs that only exist in 6000.1-6000.3 (or only in the editor) show up as compile errors although they are fine.
This script answers both questions from an index of UnityCsReference @ 6000.3.25f1 (built by setup.sh):

  audit-unity6-api.py                       scan Assets/BeamRunner/**/*.cs for uses of members/types that are
                                            [Obsolete] in 6000.3 (`Type.Member`, `.Member(`, bare inherited calls)
  audit-unity6-api.py --lookup Object.GetEntityId
                                            does it exist in 6000.3? obsolete? present in the refs check.sh uses?
  audit-unity6-api.py --lookup Awaitable --lookup Light.cookieSize

check.sh additionally runs a *semantic* version of (a) (exact symbol resolution, no false positives for
resolvable code) and uses the same index to classify (b) errors as "unity6-only".

Options: --project PATH (Unity project root), --paths DIR... (scan roots, default Assets/BeamRunner),
         --all (also list low-confidence matches), --index TSV, --tsv (machine-readable output).
"""
from __future__ import annotations

import argparse
import difflib
import os
import re
import sys
from collections import defaultdict
from pathlib import Path

CACHE = Path(os.environ.get("UCC_CACHE", "~/.cache/unity-compile-check")).expanduser()
UNITY_VERSION = "6000.3.25f1"
DEFAULT_INDEX = CACHE / "index" / f"unity-{UNITY_VERSION}-api.tsv"
REFS_INDEX = CACHE / "index" / "reference-assemblies-api.unity6.tsv"
# default project: <repo>/BeamRunner (this script lives in <repo>/tools/unity-compile-check/)
DEFAULT_PROJECT = Path(os.environ.get("UCC_PROJECT") or Path(__file__).resolve().parent.parent.parent / "BeamRunner")

# Types whose members are callable unqualified from a MonoBehaviour / ScriptableObject / Editor subclass.
INHERITED_SCOPES = ["UnityEngine.Object", "UnityEngine.Component", "UnityEngine.Behaviour", "UnityEngine.MonoBehaviour",
                    "UnityEngine.ScriptableObject", "UnityEditor.Editor", "UnityEditor.EditorWindow"]


class Row:
    __slots__ = ("kind", "ns", "type", "member", "arity", "access", "obsolete", "error", "message", "file", "line")

    def __init__(self, cols):
        (self.kind, self.ns, self.type, self.member, arity, self.access, obs, err, self.message, self.file, line) = cols[:11]
        self.arity = int(arity)
        self.obsolete = obs == "1"
        self.error = err == "1"
        self.line = int(line)

    @property
    def full_type(self):
        return f"{self.ns}.{self.type}" if self.ns else self.type

    @property
    def api(self):
        return self.full_type + (f".{self.member}" if self.member else "")


def load_index(path: Path):
    if not path.exists():
        sys.exit(f"index not found: {path}\nrun: bash tools/unity-compile-check/setup.sh")
    rows = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("#"):
                continue
            cols = line.rstrip("\n").split("\t")
            if len(cols) >= 11:
                rows.append(Row(cols))
    return rows


class Refs(dict):
    """public API of the reference assemblies check.sh compiles against: {full type: set(members)} (+ .assembly)"""

    def __init__(self):
        super().__init__()
        self.assembly = {}


def load_refs(path: Path):
    types = Refs()
    if not path.exists():
        return None
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("#"):
                continue
            c = line.rstrip("\n").split("\t")
            if len(c) < 5:
                continue
            asm, kind, ns, typ, member = c[:5]
            full = f"{ns}.{typ}" if ns else typ
            types.setdefault(full, set())
            if kind == "T":
                types.assembly.setdefault(full, asm)
            elif kind != "FWD":
                types[full].add(member)
    return types


# ---------------------------------------------------------------------------------------------
# source scanning
# ---------------------------------------------------------------------------------------------

def strip_comments_and_strings(src: str):
    """Blank out comments / string & char literals (keeping newlines so line numbers stay right)."""
    out = []
    i, n = 0, len(src)
    while i < n:
        ch = src[i]
        nxt = src[i + 1] if i + 1 < n else ""
        if ch == "/" and nxt == "/":
            j = src.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i)); i = j
        elif ch == "/" and nxt == "*":
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(re.sub(r"[^\n]", " ", src[i:j])); i = j
        elif ch == '"' or (ch in "@$" and nxt == '"') or (ch in "@$" and nxt in "@$" and i + 2 < n and src[i + 2] == '"'):
            start = i
            verbatim = False
            while src[i] in "@$":
                verbatim |= src[i] == "@"
                i += 1
            i += 1  # opening quote
            while i < n:
                if verbatim and src[i] == '"' and i + 1 < n and src[i + 1] == '"':
                    i += 2; continue
                if not verbatim and src[i] == "\\":
                    i += 2; continue
                if src[i] == '"':
                    i += 1; break
                i += 1
            out.append(re.sub(r"[^\n]", " ", src[start:i]))
        elif ch == "'":
            j = i + 1
            while j < n and src[j] != "'" and src[j] != "\n":
                j += 2 if src[j] == "\\" else 1
            j = min(j + 1, n)
            out.append(" " * (j - i)); i = j
        else:
            out.append(ch); i += 1
    return "".join(out)


RX_QUALIFIED = re.compile(r"\b([A-Z][A-Za-z0-9_]*)\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)")
RX_DOT_MEMBER = re.compile(r"(?<!\.)\.\s*([A-Za-z_][A-Za-z0-9_]*)\s*(\(|<[^;{}()]*?>\s*\()?")
RX_BARE_CALL = re.compile(r"(?<![.\w])([A-Z][A-Za-z0-9_]*)\s*(<[^;{}()]*?>)?\s*\(")
RX_IDENT = re.compile(r"\b[A-Z][A-Za-z0-9_]*\b")


def scan(paths, rows, show_all):
    obsolete = [r for r in rows if r.obsolete and (r.ns.startswith("UnityEngine") or r.ns.startswith("UnityEditor") or r.ns.startswith("Unity."))]
    by_member = defaultdict(list)      # member -> obsolete rows
    by_type_member = defaultdict(list)  # (simple type, member) -> obsolete rows
    obsolete_types = defaultdict(list)  # simple type name -> obsolete type rows
    for r in obsolete:
        if r.kind == "T":
            obsolete_types[r.type.split(".")[-1]].append(r)
        elif r.member and r.member != ".ctor":
            by_member[r.member].append(r)
            by_type_member[(r.type.split(".")[-1], r.member)].append(r)
    # how many *non*-obsolete Unity types also declare a member with that name (noise estimate)
    live = defaultdict(set)
    for r in rows:
        if not r.obsolete and r.member and r.kind != "T" and (r.ns.startswith("UnityEngine") or r.ns.startswith("UnityEditor")):
            live[r.member].add(r.full_type)
    unity_types = {r.type.split(".")[-1] for r in rows if r.kind == "T" and r.access != "partial"}
    inherited = {(r.member) for r in obsolete if r.full_type in INHERITED_SCOPES and r.kind == "M"}

    findings = []   # (confidence, file, line, text, rows)
    files = []
    for root in paths:
        root = Path(root)
        if root.is_file():
            files.append(root)
        elif root.exists():
            files += sorted(p for p in root.rglob("*.cs") if not any(part.startswith(".") or part.endswith("~") for part in p.parts))
    for f in files:
        try:
            raw = f.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        src = strip_comments_and_strings(raw)
        usings = set(re.findall(r"^\s*using\s+(?:static\s+)?([A-Za-z0-9_.]+)\s*;", src, re.M))
        if not any(u.startswith("Unity") for u in usings) and "UnityEngine." not in src and "UnityEditor." not in src:
            continue   # pure C# file (e.g. BeamRunner.Core) - cannot touch Unity APIs
        # variable/field/parameter name -> declared simple type name (best effort)
        var_types = {}
        for m in re.finditer(r"\b([A-Z][A-Za-z0-9_]*)(?:<[^<>;{}()]*>)?(?:\[\])?\s+(@?[A-Za-z_][A-Za-z0-9_]*)\s*(?=[;=,)])", src):
            var_types.setdefault(m.group(2), m.group(1))
        for m in re.finditer(r"\bvar\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:[A-Za-z_][A-Za-z0-9_.]*\.)?(?:GetComponent|AddComponent|GetComponentInChildren|GetComponentInParent|FindFirstObjectByType|FindAnyObjectByType|FindObjectOfType)\s*<\s*([A-Za-z_][A-Za-z0-9_.]*)\s*>", src):
            var_types[m.group(1)] = m.group(2).split(".")[-1]
        for m in re.finditer(r"\bvar\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+([A-Z][A-Za-z0-9_]*)", src):
            var_types[m.group(1)] = m.group(2)
        for m in re.finditer(r"\bvar\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*[^;]*?\b(?:CreateInstance|Load|LoadAsset|Instantiate|GetComponent\w*|AddComponent)\s*<\s*([A-Za-z_][A-Za-z0-9_.]*)\s*>", src):
            var_types.setdefault(m.group(1), m.group(2).split(".")[-1])

        def importable(row):
            ns = row.ns
            return ns == "" or ns in usings or (ns + ".") in src

        def receiver_type(pos):
            before = src[max(0, pos - 120):pos].rstrip()
            gm = re.search(r"(?:GetComponent|AddComponent|GetComponentInChildren|GetComponentInParent)\s*<\s*([A-Za-z_][A-Za-z0-9_.]*)\s*>\s*\(\s*\)\s*\??$", before)
            if gm:
                return gm.group(1).split(".")[-1]
            vm = re.search(r"(@?[A-Za-z_][A-Za-z0-9_]*)\s*\??$", before)
            if vm:
                return var_types.get(vm.group(1))
            return None
        line_starts = [0] + [m.end() for m in re.finditer("\n", src)]

        def line_of(pos):
            lo, hi = 0, len(line_starts) - 1
            while lo < hi:
                mid = (lo + hi + 1) // 2
                if line_starts[mid] <= pos:
                    lo = mid
                else:
                    hi = mid - 1
            return lo + 1

        seen = set()

        def add(conf, pos, text, hits):
            ln = line_of(pos)
            key = (ln, text)
            if key in seen:
                return
            seen.add(key)
            findings.append((conf, f, ln, text, hits))

        for m in RX_QUALIFIED.finditer(src):
            t, mem = m.group(1), m.group(2)
            hits = [h for h in by_type_member.get((t, mem), []) if importable(h)]
            if hits and t not in var_types:
                add("high", m.start(), f"{t}.{mem}", hits)
        for m in RX_DOT_MEMBER.finditer(src):
            mem = m.group(1)
            hits = by_member.get(mem)
            if not hits:
                continue
            if re.search(r"(?<![A-Za-z_])\d+$", src[max(0, m.start() - 12):m.start()]):
                continue          # numeric literal like 1.5f
            # `Type.Member` (capitalised receiver that is not a known variable) is handled by the qualified pass
            before = src[max(0, m.start() - 60):m.start()]
            qm = re.search(r"(?<![A-Za-z0-9_.])([A-Z][A-Za-z0-9_]*)\s*$", before)
            if qm and qm.group(1) not in var_types:
                continue
            # only declaring types whose namespace the file imports (heuristic noise filter)
            hits = [h for h in hits if importable(h)]
            if not hits:
                continue
            call = bool(m.group(2))
            kinds = {h.kind for h in hits}
            if call and not (kinds & {"M"}):
                continue
            if not call and kinds <= {"M"}:
                continue
            rt = receiver_type(m.start())
            if rt:
                typed = [h for h in hits if h.type.split(".")[-1] == rt]
                if not typed:
                    continue          # receiver's declared type is known and is not one of the obsolete declarers
                add("high", m.start(), f"{rt}.{mem} (receiver type from declaration)", typed)
                continue
            noise = len(live.get(mem, ()))
            conf = "medium" if noise <= 2 else "low"
            add(conf, m.start(), f".{mem}", hits)
        for m in RX_BARE_CALL.finditer(src):
            name = m.group(1)
            if name in inherited:
                hits = [h for h in by_member[name] if h.full_type in INHERITED_SCOPES]
                add("medium", m.start(), f"{name}(...) (inherited)", hits)
        for m in RX_IDENT.finditer(src):
            name = m.group(0)
            if name in obsolete_types and name in unity_types:
                prev = src[max(0, m.start() - 1):m.start()]
                if prev == ".":
                    continue
                after = src[m.end():m.end() + 2].lstrip()
                if after.startswith("(") and not re.search(r"\bnew\s+$", src[max(0, m.start() - 8):m.start()]):
                    continue          # method call / declaration with the same name
                hits = [h for h in obsolete_types[name] if importable(h)]
                if hits:
                    add("medium", m.start(), f"type {name}", hits)

    order = {"high": 0, "medium": 1, "low": 2}
    findings.sort(key=lambda x: (order[x[0]], str(x[1]), x[2]))
    if not show_all:
        low = sum(1 for x in findings if x[0] == "low")
        findings = [x for x in findings if x[0] != "low"]
    else:
        low = 0
    return findings, low, len(files)


def fmt_hits(hits, limit=3):
    parts = []
    uniq, seen = [], set()
    for h in hits:
        k = (h.api, h.error, h.message)
        if k not in seen:
            seen.add(k)
            uniq.append(h)
    hits = uniq
    for h in hits[:limit]:
        kind = "ERROR" if h.error else "warning"
        msg = (h.message or "").strip() or "(no message)"
        parts.append(f"{h.api} [{kind}]: {msg}")
    if len(hits) > limit:
        parts.append(f"... +{len(hits) - limit} more declaring types")
    return parts


# ---------------------------------------------------------------------------------------------
# lookup
# ---------------------------------------------------------------------------------------------

def lookup(query: str, rows, refs):
    q = query.strip()
    parts = q.split(".")
    results = []
    # try "Type.Member" split at every position (supports Namespace.Type.Member and Outer.Inner.Member)
    type_rows = [r for r in rows if r.kind == "T" and r.access != "partial"]
    for i in range(len(parts), 0, -1):
        tname = ".".join(parts[:i])
        member = ".".join(parts[i:]) if i < len(parts) else ""
        tmatch = [r for r in type_rows if r.full_type == tname or r.type == tname or r.full_type.endswith("." + tname)]
        if not tmatch:
            continue
        full_types = sorted({r.full_type for r in tmatch})
        if not member:
            results = [("type", ft, [r for r in tmatch if r.full_type == ft]) for ft in full_types]
            break
        found = []
        for ft in full_types:
            mrows = [r for r in rows if r.kind != "T" and r.full_type == ft and r.member == member]
            if mrows:
                found.append(("member", ft, mrows))
        if found:
            results = found
            break
        # type exists but member not found: suggestions
        cands = sorted({r.member for r in rows if r.full_type in full_types and r.kind != "T" and r.member})
        sugg = difflib.get_close_matches(member, cands, n=5, cutoff=0.6)
        return [("missing-member", ", ".join(full_types), member, sugg)]
    if not results:
        # maybe a bare member name
        mrows = [r for r in rows if r.kind != "T" and r.member == q]
        if mrows:
            byt = defaultdict(list)
            for r in mrows:
                byt[r.full_type].append(r)
            return [("member", t, rs) for t, rs in sorted(byt.items())]
        names = sorted({r.type.split(".")[-1] for r in type_rows})
        return [("missing-type", q, difflib.get_close_matches(parts[-1], names, n=5, cutoff=0.75))]
    return results


def print_lookup(query, res, refs):
    print(f"== {query}")
    for item in res:
        if item[0] == "missing-member":
            _, types, member, sugg = item
            print(f"  NOT FOUND in Unity {UNITY_VERSION}: {types} has no public/protected member '{member}'")
            if sugg:
                print(f"  did you mean: {', '.join(sugg)}")
            continue
        if item[0] == "missing-type":
            _, q, sugg = item
            print(f"  NOT FOUND in UnityCsReference {UNITY_VERSION}: no public engine/editor type '{q}'")
            print("  (UnityCsReference only covers UnityEngine/UnityEditor, not packages like Input System, uGUI, URP, IAP)")
            if refs is not None:
                parts = q.split(".")
                for i in range(len(parts), 0, -1):
                    t, mem = ".".join(parts[:i]), ".".join(parts[i:])
                    hits = [k for k in refs if k == t or k.endswith("." + t)]
                    if hits:
                        for k in hits[:3]:
                            ok = not mem or mem in refs[k]
                            print(f"  {'present' if ok else 'type present, member NOT found'} in the assemblies check.sh compiles against: "
                                  f"{k}{'.' + mem if mem else ''} ({refs.assembly.get(k, '?')})")
                        break
            if sugg:
                print(f"  similar engine types: {', '.join(sugg)}")
            continue
        kind, ft, rs = item
        member = rs[0].member if kind == "member" else ""
        api = ft + (f".{member}" if member else "")
        obs = [r for r in rs if r.obsolete]
        status = "EXISTS"
        if obs and len(obs) == len(rs):
            status = "EXISTS but [Obsolete" + (", error]" if any(r.error for r in obs) else "]")
        elif obs:
            status = f"EXISTS ({len(obs)}/{len(rs)} overloads [Obsolete])"
        print(f"  {status} in Unity {UNITY_VERSION}: {api}")
        for r in rs[:6]:
            sig = f"{r.kind} {r.access}" + (f", {r.arity} params" if r.arity >= 0 else "")
            ob = ""
            if r.obsolete:
                ob = f"  [Obsolete{', error' if r.error else ''}] {r.message}"
            print(f"      {sig:<24} {r.file}:{r.line}{ob}")
        if len(rs) > 6:
            print(f"      ... {len(rs) - 6} more")
        if refs is not None:
            in_refs = ft in refs and (not member or member in refs[ft] or f"get_{member}" in refs[ft])
            if in_refs:
                print(f"  present in the reference assemblies check.sh compiles against ({refs.assembly.get(ft, '?')})")
            else:
                print("  NOT in the reference assemblies check.sh compiles against (engine refs are 6000.0.58 player builds, so "
                      "6000.1+ and editor-only engine members are missing; UnityEditor there is 2021.1) -> check.sh reports "
                      "such uses as 'unity6-only' (not a failure; editor-only members only in editor code)")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--lookup", action="append", metavar="Type.Member", help="check whether an API exists in 6000.3.25f1 (repeatable)")
    ap.add_argument("--project", default=str(DEFAULT_PROJECT), help="Unity project root (default: %(default)s)")
    ap.add_argument("--paths", nargs="*", help="files/dirs to scan (default: <project>/Assets/BeamRunner)")
    ap.add_argument("--index", default=str(DEFAULT_INDEX), help="UnityCsReference API index TSV (default: %(default)s)")
    ap.add_argument("--all", action="store_true", help="also list low-confidence matches")
    ap.add_argument("--tsv", action="store_true", help="machine readable output for the scan")
    args = ap.parse_args()

    rows = load_index(Path(args.index))
    if args.lookup:
        refs = load_refs(REFS_INDEX)
        for q in args.lookup:
            print_lookup(q, lookup(q, rows, refs), refs)
        return 0

    project = Path(args.project)
    paths = args.paths or [str(project / "Assets" / "BeamRunner")]
    findings, low, nfiles = scan(paths, rows, args.all)
    base = project.parent
    if args.tsv:
        print("confidence\tfile\tline\tmatch\tobsolete_api\terror\tmessage")
        for conf, f, ln, text, hits in findings:
            for h in hits:
                print(f"{conf}\t{os.path.relpath(f, base)}\t{ln}\t{text}\t{h.api}\t{int(h.error)}\t{h.message}")
        return 0
    print(f"Unity {UNITY_VERSION} obsolete-API scan: {nfiles} files under {', '.join(paths)}")
    if not findings:
        print("  no matches" + (f" ({low} low-confidence matches hidden, use --all)" if low else ""))
        return 0
    errors = 0
    for conf, f, ln, text, hits in findings:
        is_err = any(h.error for h in hits) and all(h.error for h in hits)
        errors += is_err
        print(f"{os.path.relpath(f, base)}({ln}): [{conf}] {text}")
        for p in fmt_hits(hits):
            print(f"      {p}")
    print(f"{len(findings)} match(es){f', {low} low-confidence hidden (--all)' if low else ''}; "
          f"{errors} would be compile ERRORS in Unity {UNITY_VERSION} if the match is real. Text-based: verify each hit "
          f"(check.sh's semantic audit resolves symbols exactly).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
