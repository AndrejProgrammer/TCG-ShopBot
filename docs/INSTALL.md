# ShopBot – step-by-step guide

This guide takes you from zero to a running bot. Takes about 10 minutes.
🇩🇪 [Deutsche Anleitung](ANLEITUNG.md)

**Contents**
1. [Find the game folder](#1-find-the-game-folder)
2. [Back up your save](#2-back-up-your-save)
3. [Install BepInEx (if you don't have it yet)](#3-install-bepinex)
4. [Recommended mods](#4-recommended-mods)
5. [Install ShopBot](#5-install-shopbot)
6. [First start](#6-first-start)
7. [The dashboard (F9)](#7-the-dashboard-f9)
8. [Recommended settings](#8-recommended-settings)
9. [Playing with the bot](#9-playing-with-the-bot)
10. [Troubleshooting](#10-troubleshooting)
11. [Update & uninstall](#11-update--uninstall)

---

## 1. Find the game folder

1. Open **Steam** → **Library**.
2. Right-click **TCG Card Shop Simulator** → **Manage** → **Browse local files**.
3. The game folder opens, e.g.
   `C:\Program Files (x86)\Steam\steamapps\common\TCG Card Shop Simulator`

It contains `Card Shop Simulator.exe`. **You'll need this folder in a moment.**

## 2. Back up your save

The bot spends money, reassigns workers and sells cards. Back up your save first:

1. Press `Windows key + R`, enter `%USERPROFILE%\AppData\LocalLow\OPNeonGames\Card Shop Simulator` → **OK**.
2. Copy the whole folder somewhere safe (e.g. Desktop → "Save backup").

## 3. Install BepInEx

> **Using the Real TCG Overhaul mod pack / mod manager?** Then BepInEx is already there → continue with [step 4](#4-recommended-mods).
> Check: is there a folder **`BepInEx`** with a subfolder **`plugins`** in the game folder? Then it's installed.

1. Download the latest **BepInEx 5.4.x** for **win_x64** from [github.com/BepInEx/BepInEx/releases](https://github.com/BepInEx/BepInEx/releases) (e.g. `BepInEx_win_x64_5.4.23.x.zip`). **Not** BepInEx 6.
2. Extract the ZIP **into the game folder**. Afterwards it contains `winhttp.dll`, `doorstop_config.ini` and the folder `BepInEx`, among others.
3. **Start the game once** until the main menu and quit again. Now `BepInEx\plugins` and `BepInEx\config` exist.

## 4. Recommended mods

ShopBot also runs without them – then the matching features are simply off. For the full package:

| Mod | What the bot uses it for |
|---|---|
| **Real TCG Overhaul** (Nexus Mods, mod 577) with *Enhanced Prefab Loader* | Pokémon products, sets and the Pokémon filter |
| **Collection Tracker** | Reading the collection, buying packs, opener boost |
| **CardGrade Overhaul** | Grading at PSA, opening packages |
| **Upgrades** | Collection point upgrades |
| **ConfigurationManager** (BepInEx) | Settings with **F1** |

The easiest way is the **Real TCG Overhaul Mod Manager** – it brings everything.

## 5. Install ShopBot

**Easiest:** download the latest **`ShopBot-x.y.z.zip`** from **Releases**, extract it anywhere (e.g. Downloads) and double-click **`Install-ShopBot.bat`**. It finds the game via Steam (also on other drives), checks BepInEx, copies the mod, unblocks it and shows which recommended mods are missing. Done – continue with step 6.

**By hand:**
1. On the repo page click **Releases** on the right and download the latest **`ShopBot-x.y.z.zip`**.
2. Open the ZIP. It contains the folder `BepInEx\plugins\ShopBot\ShopBot.dll`.
3. **Drag the `BepInEx` folder from the ZIP into the game folder** and confirm "replace/merge files".
4. Check: you now have
   `…\TCG Card Shop Simulator\BepInEx\plugins\ShopBot\ShopBot.dll`

> Windows sometimes blocks downloaded DLLs: right-click `ShopBot.dll` → **Properties** → tick **"Unblock"** at the bottom → OK.

## 6. First start

1. Start the game and **load your save**.
2. A small status line appears at the top left ("Shop loaded – bot starts in 10 s").
3. After 10 seconds the bot gets going: adjusting prices, paying bills, work plan, purchasing …
4. Press **F9** → the dashboard shows what is happening. Want another language? Click **EN ▼** at the top right.

**Did it work?** Open `BepInEx\LogOutput.log` (in the game folder) with Notepad. It must contain:
```
[Info   :   BepInEx] Loading [ShopBot 0.7.0]
[Info   :   ShopBot] ShopBot 0.7.0 loaded. F8 = on/off, F9 = dashboard. Language: English
```

## 7. The dashboard (F9)

| Page | What you see / set |
|---|---|
| **Overview** | Money, revenue & costs today, purchases vs. daily limit, sets, grading, openers, pack stock with usage and range, quick actions, latest actions, daily report |
| **Collection** | Goal (all cards, pace, days left), target sets with progress, all Pokémon sets, stock, daily limit, emergency restock, "packs first" |
| **Shop & prices** | Markups, purchasing, reviews, daily routine (**bot checkout**), expansions, self-stocking, licenses, sorting boxes |
| **Customers** | Buy limits, haggling, swap rules |
| **Cards** | Grading (PSA), selling via card storage, pack openers (even spreading, minimum portion, turbo) |
| **Workers** | Live work plan (role, speed, bonus), number of cashiers/stockers, hiring |
| **History** | All bot actions, filterable |
| **Settings** | Language, dashboard size, status line, hotkeys, timing |

All values are saved automatically (when closing the dashboard, when quitting, every 30 s). You find the same settings with **F1** in the **ShopBot** section – or as the file `BepInEx\config\ratta.shopbot.cfg`.

## 8. Recommended settings

The defaults fit most players. Depending on your save:

| Situation | Setting |
|---|---|
| **New save / little money** | *Collection → Purchase limit per day* to $5,000–$20,000; *emergency restock* off |
| **Lots of money, fill the collection fast** | High daily limit or "Limit grows with: % of money" at 10 % |
| **You want to run the register yourself** | *Shop & prices → Bot serves all free registers* off |
| **Only automate some things** | Turn off single switches – e.g. only checkout + prices |
| **Later: all cards as PSA 10** | *Cards → PSA 10 collection* on |

**Important for the full flow:**
- Place enough **pack openers** – they are the engine of the collection. But only as many as your income can feed with packs (the dashboard shows how long the pack stock lasts).
- Place a **trash bin** and **storage shelves** (for boxes).
- Place a **card storage** and **card display cases** – that's how the bot sells duplicate and graded cards.

## 9. Playing with the bot

- You can play normally alongside it. The bot only opens **windows** (customer offers, grading packages) when you **haven't pressed anything for 8 seconds**.
- Registers where **you** stand are left alone.
- In the evening from 9 PM it ends the day as soon as the last customers have paid.
- **F8** switches everything off and on instantly.

## 10. Troubleshooting

| Problem | Solution |
|---|---|
| No status line, F9 does nothing | DLL in the wrong place? (see step 5) · BepInEx installed? · DLL "unblocked"? · Check the log |
| `Loading [ShopBot]` missing in the log | BepInEx isn't loading: BepInEx **5** (not 6), x64, extracted into the game folder? |
| "Collection Tracker not found" in the log | Mod missing → collection features are off. Install the mod. |
| Bot buys no packs | Daily limit reached? Low on money? License for the set missing? → dashboard *Collection* |
| Openers often run empty | See the pack stock card: usage per minute and range. Fewer openers or a higher daily limit help. |
| Grading packages stay on the floor | Search the log for `[Grading package] not opened:` – the reason is there |
| Customers pile up at the register | Dashboard → raise bot checkout "items per second" |
| Something went wrong | **F8** (bot off), restore the save backup, send the log file |

**Report a bug:** please send `BepInEx\LogOutput.log` (right after the problem, before the next game start) and a short description – on the repo page under **Issues**.

## 11. Update & uninstall

**Update:** download the new ZIP and copy it over like in step 5 (replace files). Your settings are kept – even the ones from older versions with German setting names.

**Uninstall:** delete the folder `BepInEx\plugins\ShopBot` (optionally also `BepInEx\config\ratta.shopbot.cfg`). Your save stays playable.
