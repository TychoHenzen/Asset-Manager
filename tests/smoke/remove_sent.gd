extends SceneTree

## Remove from Project, checked against the .import as the editor leaves it,
## so it takes three runs with the import in between:
##   godot --headless --path <repo> -s res://tests/smoke/remove_sent.gd -- send <workspace> <model>...
##   godot --headless --path <repo> --editor --quit-after 3000      (imports)
##   godot --headless --path <repo> -s res://tests/smoke/remove_sent.gd -- check <workspace> <model>...
## Models go to res://assets/_send_test (ignored by git). "check" fails unless
## Remove lists the model, its .import and every material .tres generated for
## it, deletes exactly those, and leaves the textures in place.

const DEST: String = "res://assets/_send_test"

func _initialize() -> void:
	# In memory only: the project's own export roots stay untouched.
	ProjectSettings.set_setting(ExportRoots.PROJECT_SETTING, {"models": DEST})

	var args := OS.get_cmdline_user_args()
	var workspace: String = args[1]
	var failures := 0
	for model: String in args.slice(2):
		if args[0] == "send":
			failures += _send(model, workspace)
		else:
			failures += _check(model, workspace)
	quit(1 if failures > 0 else 0)

func _send(model: String, workspace: String) -> int:
	var result := AssetExporter.export_asset(model, ExportRoots.get_root("models"), "models", workspace)
	print("%s: copied %d, errors %s" % [model.get_file(), result["copied_count"], result["errors"]])
	if not AssetExporter.is_in_project(model, "models", workspace):
		print("FAIL ", model.get_file(), ": not in project after send")
		return 1
	return 0

func _check(model: String, workspace: String) -> int:
	var dest := AssetExporter.project_path(model, "models", workspace)
	var dir := dest.get_base_dir()
	var files := AssetRemover.files_for(dest)
	print(model.get_file(), " removes:")
	for path in files:
		print("    ", path)

	var expected: PackedStringArray = [dest, dest + ".import"]
	var kept: PackedStringArray = []
	for name in DirAccess.get_files_at(dir):
		var path := dir.path_join(name)
		if name.begins_with(dest.get_file().get_basename() + "_") and name.ends_with(".tres"):
			expected.append(path)
		elif name.get_extension() in ["png", "jpg", "tga"]:
			kept.append(path)

	var failures := 0
	for path in expected:
		if not files.has(path):
			print("FAIL not listed: ", path)
			failures += 1
	for path in files:
		if not expected.has(path):
			print("FAIL listed but not ours: ", path)
			failures += 1

	var result := AssetRemover.remove(files)
	if not result["errors"].is_empty():
		print("FAIL errors: ", result["errors"])
		failures += 1
	for path in files:
		if FileAccess.file_exists(path):
			print("FAIL still there: ", path)
			failures += 1
	for path in kept:
		if not FileAccess.file_exists(path):
			print("FAIL texture removed: ", path)
			failures += 1
	if AssetExporter.is_in_project(model, "models", workspace):
		print("FAIL still in project: ", model.get_file())
		failures += 1

	print("%s: removed %d, kept %d texture(s), %s" % [model.get_file(), result["removed_count"], kept.size(), "ok" if failures == 0 else "FAILED"])
	return failures
