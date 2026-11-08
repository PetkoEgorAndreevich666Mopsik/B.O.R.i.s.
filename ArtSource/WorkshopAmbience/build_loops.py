"""Generate the workshop ambience WAVs with Python 3 and no dependencies."""

from array import array
import math
from pathlib import Path
import random
import sys
import wave


RATE = 24000
SECONDS = 16
FRAMES = RATE * SECONDS
OUTPUT = Path(__file__).resolve().parents[2] / "Assets/_Project/Audio/WorkshopAmbience"


def low_pass(samples, frequency):
    coefficient = 1 - math.exp(-math.tau * frequency / RATE)
    state = 0
    result = array("f", [0]) * FRAMES
    # One full warm-up uses the same input as the output cycle.
    for _ in range(2):
        for index, sample in enumerate(samples):
            state += coefficient * (sample - state)
            result[index] = state
    return result


def noise(seed, low, high):
    randomizer = random.Random(seed)
    white = array("f", (randomizer.uniform(-1, 1) for _ in range(FRAMES)))
    top = low_pass(white, high)
    bottom = low_pass(white, low)
    return array("f", (a - b for a, b in zip(top, bottom)))


def tone(time, frequency):
    return math.sin(math.tau * time * frequency)


def save(name, samples, peak):
    levels = array("f", samples)
    scale = peak * 32767 / max(abs(level) for level in levels)
    pcm = array("h", (round(level * scale) for level in levels))
    if sys.byteorder != "little":
        pcm.byteswap()
    with wave.open(str(OUTPUT / (name + ".wav")), "wb") as sound:
        sound.setparams((1, 2, RATE, FRAMES, "NONE", ""))
        sound.writeframes(pcm.tobytes())


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    fan = noise(61, 65, 610)
    air = noise(53, 28, 440)
    wash = noise(11, 35, 170)
    save("Ventilation", (
        .52 * sample + .044 * tone(i / RATE, 50) + .020 * tone(i / RATE, 100)
        + .012 * tone(i / RATE, 150)
        for i, sample in enumerate(fan)
    ), .25)
    save("BalconyWind", (
        sample * (.62 + .24 * tone(i / RATE, 3 / SECONDS) + .12 * tone(i / RATE, 7 / SECONDS))
        for i, sample in enumerate(air)
    ), .32)
    save("DistantTraffic", (
        sample * (.65 + .21 * tone(i / RATE, 2 / SECONDS) + .10 * tone(i / RATE, 5 / SECONDS))
        for i, sample in enumerate(wash)
    ), .23)


if __name__ == "__main__":
    main()
