"""Append verified ChatGPT OAuth models, preserving existing API settings and aliases."""
import json
from pathlib import Path
import re
import sys

SELECTED = {
    "gpt-6.1-sol": "GPT-6.1 Sol (ChatGPT OAuth)",
    "gpt-6-luna": "GPT-6 Luna (ChatGPT OAuth)",
    "gpt-6-astra": "GPT-6 Astra (ChatGPT OAuth)",
}
EFFORTS = ["none", "minimal", "low", "medium", "high", "xhigh", "max"]


def append_models(text, catalog):
    verified = {model["id"]: model for model in catalog}
    if not set(SELECTED) <= set(verified):
        raise ValueError("Selected ChatGPT models are not in the authenticated catalog")
    lines = text.splitlines()
    model_ids = {}
    allowed = []
    for line in lines:
        match = re.fullmatch(r"VibeMaker__Models__(\d+)__Id=(.*)", line)
        if match:
            model_ids[int(match[1])] = match[2].strip().strip('\"\'')
        match = re.fullmatch(r"VibeMaker__LiteLlm__AllowedModels__\d+=(.*)", line)
        if match:
            allowed.append(match[1].strip().strip('\"\''))
    aliases = {"chatgpt-" + model_id for model_id in SELECTED}
    old_indices = {index for index, model_id in model_ids.items() if model_id in aliases}
    kept = []
    for line in lines:
        match = re.match(r"VibeMaker__Models__(\d+)__", line)
        if match and int(match[1]) in old_indices:
            continue
        if line.startswith("VibeMaker__LiteLlm__AllowedModels__"):
            continue
        kept.append(line)
    remaining = {index: model_id for index, model_id in model_ids.items() if index not in old_indices}
    index = max(remaining, default=-1) + 1
    if not allowed:
        allowed = list(remaining.values())
    for model_id, display in SELECTED.items():
        model = verified[model_id]
        alias = "chatgpt-" + model_id
        prefix = f"VibeMaker__Models__{index}__"
        images = "image" in (model.get("inputModalities") or [])
        levels = [entry["effort"] if isinstance(entry, dict) else entry for entry in (model.get("reasoningLevels") or [])]
        levels = [level for level in EFFORTS if level in levels]
        if not levels:
            raise ValueError("Missing supported effort metadata for " + model_id)
        default = model.get("defaultReasoningLevel")
        if default not in levels:
            raise ValueError("Unsupported default effort for " + model_id)
        kept.extend([prefix + "Id=" + alias, prefix + "DisplayName=" + display,
                     prefix + "SupportsImages=" + str(images).lower(),
                     prefix + "Thinking__Parameter=reasoning.effort",
                     prefix + "Thinking__DefaultLevel=" + default,
                     prefix + "Thinking__Required=false"])
        kept.extend(prefix + f"Thinking__Levels__{i}=" + level for i, level in enumerate(levels))
        if alias not in allowed:
            allowed.append(alias)
        index += 1
    kept.extend(f"VibeMaker__LiteLlm__AllowedModels__{i}=" + alias for i, alias in enumerate(dict.fromkeys(allowed)))
    return "\n".join(kept) + "\n"


if __name__ == "__main__":
    api = Path(sys.argv[1])
    catalog = json.loads(Path(sys.argv[2]).read_text())
    result = append_models(api.read_text(), catalog)
    api.write_text(result)
