"""Offline catalog validation: never imports the SDK or requests OAuth tokens."""
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("chatgpt_oauth", Path(__file__).with_name("chatgpt-oauth.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CatalogTests(unittest.TestCase):
    def test_preserves_available_ids_and_capabilities(self):
        models = module.normalize_models({"models": [{
            "slug": "gpt-test-codex", "display_name": "GPT Test",
            "input_modalities": ["text", "image"],
            "supported_reasoning_levels": [{"effort": "low"}],
            "default_reasoning_level": "low",
        }]})
        self.assertEqual("gpt-test-codex", models[0]["id"])
        self.assertEqual(["text", "image"], models[0]["inputModalities"])
        self.assertEqual("low", models[0]["defaultReasoningLevel"])

    def test_skips_hidden_unsupported_or_unsafe_ids(self):
        models = module.normalize_models({"models": [
            {"slug": "hidden", "visibility": "hidden"},
            {"slug": "unsupported", "supported_in_api": False},
            {"slug": "bad\nmodel"}, {"slug": "../bad"},
            {"id": "valid-model"}, None,
        ]})
        self.assertEqual(["valid-model"], [model["id"] for model in models])

    def test_does_not_invent_models_on_empty_or_invalid_catalog(self):
        self.assertEqual([], module.normalize_models({"models": []}))
        with self.assertRaises(ValueError):
            module.normalize_models({"models": {"id": "bad"}})

    def test_login_persists_private_auth_without_printing_tokens(self):
        class FakeAuthenticator:
            def get_access_token(self):
                (Path(os.environ["CHATGPT_TOKEN_DIR"]) / "auth.json").write_text(json.dumps({"access_token": "test-access-value", "refresh_token": "test-refresh-value"}))
                return "test-access-value"

            def get_api_base(self):
                return "https://chatgpt.com/backend-api/codex"

            def get_account_id(self):
                return "test-account"

        fake_modules = {
            "litellm.llms.chatgpt.authenticator": types.SimpleNamespace(Authenticator=FakeAuthenticator),
            "litellm.llms.chatgpt.common_utils": types.SimpleNamespace(get_chatgpt_default_headers=lambda *_args: {"Authorization": "Bearer test-access-value"}),
        }
        with tempfile.TemporaryDirectory() as directory:
            output = io.StringIO()
            response = io.BytesIO(b'{"models":[{"slug":"test-model"}]}')
            with patch.dict(os.environ, {"CHATGPT_TOKEN_DIR": directory}), patch.dict(sys.modules, fake_modules), patch.object(sys, "argv", ["chatgpt-oauth.py"]), patch("urllib.request.urlopen", return_value=response) as fetch, patch("sys.stdout", output):
                module.main()
            self.assertEqual(0o700, Path(directory).stat().st_mode & 0o777)
            self.assertEqual(0o600, (Path(directory) / "auth.json").stat().st_mode & 0o777)
            self.assertEqual(0o600, (Path(directory) / "models.json").stat().st_mode & 0o777)
            self.assertNotIn("test-access-value", output.getvalue())
            self.assertNotIn("test-refresh-value", output.getvalue())
            self.assertIn("test-model", output.getvalue())
            self.assertTrue(fetch.call_args.args[0].full_url.startswith("https://chatgpt.com/backend-api/codex/models?"))

    def test_catalog_only_cannot_fall_back_to_device_login(self):
        class FakeAuthenticator:
            def get_access_token(self):
                return self._login_device_code()

            def _login_device_code(self):
                raise AssertionError("Interactive login must not be called")

        fake_modules = {
            "litellm.llms.chatgpt.authenticator": types.SimpleNamespace(Authenticator=FakeAuthenticator),
            "litellm.llms.chatgpt.common_utils": types.SimpleNamespace(get_chatgpt_default_headers=lambda *_args: {}),
        }
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "auth.json").write_text("{}")
            with patch.dict(os.environ, {"CHATGPT_TOKEN_DIR": directory}), patch.dict(sys.modules, fake_modules), patch.object(sys, "argv", ["chatgpt-oauth.py", "--catalog-only"]), patch("urllib.request.urlopen") as fetch:
                with self.assertRaises(SystemExit):
                    module.main()
                fetch.assert_not_called()


if __name__ == "__main__":
    unittest.main()
