#!/usr/bin/env python3
"""Lint a Workshop description (BBCode) against the JB house style (_Planung_Marketing_2026-11-24/desc/STYLE.md).

    python3 -I desc_lint.py <file.bbcode>... [--mod <ModDir>] [--about <About.xml>] [--keys art,strip] [--job job.json [--item N]]
                                             [--strict] [--quiet]

Enforced (ERROR = exit 1):  size <= 7300 bytes UTF-8 with every [[IMG:key]] counted as 150; only the tags h1 h2 h3 b i u strike list * quote url
img hr code; every tag closed and properly nested ([*] only inside [list], [hr] written [hr][/hr]); no HTML, no Markdown, no emoji, no
exclamation marks; section order (h1, italic tagline, first quote = fact line starting Main Mod|Gameplay|Tech, feature h2s, then
Settings < Compatibility < FAQ < More from JB < Thanks < Changelog); More from JB and Changelog present; Changelog <= 5 entries; footer
(optional keywords line, [hr][/hr], "Source, issues, ideas:", [i]Built with the help of Claude Code ... (c) Julius Berger ...[/i]);
[[IMG:key]] alone on its line and, when keys are known (--keys/--job), every key exists; fact-line versions equal About supportedVersions
(--mod or --about).
WARN (ERROR with --strict): unknown bracket words, missing Compatibility/FAQ/Thanks, FAQ not 2-5 questions, tagline > 14 words, em-dash
overuse, "not just X but Y", image placeholder not under an h2, About description > 1500 characters (with --mod).
Facts themselves (numbers, requirements, compatibility claims) stay a human check.
"""
import json, os, re, sys

ALLOWED = {"h1", "h2", "h3", "b", "i", "u", "strike", "list", "*", "quote", "url", "img", "hr", "code"}
LIMIT, IMG_COST = 7300, 150
IMG = re.compile(r"\[\[IMG:([A-Za-z0-9_\-]+)\]\]")
TAG = re.compile(r"\[(/?)([A-Za-z0-9*]+)(=[^\]\n]*)?\]")
EMOJI = re.compile("[\U0001F000-\U0001FAFF☀-➿⬀-⯿️‍]")
ORDER = [("settings", 1), ("compatibility", 2), ("faq", 3), ("more from jb", 4), ("thanks", 5), ("changelog", 6)]


def rank(title):
    t = title.strip().lower()
    for name, r in ORDER:
        if t == name or t.startswith(name):
            return r
    return 0


def lineno(text, pos):
    return text.count("\n", 0, pos) + 1


def size_of(text):
    return len(IMG.sub("", text).encode("utf-8")) + IMG_COST * len(IMG.findall(text))


def lint(text, versions=None, keys=None):
    """-> list of (level, line, message). versions: set of About supportedVersions; keys: set of image keys or None."""
    out = []
    add = lambda lvl, pos, msg: out.append((lvl, lineno(text, pos), msg))      # pos = character offset
    addl = lambda lvl, line, msg: out.append((lvl, line, msg))                  # line = 1-based line number

    sz = size_of(text)
    if sz > LIMIT:
        addl("ERROR", 1, f"size {sz} bytes > {LIMIT} (IMG placeholders counted as {IMG_COST})")

    # --- plain-text hygiene (outside urls)
    scrub = re.sub(r"\[url=[^\]]*\]", "[url]", text)
    scrub = re.sub(r"\[img\].*?\[/img\]", "[img][/img]", scrub, flags=re.S)
    scrub = re.sub(r"https?://\S+", "URL", scrub)
    for m in re.finditer("!", scrub):
        add("ERROR", scrub.count("\n", 0, m.start()) + 1, "exclamation mark (house style: none)"); break
    m = EMOJI.search(text)
    if m:
        add("ERROR", m.start(), f"emoji/pictograph U+{ord(m.group()):04X}")
    for m in re.finditer(r"</?[A-Za-z][A-Za-z0-9]*(\s[^>]*)?/?>", text):
        add("ERROR", m.start(), f"HTML tag {m.group()!r}"); break
    for pat, what in ((r"\*\*[^*\n]+\*\*", "Markdown bold"), (r"(?m)^#{1,6} \S", "Markdown heading"), (r"`[^`\n]+`", "Markdown code"),
                      (r"\]\(https?://", "Markdown link")):
        m = re.search(pat, text)
        if m:
            add("ERROR", m.start(), what)
    if re.search(r"(?m)^\s*[-+] \S", text):
        add("WARN", re.search(r"(?m)^\s*[-+] \S", text).start(), "line starts with '- ' (Markdown list?); use [list][*]")
    dashes = text.count("—")
    if dashes > 2 or re.search(r"—[^\n—]{0,80}—[^\n—]{0,80}—", text):
        addl("WARN", 1, f"{dashes} em dashes (no em-dash chains)")
    m = re.search(r"\bnot (?:just|only|merely)\b[^.\n]{1,80}\bbut\b", text, re.I)
    if m:
        add("WARN", m.start(), "'not just X but Y' construction")

    # --- tags
    body = IMG.sub(lambda m: " " * len(m.group()), text)
    stack, in_code = [], False
    for m in TAG.finditer(body):
        close, name = m.group(1) == "/", m.group(2).lower()
        pos = m.start()
        if name not in ALLOWED:
            if name.isdigit() or in_code:
                continue
            add("WARN", pos, f"unknown bracket word {m.group()!r} (renders literally; allowed tags: {' '.join(sorted(ALLOWED))})")
            continue
        if in_code and not (close and name == "code"):
            continue
        if name == "*":
            if close:
                add("ERROR", pos, "[/*] is not a tag")
            elif "list" not in stack:
                add("ERROR", pos, "[*] outside [list]")
            continue
        if not close:
            stack.append(name)
            if name == "code": in_code = True
        else:
            if name == "code": in_code = False
            if not stack:
                add("ERROR", pos, f"closing [/{name}] without opening"); continue
            if stack[-1] != name:
                add("ERROR", pos, f"[/{name}] closes but [{stack[-1]}] is open (nesting)")
                if name in stack:
                    while stack and stack[-1] != name: stack.pop()
                    stack.pop()
                continue
            stack.pop()
    for name in stack:
        add("ERROR", len(text), f"[{name}] never closed")

    # --- structure
    lines = [(i + 1, l.strip()) for i, l in enumerate(text.splitlines()) if l.strip()]
    if not lines or not re.fullmatch(r"\[h1\].+\[/h1\]", lines[0][1]):
        addl("ERROR", 1, "first line must be [h1]Title[/h1]")
    if len(lines) < 2 or not re.fullmatch(r"\[i\].+\[/i\]", lines[1][1]):
        addl("ERROR", lines[1][0] if len(lines) > 1 else 1, "second line must be the italic tagline [i]...[/i]")
    else:
        words = len(re.sub(r"\[/?i\]", "", lines[1][1]).split())
        if words > 14:
            addl("WARN", lines[1][0], f"tagline has {words} words (max ~12)")

    first_h2 = re.search(r"\[h2\]", text)
    q = re.search(r"\[quote\](.*?)\[/quote\]", text, re.S)
    if not q or (first_h2 and q.start() > first_h2.start()):
        addl("ERROR", 1, "fact line [quote]...[/quote] missing before the first [h2]")
    else:
        fl = q.group(1).strip()
        if not re.match(r"\[b\](Main Mod|Gameplay|Tech)\[/b\]", fl):
            add("ERROR", q.start(), "fact line must start with [b]Main Mod|Gameplay|Tech[/b]")
        vm = re.search(r"RimWorld\s+(\d+\.\d+(?:\s*/\s*\d+\.\d+)*)", fl)
        if not vm:
            add("ERROR", q.start(), "fact line has no 'RimWorld 1.x / 1.y' part")
        elif versions is not None:
            got = {v.strip() for v in vm.group(1).split("/")}
            if got != set(versions):
                add("ERROR", q.start(), f"fact-line versions {sorted(got)} != About supportedVersions {sorted(versions)}")

    h2s = [(m.start(), m.group(1)) for m in re.finditer(r"\[h2\](.*?)\[/h2\]", text)]
    seen_rank, last_rank, last_title = False, 0, ""
    for pos, title in h2s:
        r = rank(title)
        if r == 0 and seen_rank:
            add("ERROR", pos, f"feature section '{title}' after Settings/Compatibility/FAQ/More/Thanks/Changelog")
        if r and r < last_rank:
            add("ERROR", pos, f"section '{title}' out of order (after '{last_title}')")
        if r:
            seen_rank, last_rank, last_title = True, max(r, last_rank), title
    ranks = {rank(t) for _, t in h2s}
    for r, name, lvl in ((4, "More from JB", "ERROR"), (6, "Changelog", "ERROR"), (2, "Compatibility", "WARN"), (3, "FAQ", "WARN"), (5, "Thanks", "WARN")):
        if r not in ranks:
            addl(lvl, 1, f"section [h2]{name}[/h2] missing")
    for i, (pos, title) in enumerate(h2s):
        end = h2s[i + 1][0] if i + 1 < len(h2s) else len(text)
        seg = text[pos:end]
        if rank(title) == 3:
            n = len(re.findall(r"(?m)^\[b\]\s*Q", seg))
            if not 2 <= n <= 5:
                add("WARN", pos, f"FAQ has {n} questions (want 2-5, each '[b]Q...[/b]')")
        if rank(title) == 6:
            n = len(re.findall(r"\[\*\]", seg.split("[hr]")[0]))
            if n > 5: add("ERROR", pos, f"Changelog has {n} entries (max 5)")
            if n == 0: add("ERROR", pos, "Changelog has no entries")
        if rank(title) == 4:
            if "my Workshop page" not in seg:
                add("WARN", pos, "More from JB lacks the 'All of it: [url=...]my Workshop page[/url].' line")

    # footer: [hr][/hr] / Source line / [i]Built with ... [/i], optional keywords line above
    tail = [l for _, l in lines][-3:]
    if len(tail) < 3 or tail[0] != "[hr][/hr]":
        addl("ERROR", lines[-1][0] if lines else 1, "footer must end with [hr][/hr], 'Source, issues, ideas: ...', [i]Built with ...[/i]")
    else:
        if not tail[1].startswith("Source, issues, ideas:"):
            addl("ERROR", lines[-2][0], "footer line 1 must start 'Source, issues, ideas:'")
        if not (tail[2].startswith("[i]Built with the help of Claude Code") and "© Julius Berger" in tail[2] and tail[2].endswith("[/i]")):
            addl("ERROR", lines[-1][0], "footer line 2 must be [i]Built with the help of Claude Code ... © Julius Berger ...[/i]")
        k = [l for _, l in lines][-4] if len(lines) >= 4 else ""
        if k and not k.startswith("[") and not re.fullmatch(r"[^\[\]]+,[^\[\]]+", k) and not k.startswith("[/list]"):
            addl("WARN", lines[-4][0], "line above the footer is neither a lower-case keywords line nor the end of a block")
    if h2s and h2s[-1][1].strip().lower().startswith("changelog") is False:
        add("ERROR", h2s[-1][0], "last [h2] must be Changelog (footer follows it)")

    # image placeholders
    seen = set()
    raw = text.splitlines()
    for i, l in enumerate(raw):
        for m in IMG.finditer(l):
            key = m.group(1)
            if l.strip() != m.group():
                addl("ERROR", i + 1, f"[[IMG:{key}]] must be alone on its line")
            if key in seen:
                addl("WARN", i + 1, f"[[IMG:{key}]] used twice")
            seen.add(key)
            if keys is not None and key not in keys:
                addl("ERROR", i + 1, f"[[IMG:{key}]] has no image with that key in the job (keys: {sorted(keys) or 'none'})")
            j = i - 1
            while j >= 0 and not raw[j].strip(): j -= 1
            prev = raw[j].strip() if j >= 0 else ""
            if not (prev.startswith("[h2]") or prev.startswith("[quote]") or prev.startswith("[h1]")):
                addl("WARN", i + 1, f"[[IMG:{key}]] is not directly under an [h2] (or the fact line)")
    return out


def about_info(about_path):
    import xml.etree.ElementTree as ET
    r = ET.parse(about_path).getroot()
    return {"versions": {e.text.strip() for e in r.findall("./supportedVersions/li") if e.text},
            "desc": (r.findtext("description") or "")}


def main(argv):
    files, mod, about, keys, job, item = [], None, None, None, None, None
    strict, quiet = "--strict" in argv, "--quiet" in argv
    it = iter(argv[1:])
    for a in it:
        if a == "--mod": mod = next(it)
        elif a == "--about": about = next(it)
        elif a == "--keys": keys = {k for k in next(it).split(",") if k}
        elif a == "--job": job = next(it)
        elif a == "--item": item = next(it)
        elif a in ("--strict", "--quiet"): pass
        elif a.startswith("--"): print("unknown option", a); return 2
        else: files.append(a)
    if mod and not files:
        files = [os.path.join(mod, "Workshop", "description.bbcode")]
    if not files:
        print(__doc__); return 2
    if mod and not about:
        about = os.path.join(mod, "About", "About.xml")
    versions, adesc = None, None
    if about and os.path.isfile(about):
        info = about_info(about); versions, adesc = info["versions"], info["desc"]
    if job:
        items = json.load(open(job, encoding="utf-8"))["items"]
        if item is not None:
            sel = [x for x in items if str(x.get("id")) == item or x.get("name") == item]
            items = sel or ([items[int(item)]] if item.isdigit() and int(item) < len(items) else [])
        keys = {im["key"] for x in items for im in x.get("images", [])} if keys is None else keys
    bad = 0
    for f in files:
        try:
            text = open(f, encoding="utf-8").read()
        except OSError as e:
            print(f"ERROR {f}: {e}"); bad += 1; continue
        res = lint(text, versions, keys)
        if adesc is not None and len(adesc) > 1500:
            res.append(("WARN", 1, f"About.xml description is {len(adesc)} characters (keep the in-game text short, ~1500)"))
        errs = [r for r in res if r[0] == "ERROR" or (strict and r[0] == "WARN")]
        for lvl, ln, msg in res:
            if quiet and lvl == "WARN" and not strict: continue
            print(f"{lvl:5} {os.path.basename(f)}:{ln}: {msg}")
        print(f"{os.path.basename(f)}: {size_of(text)} bytes, {len([r for r in res if r[0]=='ERROR'])} errors, {len([r for r in res if r[0]=='WARN'])} warnings")
        bad += 1 if errs else 0
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
