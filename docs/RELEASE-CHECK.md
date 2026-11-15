# Pre-release check: VetPOS 1.0.0-beta.1 (14 Nov 2026)

## What was checked

| Area | Result |
|---|---|
| Secrets in source and build (API keys, passwords, tokens) | None found |
| Test data / sample products in the release | Only when the shop ticks "Add 24 sample products" at first start |
| Debug prints, TODOs, test buttons | None found |
| Fresh install | Data folder and database created on first start; database updates applied by themselves |
| Reinstall / update | Program in `%LocalAppData%\VetPOS.App`, data in `%LocalAppData%\VetPOS`: never touched; backup before every update |
| Uninstall | Removes program and shortcuts; data kept unless "also delete" is ticked (copy to Documents first) |
| SQL injection | All database access is parameterised (EF Core); the only hand-written SQL is three fixed PRAGMA commands |
| Passwords / keys | PINs: salted PBKDF2 hashes only; AI key: Windows DPAPI; 5 wrong PINs pause sign-in and unlock |
| Error handling | One safe wrapper for all screen actions, last-chance handler shows the real message, background jobs only log, plain-language messages for known Windows / disk / printer / network / file problems, reference numbers, daily log files (30 days) |
| Input validation | Services validate names, prices, quantities, categories, users, settings ranges; money rejects negatives, >2 decimals and huge amounts; text boxes limited to 300 characters (12,000 for multi-line) |
| Tests | 185 passing; build has 0 warnings (warnings are treated as errors) |

## What was fixed in this check

1. **Uninstall "also delete data"** could have deleted a whole folder if `VETPOS_DATA` pointed at a
   broad folder. It now deletes only a folder that holds nothing but VetPOS's own files.
2. **No length limits on text boxes.** Added: 300 characters (one line), 12,000 (multi-line).
3. **AI message limit** raised from 2,000 to 8,000 characters (pasted product lists), with a clear
   message when longer.
4. **Unused code removed:** the old "Add products with AI" reader, Bill editor tab switch, three
   unused styles, the old splash picture.
5. **Animated loading window:** logo with a soft pulse, name, moving bar and live status; closes by
   itself; becomes an error page (reason, reference, "Open logs folder") if start-up fails.
6. **New tests:** search ranking from the first letter, `%` / `_` searched as plain text.
7. **Version 1.0.0-beta.1**, [CHANGELOG.md](../CHANGELOG.md), README, quick start, feedback form.

## Agreed requirements: status

Done: whole-number quantities with pack sizes as products · price change on a bill (F4 or click) and
logged · line discounts (Rs each / Rs line / %) and bill discount (Rs / %), never below zero · new
delivery price question · supplier and invoice optional · expiry removed · medicine details ·
search from the first letter and inside words · bigger suggestion list · payment with Rs / % discount,
exact card / bank, card + cash, quick notes, no underpaid bills · past bills (reprint / change) ·
labels and barcode generator · notifications · activity log · reports with PDF / Excel · receipt
designer at real size with logo · AI everywhere (questions, links, owner changes with Confirm) ·
left-side menus (no top tabs) · slim, expandable sidebar · redesigned sign-in, lock and setup ·
colour themes and light / dark · logo everywhere · uninstaller · Enter moves to the next field ·
remove product · automatic updates (GitHub or folder) · backups and restore · support file.

## Unfinished, untested or risky

| Item | Risk | What to do |
|---|---|---|
| **Not code-signed** | Windows Smart App Control blocks VetPOS; SmartScreen asks "Run anyway" | Use PCs with Smart App Control off for the beta; buy a code-signing certificate (or Microsoft Store) before wide release |
| GitHub automatic updates | Built but never tried end to end (no release published yet) | Push, publish 1.0.0-beta.1, then publish beta.2 and watch an installed copy update |
| Receipt printing on a real thermal printer | Not tested by me (only the test print path in code) | Test with the shop's printer on day one |
| AI with real keys | Built and tested with simulated answers; real services vary | Run Settings → AI → Test AI on the shop PC |
| Look of every screen in dark mode × 7 colours | Not checked visually on Windows by me | Quick click-through in the VM |
| Windows 10 (older builds) | Only tested on Windows 11 in the VM | Try one Windows 10 PC if the shop has one |
| Visual C++ runtime | Setup downloads it if missing (needs internet once) | Install with internet the first time |
| One till only | No multi-PC / network database | Ask the client if they need a second till |
| Not built yet | Customers / credit sales, supplier payments, returns as a separate flow | Collect in the feedback form |

## Tell the client before testing

* It is a **beta**: use it alongside the current way of working for the first days.
* Install on a PC where **Smart App Control is off** (Windows Security → App & browser control).
  If Windows says "Windows protected your PC", choose *More info → Run anyway*.
* Owner first jobs: shop details and logo, receipt printer, AI key (Test AI), colour.
* Backups run every day by themselves; still copy the backups folder to a USB stick weekly.
* When something goes wrong: note the reference (E-…), and send **Settings → About → Save a support file**.
* Fill in [FEEDBACK.md](FEEDBACK.md) after a few days of normal use.
