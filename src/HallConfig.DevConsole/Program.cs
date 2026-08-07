using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using HallConfig.Core;
using HallConfig.Core.Models;
using Windows.Gaming.Input;

namespace HallConfig.DevConsole;

internal class Program
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            Console.Title = "HallConfig - Dev Console (Fase 2 End-to-End)";
        }
        catch { }

        if (args.Length > 0 && args[0].Equals("--vjoy", StringComparison.OrdinalIgnoreCase))
        {
            RunVJoyOutputTest();
            return;
        }

        if (args.Length > 0 && args[0].Equals("--raw", StringComparison.OrdinalIgnoreCase))
        {
            RunGamepadRawTester();
            return;
        }

        if (args.Length > 0 && args[0].Equals("--diag", StringComparison.OrdinalIgnoreCase))
        {
            RunDiagnosticTest();
            return;
        }

        if (args.Length > 0 && args[0].Equals("--xinput", StringComparison.OrdinalIgnoreCase))
        {
            RunXInputMonitor();
            return;
        }

        // Default or --run mode: End-to-End Integration loop
        RunEndToEndPipeline();
    }

    private static void RunEndToEndPipeline()
    {
        var configStore = new ConfigStore();
        var config = configStore.Load();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine("        HALLCONFIG - END-TO-END REALTIME PIPELINE (FASE 2 INTEGRASI)            ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();
        Console.WriteLine($"Config Path    : {configStore.FilePath}");
        Console.WriteLine($"Trigger Source : Gamepad #{config.DeviceIndex} -> {config.AxisSource}");
        Console.WriteLine($"vJoy Output    : Device #{config.VJoyDeviceId} -> Axis {config.VJoyAxis}");
        Console.WriteLine($"Polling Rate   : {config.PollingHz} Hz ({1000.0 / config.PollingHz:F1} ms)");
        Console.WriteLine("--------------------------------------------------------------------------------");
        Console.WriteLine("Hotkeys Real-time:");
        Console.WriteLine("  [S] Toggle Smoothing       | [H] Toggle Hysteresis      | [T] Switch RT/LT");
        Console.WriteLine("  [+] / [-] Alpha (+-0.05)   | [U] / [J] ThreshUp (+-0.01)| [D] / [C] ThreshDown (+-0.01)");
        Console.WriteLine("  [W] Simpan ke config.json  | [Q] / [Esc] Berhenti & Keluar");
        Console.WriteLine("================================================================================\n");

        using var engine = new PipelineEngine(config);

        // Enable file-based debug log (never writes to Console from worker thread)
        engine.EnableDebugLog();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"Debug log: {engine.DebugLogPath}");
        Console.ResetColor();

        // StatusMessage is only fired from the main Start() path now (not WorkerLoop)
        engine.StatusMessage += (msg) =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(msg);
            Console.ResetColor();
        };

        if (!engine.Output.IsVJoyEnabled())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] Driver vJoy tidak aktif atau belum terinstall di Windows!");
            Console.ResetColor();
            Console.WriteLine("Pastikan driver vJoy sudah terinstall dan minimal Device #1 aktif via vJoyConf.");
            return;
        }

        Console.WriteLine($"Acquiring vJoy Device #{config.VJoyDeviceId} (Axis {config.VJoyAxis})...");
        if (!engine.Output.Acquire())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Gagal acquire vJoy Device #{config.VJoyDeviceId}.");
            Console.ResetColor();
            Console.WriteLine("Pastikan vJoy Device telah dikonfigurasi di vJoyConf dan tidak di-lock aplikasi lain.");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] vJoy Device #{config.VJoyDeviceId} acquired! (Min={engine.Output.AxisMin}, Max={engine.Output.AxisMax})");
        Console.ResetColor();

        Console.WriteLine("Memulai pipeline polling loop 250Hz...\n");

        AxisSample latestSample = default;
        long sampleCount = 0;
        var fpsSw = Stopwatch.StartNew();
        int lastFps = 0;
        int frameCounter = 0;

        engine.SampleProcessed += (sample) =>
        {
            latestSample = sample;
            Interlocked.Increment(ref sampleCount);
            frameCounter++;
            if (fpsSw.ElapsedMilliseconds >= 1000)
            {
                lastFps = frameCounter;
                frameCounter = 0;
                fpsSw.Restart();
            }
        };

        engine.Start();

        bool running = true;
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            running = false;
        };

        bool isInteractive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
        string feedbackMsg = "";
        DateTime feedbackExpiry = DateTime.MinValue;

        while (running)
        {
            // Process hotkeys
            if (!Console.IsInputRedirected)
            {
                try
                {
                    while (Console.KeyAvailable)
                    {
                        var keyInfo = Console.ReadKey(intercept: true);
                        var key = keyInfo.Key;
                        char ch = keyInfo.KeyChar;

                        if (key == ConsoleKey.Q || key == ConsoleKey.Escape)
                        {
                            running = false;
                            break;
                        }
                        else if (key == ConsoleKey.S)
                        {
                            config.Smoothing.Enabled = !config.Smoothing.Enabled;
                            engine.UpdateConfig(config);
                            feedbackMsg = $"Smoothing: {(config.Smoothing.Enabled ? "ON" : "OFF")}";
                            feedbackExpiry = DateTime.UtcNow.AddSeconds(2);
                        }
                        else if (key == ConsoleKey.H)
                        {
                            config.Hysteresis.Enabled = !config.Hysteresis.Enabled;
                            engine.UpdateConfig(config);
                            feedbackMsg = $"Hysteresis: {(config.Hysteresis.Enabled ? "ON" : "OFF")}";
                            feedbackExpiry = DateTime.UtcNow.AddSeconds(2);
                        }
                        else if (ch == '+' || ch == '=' || key == ConsoleKey.Add)
                        {
                            config.Smoothing.Alpha = Math.Clamp(config.Smoothing.Alpha + 0.05f, 0.05f, 1.0f);
                            engine.UpdateConfig(config);
                            feedbackMsg = $"Alpha: {config.Smoothing.Alpha:F2}";
                            feedbackExpiry = DateTime.UtcNow.AddSeconds(2);
                        }
                        else if (ch == '-' || ch == '_' || key == ConsoleKey.Subtract)
                        {
                            config.Smoothing.Alpha = Math.Clamp(config.Smoothing.Alpha - 0.05f, 0.05f, 1.0f);
                            engine.UpdateConfig(config);
                            feedbackMsg = $"Alpha: {config.Smoothing.Alpha:F2}";
                            feedbackExpiry = DateTime.UtcNow.AddSeconds(2);
                        }
                        else if (key == ConsoleKey.U)
                        {
                            config.Hysteresis.ThresholdUp = Math.Clamp(config.Hysteresis.ThresholdUp + 0.01f, config.Hysteresis.ThresholdDown + 0.01f, 0.99f);
                            engine.UpdateConfig(config);
                            feedbackMsg = $"ThresholdUp: {config.Hysteresis.ThresholdUp:F2}";
                            feedbackExpiry = DateTime.UtcNow.AddSeconds(2);
                        }
                        else if (key == ConsoleKey.J)
                        {
                            config.Hysteresis.ThresholdUp = Math.Clamp(config.Hysteresis.ThresholdUp - 0.01f, config.Hysteresis.ThresholdDown + 0.01f, 0.99f);
                            engine.UpdateConfig(config);
                            feedbackMsg = $"ThresholdUp: {config.Hysteresis.ThresholdUp:F2}";
                            feedbackExpiry = DateTime.UtcNow.AddSeconds(2);
                        }
                        else if (key == ConsoleKey.D)
                        {
                            config.Hysteresis.ThresholdDown = Math.Clamp(config.Hysteresis.ThresholdDown + 0.01f, 0.01f, config.Hysteresis.ThresholdUp - 0.01f);
                            engine.UpdateConfig(config);
                            feedbackMsg = $"ThresholdDown: {config.Hysteresis.ThresholdDown:F2}";
                            feedbackExpiry = DateTime.UtcNow.AddSeconds(2);
                        }
                        else if (key == ConsoleKey.C)
                        {
                            config.Hysteresis.ThresholdDown = Math.Clamp(config.Hysteresis.ThresholdDown - 0.01f, 0.01f, config.Hysteresis.ThresholdUp - 0.01f);
                            engine.UpdateConfig(config);
                            feedbackMsg = $"ThresholdDown: {config.Hysteresis.ThresholdDown:F2}";
                            feedbackExpiry = DateTime.UtcNow.AddSeconds(2);
                        }
                        else if (key == ConsoleKey.T)
                        {
                            config.AxisSource = config.AxisSource switch
                            {
                                "RightTrigger" => "LeftTrigger",
                                "LeftTrigger" => "LeftStickX",
                                "LeftStickX" => "LeftStickY",
                                "LeftStickY" => "RightTrigger",
                                _ => "RightTrigger"
                            };
                            engine.UpdateConfig(config);
                            feedbackMsg = $"Source: {config.AxisSource}";
                            feedbackExpiry = DateTime.UtcNow.AddSeconds(2);
                        }
                        else if (key == ConsoleKey.W)
                        {
                            configStore.Save(config);
                            feedbackMsg = "Konfigurasi BERHASIL DISIMPAN ke config.json!";
                            feedbackExpiry = DateTime.UtcNow.AddSeconds(3);
                        }
                    }
                }
                catch { }
            }

            if (!running) break;

            float raw = latestSample.RawValue;
            float processed = latestSample.ProcessedValue;
            bool active = latestSample.IsActive;

            string rawBar = CreateAsciiBar(raw, 18);
            string processedBar = CreateAsciiBar(processed, 18);

            string statusTag = !active && config.Hysteresis.Enabled
                ? "[DEADZONE]"
                : (processed > 0f ? "[ACTIVE]  " : "[IDLE]    ");

            string msgDisplay = DateTime.UtcNow < feedbackExpiry ? feedbackMsg : "";

            bool gpConnected = engine.Reader.IsGamepadConnected;
            string gpTag = gpConnected ? $"[PAD#{config.DeviceIndex}: OK]" : $"[PAD#{config.DeviceIndex}: DISCONNECTED]";

            if (isInteractive)
            {
                var sb = new StringBuilder();
                sb.Append($"\r{gpTag} [SMOOTH: {(config.Smoothing.Enabled ? "ON" : "OFF")} α={config.Smoothing.Alpha:F2}] ");
                sb.Append($"[HYST: {(config.Hysteresis.Enabled ? "ON" : "OFF")} Up={config.Hysteresis.ThresholdUp:F2} Dn={config.Hysteresis.ThresholdDown:F2}] ");
                sb.Append($"[RATE: {lastFps,3}Hz] ");
                sb.Append($"\n\rRAW: {raw:F3} ({raw * 100,5:F1}%) [{rawBar}] -> OUT: {processed:F3} ({processed * 100,5:F1}%) [{processedBar}] {statusTag} {msgDisplay,-30}");

                try
                {
                    Console.Write(sb.ToString());
                    Console.SetCursorPosition(0, Math.Max(0, Console.CursorTop - 1));
                }
                catch
                {
                    Console.WriteLine(sb.ToString());
                }
            }
            else
            {
                Console.WriteLine($"{gpTag} RAW: {raw:F3} ({raw * 100,5:F1}%) [{rawBar}] -> OUT: {processed:F3} ({processed * 100,5:F1}%) [{processedBar}] {statusTag}");
            }

            Thread.Sleep(25);
        }

        engine.Stop();
        Console.WriteLine("\n\nPipeline dihentikan. vJoy device direlease. Selesai.");
    }

    private static string CreateAsciiBar(float value, int length = 20)
    {
        float clamped = Math.Clamp(value, 0f, 1f);
        int filled = (int)Math.Round(clamped * length);
        return new string('#', filled).PadRight(length, '-');
    }

    private static void RunGamepadRawTester()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================================");
        Console.WriteLine("        HALLCONFIG - GAMEPAD TRIGGER RAW TESTER (FASE 0)         ");
        Console.WriteLine("=================================================================");
        Console.ResetColor();
        Console.WriteLine("Mendeteksi gamepad via Windows.Gaming.Input...");
        Console.WriteLine("Tekan [Q] atau [Esc] untuk kembali.\n");

        using var reader = new InputReader();
        bool running = true;
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; running = false; };

        while (running)
        {
            if (!Console.IsInputRedirected && Console.KeyAvailable)
            {
                var k = Console.ReadKey(intercept: true).Key;
                if (k == ConsoleKey.Q || k == ConsoleKey.Escape) break;
            }

            float lt = reader.ReadRawTrigger(0, "LeftTrigger");
            float rt = reader.ReadRawTrigger(0, "RightTrigger");

            string ltBar = CreateAsciiBar(lt, 15);
            string rtBar = CreateAsciiBar(rt, 15);

            Console.Write($"\r[LT: {lt:F3} ({lt * 100,5:F1}%) [{ltBar}]]  |  [RT: {rt:F3} ({rt * 100,5:F1}%) [{rtBar}]]   ");
            Thread.Sleep(25);
        }

        Console.WriteLine("\nSelesai.");
    }

    private static void RunXInputMonitor()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================================");
        Console.WriteLine("            HALLCONFIG - XINPUT LIVE MONITOR                      ");
        Console.WriteLine("==================================================================");
        Console.ResetColor();
        Console.WriteLine("Mencari controller XInput (slot 0-3)...");
        Console.WriteLine("Tekan [Q] atau [Esc] untuk keluar.\n");

        // Scan semua 4 slot XInput
        int slot = -1;
        for (int i = 0; i < XInputHelper.MaxControllers; i++)
        {
            if (XInputHelper.GetState(i, out _))
            {
                slot = i;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Controller ditemukan di slot XInput #{i}");
                Console.ResetColor();
                break;
            }
        }

        if (slot < 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Tidak ada controller XInput terdeteksi di slot 0-3.");
            Console.ResetColor();
            Console.WriteLine("Pastikan controller tersambung via USB atau receiver wireless.");
            return;
        }

        Console.WriteLine($"Monitoring slot #{slot}. Gerakkan LT/RT/Left Stick sekarang:\n");

        bool running = true;
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; running = false; };

        while (running)
        {
            if (!Console.IsInputRedirected && Console.KeyAvailable)
            {
                var k = Console.ReadKey(intercept: true).Key;
                if (k == ConsoleKey.Q || k == ConsoleKey.Escape) break;
            }

            if (!XInputHelper.GetState(slot, out var state))
            {
                Console.Write($"\r[XInput Slot #{slot}] DISCONNECTED                                              ");
                Thread.Sleep(200);
                continue;
            }

            float lt  = state.Gamepad.LeftTrigger  / 255f;
            float rt  = state.Gamepad.RightTrigger / 255f;
            float lx  = Math.Clamp(state.Gamepad.ThumbLX / 32767f, -1f, 1f);
            float ly  = Math.Clamp(state.Gamepad.ThumbLY / 32767f, -1f, 1f);

            string ltBar = CreateAsciiBar(lt, 14);
            string rtBar = CreateAsciiBar(rt, 14);
            // LX centered bar
            int lxFilled = (int)Math.Round((lx + 1f) / 2f * 14);
            string lxBar = ("<" + new string('-', lxFilled).PadRight(14, '-') + ">").Substring(0, 16);

            Console.Write(
                $"\rLT={lt:F3} [{ltBar}]  " +
                $"RT={rt:F3} [{rtBar}]  " +
                $"LX={lx:+0.000;-0.000} LY={ly:+0.000;-0.000}  " +
                $"Btn={state.Gamepad.Buttons:X4}   "
            );

            Thread.Sleep(16); // ~60Hz display
        }

        Console.WriteLine("\nXInput monitor selesai.");
    }

    private static void RunDiagnosticTest()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================================");
        Console.WriteLine("             HALLCONFIG - GAMEPAD DIAGNOSTIC TESTER              ");
        Console.WriteLine("=================================================================");
        Console.ResetColor();
        Console.WriteLine($"Main Thread: ID={Environment.CurrentManagedThreadId}, Apartment={Thread.CurrentThread.GetApartmentState()}");
        Console.WriteLine();

        // --- Step 0: Wait 500ms for WinRT to enumerate devices ---
        Thread.Sleep(500);

        // --- Step 1: Enumerate all detected devices ---
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("=== Detected Devices ===");
        Console.ResetColor();

        try
        {
            var rawControllers = Windows.Gaming.Input.RawGameController.RawGameControllers;
            Console.WriteLine($"RawGameController.Count = {rawControllers.Count}");
            for (int i = 0; i < rawControllers.Count; i++)
            {
                var rc = rawControllers[i];
                Console.WriteLine($"  Raw[{i}]: Name='{rc.DisplayName}' VendorId={rc.HardwareVendorId:X4} ProductId={rc.HardwareProductId:X4} AxisCount={rc.AxisCount} ButtonCount={rc.ButtonCount}");
            }
        }
        catch (Exception ex) { Console.WriteLine($"  RawGameController query FAILED: {ex.Message}"); }

        try
        {
            var gamepads = Gamepad.Gamepads;
            Console.WriteLine($"Gamepad.Gamepads.Count = {gamepads.Count}");
            for (int i = 0; i < gamepads.Count; i++)
            {
                var r = gamepads[i].GetCurrentReading();
                Console.WriteLine($"  Gamepad[{i}]: LT={r.LeftTrigger:F3} RT={r.RightTrigger:F3} LX={r.LeftThumbstickX:F3} LY={r.LeftThumbstickY:F3} Buttons={r.Buttons}");
            }
        }
        catch (Exception ex) { Console.WriteLine($"  Gamepad.Gamepads query FAILED: {ex.Message}"); }

        Console.WriteLine();

        // --- Step 2: Live interactive reading via Gamepad API (MAIN THREAD) ---
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("=== Step 2: Live Gamepad.GetCurrentReading() dari MAIN THREAD (10 detik) ===");
        Console.ResetColor();
        Console.WriteLine(">> TEKAN LT/RT fisik sekarang! <<");
        Console.WriteLine("   Jika nilai berubah = Gamepad API OK dari main thread");
        Console.WriteLine("   Jika tetap 0 = ada masalah thread/focus/device\n");

        {
            Gamepad? gp = Gamepad.Gamepads.Count > 0 ? Gamepad.Gamepads[0] : null;
            if (gp == null) { Console.WriteLine("  SKIP: Tidak ada Gamepad terdeteksi di main thread."); }
            else
            {
                double ltMax = 0, rtMax = 0;
                for (int i = 0; i < 400; i++) // 400 * 25ms = 10s
                {
                    var r = gp.GetCurrentReading();
                    if (r.LeftTrigger > ltMax) ltMax = r.LeftTrigger;
                    if (r.RightTrigger > rtMax) rtMax = r.RightTrigger;
                    string ltBar = CreateAsciiBar((float)r.LeftTrigger, 12);
                    string rtBar = CreateAsciiBar((float)r.RightTrigger, 12);
                    Console.Write($"\r[MAIN] LT={r.LeftTrigger:F3} [{ltBar}] RT={r.RightTrigger:F3} [{rtBar}] Btn={r.Buttons}   ");
                    Thread.Sleep(25);
                }
                Console.WriteLine($"\n  RESULT: LT_max={ltMax:F3}, RT_max={rtMax:F3}");
                if (ltMax > 0.01 || rtMax > 0.01)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  >> Gamepad API BERFUNGSI dari main thread!");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  >> Gamepad API TIDAK BERFUNGSI dari main thread (nilai selalu 0)");
                }
                Console.ResetColor();
            }
        }

        Console.WriteLine();

        // --- Step 3: Live interactive reading via RawGameController API (MAIN THREAD) ---
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("=== Step 3: Live RawGameController.GetCurrentReading() dari MAIN THREAD (5 detik) ===");
        Console.ResetColor();
        Console.WriteLine(">> TEKAN LT/RT fisik sekarang! <<\n");

        try
        {
            // Skip vJoy (VendorId=1234)
            Windows.Gaming.Input.RawGameController? rawGp = null;
            foreach (var rc in Windows.Gaming.Input.RawGameController.RawGameControllers)
            {
                if (rc.HardwareVendorId != 0x1234) { rawGp = rc; break; }
            }

            if (rawGp == null) { Console.WriteLine("  SKIP: Tidak ada RawGameController non-vJoy terdeteksi."); }
            else
            {
                Console.WriteLine($"  Menggunakan: '{rawGp.DisplayName}' VendorId={rawGp.HardwareVendorId:X4}");
                double[] axes = new double[rawGp.AxisCount];
                bool[] buttons = new bool[rawGp.ButtonCount];
                GameControllerSwitchPosition[] switches = new GameControllerSwitchPosition[rawGp.SwitchCount];

                double[] axisMax = new double[rawGp.AxisCount];

                for (int i = 0; i < 200; i++) // 200 * 25ms = 5s
                {
                    rawGp.GetCurrentReading(buttons, switches, axes);
                    for (int a = 0; a < axes.Length; a++)
                        if (axes[a] > axisMax[a]) axisMax[a] = axes[a];

                    // Show first 6 axes max
                    var sb = new StringBuilder("\r[RAW] Axes: ");
                    for (int a = 0; a < Math.Min(axes.Length, 8); a++)
                        sb.Append($"[{a}]={axes[a]:F2} ");
                    Console.Write(sb.ToString().PadRight(80));
                    Thread.Sleep(25);
                }

                Console.WriteLine("\n  AXIS MAX VALUES:");
                for (int a = 0; a < axisMax.Length; a++)
                    Console.WriteLine($"    Axis[{a}] max = {axisMax[a]:F4}");
            }
        }
        catch (Exception ex) { Console.WriteLine($"  RawGameController reading FAILED: {ex.Message}"); }

        Console.WriteLine();

        // --- Step 4: Worker thread reading check ---
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("=== Step 4: GetCurrentReading() dari WORKER THREAD (5 detik) ===");
        Console.ResetColor();
        Console.WriteLine(">> TEKAN LT/RT fisik sekarang! <<\n");

        {
            double bgLtMax = 0, bgRtMax = 0;
            bool bgRunning = true;
            var bgThread = new Thread(() =>
            {
                Thread.Sleep(200);
                Gamepad? gp = null;
                try { gp = Gamepad.Gamepads.Count > 0 ? Gamepad.Gamepads[0] : null; }
                catch { }
                if (gp == null) { Console.WriteLine("  Worker: Tidak ada Gamepad."); return; }
                Console.WriteLine($"  Worker: Gamepad ditemukan, mulai membaca...");
                while (bgRunning)
                {
                    try
                    {
                        var r = gp.GetCurrentReading();
                        if (r.LeftTrigger > bgLtMax) bgLtMax = r.LeftTrigger;
                        if (r.RightTrigger > bgRtMax) bgRtMax = r.RightTrigger;
                    }
                    catch { }
                    Thread.Sleep(4);
                }
            }) { IsBackground = true };
            bgThread.Start();
            Thread.Sleep(5000);
            bgRunning = false;
            bgThread.Join(1000);
            Console.WriteLine($"  Worker thread result: LT_max={bgLtMax:F3}, RT_max={bgRtMax:F3}");
            if (bgLtMax > 0.01 || bgRtMax > 0.01)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  >> Worker thread reading BERFUNGSI!");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  >> Worker thread reading TIDAK BERFUNGSI (nilai selalu 0)");
            }
            Console.ResetColor();
        }

        Console.WriteLine("\n[DIAG] Diagnosa selesai.");
    }


    private static void RunVJoyOutputTest()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("=================================================================");
        Console.WriteLine("        HALLCONFIG - VJOY OUTPUT GENERATOR TESTER (FASE 1)       ");
        Console.WriteLine("=================================================================");
        Console.ResetColor();

        uint deviceId = 1;
        var axis = VJoyAxisType.X;

        using var vjoy = new VJoyOutput(deviceId, axis);

        if (!vjoy.IsVJoyEnabled())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] Driver vJoy tidak aktif atau belum terinstall di Windows!");
            Console.ResetColor();
            Console.WriteLine("Pastikan driver vJoy sudah terinstall dan aktif.");
            return;
        }

        Console.WriteLine($"Mencoba acquire vJoy Device #{deviceId} (Axis {axis})...");
        if (!vjoy.Acquire())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Gagal acquire vJoy Device #{deviceId}.");
            Console.ResetColor();
            Console.WriteLine("Pastikan vJoy Device #1 telah dikonfigurasi di vJoyConf dan tidak di-lock aplikasi lain.");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] vJoy Device #{deviceId} berhasil di-acquire!");
        Console.WriteLine($"Rentang Axis {axis}: Min={vjoy.AxisMin}, Max={vjoy.AxisMax}");
        Console.ResetColor();
        Console.WriteLine("\nMengirimkan sinyal uji smooth sine-sweep (0.0 -> 1.0 -> 0.0) ke vJoy Axis X...");
        Console.WriteLine("Buka 'vJoy Monitor' atau 'joy.cpl' di Windows untuk melihat pergerakan axis secara live!");
        Console.WriteLine("Tekan [Q] atau [Esc] atau [Ctrl+C] untuk berhenti.\n");

        bool running = true;
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            running = false;
        };

        float phase = 0f;
        while (running)
        {
            if (!Console.IsInputRedirected)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true).Key;
                        if (key == ConsoleKey.Q || key == ConsoleKey.Escape)
                        {
                            running = false;
                            break;
                        }
                    }
                }
                catch { }
            }

            float rawWave = (MathF.Sin(phase) + 1f) / 2f;
            vjoy.SetValue(rawWave);

            string bar = CreateAsciiBar(rawWave, 30);
            Console.Write($"\r[vJoy Output Dev #{deviceId} Axis {axis}] Nilai: {rawWave,6:F3} ({rawWave * 100,5:F1}%) [{bar}]   ");

            phase += 0.04f;
            if (phase > MathF.PI * 2) phase -= MathF.PI * 2;

            Thread.Sleep(20);
        }

        vjoy.SetValue(0f);
        Console.WriteLine("\n\nSelesai. vJoy Device direlease.");
    }
}
