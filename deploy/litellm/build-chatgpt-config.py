"""Merge opt-in subscription routes into the existing LiteLLM YAML, without tokens."""
from pathlib import Path
import sys

import yaml

base = yaml.safe_load(Path(sys.argv[1]).read_text())
addon = yaml.safe_load(Path(sys.argv[2]).read_text())
old_names = {entry["model_name"] for entry in base["model_list"]}
new_names = {entry["model_name"] for entry in addon["model_list"]}
if old_names & new_names or len(new_names) != len(addon["model_list"]):
    raise SystemExit("Duplicate model alias; refusing to replace existing routes")
base["model_list"].extend(addon["model_list"])
base.setdefault("router_settings", {}).update({
    "enable_pre_call_checks": True,
    "routing_strategy": "usage-based-routing-v2",
    "num_retries": 0,
})
sys.stdout.write(yaml.safe_dump(base, allow_unicode=True, sort_keys=False))
