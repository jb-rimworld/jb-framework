#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Builds the knowledge base Common/Defs/ModNotes/*.xml and its German DefInjected files from tools/modnotes/notes.py.

  python3 -I tools/modnotes/build.py          (from the framework folder)

Facts come only from real About.xml files (Rimling's Workshop cache ~/Library/Caches/RimlingModCompat/... and the local Steam
Workshop folder), from our own mods' About.xml files listed in EXTRA, and from Rimling/ModKnowledge/<packageId>.json. A packageId
that is in none of them, or whose case differs from its About.xml, stops the build: packageIds are never invented.
Versions and Workshop ids of a note are filled from the About.xml files found.
"""
import html, json, os, sys, xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
HERE = os.path.dirname(os.path.abspath(__file__))
FWROOT = os.path.dirname(os.path.dirname(HERE))
CODING = os.path.dirname(FWROOT)
sys.path.insert(0, HERE)
from notes import N

ROOTS = {"cache": os.path.expanduser("~/Library/Caches/RimlingModCompat/dl/steamapps/workshop/content/294100"),
         "subs": os.path.expanduser("~/Library/Application Support/Steam/steamapps/workshop/content/294100")}
EXTRA = [os.path.join(CODING, "_Release/Packages_v110/Silverline/About/About.xml"),
         os.path.join(CODING, "QualityBuilderRobots/About/About.xml"),
         os.path.join(CODING, "HearthHarbor/About/About.xml")]
K = os.path.join(CODING, "Rimling/ModKnowledge")
FW = os.path.join(FWROOT, "Common")

def read_about(p, src, wid):
    try:
        r = ET.fromstring(open(p, "rb").read().decode("utf-8-sig", "replace").encode("utf-8"))
    except Exception:
        return None
    return dict(src=src, wid=wid, pid=(r.findtext("packageId") or "").strip(), name=(r.findtext("name") or "").strip(),
                sv=[li.text.strip() for li in r.findall("supportedVersions/li") if li.text])

about = []
for src, C in ROOTS.items():
    if not os.path.isdir(C): continue
    for wid in sorted(os.listdir(C)):
        for cand in ("About/About.xml", "about/About.xml", "About/about.xml"):
            p = os.path.join(C, wid, cand)
            if os.path.exists(p):
                m = read_about(p, src, wid)
                if m: about.append(m)
                break
for p in EXTRA:
    if os.path.exists(p):
        m = read_about(p, "local", "")
        if m:
            pf = os.path.join(os.path.dirname(p), "PublishedFileId.txt")
            if os.path.exists(pf): m["wid"] = open(pf).read().strip()
            about.append(m)
by = {}
for m in about: by.setdefault(m['pid'].lower(), []).append(m)
groups = {'library':'Libraries','combat-overhaul':'Combat','alien-races':'Races','genes':'Races','adds-buildings':'VanillaExpanded',
          'psycasts':'VanillaExpanded','adds-pipe-networks':'VanillaExpanded','changes-bills-ui':'Bills','changes-bills':'Bills',
          'replaces-construction':'Construction','changes-quality':'Construction','changes-architect':'Construction','changes-storage':'Storage',
          'guests':'Guests','economy':'Guests','robots':'Robots'}
files = {}
errors = []
for n in N:
    ms = []
    for pid in n['ids']:
        hit = by.get(pid.lower())
        if not hit and not os.path.exists(os.path.join(K, pid.lower() + '.json')): errors.append('unknown packageId ' + pid)
        exact = [m for m in (hit or []) if m['pid'] == pid]
        if hit and not exact: errors.append('case differs: ' + pid + ' vs ' + hit[0]['pid'])
        ms += hit or []
    label = n['label'] or html.unescape((by.get(n['ids'][0].lower()) or [{'name': n['ids'][0]}])[0]['name'])
    vers = sorted({v for m in ms for v in m['sv'] if v[0].isdigit()}, key=lambda v: [int(x) for x in v.split('.')])
    wids = sorted({m['wid'] for m in ms if m['wid']})
    g = 'Misc'
    for t in n['tags']:
        if t in groups: g = groups[t]; break
    if n['defName'] in ('JBF_Note_HearthHarbor', 'JBF_Note_Silverline'): g = 'Guests'
    files.setdefault(g, []).append((n, label, vers, wids))
if errors: print('\n'.join(errors)); sys.exit(1)
def li(tag, vals, ind='    '):
    if not vals: return ''
    return ind + '<' + tag + '>' + ''.join('<li>' + escape(v) + '</li>' for v in vals) + '</' + tag + '>\n'
total = 0
for g, items in files.items():
    en = ['<?xml version="1.0" encoding="utf-8"?>', '<!-- JB Framework compatibility knowledge base (' + g + '). Generated from real About.xml files and Rimling/ModKnowledge;',
          '     packageIds are never invented. Format: Source/JBFramework/Compat/ModNoteDef.cs. German: Languages/German (Deutsch)/DefInjected. -->', '<Defs>']
    de = ['<?xml version="1.0" encoding="utf-8"?>', '<LanguageData>']
    for n, label, vers, wids in items:
        total += 1
        en.append('  <JBFramework.ModNoteDef>')
        en.append('    <defName>' + n['defName'] + '</defName>')
        en.append('    <label>' + escape(label) + '</label>')
        en.append('    <description>' + escape(n['desc'][0]) + '</description>')
        s = li('packageIds', n['ids']) + li('tags', n['tags']) + li('versions', vers) + li('workshopIds', wids) + li('harmonyIds', n['harmony'])
        en.append(s.rstrip('\n'))
        if n['pit']: en.append('    <pitfall>' + escape(n['pit'][0]) + '</pitfall>')
        if n['adv']: en.append('    <advice>' + escape(n['adv'][0]) + '</advice>')
        if n['src']: en.append('    <source>' + escape(n['src']) + '</source>')
        en.append('  </JBFramework.ModNoteDef>')
        de.append('  <' + n['defName'] + '.label>' + escape(label) + '</' + n['defName'] + '.label>')
        de.append('  <' + n['defName'] + '.description>' + escape(n['desc'][1]) + '</' + n['defName'] + '.description>')
        if n['pit']: de.append('  <' + n['defName'] + '.pitfall>' + escape(n['pit'][1]) + '</' + n['defName'] + '.pitfall>')
        if n['adv']: de.append('  <' + n['defName'] + '.advice>' + escape(n['adv'][1]) + '</' + n['defName'] + '.advice>')
    en.append('</Defs>'); de.append('</LanguageData>')
    os.makedirs(FW + '/Defs/ModNotes', exist_ok=True)
    os.makedirs(FW + '/Languages/German (Deutsch)/DefInjected/JBFramework.ModNoteDef', exist_ok=True)
    open(FW + '/Defs/ModNotes/ModNotes_' + g + '.xml', 'w').write('\n'.join(en) + '\n')
    open(FW + '/Languages/German (Deutsch)/DefInjected/JBFramework.ModNoteDef/ModNotes_' + g + '.xml', 'w').write('\n'.join(de) + '\n')
print('notes', total, 'files', sorted(files))
