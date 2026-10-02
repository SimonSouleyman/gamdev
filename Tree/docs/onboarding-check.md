# First session against the onboarding checklist (2026-09-29)

The role prompt's onboarding checklist, used as a design check on `main` (0.6). There are no
metrics and Simon is the only tester, so each line is judged from the flow in `main.gd` and the
first-time pages in `ui/pages.gd`. Gaps are for after the current 0.6.x plan.

The first session today: the shed (start menu) → outside at sunset, "planted" page → tap the
ground → "first night" page → the first root from the seed → "first root" page → sunrise, the
sapling in the dawn burst → "sapling" page → the first day → "sunset" page → the second night.

| Check | Today | Verdict |
|---|---|---|
| Core verb within 30 s of first control | The first control is the dive, then steering the root. Before it: the first start's loading (about a minute on the phone), the shed, and two pages. | Partly. Loading is outside design; the pages are the gap. |
| First success guaranteed | A seed's 20 life force always buys a first root; in seeded runs even a 1 m first root grew a sapling at dawn (3 seeds). | Yes. |
| Each new mechanic in a safe, low-stakes context | Nothing can be lost, so every step is safe. But the "sapling" page introduces five things at once (boost, the trade, sun steering by hour, the pills, orbit and zoom), and the "first night" page four (dot colours, stick and dive, costs, ending early). | Partly: safe, but too much at once. |
| At least one mechanic found by exploring, not by text | Dragging the sun, finds underground, visitors, the living clearing, the shed objects answering a tap. | Yes. |
| First session ends on a hook | The first sunrise shows the sapling grown from the player's own root, the diary writes the first line, the album gets its first photo. Nothing says what tomorrow may bring. | Mostly. |

## Gaps (after the current plan)
1. **Split the long first pages.** Teach one thing per moment: the dot colours when the root
   first nears a dot, "end root here" when half the tank is spent, boost at the first sapling,
   sun steering after the first boost, the pills when a nutrient first runs short. Each stays a
   journal page or a small scrap, shown once.
2. **Let the first root start sooner.** The "first night" page could shrink to one line
   ("steer toward the glow") with the rest arriving in the moments above.
3. **A calm hook at the end of the first day.** The evening diary line hints at what may come
   ("the first leaves might unfold tomorrow"), with no timer and no pressure. The same could hold
   for the "while you were away" page.
4. **Care signals and the 0.7 nightly glow need their own first-time moments** when they land,
   in the same one-thing-at-a-time way.

# After 0.8.2: the first-time check (2026-10-02, specs/root-field-extras.md 5)

Simulated from the code's flow on a fresh install (no tester yet); details and the table in
`docs/notes/field-0.8.2.md`. What happened:
- Hold to fast-forward: one line, at the right moment (the first day), but the last line of a
  page that teaches four things.
- Side roots: taught on the first-night page before any root grows (too early); the settle line
  now says "The leftover grows side roots." when it happens (fixed in 0.8.2).
- The wider field: nothing said that a far wish may take a root over two nights; the far wish's
  diary line now says "far" (fixed in 0.8.2).
- The far view, rock bands and soft veins: one line each, when they first matter (the far view's
  line from the 4th pick night until it is opened; the band and vein lines on first contact).
- Still open: the long first pages (gaps 1 and 2 above).
