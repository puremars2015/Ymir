"""Loaded as sitecustomize only in the OAuth-enabled proxy.

Pinned 1.103.2 can fall back to interactive device login after refresh failure.
Workers must instead fail closed and ask for an external re-login.
"""
import os

try:
    from litellm.llms.chatgpt.authenticator import Authenticator
    from litellm.llms.chatgpt.common_utils import GetAccessTokenError

    if not all(hasattr(Authenticator, name) for name in ("_login_device_code", "_wait_for_access_token")):
        raise RuntimeError("Unsupported pinned authenticator")

    def require_external_login(*_args, **_kwargs):
        raise GetAccessTokenError(status_code=401, message="ChatGPT OAuth requires re-login outside the proxy; run login-chatgpt.sh")

    Authenticator._login_device_code = require_external_login
    Authenticator._wait_for_access_token = require_external_login
except Exception:
    # Python normally ignores sitecustomize import failures; this credential guard must not.
    os.write(2, b"ChatGPT OAuth headless guard could not load; refusing proxy startup.\n")
    os._exit(78)
