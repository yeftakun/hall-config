# PLAN.md - HallConfig

Aplikasi desktop Windows untuk remap/deadzone trigger gamepad hall-effect (khususnya mengatasi glitch input di 0-20%) sebelum diteruskan ke Assetto Corsa via vJoy.

## 1. Tujuan

- Trigger gamepad hall-effect mengalami stutter/blip di rentang input 0-20% (deadzone hardware).
- Aplikasi membaca sinyal trigger mentah, menerapkan pipeline pemrosesan (smoothing opsional  hysteresis deadzone opsional  remap linear 0-100%), lalu menulis hasilnya ke axis vJoy.
- Assetto Corsa membaca axis vJoy tersebut sebagai wheel/pedal input.
- Smoothing dan hysteresis harus bisa di-toggle independen agar user bisa A/B test feel di game.

## 2. Tech Stack

| Layer | Teknologi |
|---|---|
| Bahasa/Runtime | C# / .NET 8 |
| UI | WPF |
| Input gamepad | `Windows.Gaming.Input` (utama) - fallback `SharpDX`/`Vortice.XInput` jika perlu |
| Output virtual joystick | `vJoyInterfaceWrap` (vJoy SDK resmi) |
| Grafik real-time | LiveCharts2 |
| Config persistence | `System.Text.Json`, disimpan di `%AppData%/HallConfig/config.json` |
| Packaging | `dotnet publish` single-file, self-contained |

### Prasyarat sebelum coding
- [ ] Driver **vJoy** sudah terinstall dan minimal 1 device vJoy dikonfigurasi (cek via vJoyConf).
- [ ] .NET 8 SDK terinstall.
- [ ] Identifikasi apakah controller terbaca sebagai **XInput** atau **DirectInput/raw HID** - jalankan `Get-PnpDevice` atau cek di `joy.cpl` / test dengan tool seperti "Gamepad Tester" untuk lihat resolusi trigger yang tersedia. Ini menentukan apakah `Windows.Gaming.Input` cukup atau perlu baca HID mentah.

## 3. Arsitektur / Alur Data

```
[Gamepad raw trigger] 
      3  (poll ~250Hz)
      [InputReader]   raw value (0.0-1.0)
      [SignalProcessor]
   CD Smoothing (EMA, on/off) 
   CD Hysteresis deadzone (schmitt trigger, on/off)
   @D Remap ke 0.0-1.0
      [VJoyOutput]  tulis ke axis vJoy device
      [Assetto Corsa] baca vJoy sebagai wheel/pedal
```

GUI berjalan paralel, subscribe ke stream nilai raw & output untuk live graph, dan mengubah parameter `SignalProcessor` secara real-time.

## 4. Struktur Proyek

```
HallConfig/
CDD HallConfig.sln
CDD src/
3   CDD HallConfig.Core/              # class library, no UI deps - mudah di-unit-test
3   3   CDD InputReader.cs
3   3   CDD SignalProcessor.cs
3   3   CDD VJoyOutput.cs
3   3   CDD Models/
3   3   3   CDD ProcessorConfig.cs
3   3   3   @DD AxisSample.cs
3   3   @DD ConfigStore.cs
3   @DD HallConfig.App/               # WPF project
3       CDD App.xaml
3       CDD MainWindow.xaml(.cs)
3       CDD ViewModels/
3       3   @DD MainViewModel.cs
3       @DD Controls/
3           @DD LiveCurveChart.xaml(.cs)
CDD tests/
3   @DD HallConfig.Core.Tests/
3       CDD SignalProcessorTests.cs
3       @DD ConfigStoreTests.cs
@DD PLAN.md
```

## 5. Model Konfigurasi (`config.json`)

```json
{
  "deviceIndex": 0,
  "axisSource": "RightTrigger",
  "vjoyDeviceId": 1,
  "vjoyAxis": "X",
  "smoothing": {
    "enabled": true,
    "alpha": 0.4
  },
  "hysteresis": {
    "enabled": true,
    "thresholdUp": 0.27,
    "thresholdDown": 0.23
  },
  "pollingHz": 250
}
```

## 6. Algoritma Inti (pseudocode, jadi acuan implementasi `SignalProcessor`)

```csharp
class SignalProcessor
{
    float _smoothed;
    bool _active;

    public float Process(float raw, ProcessorConfig cfg)
    {
        float value = raw;

        if (cfg.Smoothing.Enabled)
        {
            _smoothed = cfg.Smoothing.Alpha * value + (1 - cfg.Smoothing.Alpha) * _smoothed;
            value = _smoothed;
        }

        if (cfg.Hysteresis.Enabled)
        {
            if (value > cfg.Hysteresis.ThresholdUp) _active = true;
            else if (value < cfg.Hysteresis.ThresholdDown) _active = false;

            if (!_active) return 0f;

            value = (value - cfg.Hysteresis.ThresholdDown) / (1 - cfg.Hysteresis.ThresholdDown);
        }

        return Math.Clamp(value, 0f, 1f);
    }
}
```

Catatan: ketika hysteresis dimatikan, tetap perlu deadzone dasar (misal pakai `ThresholdUp` saja sebagai hard cutoff) supaya app tidak mengirim nol mentah tanpa filtering sama sekali - putuskan default behavior ini di Fase 2.

## 7. Fase Pengembangan

### Fase 0 - Setup proyek
- [x] Buat solution + 3 project (`Core`, `App`, `Core.Tests`).
- [x] Install/link: `vJoyInterfaceWrap`, native vJoy DLLs.
- [x] Verifikasi controller detection (XInput 1.4 low-latency reader).

### Fase 1 - Core signal pipeline (tanpa GUI)
- [x] Implement `SignalProcessor` (EMA + Schmitt-Trigger Hysteresis).
- [x] Unit test: smoothing on/off, hysteresis on/off, kombinasi keduanya, edge cases (14/14 tests pass).
- [x] Implement `VJoyOutput` multi-axis (X, Y, Z, Rx).

### Fase 2 - Integrasi end-to-end tanpa GUI
- [x] Loop utama: `InputReader` -> `SignalProcessor` -> `VJoyOutput` pada 250 Hz.
- [x] Load/save `config.json` dari `ConfigStore`.
- [x] Verifikasi HUD DevConsole dan testing live controller.

### Fase 3 - GUI (WPF)
- [x] `MainWindow`: 2x2 interactive axis cards (RT, LT, LX, LY) with live RAW and OUTPUT meters.
- [x] Independent per-axis tuning with instant parameter feedback.
- [x] Save/Load profile settings.
- [x] Start/Stop Pipeline manual control.

### Fase 4 - Polish & packaging
- [x] System tray icon, minimize-to-tray & close-to-tray.
- [x] Opsi run-on-startup (Windows Registry) + `--minimized` auto-start.
- [x] `dotnet publish` single-file self-contained packaging (`publish.ps1`).
- [x] README lengkap: cara install vJoy, panduan setting axis Assetto Corsa & tips HidHide.
- [x] Jendela About modern yang terbuka dari system tray dan header UI.

**Acceptance:** `.exe` jalan di mesin bersih (dengan vJoy terinstall) tanpa perlu install .NET runtime terpisah.

## 8. Open Questions (perlu dijawab sebelum/selagi Fase 0-1)

- Apakah controller terbaca XInput atau perlu raw HID? (menentukan resolusi input yang tersedia)
- Axis vJoy mana yang dipetakan ke trigger ini - apakah perlu 2 axis terpisah (throttle & brake) atau cuma 1 yang sedang bermasalah?
- Default behavior saat hysteresis dimatikan: pakai hard cutoff sederhana, atau benar-benar 0 filtering?

## 9. Di Luar Scope (v1)

- Kurva non-linear/custom response curve (gamma) - AC sudah punya setting ini sendiri, tidak perlu diduplikasi.
- Dukungan multi-controller simultan.
- Cross-platform (Linux/Mac) - vJoy Windows-only, jadi tidak relevan.
