## 🎯 Project Objective And Motivation

This project aims to create a **steering simulation layer** that allows a standard game controller to behave like a **continuous steering system**, rather than a raw digital input, in Forza games (Forza Horizon and Forza Motorsport series).

Instead of mapping thumbstick position directly to steering angle, the engine maintains an internal **steering state**, producing an output closer to a real steering rack.

The core goal is **not to make drifting easier**, but to make it **more natural, predictable, and visually smooth**, preserving driver skill while eliminating the inherent limitations of digital thumbstick steering (some games like BeamNG.drive already implement similar systems natively).

In drifting, **style matters**. Smooth steering arcs and controlled counter-steer are essential for both realism and visual appeal. Traditional game controllers, with their binary thumbstick inputs, often lead to abrupt and unnatural steering behavior, detracting from the overall experience.

This project focuses on reinterpreting controller input as a simulated steering system.

---

## ⚠️ Current Status: NON-FUNCTIONAL (Work In Progress)

Only controller -> engine -> vJoy communication is currently functional (it already process the input, holding the steering angle properly in vJoy). The engine's output to Forza via EmuWheel is not working due to the following issues:

### Problems Identified:

* **1) vJoy Access Conflict:** Both **DRIFT Engine** and **Forza EmuWheel** are attempting to access the same vJoy device simultaneously. This causes access conflicts, making it impossible for both to function correctly at the same time.

* **2) HidHide Failure:** The software is unable to effectively apply hiding to **Xbox Controller**. Even with the device selected and blocking enabled, it remains visible to Steam and Forza. Despite this, it hides the physical controller correctly in other software (e.g., vJoy Monitor, EmuWheel Configurator). I have tested multiples workarounds, involving Windows 11, Steam and Forza Horizon 5 configuration, but none have resolved the issue.

    - **Symptom:** Both **Steam** and **Forza** simultaneously detect two devices:
        1.  🎮 The Physical Controller (Xbox Wireless).
        2.  🕹️ The Emulated Controller.

    - **Consequence:** A "Double Input" conflict occurs. The game receives raw input from the physical controller (unprocessed) at the same time it receives processed input from the *Drift Engine*, resulting in erratic behavior and nullifying the engine's simulation.

## Why not use ViGEmBus? (and use vJoy instead)

* **1) In Forza Horizon 5:** ViGEmBus is not recognized as a wheel device, only as a generic controller. The game applies a non-configurable deadzone on the steering axis between -0.25 and 0.25 (25% of the total range, in the center of the axis), which does not favor smooth movement; although it can be bypassed with adjustments in the engine's input processing, it is not ideal.

* **2) With EmuWheel:** ViGEmBus + EmuWheel only allows for Combined Brake and Throttle (Brake and Accelerator combined into a single axis). This does not allow Left Foot Braking to adjust the car's angle (Braking with the left foot - finger, in this case - while accelerating), which is important for advanced drift simulation with specific brake tuning.

## Why not use SpecialK to hide input?
* Well... its modding. It injects dlls directly into the game, which probably gonna get us banned.

---

Next steps: 
- Fix vJoy access conflict
- Fix input hiding: Maybe try manage the usb ports directly and use custom controller driver.
    - Zadig + Vortice.DirectInput?


---

# DRIFT Project - Input/Output Configuration Guide

This document details the step-by-step process to configure the project, ensuring that **vJoy**, **HidHide**(maybe), and not-**EmuWheel** work with the **DRIFT Engine**.

## 📋 Prerequisites
* **.NET 8.0 SDK**
* **Administrator Access** (for driver installation)

## 1. Engine Configuration (DRIFT)

* Ensure that the build is up to date and compiling without errors.
    - run in test mode: ...src\DriftCore> `dotnet run --test`

## 2. vJoy (Virtual Joystick)

### 2.1 Installation
1.  Download and install **vJoy** (recommended version: 2.1.9).
2.  After installing, open **Configure vJoy (vJoyConf)** as Administrator.

### 2.2 Configuration
Configure **vJoy Device 1** exactly according to the parameters below:

* [x] **Basic Axes:** Check ALL (`X`, `Y`, `Z`, `Rx`, `Ry`, `Rz`, `Slider`, `Dial/Slider2`).
* [ ] **Force Feedback:** Unchecked (*Enable Effects* OFF).
* **Buttons:** Set to **128**.
* **POV Hat Switch:**
    * Select **Continuous**.
    * POVs: **1**.
* **Finish:** Click on `Apply`.

### 2.3 Test (Monitoring)
1.  Open **vJoy Monitor** (JoyMonitor).
2.  Select "vJoy Device 1".
3.  Start the **DRIFT Engine** in test mode.
4.  **Verification:** When moving the physical controller or interacting with the Engine, do the bars in *JoyMonitor* move?
    * *If yes:* The Engine -> vJoy communication is working.

## 3. HidHide (Hardware Hiding) - optional for now

HidHide is crucial to avoid "double input" in games (although the error mentioned above still exists, it works correctly for other programs).

### 3.1 Installation
1.  Download and install the latest version of **HidHide**.
2.  **Restart the computer** (mandatory for the driver to function correctly).

### 3.2 Configuration: "Applications" Tab (Whitelist)

Add the paths using the `+` button:
1.  `DriftCore.exe` (Engine). It needs to see the physical controller.
    * **Build Path:**
    `...\smoothie_drift_steer_engine\src\DriftCore\bin\Debug\net8.0\DriftCore.exe`

### 3.3 Configuration: "Devices" Tab
1.  Check the `Enable device hiding` box.
2.  In the list, locate your physical controller (e.g., *HID-compliant game controller* or other).
3.  Select the controller.
4.  Reconnect the controller.

## 4. Forza EmuWheel - optional for now

(Not yet functional due to the problems mentioned at the beginning).

1.  Download **Forza EmuWheel**.
2.  Open **Configurator.exe**:
    * Map vJoy to the wheel controls.
3.  Open **Forza EmuWheel.exe**:

    * Click **Start**.

