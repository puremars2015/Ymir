"""Pinned LiteLLM native ChatGPT bridge regression using fake OAuth and local SSE.

No real OAuth volume, provider keys, databases or upstream model calls are used.
Run inside the pinned image with chatgpt-models.yaml as the argument.
"""
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import yaml
from litellm import Router

requests = []


class Upstream(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        requests.append((self.path, body))
        assert self.headers["Authorization"] == "Bearer test-chatgpt-access"
        assert self.headers["ChatGPT-Account-Id"] == "test-chatgpt-account"
        assert self.path == "/responses", self.path
        assert body["stream"] is True and body["store"] is False
        assert "Ymir bridge test" in body["instructions"]
        assert "based on GPT-5" not in body["instructions"]
        assert not any(key in body for key in ("max_tokens", "max_output_tokens", "max_completion_tokens", "metadata"))
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.end_headers()

        def event(kind, **values):
            self.wfile.write(("data: " + json.dumps({"type": kind, **values}) + "\n\n").encode())
            self.wfile.flush()

        response = {"id": "resp_test", "object": "response", "created_at": 1,
                    "status": "in_progress", "model": body["model"], "output": [],
                    "parallel_tool_calls": False, "tool_choice": "auto", "tools": [],
                    "usage": {"input_tokens": 8, "output_tokens": 4, "total_tokens": 12}}
        event("response.created", response=response)
        if body.get("tools"):
            item = {"type": "function_call", "id": "fc_test", "call_id": "call_test",
                    "name": "read", "arguments": "", "status": "in_progress"}
            event("response.output_item.added", output_index=0, item=item)
            arguments = '{"path":"README.md"}'
            event("response.function_call_arguments.delta", item_id="fc_test", output_index=0, delta=arguments)
            item = {**item, "arguments": arguments, "status": "completed"}
            event("response.function_call_arguments.done", item_id="fc_test", output_index=0, arguments=arguments)
        else:
            item = {"type": "message", "id": "msg_test", "role": "assistant", "content": [], "status": "in_progress"}
            event("response.output_item.added", output_index=0, item=item)
            event("response.content_part.added", item_id="msg_test", output_index=0, content_index=0,
                  part={"type": "output_text", "text": "", "annotations": []})
            event("response.output_text.delta", item_id="msg_test", output_index=0, content_index=0, delta="route works")
            event("response.output_text.done", item_id="msg_test", output_index=0, content_index=0, text="route works")
            item = {**item, "content": [{"type": "output_text", "text": "route works", "annotations": []}], "status": "completed"}
        event("response.output_item.done", output_index=0, item=item)
        event("response.completed", response={**response, "status": "completed", "output": [item]})


with open(sys.argv[1]) as source:
    config = yaml.safe_load(source)
expected = {"chatgpt-gpt-6.1-sol": "gpt-6.1-sol", "chatgpt-gpt-6-luna": "gpt-6-luna", "chatgpt-gpt-6-astra": "gpt-6-astra"}
entries = config["model_list"]
assert {entry["model_name"] for entry in entries} == set(expected)
for entry in entries:
    assert entry["litellm_params"]["model"] == "chatgpt/" + expected[entry["model_name"]]
    assert "api_key" not in entry["litellm_params"]
    assert entry["model_info"]["mode"] == "responses"
    assert entry["litellm_params"]["rpm"] == 6
    assert entry["litellm_params"]["tpm"] == 64000
    # Functional probes exceed the production RPM; keep checks enabled with test limits.
    entry["litellm_params"]["rpm"] = 1000
    entry["litellm_params"]["tpm"] = 1000000

server = ThreadingHTTPServer(("127.0.0.1", 0), Upstream)
threading.Thread(target=server.serve_forever, daemon=True).start()
with tempfile.TemporaryDirectory() as directory:
    Path(directory, "auth.json").write_text(json.dumps({
        "access_token": "test-chatgpt-access", "refresh_token": "test-refresh-unused",
        "account_id": "test-chatgpt-account", "expires_at": time.time() + 3600,
    }))
    os.environ["CHATGPT_TOKEN_DIR"] = directory
    os.environ["CHATGPT_API_BASE"] = f"http://127.0.0.1:{server.server_port}"
    os.environ["CHATGPT_DEFAULT_INSTRUCTIONS"] = "Ymir bridge test: follow supplied tool definitions, do not assume a model identity"
    router = Router(model_list=entries, num_retries=0, enable_pre_call_checks=True, routing_strategy="usage-based-routing-v2")
    messages = [{"role": "user", "content": "local bridge test"}]
    tool = {"type": "function", "function": {"name": "read", "parameters": {"type": "object", "properties": {"path": {"type": "string"}}, "required": ["path"]}}}
    try:
        for alias, model in expected.items():
            response = router.completion(model=alias, messages=messages, max_tokens=32)
            assert response.choices[0].message.content == "route works"
            response = router.completion(model=alias, messages=messages, tools=[tool])
            assert response.choices[0].message.tool_calls[0].function.name == "read"
            chunks = router.completion(model=alias, messages=messages, stream=True)
            assert "".join(chunk.choices[0].delta.content or "" for chunk in chunks if chunk.choices) == "route works"
            assert all(body["model"] == model for _, body in requests[-3:])
            for effort in ["low", "medium", "high", "xhigh", "max"]:
                # Pinned Chat Completions validator does not recognize new xhigh/max IDs.
                # Native Responses supports the exact catalog effort via reasoning.effort.
                router.completion(model=alias, messages=messages, reasoning={"effort": effort})
                assert requests[-1][1]["reasoning"]["effort"] == effort
            print(f"PASS {alias}: OAuth identity, Responses bridge, text/tools/stream, exact model and reasoning")
        assert len(requests) == 24
        limited = Router(model_list=[{
            "model_name": "chatgpt-rate-test", "model_info": {"mode": "responses"},
            "litellm_params": {"model": "chatgpt/gpt-6.1-sol", "rpm": 1, "tpm": 64000},
        }], num_retries=0, enable_pre_call_checks=True, routing_strategy="usage-based-routing-v2")
        limited.completion(model="chatgpt-rate-test", messages=messages)
        sent = len(requests)
        try:
            limited.completion(model="chatgpt-rate-test", messages=messages)
        except Exception as error:
            assert any(word in str(error).lower() for word in ("rpm", "rate", "429", "no deployments")), str(error)
            assert len(requests) == sent
        else:
            raise AssertionError("RPM limit did not block the second upstream request")
        print("PASS OAuth RPM pre-call guard: over-limit request never reaches upstream")
    finally:
        server.shutdown()
        server.server_close()
