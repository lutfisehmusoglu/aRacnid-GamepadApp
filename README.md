# aRacnid GamepadApp

**aRacnid GamepadApp** is a Windows gamepad remapping and virtual controller application.

It reads supported physical controllers, applies profile-based remapping and analog settings, then exposes the result to games as a virtual **DualShock 4** or **Xbox 360 Controller**.

The application includes controller profiles, button and analog-direction remapping, deadzone and anti-deadzone controls, sensitivity adjustment, vibration control, DS4 lightbar customization, a live gamepad tester, system tray support, and Turkish / English interface support.

---

## Features

- Physical controller → virtual controller conversion
- Virtual DualShock 4 output
- Virtual Xbox 360 output
- Profile-based configuration
- Button remapping
- Analog direction remapping
- Button → analog mapping
- Analog → button / trigger mapping
- Deadzone adjustment
- Anti-deadzone adjustment
- Analog sensitivity adjustment
- Vibration strength control
- DualShock 4 lightbar customization
- DualShock 4 touchpad support (USB and Bluetooth)
- Single-touch and dual-touch tracking
- Native touch forwarding to the virtual DualShock 4
- Touchpad swipe detection (Up / Down / Left / Right)
- Animated touch trail in the Gamepad Tester, following the selected lightbar color
- Touchpad modes: Normal, Mouse, Disabled (saved per profile)
- Mouse mode: touchpad controls the Windows cursor; touchpad click acts as left mouse click
- Live virtual-output gamepad tester
- Windows startup support
- System tray mode
- Controller connection notifications
- Turkish and English interface
- Built-in ViGEmBus / HidHide component management

---

## Supported Controllers

### Physical input

| Controller | Status |
|---|---|
| DualShock 4 v1 / v2 | Supported |
| DualShock 4 USB Wireless Adapter | Supported |
| DualSense | Supported |
| DualSense Edge | Supported |
| Nintendo Switch Pro Controller | Supported |
| Nintendo Joy-Con | Supported |
| Logitech F310 | Supported |
| Logitech F510 | Supported |
| Logitech F710 | Supported |

DualShock 4 uses the application's raw HID input path.

DualSense, Nintendo and supported Logitech controllers use the SDL input provider.

Physical Xbox controllers and Steam Controller input are currently outside the scope of this version.

### Virtual output

- DualShock 4
- Xbox 360 Controller

The application filters its own ViGEm virtual devices so they cannot be detected again as physical input.

---

## Download

The recommended version is the Windows installer from **GitHub Releases**:

```text
aRacnid-GamepadApp-Setup-1.0.4-x64.exe
```

A portable package is also available:

```text
aRacnid-GamepadApp-1.0.4-win-x64-portable.zip
```

Release downloads are accompanied by:

```text
SHA256SUMS.txt
```

for file-integrity verification.

---

## System Requirements

- Windows 10 / Windows 11
- x64 system
- ViGEmBus
- HidHide recommended

### .NET installation

The official Setup and Portable releases are published as:

```text
win-x64 self-contained
```

Therefore, users **do not need to install .NET 10 separately**.

The required .NET runtime files are included with the application.

---

## Installation

1. Download and run:

   ```text
   aRacnid-GamepadApp-Setup-1.0.4-x64.exe
   ```

2. Complete the installer.

3. Open **aRacnid GamepadApp**.

4. Go to:

   **Advanced → Manage Components**

5. Install **ViGEmBus** if it is not already installed.

6. Install **HidHide** if you want to prevent games from detecting both the physical and virtual controller.

7. Connect your physical controller.

A virtual controller is not created until a valid physical controller input frame is received.

> aRacnid GamepadApp and its installer are currently unsigned. Windows SmartScreen may therefore display a warning on first launch.

---

## ViGEmBus

ViGEmBus is required for virtual controller output.

aRacnid GamepadApp can create:

- a virtual DualShock 4
- a virtual Xbox 360 Controller

without exposing a virtual device when no supported physical controller is connected.

If ViGEmBus is installed from inside the application, close and reopen aRacnid GamepadApp after installation.

---

## HidHide

HidHide is optional but strongly recommended.

Without HidHide, some games may detect:

```text
Physical controller
+
Virtual controller
```

at the same time, causing double input or menus moving twice.

### Setup installation

If HidHide is already installed when the aRacnid Setup is executed, the installer automatically registers the installed:

```text
GamepadApp.exe
```

in the HidHide Applications whitelist.

### If HidHide is installed later

If HidHide is installed after aRacnid GamepadApp has already been installed:

1. Open HidHide Configuration Client.
2. Open the **Applications** tab.
3. Add the installed `GamepadApp.exe`.
4. Open the **Devices** tab.
5. Select the physical controller.
6. Enable device hiding / cloaking.

Do not hide the physical controller before allowing `GamepadApp.exe` through HidHide, otherwise aRacnid will not be able to read it.

The goal is:

```text
aRacnid GamepadApp → physical controller visible
Games               → physical controller hidden
Games               → virtual controller visible
```

---

## Profiles

aRacnid supports multiple user profiles.

Controller configuration can be stored separately for each profile, including:

- button mappings
- analog mappings
- deadzone
- anti-deadzone
- sensitivity
- vibration strength
- virtual controller type
- lightbar settings

The last selected profile is remembered between launches.

Profile data is stored under:

```text
%AppData%\GamepadApp\
```

Profile and application settings use atomic file writes and backup recovery to reduce the risk of configuration loss after an interrupted write.

---

## Analog Processing

The analog pipeline supports:

```text
Physical input
→ Deadzone
→ Anti-deadzone
→ Sensitivity
→ Profile remapping
→ Virtual output
```

At **100% sensitivity**, analog byte values are preserved without applying an additional sensitivity curve.

Analog directions can also participate in mappings such as:

```text
Analog direction → Analog direction
Analog direction → Button
Analog direction → Trigger
Button           → Analog direction
```

---

## Gamepad Tester

The built-in Gamepad Tester displays the **mapped virtual output**, not a second independent reading of the physical controller.

This means the tester reflects the same output state that is sent to the virtual controller, including:

- buttons
- triggers
- remapped analog directions
- button → analog mappings
- analog → button mappings

DualShock 4 and Xbox layouts are displayed independently.

---

## Lightbar

Supported Sony controllers can expose lightbar controls through the application.

Profiles may store:

- custom colors
- selected color
- lightbar enabled / disabled state

Lightbar settings are restored with the active profile where supported.

---

## Vibration

Virtual-controller rumble feedback is forwarded back to supported physical controllers.

Separate strength controls are available for:

- left motor
- right motor

Controller families that do not support vibration simply ignore physical rumble output.

---

## Testing

The project includes an automated input and regression test suite.

Current validated result:

```text
Build:    0 errors, 0 warnings
Tests:    37 / 37 PASS
```

The tests cover areas including:

- DS4 USB input reports
- DS4 Bluetooth report normalization
- report integrity and CRC
- malformed report rejection
- physical input snapshots
- SDL stick conversion
- SDL trigger conversion
- supported-device allowlist
- virtual-device rejection
- button → trigger mapping
- trigger → button mapping
- analog-direction remapping
- 100% sensitivity byte identity
- vibration scaling
- DS4 feedback parsing
- profile atomic recovery
- settings atomic recovery
- localization JSON validation
- single-submit ViGEm report behavior
- SDL native ABI validation

The `--live` test additionally creates real ViGEm virtual DS4 and Xbox targets and verifies their PnP filtering behavior.

### Hardware validation

The DualShock 4 USB and Bluetooth paths have received physical hardware testing.

DualSense, Nintendo and Logitech support is implemented and covered by code / automated validation, but physical testing is still recommended for every supported model and connection type.

---

## Portable Version

The portable ZIP is also self-contained.

Always extract the **entire ZIP** before launching the application.

Do not copy only:

```text
GamepadApp.exe
```

out of the package.

Runtime and dependency files beside the executable are required.

---

## Development

### Requirements

- Windows 10 / 11 x64
- .NET 10 SDK
- ViGEmBus
- Inno Setup 6 for generating the installer

### Build

```powershell
dotnet restore GamepadApp.csproj
dotnet build GamepadApp.csproj -c Debug
dotnet build GamepadApp.csproj -c Release
dotnet build tests\GamepadApp.InputTests\GamepadApp.InputTests.csproj -c Release
```

Run normal tests:

```powershell
dotnet run --project tests\GamepadApp.InputTests\GamepadApp.InputTests.csproj -c Release
```

Run live ViGEm tests:

```powershell
dotnet run --project tests\GamepadApp.InputTests\GamepadApp.InputTests.csproj -c Release -- --live
```

---

## Building a Release

The release script generates a self-contained `win-x64` build.

```powershell
.\scripts\build-release.ps1 -Version 1.0.4
```

The script produces:

```text
artifacts\
├── installer\
│   └── aRacnid-GamepadApp-Setup-1.0.4-x64.exe
├── aRacnid-GamepadApp-1.0.4-win-x64-portable.zip
├── SHA256SUMS.txt
└── publish\
```

If Inno Setup is not installed:

```powershell
.\scripts\build-release.ps1 -Version 1.0.4 -SkipInstaller
```

---

## Third-Party Software

aRacnid GamepadApp uses or interoperates with third-party open-source projects.

Major components include:

- **ViGEmBus** — virtual gamepad bus driver — BSD 3-Clause
- **HidHide** — physical HID device hiding — MIT
- **SDL 3.4.14** — additional physical-controller input support — zlib
- **HidSharp** — HID communication library
- **Nefarius.ViGEm.Client** — .NET interface for ViGEm

Full third-party copyright and license notices are available in:

[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)

Third-party projects remain the property of their respective authors and copyright holders.

---

## Trademarks and Disclaimer

aRacnid GamepadApp is an **independent, unofficial project**.

It is not affiliated with, sponsored by, approved by, or endorsed by Sony Interactive Entertainment, Microsoft, Nintendo, Logitech, Nefarius Software Solutions, or any other controller or platform manufacturer.

Names such as **PlayStation**, **DualShock**, **DualSense**, **Xbox**, **Nintendo Switch**, and **Logitech** are used only to describe controller compatibility.

All product names, trademarks and registered trademarks are the property of their respective owners.

No affiliation or endorsement should be inferred from compatibility with these devices.

---

## Security

The built-in component installer downloads supported dependency installers from their configured official project sources and performs package verification before launching them.

The application itself does not require administrator privileges for normal use, although Windows may request administrator approval when installing system drivers such as ViGEmBus or HidHide.

---

## Language

The application interface currently supports:

- Turkish
- English

---

## License

aRacnid GamepadApp is licensed under the **GNU General Public License v3.0 only (GPL-3.0-only)**.

You may use, study, modify, and redistribute the software under the terms of the GPLv3. If you distribute modified versions or derivative works, the corresponding source code must remain available under the same license.

See [LICENSE](LICENSE) for the full license text.

---

## Status

**aRacnid GamepadApp 1.0.4**

Windows x64  
Self-contained release  
Setup + Portable distribution
