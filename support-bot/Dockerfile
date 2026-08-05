FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

WORKDIR /app
COPY support-bot/requirements.txt /app/support-bot/requirements.txt
RUN pip install --no-cache-dir -r /app/support-bot/requirements.txt

COPY README.md CHANGELOG.md /app/
COPY docs /app/docs
COPY support-bot /app/support-bot

WORKDIR /app/support-bot
CMD ["python", "bot.py"]

