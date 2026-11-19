@tool
extends EditorPlugin

## EditorPlugin entry point. Instantiates the main screen panel and forwards
## visibility changes so it can sync when the user switches back to it.

const MAIN_SCREEN_SCENE := "res://addons/asset_manager/ui/main_screen/main_screen.tscn"
const CONTEXT_MENU_SCRIPT := "res://addons/asset_manager/ui/context_menu/context_menu.gd"

var main_panel_instance: Control
var _context_menu: EditorContextMenuPlugin

func _enter_tree() -> void:
	# ASSUMPTION: a headless editor (an import run, CI) has no one to use the
	# tab or the menu, so the plugin builds neither there. The scenes are loaded
	# at run time, never preloaded, so a headless start compiles none of them:
	# they cost about 1.3 s of every headless import.
	if DisplayServer.get_name() == "headless":
		return
	main_panel_instance = (load(MAIN_SCREEN_SCENE) as PackedScene).instantiate()
	EditorInterface.get_editor_main_screen().add_child(main_panel_instance)
	_make_visible(false)

	# add_context_menu_plugin arrived in 4.4
	if ClassDB.class_exists("EditorContextMenuPlugin"):
		var menu = load(CONTEXT_MENU_SCRIPT).new()
		menu.main_panel = main_panel_instance
		_context_menu = menu
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
		# ASSUMPTION: showing the tab is not a request for the library (1.77M
		# records, about 1.4 GB once shown), whether the editor restored it or
		# someone opened it. Until the library is used (see wait_for_use), every
		# showing offers Load library; once it is loaded, this does nothing.
		if visible and main_panel_instance.has_method("wait_for_use"):
			main_panel_instance.wait_for_use()
		if visible and not was_visible and main_panel_instance.has_method("sync_if_stale"):
			main_panel_instance.sync_if_stale()
			main_panel_instance.focus_search()

func _get_plugin_name() -> String:
	return "AssetManager"

func _get_plugin_icon() -> Texture2D:
	return IconHelper.get_icon("Load")
