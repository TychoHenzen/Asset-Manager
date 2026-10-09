@tool
class_name AssetToolbar
extends MarginContainer

## Emits intent; it never reads the database or touches the grid. main_screen
## decides what each one means.
signal sidebar_toggled(collapsed: bool)
## Pressed while a scan runs, it means stop.
signal scan_pressed
signal add_pressed(type_id: String)
signal search_changed(text: String)
signal settings_pressed

@onready var _sidebar_btn: Button = $HBox/SidebarButton
@onready var _add_btn: MenuButton = $HBox/AddButton
@onready var _scan_btn: Button = $HBox/ScanButton
@onready var _search_input: LineEdit = $HBox/SearchInput
@onready var _settings_btn: Button = $HBox/SettingsButton

var _sidebar_collapsed: bool = false

## Types in menu order, so an item's id maps straight back to one.
var _add_type_ids: Array[String] = []

func _ready() -> void:
	if EditorGuard.is_scene_tab(self):
		return

	_sidebar_btn.pressed.connect(_on_sidebar_button_pressed)
	_scan_btn.pressed.connect(func() -> void: scan_pressed.emit())
	_search_input.text_changed.connect(func(text: String) -> void: search_changed.emit(text))
	_settings_btn.pressed.connect(func() -> void: settings_pressed.emit())

	_build_add_menu()
	_apply_icons()
	_apply_spacing()
	_update_sidebar_icon(false)

func _build_add_menu() -> void:
	var popup := _add_btn.get_popup()
	_add_type_ids = TypeMenu.populate(popup)

	if not popup.id_pressed.is_connected(_on_add_type_selected):
		popup.id_pressed.connect(_on_add_type_selected)

func _on_add_type_selected(id: int) -> void:
	if id >= 0 and id < _add_type_ids.size():
		add_pressed.emit(_add_type_ids[id])

func _apply_spacing() -> void:
	if not Engine.is_editor_hint():
		return
	var spacing: int = EditorInterface.get_editor_settings().get_setting("interface/theme/base_spacing")
	var pad := int(spacing * EditorInterface.get_editor_scale())
	$HBox.add_theme_constant_override("separation", pad)
	add_theme_constant_override("margin_bottom", pad)

## Godot's own icons rather than drawn ones, so the toolbar re-themes with the
## editor and matches the docks either side of it.
func _apply_icons() -> void:
	_set_icon(_add_btn, "Add")
	_set_icon(_scan_btn, "Reload")
	_set_icon(_settings_btn, "Tools")

	# same treatment Godot gives its own filter fields (filesystem_dock.cpp:663)
	_search_input.right_icon = IconHelper.get_icon("Search")

func _set_icon(button: Button, icon_name: String) -> void:
	var icon := IconHelper.get_icon(icon_name)
	if icon != null:
		button.icon = icon
		button.text = ""

func _on_sidebar_button_pressed() -> void:
	_sidebar_collapsed = not _sidebar_collapsed
	_update_sidebar_icon(_sidebar_collapsed)
	sidebar_toggled.emit(_sidebar_collapsed)

## Shows the layout you're currently in, split while the sidebar is up, single
## pane once it's collapsed. Both are Godot's own layout icons.
func _update_sidebar_icon(collapsed: bool) -> void:
	var icon := IconHelper.get_icon("Panels1" if collapsed else "Panels2Alt")
	if icon != null:
		_sidebar_btn.icon = icon
		_sidebar_btn.text = ""
	_sidebar_btn.tooltip_text = "Show sidebar" if collapsed else "Hide sidebar"

## A full scan of a large library runs for minutes in the background, so the
## same button turns into the way to stop it. Add waits, its own scan of the
## new pack would be refused while one runs.
func set_scanning(scanning: bool) -> void:
	_add_btn.disabled = scanning
	_set_icon(_scan_btn, "Stop" if scanning else "Reload")
	_scan_btn.tooltip_text = "Stop scanning" if scanning else "Scan for new, changed and removed files"

func search_text() -> String:
	return _search_input.text

func set_search_text(text: String) -> void:
	_search_input.text = text

## Same as the AssetLib tab (asset_library_editor_plugin.cpp:1072): switching to
## the plugin puts the caret in the search box so you can just type.
func focus_search() -> void:
	_search_input.grab_focus()

## Cmd/Ctrl+F selects what's there rather than appending to it
## (asset_library_editor_plugin.cpp:1177).
func focus_search_and_select() -> void:
	_search_input.grab_focus()
	_search_input.select_all()
