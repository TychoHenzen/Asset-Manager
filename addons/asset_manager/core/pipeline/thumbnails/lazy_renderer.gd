@tool
class_name LazyThumbnailRenderer
extends RefCounted

## Renders thumbnails that need a live viewport (models, effects, materials,
## audio, ...), only for the page the grid is showing. Viewport capture is
## main-thread only (scene/main/node.h's thread guards), so this runs here and
## not in ThumbnailService. Each chunk is decoded across worker threads first,
## then captured, and every capture is followed by a frame so the editor keeps
## drawing. Jobs from a page the user has already left are dropped unrendered.

signal rendered(request_id: int, path: String, image: Image)
## No thumbnail could be made; the grid keeps the type icon.
signal failed(request_id: int, path: String)

const RENDERERS: Dictionary = {
	"models": preload("res://addons/asset_manager/core/types/models/thumb.gd"),
	"hdris": preload("res://addons/asset_manager/core/types/hdris/thumb.gd"),
	"themes": preload("res://addons/asset_manager/core/types/themes/thumb.gd"),
	"materials": preload("res://addons/asset_manager/core/types/materials/thumb.gd"),
	"scenes": preload("res://addons/asset_manager/core/types/scenes/thumb.gd"),
	"effects": preload("res://addons/asset_manager/core/types/effects/thumb.gd"),
	"shaders": preload("res://addons/asset_manager/core/types/shaders/thumb.gd"),
	"sounds": preload("res://addons/asset_manager/core/types/audio/thumb.gd"),
	"music": preload("res://addons/asset_manager/core/types/audio/thumb.gd"),
	"videos": preload("res://addons/asset_manager/core/types/videos/thumb.gd"),
}

## How many assets are decoded ahead of capture. Everything a chunk prepares is
## held in memory at once, and a 4K material is ~50MB per channel, so this is a
## memory ceiling, not a speed dial.
const PREPARE_CHUNK: int = 8

## A batch is one frame that must render every viewport in it, so more per
## batch is not simply better. Anything unlisted uses PREPARE_CHUNK.
const CHUNK_OVERRIDES: Dictionary = {
	"models": 16,
	"effects": 16,
}

var _host: Node
var _service: ThumbnailService
var _catalog: AssetCatalog
var _queue: Array[Dictionary] = []
var _current_request: int = 0
var _running: bool = false

## `host` is any node in the tree: a viewport outside the tree never renders.
func setup(host: Node, service: ThumbnailService, catalog: AssetCatalog) -> void:
	_host = host
	_service = service
	_catalog = catalog

## `job` is {request_id, path, type, size, mtime}, as NeedsRender sent it.
func enqueue(job: Dictionary) -> void:
	if int(job["request_id"]) < _current_request or not RENDERERS.has(job["type"]):
		return
	_queue.append(job)
	if not _running:
		_drain()

## A new page replaces the old one, whatever it had left goes unrendered.
func start_request(request_id: int) -> void:
	_current_request = request_id
	_queue = _queue.filter(func(job: Dictionary) -> bool: return int(job["request_id"]) >= request_id)

func _drain() -> void:
	_running = true
	var viewports: Dictionary = {}

	while not _queue.is_empty() and is_instance_valid(_host):
		var type_id: String = _queue[0]["type"]
		var chunk := _take_chunk(type_id)
		var viewport: ThumbnailViewport = viewports.get(type_id)
		if viewport == null:
			viewport = _make_viewport(type_id)
			viewports[type_id] = viewport
		await _render_chunk(chunk, viewport)
		_collect_subtypes()

	for viewport: ThumbnailViewport in viewports.values():
		viewport.teardown()
	# Scene resources are shared across a page's effects; released once the
	# page is done rather than held until the next one.
	TscnSceneLoader.clear_cache()
	_running = false

func _take_chunk(type_id: String) -> Array[Dictionary]:
	var size := int(CHUNK_OVERRIDES.get(type_id, PREPARE_CHUNK))
	var chunk: Array[Dictionary] = []
	var rest: Array[Dictionary] = []
	for job in _queue:
		if job["type"] == type_id and chunk.size() < size:
			chunk.append(job)
		else:
			rest.append(job)
	_queue = rest
	return chunk

## One viewport per type. Sharing one across types doesn't work: sky/fog
## captures reconfigure it in ways that cost other types seconds of recovery.
func _make_viewport(type_id: String) -> ThumbnailViewport:
	var renderer: Variant = RENDERERS[type_id]
	var viewport := ThumbnailViewport.new()
	if renderer.has_method("reuses_main_viewport"):
		viewport.reuse_main_viewport = renderer.reuses_main_viewport()
	if renderer.has_method("wants_animation"):
		viewport.animate = renderer.wants_animation()
	viewport.setup(_host, ThumbnailSizes.size_for(viewport.animate))
	return viewport

func _render_chunk(chunk: Array[Dictionary], viewport: ThumbnailViewport) -> void:
	var renderer: Variant = RENDERERS[chunk[0]["type"]]

	# Anything a renderer needs loaded on the main thread has to happen before
	# the workers start, see TscnSceneLoader.preload_binaries.
	if renderer.has_method("preload_main_thread"):
		for job in chunk:
			renderer.preload_main_thread(job["path"])

	await _prepare(chunk, renderer)

	# A renderer that can build its subject without capturing hands the whole
	# chunk over at once, so one drawn frame produces the lot.
	var batched: Array[Image] = []
	if renderer.has_method("build_subject"):
		var subjects: Array[Node3D] = []
		for job in chunk:
			subjects.append(renderer.build_subject(job.get("prepared"), job["path"]))
		batched = await viewport.capture_batch(subjects)

	for i in chunk.size():
		var job := chunk[i]
		var image: Image = batched[i] if i < batched.size() else null
		# A null here means the renderer declined to batch this one (a 2D
		# effect, say), it still needs its own capture.
		if image == null:
			image = await renderer.render_prepared(job.get("prepared"), viewport, job["path"])
		job.erase("prepared")
		if image == null:
			push_warning("AssetManager: no thumbnail generated for ", job["path"])
			failed.emit(int(job["request_id"]), job["path"])
			continue
		_service.Store(job["path"], job["type"], job["size"], job["mtime"], image, ThumbnailSizes.size_for(viewport.animate))
		rendered.emit(int(job["request_id"]), job["path"], image)

	await _host.get_tree().process_frame

## Decodes a chunk in parallel. A renderer that fails leaves a null in
## "prepared", which render_prepared turns into a skipped thumbnail.
func _prepare(chunk: Array[Dictionary], renderer: Variant) -> void:
	var group_id := WorkerThreadPool.add_group_task(
		func(i: int) -> void: chunk[i]["prepared"] = renderer.prepare(chunk[i]["path"]),
		chunk.size()
	)
	while not WorkerThreadPool.is_group_task_completed(group_id):
		await _host.get_tree().process_frame
	WorkerThreadPool.wait_for_group_task_completion(group_id)

## Renderers that detect a subtype while capturing park it per path; the
## catalog stores it so the subtype filter can find the asset.
func _collect_subtypes() -> void:
	var paths := PackedStringArray()
	var subtypes := PackedStringArray()
	for renderer: Variant in RENDERERS.values():
		if not renderer.has_method("take_subtype"):
			continue
		var found: Dictionary = renderer.take_subtype()
		for path: String in found:
			paths.append(path)
			subtypes.append(",".join(PackedStringArray(found[path])))
	if not paths.is_empty():
		_catalog.SetSubtypes(paths, subtypes)
