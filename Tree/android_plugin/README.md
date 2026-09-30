# TreePhone Android plugin

Godot 4.7 Android plugin (v2) for Tree. Singleton `TreePhone`, plain Android APIs only
(no Google Play services, works on /e/OS).

| Method | What it does |
| --- | --- |
| `setWallpaper(pngPath: String, alsoLock: bool) -> bool` | `WallpaperManager.setBitmap` with `FLAG_SYSTEM` (+ `FLAG_LOCK`). Absolute file path. |
| `requestNotificationPermission()` | Android 13+ runtime prompt for `POST_NOTIFICATIONS`. Emits `notification_permission_result(granted: bool)`. |
| `isNotificationPermissionGranted() -> bool` | Permission granted and notifications enabled. |
| `scheduleDaily(hour, minute, title, text)` | Daily reminder via `AlarmManager` (inexact `setAndAllowWhileIdle`, re-armed after each firing; exact only if `canScheduleExactAlarms()`). Channel `tree_daily`. Tap opens the game. Re-armed after reboot, app update and clock change. |
| `cancelDaily()` | Stops the reminder. |
| `saveVideoToGallery(aviPath: String, album: String) -> bool` | Month time-lapse: MJPEG AVI (absolute path) to H.264 MP4 in the gallery under `Movies/<album>`. Runs on its own thread: returns `true` once started (`false` if the file is missing or a video is already being saved), then emits `video_saved(ok: bool)`. See below. |
| `createDocument(srcPath: String, name: String, mime: String) -> bool` | 0.8 backup: Android's "save as" picker (`ACTION_CREATE_DOCUMENT`) for the file, suggesting `name`; copies it there on a worker thread. Emits `document_saved(result)`. |
| `openDocument(destPath: String) -> bool` | 0.8 backup: Android's "open" picker (`ACTION_OPEN_DOCUMENT`); copies the chosen file to `destPath` (at most 512 MB). Emits `document_opened(result)`. |
| `shareFile(path: String, mime: String) -> String` | 0.8 sharing: the share sheet (`ACTION_SEND` through the chooser, no title or text) with the file, served read-only by `ShareProvider` from the cache folder `share`. Returns the result at once. |
| `shareVideo(aviPath: String) -> bool` | 0.8 sharing: the month's MJPEG AVI as an MP4 (VideoSaver's encoder) in the share sheet. Emits `shared(result)`. |

Signals: `notification_permission_result(granted: bool)`, `video_saved(ok: bool)`,
`document_saved(result: String)`, `document_opened(result: String)`, `shared(result: String)`.
Result words: `ok`, `cancelled`, `no_room`, `too_big`, `no_app`, `failed`. The game uses them
through `res://shared/phone_files.gd` (`PhoneFiles`). The file provider (`ShareProvider`,
authority `<package>.treeshare`) replaces androidx FileProvider so the plugin needs no library;
a `<queries>` block lets the game see whether any app takes a picture or a video (Android 11+).

Game code uses the wrapper `res://shared/phone.gd` (`class_name Phone`), which is a no-op on PC.

## `saveVideoToGallery` (month time-lapse, design doc 17.7) - built

The album's "save as video" writes the tree's morning photos as a Motion-JPEG AVI
(`res://ui/mjpeg_avi.gd`: RIFF, one `vids`/`MJPG` stream, 6 fps, 540 px wide, every frame a
whole JPEG in a `00dc` chunk, `idx1` index with offsets from the `movi` tag) into
`user://timelapse/<species>_<stamp>.avi`, then calls
`Phone.save_video_to_gallery(path, on_done)`, which passes the absolute path and the album name
`"Tree"` to the plugin and connects `on_done` once to `video_saved`. The album shows
"saving to the phone's gallery..." until the signal arrives. Android's gallery apps do not play
MJPEG AVI, so `VideoSaver.java` (on a worker thread):

1. Reads the AVI: walks the RIFF chunks (`hdrl`/`strl`/`movi`), takes the `##dc` chunks inside
   `movi` as frames and decodes each JPEG with `BitmapFactory.decodeByteArray`.
   Frame rate = `dwRate / dwScale` of the `vids` `strh`.
2. Encodes H.264 MP4 with `MediaCodec` (`video/avc`, `COLOR_FormatSurface`, each bitmap drawn onto
   the encoder's input `Surface` with `lockHardwareCanvas`) and `MediaMuxer` (`MUXER_OUTPUT_MPEG_4`)
   into the app's cache dir. Canvas input stamps frames with the time they are posted, so frames
   are posted a steady 40 ms apart (bitrate scaled to match), each encoder output is matched to the
   frame posted at its timestamp, and gets presentation time frame / fps. Size: the frames' size
   rounded to even (retried at multiples of 16 if the encoder refuses).
3. Inserts it with `MediaStore.Video.Media.EXTERNAL_CONTENT_URI`: `DISPLAY_NAME` = AVI name with
   `.mp4`, `MIME_TYPE` = `video/mp4`, `RELATIVE_PATH` = `Movies/` + album, `IS_PENDING` 1 while
   copying, then 0 (the row is deleted if the copy fails). Android 9 and older: copies the file to
   the public `Movies/<album>` folder and registers it with `DATA`; that needs
   `WRITE_EXTERNAL_STORAGE` (manifest, `maxSdkVersion` 28) granted by the owner - the game does not
   ask for it, so there it fails cleanly (`video_saved(false)`, the AVI stays in the user folder).
4. Emits `video_saved(ok)` on Godot's render thread. The temporary MP4 is deleted either way.

## TreeLive: the live wallpaper and screen saver (0.8)

A second, plain Android library in `treelive/` (no Godot dependency, no Google services, no
permission): `TreeWallpaperService` (WallpaperService) and `TreeDreamService` (DreamService) draw
the tree the game renders into `user://live_picture` (the app's files dir) with a small Canvas
renderer, in their own process `:live`. Built as `addons/tree_phone/bin/tree_live-release.aar`
(`gradlew.bat :treelive:copyAarToAddon`), which the export plugin adds beside TreePhone's AAR.
Details, battery reasoning and how to switch it on: `docs/notes/live-icon-0.8.md`.

## Layout

- `treephone/` the library module (Java). Manifest: permissions, plugin meta-data
  `org.godotengine.plugin.v2.TreePhone`, `ReminderReceiver`, `BootReceiver`.
- The built AAR is committed at `res://addons/tree_phone/bin/tree_phone-release.aar`;
  `res://addons/tree_phone/export_plugin.gd` adds it to Android exports. The game builds without
  rebuilding the plugin.
- `.gdignore` keeps Godot from importing this folder.

## Rebuild the AAR

Needs JDK 17 and an Android SDK with platform 36 and build-tools 36.0.0 (Unity's copies work).
Gradle and the `org.godotengine:godot` library are downloaded from Maven Central on first run.

```sh
cd Tree/android_plugin
# once: point Gradle at the SDK (local.properties is gitignored)
echo "sdk.dir=C\:\\Program Files\\Unity\\Hub\\Editor\\6000.6.2f1\\Editor\\Data\\PlaybackEngines\\AndroidPlayer\\SDK" > local.properties
set JAVA_HOME=C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK
gradlew.bat :treephone:copyAarToAddon
```

`copyAarToAddon` builds the release AAR and copies it to `../addons/tree_phone/bin/`. Commit the AAR.
When the Godot version changes, update `godotVersion` in `treephone/build.gradle`.

## Export the game with the plugin

The plugin needs Godot's Gradle build:

1. Install the build template once (creates the gitignored `Tree/android/`):
   Project > Install Android Build Template, or headless together with an export (below).
2. Export preset "Android": `gradle_build/use_gradle_build=true`.
3. `[editor_plugins]` in `project.godot` enables `res://addons/tree_phone/plugin.cfg`.

```sh
godot --headless --path Tree --install-android-build-template --export-debug "Android" ../tree-releases/tree.apk
```

`--install-android-build-template` on its own does not quit; use it together with an export.

**Build-tools on this PC:** Godot 4.7.2's template asks for build-tools 36.1.0, but Unity's SDK has
36.0.0 only (and is read-only, so Gradle cannot fetch 36.1.0). After installing the template, once:

```sh
sed -i "s/buildTools         : '36.1.0'/buildTools         : '36.0.0'/" Tree/android/build/config.gradle
godot --headless --path Tree --export-debug "Android" C:/Users/Home/Documents/GameDev/tree-releases/tree-plugin-test.apk
```

Reinstalling the template (new Godot version) undoes this edit.

## On the phone

- Android 13+: the game must call `Phone.ask_notification_permission()`; the owner taps Allow.
  After a refusal, allow it in Settings > Apps > Tree > Notifications.
- Reminders are inexact: the system may deliver them some minutes late (Doze, battery saver).
  Aggressive battery management can delay them further.
