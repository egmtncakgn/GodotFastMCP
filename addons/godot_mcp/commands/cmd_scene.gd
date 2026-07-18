@tool
extends RefCounted

# Sorun 7 fix (genişletildi): Godot 4.7'de RefCounted + @tool sınıfında
# EditorInterface TİPLİ üye değişken de bozuk bytecode üretiyor
# ("Internal script error! Opcode: 28"). Üye ve parametre TİPSİZ.
var _ei

func _init(ei) -> void:
	_ei = ei

func open_scene(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	if path.is_empty():
		return {"success": false, "error": "path parametresi gerekli."}
	if not FileAccess.file_exists(path):
		return {"success": false, "error": "Dosya bulunamadı: " + path}
	
	_ei.open_scene_from_path(path)
	return {"success": true, "result": {"path": path, "message": "Sahne açıldı."}}

func save_scene(params: Dictionary) -> Dictionary:
	# Sorun 13 fix: path parametresi artık gerçekten kullanılıyor.
	# path boş → aktif sahne kendi konumuna kaydedilir.
	# path dolu → EditorInterface.save_scene_as(path) ile o konuma kaydedilir.
	# NOT: Godot 4.7'de save_scene_as() VOID döner (Error değil) — dönüş atanamaz;
	# başarı, sahnenin yolunun güncellenmesiyle doğrulanır.
	var path: String = params.get("path", "")
	var scene = _ei.get_edited_scene_root()
	if not scene:
		return {"success": false, "error": "Açık sahne yok."}

	if path.is_empty():
		_ei.save_scene()
	else:
		_ei.save_scene_as(path)
		if scene.scene_file_path != path:
			return {"success": false, "error": "Sahne '%s' konumuna kaydedilemedi (save_scene_as başarısız)." % path}
	return {"success": true, "result": {"message": "Sahne kaydedildi.", "path": scene.scene_file_path}}

func create_scene(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	var root_type: String = params.get("root_node_type", "Node2D")
	if path.is_empty():
		return {"success": false, "error": "path gerekli."}
	
	var packed = PackedScene.new()
	var root = ClassDB.instantiate(root_type)
	if not root:
		return {"success": false, "error": "Geçersiz node tipi: " + root_type}
	root.name = path.get_file().get_basename()
	packed.pack(root)
	
	var err = ResourceSaver.save(packed, path)
	if err != OK:
		return {"success": false, "error": "Sahne kaydedilemedi. Hata kodu: " + str(err)}
	
	_ei.open_scene_from_path(path)
	return {"success": true, "result": {"path": path}}

func list_opened(params: Dictionary) -> Dictionary:
	var scenes = []
	for i in _ei.get_open_scenes().size():
		scenes.append(_ei.get_open_scenes()[i])
	return {"success": true, "result": {"scenes": scenes}}

func get_data(params: Dictionary) -> Dictionary:
	# Sorun 13 fix: path verilirse sahne EDİTÖRDE AÇILMADAN diskten yüklenip
	# node ağacı döndürülür; boşsa aktif sahne kullanılır.
	# Sorun 18 fix: max_depth artık parametre (varsayılan 10).
	var path: String = params.get("path", "")
	var max_depth: int = int(params.get("max_depth", 10))

	if path.is_empty():
		var scene = _ei.get_edited_scene_root()
		if not scene:
			return {"success": false, "error": "Açık sahne yok."}
		return {"success": true, "result": _node_to_dict(scene, 0, max_depth)}

	if not ResourceLoader.exists(path):
		return {"success": false, "error": "Sahne bulunamadı: " + path}
	var packed := ResourceLoader.load(path) as PackedScene
	if not packed:
		return {"success": false, "error": "Sahne yüklenemedi (PackedScene değil): " + path}
	var inst := packed.instantiate()
	var data := _node_to_dict(inst, 0, max_depth)
	inst.free()
	return {"success": true, "result": data}

func _node_to_dict(node: Node, depth: int = 0, max_depth: int = 10) -> Dictionary:
	if depth > max_depth:
		return {}
	var data = {
		"name": node.name,
		"type": node.get_class(),
		"path": str(node.get_path()),
		"children": [],
	}
	for child in node.get_children():
		data["children"].append(_node_to_dict(child, depth + 1, max_depth))
	return data

func close_scene(params: Dictionary) -> Dictionary:
	# Sorun 13 fix: Godot API'si sadece AKTİF sahneyi kapatabilir.
	# path verilmişse ve aktif sahneyle eşleşmiyorsa sessizce yoksaymak yerine
	# açık hata döndür.
	var path: String = params.get("path", "")
	if not path.is_empty():
		var scene = _ei.get_edited_scene_root()
		var active_path: String = scene.scene_file_path if scene else ""
		if active_path != path:
			return {"success": false, "error": "Sadece aktif sahne kapatılabilir (aktif: '%s'). Önce scene_open ile açın." % active_path}
	_ei.close_scene()
	return {"success": true, "result": {"message": "Sahne kapatıldı."}}
