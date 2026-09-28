@tool
extends EditorPlugin
## Adds the TreePhone Android plugin (bin/tree_phone-release.aar) to Android Gradle exports.
## Source and rebuild notes: res://android_plugin/README.md

var _export_plugin: TreePhoneExportPlugin


func _enter_tree() -> void:
	_export_plugin = TreePhoneExportPlugin.new()
	add_export_plugin(_export_plugin)


func _exit_tree() -> void:
	remove_export_plugin(_export_plugin)
	_export_plugin = null


class TreePhoneExportPlugin extends EditorExportPlugin:
	func _get_name() -> String:
		return "TreePhone"

	func _supports_platform(platform: EditorExportPlatform) -> bool:
		return platform is EditorExportPlatformAndroid

	func _get_android_libraries(_platform: EditorExportPlatform, _debug: bool) -> PackedStringArray:
		# Relative to res://addons/. One release AAR serves debug and release exports.
		return PackedStringArray(["tree_phone/bin/tree_phone-release.aar"])

	func _get_android_dependencies(_platform: EditorExportPlatform, _debug: bool) -> PackedStringArray:
		# Plain Android APIs only: no Maven dependencies.
		return PackedStringArray()

	func _get_android_manifest_element_contents(_platform: EditorExportPlatform, _debug: bool) -> String:
		# Haptics (ui/haptics.gd): a short buzz on a cut, a dive and a finished tree.
		return "<uses-permission android:name=\"android.permission.VIBRATE\" />"
