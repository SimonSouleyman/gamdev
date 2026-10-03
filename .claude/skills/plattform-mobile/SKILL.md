---
name: plattform-mobile
description: >-
  Alles, was ein Handy-Spiel (Android, später iOS) von anderen unterscheidet – auf das Testgerät bringen,
  Protokoll und Bildrate aufzeichnen, Screenshots und Taps über USB, Leistung erst messen und dann optimieren
  (GPU/CPU-Engpass, automatische Auflösung, Messsonde), Touch- und Neigungseingabe, Hintergrund/Rückkehr, Akku
  und Wärme. Verwenden bei "aufs Handy", "APK", "es ruckelt auf dem Handy", "Bildschirm geht aus", "Laufzeit
  optimieren", "fps", "Touch", "Kippen/Neigung", "Hochformat", "Notch", und bei jedem Performance-Auftrag für
  ein Mobile-Projekt.
---

# Plattform: Mobile

Das Handy ist die Wahrheit: Was im Editor flüssig läuft, sagt wenig über das Gerät. Deshalb immer auf dem
Testgerät messen, bevor optimiert oder „fertig“ gesagt wird. Engine-Befehle stehen in der Projekt-`CLAUDE.md`,
Gerätedetails im Gedächtnis (Testgerät, adb-Pfad).

## Aufs Gerät und aufzeichnen (Android über USB)
- Installieren mit `adb install -r <apk>`; starten über den Launcher-Intent; Version danach prüfen.
- Protokoll: `adb logcat` gefiltert auf die App und Laufzeitfehler, im Hintergrund mit hartem Zeitlimit in eine
  Datei. In Git Bash unter Windows `MSYS_NO_PATHCONV=1` setzen, sonst werden Gerätepfade verbogen.
- Screenshots (`adb exec-out screencap -p`), Taps und Wischer (`adb shell input …`) für eigene Prüfungen.
  Koordinaten der wichtigen Knöpfe im Gedächtnis notieren.
- Daten der App liegen im externen App-Ordner (`/sdcard/Android/data/<paket>/files`).
- **Regeln:** nie entsperren; vor Übernahme fragen; Screenshots mit Privatem löschen. Neigung/Sensoren lassen
  sich über USB nicht testen – das prüft Simon.

## Was jede Entwickler-Version können sollte
- Alle ~10 s eine Protokollzeile: mittlere Bildrate, 95-%-Bildzeit, längstes Bild, Anzahl langsamer Bilder,
  Modus, Speicher. Damit ist jede Spielsitzung im Nachhinein auswertbar.
- Eine **Messsonde**, die per Schalter (z. B. Datei im App-Ordner) startet und nacheinander Einstellungen und
  Bestandteile abschaltet (Auflösung, Kantenglättung, UI, jeder Shader/jedes System) und je Schritt Bildrate und
  GPU/CPU-Zeit ausgibt – mit „Schritt beginnt“-Zeile, damit man je Schritt einen Screenshot machen kann.

## Leistung: erst messen
1. **Engpass bestimmen:** GPU- oder CPU-gebunden? (GPU-Zeit über die Frame-Timing-Schnittstelle der Engine;
   wenn sie fehlt: skaliert die Bildrate mit der Auflösung, ist es die GPU.)
2. **Verursacher finden:** Sonde laufen lassen; den teuersten Posten zuerst. Auf dem Handy sind es meist
   bildschirmfüllende Shader (Wasser, Himmel, Nebel), Überzeichnung durch Transparenz und zusätzliche Kameras.
3. **Optimieren** in der Reihenfolge billig → teuer: Arbeit aus dem Pixel in den Eckpunkt oder in eine kleine
   Textur verlagern; halbe Genauigkeit für Farb-/Lichtrechnung (nie für Weltkoordinaten, Zeit, Rausch-Eingaben);
   früh verwerfen; Tiefen-/Farbkopien nur für Kameras, die sie brauchen; Meshes nicht jedes Bild neu aufbauen.
4. **Absichern:** automatische Auflösung als Sicherheitsnetz (runter, wenn die GPU-Zeit das Budget reißt; hoch
   nur mit klarem Abstand; nie steuern, wenn das Gerät keine GPU-Zeit meldet).
5. **Nachmessen** auf dem Gerät, gleiche Szene, vorher/nachher als Tabelle. Sichtbare Unterschiede im Bericht
   nennen – Simon ist empfindlich für Optik-Änderungen.

Rausch-Funktionen mit großen Weltkoordinaten oder Laufzeiten verlieren auf Mobil-GPUs Bits und erzeugen
Kachelmuster – Eingaben vorher ganzzahlig reduzieren.

## Eingabe und Verhalten
- **Touch:** ein Finger irgendwo = steuern relativ zum Aufsetzpunkt; ein Tipp ohne Ziehen bleibt ein Tipp; zwei
  Finger = Zoom; Finger auf Bedienelementen steuern nie. Bedienelemente fingergroß, sichere Bereiche (Notch)
  beachten.
- **Neigung:** Mitte kalibrieren beim Start und nach Rückkehr aus dem Hintergrund; Totzone; die Richtung gilt im
  Bildschirm-Bezug.
- **Bildschirm:** bleibt nur im laufenden Spiel an; in Menüs und Pause darf er abdunkeln.
- **Hintergrund/Rückkehr:** speichern beim Verlassen, pausiert zurückkommen, verstrichene Zeit nachholen, wenn
  das Spiel „weiterlebt“.
- **Dauerlauf:** 10 Minuten zeigen Wärme-Drosselung und Speicherwachstum; Akkuverbrauch nur ohne Ladekabel
  messbar.

## iOS
Braucht einen Mac mit Xcode für den Build; vorher klären, ob und wie getestet werden kann. Regeln für Eingabe,
Leistung und Unterbrechungen gelten gleich.
