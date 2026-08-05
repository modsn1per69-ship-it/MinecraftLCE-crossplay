from __future__ import annotations

import asyncio
import logging
import os
import re
import time
from pathlib import Path

import discord
from discord import app_commands
from discord.ext import commands
from dotenv import load_dotenv
from openai import OpenAI

from diagnostics import diagnose, format_diagnostics


ROOT = Path(__file__).resolve().parent.parent
BOT_ROOT = Path(__file__).resolve().parent
load_dotenv(BOT_ROOT / ".env")

DISCORD_TOKEN = os.environ.get("DISCORD_TOKEN", "").strip()
OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY", "").strip()
OPENAI_MODEL = os.environ.get("OPENAI_MODEL", "gpt-5.6").strip()
GUILD_ID = int(os.environ["DISCORD_GUILD_ID"]) if os.environ.get("DISCORD_GUILD_ID") else None
SUPPORT_CHANNEL_IDS = {
    int(value.strip())
    for value in os.environ.get("SUPPORT_CHANNEL_IDS", "").split(",")
    if value.strip().isdigit()
}
AUTO_REPLY = os.environ.get("AUTO_REPLY", "false").lower() in {"1", "true", "yes", "on"}
COOLDOWN_SECONDS = max(5, int(os.environ.get("USER_COOLDOWN_SECONDS", "20")))
MAX_ATTACHMENT_BYTES = max(4096, int(os.environ.get("MAX_ATTACHMENT_BYTES", "262144")))

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
log = logging.getLogger("legacy-support-bot")


def load_knowledge() -> str:
    paths = [
        ROOT / "README.md",
        ROOT / "docs" / "PATCHER.md",
        ROOT / "docs" / "ARCHITECTURE.md",
        ROOT / "docs" / "LEGAL.md",
        ROOT / "CHANGELOG.md",
        BOT_ROOT / "knowledge" / "technical-support.md",
    ]
    sections: list[str] = []
    for path in paths:
        if path.is_file():
            sections.append(f"\n--- {path.relative_to(ROOT)} ---\n{path.read_text(encoding='utf-8', errors='replace')}")
    return "".join(sections)


KNOWLEDGE = load_knowledge()
SYSTEM_INSTRUCTIONS = """
You are the technical support bot for Minecraft Legacy Console Crossplay.

Answer only from the supplied project knowledge, deterministic diagnostics, and evidence in the user's message. Never invent a successful build, file, protocol, download, or test result.

For technical failures:
1. Start with the strongest observation from the logs or error text.
2. State the most likely cause with calibrated language.
3. Give a short ordered checklist of concrete checks.
4. Ask for only the smallest missing evidence needed for the next diagnosis.
5. Keep the answer under 1,700 characters unless the user explicitly asks for a detailed guide.

Always distinguish PC, Xbox 360/Xenia, and PS3/RPCS3. Distinguish emulator clients from physical consoles. A physical console cannot connect to 127.0.0.1 on the relay PC. 0.0.0.0 is a listener bind address, not a client destination.

Never provide or locate copyrighted game binaries, game assets, complete proprietary source trees, console SDKs, firmware, keys, certificates, entitlement bypasses, or license bypasses. You may help users patch and compile their own legally obtained matching source and dumps.

Do not repeat access tokens, passwords, API keys, or other secrets found in logs. Tell the user to rotate exposed secrets. Local IPv4 addresses are acceptable for network diagnosis.

Use clear Discord markdown. Do not insult the user. Do not tell them merely to 'follow the guide' when a specific technical error was supplied.
""".strip()


def redact_secrets(text: str) -> str:
    redacted = text
    substitutions = (
        (r"(?i)(access[_ -]?token\s*[=:]\s*)\S+", r"\1[REDACTED]"),
        (r"(?i)(discord[_ -]?token\s*[=:]\s*)\S+", r"\1[REDACTED]"),
        (r"(?i)(openai[_ -]?api[_ -]?key\s*[=:]\s*)\S+", r"\1[REDACTED]"),
        (r"(?i)(password\s*(?:->|[=:])\s*)\S+", r"\1[REDACTED]"),
        (r"\bsk-[A-Za-z0-9_-]{16,}\b", "[REDACTED]"),
    )
    for pattern, replacement in substitutions:
        redacted = re.sub(pattern, replacement, redacted)
    return redacted


def split_message(text: str, limit: int = 1900) -> list[str]:
    text = text.strip()
    if len(text) <= limit:
        return [text]
    chunks: list[str] = []
    remaining = text
    while remaining:
        if len(remaining) <= limit:
            chunks.append(remaining)
            break
        cut = remaining.rfind("\n", 0, limit)
        if cut < limit // 2:
            cut = remaining.rfind(" ", 0, limit)
        if cut < limit // 2:
            cut = limit
        chunks.append(remaining[:cut].rstrip())
        remaining = remaining[cut:].lstrip()
    return chunks


def fallback_answer(question: str) -> str:
    findings = diagnose(question)
    if findings:
        return "**Automatic diagnosis**\n" + format_diagnostics(findings)
    return (
        "I need a little more evidence to diagnose this safely. Please send:\n"
        "1. Platform and whether it is an emulator or physical console\n"
        "2. Who hosts the world and who joins\n"
        "3. Exact build/version\n"
        "4. Relay log lines from immediately before and after the failed join\n"
        "5. The exact compiler/runtime error or a screenshot\n\n"
        "Redact passwords and access tokens."
    )


def answer_with_ai(question: str) -> str:
    if not OPENAI_API_KEY:
        return fallback_answer(question)

    client = OpenAI(api_key=OPENAI_API_KEY, timeout=45.0)
    response = client.responses.create(
        model=OPENAI_MODEL,
        reasoning={"effort": "low"},
        max_output_tokens=1400,
        instructions=SYSTEM_INSTRUCTIONS,
        input=(
            f"PROJECT KNOWLEDGE:\n{KNOWLEDGE}\n\n"
            f"DETERMINISTIC DIAGNOSTICS:\n{format_diagnostics(diagnose(question))}\n\n"
            f"USER QUESTION AND LOGS:\n{redact_secrets(question)}"
        ),
    )
    return response.output_text.strip() or fallback_answer(question)


async def read_text_attachments(attachments: list[discord.Attachment]) -> str:
    blocks: list[str] = []
    for attachment in attachments[:3]:
        suffix = Path(attachment.filename).suffix.lower()
        if suffix not in {".txt", ".log", ".md"}:
            continue
        if attachment.size > MAX_ATTACHMENT_BYTES:
            blocks.append(f"[{attachment.filename}: skipped because it exceeds {MAX_ATTACHMENT_BYTES} bytes]")
            continue
        raw = await attachment.read()
        blocks.append(f"\n--- attachment: {attachment.filename} ---\n{raw.decode('utf-8', errors='replace')}")
    return "".join(blocks)


async def collect_message_context(message: discord.Message) -> str:
    prior: list[str] = []
    try:
        async for item in message.channel.history(limit=12, before=message, oldest_first=False):
            if item.author.id != message.author.id or not item.content.strip():
                continue
            prior.append(item.content.strip())
            if len(prior) == 5:
                break
    except (discord.Forbidden, discord.HTTPException, AttributeError):
        pass

    prior.reverse()
    content = message.content
    if bot.user is not None:
        content = content.replace(f"<@{bot.user.id}>", "").replace(f"<@!{bot.user.id}>", "")
    parts = [f"Earlier message from the same user: {item}" for item in prior]
    parts.append(f"Current message: {content.strip()}")
    parts.append(await read_text_attachments(message.attachments))
    return "\n".join(part for part in parts if part).strip()[-30000:]


intents = discord.Intents.default()
intents.message_content = True
bot = commands.Bot(command_prefix="!legacy ", intents=intents)
cooldowns: dict[int, float] = {}


async def generate_answer(question: str) -> str:
    safe_question = redact_secrets(question)[:30000]
    try:
        return await asyncio.to_thread(answer_with_ai, safe_question)
    except Exception as exc:
        log.exception("Support answer failed")
        return fallback_answer(safe_question) + f"\n\nThe AI service was unavailable ({type(exc).__name__})."


@bot.event
async def on_ready() -> None:
    if not getattr(bot, "_commands_synced", False):
        if GUILD_ID:
            guild = discord.Object(id=GUILD_ID)
            bot.tree.copy_global_to(guild=guild)
            await bot.tree.sync(guild=guild)
        else:
            await bot.tree.sync()
        bot._commands_synced = True
    log.info("Logged in as %s; model=%s; AI=%s", bot.user, OPENAI_MODEL, bool(OPENAI_API_KEY))


@bot.tree.command(name="ask", description="Ask a Legacy Crossplay technical support question")
@app_commands.describe(question="Include the platform, topology, exact error, and relevant relay lines")
async def ask(interaction: discord.Interaction, question: str) -> None:
    await interaction.response.defer(thinking=True)
    chunks = split_message(await generate_answer(question))
    await interaction.followup.send(chunks[0], allowed_mentions=discord.AllowedMentions.none())
    for chunk in chunks[1:]:
        await interaction.followup.send(chunk, allowed_mentions=discord.AllowedMentions.none())


@bot.event
async def on_message(message: discord.Message) -> None:
    if message.author.bot or bot.user is None:
        return

    mentioned = bot.user.mentioned_in(message)
    configured_channel = AUTO_REPLY and (not SUPPORT_CHANNEL_IDS or message.channel.id in SUPPORT_CHANNEL_IDS)
    looks_like_question = "?" in message.content or any(
        term in message.content.lower()
        for term in ("error", "failed", "can't", "cant", "crash", "stuck", "loading", "missing", "doesn't work", "wont connect")
    )
    if not mentioned and not (configured_channel and looks_like_question):
        await bot.process_commands(message)
        return

    now = time.monotonic()
    if now - cooldowns.get(message.author.id, 0.0) < COOLDOWN_SECONDS:
        return
    cooldowns[message.author.id] = now

    question = await collect_message_context(message)
    if not question:
        await message.reply("Send the platform, topology, exact error, and relevant log lines.", mention_author=False)
        return

    async with message.channel.typing():
        chunks = split_message(await generate_answer(question))
    await message.reply(chunks[0], mention_author=False, allowed_mentions=discord.AllowedMentions.none())
    for chunk in chunks[1:]:
        await message.channel.send(chunk, allowed_mentions=discord.AllowedMentions.none())

    await bot.process_commands(message)


def main() -> None:
    if not DISCORD_TOKEN:
        raise SystemExit("DISCORD_TOKEN is missing. Copy .env.example to .env and add the bot token.")
    bot.run(DISCORD_TOKEN, log_handler=None)


if __name__ == "__main__":
    main()
