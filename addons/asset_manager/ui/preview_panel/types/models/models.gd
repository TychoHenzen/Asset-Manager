@tool
class_name ModelsPreview
extends Control

@onready var _viewport_container: SubViewportContainer = $SubViewportContainer
@onready var _viewport: SubViewport = $SubViewportContainer/SubViewport
@onready var _camera: Camera3D = $SubViewportContainer/SubViewport/Camera3D
@onready var _pivot: Node3D = $SubViewportContainer/SubViewport/ModelPivot
@onready var _toolbar: Control = $ViewportToolbar
@onready var _reset_origin_btn: Button = $ViewportToolbar/HBox/ResetOriginBtn
@onready var _light_mode_btn: OptionButton = $ViewportToolbar/HBox/LightModeBtn
@onready var _grid_toggle_btn: Button = $ViewportToolbar/HBox/GridToggleBtn

var _settings: SettingsManager

var _loaded_node: Node3D = null
var _light_mode_idx: int = 0
var _orbit := PreviewOrbitCamera.new()
var _grid := PreviewReferenceGrid.new()

func _ready() -> void:
	if EditorGuard.is_scene_tab(self):
		return

	var clean_env := Environment.new()
	clean_env.background_mode = Environment.BG_COLOR
	clean_env.background_color = PreviewTheme.background()
	clean_env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	clean_env.ambient_light_color = Color(1.0, 1.0, 1.0)
	clean_env.ambient_light_energy = 0.4
	clean_env.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	clean_env.glow_enabled = false

	var isolated_world := World3D.new()
	isolated_world.environment = clean_env
	_viewport.world_3d = isolated_world

	_viewport_container.mouse_filter = Control.MOUSE_FILTER_IGNORE

	for mode in ["Lit", "Unlit", "Wireframe"]:
		_light_mode_btn.add_item(mode)
	_light_mode_btn.item_selected.connect(_on_light_mode_selected)
	_grid_toggle_btn.toggled.connect(_on_grid_toggled)
	# origin restores the whole view, not just the pan pivot, resetting one
	# without the other leaves you recentred but still zoomed into nothing
	_reset_origin_btn.pressed.connect(func() -> void:
		_orbit.reset_origin()
		_orbit.reset_zoom()
	)

	_apply_toolbar_icons()

	_orbit.setup(_camera, _pivot)
	_grid.setup(_pivot)
	_grid.set_enabled(_grid_toggle_btn.button_pressed)

func _apply_toolbar_icons() -> void:
	IconHelper.apply(_reset_origin_btn, "CenterView")
	IconHelper.apply(_grid_toggle_btn, "GridToggle")

func setup(p_settings: SettingsManager) -> void:
	_settings = p_settings
	set_light_mode_idx(_settings.get_light_mode_idx())
	set_grid_enabled(_settings.get_preview_grid_visible())

func show_asset(path: String, _type_entry: Dictionary = {}) -> void:
	visible = true
	_toolbar.visible = true
	_grid.set_visible(true)
	_orbit.snap_to_look_at()

	_loaded_node = ModelLoader.load_model(path)
	if _loaded_node:
		_pivot.add_child(_loaded_node)
		_center_and_scale(_loaded_node)

func hide_asset() -> void:
	visible = false
	_toolbar.visible = false
	_grid.set_visible(false)
	_orbit.reset_views()

	if is_instance_valid(_loaded_node):
		_loaded_node.queue_free()
		_loaded_node = null

func handle_gui_input(event: InputEvent) -> void:
	_orbit.handle_input(event)

func get_grid_enabled() -> bool:
	return _grid.is_enabled()

func get_light_mode_idx() -> int:
	return _light_mode_idx

func _on_grid_toggled(enabled: bool) -> void:
	_grid.set_enabled(enabled)
	if _settings:
		_settings.set_preview_grid_visible(enabled)

func _on_light_mode_selected(idx: int) -> void:
	set_light_mode_idx(idx)
	if _settings:
		_settings.set_light_mode_idx(_light_mode_idx)

func set_light_mode_idx(idx: int) -> void:
	_light_mode_idx = idx
	_light_mode_btn.selected = idx
	match _light_mode_idx:
		0:
			_viewport.debug_draw = Viewport.DEBUG_DRAW_DISABLED
		1:
			_viewport.debug_draw = Viewport.DEBUG_DRAW_UNSHADED
		2:
			_viewport.debug_draw = Viewport.DEBUG_DRAW_WIREFRAME

func set_grid_enabled(enabled: bool) -> void:
	_grid_toggle_btn.button_pressed = enabled
	_grid.set_enabled(enabled)

func _center_and_scale(node: Node3D) -> void:
	var meshes: Array[MeshInstance3D] = []
	_find_meshes_recursive(node, meshes)

	if meshes.is_empty():
		return

	var aabb := AABB()
	var first := true

	for mi in meshes:
		var mi_aabb := mi.get_aabb()
		var xform := node.global_transform.affine_inverse() * mi.global_transform
		var xformed_aabb := xform * mi_aabb

		if first:
			aabb = xformed_aabb
			first = false
		else:
			aabb = aabb.merge(xformed_aabb)

	var max_size: float = maxf(aabb.size.x, maxf(aabb.size.y, aabb.size.z))
	if max_size > 0:
		var target_scale: float = 2.0 / max_size
		node.scale = Vector3(target_scale, target_scale, target_scale)

	var center_offset: Vector3 = aabb.get_center() * node.scale
	node.position = -center_offset

func _find_meshes_recursive(node: Node, result: Array[MeshInstance3D]) -> void:
	if node is MeshInstance3D:
		result.append(node)
	for child in node.get_children():
		_find_meshes_recursive(child, result)

