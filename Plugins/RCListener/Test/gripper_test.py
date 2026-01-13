import time
from pymavlink import mavutil

DO_GRIPPER = 211

# Бітова маска (як у твоїй системі)
# Lock1=8, Lock2=4, Lock3=2, Lock4=1
LOCK_BITS = {1: 8, 2: 4, 3: 2, 4: 1}

def decode_mask_to_changes(prev_mask: int, new_mask: int):
    """Return list of (lock_id, new_state_0_or_1) for bits that changed."""
    changes = []
    diff = prev_mask ^ new_mask
    for lock_id, bit in LOCK_BITS.items():
        if diff & bit:
            changes.append((lock_id, 1 if (new_mask & bit) else 0))
    return changes

def send_open(mav: mavutil.mavfile, target_sys: int, target_comp: int, servo_id: int):
    """Send DO_GRIPPER trigger: only param1 matters in your device; keep others 0."""
    mav.mav.command_long_send(
        target_sys,            # target_system
        target_comp,           # target_component (device compid=169)
        DO_GRIPPER,            # command
        0,                     # confirmation
        float(servo_id),       # param1 = servo/lock number (1..4)
        0.0,                   # param2 (unused in your capture; keep 0)
        0.0, 0.0, 0.0, 0.0, 0.0
    )

def main(conn_str: str,
         device_compid: int = 169,
         gcs_compid: int = 190,
         target_system: int = 1,
         open_on: str = "close"):
    """
    open_on:
      - "close" : send open command when lock becomes CLOSED (bit goes 0->1)
      - "open"  : send open command when lock becomes OPENED (bit goes 1->0)
    """

    # Важливо: source_system/source_component — це "хто ми"
    # Якщо пристрій фільтрує sender як у "рідній" програмі, постав compid=190.
    mav = mavutil.mavlink_connection(
        conn_str,
        source_system=255,
        source_component=gcs_compid
    )

    # Дочекайся першого heartbeat від будь-кого, щоб піднявся лінк
    print("[*] Waiting for heartbeat...")
    mav.wait_heartbeat()
    print("[*] Heartbeat received.")
    
    print("[*] Waiting for heartbeat from device (compid=169)...")
    
    while True:
        hb = mav.recv_match(type="HEARTBEAT", blocking=True)
        if hb is None:
            print("...no heartbeat (timeout)")
            continue
        sender_comp = int(hb.get_srcComponent())
        sender_sys  = int(hb.get_srcSystem())
        print(sender_sys, sender_comp)
        if hb and sender_comp == 169:
            break

    print("[*] Device heartbeat received. Listening...")

    last_mask = None
    last_action_ts = 0.0
    min_action_interval_s = 0.25  # анти-спам, підкрути якщо треба
    n = 1

    while True:
        print(f"While next {n}")
        n = n + 1
        msg = mav.recv_match(type="COMMAND_LONG", blocking=True, timeout=2)
        if msg is None:
            print("...no COMMAND_LONG (timeout)")
            continue

        # 1) Фільтруємо тільки DO_GRIPPER
        if int(msg.command) != DO_GRIPPER:
            continue

        # 2) Реагуємо тільки на "стан", який йде ВІД пристрою compid=169 ДО GCS compid=190
        # sender у заголовку:
        sender_comp = int(msg.get_srcComponent())
        sender_sys  = int(msg.get_srcSystem())

        # target у payload:
        tgt_comp = int(msg.target_component)
        # tgt_sys = int(msg.target_system)  # у тебе часто FF, тому по ньому не фільтруй жорстко

        if sender_comp != device_compid:
            continue
        if tgt_comp != gcs_compid:
            continue

        # 3) Маска стану у твоїй системі в param3 (float)
        new_mask = int(round(float(msg.param3)))

        # Ініціалізація
        if last_mask is None:
            last_mask = new_mask
            print(f"[*] Initial mask={last_mask} (bin={last_mask:04b})")
            continue

        if new_mask == last_mask:
            continue

        changes = decode_mask_to_changes(last_mask, new_mask)
        print(f"[+] Mask {last_mask:02d}({last_mask:04b}) -> {new_mask:02d}({new_mask:04b}) | changes={changes}")

        # 4) Вирішуємо, на які події реагувати
        # close: 0->1, open: 1->0
        for lock_id, state in changes:
            want = (open_on == "close" and state == 1) or (open_on == "open" and state == 0)
            if not want:
                continue

            now = time.time()
            if now - last_action_ts < min_action_interval_s:
                continue

            print(f"[>] Trigger OPEN for lock/servo #{lock_id} -> sending COMMAND_LONG DO_GRIPPER param1={lock_id} to target {target_system}:{device_compid}")
            send_open(mav, target_system, device_compid, lock_id)
            last_action_ts = now

        last_mask = new_mask

if __name__ == "__main__":
    # Приклади conn_str:
    #   UDP (слухати порт):  "udp:0.0.0.0:14550"
    #   UDP (підключитись до remote): "udpout:192.168.0.10:14550"
    #   Serial (Windows):    "COM15,115200"
    #
    # !!! Зміни conn_str під свій випадок !!!
    conn_str = "COM15,115200"
    main(conn_str)
