# Small sim for 0.8.2.2 roots (design thread, 2026-10-02)

Short run only (Simon, 2026-10-01: no full sweeps while prototyping). Branch s0822-tree at
5c893fa, `tools/strategies.gd`, linden, seed 14, headless in the cloud.

## What happened

| Style | Finished | Night run cost (sim, avg / max ms) |
|---|---|---|
| wish (glow from the newest tip) | day 29 | 390 / 901 |
| dots (steering to deposits) | day 30 | 411 / 605 |
| end_early (ended at once every night) | day 36 | 51 / 95 |
| straight_down (never steered) | day 38 | 476 / 1162 |

The slowest frame stayed under 35 ms in every run. The "wishes reached" counter printed more
reached than shown (73/8, 112/6), so it no longer counts correctly with the 0.8.2 reached flag.
This is a tool problem, not a game one.

## What it means

Ending early still loses: 6 days behind steering (acceptance.md asks for at least 4), and it
finishes by day 40. Never steered stays within the limit (day 38). The build thread's reading
of "small roots from every root" (main and fine roots, not older side roots) holds the balance;
with side roots included, ending early had reached day 33. The nightly sim cost about doubled
against 0.8.1 (dots 314 to 411 ms on average, straight_down's longest night 187 to 1162 ms),
probably from the detour check around old roots. The PC thread should check that the end of
the night does not hitch on the phone.
