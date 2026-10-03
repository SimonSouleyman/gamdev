---
name: spieltest-aufzeichnen
description: >-
  Simon spielt selbst (Handy, Browser oder PC) und Claude zeichnet im Hintergrund auf und wertet danach aus –
  Bildrate je Modus, schlechteste Abschnitte, Fehler, Speicher, Auffälligkeiten. Verwenden bei "ich teste
  jetzt", "du kannst aufzeichnen", "zeichne auf", "ich spiele ein bisschen", "bin fertig, werte aus", "beende
  die Aufzeichnung", "ist dir noch was aufgefallen?" – auch wenn das Aufzeichnen nicht ausdrücklich verlangt
  wird, aber Simon ankündigt, gleich zu spielen.
---

# Spieltest aufzeichnen und auswerten

Simons eigene Spielsitzungen sind die wertvollste Testquelle: echtes Gerät, echte Hände. Claude liefert dazu die
Messung, die er beim Spielen nicht sieht. Zwei Phasen – die Auswertung kommt erst, wenn Simon sagt, dass er
fertig ist.

## Phase 1 – Aufzeichnen
1. Prüfen, dass der richtige Build läuft (Version, möglichst die Entwickler-Version mit Messausgaben). Falls
   nicht: anbieten, ihn aufzuspielen, und auf Simons Okay warten.
2. Aufzeichnung starten – je Plattform anders, siehe `plattform-mobile`, `plattform-browser`, `plattform-pc`
   (Abschnitt „Aufzeichnen“). Immer mit hartem Zeitlimit (z. B. 90 min), als Hintergrundprozess, in eine Datei
   außerhalb des Repos.
3. Startwerte notieren, soweit verfügbar: Uhrzeit, Akku, Temperatur, Speicher.
4. Eine Zeile an Simon: Aufzeichnung läuft, was sie erfasst. Dann **nichts am Gerät anfassen** – keine Taps,
   keine Screenshots, kein Neustart –, bis er sich meldet.

Parallel darf am Rechner weitergearbeitet werden, solange es das Spielgerät nicht stört.

## Phase 2 – Auswerten (bei „bin fertig“)
1. Aufzeichnung beenden.
2. Zahlen ziehen:
   - Dauer, gespielte Modi.
   - Bildrate je Modus: Mittel, schlechtestes Zeitfenster, Anteil langsamer Bilder, Zeitpunkte der Ausreißer
     (und was da vermutlich geschah: Laden, Verschmelzen, Speichern).
   - Auflösungsstufe/Qualitätsregler, falls das Spiel einen hat.
   - Speicher: Anfang, Ende, Verlauf (stetiges Wachsen ist das Signal, nicht Schwanken).
   - Fehler, Exceptions, Warnungen – nach Häufigkeit; bekannte harmlose Meldungen als solche benennen.
   - App-Ereignisse: Hintergrund/Rückkehr, Neustarts, vom System beendet.
3. Kurz berichten (Tabelle, wenn mehrere Modi). Unter „aufgefallen“ nur, was die Daten wirklich zeigen.
4. Simons Punkte aus derselben Nachricht gehen danach in den Skill `feedback-runde`.

## Regeln am Testgerät
- Nie aus der Ferne entsperren. Bildschirm schwarz oder gesperrt: Simon Bescheid geben.
- Vor jeder Übernahme (Installieren, Tippen, Screenshots) fragen, wenn Simon das Gerät gerade nutzt, und auf
  „du kannst es haben“ warten.
- Screenshots, auf denen Privates zu sehen ist (Benachrichtigungen, andere Apps), sofort löschen.
- Aufzeichnungen bleiben lokal; nichts davon ins Repo.

## Was die Aufzeichnung nicht kann
Gefühl (Steuerung, Kamera, Lesbarkeit) misst kein Protokoll. Wenn Simon so etwas beschreibt, im Code die
Ursache suchen und nachfragen, statt aus den Zahlen zu raten.
