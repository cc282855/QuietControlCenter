#!/usr/bin/env python3
"""Build the frozen Quiet Control Center brand assets deterministically.

The generated-image chroma source and its locally extracted alpha source are
immutable provenance inputs.  This script only resizes and packages the alpha
source; it never invokes an image model and never reads runtime configuration.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


CHROMA_SHA256 = "C4A7CBE53799F29077BEC13202C6D6C702327D9965F2F1D9B0A3378A2E02590B"
ALPHA_SOURCE_SHA256 = "D2B2A67174FA0496E07B9C0B237B55D25B3DB23EE39F00B5B29754325298F1F3"
ICO_SIZES = ((16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256))


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest().upper()


def read_verified(path: Path, expected_hash: str) -> bytes:
    payload = path.read_bytes()
    actual = sha256_bytes(payload)
    if actual != expected_hash:
        raise RuntimeError(f"Immutable brand source hash mismatch: {path} expected={expected_hash} actual={actual}")
    return payload


def png_bytes(image: Image.Image) -> bytes:
    stream = io.BytesIO()
    image.save(stream, format="PNG", optimize=False, compress_level=9)
    return stream.getvalue()


def ico_bytes(image: Image.Image) -> bytes:
    stream = io.BytesIO()
    image.save(stream, format="ICO", sizes=ICO_SIZES, bitmap_format="png")
    return stream.getvalue()


def icns_bytes(image: Image.Image) -> bytes:
    stream = io.BytesIO()
    image.save(stream, format="ICNS")
    return stream.getvalue()


def normalized_master(alpha_source: bytes) -> Image.Image:
    with Image.open(io.BytesIO(alpha_source)) as loaded:
        image = loaded.convert("RGBA")
    if image.getchannel("A").getbbox() is None:
        raise RuntimeError("Transparent brand source has no visible pixels")
    if any(image.getpixel(point)[3] != 0 for point in ((0, 0), (image.width - 1, 0), (0, image.height - 1), (image.width - 1, image.height - 1))):
        raise RuntimeError("Transparent brand source corners must be fully transparent")
    image = image.resize((1024, 1024), Image.Resampling.LANCZOS)
    # Alpha-composite onto transparent black so fully transparent RGB samples
    # cannot create colored fringes during later downsampling.
    return Image.alpha_composite(Image.new("RGBA", image.size, (0, 0, 0, 0)), image)


def contact_sheet(master: Image.Image, *, dark: bool) -> Image.Image:
    background = "#101426" if dark else "#F4F7FC"
    foreground = "#F6F8FF" if dark else "#172044"
    panel = "#1B2140" if dark else "#FFFFFF"
    border = "#343D69" if dark else "#D7DDEA"
    checker_a = "#242B4A" if dark else "#E5E9F2"
    checker_b = "#171D35" if dark else "#F8FAFD"
    sheet = Image.new("RGB", (1024, 1024), background)
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=20)
    small = ImageFont.load_default(size=16)

    draw.text((56, 42), f"MIKA BRAND ICON / {'DARK' if dark else 'LIGHT'} PREVIEW", fill=foreground, font=font)
    draw.text((56, 74), "1024 px transparent master -> Windows multi-size assets", fill=foreground, font=small)

    draw.rounded_rectangle((56, 116, 536, 596), radius=36, fill=panel, outline=border, width=2)
    tile = 48
    for y in range(140, 572, tile):
        for x in range(80, 512, tile):
            draw.rectangle((x, y, min(x + tile, 512), min(y + tile, 572)), fill=checker_a if ((x // tile) + (y // tile)) % 2 else checker_b)
    large = master.resize((400, 400), Image.Resampling.LANCZOS)
    sheet.paste(large, (96, 156), large)

    sizes = (16, 20, 24, 32, 40, 48, 64, 128)
    slots = ((584, 116), (760, 116), (584, 238), (760, 238), (584, 360), (760, 360), (584, 482), (760, 482))
    for size, (x, y) in zip(sizes, slots):
        slot_w = 152
        slot_h = 150 if size >= 64 else 110
        draw.rounded_rectangle((x, y, x + slot_w, y + slot_h), radius=18, fill=panel, outline=border, width=2)
        icon = master.resize((size, size), Image.Resampling.LANCZOS)
        icon_x = x + (slot_w - size) // 2
        icon_y = y + 10 + max(0, (76 - size) // 2)
        sheet.paste(icon, (icon_x, icon_y), icon)
        draw.text((x + 12, y + slot_h - 27), f"{size} x {size}", fill=foreground, font=small)

    draw.text((56, 638), "Small-size legibility", fill=foreground, font=font)
    draw.text((56, 674), "One silhouette / one gold node / no text", fill=foreground, font=small)
    for index, size in enumerate((16, 20, 24, 32, 48, 64)):
        x = 56 + index * 82
        draw.rounded_rectangle((x, 718, x + 66, 808), radius=12, fill=panel, outline=border, width=2)
        icon = master.resize((size, size), Image.Resampling.LANCZOS)
        sheet.paste(icon, (x + (66 - size) // 2, 730), icon)
        draw.text((x + 8, 784), str(size), fill=foreground, font=small)

    draw.rounded_rectangle((56, 850, 536, 958), radius=18, fill=panel, outline=border, width=2)
    draw.text((80, 876), "EXE / shortcut / taskbar / tray", fill=foreground, font=small)
    draw.text((80, 910), "Menu glyphs remain semantic", fill=foreground, font=small)

    draw.rounded_rectangle((584, 654, 968, 958), radius=18, fill=panel, outline=border, width=2)
    icon_256 = master.resize((256, 256), Image.Resampling.LANCZOS)
    sheet.paste(icon_256, (648, 668), icon_256)
    draw.text((604, 926), "256 x 256 / Explorer and shortcut", fill=foreground, font=small)
    return sheet


def build_outputs(repo_root: Path) -> dict[Path, bytes]:
    branding = repo_root / "branding"
    chroma_path = branding / "source" / "mika-wind-gate-chroma-source.png"
    alpha_path = branding / "source" / "mika-wind-gate-alpha-extracted-source.png"
    read_verified(chroma_path, CHROMA_SHA256)
    alpha_source = read_verified(alpha_path, ALPHA_SOURCE_SHA256)
    master = normalized_master(alpha_source)

    master_payload = png_bytes(master)
    header_payload = png_bytes(master.resize((512, 512), Image.Resampling.LANCZOS))
    desktop_payload = png_bytes(master.resize((256, 256), Image.Resampling.LANCZOS))
    icon_payload = ico_bytes(master)
    outputs: dict[Path, bytes] = {
        branding / "master" / "mika-wind-gate-transparent-1024.png": master_payload,
        branding / "evidence" / "mika-brand-contact-light-1024.png": png_bytes(contact_sheet(master, dark=False)),
        branding / "evidence" / "mika-brand-contact-dark-1024.png": png_bytes(contact_sheet(master, dark=True)),
        repo_root / "v2rayN" / "v2rayN" / "Resources" / "MikaLogo.png": header_payload,
        repo_root / "v2rayN" / "v2rayN" / "Resources" / "v2rayN.ico": icon_payload,
        repo_root / "v2rayN" / "v2rayN.Desktop" / "v2rayN.png": desktop_payload,
        repo_root / "v2rayN" / "v2rayN.Desktop" / "v2rayN.icns": icns_bytes(master),
        repo_root / "v2rayN" / "AmazTool" / "Resources" / "v2rayN.ico": icon_payload,
    }
    for name in ("NotifyIcon1.ico", "NotifyIcon2.ico", "NotifyIcon3.ico", "NotifyIcon4.ico"):
        outputs[repo_root / "v2rayN" / "v2rayN" / "Resources" / name] = icon_payload
    for name in ("v2rayN.ico", "NotifyIcon1.ico", "NotifyIcon2.ico", "NotifyIcon3.ico", "NotifyIcon4.ico"):
        outputs[repo_root / "v2rayN" / "v2rayN.Desktop" / "Assets" / name] = icon_payload

    manifest = {
        "schema": 1,
        "brand": "玄同",
        "source": {
            "chroma": str(chroma_path.relative_to(repo_root)).replace("\\", "/"),
            "chromaSha256": CHROMA_SHA256,
            "alpha": str(alpha_path.relative_to(repo_root)).replace("\\", "/"),
            "alphaSha256": ALPHA_SOURCE_SHA256,
        },
        "master": {"width": 1024, "height": 1024, "sha256": sha256_bytes(master_payload)},
        "icoSizes": [size[0] for size in ICO_SIZES],
        "outputs": {
            str(path.relative_to(repo_root)).replace("\\", "/"): sha256_bytes(payload)
            for path, payload in sorted(outputs.items(), key=lambda item: str(item[0]).lower())
        },
    }
    outputs[branding / "brand-assets-manifest.json"] = (json.dumps(manifest, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")
    return outputs


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--check", action="store_true", help="verify generated files without writing")
    args = parser.parse_args()
    repo_root = args.repo_root.resolve()
    outputs = build_outputs(repo_root)
    mismatches: list[str] = []
    for path, payload in outputs.items():
        if args.check:
            if not path.is_file() or path.read_bytes() != payload:
                mismatches.append(str(path))
            continue
        path.parent.mkdir(parents=True, exist_ok=True)
        if not path.is_file() or path.read_bytes() != payload:
            path.write_bytes(payload)
        print(f"{path.relative_to(repo_root)} SHA256={sha256_bytes(payload)}")
    if mismatches:
        print("Brand assets are stale or missing:", file=sys.stderr)
        for path in mismatches:
            print(f"  {path}", file=sys.stderr)
        return 1
    if args.check:
        print(f"Verified {len(outputs)} deterministic brand outputs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
