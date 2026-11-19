extends SceneTree

## The dependency walker copies every texture an FBX names to its mirrored
## path, the same path below the export root that it has below the pack:
##   godot --headless --path <repo> -s res://tests/smoke/walk_fbx_textures.gd -- <empty temp dir>
## 1. A texture that an FBX and a .gltf share is copied once, to the path the
##    .gltf names, so the copied .gltf is byte for byte the source and still
##    finds its texture, whichever model is walked first.
## 2. Two different textures with one file name keep two copies, so the .gltf's
##    copy holds its own texture and not the FBX's.
## 3. A texture outside the pack, which only a reference climbing out with ..
##    reaches, is not copied.

var _failures := 0

func _initialize() -> void:
	var root: String = OS.get_cmdline_user_args()[0].replace("\\", "/")
	var pack := root.path_join("lib/models/vendor/Pack")
	_shared_texture(pack, root.path_join("out"))
	_same_file_name(pack, root.path_join("out"))
	_outside_pack(pack, root.path_join("out"))
	print("walk_fbx_textures: %d failure(s)" % _failures)
	quit(1 if _failures > 0 else 0)

func _shared_texture(pack: String, out: String) -> void:
	var fbx := pack.path_join("FBX/wall.fbx")
	_write(fbx, "RelativeFilename: \"..\\Textures\\Trim Sheet.png\"\n")
	var gltf := pack.path_join("Models/pot.gltf")
	_write(gltf, '{\n  "asset": {"version": "2.0"},\n  "images": [{"uri": "../Textures/Trim%20Sheet.png"}]\n}\n')
	var texture := pack.path_join("Textures/Trim Sheet.png")
	_png(texture, Color.RED)
	for order: Array in [[fbx, gltf], [gltf, fbx]]:
		var label := "shared texture, %s first" % str(order[0]).get_extension()
		var dest := out.path_join("shared_" + str(order[0]).get_extension())
		var copy_map := _export(pack, dest, order, label)
		_check(str(copy_map.get(texture)) == dest.path_join("Textures/Trim Sheet.png"), label,
			"texture copied to its mirrored path, got %s" % copy_map.get(texture))
		_check(_same_bytes(gltf, str(copy_map[gltf])), label, ".gltf copied unchanged")
		_check(FileAccess.file_exists(_gltf_image(str(copy_map[gltf]))), label,
			"copied .gltf finds its texture at %s" % _gltf_image(str(copy_map[gltf])))

## The case from the review of TychoHenzen/cardcleaner#465: the FBX names
## variants/albedo.png and the .gltf beside it textures/albedo.png. Putting FBX
## textures in a textures/ folder beside the FBX sent both to one destination,
## and the copy that came second was skipped.
func _same_file_name(pack: String, out: String) -> void:
	var fbx := pack.path_join("Rocks/rock.fbx")
	_write(fbx, "RelativeFilename: \"variants\\albedo.png\"\n")
	var gltf := pack.path_join("Rocks/rock.gltf")
	_write(gltf, '{\n  "asset": {"version": "2.0"},\n  "images": [{"uri": "textures/albedo.png"}]\n}\n')
	var fbx_texture := pack.path_join("Rocks/variants/albedo.png")
	var gltf_texture := pack.path_join("Rocks/textures/albedo.png")
	_png(fbx_texture, Color.RED)
	_png(gltf_texture, Color.BLUE)
	for order: Array in [[fbx, gltf], [gltf, fbx]]:
		var label := "same file name, %s first" % str(order[0]).get_extension()
		var dest := out.path_join("name_" + str(order[0]).get_extension())
		var copy_map := _export(pack, dest, order, label)
		_check(str(copy_map.get(fbx_texture)) == dest.path_join("Rocks/variants/albedo.png"), label,
			"FBX texture copied to its mirrored path, got %s" % copy_map.get(fbx_texture))
		_check(str(copy_map.get(gltf_texture)) == dest.path_join("Rocks/textures/albedo.png"), label,
			".gltf texture copied to its mirrored path, got %s" % copy_map.get(gltf_texture))
		_check(_same_bytes(fbx_texture, str(copy_map.get(fbx_texture))), label, "FBX texture copy holds its own pixels")
		_check(_same_bytes(gltf_texture, _gltf_image(str(copy_map[gltf]))), label,
			"the texture the copied .gltf finds is its own, not the FBX's")

func _outside_pack(pack: String, out: String) -> void:
	var fbx := pack.path_join("Props/lamp.fbx")
	_write(fbx, "RelativeFilename: \"..\\..\\Shared\\outside.png\"\n")
	var outside := pack.get_base_dir().path_join("Shared/outside.png")
	_png(outside, Color.GREEN)
	var label := "texture outside the pack"
	var located := false
	for entry: Dictionary in ModelLoader.resolved_textures(fbx):
		located = located or str(entry["path"]) == outside
	# Without this the check below would pass however the walker placed it.
	_check(located, label, "the locator resolves the .. reference to %s" % outside)
	var dest := out.path_join("outside")
	var copy_map := _export(pack, dest, [fbx], label)
	_check(not copy_map.has(outside), label, "not copied, got %s" % copy_map.get(outside))

## Walks the models in order into one copy map, then copies and rewrites the
## files the way a scene export does.
func _export(pack: String, dest_root: String, order: Array, label: String) -> Dictionary:
	var copy_map: Dictionary = {}
	for model: String in order:
		copy_map[model] = dest_root.path_join(model.trim_prefix(pack + "/"))
		DependencyWalker.collect_dependencies(model, pack, dest_root, copy_map)
	var result := AssetExporter.new_result()
	for source: String in copy_map:
		AssetExporter.copy_one_file(source, str(copy_map[source]), result)
	DependencyWalker.rewrite_dependencies(copy_map, pack, result)
	_check(result["errors"].is_empty(), label, "export without errors %s" % [result["errors"]])
	return copy_map

## The file the first image URI of a .gltf points at.
func _gltf_image(gltf: String) -> String:
	var json: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(gltf))
	var uri: String = json["images"][0]["uri"]
	return gltf.get_base_dir().path_join(uri.uri_file_decode()).simplify_path()

func _same_bytes(a: String, b: String) -> bool:
	return FileAccess.file_exists(b) and FileAccess.get_file_as_bytes(a) == FileAccess.get_file_as_bytes(b)

func _check(passed: bool, label: String, what: String) -> void:
	print("%s %s: %s" % ["ok  " if passed else "FAIL", label, what])
	if not passed:
		_failures += 1

func _write(path: String, text: String) -> void:
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(text)
	file.close()

func _png(path: String, color: Color) -> void:
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	var image := Image.create_empty(1, 1, false, Image.FORMAT_RGBA8)
	image.fill(color)
	image.save_png(path)
