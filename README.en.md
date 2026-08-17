# tgdl-bot

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

[中文版](README.md)

A **.NET 10** Telegram download bot distributed as a **Docker sandbox**: one image bundles
everything (tgdl-bot + a local Telegram Bot API Server + yt-dlp + ffmpeg), with no dependency
on any host program — deployable on any Linux distribution that can run Docker.

Users send video/music links to the bot (in private chat or an allowlisted channel/group).
The bot downloads with **yt-dlp** and pushes the result to the target channel/group through
the **local Bot API Server (--local mode)**, supporting uploads up to **~2GB**.

> **Legal & Compliance**: This project is a download tool intended for **personal, lawful use
> only**. Copyright laws vary widely by jurisdiction; you are responsible for verifying that
> your use is legal in your jurisdiction and for **respecting the target sites' Terms of
> Service (ToS)** and robots directives. You assume all infringement risk. This project does
> not store, retrieve, or redistribute any media content.
> See [Legal & Compliance](#legal--compliance) at the end.

## Features

- Downloads triggered from both private chat and channels/groups, pushed to configured target channels/groups
- Dual allowlist access control: private chat restricted to allowlisted users, channels/groups restricted to allowlisted chats
- Local Bot API Server (`--local`) bypasses the 50MB limit, enabling ~2GB large-file uploads
- **Docker sandbox**: telegram-bot-api / yt-dlp / ffmpeg bundled in the image; no host dependencies, works on any distro
- Low memory footprint: bot process resident RSS ≈ 40MB (under the 100MB target)
- `/update` self-updates yt-dlp and ffmpeg (written to the `tgdl-bin` volume, survives container recreation)
- Concurrent download queue, failure retries, progress notifications, disk-space and oversized-file detection
- Zero-trust: SSRF protection, full URL/command/path validation and sanitization, temp dir mode 0700, log masking, non-root execution in the container
- 12-factor configuration: environment variables first, or mount a `config.conf` file directly
- **Interactive installation wizard**: one-command deployment with guided language selection and per-field validation (degrades to template mode without a TTY)
- **Bilingual interface (en/zh)**: follows the user's Telegram language setting by default; switch anytime with `/language`
- **In-bot configuration**: `/config` to view and modify settings (effective after restart), `/access` to manage allowlist members

## Architecture Overview

```
src/TGBot/
├── Config/       config.conf parsing and validation
├── Logging/      leveled logging (console + optional file)
├── Security/     URL/SSRF validation, path sanitization, temp dir management, disk checks
├── Access/       dual allowlist access control
├── Download/     IDownloader abstraction, YtDlpDownloader (process invocation), concurrency gate, job registry, output parsing
├── Update/       IUpdater abstraction, version comparison, atomic replacement, remote version sources (yt-dlp GitHub / ffmpeg johnvansickle)
├── Messaging/    ITelegramClient abstraction (Telegram.Bot implementation), upload service, caption builder
├── Application/  message routing, download coordination, command handling, bot long-polling host, entry point
└── Texts/        user-facing text messages (i18n: en/zh)

tests/TGBot.Tests/   xUnit unit tests + real yt-dlp integration tests
docker/              Dockerfile, docker-entrypoint.sh, compose.yaml, .env.example
scripts/install.sh   one-command Docker deployment
third_party/         telegram-bot-api submodule (source reference; not built by CI)
```

In-container sandbox layout:

```
/opt/tgdl-bot
├── tgdl-bot                 # main binary (self-contained single file)
├── api/telegram-bot-api     # local Bot API Server (--local :8081)
├── seed-bin/{yt-dlp,ffmpeg} # bundled in the image, seeded to the bin volume on first start
├── bin/{yt-dlp,ffmpeg}      # runtime-writable (/update self-updates, tgdl-bin volume)
├── api-data/                # telegram-bot-api data (tgdl-data volume)
└── config.conf              # generated from environment variables, or a mounted file
```

Modules are decoupled through interfaces (`IDownloader`, `ITelegramClient`, `IUpdater`, etc.), following SOLID principles.

## Quick Start (Docker)

### One command (any Linux distribution)

```bash
curl -fsSL https://raw.githubusercontent.com/linkcccp/tgdl-bot/main/scripts/install.sh | sudo bash
```

The script automatically: detects/installs Docker → runs an **interactive installation wizard**
(language selection + step-by-step input and validation of required settings; degrades to a
template mode when no TTY is available) → generates `/opt/tgdl-bot/.env` → pulls
`ghcr.io/linkcccp/tgdl-bot:latest` and starts the container.

### Manual

```bash
git clone --recurse-submodules https://github.com/linkcccp/tgdl-bot.git
cd tgdl-bot/docker
cp .env.example .env
$EDITOR .env        # fill in TGDL_BOT_TOKEN / TGDL_TARGET_CHANNELS / TGDL_ALLOWED_USERS / TGDL_API_ID / TGDL_API_HASH
docker compose up -d
docker logs -f tgdl-bot
```

## Configuration: Environment Variables (docker/.env)

| Variable | Required | Description |
| --- | --- | --- |
| `TGDL_BOT_TOKEN` | Yes | Bot token from @BotFather |
| `TGDL_TARGET_CHANNELS` | Yes | Target channel/group IDs, comma separated (negative allowed); push destination + group allowlist |
| `TGDL_ALLOWED_USERS` | Yes | Allowlisted user IDs for private chat, comma separated |
| `TGDL_API_ID` / `TGDL_API_HASH` | Yes | Local Bot API Server credentials (my.telegram.org); used only by tba |
| `TGDL_LOG_LEVEL` | No | Trace/Debug/Info/Warn/Error |
| `TGDL_MAX_CONCURRENT` | No | Max concurrent downloads, default 2 |
| `TGDL_DOWNLOAD_RETRIES` / `TGDL_UPLOAD_RETRIES` | No | Retry counts |
| `TGDL_DOWNLOAD_TIMEOUT` | No | Per-job timeout (seconds) |
| `TGDL_EXTRACT_AUDIO` | No | Extract audio as mp3 |
| `TGDL_SEND_TO_REQUESTER` | No | Whether the private-chat requester also receives the media |
| `TGDL_ALLOW_PRIVATE_URLS` | No | Allow private-network URLs (default no, SSRF protection) |
| `TGDL_ALLOW_PLAYLISTS` | No | Allow playlists |
| `TGDL_MERGE_FORMAT` | No | Merge container (`/`-separated candidates, default `mp4/mkv`; falls back to mkv when remuxing fails) |
| `TGDL_MAX_MEDIA_SIZE` | No | Max uploadable bytes (default close to 2GB) |
| `TGDL_UPDATE_YTDLP` / `TGDL_UPDATE_FFMPEG` | No | Whether to include the tool in /update |
| `TGDL_LANGUAGE` | No | Bot global default language: `auto` (follows user `language_code`, default) / `en` / `zh`; written automatically by the interactive wizard |
| `TGDL_STATE_DIR` | No | Runtime state directory, default `/opt/tgdl-bot/api-data` (persistent inside the tgdl-data volume) |

See [`docker/.env.example`](docker/.env.example) for the full example. To manage everything
with a file instead, mount a `config.conf` and set `TGDL_CONFIG_FILE=/opt/tgdl-bot/config.conf`
— in that case the required variables above are not needed (format reference:
[`docker/config.conf.example`](docker/config.conf.example)).

### State Files (StateDir, persistent in volume)

The following runtime state is stored under `StateDir` (container default
`/opt/tgdl-bot/api-data`, i.e. inside the `tgdl-data` volume — **survives image upgrades and
container recreation**):

| File | Contents |
| --- | --- |
| `languages.json` | Per-user explicit language choice (`/language`) |
| `config-overlay.json` | Config overrides from `/config` (take effect after restart) |
| `access-overlay.json` | Allowlist members added via `/access` |
| `pending-notify.json` | Pending restart notifications after config changes (deleted once sent) |

### How to Get Your User ID and Channel ID

- **user ID / chat ID / channel ID**: message [@userinfobot](https://t.me/userinfobot) and **tap the button options it offers** to get your own ID, group/channel IDs, etc. (easiest way).
- Alternatively: add the bot as a channel/group admin → post a message →
  `curl https://api.telegram.org/bot<TOKEN>/getUpdates` and look at `chat.id` (channels look like `-100...`).

## Routine Operations

```bash
docker ps                                   # status
docker logs -f tgdl-bot                     # logs
cd /opt/tgdl-bot && docker compose up -d    # restart after editing .env
```

Message the bot privately: `/update` to self-update yt-dlp/ffmpeg, `/status` for versions and
memory, `/language` to switch the interface language; `/config` and `/access` manage
configuration and the allowlist in-bot.

### Updating the Image (Upgrading)

```bash
cd /opt/tgdl-bot && sudo docker compose pull && sudo docker compose up -d && sudo docker image prune -f
```

- `pull` fetches the latest image → `up -d` recreates the container → `image prune -f` removes old images (prevents disk buildup)
- Only the image and container are updated; **`.env`, the `tgdl-data`/`tgdl-tmp`/`tgdl-bin` volumes, cookies, and download cache are all preserved**
- Or simply re-run the one-command installer (it performs pull/start/cleanup automatically):
  ```bash
  curl -fsSL https://raw.githubusercontent.com/linkcccp/tgdl-bot/main/scripts/install.sh | sudo bash
  ```

## Dealing with Bot Detection (Upload Cookies via the Bot)

Some sites (e.g. YouTube) require sign-in confirmation; the bot will **fail fast** and ask for
cookies (no more idling retries). Upload that site's cookies directly in private chat:

```text
/cookie youtube         → bot replies "send the cookies file"
(send cookies.txt)      → bot saves it and confirms
/cookies                → check per-site status
/cookie youtube clear   → delete that site's cookies
```

- **Auto-selected by domain**: uploaded cookies are assigned to their site; on download the bot picks the matching site's cookies by URL domain and passes them to yt-dlp
- Built-in sites: YouTube, X (Twitter), Instagram, TikTok, Twitch, Facebook, Bilibili, Douyin, Xiaohongshu, Weibo, SoundCloud, Vimeo, Dailymotion, Reddit
- One file per site, stored at `/opt/tgdl-bot/api-data/cookies` (inside the `tgdl-data` volume, persistent across image recreations; file mode 0600)
- Getting cookies.txt: log in on the site in your browser, export a Netscape-format file with an extension such as *Get cookies.txt LOCALLY*

### Optional Alternative Without Cookies

Datacenter IPs are often blocked by YouTube. Besides cookies, you can try:

```bash
# configure a proxy in docker/.env, or pass extra yt-dlp arguments
TGDL_YTDLP_PROXY=http://<residential/dedicated proxy>:port
TGDL_YTDLP_EXTRA_ARGS=--extractor-args youtube:player_client=android,ios
```

### Automatic Format Fallback

If the default format selection fails (`Requested format is not available`, common on YouTube
when multiple player_clients return inconsistent format lists), the bot automatically falls
back **in the background**: list available formats → pick **the highest-quality video + the
highest-quality audio** → re-download with `-f <videoID>+<audioID>` and **merge with ffmpeg** —
no manual intervention. YouTube uses multiple player_clients by default
(`TGDL_YTDLP_PLAYER_CLIENTS=android,ios,web_embedded,tv`; leave empty to disable).

> **JS runtime**: yt-dlp 2026.07.04+ needs a JavaScript runtime (deno) for full YouTube format
> extraction; without it the format list is incomplete and you get "Requested format is not
> available". **The Docker image bundles deno** (only inside the container sandbox, no host
> pollution); when running yt-dlp directly on a host machine, install deno yourself and add it
> to PATH.

## Download Modes (Video / Audio)

After a link is sent, the bot probes the content and picks the download method automatically:

- **Audio only** (e.g. song links): downloads that site's **highest-quality audio** and uploads **two copies** to the target channel:
  - `.flac` (lossless container, the best-quality copy)
  - `.mp3` (320k, for inline streaming in Telegram)
- **Contains video**: in private chat (allowlisted users), an inline button prompt appears:
  - 🎬 **Video+audio**: merged download (mp4/mkv, with automatic format fallback)
  - 🎵 **Audio only**: same flac+mp3 double upload as above
  - If no choice is made within 2 minutes, or when triggered from a channel/group, `TGDL_DEFAULT_MODE` is used (default `video`, changeable to `audio`)

## Local Development & Testing

```bash
dotnet restore && dotnet test
dotnet publish src/TGBot/TGBot.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/linux-x64
./publish/linux-x64/tgdl-bot --config ./config.conf --smoke-test 8    # local self-check (no network)
```

### Building the Docker Image

```bash
# First prepare docker/dist/ artifacts the way CI does (two sets, x64 and arm64):
#   dist/x64/tgdl-bot            (dotnet publish -r linux-x64)
#   dist/x64/telegram-bot-api    (prebuilt Release from the linkcccp/telegram-bot-api fork, official asset telegram-bot-api-linux-x64)
#   dist/x64/ffmpeg              (johnvansickle static build, official URL uses the amd64 name, i.e. x64)
#   dist/x64/yt-dlp              (official latest static build, official asset name yt-dlp, i.e. x64)
#   dist/arm64/...               (same layout for arm64: RID linux-arm64, tba asset -arm64,
#                                 ffmpeg arm64-static, yt-dlp_linux_aarch64)
# Local build (x64 example; TARGETARCH is the Docker standard injected value, DISTARCH matches the dist dir):
docker build --build-arg TARGETARCH=amd64 --build-arg DISTARCH=x64 -f docker/Dockerfile -t ghcr.io/linkcccp/tgdl-bot:dev docker/
```

## Release (GitHub Actions Builds and Pushes the Image Automatically)

Tagging `v*` triggers [`.github/workflows/release.yml`](.github/workflows/release.yml):
a matrix of two jobs builds **x64** (ubuntu-latest) and **arm64** (ubuntu-24.04-arm native runner) —
publish tgdl-bot (linux-x64 / linux-arm64) → download the prebuilt telegram-bot-api (architecture-specific
asset) from the **latest Release of the `linkcccp/telegram-bot-api` fork** → download the matching
yt-dlp/ffmpeg static builds (native execution check) → build the **linux/amd64 (i.e. x64) + linux/arm64**
images and push per-arch tags (`{ver}-x64` / `{ver}-arm64`) → a manifest job merges them into the
**multi-arch** `ghcr.io/linkcccp/tgdl-bot:{tag}` and `:latest` → create a Release.

```bash
git tag v2.0.0 && git push origin v2.0.0
```

> **Important**: In the repository **Settings → Actions → General → Workflow permissions**,
> select **Read and write permissions**, otherwise the Release/GHCR push will fail (the
> workflow explicitly declares `permissions: contents: write, packages: write`).

## API Documentation

```bash
dotnet tool install --global docfx
dotnet run --project tools/TgdlDocBuilder   # outputs to docs/, open docs/index.html; see --help for options
```

> On macOS/Linux with a custom dotnet installation (e.g. `$HOME/dotnet`), run
> `export DOTNET_ROOT=$HOME/dotnet` first (docfx needs the aspnetcore runtime).

## Known Limitations & Unverified Items

- **Architecture**: linux/amd64 (i.e. x64) and linux/arm64 are supported (multi-arch manifest;
  `docker pull` automatically fetches the image for the host architecture); armv7/32-bit is not
  supported. The arm64 build side is verified (native arm64 runner build + native execution check
  of assets), **real-device runtime testing on ARM hardware is pending**
- Files above the upload limit (~2GB) are fully downloaded before being rejected (direct-link sizes cannot be known in advance)
- URLs glued to adjacent Chinese text without a space cannot be reliably delimited; separate them with spaces
- The prebuilt telegram-bot-api binary depends on Releases of the fork (`linkcccp/telegram-bot-api`);
  the fork's `auto-sync` workflow periodically syncs upstream and rebuilds/releases
- **Unverified**: real Telegram Bot API interaction (needs a real token) cannot be verified on the dev machine; the full container chain (tba readiness / bot connection / memory) has been tested locally with Docker
- The repository is hosted at https://github.com/linkcccp/tgdl-bot (public). GitHub keeps
  only the `main` branch (major releases, merged from `dev`) plus tags; development branches
  (`dev`, `feat/*`, etc.) are local only

## Legal & Compliance

> This project is a download tool only. It does **not store, retrieve, or redistribute any
> media content**, and provides no content search service.

- **Personal lawful use only**: copyright laws vary widely by country/region; you are responsible
  for verifying legality in your jurisdiction. Do not use it for commercial redistribution,
  infringing copying, or other unlawful purposes.
- **Respect target-site terms**: when using it, comply with the target sites' **Terms of
  Service (ToS)** and robots directives; bypassing access controls (e.g. paywalls) may violate
  the law and site terms.
- **You assume infringement risk**: copyright and compliance risks arising from downloads are
  borne by the user. This project hosts no content; copyright holders may contact the project
  via Issues or [SECURITY.md](SECURITY.md) (there is no hosted content to remove; this
  statement is kept for process transparency).
- Reference: yt-dlp's legality note <https://github.com/yt-dlp/yt-dlp#legal>.

### Telegram Bot API Terms

- Bot developers/deployers must comply with the [Telegram Bot API Terms of Service](https://telegram.org/tos/bots)
  and the [Telegram Terms of Service](https://telegram.org/tos); this project is only a
  download tool — how the bot is deployed and used is the deployer's own responsibility.
- **Abuse risk warning**: high-frequency downloads may trigger Telegram risk controls
  (account/bot bans) or target-site IP bans; deployers assume those risks.
- Cookies uploaded via `/cookie` are used only to access content **the user is authorized**
  to access; they must not be used to bypass paywalls or access restricted content.