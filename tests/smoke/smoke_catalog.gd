extends SceneTree

## Headless check of the catalog against a real workspace. Run:
##   godot --headless --path <repo> -s res://tests/smoke/smoke_catalog.gd -- <workspace>
## Proves the main thread keeps ticking during a full scan (the original addon
## froze the editor), that a rescan of an unchanged library changes nothing,
## that a query answers fast, and that a page of thumbnails loads off-thread.
## The incremental-rescan check runs on a local temp workspace so it never
## writes test files into the real library.

const MAX_FRAME_GAP_MS: int = 100
const MAX_QUERY_MS: int = 1000
const THUMBNAIL_WAIT_MS: int = 120000

var _last_tick_usec: int = 0
var _max_gap_ms: float = 0.0
var _measuring: bool = false
var _failures: Array[String] = []
var _thumb_state := {"ready": 0, "render": 0}
var _types_json := JSON.stringify(AssetTypes.ALL)

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
	if args.is_empty():
		push_error("usage: -s smoke_catalog.gd -- <workspace>")
		quit(2)
		return

	await _check_incremental_on_temp_workspace()
	await _check_workspace(String(args[0]).replace("\\", "/"))

	print("")
	if _failures.is_empty():
		print("SMOKE PASS")
		quit(0)
	else:
		for failure in _failures:
			print("FAIL: ", failure)
		quit(1)

func _check_workspace(workspace: String) -> void:
	print("== workspace ", workspace)
	var catalog := AssetCatalog.new()
	catalog.Setup(workspace, _types_json)

	_start_measuring()
	catalog.Open()
	var opened: Array = await catalog.Opened
	print("opened: %d assets, needs scan: %s, max frame gap %.1f ms" % [opened[0], opened[1], _max_gap_ms])

	_start_measuring()
	var started := Time.get_ticks_msec()
	catalog.Scan(workspace)
	var summary: Array = await catalog.ScanFinished
	_measuring = false
	print("full scan: added %d changed %d removed %d total %d in %.1f s, max frame gap %.1f ms"
		% [summary[0], summary[1], summary[2], summary[3], summary[6], _max_gap_ms])
	_expect(_max_gap_ms < MAX_FRAME_GAP_MS, "main thread stalled %.1f ms during full scan" % _max_gap_ms)

	catalog.Scan(workspace)
	var rescan: Array = await catalog.ScanFinished
	print("rescan: added %d changed %d removed %d in %.1f s" % [rescan[0], rescan[1], rescan[2], rescan[6]])
	_expect(rescan[0] == 0 and rescan[1] == 0, "unchanged rescan reported changes: %s" % [rescan])

	started = Time.get_ticks_msec()
	catalog.Query(7, JSON.stringify({"prefix": workspace + "/images", "sort": 2, "page": 0, "per_page": 100}))
	var page: Array = await catalog.QueryReady
	var query_ms := Time.get_ticks_msec() - started
	print("query images by size: %d of %d in %d ms" % [page[1].size(), page[5], query_ms])
	_expect(query_ms < MAX_QUERY_MS, "query took %d ms" % query_ms)

	started = Time.get_ticks_msec()
	catalog.Stats(8, "")
	await catalog.StatsReady
	print("stats in %d ms" % (Time.get_ticks_msec() - started))

	await _check_thumbnails(workspace, page)
	catalog.Shutdown()

## `page` is a QueryReady payload: id, paths, types, sizes, mtimes, ...
func _check_thumbnails(workspace: String, page: Array) -> void:
	var thumbs := ThumbnailService.new()
	thumbs.Setup(workspace)
	_thumb_state = {"ready": 0, "render": 0}
	thumbs.ThumbnailReady.connect(_on_thumbnail_ready)
	thumbs.NeedsRender.connect(_on_needs_render)

	var count: int = page[1].size()
	_start_measuring()
	var started := Time.get_ticks_msec()
	thumbs.RequestPage(1, page[1], page[2], page[3], page[4])
	while _thumb_state["ready"] + _thumb_state["render"] < count and Time.get_ticks_msec() - started < THUMBNAIL_WAIT_MS:
		await process_frame
	_measuring = false
	print("thumbnails: %d ready, %d need a viewport, of %d in %.1f s, max frame gap %.1f ms"
		% [_thumb_state["ready"], _thumb_state["render"], count, (Time.get_ticks_msec() - started) / 1000.0, _max_gap_ms])
	_expect(_thumb_state["ready"] + _thumb_state["render"] > 0, "no thumbnails came back")
	_expect(_max_gap_ms < MAX_FRAME_GAP_MS, "main thread stalled %.1f ms loading thumbnails" % _max_gap_ms)
	thumbs.Shutdown()

func _check_incremental_on_temp_workspace() -> void:
	var root := OS.get_temp_dir().path_join("am-smoke-%d" % Time.get_ticks_usec()).replace("\\", "/")
	_write(root.path_join("images/vendor/pack_a/one.png"))
	_write(root.path_join("images/vendor/pack_a/two.png"))
	print("== incremental on ", root)

	var catalog := AssetCatalog.new()
	catalog.Setup(root, _types_json)
	catalog.Open()
	var opened: Array = await catalog.Opened
	_expect(opened[1] == true, "fresh temp workspace did not ask for a scan")

	catalog.Scan(root)
	var first: Array = await catalog.ScanFinished
	_expect(first[0] == 2, "first scan added %d, expected 2" % first[0])

	_write(root.path_join("images/vendor/pack_b/three.png"))
	catalog.Scan(root.path_join("images/vendor/pack_b"))
	var scoped: Array = await catalog.ScanFinished
	print("scoped rescan: added %d removed %d total %d" % [scoped[0], scoped[2], scoped[3]])
	_expect(scoped[0] == 1 and scoped[2] == 0 and scoped[3] == 3, "scoped rescan of a new pack: %s" % [scoped])

	# A vendor marker added later retags the vendor's unchanged files.
	VendorMarker.ensure(root.path_join("images/vendor"))
	catalog.Scan(root)
	var marked: Array = await catalog.ScanFinished
	print("rescan after marker: changed %d retagged %d" % [marked[1], marked[7]])
	_expect(marked[1] == 0 and marked[7] == 3, "marker rescan: %s" % [marked])
	_expect(Array(catalog.TagsOf(root.path_join("images/vendor/pack_a/one.png"))) == ["images", "vendor", "pack_a"],
		"marked tags: %s" % [catalog.TagsOf(root.path_join("images/vendor/pack_a/one.png"))])

	var reopened := AssetCatalog.new()
	reopened.Setup(root, _types_json)
	reopened.Open()
	var again: Array = await reopened.Opened
	_expect(again[0] == 3 and again[1] == false, "reopened catalog has %d assets" % again[0])

	# A tag edit writes tags.bin only; catalog.bin keeps its bytes.
	var catalog_bin := root.path_join(".assetmanager/catalog.bin")
	var catalog_bytes := FileAccess.get_file_as_bytes(catalog_bin)
	var tags_bin := root.path_join(".assetmanager/tags.bin")
	var tagged := root.path_join("images/vendor/pack_a/one.png")
	reopened.SetTags(tagged, PackedStringArray(["hero"]))
	var waited := 0
	while not FileAccess.file_exists(tags_bin) and waited < 300:
		waited += 1
		await process_frame
	_expect(FileAccess.file_exists(tags_bin), "SetTags did not write tags.bin")
	_expect(FileAccess.get_file_as_bytes(catalog_bin) == catalog_bytes, "SetTags rewrote catalog.bin")

	var third := AssetCatalog.new()
	third.Setup(root, _types_json)
	third.Open()
	await third.Opened
	_expect(Array(third.TagsOf(tagged)) == ["hero"], "tag did not survive a reopen: %s" % [third.TagsOf(tagged)])

	catalog.Shutdown()
	reopened.Shutdown()
	third.Shutdown()

## Bound methods, not lambdas: see the note on AssetCatalog.
func _on_thumbnail_ready(_id: int, _path: String, webp: PackedByteArray) -> void:
	if Image.new().load_webp_from_buffer(webp) == OK:
		_thumb_state["ready"] += 1

func _on_needs_render(_id: int, _path: String, _type: String, _size: int, _mtime: int) -> void:
	_thumb_state["render"] += 1

func _write(path: String) -> void:
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	var image := Image.create_empty(4, 4, false, Image.FORMAT_RGBA8)
	image.fill(Color.RED)
	image.save_png(path)

func _start_measuring() -> void:
	_max_gap_ms = 0.0
	_last_tick_usec = 0
	_measuring = true

func _expect(ok: bool, message: String) -> void:
	if not ok:
		_failures.append(message)
