# ShopBot – your shop runs itself

**ShopBot** is a BepInEx plugin for **TCG Card Shop Simulator** that runs your shop almost on its own – built for the **Real TCG Overhaul** modpack (Pokémon).
Goal: **own every Pokémon card in the Collection Tracker** (later every card as PSA 10) while the shop makes money.

> 🇩🇪 Deutsche Version: [README.md](README.md) · Detailed German step-by-step guide: [docs/ANLEITUNG.md](docs/ANLEITUNG.md)
> The in-game dashboard and log messages are in **German**.

---

## What the bot does

| Area | Automatic |
|---|---|
| 💰 **Checkout** | Serves **all free registers at once** (scan, take money, give change) – no cashiers needed |
| 👷 **Workers** | All workers refill the **pack openers** (only packs of unfinished Pokémon sets), daily plan by skills, 3× daily bonus, hires new workers |
| 📦 **Packs & collection** | Reads the Collection Tracker, buys packs of the sets with the **most new cards per dollar**, fetches boxes from warehouse shelves, **emergency restock** when openers run dry |
| ⚙️ **Pack openers** | Fills openers itself, starts half-full ones, collects finished ones |
| 🏷️ **Prices** | Market price **+20 %**, rounded (products, singles, graded cards) |
| 🛒 **Shelves & displays** | Stocks shelves, puts cards into empty card display slots (graded first) |
| 🗑️ **Tidying** | Full boxes onto warehouse shelves, empty boxes into the trash bin |
| 🏅 **Grading** | Sends cards to PSA (standard, full 52-card submissions, most valuable first), **opens returned packages**, sells graded cards (one PSA 10 of each card is kept) |
| 🤝 **Customer offers** | Buys & trades smartly: never pays more than the customer asks, pays more for cards still missing from the collection (even more for sets that are hard to get), never gives away the last copy |
| 🔓 **Unlocks** | Licenses (packs of missing sets first), shop/warehouse expansions, upgrades |
| 📅 **Daily routine** | Pays bills immediately, ends the day at 9 pm when the shop is empty |
| 🛡️ **Money** | May spend everything but always keeps the next bills – **never goes negative** |

Everything can be switched off and tuned in the **dashboard (F9)** or the **Configuration Manager (F1)**.

## Installation (step by step)

1. **Find your game folder:** Steam → Library → right-click *TCG Card Shop Simulator* → *Manage* → *Browse local files*.
2. **Back up your save:** press `Win + R`, enter `%USERPROFILE%\AppData\LocalLow\OPNeonGames\Card Shop Simulator`, copy that folder somewhere safe.
3. **BepInEx 5:** if there is no `BepInEx` folder in the game folder yet, download **BepInEx 5.4.x win_x64** from [github.com/BepInEx/BepInEx/releases](https://github.com/BepInEx/BepInEx/releases) (not BepInEx 6), extract it into the game folder, start the game once and quit. (Already included with the Real TCG Overhaul mod manager.)
4. **Recommended mods:** Real TCG Overhaul (Nexus Mods #577) with Enhanced Prefab Loader, Collection Tracker, CardGrade Overhaul, Upgrades, ConfigurationManager. Missing mods only disable the matching bot feature.
5. **Install ShopBot:** download the latest `ShopBot-x.y.z.zip` from [**Releases**](../../releases) and drag the `BepInEx` folder from the zip into your game folder (merge folders). You should now have `…\BepInEx\plugins\ShopBot\ShopBot.dll`.
   *If Windows blocks the DLL: right-click → Properties → tick "Unblock".*
6. **Start the game and load your save.** A small status line appears top left; after 10 seconds the bot starts. Press **F9** for the dashboard.
7. **Check:** `BepInEx\LogOutput.log` must contain `Loading [ShopBot …]`.

## Keys

| Key | Function |
|---|---|
| **F9** | Dashboard on/off (also ESC) |
| **F8** | Bot on/off |
| **F1** | Configuration Manager (section "ShopBot") |

## Tips

- Set up enough **pack openers**, a **trash bin**, **warehouse shelves**, a **card storage** and **card displays** – the bot works with what is in your shop.
- Windows (customer offers, grading packages) only open when you haven't pressed anything for **8 seconds**.
- New/low-money save: lower the daily limit (dashboard → *Sammlung → Grenzen*) and switch off the emergency restock.
- Want to work the register yourself? Dashboard → *Laden & Preise → Tagesablauf* → turn off "Bot kassiert an allen freien Kassen". Registers you stand at are never touched.

## Troubleshooting

| Problem | Fix |
|---|---|
| Nothing happens, F9 does nothing | DLL in the wrong place? BepInEx 5 x64 installed? DLL unblocked? Check the log |
| "Collection Tracker nicht gefunden" in the log | Mod missing → collection features are off |
| Grading packages stay on the floor | Search the log for `[Grading-Paket] nicht geöffnet:` – the reason is there |
| Customers pile up at the registers | Dashboard → raise "Bot-Kasse: Artikel pro Sekunde" |
| Something went wrong | Press **F8**, restore your save backup, send `BepInEx\LogOutput.log` via **Issues** |

**Uninstall:** delete `BepInEx\plugins\ShopBot` (optionally `BepInEx\config\ratta.shopbot.cfg`). Your save stays playable.

## Building from source

```
git clone <repo-url>
cd TCG-ShopBot
copy GameDir.local.props.example GameDir.local.props   # enter your game folder
dotnet build -c Release
```
Requires the **.NET 8 SDK**. The DLL is copied into `BepInEx\plugins\ShopBot` automatically. Changelog: [CHANGELOG.md](CHANGELOG.md) (German).

*Unofficial fan project, not affiliated with OPNeon Games, the Nexus mod authors, PSA or The Pokémon Company.*
