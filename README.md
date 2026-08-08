# HallConfig

[ID](/README-ID.md) | EN

**Smoother Inputs. Cleaner Trailbraking.**

A Windows desktop app that smooths out gamepad trigger/stick signals from hall-effect controllers that stutter/jitter at low input ranges (a common hardware deadzone issue) — before passing them to your game. Built specifically for clean trailbraking in sim racing (Assetto Corsa).

![ss](assets/banner.webp)

## What Is This?

Some hall-effect trigger controllers have a "blind"/unstable zone at low pressure ranges (e.g. 0–20%), causing input to blip/stutter during trailbraking. HallConfig reads that raw signal, cleans it up, and sends the result to your game through a virtual controller — no hardware replacement needed.

## How It Works (Brief)

```
Physical controller (XInput)
      ↓
[Read raw trigger/stick]
      ↓
[Signal Processor: Smoothing + Hysteresis Deadzone]
      ↓
[Output to virtual controller: vJoy or Xbox 360 (ViGEm)]
      ↓
Game (Assetto Corsa, etc.)
```

Buttons, D-pad, and Right Stick are passed through directly (unprocessed) — signal processing only applies to the axes that need it (triggers & left stick).

## Key Features

- **Dual output mode** — output as a vJoy device or a virtual Xbox 360 controller (ViGEm). Xbox 360 is recommended, as the sensitivity curve games read from it tends to feel more natural than vJoy's.
- **Per-axis independent config** — Right Trigger, Left Trigger, Left Stick X/Y each have their own smoothing and hysteresis settings.
- **Live monitoring** — real-time raw vs. output graph right in the app while you tune.
- **Save/Load presets** — save your configuration to a file, load it back anytime.

## Setup

### 1. Install an output driver (pick one, matching the mode you'll use)

- **Xbox 360 (ViGEm)** — *recommended*. Install **ViGEmBus** from [github.com/ViGEm/ViGEmBus/releases](https://github.com/ViGEm/ViGEmBus/releases).
- **vJoy** — install from [sourceforge.net/projects/vjoystick](https://sourceforge.net/projects/vjoystick/), then open `vJoyConf` and make sure at least 1 device is enabled.

### 2. Install HallConfig

Download the latest installer from [Releases](https://github.com/yeftakun/hall-config/releases), run it, and follow the wizard.

### 3. (Optional, recommended) Install HidHide

So your physical controller doesn't also get read by the game alongside the virtual one — prevents binding the wrong device by mistake.

1. Download from [github.com/nefarius/HidHide/releases](https://github.com/nefarius/HidHide/releases), install it.
2. Open **HidHide Configuration Client**:
   - **Applications** tab → add `HallConfig.App.exe`.
   - **Devices** tab → check your physical controller, then check **Enable device hiding** at the bottom.
3. Unplug and replug your physical controller for the change to take effect.

## How to Use

Open HallConfig, pick the **Axis Source** (RT/LT/LX/LY) you want to tune, then adjust:

| Part | About |
|---|---|
| **Smoothing (α / alpha)** | Dampens noise in the raw signal via a moving average. Higher alpha = more responsive but less smooth; lower alpha = smoother but with slightly more delay. |
| **Hysteresis** | A deadzone mechanism with two separate thresholds for rising (`Threshold Up`) and falling (`Threshold Down`) — prevents the signal from "blipping" back and forth when it hovers right around the deadzone point. |
| **Threshold Up** | The raw value must cross this before output starts being considered "active" (above 0). |
| **Threshold Down** | The raw value must drop below this for output to return to 0. Always lower than Threshold Up. |
| **Output Mode** | Where the processed signal goes: **Xbox 360 (ViGEm)** or **vJoy**. |
| **Start/Stop Pipeline** | Turns the read-process-write loop on/off. The virtual controller only exists while the pipeline is running. |
| **Rate** | Processing loop speed (target ~250Hz) — a performance health indicator, not a UI render rate. |

Smoothing and hysteresis can each be toggled independently per axis, so you can A/B test the feel directly in-game.

## Requirements

Windows 10/11, an XInput-compatible controller (Xbox 360/One/generic).

## Known Limitation

Without HidHide, both the physical and virtual controllers get read by the game at the same time (they don't conflict, but you can accidentally bind the wrong one) — see the [Setup](#2-optional-recommended-install-hidhide) section above.

## License

MIT — see [LICENSE](LICENSE).

---

*made by Yefta Asyel*