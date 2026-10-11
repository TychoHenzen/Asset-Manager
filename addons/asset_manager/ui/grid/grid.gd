@tool
class_name AssetGrid
extends PanelContainer

## The grid's only outward contract. Listeners bind to this signal rather
## than reaching into ItemList, so the widget underneath can change without
## touching the preview or the tag editor.
signal selection_changed(path: String)
signal item_activated(path: String)
signal open_location_requested
signal open_external_requested
signal send_to_project_requested
signal remove_from_project_requested
signal add_requested(type_id: String)

var catalog: AssetCatalog
var settings: SettingsManager
var thumbnails: ThumbnailLoader

var _animator: ThumbnailAnimator = ThumbnailAnimator.new()
var _blank_texture: ImageTexture

var _filter: Dictionary = {}
var _subtype: String = ""
var _sort_mode: int = GridSubbar.Sort.NAME
var _page: int = 0
var _total_pages: int = 1

## Queries answer asynchronously and only the newest one is drawn. Thumbnails
## carry the same id, so a late one for a page already left is ignored.
var _request_id: int = 0
var _item_by_path: Dictionary = {}
var _type_by_path: Dictionary = {}
## How many versions each tile's asset has, 1 for none.
var _versions_by_path: Dictionary = {}
## Paths on this page whose file is already at its project destination.
var _in_project: Dictionary = {}

## Typing in the search box changes the filter per keystroke, and each query
## walks the whole catalog, so a burst of changes becomes one query.
const FILTER_SETTLE_SECONDS: float = 0.12
var _settle_timer: Timer

@onready var _subbar: GridSubbar = $VBox/Subbar
@onready var _list: ItemList = $VBox/GridStack/AssetGrid
@onready var _empty_state: CenterContainer = $VBox/GridStack/EmptyState
@onready var _empty_title: Label = $VBox/GridStack/EmptyState/EmptyBox/TitleLabel
@onready var _empty_body: Label = $VBox/GridStack/EmptyState/EmptyBox/BodyLabel
@onready var _empty_buttons: HBoxContainer = $VBox/GridStack/EmptyState/EmptyBox/EmptyButtons
@onready var _open_folder_button: Button = $VBox/GridStack/EmptyState/EmptyBox/EmptyButtons/OpenFolderButton
@onready var _add_asset_button: MenuButton = $VBox/GridStack/EmptyState/EmptyBox/EmptyButtons/AddAssetButton
@onready var _context_menu: PopupMenu = $AssetContextMenu

var _add_type_ids: Array[String] = []

const MENU_OPEN_LOCATION: int = 0
const MENU_OPEN_EXTERNAL: int = 1
const MENU_SEND_TO_PROJECT: int = 2
const MENU_REMOVE_FROM_PROJECT: int = 3

## Tile tint for assets already in the project, at this alpha over the accent.
const IN_PROJECT_TINT_ALPHA: float = 0.25

func _ready() -> void:
	if EditorGuard.is_scene_tab(self):
		return

	_list.item_selected.connect(_on_item_selected)
	_list.item_activated.connect(_on_item_activated)
	_list.gui_input.connect(_on_list_gui_input)
	_list.item_clicked.connect(_on_item_clicked)
	_context_menu.id_pressed.connect(_on_context_menu_pressed)

	_subbar.sort_changed.connect(_on_sort_changed)
	_subbar.page_changed.connect(_on_page_changed)
	_subbar.per_page_changed.connect(_on_per_page_changed)
	_subbar.tile_size_changed.connect(_on_tile_size_changed)
	_subbar.view_mode_changed.connect(_on_view_mode_changed)
	_subbar.subtype_changed.connect(_on_subtype_changed)
	_subbar.group_versions_changed.connect(_on_group_versions_changed)
	_open_folder_button.pressed.connect(func() -> void:
		if catalog != null and not catalog.WorkspacePath.is_empty():
			OS.shell_open(catalog.WorkspacePath)
	)

	_add_asset_button.icon = IconHelper.get_icon("Add")
	_open_folder_button.icon = IconHelper.get_icon("Folder")

	var add_popup := _add_asset_button.get_popup()
	_add_type_ids = TypeMenu.populate(add_popup)
	add_popup.id_pressed.connect(func(id: int) -> void:
		if id >= 0 and id < _add_type_ids.size():
			add_requested.emit(_add_type_ids[id])
	)

	var blank := Image.create_empty(1, 1, false, Image.FORMAT_RGBA8)
	_blank_texture = ImageTexture.create_from_image(blank)

	_settle_timer = Timer.new()
	_settle_timer.one_shot = true
	_settle_timer.wait_time = FILTER_SETTLE_SECONDS
	_settle_timer.timeout.connect(refresh)
	add_child(_settle_timer)

	_apply_panel_style()

## The grid panel holds the sub-toolbar and the list together, so the controls
## read as belonging to the content below them rather than floating above it.
## The ItemList inside draws no background of its own, the panel is the surface.
func _apply_panel_style() -> void:
	if not Engine.is_editor_hint():
		return
	var theme := EditorInterface.get_editor_theme()
	if theme == null:
		return

	# TreeSecondary's shape, with the background a text field uses. Both come
	# from the theme rather than being picked: LineEdit's normal style is built
	# on surface_lower (theme_modern.cpp:891), one step darker than the
	# sidebar's surface_low, which is the separation the editor itself uses
	# between a list and the content beside it.
	# Duplicated because get_stylebox returns the shared theme resource, writing
	# to it would repaint every Tree in the editor, sidebar included.
	if theme.has_stylebox("panel", "TreeSecondary"):
		var style: StyleBox = theme.get_stylebox("panel", "TreeSecondary").duplicate()
		var field: StyleBox = theme.get_stylebox("normal", "LineEdit")
		if style is StyleBoxFlat and field is StyleBoxFlat:
			(style as StyleBoxFlat).bg_color = (field as StyleBoxFlat).bg_color
		add_theme_stylebox_override("panel", style)

	_list.add_theme_stylebox_override("panel", StyleBoxEmpty.new())

	var spacing: int = EditorInterface.get_editor_settings().get_setting("interface/theme/base_spacing")
	$VBox.add_theme_constant_override("separation", int(spacing * EditorInterface.get_editor_scale()))

func setup(p_catalog: AssetCatalog, p_settings: SettingsManager, p_thumbnails: ThumbnailLoader) -> void:
	catalog = p_catalog
	settings = p_settings
	thumbnails = p_thumbnails
	# Bound methods only: a lambda connected to a C# signal crashes Godot on
	# exit (see AssetCatalog).
	catalog.QueryReady.connect(_on_query_ready)
	if not thumbnails.thumbnail_ready.is_connected(_on_thumbnail_ready):
		thumbnails.thumbnail_ready.connect(_on_thumbnail_ready)

	_animator.setup(_list, self)
	_subbar.set_tile_size(settings.get_grid_icon_size())
	_subbar.set_per_page(settings.get_items_per_page())
	_subbar.set_view_mode(settings.get_is_grid_view())
	_subbar.set_group_versions(settings.get_group_versions())
	_apply_view_mode()

## Everything the grid filters by, handed in rather than read from the sidebar,
## the grid never learns what a sidebar is.
func set_filter(prefix: String, tags: Array, extensions: Array, search: String, type_id: String = "") -> void:
	_filter = {"prefix": prefix, "tags": tags, "extensions": extensions, "search": search}
	_page = 0
	# A subtype only means something within one type, so changing folder
	# drops it.
	_subtype = ""
	_subbar.set_subtypes(type_id)
	_settle_timer.start()

## Asks the catalog for the current page; _on_query_ready draws it.
func refresh() -> void:
	if catalog == null:
		return
	_settle_timer.stop()
	_request_id += 1
	catalog.Query(_request_id, JSON.stringify({
		"prefix": _filter.get("prefix", ""),
		"tags": _filter.get("tags", []),
		"extensions": _filter.get("extensions", []),
		"search": _filter.get("search", ""),
		"subtype": _subtype,
		"sort": _sort_mode,
		"page": _page,
		"per_page": settings.get_items_per_page(),
		"group_versions": settings.get_group_versions(),
	}))

func _on_query_ready(request_id: int, paths: PackedStringArray, types: PackedStringArray,
		sizes: PackedInt64Array, mtimes: PackedInt64Array, total: int, page: int, total_pages: int,
		version_counts: PackedInt32Array) -> void:
	if request_id != _request_id:
		return

	_list.clear()
	_animator.clear()
	_item_by_path.clear()
	_type_by_path.clear()
	_versions_by_path.clear()
	_in_project.clear()

	_page = page
	_total_pages = total_pages
	_subbar.set_page(_page, _total_pages)

	# Type icons first, so the page is usable at once; thumbnails replace them
	# as they arrive.
	for i in paths.size():
		var path := paths[i]
		var label := path.get_file()
		if version_counts[i] > 1:
			label += " (+%d)" % (version_counts[i] - 1)
		var idx := _list.add_item(label, _type_icon(types[i]))
		_list.set_item_metadata(idx, path)
		_item_by_path[path] = idx
		_type_by_path[path] = types[i]
		_versions_by_path[path] = version_counts[i]
		_mark_presence(idx, path)

	_update_empty_state(total)
	thumbnails.request(request_id, paths, types, sizes, mtimes)

func _on_thumbnail_ready(request_id: int, path: String, texture: Texture2D) -> void:
	if request_id != _request_id:
		return
	var idx: int = _item_by_path.get(path, -1)
	if idx < 0:
		return
	_list.set_item_icon(idx, _animator.register(idx, texture))
	_animator.start()

## Re-checks every tile on the page against the project, after a send or a
## remove, or when the project's files change underneath.
func refresh_presence() -> void:
	for path: String in _item_by_path:
		_mark_presence(_item_by_path[path], path)

func is_in_project(path: String) -> bool:
	return _in_project.has(path)

func _mark_presence(idx: int, path: String) -> void:
	var present := AssetExporter.is_in_project(path, _type_by_path[path], catalog.WorkspacePath)
	var tooltip := path
	var versions: int = _versions_by_path.get(path, 1)
	if versions > 1:
		tooltip += "\n%d versions (formats or exact copies), listed in the preview" % versions
	if present:
		_in_project[path] = true
		_list.set_item_custom_bg_color(idx, _in_project_tint())
		tooltip += "\nIn project: " + AssetExporter.project_path(path, _type_by_path[path], catalog.WorkspacePath)
	else:
		_in_project.erase(path)
		_list.set_item_custom_bg_color(idx, Color(0, 0, 0, 0))
	_list.set_item_tooltip(idx, tooltip)

func _in_project_tint() -> Color:
	var accent := Color(0.44, 0.73, 0.98)
	if Engine.is_editor_hint():
		accent = EditorInterface.get_editor_theme().get_color("accent_color", "Editor")
	accent.a = IN_PROJECT_TINT_ALPHA
	return accent

## Two different nothings: a workspace with no assets at all needs telling where
## assets come from, a filter that matched nothing just needs saying so.
func _update_empty_state(matched_count: int) -> void:
	if matched_count > 0:
		_empty_state.visible = false
		return

	if catalog.Count == 0:
		_empty_title.text = "This workspace is empty"
		_empty_body.text = "Add an asset pack, or copy files into\n%s\nthen press Scan." % catalog.WorkspacePath
		_empty_buttons.visible = true
	else:
		_empty_title.text = "Nothing to show"
		_empty_body.text = "No assets match the current filters."
		_empty_buttons.visible = false

	_empty_state.visible = true

## The type's editor icon, shown until the thumbnail arrives, and for good
## when there is none.
func _type_icon(type_id: String) -> Texture2D:
	if not Engine.is_editor_hint():
		return _blank_texture

	var type_entry := AssetTypes.get_by_id(type_id)
	var icon := IconHelper.get_icon(type_entry.get("default_icon", "File"))
	if icon != null:
		return icon
	return _blank_texture

func _apply_view_mode() -> void:
	var is_grid := settings.get_is_grid_view()
	var scale := EditorInterface.get_editor_scale() if Engine.is_editor_hint() else 1.0

	if is_grid:
		var size := settings.get_grid_icon_size()
		_list.max_columns = 0
		_list.same_column_width = true
		# The column is the icon's width, h_separation already puts space
		# between tiles, so padding here only widens the selection box around
		# the image.
		_list.fixed_column_width = size
		_list.icon_mode = ItemList.ICON_MODE_TOP
		_list.max_text_lines = 2
		_list.fixed_icon_size = Vector2i(size, size)
		_list.add_theme_constant_override("h_separation", int(12 * scale))
		_list.add_theme_constant_override("v_separation", int(12 * scale))
	else:
		_list.max_columns = 1
		_list.same_column_width = false
		_list.fixed_column_width = 0
		_list.icon_mode = ItemList.ICON_MODE_LEFT
		_list.max_text_lines = 1
		_list.fixed_icon_size = Vector2i(24, 24) * scale
		_list.add_theme_constant_override("h_separation", int(8 * scale))
		_list.add_theme_constant_override("v_separation", int(4 * scale))

	_list.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS

func selected_path() -> String:
	var selected := _list.get_selected_items()
	if selected.is_empty():
		return ""
	return _list.get_item_metadata(selected[0])

func _on_item_selected(index: int) -> void:
	selection_changed.emit(_list.get_item_metadata(index))

func _on_item_activated(index: int) -> void:
	item_activated.emit(_list.get_item_metadata(index))

## The preview panel's actions, on whichever asset was right-clicked.
## allow_rmb_select means it is already the selection by now.
func _on_item_clicked(index: int, at_position: Vector2, mouse_button_index: int) -> void:
	if mouse_button_index != MOUSE_BUTTON_RIGHT:
		return

	selection_changed.emit(_list.get_item_metadata(index))

	_context_menu.clear()
	_context_menu.add_icon_item(IconHelper.get_icon("Folder"), "Open Folder", MENU_OPEN_LOCATION)
	_context_menu.add_icon_item(IconHelper.get_icon("ExternalLink"), "Open in Default Application", MENU_OPEN_EXTERNAL)
	_context_menu.add_separator()
	_context_menu.add_icon_item(IconHelper.get_icon("Add"), "Send to Project", MENU_SEND_TO_PROJECT)
	# Send stays offered alongside: a re-send skips what is there and fills in
	# what is missing, such as textures for a model sent before they came along.
	if is_in_project(_list.get_item_metadata(index)):
		_context_menu.add_icon_item(IconHelper.get_icon("Remove"), "Remove from Project", MENU_REMOVE_FROM_PROJECT)

	_context_menu.position = Vector2i(_list.get_screen_position() + at_position)
	_context_menu.reset_size()
	_context_menu.popup()

func _on_context_menu_pressed(id: int) -> void:
	match id:
		MENU_OPEN_LOCATION:
			open_location_requested.emit()
		MENU_OPEN_EXTERNAL:
			open_external_requested.emit()
		MENU_SEND_TO_PROJECT:
			send_to_project_requested.emit()
		MENU_REMOVE_FROM_PROJECT:
			remove_from_project_requested.emit()

## Shift+scroll resizes tiles, the same gesture every canvas app trains, the
## slider in the sub-toolbar is the discoverable version of it.
func _on_list_gui_input(event: InputEvent) -> void:
	if not (event is InputEventMouseButton and event.pressed and event.shift_pressed):
		return
	if not settings.get_is_grid_view():
		return

	var step := 0
	if event.button_index == MOUSE_BUTTON_WHEEL_UP:
		step = 8
	elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
		step = -8
	else:
		return

	var size := clampi(settings.get_grid_icon_size() + step, GridSubbar.MIN_TILE_SIZE, GridSubbar.MAX_TILE_SIZE)
	settings.set_grid_icon_size(size)
	_subbar.set_tile_size(size)
	_apply_view_mode()
	_list.accept_event()

func _on_sort_changed(mode: int) -> void:
	_sort_mode = mode
	_page = 0
	refresh()

func _on_group_versions_changed(enabled: bool) -> void:
	settings.set_group_versions(enabled)
	_page = 0
	refresh()

func _on_subtype_changed(subtype: String) -> void:
	_subtype = subtype
	_page = 0
	refresh()

func _on_page_changed(delta: int) -> void:
	_page = clampi(_page + delta, 0, _total_pages - 1)
	refresh()

func _on_per_page_changed(count: int) -> void:
	settings.set_items_per_page(count)
	_page = 0
	refresh()

func _on_tile_size_changed(size: int) -> void:
	settings.set_grid_icon_size(size)
	_apply_view_mode()

func _on_view_mode_changed(is_grid: bool) -> void:
	settings.set_is_grid_view(is_grid)
	_subbar.set_view_mode(is_grid)
	_apply_view_mode()
