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

Game code uses the wrapper `res://shared/phone.gd` (`class_name Phone`), which is a no-op on PC.

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
