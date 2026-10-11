@tool
class_name AssetRemover
extends RefCounted
## Takes a sent asset back out of the project: the file, the sidecars Godot
## keeps next to it, and the material .tres files FbxExport generated for it.
## Textures stay. Models of one pack share them, and nothing here can see
## which other scenes in the project still use one.

const SIDECAR_EXTENSIONS: PackedStringArray = ["import", "uid"]

## Everything Remove from Project deletes for the asset at dest_path, all of
## it inside the project, so listing it reads no library files.
static func files_for(dest_path: String) -> PackedStringArray:
	var files: PackedStringArray = []
	for path in [dest_path, dest_path + ".import", dest_path + ".uid"]:
		if FileAccess.file_exists(path):
			files.append(path)
	for material_path in _generated_materials(dest_path):
		if not files.has(material_path):
			files.append(material_path)
	return files

## Fields match AssetExporter.new_result(), but count what went:
##   removed_count: int
##   errors: Array[String]
##   removed_paths: Array[String]
## In the editor the FileSystem dock is told as well. It finds an import's
## cached copies in .godot/imported through the .import file, so it has to hear
## about each deleted resource while its .import still exists. It deletes the
## .import itself while clearing up; any sidecar still there afterwards goes
## last.
static func remove(paths: PackedStringArray) -> Dictionary:
	var result := {"removed_count": 0, "errors": [], "removed_paths": []}
	var sidecars: PackedStringArray = []
	for path in paths:
		if path.get_extension() in SIDECAR_EXTENSIONS:
			sidecars.append(path)
		else:
			_remove_one(path, result)

	if Engine.is_editor_hint():
		var efs := EditorInterface.get_resource_filesystem()
		for path: String in result["removed_paths"]:
			efs.update_file(path)

	for path in sidecars:
		if FileAccess.file_exists(path):
			_remove_one(path, result)
		else:
			result["removed_count"] += 1
			result["removed_paths"].append(path)
	return result

static func _remove_one(path: String, result: Dictionary) -> void:
	var err := DirAccess.remove_absolute(path)
	if err == OK:
		result["removed_count"] += 1
		result["removed_paths"].append(path)
	else:
		result["errors"].append("Failed to remove (%d): %s" % [err, path])

## The external materials the asset's .import links that FbxExport wrote:
## next to the model and named <model>_<material>.tres. A material the user
## linked by hand from anywhere else is theirs and is left alone. Reading the
## .import rather than globbing <model>_* keeps Rock from taking Rock_01's.
static func _generated_materials(dest_path: String) -> PackedStringArray:
	var found: PackedStringArray = []
	var config := ConfigFile.new()
	if config.load(dest_path + ".import") != OK:
		return found

	var subresources: Variant = config.get_value("params", "_subresources", {})
	if not subresources is Dictionary:
		return found
	var materials: Variant = subresources.get("materials", {})
	if not materials is Dictionary:
		return found

	var prefix := dest_path.get_basename() + "_"
	for settings: Variant in materials.values():
		if not settings is Dictionary or not settings.get("use_external/enabled", false):
			continue
		var path := _resolve(settings.get("use_external/path", ""), settings.get("use_external/fallback_path", ""))
		if path.begins_with(prefix) and path.get_extension() == "tres" and FileAccess.file_exists(path):
			found.append(path)
	return found

## Saving from the Advanced Import dialog can turn the path into a uid://, and
## Godot writes fallback_path for when that uid is unknown.
static func _resolve(path: String, fallback: String) -> String:
	if path.begins_with("uid://"):
		var id := ResourceUID.text_to_id(path)
		path = ResourceUID.get_id_path(id) if ResourceUID.has_id(id) else ""
	return path if not path.is_empty() else fallback
