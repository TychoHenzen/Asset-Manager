@tool
class_name ExportRoots
extends RefCounted
## Where each asset type lands in the project, per project. Lives in core, not
## the settings dialog, because the grid needs it to tell which assets are
## already in the project.

const PROJECT_SETTING: String = "asset_manager/export_roots"

## An empty entry (a cleared field in the settings dialog) means the type's
## default, not the project root.
static func get_root(type_id: String) -> String:
	var root: String = _roots().get(type_id, "")
	if not root.is_empty():
		return root
	return AssetTypes.get_by_id(type_id).get("default_export_path", "res://assets")

static func set_root(type_id: String, value: String) -> void:
	var roots := _roots()
	roots[type_id] = value
	ProjectSettings.set_setting(PROJECT_SETTING, roots)
	ProjectSettings.save()

static func _roots() -> Dictionary:
	var value: Variant = ProjectSettings.get_setting(PROJECT_SETTING, {})
	return value if value is Dictionary else {}
