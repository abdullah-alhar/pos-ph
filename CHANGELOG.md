# Changelog

## 1.0.0-beta.2 (15 Nov 2026): shop feedback round 1

From the first feedback: staff wanted a brighter, easier screen, a better sales view, and expiry dates.

### Easier to use
- **Bright look only**: dark mode removed. White menu where every page has its own colour and a
  large picture tile (like a phone), so pages can be found without reading.
- Bigger text everywhere (16 px body), bigger buttons (46 px), bigger icons.
- Big bill total on a bright colour panel.

### Sales report with a graph
- Reports → Sales: one tap for **Day / Week / Month / Year** or **Pick dates**, with arrows to go
  back and forward.
- **Bar graph** of sales (by hour for a day, by day, or by month for long ranges); point at a bar for
  the exact amount and number of bills.
- The sales total shows **up or down compared with the period before** (green / red).
- **One product's sales**: type or scan a product in the search box on Reports → Sales to see only
  its sales, units sold, profit and graph for any day / week / month / year. "All products" goes back.
  Clicking a product in Today or Profit opens its graph too.

### Expiry dates
- **New product**: type the stock you have now and its **expiry date**.
- **Stock in**: an expiry date for every line, filled in from the last delivery; change it if the
  new stock has a different date. Dates that have already passed are refused.
- **Products**: each batch shows its expiry (green / amber / red) with **Change expiry date**.
- Product list shows **Expires soon** and **Expired stock** badges.
- Reports → **Low stock & expiry**: what expires in the next 60 days and what has already expired.
- Billing already sells the stock that expires first, and never sells expired stock.

## 1.0.0-beta.1 (14 Nov 2026): first version for the shop to try

The first complete version, for the client's feedback round. Earlier numbers (0.1 to 0.8) were
internal test builds.

### Selling
- Scan or type: suggestions from the first letter, best matches first, large rows with stock and price.
- Quantity (F3), price for this bill only (F4 or click the price), line discount (F5: Rs off each,
  Rs off the line, or %), bill discount (F6).
- Payment (F12): Rs / % bill-discount switch, card and bank filled with the exact amount, card + cash
  split, quick cash notes, change shown large. A bill can never be saved unless it is fully paid.
- Hold / recall (F8 / F9), reprint (F10), **past bills (F7)**: find, check, print again, or change a
  saved bill (old one cancelled and kept; owner PIN for cashiers).

### Stock and products
- Stock in with optional supplier and invoice number; new-price question (change all / keep old / ask).
- Products with pack sizes, medicine details (generic name, strength, form, animals), barcode
  generator, labels (A4 sheets and label printers).
- **Remove product**: deleted if never used, otherwise taken off sale (old bills keep it).

### AI assistant (Ctrl+J, any page)
- Answers from the shop's own data (sales, stock, profit, products), with links to the right screen.
- **Makes changes for the owner** (create products, change price/cost/name/category, switch on/off,
  add stock, remove products) as a card; nothing is saved until **Confirm**.
- Bill editor AI ("make the total big"), Groq / Gemini / OpenRouter / Mistral / Cerebras / GitHub /
  Ollama; **Test AI** checks key, model and data look-ups.

### Look and feel
- New design (IBM Plex Sans), slim sidebar that expands (Ctrl+B), left-side section menus.
- **Colour themes** (Teal, Blue, Green, Purple, Rose, Amber, Graphite) plus light / dark.
- Shop logo on receipts, sign-in screen, menu, window, taskbar, desktop shortcut and loading screen.
- Sign-in with faces and a round PIN keypad; lock screen (Ctrl+L).
- **Animated loading window** with live status; a clear error page if start-up fails.
- Receipt designer (Bill editor) at real paper size.

### Safety and support
- PINs stored only as salted hashes (PBKDF2); 5 wrong PINs pause sign-in and unlock.
- AI key encrypted with Windows DPAPI. Bills, payments and stock history cannot be edited or deleted.
- Daily automatic backups (30 kept), restore, damaged-database recovery.
- Clear messages for every known problem (disk full, printer, internet, files in use, …) with a
  reference number; daily log files (30 days); **Save a support file** (Settings → About).
- Automatic updates from GitHub releases (or a shared folder), with a backup before each update.

### Known limits in this beta
- VetPOS is not code-signed yet: on PCs with Windows **Smart App Control** switched on, Windows
  blocks it. Use PCs where it is off until the signed version.
- Setup downloads the Microsoft Visual C++ runtime if the PC does not have it (needs internet once).
