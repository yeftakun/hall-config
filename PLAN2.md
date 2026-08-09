# PLAN2.md — HallConfig v1.3.0

Lanjutan dari PLAN.md (v1.0–v1.2.0). Mencakup: vibration feedback, update checker, bugfix About window singleton, dan logging system.

## 1. Ringkasan Perubahan

| # | Item | Tipe |
|---|---|---|
| 1 | Vibration feedback proporsional ke Processed Output (RT/LT) | Fitur baru |
| 2 | Update checker (manual + auto-check background) | Fitur baru |
| 3 | Bug freeze saat reconnect controller fisik | Watch item (lihat §5) |
| 4 | About window buka dobel dari tray + rename menu item | Bugfix |
| 5 | Logging system | Fitur baru (infrastruktur) |

**Urutan pengerjaan disarankan:** Logging system dulu (§5) → About window fix (§4) → Update checker (§3 lama/§2 baru) → Vibration (§1). Alasan: logging duluan supaya kalau ada masalah di fase-fase berikutnya, sudah ada alat diagnosis; About window fix itu quick win kecil; vibration paling kompleks jadi terakhir.

**Versi:** disarankan bump ke **v1.3.0** setelah semua selesai (banyak fitur baru, bukan sekadar bugfix). Konfirmasi ke user sebelum agent update version string di installer/About window.

---

## 2. Logging System

### Lokasi & Rotasi
- `%AppData%\HallConfig\logs\`
- Satu file per sesi aplikasi: `hallconfig-YYYYMMDD-HHmmss.log`
- Retention: hapus otomatis file lebih dari 14 hari saat startup (cegah folder membengkak).

### Format
Plain text, satu baris per event, gampang di-paste ke GitHub issue:
```
2026-08-09 14:32:10.123 [INFO ] [Pipeline] Started. OutputMode=Xbox360, TargetRate=250Hz
2026-08-09 14:32:12.001 [INFO ] [InputReader] Controller connected. UserIndex=0, Name=Gamesir Nova Lite
2026-08-09 14:35:40.882 [WARN ] [InputReader] Controller disconnected. UserIndex=0
2026-08-09 14:35:41.900 [ERROR] [VJoyOutput] SetAxis failed: <exception message + type>
2026-08-09 14:40:00.000 [INFO ] [RateMonitor] Current rate: 248Hz (target 250Hz)
```

### Yang WAJIB dilog
- App start/stop, versi aplikasi, output mode aktif.
- Pipeline start/stop.
- Controller connect/disconnect (dengan XInput UserIndex).
- SEMUA exception yang ketangkep (reuse try/catch yang sudah ada di WorkerLoop dari v1.1, arahkan ke sini juga, bukan cuma ke debug-log.txt lama).
- Config load/save (path, sukses/gagal).
- Output device Acquire/Release (vJoy & Xbox360), termasuk kegagalan.
- Rate pipeline, dicatat periodik (misal tiap 30 detik), bukan tiap sample.
- Vibration toggle on/off per axis (fitur baru §1).
- Hasil update check (§2 baru).
- **Uncaught exception di level aplikasi** — hook `AppDomain.UnhandledException` dan `Dispatcher.UnhandledException`, log sebelum app crash (ini belum ada sama sekali sejauh ini — penting buat nangkep crash yang bahkan lolos dari try/catch WorkerLoop).

### Yang JANGAN dilog
- Nilai RAW/OUTPUT per-sample di rate 250Hz — bakal banjir dan bikin file log raksasa dalam hitungan menit. Cukup event/transisi state penting.

### Akses dari GUI
Tambahkan tombol **"Open Log Folder"** (taruh di Settings atau About window) yang buka folder log tersebut di Windows Explorer — supaya user gampang nemu & attach ke bug report.

---

## 3. About Window — Singleton + Tray Menu Fix

**Bug 1:** Klik "About" dari system tray selalu buka window baru, meski sudah ada yang terbuka.
**Fix:** Simpan static reference ke instance `AboutWindow` yang lagi aktif. Kalau sudah ada & masih terbuka → `Activate()` + bawa ke depan. Kalau belum ada/sudah ditutup → buat instance baru. Set reference ke `null` di event `Closed`.

**Bug 2:** Teks menu tray "About HallConfig..." → ganti jadi **"About"** saja.

---

## 4. Update Checker

- **Auto-check di background** saat app start — non-blocking, kalau gagal (no internet dll) diamkan saja, jangan ganggu user.
- **Tombol manual "Check for Update"** di GUI (About window) — trigger cek yang sama, tampilkan hasil ("Up to date" / "New version available: vX.X.X" + link ke Releases).
- Sumber data: GitHub Releases API — `https://api.github.com/repos/yeftakun/hall-config/releases/latest`, bandingkan field `tag_name` dengan versi app yang jalan.
- Tidak ada auto-download/install — cuma notifikasi + link buka browser ke halaman Releases.
- Toggle di Settings untuk matiin auto-check kalau user tidak mau (tetap sediakan tombol manual).
- Log hasil cek ke sistem logging §2.

---

## 5. Watch Item: Freeze Saat Reconnect Controller Fisik

Sudah "solved sendiri" via restart laptop (bukan restart app) — ini justru sinyal kalau akar masalahnya di level driver/OS (kemungkinan XInput slot orphaned, atau state ViGEm/HidHide yang nyangkut), bukan di logic aplikasi murni. Karena hard to reproduce, JANGAN coba blind-fix sekarang.

**Yang perlu dilakukan:** pastikan sistem logging §2 mencatat detail cukup di sekitar kejadian ini kalau muncul lagi — connect/disconnect event, exception dari InputReader/VJoyOutput/Xbox360Output, dan state pipeline saat itu. Kalau kejadian lagi, log file jadi bahan diagnosis nyata, bukan cuma dugaan.

---

## 6. Vibration Feedback (RT/LT saja, v1)

### Spesifikasi
- **Scope:** cuma Right Trigger & Left Trigger. Left Stick tidak dapat vibration di versi ini.
- **Toggle independen per axis** (VibrationEnabled untuk RT, terpisah untuk LT).
- **Continuous**, proporsional langsung ke nilai Processed Output (bukan pulse/confirmation).
- **Menggantikan TOTAL rumble asli game** — physical controller TIDAK PERNAH menerima rumble asli dari game. HallConfig satu-satunya sumber getaran ke device fisik.
- **Max Vibration cap** — slider/setting (0–100%) yang membatasi intensitas maksimum, supaya user bisa turunkan kalau kerasa kekencangan di Processed Output 100%.
- Berlaku di KEDUA mode output (vJoy maupun Xbox360) — vibration selalu ditulis ke controller FISIK lewat XInput, independen dari mode output yang dipilih untuk sinyal game.

### Mapping Motor
XInput cuma punya 2 motor: `LeftMotorSpeed` (motor besar/frekuensi rendah) dan `RightMotorSpeed` (motor kecil/frekuensi tinggi).

**User-selectable per axis** — bukan mapping tetap. Tiap axis (RT, LT) punya pilihan sendiri: motor mana yang dia gerakkan — Low Freq (Left Motor) atau High Freq (Right Motor). Default awal: LT → Low Freq, RT → High Freq (tetap disaranin sebagai default, tapi bisa diganti user).

**Resolusi konflik:** kalau RT dan LT kebetulan dipilih ke motor YANG SAMA, nilai motor itu = `MAX(nilai_axis_1, nilai_axis_2)` — bukan dijumlah/dirata-rata, supaya getaran tetap jelas terasa dan tidak "encer".

### Mode Intensitas (per axis)
Selain mode proporsional (default), sediakan mode **Static Intensity**: begitu ProcessedOutput axis itu aktif (di atas 0 / sudah lewat threshold hysteresis), motor langsung ke Max Vibration penuh — TIDAK ikut naik-turun sesuai kedalaman tekan. Berguna buat sinyal "sudah aktif" yang tegas, bukan gradasi halus.

- Pilihan per axis: `Proportional` (default) atau `Static`.
- Rumus mode Static: `motorValue = (ProcessedOutput > 0) ? (MaxVibrationPercent/100 × 65535) : 0`.
- Simpan pilihan mode ini di config.json per axis, sejajar dengan VibrationEnabled dan MotorSelection.

### Implementasi
- Mode Proportional: `ProcessedOutput × (MaxVibrationPercent / 100) × 65535` (batas atas ushort XInput).
- Mode Static: lihat rumus di atas.
- Tulis via `XInputSetState(physicalUserIndex, vibration)` — target device FISIK (bukan device virtual), lewat XInputHelper yang sudah ada.
- **Compatible dengan HidHide** — karena HallConfig.App.exe sudah di-whitelist, tetap bisa baca+tulis device fisik walau disembunyikan dari game lain.
- **Cek capability dulu** — pakai `XInputGetCapabilities` untuk deteksi apakah controller mendukung vibration motor sama sekali (device seperti Gamesir Nova Lite kemungkinan besar support, tapi tetap perlu graceful fallback: kalau tidak didukung, disable toggle-nya di GUI, jangan silent fail).
- **Throttle write rate** — motor fisik tidak bisa merespons secepat 250Hz, jadi batasi panggilan `XInputSetState` ke sekitar 60Hz terpisah dari loop sinyal utama 250Hz, supaya tidak buang-buang call sia-sia.
- Log toggle on/off dan hasil capability check ke sistem logging §2.

---

## 7. Testing Checklist

- [ ] Log file muncul di `%AppData%\HallConfig\logs\`, format sesuai §2, file lama otomatis kehapus setelah 14 hari.
- [ ] Klik About dari tray dua kali berturut-turut → cuma satu window, yang kedua fokus ke window pertama.
- [ ] Tray menu tulisannya "About" (bukan "About HallConfig...").
- [ ] Update checker: tombol manual jalan, auto-check saat start tidak block UI.
- [ ] Vibration RT terasa naik-turun sesuai Processed Output, LT independen bisa dimatikan tanpa pengaruhi RT.
- [ ] Max Vibration slider beneran membatasi intensitas di 100% output.
- [ ] Getaran tetap jalan baik di mode output vJoy maupun Xbox360.
- [ ] Controller fisik TIDAK pernah terasa rumble asli dari game (misal ABS kick-in AC) — cuma getaran dari HallConfig.