#!/usr/bin/env python3
"""
SoundFont sample extractor for MiniRPG procedural music system.

Extracts individual note samples from a SoundFont (.sf2) file and saves them
as .wav files organized by instrument, ready for the SamplerEngine.

Requirements:
    pip install sf2_loader

Usage:
    python extract_samples.py <soundfont.sf2> [output_dir]

Default output: Assets/Audio/Samples/

Instruments extracted (General MIDI program numbers):
    lute      -> GM #25 (Acoustic Guitar Nylon)
    harp      -> GM #47 (Orchestral Harp)
    recorder  -> GM #75 (Pan Flute)
    viol      -> GM #43 (Cello)
    percussion -> GM Channel 10 (Standard Kit)
"""

import os
import sys
import struct
import wave
import math

SAMPLE_RATE = 44100
DURATION_SEC = 2.0
FADE_OUT_SEC = 0.3

INSTRUMENT_MAP = {
    "lute": {"program": 24, "min_note": 48, "max_note": 79, "step": 6},
    "harp": {"program": 46, "min_note": 48, "max_note": 84, "step": 6},
    "recorder": {"program": 74, "min_note": 60, "max_note": 84, "step": 6},
    "viol": {"program": 42, "min_note": 36, "max_note": 60, "step": 6},
}

PERCUSSION_NOTES = {
    36: "kick",
    38: "snare",
    39: "tabor",
    41: "timpani",
    42: "hihat",
}


def midi_to_freq(note):
    return 440.0 * (2.0 ** ((note - 69) / 12.0))


def generate_sine_sample(
    freq, duration=DURATION_SEC, sample_rate=SAMPLE_RATE, velocity=0.7
):
    """Generate a simple sine wave sample as fallback when no SoundFont is available."""
    n_samples = int(sample_rate * duration)
    fade_samples = int(sample_rate * FADE_OUT_SEC)
    samples = []

    for i in range(n_samples):
        t = i / sample_rate
        val = math.sin(2 * math.pi * freq * t)

        # Add harmonics for richer tone
        val += 0.3 * math.sin(2 * math.pi * freq * 2 * t)
        val += 0.1 * math.sin(2 * math.pi * freq * 3 * t)

        # ADSR envelope
        attack = min(i / (sample_rate * 0.02), 1.0)
        release_start = n_samples - fade_samples
        if i > release_start:
            release = 1.0 - (i - release_start) / fade_samples
        else:
            release = 1.0

        val *= velocity * attack * release * 0.5
        samples.append(int(max(-32767, min(32767, val * 32767))))

    return samples


def generate_noise_sample(duration=0.3, sample_rate=SAMPLE_RATE, velocity=0.7):
    """Generate a noise burst for percussion."""
    import random

    n_samples = int(sample_rate * duration)
    fade_samples = int(sample_rate * 0.05)
    samples = []

    for i in range(n_samples):
        val = random.uniform(-1, 1)

        attack = min(i / (sample_rate * 0.005), 1.0)
        release_start = n_samples - fade_samples
        if i > release_start:
            release = 1.0 - (i - release_start) / fade_samples
        else:
            release = 1.0

        val *= velocity * attack * release * 0.4
        samples.append(int(max(-32767, min(32767, val * 32767))))

    return samples


def write_wav(filepath, samples, sample_rate=SAMPLE_RATE):
    """Write samples as 16-bit mono WAV."""
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    with wave.open(filepath, "w") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sample_rate)
        data = struct.pack(f"<{len(samples)}h", *samples)
        wf.writeframes(data)


def try_extract_from_sf2(sf2_path, output_dir):
    """Try to extract samples from a SoundFont file."""
    try:
        from sf2_loader import sf2_loader

        sf2 = sf2_loader(sf2_path)
        print(f"Loaded SoundFont: {sf2_path}")

        for inst_id, config in INSTRUMENT_MAP.items():
            inst_dir = os.path.join(output_dir, inst_id)
            os.makedirs(inst_dir, exist_ok=True)

            program = config["program"]
            for midi_note in range(
                config["min_note"], config["max_note"] + 1, config["step"]
            ):
                out_path = os.path.join(inst_dir, f"{inst_id}_{midi_note}.wav")
                try:
                    audio = sf2.export_note(
                        channel=0,
                        program=program,
                        note=midi_note,
                        velocity=100,
                        duration=DURATION_SEC,
                    )
                    audio.export(out_path, format="wav")
                    print(f"  Extracted: {out_path}")
                except Exception as e:
                    print(
                        f"  Failed {inst_id} note {midi_note}: {e}, generating fallback"
                    )
                    samples = generate_sine_sample(midi_to_freq(midi_note))
                    write_wav(out_path, samples)

        # Percussion
        perc_dir = os.path.join(output_dir, "percussion")
        os.makedirs(perc_dir, exist_ok=True)
        for midi_note, name in PERCUSSION_NOTES.items():
            out_path = os.path.join(perc_dir, f"percussion_{midi_note}.wav")
            try:
                audio = sf2.export_note(
                    channel=9,
                    program=0,
                    note=midi_note,
                    velocity=100,
                    duration=0.5,
                )
                audio.export(out_path, format="wav")
                print(f"  Extracted: {out_path} ({name})")
            except Exception as e:
                print(f"  Failed percussion {name}: {e}, generating fallback")
                samples = generate_noise_sample()
                write_wav(out_path, samples)

        return True
    except ImportError:
        print("sf2_loader not available, will generate synthetic samples")
        return False
    except Exception as e:
        print(f"SoundFont extraction failed: {e}, will generate synthetic samples")
        return False


def generate_synthetic_samples(output_dir):
    """Generate synthetic placeholder samples for all instruments."""
    print("Generating synthetic instrument samples...")

    for inst_id, config in INSTRUMENT_MAP.items():
        inst_dir = os.path.join(output_dir, inst_id)
        os.makedirs(inst_dir, exist_ok=True)

        for midi_note in range(
            config["min_note"], config["max_note"] + 1, config["step"]
        ):
            out_path = os.path.join(inst_dir, f"{inst_id}_{midi_note}.wav")
            freq = midi_to_freq(midi_note)
            samples = generate_sine_sample(freq)
            write_wav(out_path, samples)
            print(f"  Generated: {out_path}")

    # Percussion
    perc_dir = os.path.join(output_dir, "percussion")
    os.makedirs(perc_dir, exist_ok=True)
    for midi_note, name in PERCUSSION_NOTES.items():
        out_path = os.path.join(perc_dir, f"percussion_{midi_note}.wav")
        samples = generate_noise_sample(
            duration=0.5 if name in ("kick", "timpani") else 0.3,
            velocity=0.8 if name in ("kick", "timpani") else 0.6,
        )
        write_wav(out_path, samples)
        print(f"  Generated: {out_path} ({name})")


def main():
    sf2_path = sys.argv[1] if len(sys.argv) > 1 else None
    output_dir = (
        sys.argv[2]
        if len(sys.argv) > 2
        else os.path.join(
            os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
            "Assets",
            "Audio",
            "Samples",
        )
    )

    print(f"Output directory: {output_dir}")

    if sf2_path and os.path.exists(sf2_path):
        if try_extract_from_sf2(sf2_path, output_dir):
            print("\nSoundFont extraction complete!")
            return
    elif sf2_path:
        print(f"SoundFont not found: {sf2_path}")

    generate_synthetic_samples(output_dir)
    print(f"\nSynthetic sample generation complete!")
    print(
        f"Total samples: {sum(len(range(c['min_note'], c['max_note'] + 1, c['step'])) for c in INSTRUMENT_MAP.values()) + len(PERCUSSION_NOTES)}"
    )
    print(f"\nTo use real instrument samples, install sf2_loader and run:")
    print(f"  pip install sf2_loader")
    print(f"  python {sys.argv[0]} <path_to_soundfont.sf2>")


if __name__ == "__main__":
    main()
