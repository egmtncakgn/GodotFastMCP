@tool
extends RefCounted

var _ei: EditorInterface

func _init(ei: EditorInterface) -> void:
	_ei = ei

func capture_viewport(params: Dictionary) -> Dictionary:
	var viewport = _ei.get_editor_viewport_3d(0)
	if not viewport:
		# Fallback to main viewport
		viewport = _ei.get_base_control().get_viewport()
	if not viewport:
		return {"success": false, "error": "Viewport bulunamadı."}
	
	var img = viewport.get_texture().get_image()
	if not img:
		return {"success": false, "error": "Görüntü alınamadı."}
	
	var buffer = img.save_png_to_buffer()
	var base64 = Marshalls.raw_to_base64(buffer)
	return {"success": true, "result": {"image_base64": base64, "width": img.get_width(), "height": img.get_height()}}

func capture_game(params: Dictionary) -> Dictionary:
	if not _ei.is_playing():
		return {"success": false, "error": "Oyun çalışmıyor."}
	
	# Godot 4: use get_viewport() from the running scene tree
	var viewport = _ei.get_viewport()
	if not viewport:
		return {"success": false, "error": "Game viewport bulunamadı."}
	
	var img = viewport.get_texture().get_image()
	if not img:
		return {"success": false, "error": "Görüntü alınamadı."}
	
	var buffer = img.save_png_to_buffer()
	var base64 = Marshalls.raw_to_base64(buffer)
	return {"success": true, "result": {"image_base64": base64, "width": img.get_width(), "height": img.get_height()}}
