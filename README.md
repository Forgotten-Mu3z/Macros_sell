# Fortnite Edit Input

A Windows desktop utility for configurable, Fortnite-style **edit input handling**. Hold your Edit key and the app
holds your Select key for you, so you can drag across edit tiles. Release Edit and everything is released.

Every bind is configurable (keyboard, mouse, or controller). There's an optional Reset-before-Select sequence, adjustable
timing, three select modes, auto-confirm, profiles, a controller view, a debug visualizer, and a **Simple Remap** mode for
strict one-input-to-one-input remapping with no automation.

> **The most important rule in this codebase: no stuck inputs.** Every held key or button goes through one owner
> (`OutputManager`), and every exit path (Edit released, disable, emergency stop, profile change, controller
> disconnect, session lock or suspend, crash, exit) runs the same centralised `ReleaseAll`.

> **Fair-play note:** automation that sends inputs for you may be restricted by a game's rules or by tournament
> rules. Check the rules for the game and events you play before you use it. Simple Remap mode only remaps one input
> to one input.

---

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Build & run](#build--run)
- [Using the app](#using-the-app)
- [Architecture](#architecture)
- [Reliability design](#reliability-design)
- [Configuration files](#configuration-files)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)

---

## Features

| Area | What you get |
| --- | --- |
| Binds | Edit (default **E**), Select (default **P**), Reset, Confirm, Enable/Disable (default **F8**), Emergency Stop (default **F12**). Any keyboard key, mouse button (L/R/M/4/5) or controller button (A B X Y, LB RB, LT RT, LS RS, D-Pad, View, Menu, stick directions). |
| Capture | Click a bind box, then press the input. **Esc** cancels. Automation is paused until the captured input is released, so the bind press can't trigger anything. |
| Reset Before Select | Optional: Edit → Reset tap → delay → Select. Reset happens **once** per activation. |
| Timing | Reset → Select delay (0–100 ms, default 10), Edit → Select delay (0–100 ms, default 0), tap duration, confirm delay. You can use the slider or type an exact value. |
| Select modes | Hold Until Edit Released (default), Tap Once, Toggle. |
| Auto Confirm | Off (default), Confirm On Edit Release, Confirm After Select Release. When Off, the app never generates Confirm. |
| Devices | Keyboard & Mouse, Controller, or Hybrid (mix devices, e.g. keyboard Edit + controller RT Select). |
| Controller | XInput (Xbox, GameSir in XInput mode, most PC pads, PlayStation via DS4Windows/Steam Input). Auto-detect, choice of controller slot 1–4, trigger threshold with hysteresis, stick deadzone, 250/500/1000 Hz polling, and a live diagram that highlights pressed buttons. |
| Controller output | Optional virtual Xbox controller (ViGEmBus) for controller Select/Reset/Confirm outputs. |
| Profiles | New, Save, Duplicate, Rename, Delete. The last-used profile loads on start. Ships with *Default*, *Fortnite KBM*, *Fortnite Controller* and *Fast Edit*. |
| Conflicts | Live warnings such as "⚠ Edit and Select are using the same input." The Emergency Stop key can't be set to an input the app generates. |
| Safety | Emergency Stop (releases everything, then stays stopped until you click **Enable**), a release watchdog, stale-state recovery, session-change handling, and a single-instance guard. |
| Simple Remap | Strict 1:1 remapping with every automation feature off. Keyboard and mouse sources are hidden from other apps. |
| Debug | Optional millisecond-timestamped event log (only configured control inputs and app events, never typing) and a live visualizer. |
| Tray | Start with Windows, start minimized, minimize to tray. The tray menu has Enable, Disable, Open Settings, Emergency Release and Exit. The tray icon shows status: green = enabled, gray = disabled, red = emergency stop or error. |

## Requirements

- Windows 10 (1809 or newer) or Windows 11, x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build, or the .NET 8 Desktop Runtime to run a framework-dependent build
- *Optional:* [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) driver. You only need it if a profile **generates** controller inputs, for example Select = RT. Controller buttons work as *inputs* without it.

The app doesn't need admin rights. See [Troubleshooting](#troubleshooting) for when elevation matters.

## Build & run

```powershell
git clone <this repo>
cd Macros_sell

dotnet build FortniteEditInput.sln -c Release
dotnet test  tests/EditInput.Core.Tests
dotnet run   --project src/EditInput.App -c Release
```

To make a single self-contained executable (no runtime install needed):

```powershell
dotnet publish src/EditInput.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
# → publish\FortniteEditInput.exe
```

Visual Studio 2022 (17.8+): open `FortniteEditInput.sln`, set **EditInput.App** as the startup project, then press F5.

The core library and its tests are plain `net8.0`, so `dotnet test tests/EditInput.Core.Tests` also runs on Linux or
macOS. The Windows projects build anywhere (`EnableWindowsTargeting`) but only run on Windows.

## Using the app

1. On first launch, the **Welcome** screen shows the defaults: Edit **E**, Select **P**, Reset Before Select **OFF**,
   Select Mode **Hold Until Edit Released**, **F8** to enable or disable, **F12** for Emergency Stop. Click **Start**.
2. **Edit Binds** page: click a bind box and press the input you want. Click ✕ to clear a bind.
3. Adjust **Behavior** and **Timing**. Changes apply straight away, so you can test them in game. Click **Save Profile**
   (disk icon in the header) to keep them. If you switch profiles or exit with unsaved changes, the app asks whether to save.
4. Use **ENABLE** / **DISABLE** in the footer, the Enable/Disable hotkey, or the tray menu.
5. **EMERGENCY STOP** (button, hotkey or tray) releases every generated input straight away. The app stays stopped
   until you click **ENABLE**. The Enable/Disable hotkey won't restart it on purpose.

### Sequences

```
Hold mode, Reset Before Select OFF        Hold mode, Reset Before Select ON
E down  → [Select Delay] → P down         E down → [Select Delay] → Reset down
(drag across tiles)                                → [Tap Duration] → Reset up
E up    → P up                                     → [Reset Delay]  → P down
                                          E up   → P up (then Confirm, if enabled)
```

- **Tap Once**: Select is tapped once. Releasing Edit does nothing more.
- **Toggle**: the first Edit press holds Select. The next Edit press releases it. Releasing Edit doesn't release Select.
- **Auto Confirm**: after Select is released, Confirm is tapped once, after the Confirm Delay. When Off, no Confirm is ever generated.
- If you release Edit **before** Select starts (a rapid tap), the sequence is cancelled and anything it held (Reset) is
  released. Select never starts.

### Simple Remap (accessibility mode)

On the **Simple Remap** page, switch on *Simple Remap Mode* and add mappings (FROM → TO). While it's on:

- Pressing a source presses its target, and releasing it releases the target. Nothing else is generated.
- Every automation feature is off: no Reset, delays, select modes or confirm.
- Keyboard and mouse sources are hidden from other apps, so only the target is seen. Controller sources can't be hidden.
- Mappings are strictly one-to-one. If a source is mapped twice, only the first mapping is used.

## Architecture

```
FortniteEditInput.sln
├─ src/EditInput.Core        net8.0 – platform-independent, fully unit tested
│   ├─ Input/                InputId (key/mouse/pad), KeyNames, InputStateTracker (edge detection)
│   ├─ Controller/           ControllerStateInterpreter (threshold + hysteresis, radial deadzone)
│   ├─ Engine/               MacroEngine (state machine), EngineHost (input worker thread), EngineConfig
│   ├─ Output/               OutputManager (tracks every held input, centralised ReleaseAll)
│   ├─ Profiles/             Profile, AppSettings, ProfileManager, SettingsManager, JsonStore (atomic writes)
│   ├─ Validation/           ConflictDetector
│   └─ Logging/              Logger (non-blocking, ring buffer + file)
├─ src/EditInput.Windows     net8.0-windows – Win32 implementations
│   ├─ Input/                HookThread, KeyboardInputManager, MouseInputManager (WH_*_LL hooks),
│   │                        ControllerInputManager (XInput), InputManager (aggregation + hook policy),
│   │                        WindowsPhysicalStateProbe (release watchdog)
│   ├─ Output/               SendInputBackend (scan codes), ViGEmControllerOutput (virtual pad)
│   └─ HighResolutionTimer   high-resolution waitable timer (sub-ms waits without busy loops)
├─ src/EditInput.App         WPF UI (MVVM): AppServices (composition root), UIManager (tray),
│                            MainViewModel + page view-models, dark theme, pages, dialogs
└─ tests/EditInput.Core.Tests  xUnit: engine scenarios, remap, output, controller, persistence, host thread
```

The controller API sits behind `ControllerInputManager`. The engine only ever sees `InputId` edges, so XInput can be
replaced with GameInput without touching the macro engine.

### State machine

```
            Edit down                    [Reset Before Select]
Idle ─────────────────► EditPressed ──(select delay)──► Resetting ──(tap)──► WaitingForSelect
 ▲                           │  (no reset)                                        │ (reset delay)
 │                           └───────────────────────────────► Selecting ◄────────┘
 │   Edit up / confirm done                                        │ Edit up
 └───────────────────────────── Releasing ◄────────────────────────┘

Disabled ◄──► Idle        any state ──F12──► EmergencyStopped ──(Enable button only)──► Idle
```

- Only **one** timed step can be pending at a time, so duplicate Select or Reset actions are structurally impossible.
- Actions fire only on **edges** (up→down, down→up). Keyboard auto-repeat is filtered out by `InputStateTracker`.
- Delays are deadlines on a monotonic `Stopwatch` clock. They are never `Thread.Sleep` calls on the UI thread.

### Threading

| Thread | Work |
| --- | --- |
| UI (WPF dispatcher) | Views only. It reads immutable engine and controller snapshots at 30 Hz and never blocks on input work. |
| `InputHooks` | Win32 message loop that owns the low-level hooks. Callbacks do one set lookup and one queue post. |
| `InputEngine` | Owns `MacroEngine`. It drains the input/command queue, runs due steps, then sleeps on a high-resolution timer until the next deadline. It uses 0% CPU when idle. |
| `ControllerPoll` | Polls XInput at 250–1000 Hz only while a controller input is bound, being captured, or displayed. Otherwise it checks the connection 4× per second. |
| `LogWriter` | Writes logs to disk in the background. |

## Reliability design

| Risk | Mitigation |
| --- | --- |
| Stuck inputs | `OutputManager` tracks every held input. `ReleaseAll` releases Select first, then the rest newest-first, then neutralises each device. It is called on Edit release, disable, emergency stop, profile change, controller disconnect, lock/unlock/suspend, engine error, UI exception, fatal exception, process exit and shutdown. |
| Missed key-up (hook skipped, elevated window focused…) | A **release watchdog** checks the real key state (`GetAsyncKeyState` or the controller poll) every 25 ms while Select is held. If Edit has really been released, it runs the normal release path. A repeated "down" after 1.5 s of silence is also treated as a missed release. |
| Duplicate events | Edge detection, a single pending step per activation, and `OutputManager` refusing a second press of a held input. |
| Feedback loops | Injected events carry a signature in `dwExtraInfo` and our hooks drop them. The virtual controller's XInput slot is excluded from polling. |
| Race conditions | All engine state lives on one thread and everything else posts messages to it. `OutputManager` is lock-protected. Hooks read an immutable, atomically swapped policy. |
| Emergency stop latency | The emergency stop first closes the output gate and releases on the calling thread, then notifies the engine. |
| Controller disconnect | Detected on the next poll. Generated inputs are released and controller state is forgotten, so resuming needs a **fresh** Edit press. |
| Remap blocking | A swallowed key-up is only ever one whose key-down was swallowed, so other apps never see a key go down without coming back up. |
| Two instances | A named mutex. A second launch just brings the first window forward. |
| UI freezes | No blocking calls on the UI thread. Driver checks and virtual-pad plug-in run on background tasks. |

## Configuration files

Everything is local, in `%APPDATA%\FortniteEditInput\`. There's no account and nothing is uploaded.

| File | Contents |
| --- | --- |
| `settings.json` | Active profile, global hotkeys, startup/tray options, debug logging, polling rate |
| `profiles.json` | All profiles |
| `logs\edit-input-YYYYMMDD.log` | Warnings/errors always; debug events only when Debug logging is on |

Example `settings.json`:

```json
{
  "activeProfile": "Default",
  "emergencyStop": "Key:F12",
  "toggleBind": "Key:F8",
  "startMinimized": false,
  "startWithWindows": false,
  "minimizeToTray": true,
  "enableOnLaunch": true,
  "debugLogging": false,
  "controllerPollRateHz": 500
}
```

Bind strings look like `Key:E`, `Key:LShift`, `Mouse:X1`, `Pad:RT`, `Pad:LStickUp` or `None`. Files are written
atomically. If a file is unreadable, it's renamed to `*.corrupt-<timestamp>` and the app starts with defaults.

## Troubleshooting

**Select (P) isn't reaching the game.**
- If the game (or its launcher) runs **as administrator**, Windows blocks input from non-elevated apps (UIPI). Run
  this app as administrator too, or run neither elevated.
- Open **Debug**, turn on *Debug logging* and hold Edit. You should see `Edit Down`, `Select Down`, `Edit Up` and
  `Select Up`. If you see `OS rejected press`, the target is elevated or on a secure desktop.
- Check the **Bind Check** card for conflicts or device-mode mismatches. For example, a controller Edit bind is
  ignored when Device Mode is *Keyboard & Mouse*.

**A key seems stuck.**
Press **F12** (Emergency Stop) or use **Emergency Release** in the tray. Everything the app generated is released.
The watchdog also clears a missed Edit release within about 50 ms. If it keeps happening, turn on debug logging and
check for `Watchdog:` or `Recovered missed release` lines.

**The bind press also triggered the macro.**
It can't: automation is paused while a bind is being captured and until the captured input is released. If a
profile switch happens while you hold Edit, you have to press Edit again. This is on purpose.

**No controller detected.**
- Only XInput devices are read. Xbox and most PC pads work directly. GameSir controllers need **XInput mode**.
- PlayStation controllers aren't XInput. Use Steam Input, DS4Windows or DSX to expose them as an Xbox controller.
- With several pads connected, pick one under **Controller → Use Controller**, or leave it on *Auto*.

**Controller Select/Reset/Confirm do nothing.**
Generating controller inputs needs the **ViGEmBus** driver. The Controller page shows its status, and the Bind
Check card warns when it's missing. With ViGEmBus, the game sees a second (virtual) Xbox controller that carries
the generated buttons. Some games only listen to one controller. If yours does, use keyboard/mouse outputs instead.

**Edit key comes from Steam Input or another remapper and isn't detected.**
Turn off *Settings → Ignore input generated by other software*. Input generated by this app is still ignored.

**Hooks stop responding after heavy system load.**
Windows can silently remove a low-level hook if the system stalls for a long time (see `LowLevelHooksTimeout`).
Restart the app. The hook callbacks do almost no work to keep this unlikely.

**Antivirus warning.**
Global keyboard hooks and `SendInput` are what every remapper uses, and some antivirus tools flag them. The app
has no network code. Build it from source if you want to be sure.

**Reset everything.**
Exit the app and delete `%APPDATA%\FortniteEditInput\`.

## Known limitations

- Controller input uses XInput, so Elite or GameSir paddles show up only as the button the controller maps them to,
  and native DualShock/DualSense support needs a translation layer. `ControllerInputManager` is the only class to
  replace for GameInput.
- Controller buttons can't be hidden from games in Simple Remap mode. XInput has no way to block them.
- If two opposite stick-direction outputs are held at the same time, releasing one centres that axis.
- The virtual controller is plugged in only while the active profile generates controller inputs. Plugging it in
  can make some games switch to controller UI prompts.
