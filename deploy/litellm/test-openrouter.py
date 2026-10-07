"""Verify the installed LiteLLM OpenRouter adapter against a local HTTP stub.

Run inside the pinned LiteLLM image with config.yaml as the first argument.
No provider credentials, external model requests or database writes are used.
"""
import json
import os
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import yaml
from litellm import Router

requests = []


class Upstream(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def do_POST(self):
        assert self.path == "/api/v1/chat/completions"
        assert self.headers["Authorization"] == "Bearer test-openrouter-key"
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        requests.append(body)
        self.send_response(200)
        if body.get("stream"):
            self.send_header("Content-Type", "text/event-stream")
            self.end_headers()
            for delta, finish in [({"role": "assistant", "content": "stream works"}, None), ({}, "stop")]:
                chunk = {"id": "test-stream", "object": "chat.completion.chunk", "created": 1,
                         "model": body["model"], "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]}
                self.wfile.write(("data: " + json.dumps(chunk) + "\n\n").encode())
            self.wfile.write(b"data: [DONE]\n\n")
        else:
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            message = {"role": "assistant", "content": "route works"}
            if body.get("tools"):
                message = {"role": "assistant", "content": None, "tool_calls": [{"id": "call-test", "type": "function", "function": {"name": "read", "arguments": '{"path":"README.md"}'}}]}
            response = {"id": "test", "object": "chat.completion", "created": 1,
                        "model": body["model"], "choices": [{"index": 0, "message": message,
                        "finish_reason": "tool_calls" if body.get("tools") else "stop"}],
                        "usage": {"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15}}
            self.wfile.write(json.dumps(response).encode())


with open(sys.argv[1], encoding="utf-8-sig") as source:
    config = yaml.safe_load(source)
expected = {
    "openrouter-sonnet-5.5": "anthropic/claude-sonnet-5.5",
    "openrouter-gpt-6.1-sol": "openai/gpt-6.1-sol",
    "openrouter-gpt-6-luna": "openai/gpt-6-luna",
}
entries = [item for item in config["model_list"] if item["model_name"] in expected]
assert len(entries) == len(expected)
for entry in entries:
    assert entry["litellm_params"]["api_key"] == "os.environ/OPENROUTER_API_KEY"
    assert entry["litellm_params"]["model"] == "openrouter/" + expected[entry["model_name"]]
    assert entry["litellm_params"]["api_base"] == "https://openrouter.ai/api/v1"
os.environ["OPENROUTER_API_KEY"] = "test-openrouter-key"
server = ThreadingHTTPServer(("127.0.0.1", 0), Upstream)
threading.Thread(target=server.serve_forever, daemon=True).start()
for entry in entries:
    entry["litellm_params"]["api_key"] = os.environ["OPENROUTER_API_KEY"]
    entry["litellm_params"]["api_base"] = f"http://127.0.0.1:{server.server_port}/api/v1"
router = Router(model_list=entries, num_retries=0)
try:
    messages = [{"role": "user", "content": "integration test"}]
    tool = {"type": "function", "function": {"name": "read", "parameters": {"type": "object", "properties": {"path": {"type": "string"}}, "required": ["path"]}}}
    for alias, model in expected.items():
        response = router.completion(model=alias, messages=messages)
        assert response.choices[0].message.content == "route works"
        response = router.completion(model=alias, messages=messages, tools=[tool])
        assert response.choices[0].message.tool_calls[0].function.name == "read"
        chunks = router.completion(model=alias, messages=messages, stream=True)
        assert "".join(chunk.choices[0].delta.content or "" for chunk in chunks if chunk.choices) == "stream works"
        assert all(request["model"] == model for request in requests[-3:])
        assert requests[-2]["tools"] == [tool]
        print(f"PASS {alias}: upstream model ID, credentials, text, tool calls and streaming")
    for alias in expected:
        for effort in ("low", "medium", "high"):
            router.completion(model=alias, messages=messages, reasoning_effort=effort)
            assert requests[-1].get("reasoning", {}).get("effort", requests[-1].get("reasoning_effort")) == effort
        router.completion(model=alias, messages=messages)
        assert "reasoning" not in requests[-1] and "reasoning_effort" not in requests[-1]
        print(f"PASS {alias}: reasoning efforts low/medium/high and provider default")
    assert len(requests) == 21
finally:
    server.shutdown()
    server.server_close()
