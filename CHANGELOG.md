# Änderungen

Ausführliche Entwickler-Notizen: [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md)

## 0.4.2 – 23.11.2026
- **Geld nie im Minus:** eigene Ausgaben werden sofort vom Budget abgezogen (vorher konnte der Bot innerhalb eines Einkaufs mehrfach mit altem Kontostand kaufen).
- **Ganzes Geld nutzen:** statt fester Reserve werden nur offene + nächste Rechnungen zurückgehalten (abschaltbar).
- **Notfall-Nachkauf**, wenn die Opener-Packs ausgehen (auch über das Tageslimit, eigenes Notfall-Limit).
- Vorrat immer mindestens eine volle Opener-Füllung; Kartons im Lagerregal werden mitgezählt.
- Lizenzen: alle verfügbaren auf einmal, Packs fehlender Sets zuerst; Lizenzarten, die eine Mod immer wieder sperrt, werden nicht mehr gekauft.
- Grading-Pakete: Öffnungsfenster wird zuverlässig gefunden; Grund fürs Nicht-Öffnen steht im Log.
- Tagesende wartet auch an der Bot-Kasse bis zu 5 Min. auf die letzten Kunden.

## 0.4.0
- **Bot-Kasse:** kassiert an allen freien Kassen gleichzeitig → alle Arbeiter befüllen Opener.
- Lizenzen nach bester Marge, optionale PSA-10-Sammlung.

## 0.3.0
- Kartons einsortieren (Lagerregal) und leere Kartons wegwerfen.
- Grading-Pakete mit CardGrade Overhaul (mehrere Runden) korrekt öffnen.
- Volle Kartenablage: billige Doppelte zurück ins Album, damit gegradete Karten verkauft werden; Vitrinen bekommen gegradete zuerst.
- Bereits bezahlte Lizenzen werden kostenlos wiederhergestellt.

## 0.2.0
- Bot befüllt Pack-Opener selbst, stellt neue Arbeiter ein.
- Optional: Tageslimit wächst mit dem Geld, Opener-Turbo.

## 0.1.0
- Erste Version: Preise, Kunden-Angebote, Opener, Tagesende, Rechnungen, Sammlung/Pack-Kauf, Graden, Arbeiter-Einsatzplan, Upgrades, Erweiterungen, Lizenzen, Selbst einräumen, Dashboard.
