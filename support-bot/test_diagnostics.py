import unittest

from diagnostics import diagnose


class DiagnosticTests(unittest.TestCase):
    def test_physical_xbox_loopback_is_detected(self):
        text = """
        Physical Xbox 360 infinitely loads.
        handshake role=hosting session=local-test remote=127.0.0.1
        handshake role=joining session=local-test remote=127.0.0.1
        """
        codes = {item.code for item in diagnose(text)}
        self.assertIn("physical-console-loopback", codes)
        self.assertIn("join-stall", codes)

    def test_missing_xbox_media_header_is_detected(self):
        codes = {item.code for item in diagnose("fatal error: XboxMedia/strings.h: No such file or directory")}
        self.assertIn("missing-xbox-media", codes)

    def test_session_mismatch_is_detected(self):
        text = "session=local test build=x\nsession=local testing build=x"
        codes = {item.code for item in diagnose(text)}
        self.assertIn("session-mismatch", codes)

    def test_reported_physical_xbox_case(self):
        text = """
        I started the relay on PC but my real Xbox 360 infinitely loads.
        listening 0.0.0.0:61000
        handshake role=hosting session=local test build=584111F7-1.0.10.0-lce1.2.3-net495-proto39 protocol=V2 remote=127.0.0.1
        host reader stopped session=local testing error=connection closed
        """
        codes = {item.code for item in diagnose(text)}
        self.assertEqual(
            {"physical-console-loopback", "session-mismatch", "join-stall", "bind-address-note"},
            codes,
        )

    def test_clean_question_has_no_false_positive(self):
        self.assertEqual([], diagnose("How do I start the patcher?"))


if __name__ == "__main__":
    unittest.main()
