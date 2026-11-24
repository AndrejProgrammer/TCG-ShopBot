# ShopBot – your card shop runs itself

**ShopBot** is a BepInEx plugin for **TCG Card Shop Simulator** that runs your shop almost on its own – tuned for the **Real TCG Overhaul** mod pack (Pokémon).
Goal: **own every Pokémon card in the Collection Tracker** (later all of them as PSA 10) – while the shop makes money.

> Version **0.6.1** · Game: Steam default branch (Unity 6) · BepInEx 5.4.x · Dashboard in **English** and **Deutsch**
> 📖 **New here? → [Step-by-step guide](docs/INSTALL.md)** · 🇩🇪 [Deutsche Version](README.de.md)

---

## What the bot does

| Area | Automatic |
|---|---|
| 💰 **Checkout** | Serves **all free registers at the same time** (scan, take money, give change) – no cashiers needed |
| 👷 **Workers** | All workers fill the **pack openers** (only packs of unfinished Pokémon sets), work plan by strengths, 3× bonus daily, hires new workers |
| 📦 **Packs & collection** | Reads the Collection Tracker, buys packs of the sets with the **most new cards per dollar**, takes boxes from storage, **emergency restock** when the openers run dry |
| ⚙️ **Pack openers** | Fills openers itself, **spreads packs evenly when stock is low** so every opener keeps running, starts half-full ones, collects finished ones |
| 🏷️ **Prices** | Market price **+20 %**, rounded (products, single cards, graded cards) |
| 🛒 **Shelves & displays** | Stocks shelves itself, puts cards into empty display slots (graded first) |
| 🗑️ **Tidying** | Full boxes onto the storage shelf, empty boxes into the trash |
| 🏅 **Grading** | Sends cards to PSA (Standard, full 52-card submissions, most expensive first), **opens returned packages** and sells graded cards (one PSA 10 of every card stays) |
| 🔓 **Unlocking** | Licenses (packs of missing sets first), shop/warehouse expansions, upgrades – but **packs come first** when the openers are short |
| 🤝 **Customer offers** | Buying & swapping: **new cards always at the asking price**, duplicates only below market price with smart haggling, never more than asked, never gives away the last copy |
| 📅 **Daily routine** | Pays bills right away, ends the day at 9 PM when the shop is empty |
| 🛡️ **Money** | May spend everything but always holds back the next bills – **never goes negative** |

Everything can be switched on/off and tuned – in the **dashboard (F9)** or the **Configuration Manager (F1)**.

---

## Quick start

1. Install **BepInEx 5** in the game (already included in the Real TCG Overhaul mod pack).
2. Download the latest `ShopBot-x.y.z.zip` from [**Releases**](../../releases).
3. Extract it into the **game folder** → you get `BepInEx\plugins\ShopBot\ShopBot.dll`.
4. Start the game, load your save – after 10 seconds the bot gets going. Press **F9** for the dashboard.

👉 Detailed guide with finding the game folder, recommended settings and troubleshooting: **[docs/INSTALL.md](docs/INSTALL.md)**

## Keys

| Key | Function |
|---|---|
| **F9** | Open/close the dashboard (also Esc) |
| **F8** | Bot on/off |
| **F1** | Configuration Manager (all settings, section "ShopBot") – needs the *ConfigurationManager* plugin |

## Language

The dashboard is available in **English** (default) and **Deutsch**. Switch it with the language button at the top right of the dashboard or on the **Settings** page.
The log file (`BepInEx\LogOutput.log`) always stays English, so bug reports are readable for everyone.

**Add your language:** copy [`lang/en.txt`](lang/en.txt), translate the right side of every line and save it as `<code>.txt` (e.g. `fr.txt`) in `BepInEx\plugins\ShopBot\lang\`. It shows up in the language picker right away. Keep placeholders like `{0}` or `{1:N0}`. Pull requests with new languages are welcome!

## Requirements

| Required | |
|---|---|
| TCG Card Shop Simulator (Steam) | |
| [BepInEx 5.4.x](https://github.com/BepInEx/BepInEx/releases) (x64) | included in the mod pack |

| Recommended (otherwise that feature is off) | For |
|---|---|
| **Real TCG Overhaul** (Nexus Mods, mod 577) incl. Enhanced Prefab Loader | Pokémon products and sets |
| **Collection Tracker** | Reading the collection, "buy now" packs, opener boost |
| **CardGrade Overhaul** | Grading via PSA, opening grading packages |
| **Upgrades** | Collection point upgrades |
| **ConfigurationManager** | Settings with F1 |

## FAQ

<details><summary><b>The bot does nothing.</b></summary>

- Is a save loaded? In the main menu it does nothing. After loading it waits 10 s.
- Is it on? Press **F8** – the status is shown at the top left.
- Is the DLL really at `…\TCG Card Shop Simulator\BepInEx\plugins\ShopBot\ShopBot.dll`?
- Check the log: `BepInEx\LogOutput.log` must contain `Loading [ShopBot …]`.
</details>

<details><summary><b>Windows pop up while I'm playing.</b></summary>

Window actions (customer offers, grading packages) only happen when you **haven't pressed anything for 8 seconds** (adjustable on the Settings page). Grading packages that have been waiting for more than 60 s are also opened in between (window for about 2 s).
</details>

<details><summary><b>It spends too much money.</b></summary>

Dashboard → **Collection → Limits**: lower the daily limit. Or turn off "Use all money" and set a fixed **safety reserve**. You can also switch off the **emergency restock** or change its limit there.
</details>

<details><summary><b>My openers keep running out of packs.</b></summary>

Openers are the engine of the collection – but every pack costs money. The bot already puts packs first (no expansions or nice-to-have licenses while stock is low) and spreads packs so all openers keep working. The dashboard shows the pack usage per minute and how long the stock lasts. If the stock still keeps falling, your income can't feed that many openers yet: place fewer openers or raise the daily limit.
</details>

<details><summary><b>I want to run the register myself / use cashiers.</b></summary>

Dashboard → **Shop & prices → Daily routine**: turn off "Bot serves all free registers". The work plan then assigns cashiers again. Registers where you stand are always left alone.
</details>

<details><summary><b>How do I uninstall it?</b></summary>

Delete the folder `BepInEx\plugins\ShopBot`. Optionally delete `BepInEx\config\ratta.shopbot.cfg`. Your save stays playable.
</details>

## ⚠️ Notes

- **Back up your save before the first start** (`%USERPROFILE%\AppData\LocalLow\OPNeonGames\Card Shop Simulator`).
- The bot deeply interacts with the game (spends money, reassigns workers, sells cards). Set in the dashboard what it may do.
- Unofficial fan project, not affiliated with OPNeon Games, the Nexus mod authors, PSA or The Pokémon Company.

## Building it yourself (developers)

```
git clone https://github.com/AndrejProgrammer/TCG-ShopBot.git
cd TCG-ShopBot
copy GameDir.local.props.example GameDir.local.props   # enter the path to your game folder
dotnet build -c Release
```
Needs the **.NET SDK 8**. After building, the DLL is copied into `BepInEx\plugins\ShopBot` automatically (turn off with `-p:CopyToGame=false`).
Release ZIP: `powershell -File tools\release.ps1` → `dist\ShopBot-x.y.z.zip`.
Translations: `node tools\i18n-keys.js` refreshes `lang/en.txt` and lists missing keys per language.
Developer notes (German): [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md) · Changes: [CHANGELOG.md](CHANGELOG.md)

## License

[MIT](LICENSE) – free to use, change and share; keep the copyright notice.
