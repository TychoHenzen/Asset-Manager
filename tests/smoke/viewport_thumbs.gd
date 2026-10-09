extends SceneTree

## Windowed check of the thumbnail path that needs a viewport. Headless runs
## use a dummy renderer and cannot capture, so run this WITHOUT --headless:
##   godot --path <repo> -s res://tests/smoke/viewport_thumbs.gd -- <workspace> [type[:page]...]
## For each type (default: models sounds) it requests one page through
## ThumbnailLoader, waits for textures, measures main-thread frame gaps, then
## checks the rendered thumbnails were written to the cache.
## The first pages of a material type ever rendered on a machine can stall for
## a second or two while the GPU driver compiles pipelines; the engine caches
## them, so a rerun on fresh pages is the fair measure.

const PAGE_SIZE: int = 24
const WAIT_MS: int = 180000
const MAX_FRAME_GAP_MS: int = 250

var _last_tick_usec: int = 0
var _max_gap_ms: float = 0.0
var _measuring: bool = false
var _arrived: Dictionary = {}
var _failed: Dictionary = {}
var _failures: Array[String] = []
var _paths: ThumbnailService

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
	var types: Array = Array(args.slice(1)) if args.size() > 1 else ["models", "sounds"]

	var catalog := AssetCatalog.new()
	catalog.Setup(workspace, JSON.stringify(AssetTypes.ALL))
	catalog.Open()
	await catalog.Opened

	# Only asked for paths, nothing connected, so no shutdown concerns.
	_paths = ThumbnailService.new()
	_paths.Setup(workspace)

	var loader := ThumbnailLoader.new()
	root.add_child(loader)
	loader.setup(workspace, catalog)
	loader.thumbnail_ready.connect(_on_thumbnail_ready)
	loader.thumbnail_failed.connect(_on_thumbnail_failed)

	var request_id := 100
	for spec: String in types:
		var type_id := spec.get_slice(":", 0)
		var page_index := int(spec.get_slice(":", 1)) if spec.contains(":") else 0
		request_id += 1
		catalog.Query(request_id, JSON.stringify({"prefix": workspace.path_join(type_id), "page": page_index, "per_page": PAGE_SIZE}))
		var page: Array = await catalog.QueryReady
		var paths: PackedStringArray = page[1]
		_arrived.clear()
		_failed.clear()
		_max_gap_ms = 0.0
		_last_tick_usec = 0
		_measuring = true
		var started := Time.get_ticks_msec()
		loader.request(request_id, paths, page[2], page[3], page[4])
		while _arrived.size() + _failed.size() < paths.size() and Time.get_ticks_msec() - started < WAIT_MS:
			await process_frame
		_measuring = false

		# Store() writes on a worker; give it a moment before looking.
		await create_timer(2.0).timeout
		var cached := 0
		for i in paths.size():
			if FileAccess.file_exists(_paths.ThumbPath(paths[i], page[2][i], page[3][i], page[4][i])):
				cached += 1

		print("%s: %d of %d textures (%d failed) in %.1f s, %d cached on disk, max frame gap %.1f ms"
			% [type_id, _arrived.size(), paths.size(), _failed.size(), (Time.get_ticks_msec() - started) / 1000.0, cached, _max_gap_ms])
		if paths.is_empty():
			continue
		if _arrived.is_empty():
			_failures.append("%s: no textures arrived" % type_id)
		if cached == 0:
			_failures.append("%s: nothing written to the thumbnail cache" % type_id)
		if _max_gap_ms > MAX_FRAME_GAP_MS:
			_failures.append("%s: main thread stalled %.1f ms" % [type_id, _max_gap_ms])

	loader.queue_free()
	catalog.Shutdown()
	await process_frame
	for failure in _failures:
		print("FAIL: ", failure)
	print("VIEWPORT SMOKE PASS" if _failures.is_empty() else "VIEWPORT SMOKE FAIL")
	quit(0 if _failures.is_empty() else 1)

func _on_thumbnail_ready(_request_id: int, path: String, _texture: Texture2D) -> void:
	_arrived[path] = true

func _on_thumbnail_failed(_request_id: int, path: String) -> void:
	_failed[path] = true
