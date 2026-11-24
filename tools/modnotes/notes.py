# SPDX-License-Identifier: MIT
# Seed data of the JB Framework knowledge base. Every packageId must exist in a real About.xml (scan) or a ModKnowledge file;
# gen.py checks that and fills versions/workshopIds from the About.xml files. Texts: (EN, DE).
# Facts: descriptions paraphrase Rimling/ModKnowledge/<id>.json "summary"; pitfalls/advice come from the knowledge files' compat notes,
# from our own mods' code and changelogs (sources named per note) or are left empty.
N = []
def note(defname, ids, tags, desc, pit=None, adv=None, src="", label=None, harmony=None):
    N.append(dict(defName="JBF_Note_" + defname, ids=ids, tags=tags, desc=desc, pit=pit, adv=adv, src=src, label=label, harmony=harmony or []))

KB = "Rimling/ModKnowledge"

# ---------------------------------------------------------------- libraries and frameworks
note("Harmony", ["brrainz.harmony"], ["library"],
     ("The patching library almost every code mod needs, JB Framework included.",
      "Die Patch-Bibliothek, die fast jede Code-Mod braucht, auch JB Framework."),
     adv=("Load it first, above everything else.", "Ganz oben laden, vor allem anderen."), src=KB)
note("HugsLib", ["UnlimitedHugs.HugsLib"], ["library"],
     ("A shared library with settings, logging and update news for other mods.",
      "Gemeinsame Bibliothek mit Einstellungen, Log und Update-Hinweisen für andere Mods."), src=KB)
note("VEF", ["OskarPotocki.VanillaFactionsExpanded.Core"], ["library", "adds-pipe-networks"],
     ("Vanilla Expanded Framework: the shared code of every Vanilla Expanded mod, including the pipe network system (PipeSystem).",
      "Vanilla Expanded Framework: der gemeinsame Code aller Vanilla-Expanded-Mods, mit dem Rohrnetz-System (PipeSystem)."),
     adv=("JB mods read VE pipe buildings through the framework's VE adapter and leave their networks alone.",
          "JB-Mods lesen VE-Rohrgebäude über den VE-Adapter des Frameworks und lassen deren Netze in Ruhe."),
     src=KB + "; PipeSystem.dll 1.7 (Workshop 2023507013)")
note("Prepatcher", ["zetrith.prepatcher"], ["library", "early-patcher"],
     ("Patches game code before Harmony runs, for mods that need new fields on vanilla classes.",
      "Patcht Spielcode noch vor Harmony, für Mods, die neue Felder in Vanilla-Klassen brauchen."),
     pit=("On RimWorld 1.7 (new runtime) it stops with \"Not available on non-Mono runtime\"; mods that require it do not work there.",
          "Auf RimWorld 1.7 (neue Laufzeit) bricht es mit „Not available on non-Mono runtime“ ab; Mods, die es voraussetzen, laufen dort nicht."),
     adv=("On 1.7 leave it out unless a mod states 1.7 support. JB mods never need it on 1.7.",
          "Auf 1.7 weglassen, außer eine Mod nennt ausdrücklich 1.7. JB-Mods brauchen es auf 1.7 nie."),
     src=KB + "; RimWard 1.7 test 14.10.2026 (22_RimWorld/CLAUDE.md)")

# ---------------------------------------------------------------- combat
note("CombatExtended", ["CETeam.CombatExtended"], ["combat-overhaul"],
     ("Combat Extended replaces combat with ballistics: calibres, penetration armour, cover, suppression and an ammo and bulk inventory.",
      "Combat Extended ersetzt den Kampf durch Ballistik: Kaliber, Durchschlag gegen Rüstung, Deckung, Niederhalten und Munition mit Gewicht und Volumen."),
     pit=("Every gun needs its ammo; guns made or spawned by other mods arrive without any.",
          "Jede Waffe braucht ihre Munition; von anderen Mods erzeugte Waffen kommen ohne."),
     adv=("JB mods look up a gun's ammo through the framework's CE adapter (default ammo of its ammo set).",
          "JB-Mods finden die Munition einer Waffe über den CE-Adapter des Frameworks (Standardmunition ihres Munitionssatzes)."),
     src=KB + "; CombatExtended.dll 1.6 (Workshop 2890901044); DNDM Creator withAmmo")
note("YayosCombat", ["Mlie.YayosCombat3"], ["combat-overhaul"],
     ("Yayo's Combat 3: a lighter combat overhaul with ammo, armour and accuracy changes.",
      "Yayo's Combat 3: leichtere Kampfüberarbeitung mit Munition, Rüstung und Trefferänderungen."),
     src="About.xml (Workshop 2854006492)")
note("SimpleSidearms", ["PeteTimesSix.SimpleSidearms"], ["combat-overhaul"],
     ("Pawns carry extra weapons as sidearms and switch on their own between melee and ranged.",
      "Figuren tragen Zweitwaffen im Inventar und wechseln selbst zwischen Nah- und Fernkampf."), src=KB)

# ---------------------------------------------------------------- races
note("HAR", ["erdelf.HumanoidAlienRaces"], ["alien-races", "library"],
     ("Humanoid Alien Races: the framework most custom humanoid races are built on (own bodies, graphics, stats, thoughts).",
      "Humanoid Alien Races: das Framework, auf dem die meisten eigenen Menschenrassen aufbauen (eigene Körper, Grafik, Werte, Gedanken)."),
     pit=("Many alien races have their own body instead of the human one: code that expects human limbs and organs can miss parts or do needless work.",
          "Viele Alien-Rassen haben einen eigenen Körper statt des menschlichen: Code, der menschliche Gliedmaßen und Organe erwartet, verfehlt Teile oder arbeitet unnötig."),
     adv=("JB mods ask the framework's HAR adapter whether a race is custom-bodied and fall back to vanilla behaviour for it (Luciferium Overhaul does so by default).",
          "JB-Mods fragen den HAR-Adapter des Frameworks, ob eine Rasse einen eigenen Körper hat, und nutzen dafür Vanilla-Verhalten (Luciferium Overhaul macht das standardmäßig)."),
     src=KB + "; AlienRace.dll 1.6 (Workshop 839005762); LuciferiumOverhaul LORaceModes")
note("Ratkin", ["Solaris.RatkinRaceMod"], ["alien-races"],
     ("NewRatkinPlus: the Ratkin, a small rat-eared humanoid race with factions, research, weapons and buildings (built on HAR).",
      "NewRatkinPlus: die Ratkin, eine kleine Rasse mit Rattenohren, mit Fraktionen, Forschung, Waffen und Gebäuden (auf HAR)."),
     pit=("A player reported tick spikes with Ratkin and Luciferium Overhaul; they could not be reproduced (about 1 µs per tick).",
          "Ein Spieler meldete Tick-Spitzen mit Ratkin und Luciferium Overhaul; nachstellen ließ sich das nicht (etwa 1 µs pro Tick)."),
     adv=("If it happens: Luciferium Overhaul's \"Alien races use vanilla Luciferium healing\" and its performance watchdog.",
          "Falls es auftritt: in Luciferium Overhaul „Alien-Rassen nutzen Vanilla-Luciferium-Heilung“ und den Leistungswächter verwenden."),
     src=KB + "; LuciferiumOverhaul update 13.11.2026 (22_RimWorld/CLAUDE.md)")
note("VREAndroid", ["vanillaracesexpanded.android"], ["alien-races", "genes"],
     ("Vanilla Races Expanded - Android: buildable android colonists with hardware genes, powered by neutroamine.",
      "Vanilla Races Expanded - Android: baubare Android-Kolonist:innen mit Hardware-Genen, betrieben mit Neutroamin."), src=KB)
note("VRESanguophage", ["vanillaracesexpanded.sanguophage"], ["genes", "adds-pipe-networks"],
     ("Vanilla Races Expanded - Sanguophage: more vampire xenotypes, hemogen abilities and a hemogen pipe network.",
      "Vanilla Races Expanded - Sanguophage: weitere Vampir-Xenotypen, Hämogen-Fähigkeiten und ein Hämogen-Rohrnetz."), src=KB)

# ---------------------------------------------------------------- Vanilla Expanded content
note("VFE", ["VanillaExpanded.VFECore"], ["adds-buildings"],
     ("Vanilla Furniture Expanded: about 50 vanilla-style furniture pieces with their own research.",
      "Vanilla Furniture Expanded: rund 50 Möbel im Vanilla-Stil mit eigener Forschung."), src=KB)
note("VFEMedical", ["VanillaExpanded.VFEMedical"], ["adds-buildings", "changes-medical"],
     ("Vanilla Furniture Expanded - Medical: hospital furniture and linkables such as IV drips and vitals monitors.",
      "Vanilla Furniture Expanded - Medical: Krankenhausmöbel und Anbauteile wie Infusionsständer und Vitalmonitore."), src=KB)
note("VFEArchitect", ["VanillaExpanded.VFEArchitect"], ["adds-buildings"],
     ("Vanilla Furniture Expanded - Architect: walls, fences, floors, carpets and bridges.",
      "Vanilla Furniture Expanded - Architect: Mauern, Zäune, Böden, Teppiche und Brücken."), src=KB)
note("VFEProduction", ["VanillaExpanded.VFEProduction"], ["adds-buildings", "changes-bills"],
     ("Vanilla Furniture Expanded - Production: powered early workbenches, tool cabinets and large double workbenches.",
      "Vanilla Furniture Expanded - Production: elektrische frühe Werkbänke, Werkzeugschränke und große Doppelwerkbänke."), src=KB)
note("VFESecurity", ["VanillaExpanded.VFESecurity"], ["adds-buildings"],
     ("Vanilla Furniture Expanded - Security: defensive buildings for every tech level, from traps to turrets and trenches.",
      "Vanilla Furniture Expanded - Security: Verteidigungsbauten für jede Techstufe, von Fallen über Geschütze bis zu Gräben."), src=KB)
note("VEHelixien", ["VanillaExpanded.HelixienGas"], ["adds-pipe-networks", "adds-buildings"],
     ("Vanilla Helixien Gas Expanded: a gas network that feeds generators, heaters and workbenches.",
      "Vanilla Helixien Gas Expanded: ein Gasnetz für Generatoren, Heizungen und Werkbänke."), src=KB)
note("VEChemfuel", ["VanillaExpanded.VChemfuelE"], ["adds-pipe-networks", "adds-buildings"],
     ("Vanilla Chemfuel Expanded: piped deepchem and chemfuel with refineries, tanks and automatic refuelling.",
      "Vanilla Chemfuel Expanded: Tiefenchemie und Chemtreibstoff in Rohren, mit Raffinerien, Tanks und automatischem Betanken."), src=KB)
note("VENutrient", ["VanillaExpanded.VNutrientE"], ["adds-pipe-networks", "adds-buildings"],
     ("Vanilla Nutrient Paste Expanded: piped nutrient paste with grinders, vats, taps and drip feeders.",
      "Vanilla Nutrient Paste Expanded: Nährpaste in Rohren mit Mühlen, Tanks, Zapfstellen und Tropfernährung."), src=KB)
note("VEPsycasts", ["VanillaExpanded.VPsycastsE"], ["psycasts"],
     ("Vanilla Psycasts Expanded: replaces Royalty psycasts with level-up paths and about 150 abilities.",
      "Vanilla Psycasts Expanded: ersetzt die Royalty-Psycasts durch Stufenpfade mit rund 150 Fähigkeiten."), src=KB)
note("VECooking", ["VanillaExpanded.VCookE"], ["changes-bills", "adds-buildings"],
     ("Vanilla Cooking Expanded: many new meals, condiments and cooking buildings.",
      "Vanilla Cooking Expanded: viele neue Mahlzeiten, Würzmittel und Kochgebäude."), src=KB)
note("VFEPirates", ["OskarPotocki.VFE.Pirates"], ["combat-overhaul"],
     ("Vanilla Factions Expanded - Pirates: pirate factions, curses and buildable warcaskets.",
      "Vanilla Factions Expanded - Pirates: Piratenfraktionen, Flüche und baubare Kriegspanzer (Warcaskets)."), src=KB)
note("VFEEmpire", ["OskarPotocki.VFE.Empire"], ["guests"],
     ("Vanilla Factions Expanded - Empire: more noble titles, vassals, honours and noble visits for the Royalty Empire.",
      "Vanilla Factions Expanded - Empire: mehr Adelstitel, Vasallen, Ehren und Adelsbesuche für das Royalty-Imperium."),
     pit=("It changes how guests are set up (guest status); guest mods may see visitors it changed.",
          "Es greift in das Einrichten von Gästen ein (Gaststatus); Gäste-Mods können von ihm veränderte Besucher sehen."),
     src=KB + " (compat notes: patches Pawn_GuestTracker.SetGuestStatus)")

# ---------------------------------------------------------------- plumbing and hygiene
note("DubsBadHygiene", ["Dubwise.DubsBadHygiene"], ["adds-pipe-networks", "adds-buildings"],
     ("Dubs Bad Hygiene: hygiene and bladder needs with plumbing, water, sewage, heating and cooling.",
      "Dubs Bad Hygiene: Hygiene- und Blasenbedürfnis mit Rohrleitungen, Wasser, Abwasser, Heizung und Kühlung."), src=KB)
note("DubsBadHygieneLite", ["Dubwise.DubsBadHygiene.Lite"], ["adds-pipe-networks", "adds-buildings"],
     ("Dubs Bad Hygiene Lite: the same without thirst and water quality.",
      "Dubs Bad Hygiene Lite: dasselbe ohne Durst und Wasserqualität."), src=KB)

# ---------------------------------------------------------------- bills, workbenches, UI
note("NiceBillTab", ["Andromeda.NiceBillTab"], ["changes-bills-ui"],
     ("Nice Bill Tab: a redesigned bills tab with stat readouts.",
      "Nice Bill Tab: ein neu gestalteter Aufträge-Reiter mit Werteanzeigen."),
     pit=("It caches the list of the open bills tab: bills that another mod adds or removes stay invisible until the tab is reopened. It also enlarges the shared bills tab size.",
          "Es puffert die Liste des offenen Aufträge-Reiters: Aufträge, die eine andere Mod hinzufügt oder entfernt, erscheinen erst nach erneutem Öffnen. Außerdem vergrößert es die gemeinsame Größe des Aufträge-Reiters."),
     adv=("JB mods ask it to refresh through the framework's Nice Bill Tab adapter; otherwise reopen the tab.",
          "JB-Mods lassen es über den Nice-Bill-Tab-Adapter des Frameworks neu aufbauen; sonst den Reiter neu öffnen."),
     src=KB + "; NiceBillTab.dll 1.6 (Workshop 3520130671); BetterWorkbenchDefaults fix 13.11.2026; DNDM bill tab size")
note("NiceInventoryTab", ["Andromeda.NiceInventoryTab"], ["changes-pawn-ui", "changes-bills-ui"],
     ("Nice Inventory Tab: a redesigned gear tab; it brings its own copy of Nice Bill Tab when Nice Bill Tab itself is not active.",
      "Nice Inventory Tab: ein neu gestalteter Ausrüstungsreiter; bringt eine eigene Kopie von Nice Bill Tab mit, wenn Nice Bill Tab selbst nicht aktiv ist."),
     pit=("Its bundled Nice Bill Tab has the same bill list cache (see Nice Bill Tab).",
          "Das mitgelieferte Nice Bill Tab hat denselben Auftragslisten-Puffer (siehe Nice Bill Tab)."),
     src=KB + "; LoadFolders.xml (Workshop 3609897594)")
note("BWM", ["falconne.BWM"], ["changes-bills-ui", "changes-bills"],
     ("Better Workbench Management: counting by hit points, quality and material, bill copying and pawn restrictions.",
      "Better Workbench Management: Zählen nach Trefferpunkten, Qualität und Material, Aufträge kopieren und Figuren festlegen."), src=KB + "; About.xml 1.7 (Workshop 3819555998)")
note("DubsMintMenus", ["Dubwise.DubsMintMenus"], ["changes-bills-ui", "changes-architect"],
     ("Dubs Mint Menus: searchable pickers for bills, operations, plants, research and the architect menu.",
      "Dubs Mint Menus: durchsuchbare Auswahlmenüs für Aufträge, Operationen, Pflanzen, Forschung und das Baumenü."), src=KB)
note("NoMaxBills", ["justharry.nomaxbillsredux"], ["changes-bills"],
     ("No Max Bills: Redux: removes the limit of 15 bills per workbench.",
      "No Max Bills: Redux: hebt die Grenze von 15 Aufträgen pro Werkbank auf."), src=KB)
note("CommonSense", ["avilmask.CommonSense"], ["changes-bills", "changes-work"],
     ("Common Sense: pawns clean before working, haul all ingredients at once and eat food that spoils first.",
      "Common Sense: Figuren putzen vor der Arbeit, tragen alle Zutaten auf einmal und essen zuerst, was bald verdirbt."), src=KB)

# ---------------------------------------------------------------- construction and quality
note("ReplaceStuff", ["Memegoddess.ReplaceStuff", "mbesh.ReplaceStuff", "Uuugggg.ReplaceStuff"], ["replaces-construction"],
     ("Replace Stuff: replace a building's material in place, blueprints over rock and fog, auto-bridges.",
      "Replace Stuff: Material eines Gebäudes an Ort und Stelle tauschen, Baupläne über Fels und Nebel, automatische Brücken."),
     label="Replace Stuff", src=KB + "; About.xml (Workshop 3526354009, 3821974777, 1372003680)")
note("AllowTool", ["UnlimitedHugs.AllowTool"], ["changes-architect"],
     ("Allow Tool: more orders to allow, forbid, select similar, finish off and haul urgently.",
      "Allow Tool: mehr Befehle zum Erlauben, Verbieten, Ähnliches-Auswählen, Erlösen und dringenden Tragen."), src=KB)
note("QualityBuilder", ["hatti.qualitybuilder"], ["changes-quality", "replaces-construction"],
     ("QualityBuilder: only your best builders build quality items, and buildings below a minimum quality are rebuilt.",
      "QualityBuilder: nur die besten Baumeister:innen bauen Qualitätsgegenstände, und Gebäude unter einer Mindestqualität werden neu gebaut."),
     pit=("Robots fail its \"only the best builder\" rule, and while they count they raise the bar for colonists.",
          "Roboter scheitern an seiner Regel „nur der beste Baumeister“, und solange sie mitzählen, heben sie die Latte für Kolonist:innen."),
     adv=("With Misc. Robots: QualityBuilder × Misc. Robots lets robots build only what they can reach.",
          "Mit Misc. Robots: QualityBuilder × Misc. Robots lässt Roboter nur das bauen, was sie schaffen."),
     src=KB + "; ModSettingsOverrides feature D retired 02.10.2026; QualityBuilderRobots README")
note("StaticQuality", ["vis.StaticQuality"], ["changes-quality"],
     ("Static Quality: crafting and construction quality follows skill instead of a wide random roll.",
      "Static Quality: Qualität bei Herstellung und Bau folgt der Fertigkeit statt eines breiten Zufallswurfs."), src=KB)
# ---------------------------------------------------------------- storage and hauling
note("DeepStorage", ["LWM.DeepStorage"], ["changes-storage"],
     ("LWM's Deep Storage: shelves, pallets and lockers that hold several stacks per cell.",
      "LWM's Deep Storage: Regale, Paletten und Spinde mit mehreren Stapeln pro Feld."), src=KB)
note("DeepStack", ["void.deepstack"], ["changes-storage"],
     ("Deep Stack: much larger stack limits.",
      "Deep Stack: viel größere Stapelgrenzen."),
     pit=("With very large stacks the colony's resource counter can lag behind: counting stock through it can overproduce.",
          "Bei sehr großen Stapeln hinkt der Ressourcenzähler der Kolonie hinterher: wer darüber den Vorrat zählt, produziert zu viel."),
     adv=("JB mods count items directly (including carried stacks).", "JB-Mods zählen Gegenstände direkt (auch getragene Stapel)."),
     src=KB + "; DNDM Creator counting (22_RimWorld/CLAUDE.md)")
note("AdaptiveStorage", ["adaptive.storage.framework"], ["changes-storage", "library"],
     ("Adaptive Storage Framework: storage buildings with content-dependent graphics and capacities.",
      "Adaptive Storage Framework: Lagergebäude mit inhaltsabhängiger Grafik und Kapazität."), src=KB)
note("PickUpAndHaul", ["Zzyxzz.PickUpAndHaulReworked", "Mehni.PickUpAndHaul"], ["changes-storage", "changes-work"],
     ("Pick Up And Haul: pawns gather several items into their inventory before walking to the stockpile.",
      "Pick Up And Haul: Figuren sammeln mehrere Gegenstände ins Inventar, bevor sie zum Lager laufen."),
     label="Pick Up And Haul", src=KB + "; About.xml (Workshop 3829549663, 1279012058)")

# ---------------------------------------------------------------- guests (Hospitality family, JB)
REPL = ("Hearth & Harbor is built as the successor of this mod and expects it to be switched off; run one of the two.",
        "Hearth & Harbor ist als Nachfolger dieser Mod gebaut und erwartet, dass sie aus ist; nur eine von beiden verwenden.")
REPL_ADV = ("Hearth & Harbor can take over a save that used it.", "Hearth & Harbor kann einen Spielstand übernehmen, der sie benutzt hat.")
HSRC = "HearthHarbor Source/Hearth/Compat/Hospitality/HospitalityFamily.cs (role Replaced)"
note("Hospitality", ["Orion.Hospitality"], ["guests"],
     ("Hospitality: visiting groups stay as hotel guests, rent beds, shop and eat, and can be befriended or recruited.",
      "Hospitality: Besuchergruppen bleiben als Hotelgäste, mieten Betten, kaufen ein, essen und lassen sich anfreunden oder anwerben."),
     pit=REPL, adv=REPL_ADV, src=KB + "; " + HSRC)
note("HospitalityCasino", ["Adamas.HospitalityCasino"], ["guests", "economy"],
     ("Hospitality: Casino: gambling buildings for guests and colonists.", "Hospitality: Casino: Glücksspielgebäude für Gäste und Kolonist:innen."),
     pit=REPL, src=KB + "; " + HSRC)
note("HospitalityStorefront", ["Adamas.Storefront"], ["guests", "economy"],
     ("Hospitality: Storefront: a shop register and a selling job for social colonists.", "Hospitality: Storefront: eine Ladenkasse und eine Verkaufsarbeit für gesellige Kolonist:innen."),
     pit=REPL, src=KB + "; " + HSRC)
note("HospitalitySpa", ["Adamas.HospitalitySpa"], ["guests", "economy"],
     ("Hospitality: Spa: guests pay to use Dubs Bad Hygiene pools, hot tubs and saunas.", "Hospitality: Spa: Gäste zahlen für Becken, Whirlpools und Saunen aus Dubs Bad Hygiene."),
     pit=REPL, src=KB + "; " + HSRC)
note("HospitalityVending", ["Adamas.VendingMachines"], ["guests", "economy"],
     ("Hospitality: Vending machines: powered machines where guests buy goods and meals themselves.", "Hospitality: Vending machines: Automaten, an denen Gäste selbst Waren und Essen kaufen."),
     pit=REPL, src=KB + "; " + HSRC)
note("HearthHarbor", ["juliusberger00.hearthharbor"], ["guests", "economy", "jb-mod"],
     ("Hearth & Harbor: guests, hotels and hospitality with shops, casino, wellness and the Silverline bank.",
      "Hearth & Harbor: Gäste, Hotels und Gastfreundschaft mit Läden, Casino, Wellness und der Silverline-Bank."),
     adv=("A JB mod with its own core (Hearth.Core); JB Framework mods detect it through the framework's Hearth adapter.",
          "Eine JB-Mod mit eigenem Kern (Hearth.Core); JB-Framework-Mods erkennen sie über den Hearth-Adapter des Frameworks."),
     src="About.xml (Workshop 3844621374)")
note("Silverline", ["juliusberger00.silverline"], ["economy", "jb-mod"],
     ("Silverline: banking, loans and insurance, standalone or inside Hearth & Harbor.",
      "Silverline: Bank, Kredite und Versicherungen, eigenständig oder in Hearth & Harbor."),
     src="_Release/Packages_v110/Silverline/About/About.xml")

# ---------------------------------------------------------------- robots
note("MiscCore", ["Haplo.Miscellaneous.Core"], ["library"],
     ("Miscellaneous 'CORE': shared code of Haplo's Misc. mods.", "Miscellaneous 'CORE': gemeinsamer Code von Haplos Misc.-Mods."),
     src="About.xml (Workshop 704181221)")
note("MiscRobots", ["Haplo.Miscellaneous.Robots"], ["robots"],
     ("Misc. Robots: hauling and cleaning robots bought from traders.", "Misc. Robots: Trage- und Putzroboter, bei Händlern zu kaufen."),
     pit=("On RimWorld 1.7 the base mod has only haulers and cleaners; builder robots come from Misc. Robots++.",
          "Auf RimWorld 1.7 hat die Grundmod nur Träger und Putzer; Bauroboter kommen aus Misc. Robots++."),
     src=KB + "; QualityBuilderRobots 1.1.0 test 22.11.2026 (22_RimWorld/CLAUDE.md)")
note("MiscRobotsPP", ["Alaestor.MiscRobots.PlusPlus"], ["robots"],
     ("Misc. Robots++: kitchen, crafter, builder, ER and omni robots in five tiers (skill 4 to 20).",
      "Misc. Robots++: Küchen-, Handwerks-, Bau-, Notfall- und Allzweckroboter in fünf Stufen (Fertigkeit 4 bis 20)."),
     pit=("Vanilla rolls mechanoid work with a fixed skill of 10; robots do not use their own skill there.",
          "Vanilla würfelt Arbeit von Mechanoiden mit fester Fertigkeit 10; Roboter nutzen dort nicht ihre eigene Fertigkeit."),
     adv=("QualityBuilder × Misc. Robots makes robots roll with their own skill.", "QualityBuilder × Misc. Robots lässt Roboter mit ihrer eigenen Fertigkeit würfeln."),
     src=KB + "; QualityBuilderRobots README")
note("QBRobots", ["juliusberger00.qualitybuilderrobots"], ["robots", "changes-quality", "jb-mod"],
     ("QualityBuilder × Misc. Robots: robots may build QualityBuilder frames when their roll can reach the minimum quality.",
      "QualityBuilder × Misc. Robots: Roboter dürfen QualityBuilder-Rahmen bauen, wenn ihr Wurf die Mindestqualität erreichen kann."),
     src="QualityBuilderRobots/About/About.xml")

# ---------------------------------------------------------------- editors, performance, multiplayer, AI
note("CharacterEditor", ["void.charactereditor"], ["editor"],
     ("Character Editor: edit pawns, appearance, gear, health, skills and genes in game or at the start.",
      "Character Editor: Figuren, Aussehen, Ausrüstung, Gesundheit, Fertigkeiten und Gene im Spiel oder beim Start bearbeiten."),
     pit=("Xenotypes you create with it can show up in other factions.", "Damit erstellte Xenotypen können bei anderen Fraktionen auftauchen."),
     adv=("Mod Settings Overrides keeps your own xenotypes away from other factions.", "Mod Settings Overrides hält eigene Xenotypen von anderen Fraktionen fern."),
     src=KB + "; ModSettingsOverrides feature C (22_RimWorld/CLAUDE.md)")
note("PawnCompatibilityEditor", ["jomar.pawncompatibilityeditor"], ["editor"],
     ("Pawn Compatibility Editor: view and override the compatibility between pawns.",
      "Pawn Compatibility Editor: Kompatibilität zwischen Figuren ansehen und überschreiben."), src=KB)
note("RocketMan", ["Krkr.RocketMan"], ["performance"],
     ("RocketMan: throttles pawn ticks and caches stats for speed.", "RocketMan: drosselt Figuren-Ticks und puffert Werte für mehr Tempo."),
     pit=("Its About.xml lists RimWorld up to 1.5 only.", "Seine About.xml nennt RimWorld nur bis 1.5."), src=KB)
note("PerformanceOptimizer", ["Taranchuk.PerformanceOptimizer"], ["performance"],
     ("Performance Optimizer: optimises vanilla routines, caches UI and throttles background checks.",
      "Performance Optimizer: optimiert Vanilla-Abläufe, puffert Oberflächen und drosselt Hintergrundprüfungen."), src=KB)
note("DubsPerformanceAnalyzer", ["Dubwise.DubsPerformanceAnalyzer.steam"], ["performance"],
     ("Dubs Performance Analyzer: profiles where tick and frame time goes, per mod.",
      "Dubs Performance Analyzer: misst, wohin Tick- und Bildzeit geht, je Mod."),
     adv=("The tool to use when a JB mod seems slow; the numbers help a bug report.",
          "Das Werkzeug der Wahl, wenn eine JB-Mod langsam wirkt; die Zahlen helfen bei einer Fehlermeldung."), src=KB)
note("Multiplayer", ["rwmt.Multiplayer"], ["multiplayer"],
     ("Multiplayer: co-op over LAN or the internet on one colony.", "Multiplayer: Koop über LAN oder Internet in einer Kolonie."),
     pit=("Every mod must make the same random choices on every machine; JB mods are not tested in Multiplayer.",
          "Jede Mod muss auf jedem Rechner dieselben Zufallsentscheidungen treffen; JB-Mods sind nicht im Multiplayer getestet."),
     adv=("JB Framework's random streams are deterministic per save, a good base; still treat JB mods in Multiplayer as untested.",
          "Die Zufallsströme von JB Framework sind je Spielstand deterministisch, eine gute Grundlage; JB-Mods im Multiplayer trotzdem als ungetestet behandeln."),
     src=KB)
note("MultiplayerCompat", ["rwmt.MultiplayerCompatibility"], ["multiplayer"],
     ("Multiplayer Compatibility: patches that make other mods work with Multiplayer.", "Multiplayer Compatibility: Patches, die andere Mods Multiplayer-tauglich machen."),
     src="About.xml (Workshop 1629973374)")
note("RimTalk", ["cj.rimtalk"], ["ai"],
     ("RimTalk: AI-written dialogue for social interactions.", "RimTalk: KI-geschriebene Dialoge für soziale Interaktionen."), src=KB)

# ---------------------------------------------------------------- work and pawn UI
note("WorkTab", ["mbesh.WorkTab", "Fluffy.WorkTab"], ["changes-work"],
     ("Work Tab: sub-tasks per work type, priorities by time of day, priorities up to 9.",
      "Work Tab: Unteraufgaben je Arbeitsart, Prioritäten nach Tageszeit, Prioritäten bis 9."),
     label="Work Tab", src=KB + "; About.xml (Workshop 3823275757, 725219116)")
note("RimHUD", ["Jaxe.RimHUD"], ["changes-pawn-ui"],
     ("RimHUD: a configurable HUD instead of the inspect card.", "RimHUD: ein einstellbares HUD statt der Infokarte."), src=KB)