---
name: spielprojekt-start
description: >-
  Ein neues Spielprojekt im gamdev-Repo anlegen – Ordner, Projektregeln (CLAUDE.md), README, Design- und
  Änderungsdoku, Testläufer, Bot-/Screenshot-Werkzeuge, Versionsschema, Eintrag in der Repo-README – unabhängig
  von Engine und Zielplattform. Verwenden bei "neues Spiel", "neues Projekt", "ich will ein Spiel für
  Browser/PC/Handy bauen", "leg ein Projekt an", "fang mit … an", "Prototyp für …", auch wenn Simon nur eine
  Spielidee beschreibt und noch keine Engine nennt.
---

# Neues Spielprojekt anlegen

Bei Drift und Tree sind die Arbeitsgrundlagen (Regeln, Doku je Version, Testläufer, Bot-Szenarien, Versionen)
erst nach und nach entstanden. Dieser Skill legt sie am ersten Tag an, damit die späteren Runden
(`feedback-runde`, `testlauf`, `version-speichern`) sofort greifen.

## 1. Klären (wenige Fragen, mit Empfehlung)
- **Idee in zwei Sätzen** und das Gefühl, das das Spiel haben soll (gemütlich, schnell, knifflig …).
- **Zielplattform(en):** Handy, Browser, PC – das bestimmt Engine, Eingabe, Budgets (Skills `plattform-*`).
- **Engine:** vorschlagen, was zu Plattform und Idee passt, und begründen; Simon entscheidet. Nur Engines
  vorschlagen, die auf dem Rechner installiert sind oder deren Installation er will.
- **UI-Sprache** und Projektname (Ordnername, kurz, ohne Leerzeichen).
- **Erster spielbarer Schritt:** was muss der Prototyp können, damit Simon etwas fühlen kann?

## 2. Anlegen (direkt im Repo: `gamdev/<Projekt>/`)
- Engine-Projekt im Ordner; `.gitignore` und, wenn nötig, `.gitattributes` (LFS für Binärdateien) der Engine.
- `CLAUDE.md` aus `assets/CLAUDE-vorlage.md`: Engine + Version, harte Regeln, Ordneraufbau, wie man kompiliert,
  testet, das Spiel automatisch laufen lässt und baut, Stolperfallen (anfangs leer, wächst mit).
- `README.md`: was das Spiel ist, Technik, Aufbau, Bauen/Installieren, Tests, Versionen.
- `docs/` (oder `Docs/`): Design-Dokument (Idee, Loop, Entscheidungen mit Datum), Architektur,
  Änderungsnotizen je Version, Tuning-Werte, Zielwerte für Testläufe.
- **Testläufer**, der ohne Handarbeit läuft (Kommandozeile/Headless) und mit dem Projekt wächst.
- **Automatisches Spielen:** ein kleiner Bot bzw. Autoplay mit festem Seed, der das echte Spiel bedient,
  Kennzahlen als Datei schreibt und Screenshots macht. Früh anlegen – es ist die Grundlage für jede spätere
  Messung („alle 10 s etwas Sehenswertes“, „Anfänger überlebt 45 s“).
- **Messausgaben** in Entwickler-Builds (Bildrate, Auflösungsstufe, Fehler) von Anfang an.
- Eintrag in der Projektliste der Repo-`README.md`.

## 3. Arbeitsweise festlegen
- Versionsschema `<projekt>-vX.Y(.Z)` und was eine neue zweite Stelle rechtfertigt.
- Wo Builds landen (außerhalb des Repos) und wie sie aufs Testgerät kommen.
- Was Simon selbst prüft (Gefühl, Aussehen) und wie er es zu sehen bekommt (Screenshots, Build).
- Sitzungen für dieses Projekt im Repo starten (`gamdev/` oder `gamdev/<Projekt>/`), damit Regeln und Skills
  geladen werden.

## 4. Erster Schritt
Den ersten spielbaren Schritt bauen, auf der Zielplattform zeigen, dann in den normalen Rhythmus wechseln:
Simon spielt → `feedback-runde` → „speichere als v0.1“.

## Nicht übertreiben
Ein Prototyp braucht am ersten Tag kein Speichersystem, keine Menüs und keine Optimierung. Anlegen, was die
*Zusammenarbeit* trägt (Regeln, Doku, Tests, Bot, Messung); Spielinhalte kommen über die Runden.
