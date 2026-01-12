import socket
import time
from typing import Optional, Tuple

UDP_IP = "192.168.144.108"
UDP_PORT = 9003

# Команди як ти дав (ASCII рядки)
CMDS = {
    "down":        "#TPPG2wPTZ0A76",
    "center":      "#TPPG2wPTZ056A",
    "zoom_in":     "#TPPM2wZMC0259",
    "zoom_stop":   "#TPPM2wZMC0057",
    "zoom_out":    "#TPPM2wZMC0158",
    "pitch_up":    "#TPUG2wGSPE26D",
    "pitch_stop":  "#TPPG2wPTZ0065",
    "pitch_down":  "#TPUG2wGSP1E6C",
    "termal_next": "#TPPD2wIMG0A52",
    "pip_change":  "#TPPD2wPIP0A5E",
}

def send_cmd(sock: socket.socket, name: str, cmd: str, expect_reply: bool = True, timeout_s: float = 0.25):
    payload = cmd.encode("ascii", errors="strict")

    print(f"\n== {name} ==")
    print("TX ASCII:", cmd)
    print("TX HEX  :", payload.hex().upper())

    sock.sendto(payload, (UDP_IP, UDP_PORT))

    if not expect_reply:
        return

    sock.settimeout(timeout_s)
    try:
        data, addr = sock.recvfrom(2048)
        # Спробуємо показати як ASCII (не всі відповіді можуть бути друковані)
        try:
            ascii_text = data.decode("ascii", errors="replace")
        except Exception:
            ascii_text = "<decode error>"

        print("RX from:", addr)
        print("RX HEX :", data.hex().upper())
        print("RX ASCII:", ascii_text)
    except socket.timeout:
        print("RX: (no reply)")

def main(bind_port: Optional[int] = None):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    # Якщо хочеш, можеш зафіксувати вихідний порт (наприклад 9004 або 8080).
    # Якщо bind_port=None — ОС вибере будь-який вільний порт.
    if bind_port is not None:
        sock.bind(("0.0.0.0", bind_port))
        print(f"Local UDP bound to :{bind_port}")
    else:
        print("Local UDP port: (auto)")

    print(f"Sending to {UDP_IP}:{UDP_PORT}")

    # ---- Тестова послідовність ----
    script = [
        ("center",      1.5),
        ("down",        1.5),

        ("pitch_up",    1.2),
        ("pitch_stop",  0.6),
        ("pitch_down",  1.2),
        ("pitch_stop",  0.6),

        ("zoom_in",     1.2),
        ("zoom_stop",   0.6),
        ("zoom_out",    1.2),
        ("zoom_stop",   0.6),

        ("termal_next", 0.8),
        ("termal_next", 0.8),
        ("termal_next", 0.8),
        ("termal_next", 0.8),
        ("pip_change",  0.8),
        ("pip_change",  0.8),
        ("center",      1.5),
    ]

    for cmd_name, delay_s in script:
        cmd = CMDS[cmd_name]
        send_cmd(sock, cmd_name, cmd, expect_reply=True)
        time.sleep(delay_s)

    sock.close()
    print("\nDone.")

if __name__ == "__main__":
    # Можеш поставити bind_port=9004 (як в докі) або bind_port=8080 (як у їхній програмі),
    # або залишити None.
    main(bind_port=None)
