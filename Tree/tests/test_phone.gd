extends RefCounted
var t


func test_phone_is_a_no_op_on_pc() -> void:
	if Phone.is_available():
		return
	t.check(not Phone.set_wallpaper("res://icon_app.png"), "no wallpaper without the plugin")
	t.check(not Phone.schedule_daily_reminder(9, 0, "Tree", "Your tree is waiting"), "no reminder")
	t.check(not Phone.cancel_daily_reminder(), "nothing to cancel")
	t.check(not Phone.ask_notification_permission(), "no permission prompt")
	t.check(not Phone.has_notification_permission(), "no permission on PC")
