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
| `saveVideoToGallery(aviPath: String, album: String) -> bool` | **Not built yet** (0.6 time-lapse, see below). The game already calls it when the plugin has it. |

Game code uses the wrapper `res://shared/phone.gd` (`class_name Phone`), which is a no-op on PC.

## To do: `saveVideoToGallery` (month time-lapse, design doc 17.7)

The album's "save as video" writes the tree's morning photos as a Motion-JPEG AVI
(`res://ui/mjpeg_avi.gd`: RIFF, one `vids`/`MJPG` stream, 6 fps, 540 px wide, every frame a
whole JPEG in a `00dc` chunk, `idx1` index with offsets from the `movi` tag) into
`user://timelapse/<species>_<stamp>.avi`, then calls `Phone.save_video_to_gallery(path)`, which
passes the absolute path and the album name `"Tree"` to the plugin. Android's gallery apps do not
play MJPEG AVI, so the plugin method should:

1. Read the AVI's frames: walk the `idx1` entries (or the `00dc` chunks after `movi`) and decode each
   JPEG with `BitmapFactory.decodeByteArray`. Frame rate = `dwRate / dwScale` of the `strh`.
2. Encode H.264 MP4 with `MediaCodec` (`video/avc`, `COLOR_FormatSurface`, draw each bitmap onto the
   encoder's input `Surface` with `lockHardwareCanvas`, presentation time = frame / fps) and
   `MediaMuxer` (`MUXER_OUTPUT_MPEG_4`) into the app's cache dir. Plain Android APIs, no Google
   libraries (works on /e/OS).
3. Insert it into the gallery with `MediaStore.Video.Media.EXTERNAL_CONTENT_URI`:
   `DISPLAY_NAME` = file name with `.mp4`, `MIME_TYPE` = `video/mp4`,
   `RELATIVE_PATH` = `Movies/` + album (Android 10+; `IS_PENDING` 1 while copying, then 0).
   No storage permission is needed on Android 10+ for the app's own MediaStore entries; below
   Android 10 it needs `WRITE_EXTERNAL_STORAGE` (maxSdkVersion 28).
4. Return `true` on success. Run the work off the UI thread; if it should report later, add a signal
   `video_saved(ok: bool)` (the game currently reads only the return value).

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
