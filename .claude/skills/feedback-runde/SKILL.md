---
name: feedback-runde
description: >-
  Simons Rückmeldungen nach dem Spielen eines Builds in eine neue Version umsetzen – Punkte zerlegen, im Code
  nachsehen, gezielt rückfragen, parallel bauen, testen, aufs Testgerät bringen, berichten. Verwenden, sobald
  Simon Spiel-Feedback schickt ("feedback:", "mir ist aufgefallen", "ich habe getestet", "weitere punkte", "das
  irritiert", "bitte nicht mehr als…", "hast du fragen?"), auch wenn er es nicht Feedback nennt und auch bei nur
  zwei, drei Punkten. Use for any playtest feedback list on a game in this repo.
---

# Feedback-Runde

Simon spielt einen Build, schreibt auf, was ihm auffällt (knapp, oft mit Tippfehlern, mehrere Punkte in einem
Absatz), und erwartet am Ende eine neue Version auf dem Testgerät. Der Ablauf unten hat sich über viele Runden
bewährt; er sorgt dafür, dass kein Punkt verloren geht und nichts gebaut wird, was Simon so nicht meinte.

## 1. Punkte zerlegen
Jeden Wunsch als eigenen Punkt notieren, in Simons Worten zitiert. Pro Punkt einordnen:
- **Bug** (etwas tut nicht, was es soll) – direkt beheben.
- **Wunsch mit klarem Ziel** – umsetzen.
- **Wunsch mit offener Entscheidung** (mehrere sinnvolle Lesarten, Zahlenwert fehlt, widerspricht einer
  früheren Entscheidung) – Rückfrage.
- **Beobachtung/Frage** („verschwinden die Tiere wirklich?“, „prüfe, ob…“) – erst untersuchen, mit Zahlen
  beantworten, dann ggf. beheben.

## 2. Erst nachsehen, dann fragen
Vor den Rückfragen kurz in Code und Daten nachsehen, wie es heute funktioniert. Das macht aus „wie meinst du
das?“ eine konkrete Frage („heute 70–130 s Pause und 4 Einheiten Nähe – deshalb siehst du es nie“) und klärt
manche Punkte ganz ohne Frage.

Rückfragen gebündelt in einer Nachricht stellen, höchstens vier, jede mit 2–3 Optionen und einer Empfehlung
(Werkzeug für Auswahlfragen benutzen, wenn vorhanden). Simons Antworten können vom Angebot abweichen – genau
lesen und wörtlich umsetzen. Wenn er eine frühere eigene Entscheidung umkehrt, kurz darauf hinweisen und dann
die neue umsetzen.

Ohne Rückfrage auskommen, wenn die Entscheidung keine echte ist. Dann im Bericht sagen, was gewählt wurde.

## 3. Bauen
- Größere Runden über den Skill `agents-koordinieren` aufteilen; kleine Einzelpunkte selbst erledigen.
- Gameplay und Balance nur so weit ändern, wie Simon es verlangt hat. Fällt dabei etwas Zusätzliches auf, kommt
  es in den Bericht als Vorschlag, nicht in den Code.
- Für jede Verhaltensänderung einen Test und einen Eintrag in der Änderungsnotiz der Version.

## 4. Prüfen
- Ganze Testsuite; bekannte, begründete Ausnahmen nennen.
- Das Spiel wirklich laufen lassen (Rauchtest in beiden/allen Modi, Konsole ohne Fehler), bei Balance- oder
  Dichte-Änderungen die Bot-Szenarien des Projekts.
- Auf der Zielplattform prüfen (Skill `plattform-mobile` / `-browser` / `-pc`): alter Spielstand lädt, die
  geänderten Stellen sind zu sehen, Bildrate und Fehlerprotokoll.

## 5. Abliefern
Build aufs Testgerät bzw. an den vereinbarten Ort. **Nicht committen**, bis Simon „speichere als vX“ sagt – es
sei denn, er hat es in derselben Nachricht schon gesagt (dann Skill `version-speichern`).

## Bericht (kurz, Deutsch)
1. Ein Satz: was fertig ist und wo es liegt.
2. Pro Punkt aus Schritt 1: was jetzt passiert. Bei „Beobachtung/Frage“-Punkten die Antwort mit Zahlen
   („deine Beobachtung stimmte: … 109 Tiere wurden gelöscht“).
3. Geprüft: Tests, Rauchtest, Zielplattform – mit Messwerten.
4. **Bitte selbst fühlen:** was nur Simon beurteilen kann.
5. Offene Entscheidungen oder Vorschläge, falls welche entstanden sind.

## Typische Fallen
- Zwei Punkte, die sich widersprechen oder dieselbe Stelle betreffen: vor dem Bauen auflösen, nicht danach.
- Ein „Fix“, der eine frühere Entscheidung Simons rückgängig macht: nachfragen.
- Seiteneffekte auf andere Modi: jede Änderung fragen „gilt das auch im anderen Modus?“ – Simon sagt es oft dazu
  („nur Gemütlich“), sonst nachfragen oder auf den genannten Modus beschränken.
