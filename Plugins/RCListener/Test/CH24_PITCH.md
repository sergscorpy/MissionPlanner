# TOPOTEK CH24 pitch control

Only the `TOPOTEK` profile uses CH24 for angle positioning. The SiYi profile is unchanged.

| CH24 PWM | Target pitch |
| --- | --- |
| 1000 | -90 degrees (down) |
| 1500 | -20 degrees |
| 2000 | +50 degrees (up) |

Native RadioMaster PWM values 988..2012 are accepted and clamped to 1000..2000.
Missing, zero, unparseable, and out-of-range CH24 values do not issue angle commands.
The first valid frame after connection or profile selection sets the target immediately.

Commands use `GAP`, signed hundredths of a degree, and speed 99 (9.9 degrees/second).
Changes of at least 0.5 degrees are sent at most once every 100 ms. The most recent
target is used, and a smaller final change is sent after 150 ms of stable input.
No periodic commands are sent for an unchanged target.

CH24 takes priority over simultaneous CH19/CH21 pitch actions when a new angle
target is due, including while rate-limited. A subsequent CH19/CH21 action with
stationary CH24 cancels pending angle positioning. Moving CH24 resumes positioning.
The -90..+50 degree limit applies to CH24 targets, not to manual speed control.

Protocol reference: [TOPOTEK SHD protocol, pages 8-9 and 18](https://usaimages.oss-us-west-1.aliyuncs.com/14512/download/protocol/Topotek-SHD-series-Protocol_en20200422.pdf).
Direction, firmware support, and suitable transition speed must still be checked on the camera.

Build the plugin in Debug, then run `Plugins/RCListener/Test/run_pitch_tests.ps1`.
The standalone tests cover PWM mapping, command bytes/checksums, missing channels,
timing, manual takeover, profile selection, and reconnect. UDP integration tests
use only a loopback receiver at `127.0.0.1`; they do not contact camera hardware.
