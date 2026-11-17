@tool
extends EditorPlugin

## EditorPlugin entry point. Instantiates the main screen panel and forwards
## visibility changes so it can sync when the user switches back to it.

var main_panel_instance: Control
var _context_menu: EditorContextMenuPlugin
## Set by the first click or key press anywhere in the editor. At start the
## editor shows the main screen it was closed on before anyone touched it, and
## a tab that was only restored must not load the library (1.77M records,
## about 1.4 GB once shown): it waits for use instead.
var _editor_touched: bool = false

func _input(event: InputEvent) -> void:
	if (event is InputEventMouseButton or event is InputEventKey) and event.is_pressed():
		_editor_touched = true
		set_process_input(false)

func _enter_tree() -> void:
	main_panel_instance = preload("res://addons/asset_manager/ui/main_screen/main_screen.tscn").instantiate()
	EditorInterface.get_editor_main_screen().add_child(main_panel_instance)
	_make_visible(false)

	# add_context_menu_plugin arrived in 4.4
	if ClassDB.class_exists("EditorContextMenuPlugin"):
		_context_menu = AssetManagerContextMenu.new()
		_context_menu.main_panel = main_panel_instance
		add_context_menu_plugin(EditorContextMenuPlugin.CONTEXT_SLOT_FILESYSTEM, _context_menu)

func _exit_tree() -> void:
	if main_panel_instance:
		main_panel_instance.queue_free()
	if _context_menu:
		remove_context_menu_plugin(_context_menu)

func _has_main_screen() -> bool:
	return true

func _make_visible(visible: bool) -> void:
	if main_panel_instance:
		var was_visible := main_panel_instance.visible
		main_panel_instance.visible = visible
		# The workspace loads when the tab is opened, not at editor start (see
		# activate). A tab restored before any input waits for a click instead.
		if visible and _editor_touched and main_panel_instance.has_method("activate"):
			main_panel_instance.activate()
		elif visible and main_panel_instance.has_method("wait_for_use"):
			main_panel_instance.wait_for_use()
		if visible and not was_visible and main_panel_instance.has_method("sync_if_stale"):
			main_panel_instance.sync_if_stale()
			main_panel_instance.focus_search()

func _get_plugin_name() -> String:
	return "AssetManager"

func _get_plugin_icon() -> Texture2D:
	return IconHelper.get_icon("Load")
