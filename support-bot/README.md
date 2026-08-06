# Legacy Crossplay Discord support bot

This bot diagnoses technical setup, build, relay and crossplay failures from the project documentation and supplied logs. It supports `/ask`, direct mentions and optional automatic replies in selected support channels.

For mention-based support, it includes up to five recent messages from the same user so multi-message reports remain understandable. When AI mode is enabled, that question context and supported text attachments are sent to the OpenAI API after common secret patterns are redacted. Keep `AUTO_REPLY=false` unless server members have been told how the support bot processes their messages.

## What it can diagnose

- incomplete Xbox 360 platform media such as missing `XboxMedia/strings.h`
- physical consoles incorrectly compiled with `127.0.0.1`
- relay bind-address mistakes
- session/build/protocol mismatches
- Xenia profile state causing an endless join spinner
- endless joining screens and missing host/join handshakes
- common PC/Xenia/RPCS3 build and runtime evidence

It does not provide Minecraft files, proprietary source trees, console SDKs, keys, firmware or license bypasses.

## Discord setup

1. Create an application in the [Discord Developer Portal](https://discord.com/developers/applications).
2. Open **Bot**, create the bot user and reset/copy its token.
3. Enable **Message Content Intent** if you want mention or automatic channel replies. `/ask` works through application commands.
4. Under **OAuth2 > URL Generator**, select `bot` and `applications.commands`.
5. Give the bot only **View Channels**, **Send Messages**, **Read Message History**, **Embed Links**, and **Attach Files** where needed.
6. Invite it to the server. Never post the bot token in Discord or commit it to Git.

## Local setup

```powershell
cd support-bot
py -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
Copy-Item .env.example .env
```

Edit `.env` and set `DISCORD_TOKEN` and `OPENAI_API_KEY`. Set `DISCORD_GUILD_ID` during testing so `/ask` updates immediately. Leave `AUTO_REPLY=false` initially; the bot will answer `/ask` and direct mentions without monitoring every support-channel message.

Run:

```powershell
python bot.py
```

## VPS with Docker

```bash
cd support-bot
cp .env.example .env
nano .env
docker compose up -d --build
docker compose logs -f legacy-support-bot
```

The Discord and OpenAI tokens stay in `support-bot/.env`, which is ignored by Git. Rotate either token immediately if it is exposed.

## Test deterministic diagnostics

```powershell
cd support-bot
python -m unittest -v test_diagnostics.py
```
