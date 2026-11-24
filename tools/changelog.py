#!/usr/bin/env python3
"""CHANGELOG.md -> Steam change note and the description's Changelog block (JB Framework release tooling).

    python3 -I changelog.py <ModDir> note              top entry as plain text (Steam change note)
    python3 -I changelog.py <ModDir> block [--n 5]     "[h2]Changelog[/h2]" + [list] of the newest N entries (BBCode)
    python3 -I changelog.py <ModDir> inject [--n 5] [--file F] [--write]
                                                       replace the Changelog block inside Workshop/description.bbcode
                                                       (prints the result; --write saves it; without --write nothing changes)
    python3 -I changelog.py <ModDir> check [--version X.Y.Z]
                                                       CHANGELOG.md is well formed; top entry == version (or About modVersion)

CHANGELOG.md format, newest first:

    ## 2.0.0 (2026-12-02)
    - Plain English bullet.
    - Another bullet, one sentence each.

The heading is "## <version> (<YYYY-MM-DD>)"; the date may be missing for unreleased work ("## 2.0.0"), which `note` and `block` accept
(printed without a date) and `check --release` rejects. Entries without bullets are an error.
"""
import datetime, os, re, sys

HEAD = re.compile(r"^##\s+(\S+)(?:\s+\((\d{4})-(\d{2})-(\d{2})\))?\s*$")
MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]


def parse(path):
    """-> list of dict(version, date|None, bullets[list[str]]), newest first as written. Raises ValueError on bad format."""
    if not os.path.isfile(path):
        raise ValueError(f"{path} not found")
    entries, cur = [], None
    for n, raw in enumerate(open(path, encoding="utf-8").read().splitlines(), 1):
        line = raw.rstrip()
        if not line.strip() or line.startswith("# "):
            continue
        if line.startswith("##"):
            m = HEAD.match(line)
            if not m:
                raise ValueError(f"{path}:{n}: bad heading {line!r} (want '## 2.0.0 (2026-12-02)')")
            date = None
            if m.group(2):
                try:
                    date = datetime.date(int(m.group(2)), int(m.group(3)), int(m.group(4)))
                except ValueError:
                    raise ValueError(f"{path}:{n}: invalid date in {line!r}")
            cur = {"version": m.group(1), "date": date, "bullets": []}
            entries.append(cur)
        elif re.match(r"^\s*[-*]\s+", line):
            if cur is None:
                raise ValueError(f"{path}:{n}: bullet before the first '##' heading")
            cur["bullets"].append(re.sub(r"^\s*[-*]\s+", "", line).strip())
        elif cur is not None and cur["bullets"] and line.startswith("  "):
            cur["bullets"][-1] += " " + line.strip()          # wrapped bullet
        else:
            raise ValueError(f"{path}:{n}: unexpected line {line!r} (only '## ver (date)' headings and '- ' bullets)")
    for e in entries:
        if not e["bullets"]:
            raise ValueError(f"{path}: entry {e['version']} has no bullets")
    if not entries:
        raise ValueError(f"{path}: no entries")
    return entries


def fmt_date(d):
    return f"{d.day} {MONTHS[d.month - 1]} {d.year}" if d else None


def plain(s):
    """BBCode has no place in a plain note: drop tags, keep text."""
    return re.sub(r"\[/?[A-Za-z*]+(?:=[^\]]*)?\]", "", s)


def join_bullets(bullets):
    out = []
    for b in bullets:
        b = b.strip()
        out.append(b if b[-1:] in ".!?" else b + ".")
    return " ".join(out)


def note(entries):
    e = entries[0]
    head = e["version"] + (f" ({fmt_date(e['date'])})" if e["date"] else "")
    return plain(f"{head}: " + join_bullets(e["bullets"]))


def block(entries, n=5):
    lines = ["[h2]Changelog[/h2]", "[list]"]
    for e in entries[:n]:
        head = e["version"] + (f" ({fmt_date(e['date'])})" if e["date"] else "")
        lines.append(f"[*][b]{head}[/b]: {join_bullets(e['bullets'])}")
    lines.append("[/list]")
    return "\n".join(lines)


def inject(desc_text, new_block):
    """Replace the existing '[h2]Changelog[/h2]' ... '[/list]' block; refuses when it cannot find exactly one."""
    m = re.search(r"\[h2\]Changelog\[/h2\]\s*\[list\].*?\[/list\]", desc_text, re.S)
    if not m or len(re.findall(r"\[h2\]Changelog\[/h2\]", desc_text)) != 1:
        raise ValueError("description needs exactly one '[h2]Changelog[/h2]' followed by a [list] block")
    return desc_text[:m.start()] + new_block + desc_text[m.end():]


def about_version(mod):
    p = os.path.join(mod, "About", "About.xml")
    if not os.path.isfile(p):
        return None
    m = re.search(r"<modVersion>\s*([^<\s]+)\s*</modVersion>", open(p, encoding="utf-8").read())
    return m.group(1) if m else None


def main(argv):
    if len(argv) < 3 or argv[2] not in ("note", "block", "inject", "check"):
        print(__doc__); return 2
    mod, cmd = os.path.abspath(argv[1]), argv[2]
    n = int(argv[argv.index("--n") + 1]) if "--n" in argv else 5
    try:
        entries = parse(os.path.join(mod, "CHANGELOG.md"))
    except ValueError as e:
        print("ERROR", e); return 1
    if cmd == "note":
        print(note(entries))
    elif cmd == "block":
        print(block(entries, n))
    elif cmd == "inject":
        f = argv[argv.index("--file") + 1] if "--file" in argv else os.path.join(mod, "Workshop", "description.bbcode")
        try:
            out = inject(open(f, encoding="utf-8").read(), block(entries, n))
        except (ValueError, OSError) as e:
            print("ERROR", e); return 1
        if "--write" in argv:
            open(f, "w", encoding="utf-8").write(out); print(f"updated {f}")
        else:
            print(out)
    else:  # check
        ok = True
        want = argv[argv.index("--version") + 1] if "--version" in argv else about_version(mod)
        if want and entries[0]["version"] != want:
            print(f"ERROR top CHANGELOG entry is {entries[0]['version']}, expected {want}"); ok = False
        if "--release" in argv and not entries[0]["date"]:
            print("ERROR top entry has no date"); ok = False
        seen = [e["version"] for e in entries]
        if len(set(seen)) != len(seen):
            print("ERROR duplicate versions in CHANGELOG"); ok = False
        print(f"CHANGELOG ok: {len(entries)} entries, top {entries[0]['version']}" if ok else "CHANGELOG problems")
        return 0 if ok else 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
