@tool
@static_unload
class_name AssetManager
extends Control

## The shell. Owns the workspace, the catalog and the current selection, and
## wires the four components together, it draws nothing itself.
## Nothing here touches the library's files on the main thread: the catalog
## and the thumbnail loader do their disk work in the background and answer
## with signals, so a NAS-sized library never stalls the editor.
## Selection lives here rather than in the grid: the preview, the tag editor
## and the action buttons all need it, and having each reach into the grid to
## ask would couple them to whatever widget the grid happens to use.

signal asset_selected(path: String)

var current_selected_path: String = ""
var current_workspace_path: String = ""

var _workspace_picker: WorkspacePicker
var _catalog: AssetCatalog
var _scan_starting: bool = false
var _settings: SettingsManager = SettingsManager.new()
var _thumbnails: ThumbnailLoader
var _progress_dialog: ImportProgressDialog
var _remove_dialog: ConfirmationDialog
var _pending_removal: PackedStringArray = []
## Tags the newest version lookup, so a late answer for an earlier selection
## is dropped.
var _versions_request: int = 0
## The sidebar folder as last saved, relative to the workspace, so a search
## keystroke does not rewrite the settings file.
var _remembered_folder: String = ""

const PROGRESS_DIALOG_SCENE := preload("res://addons/asset_manager/ui/import_progress/import_progress.tscn")

@onready var toolbar: AssetToolbar = $MarginContainer/RootVBox/Toolbar
@onready var main_split: HSplitContainer = $MarginContainer/RootVBox/MainSplit
@onready var content_split: HSplitContainer = $MarginContainer/RootVBox/MainSplit/ContentSplit
@onready var _sidebar: SidebarNavigator = $MarginContainer/RootVBox/MainSplit/Sidebar
@onready var _grid: AssetGrid = $MarginContainer/RootVBox/MainSplit/ContentSplit/CenterPanel
@onready var _preview: PreviewPanel = $MarginContainer/RootVBox/MainSplit/ContentSplit/PreviewPanel
@onready var folder_dialog: FileDialog = $FolderDialog
@onready var add_file_dialog: FileDialog = $AddFileDialog
@onready var add_dialog: AddDialog = $AddDialog
@onready var project_settings_dialog: ProjectSettingsDialog = $ProjectSettingsDialog
@onready var drop_type_menu: PopupMenu = $DropTypeMenu
@onready var tag_context_menu: PopupMenu = $TagContextMenu

func _notification(what: int) -> void:
	# Fires only when focus returns from outside the godot process.
	if what == NOTIFICATION_APPLICATION_FOCUS_IN:
		sync_if_stale()

func _ready() -> void:
	if EditorGuard.is_scene_tab(self):
		return

	_apply_outer_margin()
	_restore_panel_widths()

	_progress_dialog = PROGRESS_DIALOG_SCENE.instantiate()
	add_child(_progress_dialog)
	_progress_dialog.stop_requested.connect(_on_stop_scan)

	_thumbnails = ThumbnailLoader.new()
	add_child(_thumbnails)

	_sidebar.filter_changed.connect(_on_filter_changed)
	_sidebar.open_folder_requested.connect(func(path: String) -> void: OS.shell_open(path))
	_sidebar.add_requested.connect(_on_add_pressed)
	_sidebar.rescan_requested.connect(rescan_folder)
	_grid.selection_changed.connect(_on_selection_changed)
	_grid.open_location_requested.connect(_on_open_location_pressed)
	_grid.open_external_requested.connect(_on_open_external_pressed)
	_grid.send_to_project_requested.connect(_on_send_to_project_pressed)
	_grid.remove_from_project_requested.connect(_on_remove_from_project_pressed)
	_grid.add_requested.connect(_on_add_pressed)

	_preview.send_to_project_pressed.connect(_on_send_to_project_pressed)
	_preview.remove_from_project_pressed.connect(_on_remove_from_project_pressed)
	_preview.open_external_pressed.connect(_on_open_external_pressed)
	_preview.open_location_pressed.connect(_on_open_location_pressed)
	_preview.version_selected.connect(_on_version_selected)
	_preview.add_tag_requested.connect(_on_add_tag_requested)
	_preview.remove_tag_requested.connect(_on_remove_tag_requested)
	_preview.tag_search_changed.connect(_on_tag_input_text_changed)

	toolbar.search_changed.connect(func(_text: String) -> void: _on_filter_changed())
	toolbar.scan_pressed.connect(_on_scan_pressed)
	toolbar.add_pressed.connect(_on_add_pressed)
	toolbar.settings_pressed.connect(func() -> void: project_settings_dialog.open())
	project_settings_dialog.switch_workspace_requested.connect(_on_switch_workspace)
	add_file_dialog.file_selected.connect(_on_add_file_selected)
	add_dialog.confirmed_with_selection.connect(_on_add_confirmed)
	toolbar.sidebar_toggled.connect(func(collapsed: bool) -> void: _sidebar.visible = not collapsed)

	get_window().files_dropped.connect(_on_files_dropped)

	_remove_dialog = ConfirmationDialog.new()
	_remove_dialog.title = "Remove from Project"
	_remove_dialog.ok_button_text = "Delete"
	_remove_dialog.confirmed.connect(_on_remove_confirmed)
	add_child(_remove_dialog)

	# Files deleted in the FileSystem dock, or sent from another tab, change
	# what is in the project without passing through here.
	if Engine.is_editor_hint():
		EditorInterface.get_resource_filesystem().filesystem_changed.connect(_refresh_presence)

	_preview.setup(_settings)
	_init_workspace_picker()

## The plugin fills the main screen the way 2D/3D/Script do, so it should sit
## as flush to the edges as they do. interface/theme/base_spacing is the
## user's own setting, the same value the editor derives its own margins from.
func _apply_outer_margin() -> void:
	var margin := 0
	if Engine.is_editor_hint():
		var spacing: int = EditorInterface.get_editor_settings().get_setting("interface/theme/base_spacing")
		margin = int(spacing * EditorInterface.get_editor_scale())

	var container: MarginContainer = $MarginContainer
	for side in ["left", "top", "right", "bottom"]:
		container.add_theme_constant_override("margin_" + side, margin)

## Widths are stored unscaled and multiplied on the way out, so a workspace
## moved between a 4K and a 1080p machine gets a sane size on each.
func _restore_panel_widths() -> void:
	var scale := EditorInterface.get_editor_scale() if Engine.is_editor_hint() else 1.0

	main_split.split_offset = int(_settings.get_sidebar_width(SidebarNavigator.BASE_DEFAULT_WIDTH) * scale)
	content_split.split_offset = int(_settings.get_preview_width(PreviewPanel.BASE_DEFAULT_WIDTH) * scale)

	main_split.dragged.connect(func(offset: int) -> void:
		_settings.set_sidebar_width(int(offset / scale))
	)
	content_split.dragged.connect(func(offset: int) -> void:
		_settings.set_preview_width(int(offset / scale))
	)

## Called by plugin.gd when the tab is switched to.
func focus_search() -> void:
	if toolbar:
		toolbar.focus_search()

func _shortcut_input(event: InputEvent) -> void:
	if not visible or not is_visible_in_tree():
		return
	var key := event as InputEventKey
	if key == null or not key.pressed:
		return
	if key.keycode == KEY_F and (key.ctrl_pressed or key.meta_pressed):
		toolbar.focus_search_and_select()
		get_viewport().set_input_as_handled()

## Reads the store's two 16-byte headers in the background; the catalog emits
## Changed only when another machine wrote since we loaded.
func sync_if_stale() -> void:
	if _catalog != null:
		_catalog.CheckStale()

func _refresh_all_after_index_change() -> void:
	_sidebar.refresh()
	_on_filter_changed()

## The one place the sidebar's filter state and the toolbar's search meet, the
## grid is handed a plain description of what to show and never reads either.
func _on_filter_changed() -> void:
	_grid.set_filter(
		_sidebar.active_folder_prefix,
		_sidebar.active_tags,
		_sidebar.active_extensions,
		toolbar.search_text(),
		_sidebar.active_type_id
	)
	_remember_folder()

func _remember_folder() -> void:
	var prefix := _sidebar.active_folder_prefix
	var relative := "" if prefix.is_empty() else prefix.trim_prefix(current_workspace_path).trim_prefix("/")
	if relative != _remembered_folder:
		_remembered_folder = relative
		_settings.set_last_folder(relative)

## Puts the sidebar back on the folder last looked at, before the first query
## runs: the All view sorts and groups every type, the 1.58M images included,
## where a sounds folder costs a fraction of that.
func _restore_folder() -> void:
	_remembered_folder = _settings.get_last_folder()
	var type_id := _remembered_folder.get_slice("/", 0)
	if _remembered_folder.is_empty() or AssetTypes.get_by_id(type_id).is_empty():
		return
	_sidebar.active_folder_prefix = current_workspace_path.path_join(_remembered_folder)
	_sidebar.active_type_id = type_id

## Scans one folder (or the whole workspace) for new, changed and removed
## files. Used by the sidebar, by Add, and by the FileSystem dock's Send to
## Asset Manager, which only need the folder that just changed looked at.
func rescan_folder(path: String) -> void:
	if _catalog == null or _catalog.IsScanning or _scan_starting:
		return

	# Set across the material step below, which yields frames before the
	# catalog itself reports a scan.
	_scan_starting = true
	toolbar.set_scanning(true)
	_progress_dialog.start()

	# Material folders become .tres files before they are indexed, so the
	# generator runs first, off the main thread, for scans that cover them.
	var materials_root := current_workspace_path.path_join("materials")
	if path == current_workspace_path or path.begins_with(materials_root):
		_progress_dialog.on_progress({"stage": "scan", "label": "Building materials", "current": 0, "total": 0})
		await _run_off_main_thread(func() -> void:
			MaterialsImportRunner.run(materials_root, AssetTypes.get_by_id("materials")))

	_scan_starting = false
	_catalog.Scan(path)

## The toolbar button scans everything, or stops the scan that is running.
func _on_scan_pressed() -> void:
	if _catalog == null:
		return
	if _catalog.IsScanning:
		_on_stop_scan()
	else:
		rescan_folder(current_workspace_path)

func _on_stop_scan() -> void:
	if _catalog != null:
		_catalog.CancelScan()

func _on_scan_progressed(stage: String, label: String, current: int, total: int) -> void:
	_progress_dialog.on_progress({"stage": stage, "label": label, "current": current, "total": total})

## How many unreadable folders are named in the warning; the count covers the rest.
const MAX_FAILED_FOLDERS_SHOWN: int = 20

func _on_scan_finished(added: int, changed: int, removed: int, total: int, cancelled: bool, root: String,
		seconds: float, retagged: int, failed_folders: PackedStringArray, retries: int) -> void:
	_progress_dialog.finish()
	toolbar.set_scanning(false)
	print("AssetManager: scanned %s in %.1fs%s: %d added, %d changed, %d removed, %d retagged, %d assets, %d retried requests"
		% [root, seconds, " (stopped)" if cancelled else "", added, changed, removed, retagged, total, retries])
	if not failed_folders.is_empty():
		var shown := failed_folders.slice(0, MAX_FAILED_FOLDERS_SHOWN)
		push_warning("AssetManager: %d folder(s) could not be read even after retrying; what was indexed under them is kept, and the next scan tries again:\n  %s%s"
			% [failed_folders.size(), "\n  ".join(shown), "\n  ..." if failed_folders.size() > shown.size() else ""])
	if added + changed + removed + retagged > 0:
		_refresh_all_after_index_change()

## Runs `work` on the WorkerThreadPool and yields frames until it is done.
func _run_off_main_thread(work: Callable) -> void:
	var task_id := WorkerThreadPool.add_task(work)
	while not WorkerThreadPool.is_task_completed(task_id):
		await get_tree().process_frame
	WorkerThreadPool.wait_for_task_completion(task_id)

## A zip or a single file of the chosen type. The type is picked before the file
## so an extension shared by two buckets (.ogg, .tscn) never has to be guessed.
var _add_type_id: String = ""

func _on_add_pressed(type_id: String) -> void:
	if current_workspace_path.is_empty():
		return

	_add_type_id = type_id
	var entry := AssetTypes.get_by_id(type_id)
	var extensions: Array = entry.get("extensions", [])

	var filters := PackedStringArray(["*.zip ; Asset pack"])
	if not extensions.is_empty():
		var globs: Array[String] = []
		for ext: String in extensions:
			globs.append("*." + ext)
		filters.append("%s ; %s" % [", ".join(globs), entry.get("label", type_id)])

	add_file_dialog.filters = filters
	add_file_dialog.title = "Add %s" % entry.get("label", type_id)
	add_file_dialog.popup_centered_ratio(0.7)

func _on_add_file_selected(path: String) -> void:
	add_dialog.ask(_add_type_id, path, current_workspace_path)

## The signal is window-wide with no drop position, so the plugin only claims a
## drop while its own tab is the one being looked at.
func _on_files_dropped(files: PackedStringArray) -> void:
	if not is_visible_in_tree() or current_workspace_path.is_empty():
		return
	if files.size() != 1:
		return

	var path := files[0]
	var candidates := AssetAdd.candidate_types(path)

	if candidates.is_empty():
		push_warning("AssetManager: nothing the library handles in " + path.get_file())
		return

	if candidates.size() == 1:
		_add_type_id = candidates[0]
		add_dialog.ask(candidates[0], path, current_workspace_path)
		return

	_ask_dropped_type(path, candidates)

## Only the buckets that could claim this file, so the choice is as short as the
## file allows: two for a shared extension, more for a mixed archive.
func _ask_dropped_type(path: String, candidates: PackedStringArray) -> void:
	drop_type_menu.clear()
	drop_type_menu.add_item("Add as…", -1)
	drop_type_menu.set_item_disabled(0, true)
	drop_type_menu.add_separator()

	for i in candidates.size():
		var entry := AssetTypes.get_by_id(candidates[i])
		var icon := IconHelper.get_icon(entry.get("default_icon", "File"))
		if icon != null:
			drop_type_menu.add_icon_item(icon, candidates[i], i)
		else:
			drop_type_menu.add_item(candidates[i], i)

	if drop_type_menu.id_pressed.is_connected(_on_dropped_type_chosen):
		drop_type_menu.id_pressed.disconnect(_on_dropped_type_chosen)
	drop_type_menu.id_pressed.connect(_on_dropped_type_chosen.bind(path, candidates))

	# files_dropped carries only the paths (window.cpp:2038), so the cursor is
	# the only record of where the drop landed.
	drop_type_menu.reset_size()
	drop_type_menu.position = DisplayServer.mouse_get_position() - drop_type_menu.size / 2
	drop_type_menu.popup()

func _on_dropped_type_chosen(id: int, path: String, candidates: PackedStringArray) -> void:
	if id < 0 or id >= candidates.size():
		return
	_add_type_id = candidates[id]
	add_dialog.ask(candidates[id], path, current_workspace_path)

## Files land first, then only the folder they landed in is scanned.
func _on_add_confirmed(type_id: String, source_path: String, format: String, variant: String, dest_root: String) -> void:
	_progress_dialog.start()
	_progress_dialog.on_progress({"stage": "scan", "label": "Extracting", "current": 0, "total": 0})

	# Extraction writes every file of the pack to the workspace, which on a NAS
	# takes long enough to freeze the editor if it ran here.
	var result: Dictionary = {}
	# <type>/<vendor>/<pack>: the vendor folder gets its marker, so the new
	# pack is tagged with its type like the rest of that vendor.
	var vendor_dir := dest_root.get_base_dir()
	var has_vendor := vendor_dir.get_base_dir() == current_workspace_path.path_join(type_id)
	await _run_off_main_thread(func() -> void:
		if has_vendor:
			VendorMarker.ensure(vendor_dir)
		if source_path.get_extension().to_lower() == "zip":
			result.merge(AssetAdd.extract(source_path, type_id, format, variant, dest_root))
		else:
			result.merge(AssetExporter.new_result())
			AssetExporter.copy_one_file(source_path, dest_root.path_join(source_path.get_file()), result))

	for error_message in result["errors"]:
		push_error("AssetManager: ", error_message)

	print("AssetManager: added %d file(s) to %s" % [result["copied_count"], dest_root])

	_progress_dialog.finish()
	if result["copied_count"] > 0:
		rescan_folder(dest_root)

func _on_add_tag_requested(tag_text: String) -> void:
	sync_if_stale()
	_preview.apply_tag_change(_selected_paths(), tag_text, true)
	_sidebar.refresh()

func _on_remove_tag_requested(tag_text: String) -> void:
	sync_if_stale()
	_preview.apply_tag_change(_selected_paths(), tag_text, false)

	# The tag just removed may have been the last instance anywhere.
	if not _catalog.KnownTags().has(tag_text) and _sidebar.active_tags.has(tag_text):
		_sidebar.active_tags.erase(tag_text)
		_on_filter_changed()

	_sidebar.refresh()

func _on_tag_input_text_changed() -> void:
	_preview.refresh_tags(_selected_paths())

func _init_workspace_picker() -> void:
	_workspace_picker = preload("res://addons/asset_manager/ui/workspace_picker/workspace_picker.tscn").instantiate()
	_workspace_picker.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_workspace_picker)
	_workspace_picker.workspace_opened.connect(_on_workspace_opened)

	var remembered_path := _workspace_picker.try_auto_load()
	if remembered_path.is_empty():
		_show_workspace_picker(true)
	else:
		_on_workspace_opened(remembered_path)

## Forgets which workspace was open and returns to the picker. Nothing on disk
## is touched: the workspace is still there, it just isn't the one in front.
## Filters are cleared with it, they hold absolute paths into the workspace
## being left and would match nothing in the next one.
func _on_switch_workspace() -> void:
	AssetManagerConfig.set_value("workspace", "path", "")
	current_workspace_path = ""
	current_selected_path = ""
	_close_catalog()

	_sidebar.active_folder_prefix = ""
	_sidebar.active_type_id = ""
	# A folder of this workspace means nothing in the next one.
	_remembered_folder = ""
	_settings.set_last_folder("")
	_sidebar.active_tags.clear()
	_sidebar.active_extensions.clear()
	toolbar.set_search_text("")

	_preview.clear()
	_workspace_picker.status_label.text = ""
	_show_workspace_picker(true)

func _show_workspace_picker(is_visible: bool) -> void:
	_workspace_picker.visible = is_visible
	$MarginContainer.visible = not is_visible

## The catalog loads in the background; the panels fill in on Opened.
func _on_workspace_opened(path: String) -> void:
	current_workspace_path = path
	_show_workspace_picker(false)

	_close_catalog()
	_restore_folder()
	_catalog = AssetCatalog.new()
	_catalog.Setup(path, JSON.stringify(AssetTypes.ALL))
	# Bound methods only: a lambda connected to a C# signal crashes Godot on
	# exit (see AssetCatalog).
	_catalog.Opened.connect(_on_catalog_opened)
	_catalog.ScanProgressed.connect(_on_scan_progressed)
	_catalog.ScanFinished.connect(_on_scan_finished)
	_catalog.Changed.connect(_refresh_all_after_index_change)
	_catalog.VersionsReady.connect(_on_versions_ready)

	_preview.set_catalog(_catalog)
	_sidebar.set_catalog(_catalog, path)
	_thumbnails.setup(path, _catalog)
	_grid.setup(_catalog, _settings, _thumbnails)
	_catalog.Open()

## `needs_scan` is true for a workspace that was never indexed, or whose index
## came from the pre-fork index.db and has no file sizes yet. A scan runs in
## the background, so it starts straight away rather than waiting to be asked.
func _on_catalog_opened(count: int, needs_scan: bool) -> void:
	print("AssetManager: workspace opened, %d assets%s" % [count, ", scanning" if needs_scan else ""])
	_refresh_all_after_index_change()
	if needs_scan:
		rescan_folder(current_workspace_path)

func _close_catalog() -> void:
	if _catalog != null:
		_catalog.Shutdown()
	_catalog = null

func _exit_tree() -> void:
	_close_catalog()

## Multi-select is gone, the preview never supported it. Kept as an array
## because the tag editor still takes one.
func _selected_paths() -> Array:
	return [current_selected_path] if not current_selected_path.is_empty() else []

## A tile stands for every version of its asset; which ones there are comes
## back from the catalog in the background, and the preview offers them.
func _on_selection_changed(path: String) -> void:
	_versions_request += 1
	_preview.set_versions("", [], [])
	_show_selected(path)
	if not path.is_empty():
		_catalog.Versions(_versions_request, path)

func _on_versions_ready(request_id: int, _path: String, paths: PackedStringArray, sizes: PackedInt64Array) -> void:
	if request_id == _versions_request:
		_preview.set_versions(current_selected_path, paths, sizes)

## Another version of the same asset: the grid selection stays, everything
## that acts on "the selected asset" (send, remove, tags) moves to it.
func _on_version_selected(path: String) -> void:
	_show_selected(path)

func _show_selected(path: String) -> void:
	current_selected_path = path

	var has_selection := not path.is_empty()
	_preview.set_actions_enabled(has_selection)

	if has_selection:
		_preview.show_asset(path, _catalog.TypeOf(path))
		_preview.set_in_project(_selected_in_project())
		asset_selected.emit(path)
	else:
		_preview.clear()
		_preview.set_in_project(false)

	_preview.refresh_tags(_selected_paths())

func _on_open_external_pressed() -> void:
	if not current_selected_path.is_empty():
		OS.shell_open(current_selected_path)

func _on_open_location_pressed() -> void:
	if not current_selected_path.is_empty():
		OS.shell_show_in_file_manager(current_selected_path)

func _on_send_to_project_pressed() -> void:
	if current_selected_path.is_empty():
		return

	var type_id := _catalog.TypeOf(current_selected_path)
	if type_id.is_empty():
		return

	var dest_dir := ExportRoots.get_root(type_id)
	var result := AssetExporter.export_asset(current_selected_path, dest_dir, type_id, current_workspace_path)

	for error_msg in result["errors"]:
		push_error("AssetManager: ", error_msg)

	# Copied files don't show up in the FileSystem dock otherwise.
	_notify_filesystem_of_new_files(result["copied_paths"])

	var summary := str(result["copied_count"]) + " file(s) copied"
	if result["skipped_existing_count"] > 0:
		summary += ", " + str(result["skipped_existing_count"]) + " already present"
	if not result["errors"].is_empty():
		summary += ", " + str(result["errors"].size()) + " error(s)"
	_refresh_presence()
	_preview.flash_send_result(summary)

## Lists what would go and asks first: the delete is permanent.
func _on_remove_from_project_pressed() -> void:
	var dest_path := _selected_project_path()
	if dest_path.is_empty():
		return

	_pending_removal = AssetRemover.files_for(dest_path)
	if _pending_removal.is_empty():
		_refresh_presence()
		return

	_remove_dialog.dialog_text = "Permanently delete %d file(s)?\n\n%s" % [_pending_removal.size(), "\n".join(_pending_removal)]
	_remove_dialog.popup_centered()

func _on_remove_confirmed() -> void:
	var result := AssetRemover.remove(_pending_removal)
	_pending_removal = []

	for error_msg in result["errors"]:
		push_error("AssetManager: ", error_msg)

	var summary := str(result["removed_count"]) + " file(s) removed"
	if not result["errors"].is_empty():
		summary += ", " + str(result["errors"].size()) + " error(s)"
	_refresh_presence()
	_preview.flash_send_result(summary)

## "" when nothing is selected or its type is unknown.
func _selected_project_path() -> String:
	if current_selected_path.is_empty() or _catalog == null:
		return ""
	var type_id := _catalog.TypeOf(current_selected_path)
	if type_id.is_empty():
		return ""
	return AssetExporter.project_path(current_selected_path, type_id, current_workspace_path)

func _selected_in_project() -> bool:
	var dest_path := _selected_project_path()
	return not dest_path.is_empty() and FileAccess.file_exists(dest_path)

func _refresh_presence() -> void:
	_grid.refresh_presence()
	_preview.set_in_project(_selected_in_project())

## scan_sources() + awaiting resources_reimported works around a Godot core
## reentrancy bug (godotengine/godot#54864). Re-test before changing it.
func _notify_filesystem_of_new_files(paths: Array) -> void:
	if paths.is_empty() or not Engine.is_editor_hint():
		return

	var efs := EditorInterface.get_resource_filesystem()
	efs.scan_sources()
	await efs.resources_reimported
