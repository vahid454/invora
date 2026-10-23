"""Tests for the safe Backblaze connection diagnostics."""

import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch
from urllib.error import HTTPError


spec = importlib.util.spec_from_file_location(
    "configure_backblaze", Path(__file__).with_name("configure-backblaze.py")
)
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


class ConnectionDiagnosticsTests(unittest.TestCase):
    def test_rejected_pair_does_not_repeat_credentials(self):
        error = HTTPError("https://api.backblazeb2.com", 401, "Unauthorized", {}, None)
        with patch.object(helper, "urlopen", side_effect=error):
            message = helper.authorization_hint("example-key-id", "example-private-key", "invora")
        self.assertIn("rejected", message)
        self.assertNotIn("example-key-id", message)
        self.assertNotIn("example-private-key", message)

    def test_accepted_pair_with_required_capabilities(self):
        response = io.BytesIO(b'{"apiInfo":{"storageApi":{"allowed":{"buckets":[{"name":"invora"}],"capabilities":["listFiles","readFiles","writeFiles"]}}}}')
        with patch.object(helper, "urlopen", return_value=response):
            self.assertIsNone(helper.authorization_hint("id", "private", "invora"))

    def test_accepted_pair_restricted_to_other_bucket(self):
        response = io.BytesIO(b'{"apiInfo":{"storageApi":{"allowed":{"buckets":[{"name":"other"}],"capabilities":["listFiles","readFiles","writeFiles"]}}}}')
        with patch.object(helper, "urlopen", return_value=response):
            self.assertIn("another bucket", helper.authorization_hint("id", "private", "invora"))

    def test_bucket_listing_unauthorized_is_not_called_bad_key_pair(self):
        message = helper.connection_hint("failed to list bucket: 401 unauthorized")
        self.assertIn("Authentication succeeded", message)

    def test_b2_config_keeps_verbatim_key_for_backend(self):
        self.assertIn(
            "key = example-private-key\n",
            helper.config_section("invora-b2", "example-key-id", "example-private-key"),
        )


if __name__ == "__main__":
    unittest.main()
