# VetPOS

Offline point-of-sale for a veterinary shop in Sri Lanka: pet food, farm & livestock feed, animal
medicine and accessories. Native Windows app (.NET 10 LTS, WPF, SQLite), keyboard-first, with medicine
details (generic name, strength, form, species), a receipt designer, reports with PDF/Excel export, backups and an AI
assistant that reads the shop's own data.

**Version 1.0.0-beta.3** · [Changelog](CHANGELOG.md) · [Quick start for the shop](docs/QUICK-START.md) · [Feedback form](docs/FEEDBACK.md) · Plan: [docs/PLAN.md](docs/PLAN.md) · Design: [docs/DESIGN.md](docs/DESIGN.md)

> **Beta note:** VetPOS is not code-signed yet. On PCs with Windows *Smart App Control* switched on,
> Windows blocks it. Install on PCs where it is off (Windows Security → App & browser control).

## Install on the shop PC (Windows 10 / 11)

1. Run **`VetPOS.App-win-Setup.exe`**. No administrator password and no .NET install needed: the
   .NET runtime is inside VetPOS, and every update package carries it too.
   Setup checks the PC and installs anything missing (the Microsoft Visual C++ runtime) by itself,
   then adds VetPOS to the Start menu and the desktop and opens it. VetPOS itself checks for
   Windows 10 (1809+) / 11, 64-bit, and warns when the disk is almost full.
2. First start: enter the shop name and the owner's name, username and PIN.
   Tick *Add 24 sample products* to try the app with test data.
3. Sign in: click your face, type your PIN on the keyboard or the round keypad, Enter. Five wrong PINs
   pause sign-in for a short time (also on the lock screen).
4. Owner, first jobs: Settings → Shop (name, address, **logo**), Settings → Receipt printer,
   Settings → AI (paste a free Groq or Gemini key, **Test AI**), Settings → Appearance (colour).

**Where things are**

| What | Folder |
|---|---|
| Program (replaced by updates) | `%LocalAppData%\VetPOS.App` |
| Shop data (never touched by install, update or uninstall) | `%LocalAppData%\VetPOS` |
| Database | `%LocalAppData%\VetPOS\vetpos.db` (SQLite, WAL) |
| Backups | `%LocalAppData%\VetPOS\backups` |
| Logs (one file per day, 30 days) | `%LocalAppData%\VetPOS\logs` |
| Logo | `%LocalAppData%\VetPOS\assets\logo.png` |

Set the `VETPOS_DATA` environment variable to keep the data somewhere else.

## Daily use — keys

| Key | Billing |
|---|---|
| scan / type + Enter | add item (the list shows from the first letter: ↑↓ Enter) |
| ↑ ↓ · + − · Del | choose line · one more / less · remove |
| F2 | back to the scan box |
| F3 / F4 / F5 / F6 | quantity / price (this bill only) / line discount / bill discount |
| F7 | past bills: find, check, print again or change a saved bill |
| F8 / F9 | hold bill / recall held bill |
| F10 | reprint last bill |
| F12 | pay & print (C cash, K card, B bank; card/bank start at the exact amount; part card + part cash: type the card amount, Enter, then the rest in cash; bill discount with a Rs / % switch) |
| Ctrl+N | new bill |

Everywhere: **Ctrl+K** find anything · **Ctrl+1…8** Billing, Stock in, Products, Reports, Bill editor,
Settings, AI, Labels · **Ctrl+J** AI panel on any page · **F1** all keys · **Ctrl+L** lock (bill stays) · **Ctrl+Shift+L** log out ·
**Esc** close/back · **Ctrl+S** save on forms.

**Line discount (F5):** `100` = Rs 100 off **each** unit · Tab switches to *Rs off the whole line* and
*%* · `10%` = 10 %. Bill discount (F6): Rs or %. A discount can never take a line or the bill below
Rs 0.00. Every price change and discount is saved with who, when, old → new
(Reports → Discounts & prices).

**New delivery at a new price** (Stock in): if old stock is still on the shelf, VetPOS asks:
change all stock to the new price · keep the old price until the old stock is sold · ask at billing.

## More screens

* **Labels (Ctrl+8):** price & barcode stickers on A4 sheets (24 / 40 / 65 per page) or a label-printer
  roll (50×25, 38×25 mm). Products without a barcode get a valid in-store EAN-13 with one click.
* **Notifications bell:** low and out-of-stock items, a missed backup,
  bills held too long, today's price changes and discounts. Click one to go straight to it.
* **Activity log** (Reports → Activity log, owner): every sign-in, sale, stock change, setting and
  backup, with who and when; searchable and exportable.
* **AI (Ctrl+J on any page, or ✦ in the menu):** ask about sales, stock and profit. The owner can also
  ask it to **create products** ("kitten food 1kg, 3kg, 5kg from 1899, each 2000 more"), **change a
  price**, **add stock**, switch a product off or **remove** it: it shows a card and nothing is saved until
  **Confirm**. In the Bill editor it changes the receipt design (Ctrl+Z undoes it). Double-click outside
  the AI drawer, Esc or Ctrl+J closes it.
* **Products → Remove** (owner): deletes a product that was never used; a product with bills or stock
  is taken off sale instead (old bills keep it; switch it on again under *Switched off*).
* **Logo:** Settings → Shop or Bill editor. It shows on receipts, the sign-in screen, the menu, the
  window and taskbar, the desktop shortcut and the loading screen.
* **Colours:** Settings → Appearance: Teal, Blue, Green, Purple, Rose, Amber or Graphite (always a bright, light screen).
* **Support:** every error message has a reference like `E-7K2Q`. Settings → About → *Save a support
  file* puts the logs (no bills, no PINs) in a zip on the Desktop.

* **Bill editor:** add pieces from the tiles on the left, click any line on the real-size paper to
  change it, click the grey area for shop details and logo; the AI box is always at the bottom right.
* **Uninstall:** Settings → About, updates & uninstall (or Windows *Installed apps*). The shop data is
  kept unless you tick *also delete the shop data* (a copy goes to Documents first).

## Backups and restore

* A backup is made **every day automatically** (and checked), plus **Settings → Backups → Backup now**.
  The newest 30 are kept. Optionally a second copy goes to a USB drive or another folder.
* **Restore:** Settings → Backups → *Restore* next to a backup (or *Restore from another file…*).
  The current data is backed up first, then VetPOS restarts with the restored data.
* If the database file is ever damaged, VetPOS offers to restore the newest backup when it starts;
  the damaged file is kept for support.

## Updates

VetPOS updates itself. Every 2 hours (and 30 seconds after sign-in) it looks at the project's
GitHub releases (<https://github.com/abdullah-alhar/pos-ph/releases>), downloads a new version
quietly, and shows **“VetPOS x.y.z is ready”** at the bottom left: *Restart now* (backup first, about
a minute) or *Later* (it installs by itself when VetPOS next closes). Settings → About, updates &
uninstall → *Check now* does it at once. A shared folder or another web address can be set there
instead of GitHub (for shops without internet). No .NET install is needed, ever: it is inside VetPOS.

**Publishing a new version** (always a higher number than the last one):

* GitHub: `git tag v1.0.0-beta.2 && git push origin v1.0.0-beta.2`, or Actions → *VetPOS release* → Run workflow.
  The workflow tests, builds, packs and creates the release.
* From this Mac: `GITHUB_TOKEN=… ./build/publish-github.sh 1.0.0-beta.2`.
* Without GitHub: `./build/release.sh 1.0.0-beta.2` and copy `publish/releases` to the shared folder.

## Build (developers)

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet build VetPos.slnx
dotnet test                                   # 185 tests: money, discounts, payments, stock, checkout, search, medicine, reports, backups, AI (answers, changes, errors), receipts, barcodes
dotnet run --project src/VetPos.App           # Windows only; Debug adds sample products to a new database
```

The app builds on macOS/Linux too (for CI and tests) but only runs on Windows.

**Installer + update packages**

```
./build/release.sh 1.0.0-beta.3               # macOS / Linux
powershell -File build\release.ps1 -Version 1.0.0-beta.3   # Windows
```

Output in `publish/releases`: `VetPOS.App-win-Setup.exe` (give this to the shop),
`VetPOS.App-win-Portable.zip`, and the `*.nupkg` / `releases.win.json` files to upload to the update
location. Releases are not code-signed yet (Windows SmartScreen asks once: *More info → Run anyway*;
Smart App Control blocks it). To sign: `SIGN_TEMPLATE='jsign … {{file}}' ./build/release.sh …`, or the
`JSIGN_OPTIONS` secret for the GitHub workflow.

**Database changes**

```
dotnet ef migrations add <Name> -p src/VetPos.Data -s src/VetPos.Data -o Migrations
```

The app applies new migrations by itself on start. Bills, bill lines, payments, stock movements and
the audit log are protected by SQLite triggers: they can never be changed or deleted (bills are voided).

## Projects

| Project | What it holds |
|---|---|
| `src/VetPos.Core` | Entities, money, discounts, payments, PIN hashing. No UI, no database. |
| `src/VetPos.Data` | EF Core + SQLite, migrations, services (auth, catalog, stock entry, sales, reports, backups). |
| `src/VetPos.Printing` | Receipt design model, templates, renderer, design check, ESC/POS encoder. |
| `src/VetPos.AI` | OpenAI-compatible client with tool calling, read tools + owner change tools (confirmed cards), assistant + AI log. |
| `src/VetPos.App` | WPF app: design system, shell, screens, printing to the Windows spooler, PDF/Excel export. |
| `tests/*` | xUnit v3; run on any OS. |
