from flask import Flask, jsonify, request
import threading
import time
import math
import json
import random

# ---------------------- DEVICE MOCK ----------------------

device_app = Flask("device")
center_lat = 50.4501
center_lon = 30.5234
radius = 0.001
angle = 0.0
device_connected = True


@device_app.route("/api/status/ping", methods=["GET"])
def device_ping():
    if device_connected:
        return jsonify({"connected": True, "sats": random.randint(10, 18)}), 200
    else:
        return jsonify({"connected": False, "sats": 0}), 200


@device_app.route("/api/telemetry", methods=["GET"])
def telemetry():
    """Эмулирует круговое движение и возвращает координаты"""
    global angle
    angle += 0.05
    lat = center_lat + radius * math.cos(angle)
    lon = center_lon + radius * math.sin(angle)
    alt = 250 + 5 * math.sin(angle * 2)
    sats = random.randint(10, 18)
    ping = int(30 + 20 * abs(math.sin(angle * 2)))

    data = {"lat": lat, "lon": lon, "alt": alt, "sats": sats, "ping": ping}
    return jsonify(data), 200


@device_app.route("/api/restart", methods=["POST"])
def restart():
    print("[DEVICE] Restart requested.")
    return jsonify({"ok": True, "message": "Device restarting..."}), 200


@device_app.route("/api/marker", methods=["POST"])
def marker():
    data = request.get_json(force=True, silent=True)
    print(f"[DEVICE] Marker received: {json.dumps(data)}")
    return jsonify({"ok": True}), 200


# ---------------------- ORCHESTRATOR MOCK ----------------------

orch_app = Flask("orchestrator")
device_ip = "127.0.0.1"
device_port = 5001
device_status = "connected"


@orch_app.route("/api/status/ping", methods=["GET"])
def orchestrator_ping():
    """Возвращает статус и координаты устройства"""
    status = {
        "status": device_status,
        "device_ip": device_ip,
        "device_port": device_port,
    }
    return jsonify(status), 200


# ---------------------- RUN SERVERS ----------------------


def run_device():
    print(f"[DEVICE] Mock device running on http://{device_ip}:{device_port}")
    device_app.run(host=device_ip, port=device_port, threaded=True, debug=False)


def run_orchestrator():
    print("[ORCH] Mock orchestrator running on http://127.0.0.1:5000")
    orch_app.run(host="127.0.0.1", port=5000, threaded=True, debug=False)


if __name__ == "__main__":
    threading.Thread(target=run_device, daemon=True).start()
    run_orchestrator()
