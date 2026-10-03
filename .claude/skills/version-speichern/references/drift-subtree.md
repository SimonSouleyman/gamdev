# Drift: Speichern über das Quell-Repo und Subtree

Drift wird (Stand Oktober 2026) in einem eigenen Git-Repo außerhalb von gamdev entwickelt
(`C:\Users\Home\Documents\Unity\Drift`, Branch `main`, Tags `vX`) und per Subtree nach `gamdev/Drift` übernommen.
Neue Projekte sollen direkt in gamdev liegen; dann entfällt dieser Umweg.

## Im Quell-Repo
1. Version über die Engine setzen, Builds, Doku (siehe `Drift/CLAUDE.md`).
2. `DriftVersions/tools/save-version.sh <X> "<Notiz>"` – committet, taggt `vX` und legt einen Snapshot unter
   `DriftVersions/vX` an. Die Notiz endet mit einer Leerzeile und der Attributionszeile.
3. Zeile in `DriftVersions/README.md` anhängen (Version, Datum, Inhalt, Commit, Testzahl).
4. APKs nach `Drift-APK/` kopieren, `Installieren-per-USB.bat` auf die neue Datei zeigen lassen.

## Nach gamdev (nur bei „und auf GitHub“)
```bash
cd /c/Users/Home/Documents/Unity/gamdev
cp -r ../Drift/.git/lfs/objects/. .git/lfs/objects/      # sonst scheitert der LFS-Smudge
git pull --no-rebase origin main                         # Tree-Commits anderer Sitzungen
git subtree pull --prefix=Drift "C:/Users/Home/Documents/Unity/Drift" main -m "Drift vX: <Notiz>"
git tag -a drift-vX <Commit von vX> -m "Drift vX"       # derselbe Hash wie im Quell-Repo
# README.md: Tag-Bereich der Drift-Zeile anpassen, committen
git push origin main --tags
```
Der Klon braucht eine Git-Identität (`user.name`/`user.email` wie in den bisherigen Commits). Push läuft über den
Git Credential Manager ohne Abfrage.
