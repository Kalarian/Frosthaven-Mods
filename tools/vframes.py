"""Split a short video (a game clip from the user) into frames and lay them out as contact sheets, so motion can be
judged from images: a grid of frames in time order, each stamped with its time.

    python tools/vframes.py <video> [--fps 6] [--cols 4] [--width 640] [--start S] [--end S] [--out DIR]
        -> DIR/sheet_01.jpg, sheet_02.jpg ... (default DIR: next to the video, <name>_frames)

Uses the ffmpeg that the imageio-ffmpeg package bundles (pip install imageio-ffmpeg) and Pillow.
"""

import argparse
import subprocess
import sys
import tempfile
from pathlib import Path

import imageio_ffmpeg
from PIL import Image, ImageDraw


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("video", type=Path)
    ap.add_argument("--fps", type=float, default=6.0, help="frames taken per second of video")
    ap.add_argument("--cols", type=int, default=4)
    ap.add_argument("--rows", type=int, default=4, help="rows per sheet")
    ap.add_argument("--width", type=int, default=640, help="width of each frame on the sheet")
    ap.add_argument("--start", type=float, default=0.0)
    ap.add_argument("--end", type=float, default=None)
    ap.add_argument("--out", type=Path, default=None)
    a = ap.parse_args()
    if not a.video.exists():
        sys.exit(f"no such video: {a.video}")
    out = a.out or a.video.with_name(a.video.stem + "_frames")
    out.mkdir(parents=True, exist_ok=True)
    ff = imageio_ffmpeg.get_ffmpeg_exe()
    with tempfile.TemporaryDirectory() as tmp:
        cmd = [ff, "-loglevel", "error", "-ss", str(a.start)] + (["-to", str(a.end)] if a.end else []) + [
            "-i", str(a.video), "-vf", f"fps={a.fps},scale={a.width}:-2", str(Path(tmp) / "f_%05d.jpg")]
        subprocess.run(cmd, check=True)
        frames = sorted(Path(tmp).glob("f_*.jpg"))
        if not frames:
            sys.exit("no frames extracted")
        per = a.cols * a.rows
        sheets = []
        for s in range(0, len(frames), per):
            batch = [Image.open(f) for f in frames[s:s + per]]
            w, h = batch[0].size
            rows = (len(batch) + a.cols - 1) // a.cols
            sheet = Image.new("RGB", (w * a.cols, h * rows), "black")
            d = ImageDraw.Draw(sheet)
            for i, im in enumerate(batch):
                x, y = (i % a.cols) * w, (i // a.cols) * h
                sheet.paste(im, (x, y))
                t = a.start + (s + i) / a.fps
                d.rectangle([x, y, x + 70, y + 18], fill="black")
                d.text((x + 4, y + 3), f"{t:5.2f}s", fill="yellow")
            p = out / f"sheet_{len(sheets) + 1:02d}.jpg"
            sheet.save(p, quality=88)
            sheets.append(p)
    print(f"{len(frames)} frames at {a.fps}/s on {len(sheets)} sheet(s):")
    for p in sheets:
        print(" ", p)


if __name__ == "__main__":
    main()
