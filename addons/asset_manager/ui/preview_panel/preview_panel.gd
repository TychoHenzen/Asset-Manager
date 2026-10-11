@tool
class_name PreviewPanel
extends PanelContainer

signal send_to_project_pressed
signal remove_from_project_pressed
signal version_selected(path: String)
signal open_external_pressed
signal open_location_pressed
signal add_tag_requested(tag_text: String)
signal remove_tag_requested(tag_text: String)
signal tag_search_changed

const TYPE_SCENES: Dictionary = {
	"models": preload("res://addons/asset_manager/ui/preview_panel/types/models/models.tscn"),
	"sounds": preload("res://addons/asset_manager/ui/preview_panel/types/audio/audio.tscn"),
	"music": preload("res://addons/asset_manager/ui/preview_panel/types/audio/audio.tscn"),
	"images": preload("res://addons/asset_manager/ui/preview_panel/types/images/images.tscn"),
	"videos": preload("res://addons/asset_manager/ui/preview_panel/types/videos/videos.tscn"),
	"hdris": preload("res://addons/asset_manager/ui/preview_panel/types/hdris/hdris.tscn"),
	"themes": preload("res://addons/asset_manager/ui/preview_panel/types/themes/themes.tscn"),
	"materials": preload("res://addons/asset_manager/ui/preview_panel/types/materials/materials.tscn"),
	"shaders": preload("res://addons/asset_manager/ui/preview_panel/types/shaders/shaders.tscn"),
	"effects": preload("res://addons/asset_manager/ui/preview_panel/types/effects/effects.tscn"),
	"scenes": preload("res://addons/asset_manager/ui/preview_panel/types/scenes/scenes.tscn"),
}
const OTHER_SCENE := preload("res://addons/asset_manager/ui/preview_panel/types/other/other.tscn")

## Wider than the sidebar because it holds a viewport rather than a tree, but
## only a little, the grid is what the space is for. Same minimum as the sidebar.
const BASE_MIN_WIDTH: int = 170
const BASE_DEFAULT_WIDTH: int = -320

var _bodies: Dictionary = {}
var _current_type: String = ""
## The main button sends an asset that isn't in the project yet and removes
## one that is.
var _in_project: bool = false
var _catalog: AssetCatalog
## Picks which version of the asset the panel shows and sends.
var _version_option: OptionButton

@onready var _stage: PanelContainer = $VBox/Stage
@onready var _slot: Control = $VBox/Stage/PreviewSlot
@onready var _card: PanelContainer = $VBox/Card
@onready var _name_label: Label = $VBox/Card/CardBox/Meta/FileNameLabel
@onready var _detail_label: Label = $VBox/Card/CardBox/Meta/DetailLabel
@onready var _static_preview_badge: MarginContainer = $VBox/Stage/StaticPreviewBadge
@onready var _tag_editor: TagEditor = $VBox/Card/CardBox/TagEditor
@onready var _send_button: Button = $VBox/Actions/SendToProjectButton
@onready var _external_button: Button = $VBox/Actions/OpenExternalButton
@onready var _location_button: Button = $VBox/Actions/OpenLocationButton

func _ready() -> void:
	if EditorGuard.is_scene_tab(self):
		return

	_stage.gui_input.connect(_on_stage_gui_input)

	_send_button.pressed.connect(_on_send_button_pressed)
	_external_button.pressed.connect(func() -> void: open_external_pressed.emit())
	_location_button.pressed.connect(func() -> void: open_location_pressed.emit())

	_tag_editor.add_tag_requested.connect(func(t: String) -> void: add_tag_requested.emit(t))
	_tag_editor.remove_tag_requested.connect(func(t: String) -> void: remove_tag_requested.emit(t))
	_tag_editor.search_text_changed.connect(func() -> void: tag_search_changed.emit())

	_version_option = OptionButton.new()
	_version_option.tooltip_text = "Which version of this asset to preview and send"
	_version_option.clip_text = true
	_version_option.fit_to_longest_item = false
	_version_option.visible = false
	_version_option.item_selected.connect(_on_version_picked)
	_detail_label.add_sibling(_version_option)

	if Engine.is_editor_hint():
		custom_minimum_size = Vector2(BASE_MIN_WIDTH * EditorInterface.get_editor_scale(), 0)

	_apply_style()

## Send to Project (or Remove, see set_in_project) is what the panel is for, so it
## takes the width and the accent icon. Reveal and open-external are occasional, so they shrink to icons.
func _apply_style() -> void:
	if not Engine.is_editor_hint():
		return
	var theme := EditorInterface.get_editor_theme()
	if theme == null:
		return

	if theme.has_stylebox("panel", "TreeSecondary"):
		add_theme_stylebox_override("panel", theme.get_stylebox("panel", "TreeSecondary"))


	_set_icon(_send_button, "Add")
	_set_icon(_external_button, "ExternalLink", true)
	_set_icon(_location_button, "Folder", true)

	if theme.has_color("font_disabled_color", "Editor"):
		_detail_label.add_theme_color_override("font_color", theme.get_color("font_disabled_color", "Editor"))

	if theme.has_color("warning_color", "Editor"):
		var warning := theme.get_color("warning_color", "Editor")
		for label in _static_preview_badge.find_children("*", "Label"):
			label.add_theme_color_override("font_color", warning)

	# Every Label in the editor theme carries its own content margins
	# (theme_modern.cpp:1239), top and bottom padding inside the label itself,
	# which container separation can't reach. Two stacked labels therefore sit
	# four margins apart however tightly the container is set.
	var flush := StyleBoxEmpty.new()
	_name_label.add_theme_stylebox_override("normal", flush)
	_detail_label.add_theme_stylebox_override("normal", flush)

	var spacing: int = EditorInterface.get_editor_settings().get_setting("interface/theme/base_spacing")
	$VBox.add_theme_constant_override("separation", int(spacing * EditorInterface.get_editor_scale()))

	_style_card(theme, spacing)

## A tier below the panel, not another copy of it, the card has to read as
## sitting inside the panel rather than as a second panel butted against it.
func _style_card(theme: Theme, spacing: int) -> void:
	var scale := EditorInterface.get_editor_scale()
	var radius: int = EditorInterface.get_editor_settings().get_setting("interface/theme/corner_radius")

	var box := StyleBoxFlat.new()
	if theme.has_color("base_color", "Editor"):
		box.bg_color = theme.get_color("base_color", "Editor")
	box.set_corner_radius_all(int(radius * scale))

	var pad := int(spacing * 2 * scale)
	box.content_margin_left = pad
	box.content_margin_right = pad
	box.content_margin_top = pad
	box.content_margin_bottom = pad

	_card.add_theme_stylebox_override("panel", box)
	$VBox/Card/CardBox.add_theme_constant_override("separation", int(spacing * scale))

## clip_contents only clips rectangles, so a SubViewport's texture lands square.
## CLIP_CHILDREN_ONLY instead makes this node a mask from its own alpha, the
## rounded stylebox it draws becomes the shape its children are clipped to.
func _round_stage_corners() -> void:
	var radius: int = EditorInterface.get_editor_settings().get_setting("interface/theme/corner_radius")
	if radius <= 0:
		return

	var mask := StyleBoxFlat.new()
	mask.bg_color = Color.WHITE
	mask.set_corner_radius_all(int(radius * EditorInterface.get_editor_scale()))

	_stage.add_theme_stylebox_override("panel", mask)
	_stage.clip_children = CanvasItem.CLIP_CHILDREN_ONLY

## icon_only is coupled to the icon actually resolving, clearing the text when
## the lookup failed would leave a button with neither.
func _set_icon(button: Button, icon_name: String, icon_only: bool = false) -> void:
	var icon := IconHelper.get_icon(icon_name)
	if icon == null:
		return
	button.icon = icon
	if icon_only:
		button.text = ""

## One body per scene, not per type, sounds and music are both AudioPreview, so
## they share a single instance keyed under both ids.
func setup(settings: SettingsManager) -> void:
	var by_scene: Dictionary = {}

	for type_id: String in TYPE_SCENES:
		var scene: PackedScene = TYPE_SCENES[type_id]

		if not by_scene.has(scene):
			var body: Control = scene.instantiate()
			_slot.add_child(body)
			if body.has_method("setup"):
				body.setup(settings)
			body.visible = false
			by_scene[scene] = body

		_bodies[type_id] = by_scene[scene]

	var other: Control = OTHER_SCENE.instantiate()
	_slot.add_child(other)
	other.visible = false
	_bodies["other"] = other

	# Last, so it draws over every preview body rather than under them.
	_round_stage_corners()

func show_asset(path: String, type_id: String) -> void:
	hide_current()

	_name_label.text = path.get_file()
	_detail_label.text = _describe(path, type_id)
	_current_type = type_id

	TscnSceneLoader.scripts_skipped = false

	var body: Control = _bodies.get(type_id, _bodies["other"])
	body.show_asset(path, AssetTypes.get_by_id(type_id))
	_static_preview_badge.visible = TscnSceneLoader.scripts_skipped

## Pack, size and type on one line, the things you'd check before sending an
## asset to a project. The size is the scan's: opening the file to ask would
## be a network round trip on the main thread.
func _describe(path: String, type_id: String) -> String:
	var parts: PackedStringArray = []

	if _catalog != null:
		parts.append(String.humanize_size(_catalog.SizeOf(path)))

	parts.append(path.get_extension().to_upper())

	var label: String = AssetTypes.get_by_id(type_id).get("label", type_id)
	parts.append(label)

	return " · ".join(parts)

func hide_current() -> void:
	if _current_type.is_empty():
		return
	var body: Control = _bodies.get(_current_type, _bodies["other"])
	body.hide_asset()
	_current_type = ""

func clear() -> void:
	hide_current()
	_name_label.text = "No asset selected"
	_detail_label.text = ""
	_static_preview_badge.visible = false
	set_versions("", [], [])

## The asset's versions (formats, exact copies), `current` selected. Hidden
## when there is only the one. Each is labelled by format and by where it sits
## below the folder all of them share, since two can share a format
## (`GLB format/` and `GLTF format/` both hold .glb files).
func set_versions(current: String, paths: PackedStringArray, sizes: PackedInt64Array) -> void:
	if _version_option == null:
		return
	_version_option.clear()
	_version_option.visible = paths.size() > 1
	if paths.size() <= 1:
		return

	var shared := _shared_folder(paths)
	for i in paths.size():
		var path := paths[i]
		var label := "%s - %s - %s" % [path.get_extension(), path.trim_prefix(shared).trim_prefix("/"), String.humanize_size(sizes[i])]
		_version_option.add_item(label)
		_version_option.set_item_metadata(i, path)
		_version_option.set_item_tooltip(i, path)
		if path == current:
			_version_option.select(i)

func _on_version_picked(index: int) -> void:
	version_selected.emit(String(_version_option.get_item_metadata(index)))

static func _shared_folder(paths: PackedStringArray) -> String:
	var shared := paths[0].get_base_dir()
	for path in paths:
		while not shared.is_empty() and not path.begins_with(shared + "/"):
			shared = shared.get_base_dir() if shared.get_base_dir() != shared else ""
	return shared

func set_actions_enabled(enabled: bool) -> void:
	_send_button.disabled = not enabled
	_external_button.disabled = not enabled
	_location_button.disabled = not enabled
	_tag_editor.set_input_editable(enabled)

func refresh_tags(selected_paths: Array) -> void:
	_tag_editor.refresh_ui(selected_paths)

func apply_tag_change(selected_paths: Array, tag_text: String, add: bool) -> void:
	_tag_editor.apply_tag_change(selected_paths, tag_text, add)

func set_catalog(catalog: AssetCatalog) -> void:
	_catalog = catalog
	_tag_editor.catalog = catalog

## A no-op when nothing changed: filesystem_changed lands a few frames after a
## send, and rewriting the label then would wipe the send's summary.
func set_in_project(present: bool) -> void:
	if present == _in_project:
		return
	_in_project = present
	_send_button.text = _send_button_label()
	_set_icon(_send_button, "Remove" if present else "Add")

## Restores the label for whatever the asset's state is by then, not the one
## that was showing: a send or remove changes it under the flash.
func flash_send_result(summary: String) -> void:
	_send_button.text = summary
	get_tree().create_timer(2.5).timeout.connect(func() -> void: _send_button.text = _send_button_label())

func _send_button_label() -> String:
	return "Remove from Project" if _in_project else "Send to Project"

func _on_send_button_pressed() -> void:
	if _in_project:
		remove_from_project_pressed.emit()
	else:
		send_to_project_pressed.emit()

func _on_stage_gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		_tag_editor.on_preview_clicked()

	var body: Variant = _bodies.get(_current_type, null)
	if body != null and body.has_method("handle_gui_input"):
		body.handle_gui_input(event)
