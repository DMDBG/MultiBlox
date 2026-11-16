# MultiBlox

Run several Roblox accounts at the same time, from one clean launcher.

**[⬇ Download the latest version](../../releases/latest)** - grab `MultiBlox.exe` and double-click it. Nothing to install (Windows 10/11, 64-bit).

## Features

- **Multi-Roblox** - open as many Roblox windows as you like. If Roblox is already open when MultiBlox starts, it offers to close it so multi-instance can turn on.
- **Accounts** - add accounts by logging in normally in a built-in browser window (username + password, 2-step and captcha all work). Launch one, or all checked accounts, into any game or a specific server.
- **Mini viewer** - tiny live previews of every game, at any size (Roblox's own window won't go that small). The real windows stay running off-screen.
  - Hover a preview → that game pops up beside the viewer as the active window, so you can check on it.
  - Click → keep it out and play. Right-click → tuck it away.
- **Anti-AFK** - every few minutes, briefly un-hides each game, clicks it, and hides it again so you don't get idle-kicked.
- **Power saver** - lower Roblox's CPU priority, trim its RAM, limit CPU cores, and launch at low FPS.
- **FPS editor** - set the frame-rate cap new games launch with (presets up to unlimited).
- **Instances** - see every running game with RAM usage, focus or close them, and clean up leftover background Roblox processes.

## Quick start

1. Close Roblox completely, then open `MultiBlox.exe`. The bottom-left box should say **Multi-Roblox ON**.
2. **Accounts → + Add account → Log in with browser**. Log in; the window closes itself when you're in. Repeat per account.
3. Enter a game's **Place ID** - the number in its link: `roblox.com/games/<PlaceID>/...`
4. Tick accounts and press **Launch all checked**.

> **"Windows protected your PC"?** That appears for any app that isn't from a big publisher. Click **More info → Run anyway**.

## Is my login safe?

- You type your password into Roblox's own login page. MultiBlox never sees it.
- It keeps only the login session Roblox hands back, encrypted with Windows DPAPI so it only works on **your** Windows account, in `%APPDATA%\MultiBlox\config.json`.
- That session is only ever sent to `roblox.com`.
- Never share that file, or anything called `.ROBLOSECURITY` - it's as good as your password.

## Notes

- Changing FPS for a game that's *already running* isn't possible from outside without editing Roblox's memory, which its anti-cheat flags. Use Roblox's own **Settings → Maximum Frame Rate** for live changes.
- Running multiple clients and anti-AFK are a grey area under Roblox's rules. Use at your own risk.
- This project isn't affiliated with or endorsed by Roblox Corporation.

## Building from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```bash
cd MultiBlox
dotnet run
```

Standalone single-file exe (no .NET needed on the target PC):

```bash
dotnet publish MultiBlox -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```
