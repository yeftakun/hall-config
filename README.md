# HallConfig 🏎️⚡

A lightweight, ultra low-latency (~250 Hz) signal conditioning utility for Windows designed specifically for **Hall-Effect gamepad triggers and thumbsticks**. It eliminates the common 0–20% hardware deadzone flutter and signal blips before routing clean virtual joystick inputs to **vJoy** and racing simulators like **Assetto Corsa**.

---

## 🚀 Key Features

- **250 Hz Precision Pipeline**: Real-time XInput polling and processing with minimal CPU overhead (~0.1%).
- **Dual Signal Filter Engine**:
  - **EMA Smoothing**: Exponential Moving Average filter to eliminate micro-jitter.
  - **Schmitt-Trigger Hysteresis**: True threshold-up / threshold-down hysteresis cutoff that completely silences rest-position sensor noise.
- **Simultaneous 4-Axis vJoy Routing**:
  - 🎮 **Axle 1 (`X`)**: Joystick L Horizontal *(Center neutral 50%)*
  - 🎮 **Axle 2 (`Y`)**: Right Trigger / Throttle *(0% – 100%)*
  - 🎮 **Axle 3 (`Z`)**: Left Trigger / Brake *(0% – 100%)*
  - 🎮 **Axle 4 (`Rx`)**: Joystick L Vertical *(Center neutral 50%)*
- **Per-Axis Independent Tuning**: Tune Alpha, Threshold Up, and Threshold Down per individual axis with live visual feedback.
- **Interactive Modern Dark UI**: Dynamic visual meters for both RAW and OUTPUT signals for all 4 axes simultaneously.
- **Production Polish**:
  - System Tray integration (minimize-to-tray & close-to-tray).
  - Windows Run on Startup (`--minimized`) support with instant auto-start.
  - Standalone Single-File self-contained executable.

---

## 📦 Requirements & Installation

1. **Windows 10 / 11 (64-bit)**
2. **vJoy Virtual Joystick Driver (v2.1.9+)**:
   - Download and install vJoy from [SourceForge](https://sourceforge.net/projects/vjoysoft/) or [GitHub vJoy releases](https://github.com/shauleiz/vJoy/releases).
   - Ensure **vJoy Device #1** is enabled in `vJoyConf.exe` with Axes: `X`, `Y`, `Z`, `Rx`.
3. **Download HallConfig**:
   - Run `publish\HallConfig.App.exe` directly (no .NET runtime installation required).

---

## 🎮 Setup Guide for Assetto Corsa & Sim Racing

### 1. Configure HallConfig
1. Open **HallConfig.App.exe**.
2. Click **Start Pipeline**.
3. Select an axis (e.g. **Right Trigger** or **Left Trigger**) to adjust smoothing and hysteresis sliders as needed.
4. Click **Save Config** to persist your profile to `%LocalAppData%\HallConfig\config.json`.

### 2. Bind Controls in Assetto Corsa (or Content Manager)
1. Open **Assetto Corsa** ➔ **Options** ➔ **Controls** (or Content Manager ➔ **Settings** ➔ **Assetto Corsa** ➔ **Controls**).
2. Set input type to **Wheel / Custom** (or DirectInput Controllers).
3. Assign the axes:
   - **Steering**: Move Left Stick horizontally ➔ Assigns to `vJoy Device - Axle 1`
   - **Throttle**: Press Right Trigger ➔ Assigns to `vJoy Device - Axle 2`
   - **Brakes**: Press Left Trigger firmly & quickly ➔ Assigns to `vJoy Device - Axle 3`

### 3. Best Practice: Controller Isolation with HidHide (Recommended)
Because Windows detects both your physical Xbox gamepad and the virtual vJoy Device, some games might bind the physical controller instead.
To completely prevent double-input:
1. Download **[Nefarius HidHide](https://github.com/nefarius/HidHide)**.
2. In the **Applications** tab of HidHide, add `HallConfig.App.exe` to the whitelist.
3. In the **Devices** tab, hide your physical controller from other games.
4. Now games will **only see vJoy Device**, giving you 100% clean, flutter-free control!

---

## 🛠️ Building & Publishing from Source

### Prerequisites:
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build Solution:
```powershell
dotnet build
```

### Run Unit Tests:
```powershell
dotnet test
```

### Publish Standalone Single-File Release:
```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```
The output executable will be created in `./publish/HallConfig.App.exe`.

---

## 📂 Project Architecture

```
hall-config/
├── libs/x64/                     # Native vJoy C/C++ SDK & managed wrapper
├── src/
│   ├── HallConfig.Core/          # Pure signal pipeline & algorithms (Unit-tested)
│   │   ├── SignalProcessor.cs    # EMA smoothing & Schmitt hysteresis logic
│   │   ├── InputReader.cs        # XInput 1.4 low-latency reader
│   │   ├── VJoyOutput.cs         # vJoy SDK multi-axis dispatcher
│   │   ├── PipelineEngine.cs     # 250Hz dedicated worker thread
│   │   ├── ConfigStore.cs        # JSON settings serializer
│   │   └── Services/StartupHelper.cs
│   ├── HallConfig.App/           # WPF MVVM GUI & System Tray application
│   │   ├── MainWindow.xaml       # Interactive 4-axis dashboard
│   │   ├── Views/AboutWindow.xaml
│   │   └── Services/TrayService.cs
│   └── HallConfig.DevConsole/    # CLI diagnostics & live HUD tool
└── tests/
    └── HallConfig.Core.Tests/    # 14 comprehensive unit tests
```

---

## 📄 License
MIT License. Created with Google DeepMind Antigravity.
