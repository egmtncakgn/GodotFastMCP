@tool
extends RefCounted

var _ei: EditorInterface

func _init(ei: EditorInterface) -> void:
	_ei = ei

func play(params: Dictionary) -> Dictionary:
	if _ei.is_playing():
		return {"success": false, "error": "Oyun zaten çalışıyor."}
	_ei.play_main_scene()
	return {"success": true, "result": {"message": "Oyun başlatıldı."}}

func stop(params: Dictionary) -> Dictionary:
	if not _ei.is_playing():
		return {"success": false, "error": "Oyun çalışmıyor."}
	_ei.stop_playing()
	return {"success": true, "result": {"message": "Oyun durduruldu."}}

func pause(params: Dictionary) -> Dictionary:
	var state = _ei.get_editor_playback()
	if not state:
		return {"success": false, "error": "Oyun çalışmıyor."}
	state.paused = not state.paused
	return {"success": true, "result": {"paused": state.paused}}

func get_state(params: Dictionary) -> Dictionary:
	var playing = _ei.is_playing()
	var paused = false
	var state = _ei.get_editor_playback()
	if state:
		paused = state.paused
	
	var status = "stopped"
	if playing:
		status = "paused" if paused else "playing"
	
	return {"success": true, "result": {"state": status, "playing": playing, "paused": paused}}

func selection_get(params: Dictionary) -> Dictionary:
	var selection = _ei.get_selection()
	var selected = selection.get_selected_nodes()
	var paths = []
	for node in selected:
		paths.append(str(node.get_path()))
	return {"success": true, "result": {"paths": paths}}

func selection_set(params: Dictionary) -> Dictionary:
	var node_paths: Array = params.get("node_paths", [])
	var selection = _ei.get_selection()
	selection.clear()
	for p in node_paths:
		var node = _ei.get_edited_scene_root().get_node_or_null(NodePath(p))
		if node:
			selection.add_node(node)
	return {"success": true, "result": {"message": "Seçim güncellendi."}}

func get_project_settings(params: Dictionary) -> Dictionary:
	var keys: Array = params.get("keys", [])
	var result = {}
	if keys.is_empty():
		keys = ProjectSettings.get_property_list().map(func(p): return p["name"])
	for key in keys:
		if ProjectSettings.has_setting(key):
			result[key] = ProjectSettings.get_setting(key)
	return {"success": true, "result": {"settings": result}}

func set_project_setting(params: Dictionary) -> Dictionary:
	var key: String = params.get("key", "")
	var value = params.get("value", null)
	if key.is_empty():
		return {"success": false, "error": "key gerekli."}
	ProjectSettings.set_setting(key, value)
	ProjectSettings.save()
	return {"success": true, "result": {"message": "Proje ayarı güncellendi."}}

func get_project_path(params: Dictionary) -> Dictionary:
	var path = OS.get_executable_path().get_base_dir()
	return {"success": true, "result": {"path": ProjectSettings.globalize_path("res://")}}
