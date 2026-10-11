extends SceneTree

## One scan of a real workspace, reported in full:
##   godot --headless --path <repo> -s res://tests/smoke/scan_library.gd -- <workspace> [stop_after_seconds]
## With stop_after_seconds the scan is stopped then, as the Stop button does,
## and the catalog is reopened from disk to show what the stopped scan kept.
## Prints the summary, the requests that were retried, any folder that stayed
## unreadable, the record count per type, and the longest the main thread
## went without a frame while the scan ran (the editor froze on the original).

const TYPE_IDS: Array[String] = ["models", "images", "sounds", "music", "effects", "materials", "other"]

var _last_tick_usec: int = 0
var _max_gap_ms: float = 0.0
var _measuring: bool = false

func _initialize() -> void:
	_run.call_deferred()

func _process(_delta: float) -> bool:
	var now := Time.get_ticks_usec()
	if _measuring and _last_tick_usec > 0:
		_max_gap_ms = maxf(_max_gap_ms, (now - _last_tick_usec) / 1000.0)
	_last_tick_usec = now
	return false

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

	_measuring = true
	catalog.Scan(workspace)
	if stop_after > 0.0:
		await create_timer(stop_after).timeout
		print("stopping after %.0f s" % stop_after)
		catalog.CancelScan()
	var s: Array = await catalog.ScanFinished
	_measuring = false
	print("longest main-thread frame gap during the scan: %.1f ms" % _max_gap_ms)
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
	var started := Time.get_ticks_usec()
	var parsed: Dictionary = JSON.parse_string(stats[1])
	print("    stats JSON %d KB parsed on the main thread in %.1f ms" % [stats[1].length() / 1024, (Time.get_ticks_usec() - started) / 1000.0])
	var folders: Dictionary = parsed["folders"]
	var parts: Array[String] = []
	for type_id in TYPE_IDS:
		parts.append("%s %d" % [type_id, int(folders.get(type_id, {}).get("count", 0))])
	print("    ", ", ".join(parts))
