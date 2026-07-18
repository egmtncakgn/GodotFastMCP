@tool
extends Node
class_name CommandDispatcher

var _ei: EditorInterface
var _lc: LogCollector
var _handlers: Dictionary = {}
# Sorun 30 fix: RefCounted komut nesneleri yalnızca Callable üzerinden referanslanınca
# register() dönüşünde GC'ye gidiyor (Callable nesneyi CANLI TUTMAZ — zayıf ObjectID).
# Sonuç: tüm handler'lar "null::metod (Callable)" hatası veriyordu. Güçlü referans şart.
var _cmd_instances: Array = []

func _init(ei: EditorInterface, lc: LogCollector) -> void:
	_ei = ei
	_lc = lc

func register() -> void:
	# preload yerine load() kullan: editor reload sirasinda preload cache'i
	# bazen res:// script'lerini bulamayip null dondurebiliyor.
	var base := "res://addons/godot_mcp/commands/"
	# Sorun 7 fix (derinlemesine savunma): her komut nesnesi null kontrolünden
	# geçirilir; null kalırsa o modülün handler'ları kaydedilmez ve gürültülü
	# log basılır (null.get_state gibi sessiz runtime hataları yerine).
	var scene_cmd = _create_cmd(base + "cmd_scene.gd")
	var node_cmd = _create_cmd(base + "cmd_node.gd")
	var script_cmd = _create_cmd(base + "cmd_script.gd")
	var editor_cmd = _create_cmd(base + "cmd_editor.gd")
	var fs_cmd = _create_cmd(base + "cmd_filesystem.gd")
	var res_cmd = _create_cmd(base + "cmd_resource.gd")
	var screenshot_cmd = _create_cmd(base + "cmd_screenshot.gd")
	var update_cmd = _create_cmd(base + "cmd_update.gd")

	if scene_cmd:
		_handlers["scene_open"]         = scene_cmd.open_scene
		_handlers["scene_save"]         = scene_cmd.save_scene
		_handlers["scene_create"]       = scene_cmd.create_scene
		_handlers["scene_list_opened"]  = scene_cmd.list_opened
		_handlers["scene_get_data"]     = scene_cmd.get_data
		_handlers["scene_close"]        = scene_cmd.close_scene

	if node_cmd:
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

	if script_cmd:
		_handlers["script_read"]            = script_cmd.read_script
		_handlers["script_create"]          = script_cmd.create_script
		_handlers["script_update"]          = script_cmd.update_script
		_handlers["script_delete"]          = script_cmd.delete_script
		_handlers["script_attach_to_node"]  = script_cmd.attach_to_node
		_handlers["script_get_errors"]      = script_cmd.get_errors

	if editor_cmd:
		_handlers["editor_play"]                    = editor_cmd.play
		_handlers["editor_stop"]                    = editor_cmd.stop
		_handlers["editor_pause"]                   = editor_cmd.pause
		_handlers["editor_get_state"]               = editor_cmd.get_state
		_handlers["editor_selection_get"]           = editor_cmd.selection_get
		_handlers["editor_selection_set"]           = editor_cmd.selection_set
		_handlers["editor_get_project_settings"]    = editor_cmd.get_project_settings
		_handlers["editor_set_project_setting"]     = editor_cmd.set_project_setting
		_handlers["editor_get_project_path"]        = editor_cmd.get_project_path

	if fs_cmd:
		_handlers["filesystem_list"]        = fs_cmd.list_files
		_handlers["filesystem_read_file"]   = fs_cmd.read_file
		_handlers["filesystem_write_file"]  = fs_cmd.write_file
		_handlers["filesystem_delete"]      = fs_cmd.delete_file
		_handlers["filesystem_move"]        = fs_cmd.move_file
		_handlers["filesystem_reimport"]    = fs_cmd.reimport
		_handlers["filesystem_search"]      = fs_cmd.search_files

	if res_cmd:
		_handlers["resource_get_data"]  = res_cmd.get_data
		_handlers["resource_modify"]    = res_cmd.modify
		_handlers["resource_create"]    = res_cmd.create_resource
		_handlers["resource_delete"]    = res_cmd.delete_resource

	_handlers["console_get_logs"]   = func(p): return _lc.get_logs(p)
	_handlers["console_clear_logs"] = func(p): _lc.clear(); return {"success": true}
	_handlers["console_get_errors"] = func(p): return _lc.get_errors(p)

	if screenshot_cmd:
		_handlers["screenshot_viewport"] = screenshot_cmd.capture_viewport
		_handlers["screenshot_game"]     = screenshot_cmd.capture_game

	# Sağlık kontrolü ve meta
	_handlers["ping"]          = func(p): return {"success": true, "result": {"pong": true, "time": Time.get_unix_time_from_system()}}
	_handlers["get_version"]   = func(p): return {"success": true, "result": {"godot_version": Engine.get_version_info(), "plugin_version": _get_plugin_version()}}

	# Addon güncelleme
	if update_cmd:
		_handlers["update_addon"]        = update_cmd.update_addon
		_handlers["update_addon_push"]   = update_cmd.update_addon_push

# Komut script'ini yükle + örnekle. Başarısızsa null döner ve gürültülü log basar.
func _create_cmd(path: String):
	var script = load(path)
	if script == null:
		push_error("[GodotMCP] Komut scripti yüklenemedi: " + path)
		return null
	# Parse hatası olan script'te new() "Nonexistent function" hatası verir;
	# önce can_instantiate() ile kontrol edip temiz log bas.
	if not script.can_instantiate():
		push_error("[GodotMCP] Komut scripti derlenemedi (parse hatası — üstteki ERROR satırlarına bakın): " + path)
		return null
	var inst = script.new(_ei)
	if inst == null:
		push_error("[GodotMCP] Komut nesnesi oluşturulamadı (new() null döndü): " + path)
		return null
	_cmd_instances.append(inst)  # Sorun 30: GC'den koru
	return inst

func dispatch(command: String, params: Dictionary) -> Dictionary:
	if not _handlers.has(command):
		return {"success": false, "error": "Bilinmeyen komut: " + command}

	var handler = _handlers[command]
	var result = await handler.call(params)

	# Sorun 11 fix: handler içinde runtime exception fırlarsa GDScript coroutine'i
	# yutup null/boş döndürür. Bunu yakalayıp anlamlı hata üret (timeout yerine).
	if not result is Dictionary:
		push_error("[GodotMCP] Handler '%s' geçersiz yanıt döndü: %s" % [command, str(result)])
		return {"success": false, "error": "Komut '%s' çalışma zamanı hatası verdi (geçersiz yanıt). Detay için console_get_errors çağırın." % command}
	if not result.has("success"):
		push_error("[GodotMCP] Handler '%s' 'success' anahtarı döndürmedi: %s" % [command, str(result)])
		return {"success": false, "error": "Komut '%s' hatalı yanıt formatı döndürdü." % command}
	# Sorun 9 fix: başarısız ama hata mesajı boş/null ise anlamlı mesaj doldur.
	if result.get("success") == false:
		var err = result.get("error")
		if err == null or str(err).is_empty():
			result["error"] = "Komut '%s' hata döndü ama mesaj boş. Detay için console_get_errors çağırın." % command
	return result

# Plugin versiyonunu tek kaynaktan (plugin.cfg) okur.
# Server tarafı auto-update karşılaştırması için kullanır.
func _get_plugin_version() -> String:
	var cfg := ConfigFile.new()
	if cfg.load("res://addons/godot_mcp/plugin.cfg") == OK:
		return str(cfg.get_value("plugin", "version", "unknown"))
	return "unknown"
