# VetPOS design system

Source of truth: `src/VetPos.App/DesignSystem/`.
Screens use only these tokens and styles — no hex colours, no one-off sizes.

## Principles

1. **Content first, chrome quiet.** Neutral surfaces; colour only for the main action and for meaning.
2. **One primary action per screen** (blue, `Button.Primary`). Everything else is secondary or subtle.
3. **Keyboard first.** Every action shows its key (`ds:Ds.KeyHint`). Focus is always visible (2 px accent ring).
4. **Instant.** Keyboard actions never animate. Only dialogs, toasts and press feedback move.
5. **Never blank.** Every list has an empty, loading and error state (`StateView`).

## Colour (`Colors.Light.xaml`) — "Bright counter"

Shop staff asked for a brighter, simpler screen, so there is **only a light look** (dark mode was
removed). White surfaces, dark text, one bright teal accent, and a **white sidebar where every page
has its own colour and a large icon tile**, like apps on a phone. Staff who do not read English well
find pages by colour and picture.

| Token | Value | Use |
|---|---|---|
| Background | #F4F7FB | Window |
| Surface | #FFFFFF | Cards, panels, sidebar |
| SurfaceSubtle | #EEF3F8 | Secondary buttons, hover, key chips |
| Line / LineStrong | #DFE6EE / #C5D0DC | Hairlines, input borders |
| Text / Secondary / Tertiary | #13202D / #3F4E5E / #6A7A8B | |
| **Accent (teal)** | #0A8A82 | Main action, focus, selection, bill total, sales graph |
| Success | #15803D | In stock, saved, sales up |
| Warning | #B45309 | Expiring soon, low stock, price changed |
| Danger | #D12E2E | Expired, error, void, sales down |

Page colours (menu tiles): Billing #16A34A · Stock in #2563EB · Products #EA7A0C · Labels #7C3AED ·
Reports #DB2777 · Bill editor #0891B2 · Settings #475569 · AI #0A8A82.

## Type

Inter (bundled, 4 weights) for everything, page titles included. Money uses tabular figures. Body text 16 px (was 14). Bill total: 52 px bold. Pay button: 70 px high.

## Spacing, size, radius, depth

- Spacing: 4 · 8 · 12 · 16 · 24 · 32 · 48. Page padding 32/24. Card padding 24.
- Heights: control 46, search 54, table row 58, Pay 70. Icons 19-24 px. Sidebar 240 (collapsed 68). Totals panel 360.
- Radius: small 6 · input/button 10 · card 14 · sheet 16.
- Depth: `Shadow.Card` (subtle), `Shadow.Sheet` (dialogs, toasts). Scrim only behind blocking dialogs.

## Motion (`Motion.cs`)

| What | How | Why |
|---|---|---|
| Button press | scale 0.97 in 100 ms, back in 160 ms | The UI heard you |
| Dialog in / out | fade + 97 % → 100 % in 180 ms; out in 120 ms, ease-out `0.23,1,0.32,1` | Leaving is faster than arriving |
| Toast | slides 8 px up + fade in 180 ms; leaves the same way; 3.5 s (errors 6 s; waits while hovered) | Same path in and out |
| Item added to bill | row tints accent and fades back over 0.9 s — colour only | Confirms a scan without slowing the next one |
| Invalid input | 3 × 4 px shake, 240 ms + message under the field | Error is noticed where it happened |
| Keyboard actions, page switch, command palette | **no animation** | Used hundreds of times a day |

When Windows "Show animations" is off, everything appears at once (shake is shortened, not removed).

## Components

`Button.Primary` / `PrimaryLarge` / `Secondary` (default) / `Subtle` / `Danger` · `Input` (default) /
`Input.Number` / `Input.Search` with `ds:Ds.Placeholder`, `ds:Ds.Icon`, `ds:Ds.KeyHint`, `ds:Ds.Error` ·
`Card`, `Sheet`, `Divider` · `Badge` / `.Success` / `.Warning` / `.Danger` / `.Accent` · `Table` +
`TableHeader` · `RailItem` · `StateView` (Empty / Loading / Error) · `IToastService` · `IDialogService`
(`ConfirmAsync`: Enter = yes, Esc = no).

## Layout targets

1366×768 and 1920×1080 at 100 / 125 / 150 % scaling. Smallest working area: 1093×614 (1366×768 @125 %).
Billing: lines flexible, summary panel fixed 360 px, total scales down (never clips) for very large bills.
