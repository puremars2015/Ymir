"""Interactive ChatGPT subscription login and read-only model discovery.

Run in the pinned LiteLLM image, outside proxy workers. Never print tokens.
The auth directory must be a private, persistent, writable volume.
"""
import argparse
import json
import os
from pathlib import Path
import re
import urllib.error
import urllib.request


def normalize_models(payload):
    entries = payload.get("models", payload.get("data", []))
    if not isinstance(entries, list):
        raise ValueError("Unexpected ChatGPT model catalog format")
    models = []
    for entry in entries:
        if not isinstance(entry, dict):
            continue
        model_id = entry.get("slug") or entry.get("id")
        if not isinstance(model_id, str) or not re.fullmatch(r"[A-Za-z0-9_.:-]+", model_id):
            continue
        if entry.get("visibility") == "hidden" or entry.get("supported_in_api") is False:
            continue
        models.append({
            "id": model_id,
            "displayName": entry.get("display_name") or entry.get("name") or model_id,
            "inputModalities": entry.get("input_modalities"),
            "reasoningLevels": entry.get("supported_reasoning_levels"),
            "defaultReasoningLevel": entry.get("default_reasoning_level"),
        })
    return models


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog-only", action="store_true")
    args = parser.parse_args()
    directory = Path(os.environ.get("CHATGPT_TOKEN_DIR", "/var/lib/litellm/chatgpt"))
    os.environ["CHATGPT_TOKEN_DIR"] = str(directory)
    directory.mkdir(parents=True, exist_ok=True)
    directory.chmod(0o700)
    auth_file = directory / "auth.json"
    if args.catalog_only and not auth_file.is_file():
        raise SystemExit("OAuth 尚未登入；不會在 catalog-only 模式啟動登入流程。")

    from litellm.llms.chatgpt.authenticator import Authenticator
    from litellm.llms.chatgpt.common_utils import get_chatgpt_default_headers

    authenticator = Authenticator()
    if args.catalog_only:
        # Pinned SDK 在 refresh 失敗後會退回 device login；目錄查詢不可偷偷啟動互動流程。
        def deny_interactive_login(*_args, **_kwargs):
            raise RuntimeError("Re-login required")
        authenticator._login_device_code = deny_interactive_login
        authenticator._wait_for_access_token = deny_interactive_login
    # SDK 僅印官方網址與短效 device code；回傳 token 不可 print／記錄。
    try:
        access_token = authenticator.get_access_token()
    except Exception:
        raise SystemExit("ChatGPT OAuth 未完成或需重新登入；請在官方頁面確認授權後重試互動登入。") from None
    if not auth_file.is_file():
        raise SystemExit("OAuth 未產生持久 token 檔案，未接入模型服務。")
    auth_file.chmod(0o600)
    print("ChatGPT OAuth 已登入；token 僅保存在私有持久 volume。", flush=True)

    base = authenticator.get_api_base().rstrip("/")
    if base != "https://chatgpt.com/backend-api/codex":
        raise SystemExit("拒絕將 OAuth token 傳往非官方 ChatGPT backend。")
    headers = get_chatgpt_default_headers(access_token, authenticator.get_account_id())
    headers["accept"] = "application/json"
    # 僅查詢可用模型，不呼叫 Responses 或 Chat Completions。
    request = urllib.request.Request(base + "/models?client_version=0.89.0", headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            models = normalize_models(json.load(response))
    except urllib.error.HTTPError as error:
        raise SystemExit(f"OAuth 已保存，但模型目錄查詢 HTTP {error.code}；未新增路由。") from None
    except (urllib.error.URLError, ValueError, KeyError):
        raise SystemExit("OAuth 已保存，但模型目錄無法讀取；未新增路由，不推測模型 ID。") from None
    catalog = directory / "models.json"
    catalog.write_text(json.dumps(models, ensure_ascii=False, indent=2) + "\n")
    catalog.chmod(0o600)
    if not models:
        raise SystemExit("未取得可用模型，未新增路由。")
    print("可用模型（請告訴 AI 要接入哪些 ID）：", flush=True)
    for model in models:
        print(f"  {model['id']} — {model['displayName']}", flush=True)
    print("登入／目錄查詢完成；現有 LiteLLM 與 Ymir 尚未重啟或更動。", flush=True)


if __name__ == "__main__":
    main()
