# Live picture and app icon (0.8, specs sections 6 and 7)

Build notes for `docs/specs/0.8.md` sections 6 (live picture: live wallpaper and screen saver,
one animation, Simon's choice) and 7 (a new app icon). Broken-list items 14b to 14g. Numbers
not yet felt on the phone are [PLACEHOLDER].

## Approach chosen: the game renders layers, a small native renderer animates them

Three ways were weighed:

| Way | Faithful | Cost on the phone | Verdict |
|---|---|---|---|
| Godot itself inside WallpaperService (the TheOathMan plugin or our own) | fully | a second engine with a 3D GL context, the tree's 1800 sprays, the meadow cards and shaders at 12 fps: about the game's own draw cost, far over 2 %/h; Godot runs one engine per process and the game would fight the wallpaper for it | no |
| Export the tree's mesh data, redraw it natively with GLES | close, but the bark, crown and grass shaders would have to be rewritten in Java | medium; a second implementation of every look change | no |
| **The game renders the current tree into image layers; a Canvas renderer draws sky, clouds, moon and the layers, bending them in the wind** | the tree, crown, season tint and grass are the game's own renderer's pixels; sky and light use the game's own colours and calendar | a gradient, three small cloud bitmaps, 1 to 2 bitmap meshes of 325 vertices a frame | **taken** |

**Game side** (`tree/live_export.gd`, `LiveExport`): in the morning (after the album photo), at
sunset (the day's growth) and when a tree is loaded or planted, if the tree changed
(`LivePicture.signature`), a SubViewport with a world of its own renders the game's tree mesh,
crown multimesh and the meadow grass near the tree (the game's own meshes and materials, so the
season's look and the species' tint come along) on flat ground to the horizon, with a clear
(transparent) sky, four times: `dawn` (low warm light from the east), `day`, `dusk` (low warm
light from the west) and `night` (cool moonlit fill). 1080x1920, scaled to 540x960 (half the
phone's resolution in 9:16), lossy WebP with alpha (about 100 to 180 KB each), written on a worker
thread into `user://live_picture`, which on Android is the app's files dir. The meta
(`live.json`: foot line, crown top, horizon, ground colour, file names) is written last through a
temporary file, so the phone never reads a half-written set. PC games skip it (`LiveExport.enabled`
is true only with the phone plugin). On a PC it took about 300 ms spread over five frames.

**Phone side** (`android_plugin/treelive`, a plain Android library, no Godot or Google code, its
own AAR `addons/tree_phone/bin/tree_live-release.aar` added by the export plugin):
`TreeWallpaperService` (WallpaperService) and `TreeDreamService` (DreamService, the screen saver)
share `LiveRenderer`. Both run in their own small process `:live`, so the wallpaper never keeps the
game engine in memory. `LiveData` reads the meta and layers (a newer version, a missing layer or a
path outside the folder is refused, and the last good picture stays); with no picture from the
game yet it shows a young linden sapling bundled in the AAR's assets (`assets/live_fallback`,
written by `tools/live_shot.gd --fallback`). Never an error screen.

**Framing.** From the album's side (north of the tree, looking south, `TreeView.ALBUM_YAW`), but
from a little below eye height and framed on the current tree, not the album's framing for the
grown species: the album frame from its raised camera shows metres of meadow and the clearing's
paths, which breaks "at most a grass line" (14b). The tree's foot stands at 0.9 of the layer, its
top no higher than 0.13, its crown at most 0.86 of the width (`LiveExport.FOOT/TOP/WIDE`); a
small tree is framed as the game frames it (`TreeView.frame_height`), so a sapling stands small in
the meadow. The game's swells and the rise toward the forest are left out (a flat ground plane
with the same ground material), otherwise the low camera would stand inside the hill.

**On the screen** (`LivePicture.layout`): the layer as wide as the screen, standing on its bottom
edge; above it only sky. If the crown's top would come into the top 28 % (the lock screen's clock),
the layer shrinks around the trunk's foot until it does not (14e). The view ignores the home
screen's page offset (offset notifications off) and touches (it is a picture).

**Light, moon and season.** The real local clock: sunrise and sunset for 51 N 10 E by the date
(declination, refraction and the equation of time, a few minutes off at most) in the phone's time
zone with summer time. The layers mix as the game's phone sky does: golden light until the sun is
0.45 up, night deepening over the hour after sunset (and lifting in the hour before sunrise), the
sky's two colours exactly `TreeView._paint_phone_sky`'s. Stars fade in at night; the moon is drawn
in its real phase (`Almanac.moon_phase`), behind the crown. The season's look is baked into the
layers by the game (the leaf and grass tints of the day it exported); the day length follows the
calendar live. If the game is not opened for weeks, the tree keeps its last look, as the spec asks
("the tree shown updates when the game saves, never on its own").

**Wind.** A 12x24 bitmap mesh: the foot stands still, the crown bends more the higher it is
(k^1.6), a slow gust plus a quicker flutter, the crown top moving about 1.2 % of the tree's height
(`LivePicture.WIND` = 0.012 [PLACEHOLDER]); the grass below the foot ripples by 0.2 % of the
width; the picture's side edges stay put. Three soft clouds drift across the top of the sky
(a screen width in 8 to 14 minutes).

**One maths, two copies.** `shared/live_picture.gd` (tested) and `LiveScene.java` (the phone)
hold the same functions line by line: `sun_times`/`sunTimes`, `sun_height`, `light_mix`,
`golden_of`, `sky_colors`, `cloud_color`, `moment`, `wind_offset`, `cloud_position`,
`cloud_density`, `star`, `moon_outline`, `layout`, plus `Almanac.moon_phase` and
`moon_lit_fraction`. `tools/live_parity.gd` and `android_plugin/treelive/parity/Parity.java` print
both for 45 moments, 20 wind points, stars, a layout and a cloud density: identical to six decimals
(2026-09-30). `tools/live_preview.gd` is the desktop copy of `LiveRenderer`'s drawing, used for the
frames in `GameDev/tree-qa/live-icon`.

## Battery (broken list 14c: at most about 2 % an hour as a wallpaper)

Not yet measured on the Fairphone; the reasoning:
- It draws only while seen: `onVisibilityChanged(false)` (screen off, an app in front, the
  launcher's app drawer) stops the frame loop at once; the screen saver only runs while charging.
- 12 frames a second (`LiveScene.FPS`, spec 10 to 15), timed by a Handler, no busy loop.
- Per frame: about 325 vertex offsets (a few sin and pow each, well under 0.1 ms), a gradient
  rect, up to 46 dots, a moon path, three cloud bitmaps and one or two bitmap meshes on a hardware
  canvas (`lockHardwareCanvas`): a couple of full-screen fills on the GPU. No 3D, no shaders, no
  allocation per frame (the sky gradient is rebuilt every 5 s, the cloud tint when it changes).
  The light is worked out every 5 s, the picture's file date looked at once a minute.
- Battery saver on: one still frame a minute (the light still follows the day).
- Estimate: the Fairphone 6's 4415 mAh at about 3.9 V is about 170 mW per 1 % an hour. A static
  wallpaper costs nothing extra; this adds 12 surface posts a second and their composition, about
  30 to 80 mW while the home or lock screen is on, so roughly 0.2 to 0.5 % an hour of screen-on
  time on the home screen, well under the 2 % limit. To check on the phone: Settings > Battery >
  battery usage lists "Tree" (the `:live` process) after an hour with the wallpaper on.
- The game pays for the layers at most three times a day (morning, sunset, load): four renders
  of the tree and four WebP encodes, a few hundred ms spread over frames on a worker thread.

## How to switch it on (phone)
- Wallpaper: hold an empty spot of the home screen > Wallpapers (on /e/OS: "Wallpapers" or "Live
  wallpapers") > Tree > set on the home screen (and lock screen). Or Settings > Wallpaper.
- Screen saver: Settings > Display > Screen saver > Tree, and choose when (while charging or
  docked).
- The pinboard in the shed has a scrap "the tree on my phone" (phone only); tapped, it says this
  in one line. The picture changes after the game saw a new morning or sunset.

## App icon (section 7)
- `tools/render_app_icon.gd`: a linden grown by the game (seed 42, 16 days, the root bot, 15.6 m),
  summer look, turned a half turn so its fullest side faces the viewer (picked from a contact sheet
  of six seeds, two ages, four sides), rendered with the game's bark and crown in soft morning
  light from the east (the viewer's left), transparent background.
- Adaptive icon: background layer = a clear pale-blue sky (lighter toward the horizon) and a low
  grass line with a gentle swell; foreground = the tree only, scaled so every opaque pixel lies
  inside the safe circle (66 of 108 dp, 97 % of it), the trunk's foot a little into the grass.
  Round, squircle and square masks keep the whole crown (14f). Nothing else in it (14g).
- Also written: `icons/icon_mono_432.png` (Android 13 themed icon, the white silhouette),
  `icons/icon_192.png` and `icons/icon_full.png` (512, legacy square icons: the visible middle),
  `icon_app.png` (256, the project icon). The 0.3 icons are kept in `icons/v0.7/` to swap back
  (a `.gdignore` keeps them out of the export).
- Previews at 512, 192 and 48 px with round and squircle masks on light and dark home screens:
  `GameDev/tree-qa/live-icon/icon/`. At 48 px it reads as one tree on a sky; the crown is airy (the
  game's own crown), not a solid ball. Simon's visual call.

### Export preset lines (export_presets.cfg is gitignored)
```
launcher_icons/main_192x192="res://icons/icon_192.png"
launcher_icons/adaptive_foreground_432x432="res://icons/icon_fg_432.png"
launcher_icons/adaptive_background_432x432="res://icons/icon_adaptive_bg_432.png"
launcher_icons/adaptive_monochrome_432x432="res://icons/icon_mono_432.png"
```
The first three were already there (same paths, new pictures); the monochrome line is new.

## Rebuild the live library
```sh
cd Tree/android_plugin
gradlew.bat :treelive:copyAarToAddon      # JDK 17 and the SDK as in README.md
```

## Open
- Battery and the look on the Fairphone are unmeasured (not installed, as asked).
- /e/OS's wallpaper picker should list live wallpapers (AOSP's does); if Murena's launcher has
  none, Settings > Wallpaper or the "Live wallpapers" picker is the way.
- The wallpaper reads `getFilesDir()/live_picture`; Godot's `user://` is that folder on Android
  (GodotIO uses getFilesDir), not checked on the phone yet.
- The picker thumbnail (`res/drawable-nodpi/tree_live_thumb.png`) is a fixed frame of a bot-grown
  summer linden (day 20).
- The pinboard: the backup notes (section 2) will want room on the same cork; the rows moved up
  a little (196 px apart) to make room for this scrap.
