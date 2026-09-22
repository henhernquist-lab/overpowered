"""Offline CC0 preparation. Requires numpy/soundfile; never runs in the game.
Usage: python prepare_clips.py /path/to/extracted-source-directory
Source manifests and downloads are documented in ATTRIBUTION.md.
"""
from pathlib import Path
import sys
import numpy as np
import soundfile as sf

source = Path(sys.argv[1])
destination = Path(__file__).parent
clips = {
    "punch-1": "impact/Audio/impactPunch_heavy_000.ogg",
    "punch-2": "impact/Audio/impactPunch_heavy_001.ogg",
    "step-1": "impact/Audio/footstep_concrete_000.ogg",
    "step-2": "impact/Audio/footstep_concrete_001.ogg",
    "step-3": "impact/Audio/footstep_concrete_002.ogg",
    "flight-start": "scifi/Audio/spaceEngineSmall_000.ogg",
    "flight-loop": "scifi/Audio/spaceEngineLow_000.ogg",
    "land": "impact/Audio/impactSoft_heavy_000.ogg",
    "debris": "scifi/Audio/explosionCrunch_000.ogg",
    "fire": "scifi/Audio/explosionCrunch_002.ogg",
    "ice": "scifi/Audio/forceField_000.ogg",
    "telekinesis": "scifi/Audio/forceField_003.ogg",
    "gunshot": "Prepared SFX Library/1911/A_34P.wav",
    "hit": "impact/Audio/impactPunch_medium_000.ogg",
    "death": "ui/Audio/error_006.ogg",
    "jump": "ui/Audio/switch_001.ogg",
    "ui-click": "ui/Audio/click_001.ogg",
    "ui-hover": "ui/Audio/select_001.ogg",
    "city-bed": "city.ogg",
    "siren-bed": "alarm.wav",
    "music": "music.mp3",
}
for name, original in clips.items():
    samples, rate = sf.read(source / original, always_2d=True, dtype="float32")
    # Positional effects are mono. Stereo is preserved for the three non-positional beds.
    if name not in ("city-bed", "siren-bed", "music"):
        samples = samples.mean(axis=1, keepdims=True)
    # Shorten engine whoosh; wrap the sustained engine/alarm with a 50 ms overlap.
    if name == "flight-start":
        samples = samples[:int(rate * .65)]
    if name == "gunshot":
        # First recorded pistol report, with 5 ms lead-in and 0.8 s decay (not a gun loop).
        onset = int(np.flatnonzero(np.max(np.abs(samples), axis=1) > .3)[0])
        start = max(0, onset-int(rate*.005))
        samples = samples[start:start+int(rate*.8)]
    if name in ("flight-loop", "siren-bed"):
        overlap = min(int(rate * .05), len(samples) // 8)
        ramp = np.linspace(0, 1, overlap)[:, None]
        samples[:overlap] = samples[-overlap:] * (1-ramp) + samples[:overlap] * ramp
        samples = samples[:-overlap]
    elif name != "city-bed":
        fade = min(int(rate * (.2 if name == "music" else .008)), len(samples) // 8)
        samples[:fade] *= np.linspace(0, 1, fade)[:, None]
        samples[-fade:] *= np.linspace(1, 0, fade)[:, None]
    peak = float(np.max(np.abs(samples)))
    if peak > .95:
        samples *= .95 / peak
    # Chunked writes avoid oversized native Vorbis encoder buffers on macOS libsndfile.
    with sf.SoundFile(destination / (name + ".ogg"), "w", samplerate=rate,
                      channels=samples.shape[1], format="OGG", subtype="VORBIS") as output:
        for start in range(0, len(samples), 16384):
            output.write(samples[start:start+16384])
    print(f"{name}.ogg: {len(samples)/rate:.3f}s, channels={samples.shape[1]}, peak={np.max(np.abs(samples)):.4f}, source={original}")
