@tool
extends Node
class_name CommandDispatcher

var _ei: EditorInterface
var _lc: LogCollector
var _handlers: Dictionary = {}

func _init(ei: EditorInterface, lc: LogCollector) -> void:
	_ei = ei
	_lc = lc

func register() -> void:
	# preload yerine load() kullan: editor reload sirasinda preload cache'i
	# bazen res:// script'lerini bulamayip null dondurebiliyor.
	var base := "res://addons/godot_mcp/commands/"
	var scene_cmd = load(base + "cmd_scene.gd").new(_ei)
	var node_cmd  = load(base + "cmd_node.gd").new(_ei)
	var script_cmd = load(base + "cmd_script.gd").new(_ei)
	var editor_cmd = load(base + "cmd_editor.gd").new(_ei)
	var fs_cmd = load(base + "cmd_filesystem.gd").new(_ei)
	var res_cmd = load(base + "cmd_resource.gd").new(_ei)
	var screenshot_cmd = load(base + "cmd_screenshot.gd").new(_ei)
	var update_cmd = load(base + "cmd_update.gd").new(_ei)
	
	_handlers["scene_open"]         = scene_cmd.open_scene
	_handlers["scene_save"]         = scene_cmd.save_scene
	_handlers["scene_create"]       = scene_cmd.create_scene
	_handlers["scene_list_opened"]  = scene_cmd.list_opened
	_handlers["scene_get_data"]     = scene_cmd.get_data
	_handlers["scene_close"]        = scene_cmd.close_scene
	
	_handlers["node_find"]              = node_cmd.find_node
	_handlers["node_create"]            = node_cmd.create_node
	_handlers["node_get_properties"]    = node_cmd.get_properties
	_handlers["node_set_property"]      = node_cmd.set_property
	_handlers["node_set_properties"]    = node_cmd.set_properties
	_handlers["node_delete"]            = node_cmd.delete_node
	_handlers["node_reparent"]          = node_cmd.reparent_node
	_handlers["node_duplicate"]         = node_cmd.duplicate_node
	_handlers["node_rename"]            = node_cmd.rename_node
	_handlers["node_instance_scene"]    = node_cmd.instance_scene
	
	_handlers["script_read"]            = script_cmd.read_script
	_handlers["script_create"]          = script_cmd.create_script
	_handlers["script_update"]          = script_cmd.update_script
	_handlers["script_delete"]          = script_cmd.delete_script
	_handlers["script_attach_to_node"]  = script_cmd.attach_to_node
	_handlers["script_get_errors"]      = script_cmd.get_errors
	
	_handlers["editor_play"]                    = editor_cmd.play
	_handlers["editor_stop"]                    = editor_cmd.stop
	_handlers["editor_pause"]                   = editor_cmd.pause
	_handlers["editor_get_state"]               = editor_cmd.get_state
	_handlers["editor_selection_get"]           = editor_cmd.selection_get
	_handlers["editor_selection_set"]           = editor_cmd.selection_set
	_handlers["editor_get_project_settings"]    = editor_cmd.get_project_settings
	_handlers["editor_set_project_setting"]     = editor_cmd.set_project_setting
	_handlers["editor_get_project_path"]        = editor_cmd.get_project_path
	
	_handlers["filesystem_list"]        = fs_cmd.list_files
	_handlers["filesystem_read_file"]   = fs_cmd.read_file
	_handlers["filesystem_write_file"]  = fs_cmd.write_file
	_handlers["filesystem_delete"]      = fs_cmd.delete_file
	_handlers["filesystem_move"]        = fs_cmd.move_file
	_handlers["filesystem_reimport"]    = fs_cmd.reimport
	_handlers["filesystem_search"]      = fs_cmd.search_files
	
	_handlers["resource_get_data"]  = res_cmd.get_data
	_handlers["resource_modify"]    = res_cmd.modify
	_handlers["resource_create"]    = res_cmd.create_resource
	_handlers["resource_delete"]    = res_cmd.delete_resource
	
	_handlers["console_get_logs"]   = func(p): return _lc.get_logs(p)
	_handlers["console_clear_logs"] = func(p): _lc.clear(); return {"success": true}
	_handlers["console_get_errors"] = func(p): return _lc.get_errors(p)
	
	_handlers["screenshot_viewport"] = screenshot_cmd.capture_viewport
	_handlers["screenshot_game"]     = screenshot_cmd.capture_game

	# Sağlık kontrolü ve meta
	_handlers["ping"]          = func(p): return {"success": true, "result": {"pong": true, "time": Time.get_unix_time_from_system()}}
	_handlers["get_version"]   = func(p): return {"success": true, "result": {"godot_version": Engine.get_version_info(), "plugin_version": _get_plugin_version()}}

	# Addon güncelleme
	_handlers["update_addon"]        = update_cmd.update_addon
	_handlers["update_addon_push"]   = update_cmd.update_addon_push

func dispatch(command: String, params: Dictionary) -> Dictionary:
	if not _handlers.has(command):
		return {"success": false, "error": "Bilinmeyen komut: " + command}

	var handler = _handlers[command]
	var result = await handler.call(params)
	return result

# Plugin versiyonunu tek kaynaktan (plugin.cfg) okur.
# Server tarafı auto-update karşılaştırması için kullanır.
func _get_plugin_version() -> String:
	var cfg := ConfigFile.new()
	if cfg.load("res://addons/godot_mcp/plugin.cfg") == OK:
		return str(cfg.get_value("plugin", "version", "unknown"))
	return "unknown"
