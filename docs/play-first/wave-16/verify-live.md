# Wave 16 live verification — brief for `verify`

Since your last round (wave 11) the app changed a lot; this round looks at what the user sees. The user
asked for it explicitly and is off the PC. Same harness and **all eight rules** in
`docs/play-first/wave-3/detail-live-1.md` (read them again), plus the watch-outs in
`docs/play-first/wave-10/verify-live.md` and `.trail/learnings/2026-11-20-desktop-overhaul-waves-11-15-select-unknown.md`.
The exe is built (Debug x64, this worktree); do not build. Restore right after the round.

**Screenshots are the point of this round**: take them with `live.py shot`, look at each one yourself, and
describe what you see. A layout that looks wrong (overlaps, things cut off, misplaced, blurry, empty areas)
is a failure even if UIA says the elements exist.

## Fixture
`live.py setup` + `h5.py add` as in wave 10. The character and outfit tabs need cards too (item 4): look in
the harness folder for an existing helper that writes character / coordinate cards into the fake library.
If there is none, write a small one under `live\` that copies the fake scene PNGs into the fake character
and coordinate folders (a PNG without card data is still listed as a file, which is enough for the tile
layout). Never use or read the user's real library. Say what you did.

## Checks (window at the default size, then 1920x1080 if TransformPattern resize works)
1. Navigation pane: exactly Home, Library, Authors in the top list; Import and Settings at the bottom.
   Library toolbar has a `GalleryMoreButton`; open it (Invoke / ExpandCollapse) and list its items.
   Screenshot `w16-library.png`, `w16-more.png`.
2. Select a scene tile (SelectionItem): the details pane (`CardInspector`) shows it; the tile has the
   accent frame. Ctrl-less multi-select via SelectionItem.AddToSelection on two more tiles: the pane shows
   the stacked cards and "N cards selected". Screenshot `w16-multi.png`.
3. Open a card in place: Invoke is not the gesture (double click); use the details pane's `CardInspector_Open`
   button after selecting a single tile. Then:
   - screenshot `w16-loupe-open.png` about 0.1 s after invoking (to catch the flight) and `w16-loupe.png`
     after 1 s;
   - the library toolbar and search row must NOT be visible; nothing may overlap the details pane;
   - the picture is centred in the stage, not blurry-huge (a 320x180 card shows at most 640x360 at 100 %
     scaling), the filmstrip is at the bottom, the header shows Back, position, zoom, pane toggle;
   - `LoupeNext` / `LoupePrevious` (they show on hover; if not reachable, use the filmstrip or say so) and
     `LoupeZoomIn`, `LoupeZoomLevel`: report the zoom label before and after;
   - `LoupeClose`: back to the grid, the toolbar is back, the same tile selected. Screenshot
     `w16-loupe-closed.png` 0.1 s after and `w16-after-close.png` after 1 s.
   Compare the flight screenshots: does the picture start / end on the tile's position?
4. Tiny tiles: switch the library to the character tab and the coordinate tab (the selector items) and take
   a screenshot 0.3 s and 2 s after each switch (`w16-chara-0.3.png`, `w16-chara-2.png`, `w16-coord-…`).
   Report tile sizes by UIA bounding rectangles of the first few `GridViewItem`s at both moments. Expected:
   never tiny (~38 px) tiles.
5. Random discovery (from the More menu): invoke a card → the card viewer page opens; `LoupeClose` goes back
   to random discovery. Screenshot `w16-viewer.png`.

## Report
Write `docs/play-first/wave-16/verify-live-report.md`: per item what you did, what you saw in each screenshot
(describe it), anything that looks wrong (be blunt), and the rule-7 checks. Reply with only `done`.
