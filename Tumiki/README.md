# Tumiki Fighters (Godot, Hochformat)

Nachbau von **TUMIKI Fighters** (Kenta Cho / ABA Games, 2004) in Godot 4.3 für Android im Hochformat.
Das Schiff startet unten und fliegt nach oben. Spiellogik, Gegner, Stages und Schussmuster (BulletML)
sind 1:1 aus dem Original portiert; nur die Kamera ist um 90 Grad gedreht.

## Steuerung

- **Finger ziehen** (irgendwo auf dem Bildschirm): Schiff bewegen. Geschossen wird automatisch.
- **SLOW** (unten rechts, halten): langsamer, Richtung bleibt fest, angeklebte Teile werden eingezogen
  (geschützt, aber nur 1/5 Bonuspunkte).
- **Pause** unten links. Am PC: Pfeiltasten/WASD, X oder Shift = Slow, P = Pause.

## Projekt

- `src/` GDScript-Code (Port der D-Quellen aus `M-HT/tumiki_fighters`)
- `data/` Original-Spieldaten (Stages, Gegner, Tumiki-Modelle, BulletML-Muster)
- `sounds/` Original-Musik und Soundeffekte

Öffnen mit Godot 4.7 (`project.godot`). Android-Export über *Projekt > Exportieren > Android*.

Automatischer Test ohne Fenster: `godot --headless --path . --fixed-fps 60 --quit-after 6000 -- --autoplay` (mit `--invincible` spielt der Bot alle Stages durch, mit `--shots=<ordner>` und Fenster macht er Screenshots). Nach frischem Checkout einmal `godot --headless --path . --import` ausführen.

## Lizenz

Original: Copyright 2004 Kenta Cho, BSD 2-Clause License (siehe `LICENSE_original.txt`).
