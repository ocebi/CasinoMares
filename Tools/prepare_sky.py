#!/usr/bin/env python3
"""Turn an HDR sky photo (an equirectangular .hdr, e.g. a Poly Haven "puresky" HDRI) into the seaside casino's sky.

    pip install numpy opencv-python-headless
    python3 Tools/prepare_sky.py path/to/sky_4k.hdr Assets/ThirdParty/PolyHaven/<name>

Writes <name>.png and <name>.json. The PNG is the upper half of the photo (horizon to zenith) at the input's width.
An HDR sky is far too big for a phone, so it's stored in 8 bits: the radiance is scaled so the median sky is 0.35,
then x / (1 + x) keeps the bright glow round the sun from clipping (SunsetSky.hlsl undoes it). The JSON says where
the sun is (u across the image, degrees above the horizon) and gives the sky's colours at that scale, linear, for
the builder (CasinoSceneBuilder.Sea.cs) to set the fog and ambient light from.
"""
import json
import sys

import cv2
import numpy as np

MEDIAN_SKY = 0.35


def main():
    source, target = sys.argv[1], sys.argv[2]
    hdr = cv2.imread(source, cv2.IMREAD_UNCHANGED)[:, :, ::-1].astype(np.float32)  # BGR -> RGB, linear
    height, width, _ = hdr.shape
    upper = hdr[: height // 2]
    luminance = upper @ np.array([0.2126, 0.7152, 0.0722], np.float32)

    sun_y, sun_x = np.unravel_index(np.argmax(luminance), luminance.shape)
    scale = MEDIAN_SKY / float(np.median(luminance))
    sky = upper * scale

    encoded = sky / (1.0 + sky)
    srgb = np.where(encoded <= 0.0031308, encoded * 12.92, 1.055 * np.power(encoded, 1 / 2.4) - 0.055)
    cv2.imwrite(target + ".png", np.clip(np.round(srgb * 255), 0, 255).astype(np.uint8)[:, :, ::-1])

    # The ring just above the horizon, behind you (looking away from the sun) and under the sun; the dome overhead.
    ring = sky[-8:-2]
    turns_from_sun = np.abs(((np.arange(width) - sun_x + width / 2) % width) - width / 2) / width
    colours = {
        "horizonAway": ring[:, turns_from_sun > 0.3].reshape(-1, 3).mean(0),
        "horizonUnderSun": ring[:, turns_from_sun < 0.03].reshape(-1, 3).mean(0),
        "zenith": sky[: height // 8].reshape(-1, 3).mean(0),
        "midSky": sky[height // 8 : height // 2 - 40].reshape(-1, 3).mean(0),
    }
    info = {
        "sunU": round((sun_x + 0.5) / width, 5),
        "sunElevation": round(90.0 - (sun_y + 0.5) / height * 180.0, 3),
        **{name: [round(float(c), 4) for c in colour] for name, colour in colours.items()},
    }
    with open(target + ".json", "w") as file:
        json.dump(info, file, indent=2)
    print(json.dumps(info))


if __name__ == "__main__":
    main()
