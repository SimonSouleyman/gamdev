# Auftrag für einen parallelen Agent – Vorlage

Du bist einer von <n> parallelen Agents am Projekt <Pfad> (<Engine/Version>, <Plattform>, UI-Sprache <…>).
Lies zuerst <Projekt>/CLAUDE.md (Abschnitte <…>) und <die betroffenen Dateien/Doku>.

REGELN (streng):
- Dir gehören NUR: <Dateiliste, auch neue Dateien>. Nichts anderes ändern. Braucht eine fremde Datei eine
  Änderung, schreibe sie unter „Integrationswünsche“ in den Bericht.
- Kein Play-Modus, keine Szene speichern, kein Commit, kein Build.
- Prüfen nur über <Kompilier-Befehl>, <Test-Befehl für deine Testklasse> und kurze Auswertungen im Editor.
- Der Editor wird geteilt: erst lesen und schreiben, dann kompilieren. Schlägt das Kompilieren wegen einer
  fremden Datei fehl, <n> s warten und wiederholen (höchstens <m>-mal). Keine Hintergrundprozesse, nur Schleifen
  mit Obergrenze.
- Stil wie der umgebende Code; Kommentare nur für nicht offensichtliche Zwänge. UI-Texte in <Sprache>.

SCHNITTSTELLEN, die der Koordinator für dich angelegt hat:
- <Hook/Methode/Typ – was sie bedeutet, wann sie aufgerufen wird>

AUFGABE (<Simons Wunsch wörtlich>; Entscheidungen: <…>):
1. <konkreter Schritt mit Zielwerten>
2. …
n. Tests in <Testdatei>: <was abzudecken ist>.
n+1. Messen: <Aufbau, vorher/nachher, was im Bericht stehen soll>.

BERICHT (Deutsch): geänderte Dateien, Verhalten und Werte, Messungen, Testergebnisse,
„Szenen-Verdrahtung nötig“ (neue Felder mit Werten), „Integrationswünsche“.
