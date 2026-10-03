---
name: testlauf
description: >-
  Einen zeitlich begrenzten, gründlichen Test der aktuellen Spielversion fahren – Zielwerte mit bestanden/nicht
  bestanden, Messung auf der Zielplattform, Bot-Szenarien, Lese-Agents für Performance und Bugs, blinder
  Gutachter, kleine Fixes, Bericht mit Vorschlagsliste. Verwenden bei "teste und bewerte die aktuelle Version",
  "ausführliche Tests", "Zeitrahmen: bis zu …", "Zielwerte", "QA-Runde", "bugs suchen und laufzeit optimieren",
  "wie gut ist der Stand?" – auch wenn Simon nur einen Teil davon verlangt (dann die passenden Abschnitte
  nehmen).
---

# Testlauf mit Zielwerten

Ein Testlauf beantwortet „wie gut ist diese Version wirklich?“ mit Zahlen statt Eindrücken. Simon gibt meist
einen Zeitrahmen und eine Zielwerte-Liste vor; fehlt die Liste, aus `assets/zielwerte-vorlage.md` eine für das
Projekt ableiten und kurz bestätigen lassen.

## Grundsätze
- **Zeitrahmen halten.** Zu Beginn einen Plan mit Uhrzeiten machen; wenn es knapp wird, in dieser Reihenfolge
  kürzen: blinder Gutachter → Tempo ganzer Durchläufe → Fixes. Nie kürzen: Messung auf der Zielplattform,
  Fehlersuche.
- **Erst messen, dann anfassen.** Solange Szenarien oder Messungen laufen, keine Skripte ändern – das lädt den
  laufenden Stand neu und macht die Messung wertlos.
- **Nur Kleines direkt beheben.** Bugfixes und unsichtbare Optimierungen sofort; alles, was Gameplay oder
  Balance verändert, kommt auf die Vorschlagsliste.
- **Fortschritt melden**, alle paar Minuten eine Zeile.

## Ablauf
1. **Aufbau (parallel starten):**
   - Zielplattform: Entwickler-Build installieren, Protokoll im Hintergrund mitschreiben (Skill der Plattform).
   - Drei Agents: (a) lesend Performance-Bremsen im Code, (b) lesend Bugs in den neuesten Systemen,
     (c) blinder Gutachter – bekommt nur Screenshots, keinen Code, urteilt wie ein Erstspieler. (c) erst
     starten, wenn Screenshots aus beiden Umgebungen da sind.
2. **Zielplattform:** Start bis spielbar, alle Modi anspielen, Bildrate/Frametimes, Hintergrund und zurück,
   Bildschirm aus/an, dann ein Dauerlauf (≈ 10 min) nebenher: Bildrate über die Zeit, Speicher, Temperatur.
3. **Editor/Automatik:** die Bot-Szenarien des Projekts (menschenähnlicher Bot: Reaktionszeit, Rauschen,
   echter Eingabeweg), stumm, in der Auflösung und Pipeline der Zielplattform. Tests nur in kleinen Blöcken mit
   Zeitlimit.
4. **Erster Start:** Tutorial-/Erststart-Zustand vorübergehend zurücksetzen und beide Wege durchspielen.
5. **Fixes:** kleine Dinge beheben, betroffene Tests und Szenarien erneut laufen lassen, Änderungsnotiz.
6. **Wenn etwas geändert wurde:** neue Fix-Version bauen und kurz auf der Zielplattform prüfen (alter
   Spielstand lädt). Gespeichert wird sie so, wie Simon es im Auftrag gesagt hat – sonst nachfragen.
7. **Aufräumen:** Qualitätseinstellungen, Fenster-/Ansichtsgrößen, Erststart-Flags, Messdateien.

## Bericht
Vorlage: `assets/bericht-vorlage.md`. Sechs Teile, kurz, Deutsch:
1. Zielwerte-Tabelle: bestanden / nicht bestanden / nicht geschafft, jeweils mit Messwert.
2. Bugs: behoben / offen, mit Schritten zum Nachstellen.
3. Was geändert wurde und ob eine neue Version entstand.
4. Ehrliche Bewertung je Modus, dazu das Urteil des blinden Gutachters (mit Punkten).
5. Vorschlagsliste für Größeres, sortiert nach Wirkung/Aufwand – Simon wählt aus.
6. 4–6 Screenshots (Zielplattform und Editor, alle Modi) als Kontaktbogen.

## Ehrlich bleiben
- „Nicht prüfbar“ ist ein gültiges Ergebnis (z. B. Neigungssteuerung über USB) – sagen, was stattdessen
  geprüft wurde und was Simon selbst testen muss.
- Messwerte aus dem Editor sind nicht die der Zielplattform; kennzeichnen, woher eine Zahl stammt.
- Ein nicht bestandener Zielwert, den Simon gar nicht will (er fand das Abenteuer zu leicht, nicht zu schwer),
  ist kein Auftrag: als Befund melden, Entscheidung ihm lassen.
