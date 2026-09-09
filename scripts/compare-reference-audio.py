"""Compare a Python float32 NPY waveform with a .NET PCM WAV (development only)."""
import argparse
import json
import numpy as np
import soundfile as sf

parser = argparse.ArgumentParser()
parser.add_argument("reference")
parser.add_argument("actual")
args = parser.parse_args()
reference = np.load(args.reference).flatten()
actual, rate = sf.read(args.actual)
result = {"sampleRate": rate, "pythonSamples": len(reference), "dotnetSamples": len(actual)}
if actual.ndim != 1 or rate != 24000 or len(reference) != len(actual):
    raise SystemExit(f"Audio shape/sample rate differs: {result}")
result.update(
    pythonRms=float(np.sqrt(np.mean(reference ** 2))),
    dotnetRms=float(np.sqrt(np.mean(actual ** 2))),
    correlation=float(np.corrcoef(reference, actual)[0, 1]),
    maxAbsoluteDifference=float(np.max(np.abs(reference - actual))),
)
print(json.dumps(result, indent=2))
