extends SceneTree

## Opens the workspace as the editor does, then queries only sounds, then
## images, pausing at each stage so an outside sampler can read the
## process's private bytes and read counters. Run it through
## sample_memory.ps1, which starts it and does the sampling; on its own it
## only prints the STAGE lines (milliseconds since the engine started).

func _initialize() -> void:
	_run.call_deferred()

func _mark(stage: String) -> void:
	print("STAGE %s %s" % [Time.get_ticks_msec(), stage])

func _pause() -> void:
	await create_timer(6.0).timeout

func _query(catalog: AssetCatalog, id: int, prefix: String) -> void:
	catalog.Query(id, JSON.stringify({"prefix": prefix, "tags": [], "extensions": [], "search": "", "subtype": "", "sort": 0, "page": 0, "per_page": 100, "group_versions": true}))
	await catalog.QueryReady

func _run() -> void:
	var workspace := String(OS.get_cmdline_user_args()[0])
	await _pause()
	_mark("idle")
	var catalog := AssetCatalog.new()
	catalog.Setup(workspace, JSON.stringify(AssetTypes.ALL))
	catalog.Open()
	await catalog.Opened
	_mark("opened")
	await _pause()
	catalog.Stats(1, "")
	await catalog.StatsReady
	_mark("sidebar stats")
	await _pause()
	await _query(catalog, 1, workspace.path_join("sounds"))
	_mark("sounds page")
	await _pause()
	await _query(catalog, 2, workspace.path_join("images"))
	_mark("images page")
	await _pause()
	catalog.Shutdown()
	quit()
