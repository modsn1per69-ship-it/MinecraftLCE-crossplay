import unittest

from bot import redact_secrets, split_message


class BotUtilityTests(unittest.TestCase):
    def test_common_secrets_are_redacted(self):
        text = "access_token=secret123 OPENAI_API_KEY=sk-example1234567890 password -> hunter2"
        redacted = redact_secrets(text)
        self.assertNotIn("secret123", redacted)
        self.assertNotIn("sk-example1234567890", redacted)
        self.assertNotIn("hunter2", redacted)
        self.assertGreaterEqual(redacted.count("[REDACTED]"), 3)

    def test_long_discord_response_is_split(self):
        chunks = split_message(("technical diagnosis " * 300).strip())
        self.assertGreater(len(chunks), 1)
        self.assertTrue(all(0 < len(chunk) <= 1900 for chunk in chunks))
        self.assertEqual(" ".join(chunks), ("technical diagnosis " * 300).strip())


if __name__ == "__main__":
    unittest.main()
