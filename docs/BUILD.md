# Build and run PitWall.exe

This guide takes you from the source code to PitWall running on your two screens.

## 1. One-time setup

| Tool | Version | Check | Get it |
|---|---|---|---|
| .NET SDK | 10.0 or newer | `dotnet --list-sdks` | https://dotnet.microsoft.com/download |
| Node.js | 22.19 or newer (22.12+ works, with a warning) | `node --version` | https://nodejs.org |
| Git | any | `git --version` | https://git-scm.com |

Windows 10 or 11 is needed to *run* PitWall. The Microsoft Edge WebView2 Runtime is part of Windows 11.

Get the code:

```powershell
git clone https://github.com/juandresrodca/PitWall.git
cd PitWall
```

## 2. Build the exe

Double-click **`build.cmd`** in the repository folder, or run it from a terminal:

```powershell
.\build.cmd
```

It does four things, in order:

1. `npm ci` + `npm run build` in `web/`: installs the dashboard's dependencies and builds it into `src/PitWall.Server/wwwroot`.
2. `dotnet test`: runs the timing-engine tests.
3. `dotnet publish` of `src/PitWall.Desktop` for `win-x64`, self-contained and single-file, with the dashboard embedded.
4. Prints the result: **`dist\PitWall.exe`** (about 60 MB, no other files needed).

Options (they are passed through to `scripts/publish.ps1`):

```powershell
.\build.cmd -Install      # build, then add PitWall to the Start menu
.\build.cmd -SkipTests    # faster rebuild
.\build.cmd -Runtime win-arm64 -Output dist\arm64
```

## 3. Start it

Pick one:

- **From the build folder:** double-click `dist\PitWall.exe`.
- **From the Start menu:** after `.\build.cmd -Install` (or `.\scripts\install.ps1`), press Start and type **PitWall**.
  Add a desktop icon with `powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Desktop`.
- **From a terminal**, with options:

```powershell
.\dist\PitWall.exe                 # dashboard + live video on two screens
.\dist\PitWall.exe --setup         # choose which screen shows what, again
.\dist\PitWall.exe --connect       # open the F1 TV sign-in pop-up straight away
.\dist\PitWall.exe --headless      # only the dashboard server: open http://127.0.0.1:5320 in any browser
.\dist\PitWall.exe --replay 2026/2026-09-06_Italian_Grand_Prix/2026-09-06_Race/
.\dist\PitWall.exe --port 5400     # if 5320 is taken
.\dist\PitWall.exe --lan           # let a tablet/phone on your network open the dashboard
```

Windows may show **"Windows protected your PC"** the first time, because the exe is not code-signed. Choose
**More info → Run anyway**. That only happens for builds you did not make yourself on this PC.

## 4. First launch, step by step

1. **Screens**: with two monitors, PitWall asks which one gets the live video and which gets the dashboard.
   *Identify screens* flashes a number on each. Your choice is remembered; change it any time with the tray icon →
   **Screen setup…**, or **Swap screens**.
2. **Dashboard**: opens full screen on its monitor. `F11` toggles full screen, `D` opens the controls.
3. **Connect F1 TV pop-up**: appears a moment later if you are not signed in.
   - Click **Sign in with F1 TV**. A Formula 1 window opens on top of the dashboard.
   - Formula 1 first shows its own cookie banner; pick either option. Then sign in on Formula 1's page.
     The steps at the top go *Sign in → Save token → Live data*, and the window closes itself once you are in.
   - The pop-up then shows **F1 TV connected** with your plan and how long the sign-in lasts (about a week).
     The track map starts showing cars within a few seconds.
   - **Not now** closes it; *Don't ask when PitWall starts* stops the automatic prompt. You can always reopen it from
     the **F1 TV** chip in the top bar, the track map, the controls drawer (`D`) or the tray icon.
4. **Video screen**: F1 TV opens full screen in Microsoft Edge. Sign in there **once**, since the video player
   keeps its own sign-in, and Edge remembers it for next time. Press `Esc` to leave full screen.

## 5. Everyday use

| You want to… | Do this |
|---|---|
| Match the timing to a delayed stream | `D` → *Broadcast delay* |
| Rewatch a past session | `D` → pick season, Grand Prix and session → *Replay this session* |
| Follow one driver | Click their row or their car |
| Open the weekend schedule and news | `W` |
| Quit | Tray icon (next to the clock) → **Exit PitWall** |

## 6. Troubleshooting

| Symptom | Fix |
|---|---|
| `build.cmd` says *npm ci failed* | Check `node --version` (22.12+). Behind a proxy, set `npm config set proxy …`. |
| *dotnet was not found* | Install the .NET 10 **SDK** (not only the runtime) and open a new terminal. |
| *PitWall is already running* | It lives in the tray (next to the clock). Use **Show dashboard** or **Exit PitWall**. |
| Dashboard says it has not been built | You ran `dotnet build` without building `web/` first. Use `build.cmd`. |
| Video plays audio but a black picture | Set the video player to *Microsoft Edge* in **Screen setup…** (the embedded window can't play every DRM stream). |
| Sign-in pop-up says *no subscription* | Formula 1 only issues the token to F1 TV Access/Pro accounts. Timing still works without it. |
| Port 5320 in use | PitWall picks the next free port automatically; for `--headless`, pass `--port`. |

Settings, caches and the sealed F1 TV token live in `%LOCALAPPDATA%\PitWall`. Sign out from the pop-up or the
tray to delete the token. To uninstall: `powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Uninstall`.
