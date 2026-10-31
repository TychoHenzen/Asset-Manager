@tool
class_name FbxExport
extends RefCounted

## Brings an FBX's textures along on Send to Project, placed so the project's
## own importer links them.
## Godot's FBX importer looks for each texture at <model folder>/<referenced
## path>, or <model folder>/<file name> for an absolute reference
## (fbx_document.cpp:1051). A texture found under its referenced name is copied
## exactly there. One found by fallback (Synty props name a .psd that never
## shipped) has no name the importer would look for, so it is copied next to
## the model and its materials are linked explicitly: a StandardMaterial3D
## .tres per material, and a .import that tells the scene importer to use it.

## `dest_path` is the FBX's res:// destination; `result` is the exporter's
## shared result dictionary.
static func export_textures(source_path: String, dest_path: String, result: Dictionary) -> void:
	var dest_dir := dest_path.get_base_dir()
	var needs_link: Dictionary = {}   # texture source path -> res:// copy

	for entry: Dictionary in ModelLoader.resolved_textures(source_path):
		var lookup := _importer_lookup(entry["reference"], dest_dir)
		if entry["exact"] and not lookup.is_empty():
			AssetExporter.copy_one_file(entry["path"], lookup, result)
			continue
		var copy := dest_dir.path_join(String(entry["path"]).get_file())
		AssetExporter.copy_one_file(entry["path"], copy, result)
		needs_link[entry["path"]] = copy

	if needs_link.is_empty():
		return

	# Which material takes which texture: the same fill the preview does.
	var filled: Dictionary = {}
	var node := ModelLoader.load_model(source_path, filled)
	if node != null:
		node.free()

	var external: Dictionary = {}     # material name -> res:// .tres
	for material_name: String in filled:
		var texture_source: String = filled[material_name]
		if not needs_link.has(texture_source):
			continue
		var material_path := dest_dir.path_join("%s_%s.tres" % [dest_path.get_file().get_basename(), _file_safe(material_name)])
		if _write_material(material_path, material_name, needs_link[texture_source], result):
			external[material_name] = material_path

	if not external.is_empty():
		_write_import_settings(dest_path, external, result)

## Where Godot's importer will look for `reference`, or "" when that place is
## outside the project.
static func _importer_lookup(reference: String, dest_dir: String) -> String:
	var is_absolute := reference.begins_with("/") or (reference.length() > 1 and reference[1] == ":")
	if is_absolute:
		return dest_dir.path_join(reference.get_file())
	var lookup := dest_dir.path_join(reference).simplify_path()
	return lookup if lookup.begins_with("res://") and not lookup.contains("..") else ""

static func _file_safe(name: String) -> String:
	var safe := ""
	for c in name:
		safe += c if c.is_valid_identifier() or c.is_valid_int() or c == "-" else "_"
	return safe

## Written as text: the texture was only just copied and is not imported yet,
## so there is no loaded resource to save a reference to.
static func _write_material(material_path: String, material_name: String, texture_path: String, result: Dictionary) -> bool:
	if FileAccess.file_exists(material_path):
		result["skipped_existing_count"] += 1
		return true
	var file := FileAccess.open(material_path, FileAccess.WRITE)
	if file == null:
		result["errors"].append("Could not write material: " + material_path)
		return false
	file.store_string("\n".join([
		'[gd_resource type="StandardMaterial3D" format=3]',
		"",
		'[ext_resource type="Texture2D" path="%s" id="1_albedo"]' % texture_path,
		"",
		"[resource]",
		'resource_name = "%s"' % material_name.c_escape(),
		'albedo_texture = ExtResource("1_albedo")',
		"",
	]))
	file.close()
	result["copied_count"] += 1
	result["copied_paths"].append(material_path)
	return true

## The scene importer reads its settings from <model>.import before the first
## import, so the materials are linked the moment the project sees the file.
## An existing .import (the user's own settings) is left alone.
static func _write_import_settings(dest_path: String, external: Dictionary, result: Dictionary) -> void:
	var import_path := dest_path + ".import"
	if FileAccess.file_exists(import_path):
		return
	var materials: Dictionary = {}
	for material_name: String in external:
		materials[material_name] = {"use_external/enabled": true, "use_external/path": external[material_name]}

	var config := ConfigFile.new()
	config.set_value("remap", "importer", "scene")
	config.set_value("remap", "importer_version", 1)
	config.set_value("remap", "type", "PackedScene")
	config.set_value("deps", "source_file", dest_path)
	config.set_value("params", "_subresources", {"materials": materials})
	# Without these Godot reads the file as written before 4.3: FBX2glTF
	# instead of ufbx, and the old naming scheme, under which the material
	# names above would not match.
	config.set_value("params", "fbx/importer", 0)
	config.set_value("params", "fbx/naming_version", 2)
	var err := config.save(ProjectSettings.globalize_path(import_path))
	if err != OK:
		result["errors"].append("Could not write import settings (%d): %s" % [err, import_path])
