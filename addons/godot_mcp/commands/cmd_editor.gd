@tool
extends RefCounted

# Sorun 7 fix (genişletildi): Godot 4.7'de RefCounted + @tool sınıfında
# EditorInterface TİPLİ üye değişken de bozuk bytecode üretiyor
# ("Internal script error! Opcode: 28"). Üye ve parametre TİPSİZ.
var _ei

func _init(ei) -> void:
	_ei = ei

func play(params: Dictionary) -> Dictionary:
	# Sorun 32 fix: EditorInterface.is_playing() Godot 4.x'te YOK; doğru API is_playing_scene().
	if _ei.is_playing_scene():
		return {"success": false, "error": "Oyun zaten çalışıyor."}
	_ei.play_main_scene()
	return {"success": true, "result": {"message": "Oyun başlatıldı."}}

func stop(params: Dictionary) -> Dictionary:
	if not _ei.is_playing_scene():
		return {"success": false, "error": "Oyun çalışmıyor."}
	# Sorun 32 fix: stop_playing() Godot 4.7'de yok; stop_playing_scene() kullanılır.
	_ei.stop_playing_scene()
	return {"success": true, "result": {"message": "Oyun durduruldu."}}

func pause(params: Dictionary) -> Dictionary:
	# Sorun 32 fix: Godot 4.7'de EditorPlayback sınıfı kaldırıldı; editor API'si
	# üzerinden çalışan oyunu pause etmenin yolu yok (oyun ayrı process).
	# Sessizce yanlış sonuç dönmek yerine açık hata ver.
	if not _ei.is_playing_scene():
		return {"success": false, "error": "Oyun çalışmıyor."}
	return {"success": false, "error": "Godot 4.7 editor API'sinde pause desteği yok (EditorPlayback kaldırıldı)."}

func get_state(params: Dictionary) -> Dictionary:
	# Sorun 32 fix: Godot 4.7 API'si: is_playing_scene() + get_playing_scene().
	# paused bilgisi editor API'sinde artık yok → her zaman false dönülür.
	var playing = _ei.is_playing_scene()
	var scene_path = _ei.get_playing_scene() if playing else ""

	var status = "playing" if playing else "stopped"
	return {"success": true, "result": {"state": status, "playing": playing, "paused": false, "scene": scene_path}}

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
	# NOT: Windows'ta ters eğik çizgili (C:\...) native yol döner (Sorun 26).
	# JSON serialize sırasında otomatik escape edilir; C# tarafı Path.Combine /
	# Directory.Exists ile sorunsuz kullanır. Godot tarzı '/' isterseniz
	# .replace("\\", "/") uygulayın.
	return {"success": true, "result": {"path": ProjectSettings.globalize_path("res://")}}
