# gamdev – gemeinsame Regeln für alle Spielprojekte

Ein Repo, jedes Spiel ein eigener Ordner (`Drift/`, `Tree/`, …). Jeder Projektordner hat seine eigene
`CLAUDE.md` (Engine, Konventionen, Stolperfallen), eine `README.md` und seine Doku. **Die Projektregeln gehen
vor**, wenn sie etwas anderes sagen als diese Datei (z. B. UI-Sprache, Versionsschema, Branch-Ablauf).

## Wie Simon arbeitet (gilt in jedem Projekt)

- **Sprache:** Im Chat Deutsch. Die Sprache der Spieloberfläche und der Doku legt das Projekt fest.
- **Rückfragen:** Wenn eine Entscheidung wirklich Simons ist (Spielgefühl, Balance, Benennung, Umfang), fragen –
  mit einer empfohlenen Option und kurzer Begründung. Vorher im Code nachsehen, damit die Frage konkret ist.
  Alles andere selbst entscheiden und im Bericht erwähnen.
- **Was ohne Rückfrage geht:** Bugfixes, Optimierungen ohne sichtbare Änderung, Tests, Doku.
  **Was nie ohne Auftrag geht:** Gameplay- und Balance-Änderungen, große Umbauten – die kommen auf eine
  Vorschlagsliste (sortiert nach Wirkung/Aufwand), Simon wählt aus.
- **Berichte:** kurz, mit Messwerten statt Adjektiven, als Tabelle wo es Zahlen gibt. Klar trennen: geprüft /
  nicht geprüft / nicht prüfbar. Am Ende eine Liste „Bitte selbst fühlen“ für alles, was nur mit den Händen
  beurteilbar ist (Steuerung, Kamera, Tempo).
- **Lange Aufgaben:** zwischendurch kurze Fortschrittsmeldungen. Zeitrahmen einhalten, wenn einer genannt ist.
- **Parallel arbeiten:** größere Aufträge in unabhängige Teile zerlegen und an parallele Agents geben
  (Skill `agents-koordinieren`).
- **Speichern nur auf Zuruf:** Commit, Tag und Snapshot erst bei „speichere als vX“; nach GitHub erst bei
  „und auf GitHub“ (Skill `version-speichern`). Davor bleiben Änderungen im Arbeitsverzeichnis.
- **Spielstände** sind während der Entwicklung nicht wertvoll – kein Sichern/Zurückspielen nötig.
- **Ton aus** in eigenen Spieltests.
- **Testgerät:** nie aus der Ferne entsperren; bevor das Gerät übernommen wird, fragen und auf das Okay warten.
  Screenshots mit privaten Inhalten (Benachrichtigungen) sofort löschen, nie in Berichte übernehmen.
- **Ehrlichkeit:** Fehlgeschlagenes, Übersprungenes und eigene Fehler offen nennen (auch: „mein Build hat X
  blockiert“).

## Versionen und Git

- Tags heißen `<projekt>-vX.Y(.Z)` (`drift-v0.6.8`, `tree-v0.2`). Neue Funktionen heben die zweite Stelle,
  Fixes und Rückmeldungs-Runden die dritte/vierte – außer das Projekt regelt es anders.
- Jede Version hat eine Änderungsnotiz im Projekt (`Docs/CHANGES_<datum>.md` oder `CHANGELOG.md`) und eine
  Zeile in der Projektliste der Repo-`README.md`.
- Vor jedem Push `git pull` (andere Sitzungen arbeiten an anderen Projektordnern) und nur den eigenen
  Projektordner anfassen.
- Build-Ergebnisse (APKs, Web-Builds, Installer) und Engine-Caches gehören nicht ins Repo.

## Skills in diesem Repo (`.claude/skills/`)

| Skill | Wofür |
|---|---|
| `feedback-runde` | Simons Rückmeldungen nach dem Spielen umsetzen |
| `version-speichern` | „speichere als vX (und auf GitHub)“ |
| `spieltest-aufzeichnen` | Simon spielt, Claude zeichnet auf und wertet aus |
| `testlauf` | Zeitlich begrenzter Test mit Zielwerten und Bericht |
| `agents-koordinieren` | Arbeit auf parallele Agents verteilen und integrieren |
| `design-diskussion` | Über Game Loop, Mechaniken, Simulation diskutieren, bevor gebaut wird |
| `spielprojekt-start` | Neues Spiel im Repo anlegen |
| `plattform-mobile` / `plattform-browser` / `plattform-pc` | Bauen, Testen, Messen und Eigenheiten je Zielplattform |

Die Skills sind engine-neutral. Engine-Befehle und Stolperfallen stehen in der `CLAUDE.md` des Projekts –
neue Erkenntnisse dort nachtragen, nicht in den Skills.

Die Skills laden nur, wenn die Sitzung in diesem Repo gestartet wird (oder das Repo mit `--add-dir` dazukommt).
