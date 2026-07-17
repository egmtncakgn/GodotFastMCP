@tool
extends Node

class_name McpServer

const PORT_DEFAULT = 6505
const PORT_SCAN = 3  # 6505, 6506, 6507
const BIND_ADDRESS = "127.0.0.1"

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

func start(port: int = PORT_DEFAULT) -> void:
	_dispatcher = CommandDispatcher.new(editor_interface, log_collector)
	add_child(_dispatcher)
	_dispatcher.register()

	# Port çakışmasına karşı tarama yap: 6505 doluysa 6506/6507'yi dene.
	for i in range(PORT_SCAN):
		var candidate = port + i
		_tcp_server = TCPServer.new()
		if _tcp_server.listen(candidate, BIND_ADDRESS) == OK:
			_bound_port = candidate
			push_warning("[GodotMCP] WebSocket sunucusu 127.0.0.1:%d üzerinde başlatıldı. (%d aktif bağlantı destekleniyor)" % [_bound_port, 16])
			return
		else:
			push_warning("[GodotMCP] Port %d kullanımda, %d deneniyor..." % [candidate, candidate + 1])
			_tcp_server = null

	push_error("[GodotMCP] Hiçbir port (6505-%d) dinlenemiyor! Başka bir Godot editor açık mı?" % (port + PORT_SCAN - 1))

func _process(_delta: float) -> void:
	# Yeni bağlantıları kabul et
	if _tcp_server and _tcp_server.is_connection_available():
		var stream = _tcp_server.take_connection()
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
