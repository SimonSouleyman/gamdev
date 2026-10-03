---
name: plattform-browser
description: >-
  Alles, was ein Browser-Spiel (WebGL/WebGPU/HTML5, Desktop- und Handy-Browser) besonders macht – Web-Build
  erstellen und lokal ausliefern, im Browser testen und aufzeichnen (Konsole, Netzwerk, Bildrate), Ladegröße und
  Startzeit, Audio erst nach Nutzergeste, Speichern im Browser, Tab-Wechsel, Eingabe mit Maus/Tastatur/Touch,
  Veröffentlichen als statische Seite. Verwenden bei "Browser-Spiel", "im Browser", "Web-Build", "WebGL",
  "HTML5", "itch.io", "lädt zu lange", "läuft nicht in Firefox/Safari", "auf einer Webseite spielen". Noch ohne
  eigenes Projekt erprobt – nach dem ersten Browser-Spiel mit den echten Erfahrungen nachschärfen.
---

# Plattform: Browser

Im Browser entscheidet die erste halbe Minute: Wie groß ist der Download, wie schnell ist das Spiel bedienbar,
läuft es in jedem Browser? Engine-Befehle für den Web-Export stehen in der Projekt-`CLAUDE.md`.

## Bauen und ausliefern
- Web-Export der Engine in einen Ordner außerhalb des Repos.
- Immer über einen lokalen Webserver testen, nicht per Doppelklick auf die HTML-Datei (Browser blockieren
  sonst Laden und Speicher). Wenn die Engine komprimierte Dateien erzeugt, muss der Server die passenden
  Kopfzeilen liefern – sonst startet der Build nicht.
- Manche Engines brauchen für Threads besondere Server-Kopfzeilen (Cross-Origin-Isolation). Vor der Wahl von
  Thread-Funktionen klären, ob der spätere Host sie setzen kann; sonst ohne Threads bauen.

## Testen und aufzeichnen
- Im eingebauten Browser der Desktop-App oder in Chrome öffnen; Konsole (Fehler, Warnungen) und
  Netzwerk-Liste (Größe und Zahl der Dateien, fehlgeschlagene Anfragen) auslesen; Screenshots machen.
- Mindestens ein Chromium-Browser und Firefox; Safari/iOS, wenn Handy-Browser Ziel sind (dort sind Speicher und
  Grafikfunktionen am knappsten).
- Für Simons eigene Sitzungen: eine kleine Anzeige oder Protokollzeile im Spiel (Bildrate, langsame Bilder,
  Speicher), die sich in der Konsole mitlesen lässt.

## Zielwerte, die im Browser zählen
- **Download bis spielbar:** Gesamtgröße und Zeit bis zur ersten Interaktion (auch bei langsamer Leitung
  gedacht). Früh ein Budget festlegen (z. B. wenige MB für ein kleines Spiel) und bei jeder Version notieren.
- **Startzeit** nach dem Download (Shader-Übersetzung, Entpacken).
- **Bildrate** im Fenster und im Vollbild; Größenänderung des Fensters ohne Verzerrung.
- **Speicher:** Browser-Tabs haben harte Grenzen, auf Handys sehr niedrige.

## Eigenheiten
- **Audio** startet erst nach einer Nutzeraktion: erster Klick/Tipp „entsperrt“ den Ton; das Spiel darf vorher
  nicht hängen.
- **Speichern:** Browser-Speicher (lokal/IndexedDB), nie Dateien; kann vom Nutzer gelöscht werden und ist im
  privaten Modus flüchtig – Spielstand klein halten, Laden ohne Spielstand immer möglich.
- **Tab-Wechsel:** im Hintergrund läuft die Schleife gedrosselt oder gar nicht; Zeit beim Zurückkommen
  nachholen statt mit riesigen Zeitschritten zu simulieren.
- **Eingabe:** Maus und Tastatur zuerst; Touch mitdenken, wenn Handy-Browser gemeint sind (dann gelten die
  Touch-Regeln aus `plattform-mobile`). Tasten, die der Browser selbst nutzt (Leertaste scrollt, F-Tasten,
  Strg-Kürzel), abfangen oder meiden. Rechtsklick-Menü und Zeigersperre bewusst behandeln.
- **Grafik:** keine Annahmen über Grafikfunktionen; Rückfall für fehlende Erweiterungen. Rechenintensive Shader
  treffen integrierte Grafikchips hart – die Mess-Reihenfolge aus `plattform-mobile` gilt genauso.

## Veröffentlichen
Statische Dateien genügen (eigene Seite, GitHub Pages, itch.io). Vorher klären: Wo soll es liegen, öffentlich
oder nur per Link? Hochladen ist eine Veröffentlichung – nur auf Simons ausdrücklichen Auftrag.
