# Changelog

## 0.2.0 — 2026-09-11

- **Connect F1 TV pop-up** in the dashboard: explains what the subscription unlocks, opens the sign-in
  window, follows it through *waiting → checking with the timing feed → connected*, and shows the plan and
  how long the sign-in lasts. Opens by itself at start-up when you're not signed in (can be turned off) and
  again if the sign-in expires. Reachable from the top-bar F1 TV chip, the track map, the controls drawer
  and the tray.
- **New sign-in window**: PitWall's frame around Formula 1's own page, with step chips, clear status
  (no subscription, expired cookie, offline with retry) and Cancel/Esc. Non-modal, so timing keeps updating.
- Sign-out also clears the Formula 1 cookies in PitWall's browser profile, so the next sign-in starts fresh.
- Paste-a-token option for browser-only and `--headless` setups (`POST /api/auth`, sealed, never echoed).
- `PitWall.exe --connect` opens the sign-in pop-up at start-up.
- `build.cmd` (double-click build), `build.cmd -Install` and `scripts/install.ps1` (Start menu/desktop
  shortcuts, uninstall), and a step-by-step guide in `docs/BUILD.md`.

## 0.1.0 — 2026-09-11

First release.

- Two-screen launcher (`PitWall.exe`): dashboard on one display, F1 TV in Edge app mode on the other,
  first-run screen setup, swap screens, tray menu, single-file self-contained build.
- Live timing over Formula 1's SignalR Core feed, with an optional F1 TV subscription token for car
  positions, telemetry, championship projection and pit-lane data. The token is sealed with DPAPI.
- Replay of any archived session at 1–30× with seeking.
- Dashboard panels: timing tower with sectors and mini-sectors, track map built from GPS traces,
  driver focus card, tyre strategy with stint pace trend, race control, team radio, weather with
  circuit forecast, championship projection/standings, news ticker.
- Weekend page: schedule in local time with countdown, standings, news, season calendar.
- Broadcast delay to line the timing up with the video.
