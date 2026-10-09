@tool
class_name ThumbnailLoader
extends Node

## Thumbnails for one grid page at a time. Cache hits and image types come
## back from ThumbnailService's worker threads; the rest are rendered by
## LazyThumbnailRenderer on the main thread. Either way the grid gets a
## texture per path, tagged with the request it belongs to.

signal thumbnail_ready(request_id: int, path: String, texture: Texture2D)
## Nothing could be rendered for this asset.
signal thumbnail_failed(request_id: int, path: String)

var _service: ThumbnailService
var _renderer: LazyThumbnailRenderer

func setup(workspace_path: String, catalog: AssetCatalog) -> void:
	_shutdown()
	_service = ThumbnailService.new()
	_service.Setup(workspace_path)
	# Bound methods only: a lambda connected to a C# signal crashes Godot on
	# exit (see AssetCatalog).
	_service.ThumbnailReady.connect(_on_thumbnail_ready)
	_service.NeedsRender.connect(_on_needs_render)

	_renderer = LazyThumbnailRenderer.new()
	_renderer.setup(self, _service, catalog)
	_renderer.rendered.connect(_on_rendered)
	_renderer.failed.connect(thumbnail_failed.emit)

## The arrays are one QueryReady page.
func request(request_id: int, paths: PackedStringArray, types: PackedStringArray, sizes: PackedInt64Array, mtimes: PackedInt64Array) -> void:
	if _service == null:
		return
	_renderer.start_request(request_id)
	_service.RequestPage(request_id, paths, types, sizes, mtimes)

func _exit_tree() -> void:
	_shutdown()

func _shutdown() -> void:
	if _service != null:
		_service.Shutdown()
	_service = null
	_renderer = null

func _on_thumbnail_ready(request_id: int, path: String, webp: PackedByteArray) -> void:
	var image := Image.new()
	if image.load_webp_from_buffer(webp) == OK:
		thumbnail_ready.emit(request_id, path, ImageTexture.create_from_image(image))

func _on_needs_render(request_id: int, path: String, type_id: String, size: int, mtime: int) -> void:
	if _renderer != null:
		_renderer.enqueue({"request_id": request_id, "path": path, "type": type_id, "size": size, "mtime": mtime})

func _on_rendered(request_id: int, path: String, image: Image) -> void:
	thumbnail_ready.emit(request_id, path, ImageTexture.create_from_image(image))
