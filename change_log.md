# Changelog

### v1.2.0 (Latest)
- **Full Input Passthrough (Buttons, D-Pad, Right Stick)**:
  - Meneruskan seluruh input fisik controller (A, B, X, Y, LB, RB, Start, Back/Select, LS Click, RS Click, D-Pad, dan Right Stick X/Y) langsung ke controller virtual tanpa lag.
  - Zero-overhead 1 syscall XInput batch polling di worker loop 250 Hz tanpa komputasi filter pada input passthrough.
  - Dukungan passthrough lengkap baik pada mode **Virtual Xbox 360 (ViGEm)** maupun mode **vJoy**.
- **Unified Batch Output Report**: Seluruh input (4 axis yang difilter + semua tombol & stick kanan) di-submit secara atomik dalam 1 report per tick.

---

### v1.1.0
- **Virtual Xbox 360 Output (ViGEmBus)**: Native XInput output emulation for genuine 1:1 trigger linearity in Assetto Corsa and modern racing games.
- **Dual Output Mode**: Pilihan mode output (`Xbox 360` vs `vJoy`) langsung dari UI dengan penyimpanan otomatis ke `config.json`.
- **Dynamic UI Badges**: Indikator target (`🎯 TARGET XBOX 360` / `🎯 TARGET vJOY`) dan subtitle routing pada tiap Axis Card otomatis menyesuaikan mode yang aktif.
- **Unified Output Architecture**: Abstraksi `IOutputDevice` untuk transisi device yang mulus tanpa overhead pada worker loop 250 Hz.

---

### v1.0.0 (Initial Release)
- **250 Hz Precision Pipeline**: Real-time signal conditioning dengan latency sub-millisecond.
- **Dual Signal Filter Engine**: EMA Smoothing (anti-jitter) & Schmitt-Trigger Hysteresis (anti-flutter deadzone).
- **Interactive 4-Axis Dashboard**: Visualisasi RAW & OUTPUT level meter per-axis dengan tuning independen.
- **vJoy 4-Axis Routing**: Pemetaan simultan untuk Throttle, Brake, Steering, dan Pitch.
- **System Tray & Autostart**: Dukungan minimize to tray dan auto-run saat Windows booting.
- **Installer & Standalone Binary**: Single-file portable executable & Inno Setup installer.
