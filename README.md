<div align="center">

# apathy

**Set up a fresh Windows PC in one go.**
Tick the programs, drivers and tools you want, click install, and walk away.

![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows&logoColor=white)
![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?logo=dotnet&logoColor=white)
![Single exe](https://img.shields.io/badge/download-one%20~150%20KB%20exe-success)
[![Discord](https://img.shields.io/badge/Discord-join-5865F2?logo=discord&logoColor=white)](https://discord.com/invite/apathy)

</div>

---

apathy is a small Windows app for a new or freshly formatted computer. Instead of hunting down
installers on the internet one by one, you download a single exe, tick what you need and everything
installs on its own, silently, from the official sources. No toolbars, no "Next, Next, Finish".

It works like [Ninite](https://ninite.com), but with drivers that match your hardware, Windows and
Linux images, a search over thousands of programs, and a version picker.

## Contents

- [Features](#features)
- [Download and run](#download-and-run)
- [What's included](#whats-included)
- [How to use it](#how-to-use-it)
- [Drivers and hardware detection](#drivers-and-hardware-detection)
- [Operating systems](#operating-systems)
- [Adding your own programs](#adding-your-own-programs)
- [How it works](#how-it-works)
- [Is it safe?](#is-it-safe)
- [Building from source](#building-from-source)
- [Project structure](#project-structure)
- [Troubleshooting](#troubleshooting)
- [Community](#community)

## Features

- **109 programs in 7 categories:** browsers, runtimes, drivers, chat, media, games, coding tools,
  system utilities and operating systems, each category split into groups with "Select all".
- **Silent installs:** everything installs in the background, one after another, with live
  download progress and speed.
- **Hardware detection:** shows your processor, graphics card and motherboard and ticks the
  matching drivers with one click.
- **Real driver downloads:** NVIDIA app and AMD Adrenalin are fetched from nvidia.com / amd.com in
  their newest version and started for you, even though they aren't in winget.
- **Windows and Linux images:** Microsoft's Media Creation Tool for Windows 11/10, and the newest
  ISO of Ubuntu, Linux Mint, Debian, Arch and Kali.
- **Search:** filter the built-in list as you type, or search the whole winget catalog
  (thousands of programs) and install anything you find.
- **Pick a version:** install an older version of any winget program when the newest one has a bug.
- **Offline installers:** save every installer (and ISO) to a folder or USB stick to set up PCs
  without internet later.
- **Update everything:** one button updates every program on the PC.
- **Fast downloads:** switches winget away from Windows Delivery Optimization, which is often much
  slower than a direct download.
- **One small exe:** about 150 KB, nothing to install. It runs on any Windows 10 or 11 because
  .NET Framework 4.8 is already part of Windows.
- **Editable list:** drop a `programs.json` next to the exe to add, remove or reorder programs.

## Download and run

1. Download `apathy.exe` from the [Releases](../../releases) page.
2. Run it and click **Yes** on the Windows prompt. apathy asks for administrator rights once, so
   that the installers don't each ask again.
3. Pick your programs and click **Install selected**.

**Requirements:** Windows 10 (1809 or newer) or Windows 11, 64-bit, and an internet connection.
apathy uses winget (Windows Package Manager), which is built into Windows 11 and current Windows 10.
If it's missing, apathy tries to set it up by itself.

## What's included

| Category | Groups | What's in it |
| --- | --- | --- |
| **Essentials** | Browsers, Archives & files, Runtimes | Chrome, Firefox, Brave, Opera / Opera GX, Vivaldi, 7-Zip, WinRAR, Everything, Acrobat Reader, SumatraPDF, Visual C++ 2015-2022 (x64 + x86), .NET Desktop Runtime 8, DirectX, Java |
| **Drivers** | Graphics card, Processor & chipset, Motherboard, Peripherals, Everything else | NVIDIA app, AMD Adrenalin (graphics + Ryzen chipset), Intel Driver & Support Assistant, DDU, ASUS Armoury Crate, MSI Center, GIGABYTE, ASRock, Logitech G HUB / Options+, Razer Synapse 4, SteelSeries GG, Corsair iCUE 5, NZXT CAM, Snappy Driver Installer, FanControl, OpenRGB |
| **Apps** | Messaging, Media, Office, Cloud & passwords | Discord, Telegram, Signal, Zoom, Teams, TeamSpeak 3, VLC, Spotify, Audacity, HandBrake, GIMP, Krita, Inkscape, LibreOffice, Obsidian, Notion, Google Drive, Dropbox, Bitwarden, KeePassXC |
| **Games** | Launchers, Riot Games, Competitive & anti-cheat, Recording, Performance | Steam, Epic Games, EA app, Ubisoft Connect, GOG Galaxy, Minecraft, Valorant, League of Legends (EUNE / EUW), FACEIT AC + Client, OBS Studio, ShareX, MSI Afterburner, HWiNFO, CPU-Z, GPU-Z |
| **Coding** | Editors & IDEs, Languages, Tools | VS Code, Visual Studio 2022, Notepad++, IntelliJ IDEA CE, PyCharm CE, Neovim, Python 3.13, Node.js LTS, JDK 21, Git, GitHub Desktop, Windows Terminal, PowerShell 7, Docker Desktop, Postman |
| **System** | Tools, Remote access, Diagnostics | PowerToys, AutoHotkey, WizTree, qBittorrent, TeamViewer, AnyDesk, WinSCP, PuTTY, CrystalDiskInfo, Process Explorer, Malwarebytes |
| **Operating systems** | Windows, Bootable USB, Linux (ISO images) | Windows 11 / 10 Media Creation Tool, Windows 11 Installation Assistant, Rufus, Ventoy, balenaEtcher, Ubuntu 26.04 LTS, Linux Mint, Debian, Arch Linux, Kali Linux |

**Select recommended** ticks the basics every PC needs: Chrome, 7-Zip, Visual C++ runtimes,
Discord, VLC, Steam and Notepad++.

## How to use it

The icon column on the left is the menu:

| Icon | Tab | |
| --- | --- | --- |
| Magnifier (top) | **Search** | Filter the list, or search the whole winget catalog (also `Ctrl+F`). |
| The 7 icons below it | **Categories** | Tick programs. Hover an icon to see the category name. |
| Down arrow (bottom) | **Install** | Your selection, the action buttons and the log. |

**Install tab buttons:**

| Button | What it does |
| --- | --- |
| Install selected | Installs everything you ticked, newest version (or the one you picked) from the official source. |
| Download installers to folder | Saves the installers and ISO images to a folder, e.g. a USB stick, without running them. Downloads run 3 at a time. |
| Update everything on this PC | Runs `winget upgrade --all` for every program on the computer. |
| Stop | Stops the queue and the installer that is running. |
| Fast downloads | On by default. Makes winget download directly instead of through Delivery Optimization. |

**Picking a version:** in *Selected programs*, every winget program shows `latest ▾`. Click it to see
the versions winget has and pick one.

**Window:** drag it by any empty spot, resize it from the bottom-right corner, `Esc` closes it.
It opens big enough to show every tab without scrolling, as far as the screen allows.

## Drivers and hardware detection

The **Drivers** tab reads your hardware through WMI and shows it in the *This PC* box:

```
x
```

**Select drivers for this PC** ticks every driver that matches, and those entries are marked
`(your PC)`.

| Vendor | How it's installed |
| --- | --- |
| NVIDIA | The newest NVIDIA app is found on nvidia.com, downloaded and started. |
| AMD | AMD's auto-detect tool is found on amd.com, downloaded and started. It installs Radeon graphics *and* Ryzen chipset drivers. |
| Intel | Intel Driver & Support Assistant, through winget. |
| ASUS / MSI | Armoury Crate / MSI Center, through winget. |
| GIGABYTE / ASRock | No direct download exists, so their support page opens in your browser. |
| Anything else | Snappy Driver Installer covers LAN, audio, Wi-Fi and chipset drivers. |

Installers that open their own window (NVIDIA, AMD, Windows tools) run after the silent installs,
so the rest never waits for you to click through them. If a direct download fails, the vendor's
official download page opens instead.

## Operating systems

| Entry | What you get |
| --- | --- |
| Windows 11 / 10 (USB / ISO maker) | Microsoft's Media Creation Tool: makes a bootable USB stick or an ISO file. |
| Windows 11 Installation Assistant | Upgrades the current PC to the newest Windows 11. |
| Ubuntu, Linux Mint, Debian, Arch, Kali | The newest ISO image, saved to `Downloads\apathy` (not run). Several GB each. |
| Rufus, Ventoy, balenaEtcher | Tools that put an ISO on a USB stick. Ventoy holds many ISOs on one stick. |

apathy always looks up the newest release on the official mirror, so the list doesn't go out of
date when a new version of a distro comes out.

## Adding your own programs

The program list is built into the exe. To change it, put a `programs.json` next to `apathy.exe`;
it replaces the built-in list. Start from [`programs.json`](programs.json) in this repository.

```jsonc
[
  { "category": "Essentials", "icon": "E80F", "groups": [
    { "name": "Browsers", "programs": [
      { "name": "Google Chrome", "id": "Google.Chrome", "recommended": true },
      { "name": "Blender", "id": "BlenderFoundation.Blender" }
    ]}
  ]}
]
```

Find winget ids at [winget.run](https://winget.run) or with `winget search <name>`.

<details>
<summary><b>All keys</b></summary>

**Category**

| Key | Meaning |
| --- | --- |
| `category` | Name, shown as the tooltip of its icon. |
| `icon` | Segoe MDL2 Assets glyph as hex, e.g. `"E80F"`. |
| `groups` | List of groups (boxes) on the tab. |
| `hardware` | `true` shows the *This PC* box (hardware detection) on this tab. |

**Program**

| Key | Meaning |
| --- | --- |
| `name` | Text next to the checkbox. |
| `id` | winget package id. For direct downloads it's just a unique name. Entries with the same id install once. |
| `recommended` | `true`: ticked by *Select recommended*. |
| `noAdmin` | `true`: the installer refuses to run as administrator (Spotify), so it runs as the normal user. |
| `detect` | Hardware rules for *Select drivers for this PC*: `"cpu:intel"`, `"gpu:nvidia"`, `"board:asus"`. |
| `url` | Official page. Opened instead of installing when there's no download, or when a direct download fails. |
| `file` | Direct link to an installer or ISO. |
| `page` + `find` | A vendor page and a list of regexes. Each regex picks the link with the highest version on the current page (group 1 if the regex has one); the next regex runs on that link. The last match is the file. |
| `save` | `true`: only save the file (ISO images), don't run it. |
| `args` | Command line for a downloaded installer, e.g. silent switches. |

Example of a direct download that always takes the newest Linux Mint ISO:

```json
{ "name": "Linux Mint (Cinnamon)", "id": "LinuxMint.Cinnamon.ISO", "save": true,
  "page": "https://mirrors.edge.kernel.org/linuxmint/stable/",
  "find": ["href=\"(\\d+(?:\\.\\d+)?/)\"", "href=\"(linuxmint-[\\d.]+-cinnamon-64bit\\.iso)\""],
  "url": "https://linuxmint.com/download.php" }
```

</details>

## How it works

- **winget** ([Windows Package Manager](https://learn.microsoft.com/windows/package-manager/)) does
  the installing for almost everything: `winget install --id <id> -e --silent`. Packages come from
  Microsoft's community-reviewed manifest repository and point at the vendors' own download links.
- **Direct downloads** cover what winget doesn't have (NVIDIA, AMD, Windows tools, Linux ISOs):
  apathy reads the vendor's download page, finds the newest link and downloads it with progress.
  Files are written to a `.part` file first, so an interrupted download never looks finished.
- **Fast downloads** set `"network": { "downloader": "wininet" }` in winget's `settings.json`
  (a backup is kept as `settings.json.bak`). A downloader you already chose is left alone.
- **Administrator rights** are requested once at start (`requireAdministrator` manifest). Spotify,
  which refuses to install elevated, is started with a normal user token.
- **No telemetry.** apathy only talks to winget and to the download sites of the programs you pick.

## Is it safe?

- Installers come from the **official sources**: winget checks every installer's SHA-256 hash
  against its manifest before running it, and direct downloads only go to the vendors' own sites
  (nvidia.com, amd.com, microsoft.com, and the official Linux mirrors).
- Nothing is bundled: no adware, no toolbars, no extra offers.
- The whole source is in this repository. Every action is written to `apathy.log` next to the exe.

## Building from source

**Windows** (Visual Studio 2022 or the .NET SDK 6 or newer):

```powershell
git clone <this repository>
cd apathy
dotnet build -c Release
# -> bin\Release\net48\apathy.exe
```

Or open `apathy.csproj` in Visual Studio 2022 (with the *.NET desktop development* workload) and
build the *Release* configuration.

**Linux / macOS** (cross-build, e.g. for CI): the project sets `EnableWindowsTargeting`, so
`dotnet build -c Release` works with Microsoft's .NET SDK packages (the distribution-built SDKs
on some Linux distros lack the Windows Desktop workload). The result still only runs on Windows.

The app targets **.NET Framework 4.8** on purpose: it ships with every Windows 10 and 11, so the
exe is tiny and needs no runtime. A self-contained .NET 8 build would be around 70 MB.

## Project structure

```
apathy/
├── apathy.csproj          project (WPF, .NET Framework 4.8)
├── app.manifest           asks for administrator rights once, per-monitor DPI
├── programs.json          the built-in program list (embedded into the exe)
├── App.xaml(.cs)          startup and crash reporting
├── MainWindow.xaml(.cs)   the menu: tabs, search, install / download / update, window sizing
├── Models/
│   ├── Catalog.cs         programs.json format and loading
│   └── AppItem.cs         one ticked program: state, version
├── Services/
│   ├── Winget.cs          winget install / download / upgrade / search / versions, fast downloads
│   ├── DirectDownload.cs  vendor-page link finding, downloads with progress, running installers
│   ├── Hardware.cs        processor / graphics / motherboard detection (WMI)
│   ├── Log.cs             apathy.log
│   └── Native.cs          Windows API helpers (square corners, Downloads folder)
├── Themes/                the apathy look: palette, control styles, background pattern
└── Controls/              small helpers for the theme
```

## Troubleshooting

| Problem | Fix |
| --- | --- |
| "Could not start winget" | Update **App Installer** in the Microsoft Store, restart the PC and try again. |
| A program says *failed* | The reason is in `apathy.log` next to the exe (or in `%TEMP%`). Often the program is running; close it and retry. |
| Downloads are slow | Keep **Fast downloads** ticked. |
| NVIDIA / AMD opened a web page | Their site changed or was unreachable; download from the page that opened. |
| A program is *already there* | It's installed already; use **Update everything on this PC** to update it. |
| Windows SmartScreen warns about apathy.exe | The exe isn't code-signed. Click *More info* → *Run anyway*, or build it yourself from source. |

## Community

Questions, ideas, or a program you'd like in the list? Join the apathy Discord:
**[discord.com/invite/apathy](https://discord.com/invite/apathy)**

Pull requests that add programs to `programs.json` are welcome. Please use the official winget id,
and check that `winget install --id <id> -e` works.
