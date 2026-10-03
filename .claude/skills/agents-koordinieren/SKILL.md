---
name: agents-koordinieren
description: >-
  Einen größeren Auftrag an einem Spielprojekt auf parallele Agents verteilen und danach integrieren –
  Aufteilung nach Dateibesitz, Gerüst und Schnittstellen vorab, Regeln für den gemeinsamen Editor,
  Integrationswünsche, Szene/Build/Tests beim Koordinator. Verwenden, sobald eine Runde mehr als zwei, drei
  unabhängige Punkte hat, mehrere Systeme betrifft oder Simon "parallel", "Agents" oder "so schnell wie möglich"
  sagt – auch als Baustein von feedback-runde und testlauf. Nicht für kleine Ein-Datei-Änderungen.
---

# Parallele Agents koordinieren

Simon will, dass größere Arbeit standardmäßig parallel läuft. Das klappt nur, wenn die Agents sich nicht in die
Quere kommen – weder in Dateien noch im (einzigen) laufenden Editor. Der Koordinator baut dafür vorher das
Gerüst und macht nachher alles, was nur einer tun darf.

## 1. Aufteilen
- Ein Agent = ein unabhängiges System mit **eigenen Dateien**. Nie zwei Agents an derselben Datei. Große Dateien
  vorher teilbar machen (z. B. Erweiterungspunkte/Teilklassen), statt sie zu teilen.
- Üblich sind 3–7 Agents; mehr nur, wenn wirklich so viele unabhängige Systeme betroffen sind.
- Was voneinander abhängt, läuft nacheinander oder bekommt vorab eine feste Schnittstelle.
- Reine Lese-Aufgaben (Review, Analyse) können immer parallel laufen.

## 2. Gerüst vor dem Start (Koordinator)
- Gemeinsame Typen, Schnittstellen, Erweiterungspunkte und Platzhalter anlegen, sodass jeder Agent gegen eine
  **kompilierende** Basis arbeitet. Einmal kompilieren, bevor die Agents starten.
- Geteilte Dateien (Kern-Typen, Szene, Projekteinstellungen, Doku) gehören dem Koordinator.

## 3. Auftrag je Agent
Vorlage: `assets/agent-auftrag-vorlage.md`. Jeder Auftrag enthält:
- Kontext in zwei Sätzen und was zuerst zu lesen ist (Projekt-`CLAUDE.md`, betroffene Dateien).
- **Besitzliste:** genau diese Dateien, sonst nichts; fremde Wünsche unter „Integrationswünsche“.
- Regeln für den gemeinsamen Editor (siehe unten).
- Die Aufgabe mit Simons Worten und den getroffenen Entscheidungen, die Schnittstellen aus dem Gerüst,
  gewünschte Messung („vorher/nachher über 5 simulierte Minuten“).
- Berichtsformat: geänderte Dateien, Verhalten und Werte, Tests, Messungen, „Szenen-Verdrahtung nötig“,
  „Integrationswünsche“.

## 4. Regeln für den gemeinsamen Editor
- Agents starten das Spiel nicht (kein Play-Modus), speichern keine Szene, committen nicht, bauen nicht.
- Prüfen nur über Kompilieren, eigene Testklassen und kurze Auswertungen im Editor.
- Erst lesen und schreiben, dann kompilieren – möglichst wenige Runden. Schlägt das Kompilieren wegen einer
  fremden Datei fehl: warten und wiederholen, nicht „reparieren“.
- Keine Hintergrundprozesse, die den Agent überleben; Schleifen immer mit Obergrenze.
- Während ein Build läuft, ändert niemand Skripte (sonst scheitert der Build).

## 5. Integration (Koordinator)
1. Berichte lesen; Integrationswünsche umsetzen (Hooks, Enum-Werte, Aufrufe in geteilten Dateien).
   Nachzügler-Infos an noch laufende Agents weitergeben.
2. Szenen-/Projektwerte setzen: gespeicherte Werte überschreiben Code-Standardwerte – nach geänderten
   Standardwerten die gespeicherten Objekte nachziehen. Vorher den Projektzustand von der Platte neu laden.
3. Ganze Testsuite. Tests, die durch neues Verhalten kippen, verstehen (liegt es am Test oder am Verhalten?)
   und begründet anpassen – nie einfach löschen.
4. Das Spiel laufen lassen: Rauchtest, Bot-Szenarien, Konsole.
5. Doku (Änderungsnotiz, Architektur), dann Build und Zielplattform.

## Fallen aus der Praxis
- Ein Agent meldet „fertig“, aber seine Aufnahme/Messung lief auf einem Stand, den andere gerade veränderten:
  Messungen, auf die es ankommt, macht der Koordinator nach der Integration noch einmal.
- Neue globale Zustände (Anbieter, Jahreszeit, Zufall) ändern das Verhalten alter Tests: in Test-Setups
  festnageln und das in der Doku notieren.
- Agents berichten optimistisch. Stichproben: Screenshot ansehen, einen Test selbst laufen lassen.
