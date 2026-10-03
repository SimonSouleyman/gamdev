# <Projekt> (<Engine + genaue Version>) – Regeln für dieses Projekt

<Zwei Sätze: was das Spiel ist und wie es sich anfühlen soll.>
Design: `docs/design.md` (Quelle der Wahrheit für Spielentscheidungen). Architektur: `docs/architecture.md`.
Gemeinsame Regeln für alle Projekte: `../CLAUDE.md`.

## Harte Regeln
- Engine <Version>, Renderer/Pipeline <…>, Sprache <…>. Zielplattform: <Handy / Browser / PC>, Format <z. B. Hochformat 1080×1920>.
- UI-Sprache: <…>.
- <Projekt-Grundsätze, z. B. „Simulation getrennt von Darstellung“, „aller Zufall aus dem Seed“, „keine Google-Dienste“.>
- Budgets (Bildrate, Speicher, Ladegröße): <Werte> – im Code durchgesetzt, nicht nur notiert.

## Aufbau
- `<ordner>/` – <was dort liegt>
- `tests/` – <wie die Tests heißen und was sie abdecken>
- `tools/` – <Bot/Autoplay, Screenshot-Werkzeuge, Messsonden>
- `docs/` – Design, Architektur, Änderungsnotizen je Version, Tuning, Zielwerte

## Befehle
- Kompilieren/Prüfen: `<Befehl>`
- Tests: `<Befehl>` (Exit-Code 0 = bestanden)
- Spiel automatisch laufen lassen / Bot-Szenarien: `<Befehl>` → schreibt `<Datei>`
- Screenshot mit UI: `<Befehl>`
- Build Spielversion: `<Befehl>`; Entwickler-Build mit Messausgaben: `<Befehl>`
- Versionsnummer setzen: `<Weg über die Engine>`
- Aufs Testgerät / bereitstellen: `<Befehl oder Ort>`

## Arbeitsweise
- Versionen: Tags `<projekt>-vX.Y(.Z)`; <Regel für zweite/dritte Stelle>. Änderungsnotiz je Version in `docs/`.
- Vor jedem Schritt Akzeptanzkriterien notieren; für jedes einen Test.
- Aussehen und Gefühl beurteilt Simon: Screenshot bzw. Build zeigen.

## Stolperfallen (wächst mit dem Projekt)
- <Datum>: <was schiefging> → <was man stattdessen tut>.

## Stil
- <Benennung, Typisierung, Kommentare nur für nicht Offensichtliches>.
