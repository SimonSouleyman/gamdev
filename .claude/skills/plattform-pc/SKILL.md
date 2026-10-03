---
name: plattform-pc
description: >-
  Alles, was ein PC-Spiel (Windows zuerst, auch Linux/macOS) besonders macht – Desktop-Build erstellen und
  starten, Protokoll und Bildrate aufzeichnen, Fenster/Vollbild und Auflösungen, Tastatur/Maus/Gamepad mit
  Umbelegung, Einstellungsmenü, Speicherort der Spielstände, Leistung auf schwacher und starker Hardware,
  Weitergabe als Ordner/Installer. Verwenden bei "PC-Spiel", "nur für PC", "Desktop-Build", "exe", "Steam",
  "Vollbild", "Gamepad", "Tastenbelegung", "Grafikeinstellungen", "läuft auf meinem Laptop schlecht". Noch ohne
  eigenes Projekt erprobt – nach dem ersten PC-Spiel mit den echten Erfahrungen nachschärfen.
---

# Plattform: PC

Auf dem PC ist die Hardware nicht ein Gerät, sondern eine Spanne – vom Büro-Laptop mit integrierter Grafik bis
zum Spielerechner mit breitem Bildschirm. Engine-Befehle für den Desktop-Build stehen in der
Projekt-`CLAUDE.md`.

## Bauen und starten
- Build in einen Ordner außerhalb des Repos; Spielversion und Entwickler-Version (mit Protokoll und
  Messausgaben).
- Der Entwicklungsrechner ist zugleich Testgerät: eigene Tests im Build, nicht nur im Editor (Start, Fenster,
  Eingabe, Speichern verhalten sich im Build anders).
- Protokolldatei des Builds kennen (wo die Engine sie ablegt) und für Auswertungen nutzen.

## Testen und aufzeichnen
- Wenn Simon selbst spielt: Protokolldatei mitlesen (Bildrate alle ~10 s, Fehler); Screenshots nur mit seinem
  Okay, denn der Bildschirm zeigt auch anderes.
- Eigene Tests: Bot-Szenarien im Editor oder Headless; Screenshots aus dem Spiel heraus statt vom Bildschirm.
- Der Editor läuft manchmal nur im Vordergrund weiter – vor automatischen Läufen prüfen, dass das Spiel
  wirklich tickt (Bildzähler steigt).

## Zielwerte, die auf dem PC zählen
- Bildrate auf **schwacher** Hardware (integrierte Grafik, Akkubetrieb) und Verhalten bei hoher
  Bildwiederholrate (120/144 Hz): Logik darf nicht an die Bildrate gekoppelt sein.
- Start bis spielbar; Ladezeiten.
- Auflösungen und Seitenverhältnisse: 16:9, 16:10, Ultrawide, kleine Fenster; UI skaliert und bleibt lesbar.
- Fenster ↔ Vollbild, Fokusverlust (Alt-Tab), zweiter Bildschirm: Spiel pausiert oder läuft sinnvoll weiter,
  Maus bleibt nicht gefangen.

## Eigenheiten
- **Eingabe:** Tastatur und Maus zuerst; Gamepad, wenn das Spiel sich dafür eignet. Jede Aktion umbelegbar oder
  wenigstens mit einer sinnvollen Zweitbelegung; Anzeige der Tasten in Hinweisen passt zum zuletzt benutzten
  Gerät. Verschiedene Tastaturlayouts bedenken (QWERTZ/QWERTY: physische Tastenposition statt Buchstabe).
- **Einstellungen:** Auflösung/Fenstermodus, Bildratenbegrenzung/VSync, Qualitätsstufe, Lautstärken, Sprache.
  Früh anlegen, auch wenn zunächst wenig darin steht – ein PC-Spiel ohne Einstellungen wirkt unfertig.
- **Spielstände:** im Nutzerdaten-Ordner des Betriebssystems, nie neben der Programmdatei; Pfad in der
  Projekt-Doku notieren; Laden alter Versionen testen.
- **Leistung:** dieselbe Reihenfolge wie auf dem Handy (Engpass messen, Verursacher finden, dann optimieren),
  aber mit Qualitätsstufen statt einer festen Einstellung. Auf dem starken Entwicklungsrechner künstlich
  drosseln oder mit integrierter Grafik gegenprüfen.
- **Weitergabe:** zuerst als gezippter Ordner; Installer, Signierung und Shop-Anbindung (Steam, itch.io) erst,
  wenn Simon veröffentlichen will – das ist eine eigene Entscheidung und nie ein Nebenprodukt.

## Andere Betriebssysteme
Linux- und macOS-Builds nur, wenn sie Ziel sind und getestet werden können; ein ungetesteter Build ist kein
Ergebnis. macOS braucht für Signierung einen Mac.
