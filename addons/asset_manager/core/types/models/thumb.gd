@tool
extends RefCounted

## Thumbnail renderer for models. Loads through ModelLoader, the same path as
## the preview, then hands the node tree to the shared viewport for capture.
## Parsing runs on a worker; only the capture is main-thread. Safe because
## GLTFDocument's only shared state is its document-extension list, mutexed
## (gltf_document.cpp:6965), and embedded-texture creation goes through
## RenderingServer::texture_2d_create (rendering_server_default.h:147), which
## pushes onto the command queue when called off the render thread.
## A model that fails on the worker is not retried on the main thread: loading
## it there reads the file and its pack from the share and stalls the editor.

static func prepare(path: String) -> Variant:
	return ModelLoader.load_model(path)

## Models only capture plain geometry, nothing here reconfigures the viewport
## like sky/fog, the batch can use the main one as a slot.
static func reuses_main_viewport() -> bool:
	return true

## Already parsed by prepare() on a worker, hands it over so
## LazyThumbnailRenderer can render a whole chunk in one frame
## (ThumbnailViewport.capture_batch). Null when prepare() failed.
static func build_subject(prepared: Variant, _path: String) -> Node3D:
	return prepared as Node3D if is_instance_valid(prepared) and prepared is Node3D else null

## Only reached when the batch produced nothing for this model. The batch owns
## and frees its subjects, so a node it already freed is not captured again.
static func render_prepared(prepared: Variant, viewport: ThumbnailViewport, _path: String = "") -> Image:
	if not is_instance_valid(prepared) or not (prepared is Node3D):
		return null
	return await viewport.capture(prepared as Node3D)
