"""Development-only: export golden cases from an existing KittenTTS checkout.

No third-party dependencies are needed. This script is not used by the .NET runtime.
"""
import argparse
import ast
import importlib.util
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
parser = argparse.ArgumentParser()
parser.add_argument("--upstream", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
path = args.upstream / "kittentts" / "preprocess.py"
spec = importlib.util.spec_from_file_location("reference_preprocess", path)
preprocess = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = preprocess
spec.loader.exec_module(preprocess)

examples = [
    "Smith et al. 2024, pp. 31-35", "Fig. 2",
    "Dr. Rivera paid $12.50 at 3:05 p.m.", "Jan. 2026", "version 2.4",
    "May 5, 2026", "10:30 AM", "$1,250.00", "9%", "v1.2.3",
    "Visit https://example.com or email hello@example.com.",
    "", "   ", "A first sentence. A second sentence!", "Hello, world!",
]
tree = ast.parse(path.read_text(encoding="utf-8"))
for node in ast.walk(tree):
    if isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == "cases" for t in node.targets):
        try:
            examples.extend(text for _, text in ast.literal_eval(node.value))
        except (ValueError, TypeError):
            pass
examples = list(dict.fromkeys(examples))

def export(fn):
    result = []
    for text in examples:
        try:
            expected = fn(text)
        except (ValueError, OverflowError):
            # Upstream normalize_text raises on certain numeric punctuation; do not freeze crashes as desired behavior.
            continue
        result.append({"input": text, "expected": expected})
    return result

model_tree = ast.parse((args.upstream / "kittentts" / "onnx_model.py").read_text(encoding="utf-8"))
definitions = [node for node in model_tree.body if isinstance(node, (ast.FunctionDef, ast.ClassDef)) and node.name in ("basic_english_tokenize", "TextCleaner")]
namespace = {}
exec(compile(ast.Module(body=definitions, type_ignores=[]), "reference_tokenizer", "exec"), namespace)
cleaner = namespace["TextCleaner"]()
tokenize = namespace["basic_english_tokenize"]
phonemes = ["həlˈoʊ wˈɜːld!", "kˈɪʔn̩ tˌiːtˌiːˈɛs", "ɑɐɒæɓʙβɔɕçɗɖðʤəɘɚɛɜɝ", "'̩'ᵻ \"«»—…", "unknown 😀 symbols", "", "a_b ² Ⅳ é ɹˈʌnɪŋ"]
fixtures = {
    "sourceRevision": "be5758500b731b8fc674acc62ea480d3022b7ebe",
    "normalization": export(preprocess.normalize_text),
    "preprocessing": export(preprocess.TextPreprocessor(remove_punctuation=False)),
    "chunking": export(preprocess.chunk_text),
    "tokenization": [{"input": value, "expected": [0] + cleaner(" ".join(tokenize(value))) + [10, 0]} for value in phonemes],
}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(fixtures, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print({key: len(value) for key, value in fixtures.items() if isinstance(value, list)})
