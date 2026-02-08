## 🎯 Project Objective And Motivation

This project aims to create a **steering simulation layer** that allows a standard game controller to behave like a **continuous steering system**, rather than a raw digital input, in Forza games (Forza Horizon and Forza Motorsport series).

Instead of mapping thumbstick position directly to steering angle, the engine maintains an internal **steering state**, producing an output closer to a real steering rack.

The core goal is **not to make drifting easier**, but to make it **more natural, predictable, and visually smooth**, preserving driver skill while eliminating the inherent limitations of digital thumbstick steering (some games like BeamNG.drive already implement similar systems natively).

In drifting, **style matters**. Smooth steering arcs and controlled counter-steer are essential for both realism and visual appeal. Traditional game controllers, with their binary thumbstick inputs, often lead to abrupt and unnatural steering behavior, detracting from the overall experience.

This project focuses on reinterpreting controller input as a simulated steering system.

---

## ⚠️ Current Status: Work In Progress

controller -> engine -> vJoy -> Forza communication is currently functional (it already simulates a wheel using vJoy, including force feedback). However, it does not have an stable release yet; there are planned features that are not yet implemented.

## Why not use ViGEmBus? (and use vJoy instead)

* **1) In Forza Horizon 5:** ViGEmBus is not recognized as a wheel device, only as a generic controller. The game applies a non-configurable deadzone on the steering axis between -0.25 and 0.25 (25% of the total range, in the center of the axis), which does not favor smooth movement; although it can be bypassed with adjustments in the engine's input processing, it is not ideal.

* **2) ViGEmBus alone does not support force feedback**.

## Why not use HidHide to hide input to avoid double input (controller + vJoy)?
* Even with the device selected and blocking enabled, it remains visible to Steam and Forza. Despite this, it hides the physical controller correctly in other software (e.g., vJoy Monitor, EmuWheel Configurator). I have tested multiples workarounds, involving Windows 11, Steam and Forza Horizon 5 configuration, but none have resolved the issue. The input behavior in Forza was not consistent. The only way I found to hide the controller from Steam and Forza was replacing the Xbox Controller driver from xinput to winUsb using Zadig, so steam and Forza no longer recognize it.

## Why not use SpecialK to hide input to avoid double input?
* Well... its modding. It injects dlls directly into the game, which probably gonna get us banned.

---

Next steps: 
- Refactor the engine: There are old code from xinput and some stuff not used anymore that can be removed.
- Use Forza UDP Telemetry to improve the engine's behavior.

---

# DRIFT Project - Input/Output Configuration Guide

This document details the step-by-step process to configure the project, ensuring that everything works.

Note: I have only tested with Xbox 360 Wireless Receiver and Forza Horizon 5. For other controllers to work, you may need to implement a custom driver using WinUSB.

## 📋 Prerequisites
* **.NET 8.0 SDK**
* **IDE** (e.g., Visual Studio Code with C# extensions).

## 1. Engine Configuration (DRIFT)

* Ensure that the build is up to date and compiling without errors.
    - run in test mode: ...smoothie_drift_steer_engine\src\DriftCore> `dotnet run --test`

## 2. Hide the physical controller from Steam and Forza


### 2.1 Download **Zadig** (recommended version: 2.9).

### 2.2 Change the driver of the physical controller to **WinUSB** using Zadig:
1.  Open **Zadig** as Administrator.
2.  In the top menu, select `Options` > `List All Devices`.
3.  From the dropdown, select your physical controller (in this case, Xbox 360 Wireless Receiver).
4.  In the driver selection box, choose **WinUSB**.
5.  Click `Install Driver` and wait for the process to complete.
6. Restart your computer to ensure the changes take effect.
7.  **Verification:** After installation, check if the controller is no longer visible in Steam's controller settings and Forza's controller configuration. It should not be listed as an available gamepad.

PS: If you want to revert the changes, go to device manager, find the controller/receiver, uninstall the driver, disconnect and reconnect the controller, and it should reinstall the original driver. You may need to restart your computer again.

## 3. Configure the Engine to use the correct input device:
1.  Open the engine's configuration file: `appsettings.json`.
2. make sure "UseWinUsbReceiver" is true.
3. Set the `WinUsbDeviceInterfaceGuid` to the correct value for your controller/receiver. You can find the correct GUID by opening Device Manager, finding your controller/receiver, right-clicking and selecting `Properties` -> `Details`.
4. **Verification:** Run the engine in test mode (`dotnet run --test`) and check the console output to confirm that it detects the WinUSB device correctly.

## 4. vJoy (Virtual Joystick)

### 4.1 Installation
1.  Download and install **vJoy** (recommended version: 2.1.9).
2.  After installing, open **Configure vJoy (vJoyConf)** as Administrator.

### 4.2 Configuration
Configure **vJoy Device 1** exactly according to the parameters below:

* [x] **Basic Axes:** Check ALL (`X`, `Y`, `Z`, `Rx`, `Ry`, `Rz`, `Slider`, `Dial/Slider2`).
* [ ] **Force Feedback:** Unchecked (*Enable Effects* OFF).
* **Buttons:** Set to **128**.
* **POV Hat Switch:**
    * Select **4 directions**.
    * POVs: **1**.
* **Force Feedback:** Every thing checked (ON).
* **Finish:** Click on `Apply`.

### 4.3 Test (Monitoring)
1.  Open **vJoy Monitor** (JoyMonitor).
2.  Select "vJoy Device 1".
3.  Start the **DRIFT Engine** in test mode.
4.  **Verification:** When moving the physical controller or interacting with the Engine, do the bars in *JoyMonitor* move?
    * *If yes:* The Engine -> vJoy communication is working.

## 5.  **In-Game Test**
1.  Start Forza Horizon 5.
2.  Go to the game's controller configuration menu.
3.  Go to the steering mapping section.
4.  Select select a preset to enable the mapping.
5.  Map the input. (map the steering last, so it doesn't interfere with the other inputs)

---

## ⚠️ Disclaimer

This project is **not affiliated with, endorsed by, or associated with** any development team, publisher, or company related to the Forza franchise, including but not limited to Microsoft, Xbox Game Studios, or Turn 10 Studios.

This repository does **not modify the game, Steam, or any game files** in any way. It operates entirely as an **external input-processing layer**, without DLL injection, memory manipulation, or runtime hooks.

Because of this, the use of this project is **not expected to result in bans or penalties**. At the time of writing, it has been used without any issues.  
However, **no guarantees are made**. The author assumes **no responsibility** for any account restrictions, bans, or other consequences that may occur from its use.

Use this project **at your own risk**.

---

## 📄 License

This project is licensed under the **MIT License**.  
See the [LICENSE](LICENSE) file for more details.

