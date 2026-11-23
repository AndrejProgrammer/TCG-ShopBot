# Änderungen

Ausführliche Entwickler-Notizen: [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md)

## 0.5.2 – 23.11.2026
- **Neue Karten: immer genau den Wunschpreis zahlen** (nie mehr, unabhängig vom Marktpreis) – nur die Grenze von 10 % des Geldes pro Karte gilt. Abschaltbar.
- Gebote werden auf den Cent **auf**gerundet statt 1 Cent über dem Wunschpreis.

## 0.5.1 – 23.11.2026
- **Kunden-Ankauf neu gedacht:** Der Bot bietet nie mehr als der Wunschpreis – er prüft nur, ob sich der Preis lohnt. Doppelte bis 95 % Marktpreis (Weiterverkauf +20 %). **Neue Karten** bis 200 % Marktpreis, bei Sets, die **pausiert** sind oder **keine kaufbaren Packs** haben, bis 500 % (der Kunde ist dort die einzige Quelle). Höchstens 10 % vom Geld pro Karte. Liegt der Wunschpreis weit darüber, lehnt er sofort ab, statt den Kunden mit Geboten ungeduldig zu machen.
- **Tausch:** Für eine neue Karte gibt der Bot Doppelte bis zu diesem Wert her (nie die letzte Karte).
- **Ziel-Übersicht** (Dashboard → Sammlung): gesammelte / fehlende Pokémon-Karten, neue Karten pro Tag, geschätzte Spieltage bis alles da ist, Sets ohne kaufbare Packs, pausierte Sets.
- **Englische Anleitung:** [README.en.md](README.en.md).

## 0.5.0 – 23.11.2026
- **Kartons aus dem Lagerregal holen:** Fehlen Packs für freie Opener oder Ware für halb leere Regalfächer, holt der Bot Kartons selbst aus dem Lager (wie ein Klick aufs Lagerfach) – alle Opener laufen voll, auch mit wenigen Arbeitern.
- **Klügere Set-Auswahl:** Sets mit den meisten neuen Karten pro Dollar zuerst (abschaltbar). Fehlen nur noch 1–3 Karten, kauft der Bot höchstens 1.500 Packs pro fehlender Karte und pausiert das Set dann (die Karten lieber von Kunden ankaufen).
- **Lizenzen richtig gelesen:** Mod-Artikel (Enhanced Prefab Loader) speichern ihre Lizenz extra – der Bot las die Liste direkt und hielt freigeschaltete Booster Boxes für gesperrt. Jetzt über die Spielfunktion → keine falschen „wieder gesperrt“-Meldungen, Booster-Box-Lizenzen werden wieder normal gekauft.
- **Vitrinen voll:** Hinweis im Dashboard und Verlauf, wenn Karten in der Ablage warten, aber kein Vitrinen-Platz frei ist.
- **Tagesbericht** im Dashboard (Einnahmen Bot-Kasse, Bot-Ausgaben, Kunden, Packs – heute + letzte Tage).
- **Hinweis bei fehlenden Mods** (Collection Tracker, CardGrade Overhaul, Upgrades) im Dashboard.
- **Tageslimit wächst mit** (mind. 15 % vom Geld), wenn „Ganzes Geld nutzen“ an ist.


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
