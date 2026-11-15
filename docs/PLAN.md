# VetPOS — Version 1 plan

Native Windows POS for a veterinary shop in Sri Lanka (pet food, farm feed, animal medicine,
accessories). Offline, keyboard-first, premium UI.

Reference code: `~/Documents/project/pos` (ShopPOS web app + its `desktop/` WPF app). Proven C#
from `desktop/` is copied in and adapted; the receipt editor is ported from the web app
(`packages/shared/src/receipt-*.ts`, `apps/web/src/features/receipt-studio`).

---

## 1. Decisions (agreed with the owner)

| Topic | Decision |
|---|---|
| Quantity | **Whole numbers only.** Each pack size is its own product ("Whiskas Adult 1.2 kg", "Whiskas Adult 7 kg"). |
| Price change on a bill | Allowed for every user, **no PIN**. Always logged (user, time, old → new). A PIN rule can be added later as a setting. |
| Line discount | Per line, three ways: **Rs off each unit** (5 × Rs 100 off = Rs 500), **Rs off the whole line**, or **%**. Receipt shows original price, discount and the price after discount. |
| Bill discount | Rs or % off the bill total, after line discounts. |
| Discount limit | **None in v1** (no PIN). A discount can never take a line or the bill below Rs 0.00; the input is rejected with a clear message, never silently cut. Limit can be added later as a setting. |
| New selling price on stock entry | Each batch keeps its own selling price. If the new delivery's price differs and old stock is on hand, stock entry asks (3 options): **① Change all stock to the new price** · **② Keep the old price until the old stock is sold** (billing takes the FEFO batch's price by itself) · **③ Ask at billing** (the cashier picks the price each time). After an item is on the bill, staff can always change its price (F4, this bill only, logged). |
| Money | `decimal`, **Rs with cents, no rounding of the total**. One rounding rule: 2 decimals, half away from zero, in `Money.Round` only. |
| Selling above stock | Blocked by default; owner setting can allow it (logged). |
| Expired batches | Never sold. FEFO picks the batch that expires first. |
| Runtime | **.NET 10 LTS** (support to Nov 2028; .NET 11 is STS with the same end date and is one week old). Latest versions of every library. |

---

## 2. Tech stack

| Need | Choice | Why |
|---|---|---|
| UI | WPF on .NET 10, built-in **Fluent theme** (`ThemeMode`) + our own design tokens | Native, Windows 11 look, light/dark follow Windows |
| MVVM | CommunityToolkit.Mvvm | Source-generated, no boilerplate |
| DI / host | Microsoft.Extensions.Hosting | Services, options, logging in one place |
| Database | SQLite + EF Core 10, migrations from day one | One file, nothing to install |
| Logging | Serilog → `%LocalAppData%\VetPOS\logs` (rolling daily, 30 days) | |
| Tests | xUnit v3 | Money, discounts, FEFO, stock, profit |
| PDF reports | QuestPDF (Community licence: free under USD 1M revenue/yr) | Clean printable reports |
| Excel | ClosedXML (MIT) | Real `.xlsx` |
| Installer + auto-update | **Velopack** | One `Setup.exe`, per-user install, delta updates, no admin |
| Receipt printing | Own ESC/POS encoder (ported) → raw to Windows spooler (`winspool.drv`) | Works with any 80 mm thermal printer driver |
| Secrets | Windows DPAPI (`ProtectedData`, CurrentUser) | AI key never stored in plain text |

---

## 3. Solution layout

```
vetshop/
  VetPos.sln
  Directory.Build.props          version, nullable, warnings as errors
  Directory.Packages.props       central package versions
  src/
    VetPos.Core/                 no UI, no EF
      Money/        Money, Discount, CartCalculator, PaymentCalculator
      Stock/        FefoAllocator, StockRules
      Domain/       entities (POCOs), enums
      Rules/        DiscountPolicy, ValidationResult, PosException (cashier message + technical detail)
      Abstractions/ IClock, ICurrentUser, repositories/units of work interfaces
    VetPos.Data/                 EF Core
      VetPosDbContext, configurations, Migrations/, Seed/
      Services: SaleService, GoodsReceiptService, ProductService, ReportService,
                AuditService, BackupService, SettingsService, DraftBillStore
    VetPos.Printing/             receipt layout model, layout engine, ESC/POS encoder, spooler
    VetPos.AI/                   IAiProvider (OpenAI-compatible + Ollama), tool registry, agent loop
    VetPos.App/                  WPF
      DesignSystem/ Tokens (colours, type, spacing), Styles, Controls (Toast, Dialog, KeyHint, MoneyText, EmptyState …)
      Shell/        MainWindow, navigation, command palette, login, lock
      Features/     Billing, StockEntry, Products, Reports, BillEditor, Settings, Assistant
  tests/
    VetPos.Core.Tests            pure logic (runs on any OS)
    VetPos.Data.Tests            services against a real SQLite file
  docs/  PLAN.md  DESIGN.md
  README.md
```

---

## 4. Database (SQLite)

Every table: `Id`, `CreatedAt`, `UpdatedAt`, `CreatedById`. Times stored UTC, shown in shop time.
Money columns are `decimal` stored as TEXT (exact; SQLite has no decimal type).

| Table | Main columns |
|---|---|
| **User** | Username, FullName, Role (Owner / Cashier), PinHash (scrypt-style PBKDF2), IsActive, LastLoginAt |
| **Setting** | Key, JSON value (shop, receipt, printer, discount, backup, AI) |
| **Category** | Name, SortOrder (Pet food, Farm feed, Medicine, Accessories …) |
| **Product** | Name, Barcode (unique), CategoryId, Unit (piece/pack/kg bag/bottle …, label only), CostPrice, SellingPrice, ReorderLevel, TracksExpiry, IsActive |
| **ProductBarcode** | Extra barcodes for one product |
| **Supplier** | Name, Phone, Address, IsActive |
| **GoodsReceipt** | Number (GRN-0001), SupplierId, SupplierInvoiceNo, InvoiceDate, Total, Note |
| **GoodsReceiptLine** | ProductId, Qty, CostPrice, SellingPrice, BatchNo, ExpiryDate? |
| **StockBatch** | ProductId, GoodsReceiptLineId, BatchNo, ExpiryDate?, CostPrice, **SellingPrice**, QtyReceived, **QtyOnHand** |
| **StockMovement** | ProductId, BatchId?, Change (±), QtyAfter, Reason (Receipt, Sale, Void, Adjustment), Ref — append-only |
| **Sale** | Number (running, e.g. 1042), Status (Completed / Voided), CashierId, Subtotal, LineDiscountTotal, BillDiscountType/Value/Amount, Total, Paid, Change, CostTotal, VoidedAt/ById/Reason |
| **SaleLine** | LineNo, ProductId, Name snapshot, Qty, ListPrice, UnitPrice, DiscountType (PerUnit / Line / Percent) / Value / Amount, BillDiscountShare, LineTotal, CostTotal |
| **SaleLineBatch** | SaleLineId, BatchId, Qty, UnitCost — which batch each unit came from (exact profit, exact void) |
| **Payment** | SaleId, Method (Cash / Card / Bank), Amount, Tendered, Reference |
| **HeldBill** | Label, Cart JSON, Total, UserId |
| **DraftBill** | One row per till: the bill in progress, saved after every change (power-cut safety) |
| **AuditLog** | Action, EntityType/Id, OldValue, NewValue, Summary, UserId, ApprovedById — append-only |
| **AiLog** | Question, ToolsCalled (JSON), Answer, Provider, Model, DurationMs, Error |
| **Backup** | FileName, Kind (Auto / Manual / PreRestore), SizeBytes, Verified |

Protection: SQLite triggers make `AuditLog`, `StockMovement`, `Payment`, `SaleLineBatch` append-only and
block DELETE on `Sale` / `SaleLine` (bills are voided, never deleted).

**FEFO:** at checkout, for each line: batches of the product with `QtyOnHand > 0` and
(`ExpiryDate` empty or ≥ today), ordered by `ExpiryDate` (empty last), then oldest receipt first.
Units are taken across batches as needed; each part is a `SaleLineBatch` row. Expired stock is
never chosen; if only expired stock is left the cashier sees *"Only expired stock left for X —
remove it from the shelf"*. Void puts units back into the same batches.

**Profit** = line total − Σ(batch qty × batch cost). Exact per batch, no averages.

---

## 5. Screens (v1)

| Screen | Main action | Notes |
|---|---|---|
| Login | Sign in | Username + PIN, big number pad optional, Enter to sign in |
| **Billing** | Pay & print (F12) | Opens after login. Scan box always focused. |
| Payment (sheet over billing) | Confirm | Cash with change, card, bank, mixed |
| Held bills (sheet) | Recall | List with label, time, total |
| Stock entry | Save receipt (Ctrl+S) | Supplier picker (add new inline), invoice no., grid of lines |
| Products | Add product (Ctrl+N) | List + search + filters (low stock, expiring soon); editor side panel |
| Reports | Export PDF / Excel | Daily summary, sales by date, profit, stock, low stock & expiry, discounts & price changes |
| Bill history (inside Reports) | Reprint | Find a bill, reprint, void (owner) |
| Bill editor | Save design | Pieces list, properties, live 80 mm preview, templates, check, test print |
| Settings | Save | Shop, printer, discount limit, users, backups, AI |
| AI Assistant | Ask | Chat; "AI is offline" state never blocks anything |
| Command palette (Ctrl+K) | Run | Jump to any screen or action by typing |

Every screen has designed **empty**, **loading** and **error** states.

---

## 6. Keyboard map

**Everywhere**

| Key | Action |
|---|---|
| Ctrl+K | Command palette |
| F1 | All shortcuts for this screen |
| Ctrl+1 … Ctrl+7 | Billing, Stock entry, Products, Reports, Bill editor, Settings, AI |
| Ctrl+L | Lock screen (bill stays) · Ctrl+Shift+L Logout |
| Esc | Close sheet / cancel / back |
| Enter | Next field in forms; confirm in dialogs (Enter = yes, Esc = no) |

**Billing**

| Key | Action |
|---|---|
| (type / scan) | Barcode → adds at once. Text → result list, ↑↓, Enter adds |
| `3*` then scan/Enter | Add 3 of the item |
| ↑ ↓ | Select bill line |
| + / − | One more / one less on the selected line |
| F2 | Back to search box |
| F3 | Change qty of selected line |
| F4 | Change price of selected line (this bill only) |
| F5 | Line discount: `100` = Rs 100 off each · Tab switches to whole line / % · or type `10%` |
| F6 | Bill discount |
| F8 | Hold bill |
| F9 | Recall held bill |
| F10 | Reprint last bill |
| F12 | Pay & print |
| Del | Remove selected line (Enter to confirm) |
| Ctrl+N | New bill (asks if the bill is not empty) |

**Payment sheet:** `C` cash · `K` card · `B` bank · type amount, Enter · `F12` exact cash and finish ·
mixed = enter card/bank part first, rest is asked as cash · Esc back to bill.

Fastest sale: scan ×3 → ↑ F4 `450` Enter → F5 `10%` Enter → F6 `100` Enter → F12 → `5000` Enter.
Prints, toast "Bill #1042 saved · change Rs 1,234.50", screen clears, focus in scan box.

---

## 7. Design system (summary — full spec in docs/DESIGN.md at step 2)

- **Principles:** content first, chrome quiet; one primary action per screen; instant feedback on
  key-down/press; every value deliberate (tokens only, no one-off sizes or colours).
- **Font:** Segoe UI Variable (Windows 11), Segoe UI fallback (Windows 10). Tabular numerals for money.
- **Type scale:** 12 caption · 13 secondary · 14 body · 16 emphasis · 20 title · 28 page title ·
  **48 bill total** (semibold, tight tracking).
- **Spacing:** 4 · 8 · 12 · 16 · 24 · 32 · 48. **Radius:** 6 inputs · 8 buttons · 12 cards · 16 sheets.
- **Colour:** warm neutrals; one accent (calm blue) for primary actions and focus;
  green = in stock / success, amber = expiring soon / warning, red = expired / error. Light & dark.
- **Depth:** two shadow levels (card, sheet) + dim scrim only for blocking dialogs.
- **Motion:** 150–200 ms, critically damped (no bounce). Sheets/dialogs fade + scale 0.98→1 from their
  origin; toasts slide 8 px + fade and leave the way they came; added line highlights for 600 ms;
  invalid input shakes 3 × 4 px. Respects Windows "Show animations" off.
- **Targets:** 1366×768 and 1920×1080, at 100/125/150 % scaling.

Billing at 1366×768:

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ ◉ VetPOS   Billing                            Ctrl+K  Search anything   Nimal ▾│
├────────────────────────────────────────────────┬─────────────────────────────┤
│ ⌕  Scan barcode or type a product name    F2   │  Bill #1043                 │
├────────────────────────────────────────────────┤                             │
│  #  Item                    Qty   Price   Total│  Subtotal        12,450.00  │
│  1  Royal Canin Kitten 2kg   1  5,450.00  5,450│  Line discounts     −545.00 │
│  2  Pedigree Adult 10kg      1  6,200.00  6,200│  Bill discount      −100.00 │
│▸ 3  Frontline Plus M         2    400.00    800│                             │
│        price changed 450 → 400 · −10%          │  TOTAL                      │
│                                                │  Rs 11,805.00               │
│                                                │                             │
│                                                │  ┌───────────────────────┐  │
│                                                │  │  Pay & print     F12  │  │
│                                                │  └───────────────────────┘  │
├────────────────────────────────────────────────┤  Hold F8   Recall F9        │
│ F3 Qty  F4 Price  F5 Discount  F6 Bill disc.  Del Remove   F1 All keys       │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## 8. Error handling

- Global handlers (`DispatcherUnhandledException`, `AppDomain.UnhandledException`,
  `TaskScheduler.UnobservedTaskException`) → Serilog + friendly message; the till stays open.
- `PosException` carries a cashier message ("Not enough stock: only 3 left"); the log gets the detail.
- Checkout = one transaction: sale, lines, batch allocations, stock, payments, audit.
  Re-saving the same bill (same client id) returns the first one, never a duplicate.
- Draft bill written to SQLite after every change; on start: "Restore the unfinished bill?"
- Printer failure → bill is saved, toast "Saved, but the printer did not respond — F10 to reprint".
- AI/internet down → assistant shows "AI is offline"; nothing else is affected.
- DB work is async; the UI never freezes.

---

## 9. AI assistant

OpenAI-compatible connector (Groq default, Gemini, OpenRouter, Mistral, Cerebras, GitHub, Ollama,
custom) — same as the reference, **native tool calling**, max 4 rounds.
Read-only tools v1: `get_sales_summary`, `get_top_products`, `get_low_stock`,
`get_expiring_items`, `search_product`, `get_profit`. System prompt: answer only from tool
results, say when data is missing, reply in the user's language (English / Sinhala / Tamil).
Each question, tool call and answer → `AiLog`. Key: DPAPI. Future change-tools return a
*proposal* the user confirms.

---

## 10. Build steps — all done (13 Nov 2026)

1. **Skeleton** — solution, packages, DI host, Serilog, crash handlers, single instance, SQLite +
   first migration, seed data. *Check: app opens an empty window, log file appears.*
2. **Design system** — tokens, styles, controls, light/dark, a "Design gallery" window + static
   **billing screen mockup**. *You review the look before any other screen.*
3. **Core money** — Money, CartCalculator, payments, discount policy + tests.
4. **Login, users, lock** — owner created on first start, cashier accounts, PIN.
5. **Products** — list, editor, categories, barcodes, low-stock / expiring lists.
6. **Stock entry** — suppliers, goods receipt with batches & expiry, FEFO allocator + tests.
7. **Billing** — scan/search, lines, F-keys, discounts, PIN above limit, hold/recall, draft restore.
8. **Payment + checkout** — cash/card/bank/mixed, transaction, audit, void.
9. **Receipt printing + bill editor** — ESC/POS, spooler, editor screen, live preview, templates.
10. **Reports** — the six reports, PDF + Excel export, bill history/reprint.
11. **Settings + backups** — shop, printer, limits, daily backup (keep 30), backup now, restore.
12. **AI assistant** — providers, tools, logging, offline state.
13. **Installer** — Velopack setup + auto-update, README (build, run, publish, install, backup, restore).
14. **Hardening** — full-day test script, 30-second sale check, 1366×768 / 150 % pass, dark mode pass.

---

## 11. Billing price rule (batches at different prices)

Product field `PriceChoice` = `OnePrice` | `OldFirst` | `AskAtBilling`, set by the stock-entry question;
goes back to `OnePrice` by itself when only one price is left in stock.

1. Scan → sellable batches (not expired, qty > 0) in FEFO order.
2. **OnePrice / ① Change all:** one price → line added at once.
3. **② Old first:** price of the FEFO batch; if the qty is more than that price has, the rest goes on
   a second line at the next price, with a notice. No question.
4. **③ Ask at billing:** picker shows each price · qty available · expiry; first row = FEFO batch,
   Enter takes it. The line sells only from batches at the chosen price (FEFO among them).
5. In every case **F4** changes the price of the line afterwards (this bill only, logged old → new).
