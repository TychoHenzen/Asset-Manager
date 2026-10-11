extends SceneTree

## One scan of a real workspace, reported in full:
##   godot --headless --path <repo> -s res://tests/smoke/scan_library.gd -- <workspace> [stop_after_seconds]
## With stop_after_seconds the scan is stopped then, as the Stop button does,
## and the catalog is reopened from disk to show what the stopped scan kept.
## Prints the summary, the requests that were retried, any folder that stayed
## unreadable, and the record count per type.

const TYPE_IDS: Array[String] = ["models", "images", "sounds", "music", "effects", "materials", "other"]

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var workspace := String(args[0]).replace("\\", "/")
	var stop_after := float(args[1]) if args.size() > 1 else 0.0
	var types_json := JSON.stringify(AssetTypes.ALL)

	var catalog := AssetCatalog.new()
	catalog.Setup(workspace, types_json)
	catalog.Open()
	var opened: Array = await catalog.Opened
	print("opened: %d assets" % opened[0])
	await _print_counts(catalog)

	catalog.Scan(workspace)
	if stop_after > 0.0:
		await create_timer(stop_after).timeout
		print("stopping after %.0f s" % stop_after)
		catalog.CancelScan()
	var s: Array = await catalog.ScanFinished
	print("scan%s: added %d changed %d removed %d retagged %d total %d in %.0f s, %d retried requests"
		% [" (stopped)" if s[4] else "", s[0], s[1], s[2], s[7], s[3], s[6], s[9]])
	var failed: PackedStringArray = s[8]
	print("unreadable folders: %d" % failed.size())
	for folder in failed:
		print("    ", folder)
	await _print_counts(catalog)
	catalog.Shutdown()

	if stop_after > 0.0:
		var reopened := AssetCatalog.new()
		reopened.Setup(workspace, types_json)
		reopened.Open()
		var again: Array = await reopened.Opened
		print("reopened from disk: %d assets" % again[0])
		reopened.Shutdown()
	quit()

func _print_counts(catalog: AssetCatalog) -> void:
	catalog.Stats(1, "")
	var stats: Array = await catalog.StatsReady
	var folders: Dictionary = JSON.parse_string(stats[1])["folders"]
	var parts: Array[String] = []
	for type_id in TYPE_IDS:
		parts.append("%s %d" % [type_id, int(folders.get(type_id, {}).get("count", 0))])
	print("    ", ", ".join(parts))
