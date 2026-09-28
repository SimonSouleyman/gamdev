class_name Phone
extends RefCounted
## Phone features through the Android plugin singleton "TreePhone" (addons/tree_phone).
## On PC, or in an export without the plugin, every call is a no-op that returns false.

const SINGLETON := "TreePhone"


static func is_available() -> bool:
	return Engine.has_singleton(SINGLETON)


## Sets the image as the home-screen wallpaper. Accepts res://, user:// or absolute paths;
## the plugin reads a real file, so res:// images are copied to user:// first.
static func set_wallpaper(image_path: String, also_lock_screen: bool = false) -> bool:
	if not is_available():
		return false
	var path := image_path
	if path.begins_with("res://"):
		var copy := "user://wallpaper.png"
		var tex := load(path) as Texture2D
		var img: Image = tex.get_image() if tex != null else null
		if img == null or img.save_png(copy) != OK:
			return false
		path = copy
	return bool(_plugin().setWallpaper(ProjectSettings.globalize_path(path), also_lock_screen))


## Posts a notification every day at about hour:minute local time (tapping it opens the game).
## Replaces an earlier reminder. Needs the notification permission on Android 13+.
static func schedule_daily_reminder(hour: int, minute: int, title: String, text: String) -> bool:
	if not is_available():
		return false
	_plugin().scheduleDaily(hour, minute, title, text)
	return true


static func cancel_daily_reminder() -> bool:
	if not is_available():
		return false
	_plugin().cancelDaily()
	return true


## Shows the system notification prompt on Android 13+ (once; after a refusal the owner must allow
## it in the app settings). The answer arrives as the singleton's signal
## notification_permission_result(granted: bool). Returns whether the request was made.
static func ask_notification_permission() -> bool:
	if not is_available():
		return false
	_plugin().requestNotificationPermission()
	return true


static func has_notification_permission() -> bool:
	if not is_available():
		return false
	return bool(_plugin().isNotificationPermissionGranted())


## Puts a video (the month time-lapse, an MJPEG .avi written by MjpegAvi) into the phone's
## gallery (Movies/Tree). The plugin converts it to an MP4 there; see android_plugin/README.md.
## A no-op that returns false on PC, and with a plugin that does not have the method yet.
static func save_video_to_gallery(path: String) -> bool:
	if not is_available():
		return false
	var p := _plugin()
	if not p.has_method("saveVideoToGallery"):
		return false
	return bool(p.saveVideoToGallery(ProjectSettings.globalize_path(path), "Tree"))


static func _plugin() -> Object:
	return Engine.get_singleton(SINGLETON)
