extends SceneTree

## Checks that the scan's size and mtime equal what Godot's FileAccess reports
## for the same files, so thumbnail keys match the ones the GDScript addon
## generated before the fork. Looks at the oldest-added assets (an index.db
## migration puts them last when sorted by recent) and counts how many already
## have a thumbnail on disk under the shared key formula.
##   godot --headless --path <repo> -s res://tests/smoke/check_thumbnail_keys.gd -- <workspace> [pages]

const PER_PAGE: int = 100

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var workspace := String(args[0]).replace("\\", "/")
	var pages := int(args[1]) if args.size() > 1 else 3

	var catalog := AssetCatalog.new()
	catalog.Setup(workspace, JSON.stringify(AssetTypes.ALL))
	catalog.Open()
	var opened: Array = await catalog.Opened
	var total_pages := ceili(float(opened[0]) / PER_PAGE)

	var compared := 0
	var mismatched := 0
	var thumbs_found := 0
	for page in range(total_pages - pages, total_pages):
		catalog.Query(page, JSON.stringify({"sort": 1, "page": page, "per_page": PER_PAGE}))
		var result: Array = await catalog.QueryReady
		var paths: PackedStringArray = result[1]
		var types: PackedStringArray = result[2]
		var sizes: PackedInt64Array = result[3]
		var mtimes: PackedInt64Array = result[4]
		for i in paths.size():
			var godot_mtime := FileAccess.get_modified_time(paths[i])
			var godot_size := 0
			var file := FileAccess.open(paths[i], FileAccess.READ)
			if file:
				godot_size = file.get_length()
				file.close()
			compared += 1
			if godot_mtime != mtimes[i] or godot_size != sizes[i]:
				mismatched += 1
				if mismatched <= 5:
					print("MISMATCH %s: scan %d/%d, FileAccess %d/%d" % [paths[i], sizes[i], mtimes[i], godot_size, godot_mtime])
			var key := (paths[i] + "|" + str(mtimes[i]) + "|" + str(sizes[i])).md5_text()
			var thumb := workspace.path_join(".assetmanager/thumbnails").path_join(types[i]).path_join(key.substr(0, 2)).path_join(key + ".webp")
			if FileAccess.file_exists(thumb):
				thumbs_found += 1

	print("compared %d, mismatched %d, existing thumbnails found %d" % [compared, mismatched, thumbs_found])
	catalog.Shutdown()
	quit(1 if mismatched > 0 else 0)
