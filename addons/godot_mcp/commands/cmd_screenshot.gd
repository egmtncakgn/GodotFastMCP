@tool
extends RefCounted

# Sorun 7 fix (genişletildi): Godot 4.7'de RefCounted + @tool sınıfında
# EditorInterface TİPLİ üye değişken de bozuk bytecode üretiyor
# ("Internal script error! Opcode: 28"). Üye ve parametre TİPSİZ.
var _ei

func _init(ei) -> void:
	_ei = ei

# Sorun 21 fix: eski kod her zaman 3D viewport'u önceliyordu; 2D projelerde
# gizli/boş 3D görünümü yakalanıyordu. Artık "mode" parametresi var:
#   "2d"   → ana editor viewport'u (2D ekranı dahil tüm görünür alan)
#   "3d"   → 3D editor viewport'u (eski davranış)
#   "auto" → 3D viewport yalnızca ekranda GÖRÜNÜYORSA 3D, değilse 2D (varsayılan)
func capture_viewport(params: Dictionary) -> Dictionary:
	var mode: String = params.get("mode", "auto")
	var viewport = _pick_viewport(mode)
	if not viewport:
		return {"success": false, "error": "Viewport bulunamadı."}
	
	var img = viewport.get_texture().get_image()
	if not img:
		return {"success": false, "error": "Görüntü alınamadı."}
	
	var buffer = img.save_png_to_buffer()
	var base64 = Marshalls.raw_to_base64(buffer)
	return {"success": true, "result": {"image_base64": base64, "width": img.get_width(), "height": img.get_height()}}

func _pick_viewport(mode: String) -> Viewport:
	var vp3d = _ei.get_editor_viewport_3d(0)
	var main_vp = _ei.get_base_control().get_viewport()
	match mode:
		"3d":
			return vp3d if vp3d else main_vp
		"2d":
			return main_vp
		_: # "auto"
			if vp3d and _is_viewport_visible(vp3d):
				return vp3d
			return main_vp

# SubViewport Control değildir; ebeveyn zincirindeki Control'lerin
# görünürlüğüne bakarak editor'de o an ekranda olup olmadığını anlar.
func _is_viewport_visible(vp: Viewport) -> bool:
	var p = vp.get_parent()
	while p:
		if p is CanvasItem and not (p as CanvasItem).is_visible_in_tree():
			return false
		p = p.get_parent()
	return true

func capture_game(params: Dictionary) -> Dictionary:
	# Sorun 32 fix: is_playing() -> is_playing_scene(); EditorInterface.get_viewport()
	# Godot 4.x'te yok. Çalışan oyun ayrı bir process olduğundan oyun penceresinin
	# viewport'una editor üzerinden doğrudan erişilemez; bu durumda açık hata dön.
	if not _ei.is_playing_scene():
		return {"success": false, "error": "Oyun çalışmıyor."}

	if not _ei.has_method("get_viewport"):
		return {"success": false, "error": "Bu Godot sürümünde çalışan oyunun viewport'una editor üzerinden erişilemiyor (oyun ayrı process). screenshot_viewport kullanın."}
	var viewport = _ei.call("get_viewport")
	if not viewport:
		return {"success": false, "error": "Game viewport bulunamadı."}
	
	var img = viewport.get_texture().get_image()
	if not img:
		return {"success": false, "error": "Görüntü alınamadı."}
	
	var buffer = img.save_png_to_buffer()
	var base64 = Marshalls.raw_to_base64(buffer)
	return {"success": true, "result": {"image_base64": base64, "width": img.get_width(), "height": img.get_height()}}
