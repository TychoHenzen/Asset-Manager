extends SceneTree

## Send to Project for FBX models, checked in two runs because the import has
## to happen in between:
##   godot --headless --path <repo> -s res://tests/smoke/send_fbx.gd -- send <workspace> <model>...
##   godot --headless --path <repo> --editor --quit-after 3000      (imports)
##   godot --headless --path <repo> -s res://tests/smoke/send_fbx.gd -- check <workspace> <model>...
## Models go to res://assets/_send_test (ignored by git). "check" loads each
## imported scene and fails unless every mesh surface has an albedo texture.

const DEST: String = "res://assets/_send_test"

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var workspace: String = args[1]
	var models := args.slice(2)
	var failures := 0
	for model: String in models:
		if args[0] == "send":
			var result := AssetExporter.export_asset(model, DEST, "models", workspace)
			print("%s: copied %d, skipped %d, errors %s" % [model.get_file(), result["copied_count"], result["skipped_existing_count"], result["errors"]])
			for path: String in result["copied_paths"]:
				print("    ", path)
		else:
			failures += _check(AssetExporter._compute_dest_path(model, DEST, "models", workspace))
	quit(1 if failures > 0 else 0)

func _check(scene_path: String) -> int:
	var scene: PackedScene = load(scene_path)
	if scene == null:
		print("FAIL ", scene_path, ": not imported")
		return 1
	var root := scene.instantiate()
	var missing := 0
	var stack: Array[Node] = [root]
	while not stack.is_empty():
		var node: Node = stack.pop_back()
		stack.append_array(node.get_children())
		var mesh_instance := node as MeshInstance3D
		if mesh_instance == null or mesh_instance.mesh == null:
			continue
		for surface in mesh_instance.mesh.get_surface_count():
			var material := mesh_instance.get_active_material(surface) as BaseMaterial3D
			var texture: Texture2D = material.albedo_texture if material else null
			print("    %s surface %d: %s -> %s" % [node.name, surface, material.resource_name if material else "none", texture.resource_path if texture else "NO TEXTURE"])
			if texture == null:
				missing += 1
	root.free()
	print(("FAIL " if missing > 0 else "ok   ") + scene_path.get_file())
	return 1 if missing > 0 else 0
