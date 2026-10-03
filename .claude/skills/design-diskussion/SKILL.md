---
name: design-diskussion
description: >-
  Mit Simon über Spieldesign diskutieren, bevor etwas gebaut wird – Game Loop, Mechaniken, Simulationstiefe,
  Schwierigkeit, Steuerungskonzepte, "wie könnte man das einfacher/besser machen?". Verwenden bei "ich möchte
  über … diskutieren", "weiter diskutieren", "hast du eine Idee, wie man …", "ginge das gut?", "was hältst du
  von …", "game loop", "macht das Spaß?", und immer, wenn Simon eine offene Richtungsfrage stellt statt eines
  klaren Auftrags. Hier wird zuerst geredet und entschieden; gebaut wird erst nach seinem Okay.
---

# Design-Diskussion

Simon nutzt diese Gespräche, um eine Richtung zu finden. Er will eine ehrliche Einschätzung, konkrete
Vorschläge und wenige, klare Entscheidungen – keine Ideenliste zum Abnicken. Er verwirft Vorschläge auch
(„keiner der Vorschläge“) und nennt dann seine eigene Richtung; dann dieser folgen und sie ernsthaft
durchdenken.

## Ablauf
1. **Ist-Stand aus dem Code, nicht aus der Erinnerung.** Kurz nachsehen, was das Spiel heute tatsächlich tut
   (welche Daten ein System liest, welche Regeln gelten, welche Zahlen). Zwei, drei Sätze dazu – oft liegt die
   Antwort schon in einer Lücke („die Tiere lesen von ihrer Umwelt fast nichts“).
2. **Ehrliche Schwächen**, höchstens vier, jede mit dem Grund, warum sie zählt. Nichts schönreden, nichts
   dramatisieren.
3. **Machbarkeit** der Richtung, die Simon nennt: technisch ja/nein, und die zwei, drei Bedingungen, unter
   denen es *gut* wird (z. B. Sichtbarkeit: jede Regel braucht eine lesbare Folge; Leitplanken gegen
   Entgleisen; Budget auf der Zielplattform).
4. **Vorschlag in Schichten:** aufeinander aufbauende Stufen, jede für sich spielbar und testbar, mit je einem
   konkreten Beispiel aus dem Spiel. Nicht alles auf einmal versprechen.
5. **Entscheidungsfragen:** zwei bis vier Fragen, die nur Simon beantworten kann – vor allem Ton und Grenzen
   (dürfen Dinge sterben? greift der Spieler ein? gibt es Gegner?). Jede so stellen, dass ein Wort als Antwort
   reicht.
6. **Stopp.** Auf die Antworten warten. Erst danach planen und bauen (meist über `agents-koordinieren`).

## Ton
- Kurz. Eine Diskussion ist kein Bericht: keine Tabellen, wenige Absätze, konkrete Beispiele aus dem Spiel.
- Eigene Meinung sagen und begründen; wenn Simon anders entscheidet, das ohne Nachkarten umsetzen.
- Kleine konkrete Fragen im selben Atemzug gleich beantworten (z. B. „geht der Stick unsichtbar?“ – ja, das
  fehlt dafür, zwei Entscheidungen dazu) und für die nächste Version vormerken.

## Wenn die Entscheidung gefallen ist
- Simons Antworten wörtlich festhalten (in der Änderungsnotiz der Version: „Entscheidungen: keine Räuber, Herden
  wandern nur, kein Eingreifen“). Sie sind die Leitplanken für alle Agents.
- Jede neue Regel bekommt eine sichtbare Folge und einen Messpunkt (wie oft passiert es in fünf Minuten?),
  damit Simon es beim Spielen bemerkt und man es nachstellen kann.
- Nach dem Bauen: ein Beobachtungs- oder Bot-Lauf zeigt, ob die bisherigen Zielwerte noch halten.
