extends SceneTree

## The dependency walker copies a texture shared by an FBX and a .gltf once, so
## the .gltf must still find it wherever the copy landed:
##   godot --headless --path <repo> -s res://tests/smoke/walk_shared_texture.gd -- <empty temp dir>
## Walked FBX first, the FBX branch puts the texture in its textures/ folder and
## the copied .gltf's URI is repointed there. Walked .gltf first, the texture
## keeps its mirrored path and the copied .gltf is byte for byte the source.

func _initialize() -> void:
	var root: String = OS.get_cmdline_user_args()[0].replace("\\", "/")
	var pack := root.path_join("lib/models/vendor/Pack")
	DirAccess.make_dir_recursive_absolute(pack.path_join("FBX"))
	DirAccess.make_dir_recursive_absolute(pack.path_join("Models"))
	DirAccess.make_dir_recursive_absolute(pack.path_join("Textures"))
	var fbx := pack.path_join("FBX/wall.fbx")
	_write(fbx, "RelativeFilename: \"..\\Textures\\Trim Sheet.png\"\n")
	var gltf := pack.path_join("Models/pot.gltf")
	_write(gltf, '{\n  "asset": {"version": "2.0"},\n  "images": [{"uri": "../Textures/Trim%20Sheet.png"}]\n}\n')
	var image := Image.create_empty(1, 1, false, Image.FORMAT_RGBA8)
	image.save_png(pack.path_join("Textures/Trim Sheet.png"))

	var failures := 0
	failures += _walk(pack, root.path_join("fbx_first"), [fbx, gltf], false)
	failures += _walk(pack, root.path_join("gltf_first"), [gltf, fbx], true)
	quit(1 if failures > 0 else 0)

## Walks the models in order into one copy map, copies and rewrites like a
## scene export does, then reads the copied .gltf's image URI.
func _walk(pack: String, dest_root: String, order: Array, expect_unchanged: bool) -> int:
	var copy_map: Dictionary = {}
	for model: String in order:
		copy_map[model] = dest_root.path_join(model.trim_prefix(pack + "/"))
		DependencyWalker.collect_dependencies(model, pack, dest_root, copy_map)
	var result := AssetExporter.new_result()
	for source: String in copy_map:
		AssetExporter.copy_one_file(source, str(copy_map[source]), result)
	DependencyWalker.rewrite_dependencies(copy_map, pack, result)

	var label := dest_root.get_file()
	var failures := 0
	if not result["errors"].is_empty():
		print("FAIL %s: export errors %s" % [label, result["errors"]])
		failures += 1
	var copied_gltf := str(copy_map[order[0] if order[0].ends_with(".gltf") else order[1]])
	var text := FileAccess.get_file_as_string(copied_gltf)
	var json: Dictionary = JSON.parse_string(text)
	var uri: String = json["images"][0]["uri"]
	var target := copied_gltf.get_base_dir().path_join(uri.uri_file_decode()).simplify_path()
	if FileAccess.file_exists(target):
		print("ok   %s: copied .gltf finds %s" % [label, target.trim_prefix(dest_root + "/")])
	else:
		print("FAIL %s: copied .gltf points at missing %s (uri %s)" % [label, target, uri])
		failures += 1
	var source_text := FileAccess.get_file_as_string(pack.path_join("Models/pot.gltf"))
	if expect_unchanged and text != source_text:
		print("FAIL %s: the .gltf changed although its texture kept its path:\n%s" % [label, text])
		failures += 1
	if not expect_unchanged and text == source_text:
		print("FAIL %s: the .gltf was not repointed" % label)
		failures += 1
	return failures

func _write(path: String, text: String) -> void:
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(text)
	file.close()
