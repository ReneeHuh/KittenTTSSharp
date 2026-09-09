"""Development-only native parity data. Requires upstream's Python dependencies.

Export using espeakng-loader 0.2.4 and phonemizer 3.4.0 to reproduce the checked-in fixture.
"""
import argparse
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
parser = argparse.ArgumentParser()
parser.add_argument("--upstream", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--model-dir", type=Path)
args = parser.parse_args()
sys.path.insert(0, str(args.upstream.resolve()))

from kittentts.onnx_model import KittenTTS_1_Onnx, TextCleaner, basic_english_tokenize
from phonemizer.backend import EspeakBackend
from phonemizer.backend.espeak.wrapper import EspeakWrapper
import espeakng_loader
import numpy as np

# Point explicitly at the bundled dictionaries, independent of machine-wide settings.
EspeakWrapper.set_data_path(espeakng_loader.get_data_path())
phonemizer = EspeakBackend(language="en-us", preserve_punctuation=True, with_stress=True)
cleaner = TextCleaner()
texts = [
    "Hello, world.", "This is Kitten TTS running in dot net.",
    "Dr. Rivera paid $12.50 at 3:05 p.m.", "I can't believe it's already 2026!",
    "A (small) kitten—really?!", "one two three", "version 1.2.3 and 1,250 dollars",
    "  leading and trailing spaces  ", "Café, naïve résumé.", "Read https://example.com.",
    "First line\nsecond line.", "hello...world", "emoji 😀 kitten",
    "!!!", "A semi; colon: and \"quotes\".", "\"word\"!", "the 3rd runner", "co-operate",
]
cases = []
for text in texts:
    phonemes = phonemizer.phonemize([text])[0]
    ids = [0] + cleaner(" ".join(basic_english_tokenize(phonemes))) + [10, 0]
    cases.append({"input": text, "phonemes": phonemes, "tokens": ids})
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps({"espeakngLoader": "0.2.4", "phonemizer": "3.4.0", "cases": cases}, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"Exported {len(cases)} phonemizer cases")

if args.model_dir:
    config = json.loads((args.model_dir / "config.json").read_text())
    model = KittenTTS_1_Onnx(str(args.model_dir / config["model_file"]), str(args.model_dir / config["voices"]),
        config.get("speed_priors", {}), config.get("voice_aliases", {}), backend="cpu")
    text = "Hello, world."
    inputs = model._prepare_inputs(text, "Jasper", 1.0)
    audio = model.generate_single_chunk(text, "Jasper", 1.0).flatten()
    result = {
        "input": text, "voice": "Jasper", "speed": 1.0,
        "inputIds": inputs["input_ids"].flatten().tolist(),
        "style": inputs["style"].flatten().tolist(), "effectiveSpeed": float(inputs["speed"][0]),
        "sampleCount": len(audio), "rms": float(np.sqrt(np.mean(audio ** 2))),
    }
    destination = args.output.parent / "python-inference.json"
    destination.write_text(json.dumps(result, indent=2) + "\n")
    np.save(args.output.parent / "python-audio.npy", audio)
    print({key: result[key] for key in ("sampleCount", "rms")})
