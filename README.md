# PS5 PayloadManager

A Windows desktop application for managing, updating, and deploying PS5 payloads — both over the network and via USB autoload.

**Problem it solves:** Managing multiple PS5 payloads manually means rewriting USB drives, hunting GitHub releases for updates, and rebuilding `autoload.txt` every time something changes. PS5 PayloadManager handles all of that from one place.

---

## Key Features

| Feature | Details |
|---|---|
| GitHub payload sources | Fetch payloads directly from GitHub releases or repository folders |
| ZIP extraction | Payloads packed inside `.zip` release assets are unpacked transparently |
| Autoload Builder | Visual step editor — build payload execution sequences without editing text files |
| Version selection | Pin a payload step to a specific version or always use latest |
| Update detection | Four-tier check: version tag → git blob SHA → file size → publish timestamp |
| Local payloads | Add your own `.elf`/`.bin`/`.lua` files from disk |
| Autoload ZIP export | One click creates a USB-ready ZIP with `autoload.txt` and all payload files |
| Delay steps | `!5000` syntax — insert timed pauses between payloads in autoload sequences |
| Profile system | Save, import, and run named flows |
| Device management | Multiple PS5 IP addresses, live port status check |
| Network send | Send payloads directly over TCP without touching a USB drive |
| Log viewer | Structured log with level filtering (DEBUG / INFO / WARN / ERROR) |
| Backup & restore | Export and import a full portable ZIP backup of your config and payloads |

---

## How It Works

### 1. Add a Source

Go to **Sources** → enter a GitHub repo URL or `owner/repo` shorthand → choose source type:

- **Release** — scans the latest 3 GitHub releases and picks up `.elf`/`.bin`/`.lua` assets (including those inside ZIP files)
- **Folder** — scans a specific directory in the repository using the Contents API

Optionally set a filter (e.g. `*.elf`) to limit which files are imported.

### 2. Fetch Payloads

Click **Scan** on a source. Discovered payloads appear in the **Payloads** page with their current version and status dot:

- **Green** — downloaded and up to date
- **Yellow** — update available
- **Gray** — source not reachable

### 3. Check for Updates

Click **Check Updates** on the Payloads page. The tool compares the remote scan against stored metadata using (in order):

1. Version tag change
2. Git blob SHA change (folder sources)
3. File size change
4. `published_at` timestamp shift (≥ 60 s tolerance)

If an update is found, click **Download** or **Update All**.

### 4. Build a Flow

Go to **Autoload Builder**:

- Click **+ Payload** to add a payload step — choose the payload from the dropdown and optionally pin a version
- Click **+ Delay** to insert a timed pause
- Click **+ Wait** to wait until a TCP port opens before continuing
- Use the ▲ ▼ buttons to reorder steps

The **compatibility badge** tells you instantly whether the current flow can be exported as a PS5 autoload sequence (WAIT steps and Lua payloads are not compatible with the PS5 autoload format).

### 5. Export as Autoload ZIP

Click **Export Autoload ZIP**. The tool:

1. Downloads any missing remote payload files
2. Resolves local payloads from disk
3. Blocks the export if any required file is not available
4. Generates `autoload.txt` from your flow
5. Packages everything into `ps5_autoloader/` inside a ZIP

Copy the ZIP contents to your USB drive and you're done.

### 6. Or Send Directly

Click **Run** in the builder to send the flow to your PS5 over the network. Progress and logs appear inline.

---

## Autoload Format

The exported `autoload.txt` uses the PS5 autoload syntax:

```
kstuff.elf
!5000
shadowmount.elf
!2000
goldhen.bin
```

| Line | Meaning |
|---|---|
| `filename.elf` | Execute this payload |
| `!5000` | Wait 5000 ms before continuing |

**WAIT steps and Lua payloads are not supported in autoload sequences** — the builder flags these and blocks the export.

---

## Profile Format

Saved profiles (used for network execution) use a slightly different syntax:

```
kstuff.elf 9021
!5000
shadowmount.elf 9021
?9021 60 500
goldhen.bin 9021
```

| Line | Meaning |
|---|---|
| `filename.elf 9021` | Send payload to port 9021 |
| `!5000` | Delay 5000 ms |
| `?9021 60 500` | Wait for port 9021 to open, up to 60 s, polling every 500 ms |

---

## Installation

### Windows

1. Download the latest `PS5PayloadManager.exe` from [Releases](../../releases)
2. Run it — no installer required, self-contained single file
3. Data is stored in `%LOCALAPPDATA%\PS5Autopayload\`

### Requirements

- Windows 10 or later (x64)
- PS5 on the same local network (for network send)

---

## Setup

1. **GitHub Token** (optional but recommended) — add a personal access token in **Settings** to avoid GitHub API rate limits. Token needs no scopes for public repositories.

2. **PS5 IP** — add your PS5 IP under **Settings → Devices**. The builder lets you switch between multiple devices.

3. **Ports** — defaults match standard homebrew loaders:
   - ELF port: `9021`
   - Lua port: `9026`
   - BIN port: uses ELF port unless overridden

---

## Important Limitations

- **PS5 PayloadManager does not jailbreak your PS5.** You still need to trigger the exploit manually (e.g. via a browser-based jailbreak like Y2JB). This tool only manages and sends payloads after the exploit is active.
- **WAIT steps are not supported in autoload sequences.** The PS5 autoload format does not have a port-wait instruction — use delays (`!ms`) instead.
- **Lua payloads are not supported in autoload sequences.** Only `.elf` and `.bin` files can be included in `autoload.txt`.
- **Autoload requires USB.** The autoload ZIP is for USB-based execution — the PS5 reads `autoload.txt` from the drive. Network send does not use autoload.

---

## Why Use This Instead of Managing Files Manually?

| Manual | PS5 PayloadManager |
|---|---|
| Download releases from GitHub by hand | Auto-fetch and track updates from any GitHub repo |
| Rewrite the USB drive every time | Export a ready-to-use ZIP in one click |
| Remember which version you have | Version tracking with update notifications |
| Edit `autoload.txt` in a text editor | Visual builder with compatibility checking |
| Copy files between machines | Portable backup ZIP with config and payloads |

---

## FAQ

**Does it automatically jailbreak my PS5?**
No. You must trigger the exploit yourself. This tool manages payloads and sends them after the exploit is running.

**Does it replace USB?**
For network-connected PS5s yes — you can send payloads directly over TCP. For autoload (executed on boot), USB is still required, but the tool generates the correct folder structure for you.

**Can I use my own payload files?**
Yes. Click **+ Add Payload** on the Payloads page to import any `.elf`, `.bin`, or `.lua` file from disk. Local payloads are included in ZIP exports the same as downloaded ones.

**Can I have multiple payload sources?**
Yes. Add as many GitHub sources as you want. Each source is scanned independently and payloads are merged into one list.

**What happens if a source goes offline?**
Payloads from that source are marked with a gray dot ("Source not available"). Already-downloaded files remain usable.

**Can I pin a payload to an older version?**
Yes. In the Autoload Builder, each payload step has a version dropdown. Select any previously fetched version to pin it. The ZIP export will use that specific version.

**Where is my data stored?**
Everything is stored locally in `%LOCALAPPDATA%\PS5Autopayload\`. Use the backup feature in Settings to export a portable copy.

---

## Credits

This tool fetches payloads from community repositories. All payload software is the work of the respective developers and their projects.

Common sources used with this tool include repositories hosting payloads such as GoldHEN, kstuff, ShadowMount, and others maintained by the PS5 homebrew community.
