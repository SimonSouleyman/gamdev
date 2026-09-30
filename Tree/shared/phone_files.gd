class_name PhoneFiles
extends RefCounted
## 0.8: the save backup's pickers and sharing through the TreePhone plugin (plain Android:
## Storage Access Framework and the share sheet, no storage permission, no Google services).
## Every call answers once through `on_done.call(result: String)` with a result word:
## "ok", "cancelled", "no_room", "too_big", "no_app" or "failed". Without the plugin (a PC, or
## an older plugin) the `can_*` checks are false and the calls return false and never answer.

const SINGLETON := "TreePhone"


static func _plugin() -> Object:
	return Engine.get_singleton(SINGLETON) if Engine.has_singleton(SINGLETON) else null


static func _has(method: String) -> bool:
	var p := _plugin()
	return p != null and p.has_method(method)


## The phone's own "save as" and "open" pickers are there.
static func can_pick_documents() -> bool:
	return _has("createDocument") and _has("openDocument")


## The phone's share sheet is there (a very old plugin or a PC: no).
static func can_share() -> bool:
	return _has("shareFile") and _has("shareVideo")


## "save as": the player picks where the file at `path` goes, under the suggested name.
static func save_document(path: String, suggested_name: String, on_done: Callable) -> bool:
	if not can_pick_documents():
		return false
	return _call_with_signal("document_saved", on_done, func(p: Object) -> bool:
		return bool(p.createDocument(ProjectSettings.globalize_path(path), suggested_name, "application/zip")))


## "open": the player picks a file; it is copied to `dest_path` before on_done("ok").
static func open_document(dest_path: String, on_done: Callable) -> bool:
	if not can_pick_documents():
		return false
	return _call_with_signal("document_opened", on_done, func(p: Object) -> bool:
		return bool(p.openDocument(ProjectSettings.globalize_path(dest_path))))


## The share sheet with a picture (PNG). Answers at once: "ok" once the sheet is up.
static func share_image(path: String, on_done: Callable) -> bool:
	if not can_share():
		return false
	var result := str(_plugin().shareFile(ProjectSettings.globalize_path(path), "image/png"))
	on_done.call(result)
	return true


## The share sheet with the month's time-lapse (the MJPEG AVI; the phone makes an MP4 first).
static func share_video(avi_path: String, on_done: Callable) -> bool:
	if not can_share():
		return false
	return _call_with_signal("shared", on_done, func(p: Object) -> bool:
		return bool(p.shareVideo(ProjectSettings.globalize_path(avi_path))))


## Connects on_done once to the plugin's signal, then starts; undoes the connection if it did not start.
static func _call_with_signal(signal_name: String, on_done: Callable, start: Callable) -> bool:
	var p := _plugin()
	var listen := on_done.is_valid() and p.has_signal(signal_name) and p.connect(signal_name, on_done, CONNECT_ONE_SHOT) == OK
	var started := bool(start.call(p))
	if listen and not started and p.is_connected(signal_name, on_done):
		p.disconnect(signal_name, on_done)
	return started
