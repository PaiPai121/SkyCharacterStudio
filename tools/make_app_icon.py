"""Generate the multi-size application icon from the supplied artwork."""
import argparse
from pathlib import Path

from PIL import Image, ImageOps


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()

    image = Image.open(args.input).convert('RGBA')
    resampling = getattr(Image, 'Resampling', Image)
    square = ImageOps.fit(image, (512, 512), method=resampling.LANCZOS, centering=(0.5, 0.5))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    square.save(
        args.output,
        format='ICO',
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )


if __name__ == '__main__':
    main()
