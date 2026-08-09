# Changelog

### v1.3.0 (Latest)
- **Vibration Feedback System**:
  - Dukungan getaran *Rumble Motor* (Left/Right) langsung ke physical controller (tidak ada delay).
  - Isolasi *rumble* game secara mutlak: Getaran 100% dipasok murni oleh input *throttle/brake* Anda; mengabaikan getaran ABS atau tabrakan tidak wajar dari game aslinya.
  - Opsi *Motor Selection* (High/Low freq) independen per axis (RT/LT) dengan penanganan limit intensitas prioritas tinggi (`MAX()`) saat terjadi irisan mapping.
  - Dua mode profil getar: `Proportional` (getaran dinamis melandai menyesuaikan seberapa dalam *trigger* ditekan) dan `Static` (getaran konstan dengan intensitas statis seketika sesudah titik *threshold* terlewati).
- **System Diagnostics & Quality of Life**:
  - Auto-check ketersediaan *update* terbaru secara *non-blocking* di latar belakang dan panel manual di jendela About.
  - Arsitektur Singleton untuk *About Window* guna menghindari replikasi/pembukaan banyak jendela (anti tumpuk) dari System Tray.
  - *Comprehensive Session Logging* tersentralisasi harian di `%AppData%\HallConfig\logs\` beserta penangkapan aplikasi error tak terduga, konektivitas perangkat, dan laju pipeline.

---
### v1.2.0
- **Full Gamepad Input Passthrough**:
  - Passthrough instan (zero latency) untuk semua tombol fisik (`A`, `B`, `X`, `Y`, `LB`, `RB`, `Start`, `Back`, `LS click`, `RS click`), `D-Pad`, dan `Right Stick (RX/RY)` langsung ke controller virtual.
  - Tetap mempertahankan filter Smoothing & Hysteresis pada 4 axis utama (`RT`, `LT`, `LX`, `LY`).
  - Optimasi 1-syscall XInput polling & single-report atomic submit pada pipeline 250 Hz tanpa overhead CPU.
  - Kompatibel penuh pada mode **Virtual Xbox 360 (ViGEm)** dan **vJoy**.
- **Branding & Visual Refresh**:
  - Penambahan icon aplikasi resmi (`icon.ico` & `icon.png`) pada window title bar, Windows taskbar, Alt-Tab switcher, dan installer Inno Setup.
  - Desain About Window baru yang lebih minimalis & bersih dengan logo visual, tagline *"Smoother Inputs. Cleaner Trailbraking."*, dan link repository GitHub.
- **Open Source Licensing**:
  - Penambahan file lisensi resmi **MIT License** (2026).

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
