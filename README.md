# MultiBlox

Run several Roblox accounts at the same time, from one clean launcher.

**[⬇ Download the latest version](../../releases/latest)** - grab `MultiBlox.exe` and double-click it. Nothing to install (Windows 10/11, 64-bit).

## Features

- **Multi-Roblox** - open as many Roblox windows as you like. If Roblox is already open when MultiBlox starts, it offers to close it so multi-instance can turn on.
- **Accounts** - add accounts by logging in normally in a built-in browser window (username + password, 2-step and captcha all work). Launch one, or all checked accounts, into any game.
- **Private servers** - the **Server** box takes a Job ID or a private server link (old `privateServerLinkCode=` links and new `share?code=` links).
- **Profiles** - save a whole setup (which accounts, game, server, FPS, anti-AFK, power saver, auto-rejoin, mini viewer) and launch it in one click.
- **Auto-rejoin** - if a game crashes, its window closes, or Roblox freezes for a minute, that account rejoins the same game/server. Max 5 rejoins per account per 30 minutes so a broken game can't loop.
- **Mini viewer** - tiny live previews of every game, at any size (Roblox's own window won't go that small). The real windows stay running off-screen.
  - Hover a preview → that game pops up beside the viewer as the active window, so you can check on it.
  - Click → keep it out and play. Right-click → tuck it away.
  - Drag previews to put your accounts in the order you want. The order is saved and also used by the Instances list, Tile real windows and anti-AFK.
- **Discord stats** - if your macro posts match results to a Discord channel (webhook), MultiBlox reads that channel with your own bot and tallies **victories, losses and win rate per account**, plus every **drop** added up. Click an account to expand its drop totals and recent matches.
  - **Drops per hour** on every item, based on the last 24 hours.
  - **Goals** - e.g. "500,000 Summer Pearls" with a progress bar and time-left estimate across all accounts.
  - **Charts** - wins & losses or any item per day, per account or all, over 7/14/30 days.
  - **Rare-drop alerts** - Windows notification + sound when a watched item drops, plus an optional Discord ping (handy on your phone).
- **Anti-AFK** - every few minutes, briefly un-hides each game, clicks it, and hides it again so you don't get idle-kicked.
- **Power saver** - lower Roblox's CPU priority, trim its RAM, limit CPU cores, and launch at low FPS.
- **FPS editor** - set the frame-rate cap new games launch with (presets up to unlimited).
- **Instances** - see every running game with RAM usage, focus or close them, and clean up leftover background Roblox processes. Drag rows to reorder.
- **Auto-update** - MultiBlox checks GitHub for new versions, downloads them in the background, and installs on restart (or whenever you next close it). Your accounts and settings are kept. Turn it off with the switch at the bottom of the sidebar; click the version number to check now.

> **Updating from 1.3.0 or older?** Those versions can't update themselves yet. Close MultiBlox and replace `MultiBlox.exe` with the new one. Your accounts and settings live in `%APPDATA%\MultiBlox`, not in the exe, so nothing needs to be added again.

## Quick start

1. Close Roblox completely, then open `MultiBlox.exe`. The bottom-left box should say **Multi-Roblox ON**.
2. **Accounts → + Add account → Log in with browser**. Log in; the window closes itself when you're in. Repeat per account.
3. Enter a game's **Place ID** - the number in its link: `roblox.com/games/<PlaceID>/...`
4. Tick accounts and press **Launch all checked**.

> **"Windows protected your PC"?** That appears for any app that isn't from a big publisher. Click **More info → Run anyway**.

## Discord stats setup

A webhook can only *post* to a channel, so MultiBlox reads the channel with a small bot you create (read-only, takes ~3 minutes):

1. [Discord Developer Portal](https://discord.com/developers/applications) → **New Application** → **Bot** → **Reset Token** → copy it. On the same page turn on **Message Content Intent** and save.
2. **OAuth2 → URL Generator** → tick `bot`, then **View Channels** and **Read Message History** → open the link and add the bot to your server.
3. In Discord: **Settings → Advanced → Developer Mode** on. Right-click the channel your macro posts to → **Copy Channel ID**.
4. MultiBlox → **Discord Stats** → paste the token and channel ID → **Connect**. The last 100 results are imported, then new ones are checked every 20 seconds.

It reads the user, Victory/Defeat, and the `+N Item` lines under **Rewards** (units are ignored). The bot token is stored encrypted like your logins and only sent to `discord.com`.

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
