# Zielwerte – Vorlage (an das Projekt anpassen)

Jeder Wert bekommt im Bericht: bestanden / nicht bestanden / nicht geschafft + Messwert.

## Zielplattform (Gerät/Browser/PC nennen)
- Bildrate im Normalfall ≥ <Ziel> fps; im schlimmsten Fall (größte Szene + Wetter/Effekte + Ereignis) nie unter <Untergrenze>.
- Dauerlauf 10 min: kein deutlicher Abfall durch Wärme/Drosselung, Speicher wächst nicht stetig, Akku/Last notieren.
- Start bis spielbar ≤ <n> s (Browser: bis zur ersten Interaktion, inkl. Laden).
- Keine Exceptions/Abstürze im Protokoll.
- Unterbrechung (Hintergrund und zurück, Bildschirm aus/an, Tab-Wechsel, Fensterfokus): Spiel läuft korrekt weiter, Spielstand intakt.
- Eingabe der Plattform: funktioniert, kalibriert, kein Wegdriften im Ruhezustand.

## Performance (Editor, Pipeline der Zielplattform)
- GC-Allokation im laufenden Spiel ≈ 0 (< 1 KB pro Frame).
- Draw Calls/Batches und Dreiecke notieren, größte CPU- und GPU-Verbraucher nennen.

## Je Spielmodus (Beispiele)
- Beobachten/Erkunden: längste Lücke ohne sehenswertes Ereignis in Kameranähe ≤ <n> s; wichtige Objekte ≥ <n> px hoch bei Standard-Zoom; Kamera ruhig.
- Action/Rennen: eine Entscheidung alle <a>–<b> s; Anfänger-Bot überlebt Level 1 ≥ <n> s; Zeit pro Level.
- Fortschritt: Zeit bis zum ersten Meilenstein, Abstände zwischen Meilensteinen, Dauer eines ganzen Durchlaufs.

## Erster Start
- Tutorial/Erststart verständlich, nichts blockiert (Flags vorübergehend zurücksetzen).

## Allgemein
- 0 Fehler in der Konsole in allen Modi.
- Speichern/Laden, Pause, Moduswechsel, Offline-Fortschritt, Sammlungen/Tagebücher funktionieren.
- Alle Steuerungsarten funktionieren.
- UI im Zielformat: Texte lesbar, nichts abgeschnitten, keine Reste in falscher Sprache, keine Tippfehler,
  Bedienelemente groß genug, sichere Bereiche (Notch, Browser-Leisten, Ultrawide) berücksichtigt.
