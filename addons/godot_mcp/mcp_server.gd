@tool
extends Node

class_name McpServer

const PORT_RANGE_START = 46300
const PORT_RANGE_END = 46400
const BIND_ADDRESS = "127.0.0.1"
const MAX_PEERS = 16

var _tcp_server: TCPServer
var _peers: Array[WebSocketPeer] = []  # Çoklu bağlantı desteği
var _dispatcher: CommandDispatcher
var _port: int
var _bound_port: int = -1

var editor_interface: EditorInterface
var log_collector: LogCollector

func _init(ei: EditorInterface, lc: LogCollector) -> void:
	editor_interface = ei
	log_collector = lc

func start() -> void:
	_dispatcher = CommandDispatcher.new(editor_interface, log_collector)
	add_child(_dispatcher)
	_dispatcher.register()

	# 1) Manuel env override (GODOT_MCP_PORT)
	var start_port := PORT_RANGE_START
	if OS.has_environment("GODOT_MCP_PORT"):
		start_port = int(OS.get_environment("GODOT_MCP_PORT"))

	# 2) Server'ın daha önce seçtiği portu lock dosyasından oku (sıcak yeniden başlatmada stabilite)
	var cached := _read_port_lock()
	if cached > 0:
		start_port = cached

	# 3) Boş portu bul (önce cached, sonra tüm aralık)
	var candidates := PackedInt32Array()
	if _is_port_free(start_port):
		candidates.append(start_port)
	for p in range(PORT_RANGE_START, PORT_RANGE_END + 1):
		if p != start_port and _is_port_free(p):
			candidates.append(p)

	for candidate in candidates:
		_tcp_server = TCPServer.new()
		if _tcp_server.listen(candidate, BIND_ADDRESS) == OK:
			_bound_port = candidate
			_write_port_lock(_bound_port)
			push_warning("[GodotMCP] WebSocket sunucusu 127.0.0.1:%d üzerinde başlatıldı. (%d aktif bağlantı destekleniyor)" % [_bound_port, MAX_PEERS])
			return
		else:
			_tcp_server = null

	push_error("[GodotMCP] Hiçbir port (%d-%d) dinlenemiyor!" % [PORT_RANGE_START, PORT_RANGE_END])

func _is_port_free(port: int) -> bool:
	# Godot'da gerçek port probe'u yok; TCPServer.listen ile deneyip geri alıyoruz.
	var s := TCPServer.new()
	var ok := s.listen(port, BIND_ADDRESS) == OK
	if ok:
		s.stop()
	return ok

func _port_lock_path() -> String:
	return OS.get_user_data_dir().path_join("GodotMCP/port.txt")

func _write_port_lock(port: int) -> void:
	var path := _port_lock_path()
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	FileAccess.open(path, FileAccess.WRITE).store_string(str(port))

func _read_port_lock() -> int:
	var path := _port_lock_path()
	if FileAccess.file_exists(path):
		var f := FileAccess.open(path, FileAccess.READ)
		var txt := f.get_as_text().strip_edges()
		if txt.is_valid_int():
			return txt.to_int()
	return 0

func _process(_delta: float) -> void:
	# Yeni bağlantıları kabul et (MAX_PEERS ile sınırlı — sızıntı/DoS önlemi)
	if _tcp_server and _tcp_server.is_connection_available():
		var stream = _tcp_server.take_connection()
		if _peers.size() >= MAX_PEERS:
			push_error("[GodotMCP] Maksimum bağlantı sayısına (%d) ulaşıldı, bağlantı reddedildi." % MAX_PEERS)
			stream.disconnect_from_host()
		else:
			var peer = WebSocketPeer.new()
			peer.accept_stream(stream)
			_peers.append(peer)
			push_warning("[GodotMCP] Yeni bağlantı kabul edildi. Toplam: %d" % _peers.size())

	# Tüm peer'ları poll et
	for i in range(_peers.size() - 1, -1, -1):
		var peer = _peers[i]
		peer.poll()

		var state = peer.get_ready_state()
		if state == WebSocketPeer.STATE_OPEN:
			while peer.get_available_packet_count() > 0:
				var data = peer.get_packet().get_string_from_utf8()
				_handle_message(data, peer)
		elif state in [WebSocketPeer.STATE_CLOSING, WebSocketPeer.STATE_CLOSED]:
			_peers.remove_at(i)
			push_warning("[GodotMCP] Bağlantı kapandı. Kalan: %d" % _peers.size())

func _handle_message(json_text: String, peer: WebSocketPeer) -> void:
	var parsed = JSON.parse_string(json_text)
	if not parsed:
		push_error("[GodotMCP] JSON ayrıştırma hatası: " + json_text)
		return

	var request_id: String = parsed.get("id", "")
	var command: String = parsed.get("command", "")
	var params_raw = parsed.get("params", {})
	var params: Dictionary = params_raw if params_raw is Dictionary else {}

	var result = await _dispatcher.dispatch(command, params)

	var response = {
		"id": request_id,
		"success": result.get("success", false),
		"result": result.get("result", null),
		"error": result.get("error", null)
	}

	if peer and peer.get_ready_state() == WebSocketPeer.STATE_OPEN:
		peer.send_text(JSON.stringify(response))

func stop() -> void:
	for peer in _peers:
		if peer.get_ready_state() == WebSocketPeer.STATE_OPEN:
			peer.close()
	_peers.clear()
	if _tcp_server:
		_tcp_server.stop()
	push_warning("[GodotMCP] Sunucu durduruldu (port %d)." % _bound_port)
