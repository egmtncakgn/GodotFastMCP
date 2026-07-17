@tool
extends RefCounted

var _ei: EditorInterface

func _init(ei: EditorInterface) -> void:
	_ei = ei

func get_data(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	if path.is_empty():
		return {"success": false, "error": "path gerekli."}
	if not ResourceLoader.exists(path):
		return {"success": false, "error": "Resource bulunamadı: " + path}
	
	var res = ResourceLoader.load(path)
	if not res:
		return {"success": false, "error": "Resource yüklenemedi: " + path}
	
	var props = {}
	for i in res.get_property_list().size():
		var p = res.get_property_list()[i]
		if p["usage"] & PROPERTY_USAGE_STORAGE:
			props[p["name"]] = res.get(p["name"])
	
	return {"success": true, "result": {"path": path, "properties": props}}

func modify(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	var properties: Dictionary = params.get("properties", {})
	
	if path.is_empty():
		return {"success": false, "error": "path gerekli."}
	
	var res = ResourceLoader.load(path)
	if not res:
		return {"success": false, "error": "Resource yüklenemedi: " + path}
	
	for key in properties:
		res.set(key, properties[key])
	
	var err = ResourceSaver.save(res, path)
	if err != OK:
		return {"success": false, "error": "Kaydedilemedi. Hata: " + str(err)}
	
	return {"success": true, "result": {"message": "Resource güncellendi."}}

func create_resource(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	var type: String = params.get("type", "")
	var properties: Dictionary = params.get("properties", {})
	
	if path.is_empty() or type.is_empty():
		return {"success": false, "error": "path ve type gerekli."}
	
	var res = ClassDB.instantiate(type)
	if not res:
		return {"success": false, "error": "Geçersiz tip: " + type}
	
	for key in properties:
		res.set(key, properties[key])
	
	var err = ResourceSaver.save(res, path)
	if err != OK:
		return {"success": false, "error": "Kaydedilemedi. Hata: " + str(err)}
	
	return {"success": true, "result": {"path": path, "message": "Resource oluşturuldu."}}

func delete_resource(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	if path.is_empty():
		return {"success": false, "error": "path gerekli."}
	
	var dir = DirAccess.open(path.get_base_dir())
	if not dir:
		return {"success": false, "error": "Dizin bulunamadı."}
	dir.remove(path.get_file())
	
	return {"success": true, "result": {"message": "Resource silindi."}}
