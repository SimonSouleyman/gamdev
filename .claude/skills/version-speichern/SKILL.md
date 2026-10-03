---
name: version-speichern
description: >-
  Einen Stand eines Spielprojekts als Version festhalten – Versionsnummer, Builds, Änderungsnotiz, Commit, Tag,
  README-Zeile, optional Push nach GitHub, Build aufs Testgerät. Verwenden bei "speichere als vX", "als 0.6.8
  speichern", "save as vX", "mach eine Version", "und auf GitHub", "push das", auch wenn nur die Versionsnummer
  genannt wird ("dann 0.7"). Nicht von selbst auslösen: gespeichert wird nur, wenn Simon es sagt.
---

# Version speichern

„Speichere als vX“ heißt bei Simon immer das ganze Paket, nicht nur ein Commit. Der Push nach GitHub gehört nur
dazu, wenn er „und auf GitHub“ (oder Ähnliches) sagt. Die Reihenfolge unten vermeidet die Fehler, die schon
einmal passiert sind (falsche Versionsnummer im Build, Build-Müll im Commit, überschriebene fremde Commits).

## Vorher
- Tests grün? Wenn nicht: erst melden, nicht speichern.
- Das Spiel läuft gerade nicht im Editor; Szene/Projekt ist im gewünschten Zustand (bei Projekten mit Modi:
  im Standardmodus, nicht mitten in einem Testlauf).
- Welche Nummer? Simons Angabe wörtlich nehmen. Ohne Angabe: Fixes heben die letzte Stelle, neue Funktionen die
  zweite – oder die Regel aus der Projekt-`CLAUDE.md`.

## Ablauf
1. **Versionsnummer im Projekt setzen** – über den Weg, den die Engine vorsieht (steht in der Projekt-`CLAUDE.md`).
   Nie nur eine Einstellungsdatei von Hand ändern, wenn der Editor läuft: er überschreibt sie beim Build.
2. **Builds** für die Zielplattform(en): Spielversion und, wo sinnvoll, Entwickler-Version mit Messausgaben.
   Während eines Builds keine Skripte ändern (auch keine Agents).
3. **Doku:** Änderungsnotiz der Version (was, warum, Messwerte), Architektur-/Design-Doku nachziehen,
   Projekt-README, falls sich Bedienung oder Aufbau geändert haben.
4. **Aufräumen:** Build-Nebenprodukte, Temp- und Messdateien aus dem Arbeitsverzeichnis; nichts Privates
   (Screenshots mit Benachrichtigungen, Zugangsdaten).
5. **Commit** mit einer Nachricht, die die Version in einem Satz beschreibt, plus der Attributionszeile der
   Sitzung. **Tag** `<projekt>-vX`.
6. **Repo-README:** die Zeile des Projekts auf den neuen Tag bringen.
7. **Nur bei „und auf GitHub“:** `git pull` (andere Projekte im Repo ändern sich parallel – mergen, nie
   überschreiben), dann `main` und den Tag pushen. Bei großen Binärdateien prüfen, dass LFS mitkommt.
8. **Ablage und Testgerät:** Builds an den vereinbarten Ort (außerhalb des Repos), Installer/Startdatei
   aktualisieren, aktuelle Version aufs Testgerät bzw. bereitstellen.
9. **Gedächtnis:** Stand der Version (Nummer, Commit, was drin ist, was offen ist) festhalten.

Sonderfall Drift (Quell-Repo liegt außerhalb, wird per Subtree übernommen): `references/drift-subtree.md`.

## Bestätigung an Simon
Zwei, drei Zeilen: Version, Commit/Tag, wo die Builds liegen, was auf dem Testgerät ist, ob gepusht wurde. Falls
beim Pull fremde Commits gemergt wurden, das erwähnen.

## Wenn etwas schiefgeht
- Build schlägt fehl oder hängt: Ursache nennen (offener Dialog, geänderte Skripte während des Builds), beheben,
  neu bauen – nicht mit einem alten Build weitermachen.
- Push abgelehnt: pull + merge, nie `--force`.
- Tag existiert schon: nachfragen, nicht verschieben (Ausnahme: Simon sagt es ausdrücklich).
