#!/usr/bin/env python3
"""Build the AnimaSeek Phoenix identity from its approved raster artwork.

The approved image-generation PNG is the geometry source of truth. This tool
segments that bitmap into the four locked brand colors, traces closed contours
from the raster masks, and uses those contours to package SVG and PNG identity
assets. The original generated PNG is preserved byte-for-byte for provenance.

Example:
    python build_phoenix_identity.py /path/to/approved-preview.png
"""

from __future__ import annotations

import argparse
from collections import defaultdict
import hashlib
import json
import math
from pathlib import Path
import re
import shutil

import numpy as np
from PIL import Image, ImageDraw, ImageFont


type Point = tuple[float, float]
type Loop = list[Point]

ROOT = Path(__file__).resolve().parents[1]
SOURCE_SIZE = 1254
PALETTE = {
    "navy": (7, 19, 33, 255),
    "cyan": (39, 185, 255, 255),
    "coral": (255, 76, 88, 255),
    "yellow": (255, 211, 61, 255),
    "warm_white": (255, 249, 238, 255),
}
HEX = {
    "navy": "#071321",
    "cyan": "#27B9FF",
    "coral": "#FF4C58",
    "yellow": "#FFD33D",
    "warm_white": "#FFF9EE",
}
ORDER = ("cyan", "coral", "yellow", "navy")
WORDMARK_PATH_PATTERN = re.compile(r'<path fill="#[0-9A-Fa-f]{6}" d="([^"]+)"/?>')


def parse_arguments() -> argparse.Namespace:
    """Parse the approved PNG path supplied on the command line."""
    parser = argparse.ArgumentParser(
        description="Trace and package the AnimaSeek Phoenix raster identity."
    )
    parser.add_argument(
        "approved_png",
        type=Path,
        help="Path to the user-approved 1254-pixel Phoenix PNG.",
    )
    return parser.parse_args()


def prepare_directories() -> None:
    """Create every identity output directory without removing existing work."""
    for directory in (
        ROOT / "master",
        ROOT / "preview",
        ROOT / "raster",
        ROOT / "source",
        ROOT / "ios" / "AnimaSeekAssets.xcassets" / "AppIcon.appiconset",
        ROOT / "ios" / "AnimaSeekAssets.xcassets" / "LaunchLogo.imageset",
        ROOT / "ios" / "AnimaSeekAssets.xcassets" / "AnimaSeekMark.imageset",
    ):
        directory.mkdir(parents=True, exist_ok=True)


def sha256(path: Path) -> str:
    """Return the lowercase SHA-256 digest of a file."""
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def segment_palette(image: Image.Image) -> dict[str, np.ndarray]:
    """Segment the approved RGB preview into four mutually exclusive masks.

    The generated preview contains a light neutral checkerboard rather than an
    alpha channel. Saturation and luminance separate that neutral field from
    the colored artwork; each remaining pixel is then assigned to its nearest
    locked palette color. The method preserves the user's approved silhouette
    while discarding the checkerboard and subtle generative color drift.
    """
    pixels = np.asarray(image.convert("RGB"), dtype=np.int32)
    maximum = pixels.max(axis=2)
    minimum = pixels.min(axis=2)
    chroma = maximum - minimum
    foreground = (chroma > 24) | (maximum < 120)
    targets = np.array([PALETTE[name][:3] for name in ORDER], dtype=np.int32)
    distances = np.square(pixels[:, :, None, :] - targets[None, None, :, :]).sum(axis=3)
    labels = distances.argmin(axis=2)
    return {
        name: foreground & (labels == index)
        for index, name in enumerate(ORDER)
    }


def boundary_edges(mask: np.ndarray) -> set[tuple[tuple[int, int], tuple[int, int]]]:
    """Return clockwise directed pixel-grid boundary edges for a binary mask."""
    height, width = mask.shape
    edges: set[tuple[tuple[int, int], tuple[int, int]]] = set()
    for y, x in np.argwhere(mask):
        if y == 0 or not mask[y - 1, x]:
            edges.add(((int(x), int(y)), (int(x + 1), int(y))))
        if x == width - 1 or not mask[y, x + 1]:
            edges.add(((int(x + 1), int(y)), (int(x + 1), int(y + 1))))
        if y == height - 1 or not mask[y + 1, x]:
            edges.add(((int(x + 1), int(y + 1)), (int(x), int(y + 1))))
        if x == 0 or not mask[y, x - 1]:
            edges.add(((int(x), int(y + 1)), (int(x), int(y))))
    return edges


def direction_index(start: tuple[int, int], end: tuple[int, int]) -> int:
    """Map a directed unit edge to east, south, west, or north."""
    delta = (end[0] - start[0], end[1] - start[1])
    return {(1, 0): 0, (0, 1): 1, (-1, 0): 2, (0, -1): 3}[delta]


def choose_next_edge(
    previous: tuple[int, int],
    current: tuple[int, int],
    candidates: list[tuple[int, int]],
) -> tuple[int, int]:
    """Choose the rightmost continuation to separate diagonal boundary joins."""
    incoming = direction_index(previous, current)

    def priority(candidate: tuple[int, int]) -> int:
        """Rank right, straight, left, and reverse turns in that order."""
        outgoing = direction_index(current, candidate)
        turn = (outgoing - incoming) % 4
        return {1: 0, 0: 1, 3: 2, 2: 3}[turn]

    return min(candidates, key=priority)


def trace_loops(mask: np.ndarray) -> list[Loop]:
    """Trace every closed pixel-grid loop in a segmented raster mask."""
    remaining = boundary_edges(mask)
    outgoing: dict[tuple[int, int], list[tuple[int, int]]] = defaultdict(list)
    for start, end in remaining:
        outgoing[start].append(end)

    loops: list[Loop] = []
    while remaining:
        first = next(iter(remaining))
        start, current = first
        previous = start
        loop: Loop = [(float(start[0]), float(start[1]))]
        remaining.remove(first)
        while current != start:
            loop.append((float(current[0]), float(current[1])))
            candidates = [
                end for end in outgoing[current] if (current, end) in remaining
            ]
            if not candidates:
                raise RuntimeError(f"Open raster contour at {current}.")
            following = choose_next_edge(previous, current, candidates)
            remaining.remove((current, following))
            previous, current = current, following
        if abs(polygon_area(loop)) >= 80:
            loops.append(loop)
    return loops


def polygon_area(points: Loop) -> float:
    """Return the signed area of a closed polygon in image coordinates."""
    return 0.5 * sum(
        first[0] * second[1] - second[0] * first[1]
        for first, second in zip(points, points[1:] + points[:1], strict=True)
    )


def point_segment_distance(point: Point, start: Point, end: Point) -> float:
    """Return the shortest Euclidean distance from a point to a segment."""
    dx = end[0] - start[0]
    dy = end[1] - start[1]
    if dx == 0 and dy == 0:
        return math.dist(point, start)
    ratio = max(
        0.0,
        min(
            1.0,
            ((point[0] - start[0]) * dx + (point[1] - start[1]) * dy)
            / (dx * dx + dy * dy),
        ),
    )
    projection = (start[0] + ratio * dx, start[1] + ratio * dy)
    return math.dist(point, projection)


def rdp(points: Loop, tolerance: float) -> Loop:
    """Simplify an open polyline with the Ramer-Douglas-Peucker algorithm."""
    if len(points) <= 2:
        return points
    distances = [
        point_segment_distance(point, points[0], points[-1])
        for point in points[1:-1]
    ]
    if not distances or max(distances) <= tolerance:
        return [points[0], points[-1]]
    split = distances.index(max(distances)) + 1
    return rdp(points[: split + 1], tolerance)[:-1] + rdp(points[split:], tolerance)


def simplify_closed_loop(points: Loop, tolerance: float) -> Loop:
    """Simplify a closed contour without biasing the arbitrary start vertex."""
    anchor = min(range(len(points)), key=lambda index: points[index][0] + points[index][1])
    rotated = points[anchor:] + points[:anchor]
    opposite = max(range(1, len(rotated)), key=lambda index: math.dist(rotated[0], rotated[index]))
    first = rdp(rotated[: opposite + 1], tolerance)
    second = rdp(rotated[opposite:] + [rotated[0]], tolerance)
    simplified = first[:-1] + second[:-1]
    return simplified if len(simplified) >= 6 else rotated[:: max(1, len(rotated) // 12)]


def point_in_polygon(point: Point, polygon: Loop) -> bool:
    """Return whether a point lies inside a polygon using ray casting."""
    x, y = point
    inside = False
    previous = polygon[-1]
    for current in polygon:
        crosses = (current[1] > y) != (previous[1] > y)
        if crosses:
            intersection = (
                (previous[0] - current[0])
                * (y - current[1])
                / (previous[1] - current[1])
                + current[0]
            )
            if x < intersection:
                inside = not inside
        previous = current
    return inside


def principal_loops(mask: np.ndarray) -> list[Loop]:
    """Return the main closed silhouette and meaningful interior holes."""
    traced = trace_loops(mask)
    if not traced:
        raise RuntimeError("No contour found for a required Phoenix color region.")
    outer = max(traced, key=lambda loop: abs(polygon_area(loop)))
    holes = [
        loop
        for loop in traced
        if loop is not outer
        and abs(polygon_area(loop)) >= 150
        and point_in_polygon(loop[0], outer)
    ]
    return [
        simplify_closed_loop(loop, 1.8 if index == 0 else 1.0)
        for index, loop in enumerate([outer, *holes])
    ]


def midpoint(first: Point, second: Point) -> Point:
    """Return the midpoint between two points."""
    return ((first[0] + second[0]) / 2, (first[1] + second[1]) / 2)


def svg_loop(points: Loop) -> str:
    """Encode a smoothed closed contour as SVG quadratic path data."""
    start = midpoint(points[-1], points[0])
    commands = [f"M{start[0]:.2f} {start[1]:.2f}"]
    for index, control in enumerate(points):
        end = midpoint(control, points[(index + 1) % len(points)])
        commands.append(
            f"Q{control[0]:.2f} {control[1]:.2f} {end[0]:.2f} {end[1]:.2f}"
        )
    commands.append("Z")
    return " ".join(commands)


def svg_path(loops: list[Loop]) -> str:
    """Encode an outer contour and its holes as one even-odd SVG path."""
    return " ".join(svg_loop(loop) for loop in loops)


def sample_quadratic_loop(points: Loop, samples: int = 10) -> Loop:
    """Sample the same quadratic contour used by the SVG for raster drawing."""
    start = midpoint(points[-1], points[0])
    sampled: Loop = [start]
    segment_start = start
    for index, control in enumerate(points):
        end = midpoint(control, points[(index + 1) % len(points)])
        for step in range(1, samples + 1):
            amount = step / samples
            inverse = 1 - amount
            sampled.append(
                (
                    inverse * inverse * segment_start[0]
                    + 2 * inverse * amount * control[0]
                    + amount * amount * end[0],
                    inverse * inverse * segment_start[1]
                    + 2 * inverse * amount * control[1]
                    + amount * amount * end[1],
                )
            )
        segment_start = end
    return sampled


def render_mark(
    loops_by_name: dict[str, list[Loop]],
    colors: dict[str, tuple[int, int, int, int]],
    size: int,
    background: tuple[int, int, int, int] | None = None,
) -> Image.Image:
    """Render the traced Phoenix mark with antialiased edges at a square size."""
    oversample = 4
    scale = size * oversample / SOURCE_SIZE
    canvas = Image.new(
        "RGBA",
        (size * oversample, size * oversample),
        background or (0, 0, 0, 0),
    )
    for name in ORDER:
        shape = Image.new("L", canvas.size, 0)
        draw = ImageDraw.Draw(shape)
        for index, loop in enumerate(loops_by_name[name]):
            points = [
                (round(x * scale), round(y * scale))
                for x, y in sample_quadratic_loop(loop)
            ]
            draw.polygon(points, fill=255 if index == 0 else 0)
        fill = Image.new("RGBA", canvas.size, colors[name])
        canvas.alpha_composite(Image.composite(fill, Image.new("RGBA", canvas.size), shape))
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def render_union(
    union_loops: list[Loop],
    color: tuple[int, int, int, int],
    size: int,
    background: tuple[int, int, int, int] | None = None,
) -> Image.Image:
    """Render the Phoenix as one seamless monochrome silhouette."""
    oversample = 4
    scale = size * oversample / SOURCE_SIZE
    canvas = Image.new("RGBA", (size * oversample, size * oversample), background or (0, 0, 0, 0))
    mask = Image.new("L", canvas.size, 0)
    draw = ImageDraw.Draw(mask)
    for index, loop in enumerate(union_loops):
        points = [
            (round(x * scale), round(y * scale))
            for x, y in sample_quadratic_loop(loop)
        ]
        draw.polygon(points, fill=255 if index == 0 else 0)
    fill = Image.new("RGBA", canvas.size, color)
    canvas.alpha_composite(Image.composite(fill, Image.new("RGBA", canvas.size), mask))
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def crop_alpha(image: Image.Image) -> Image.Image:
    """Crop a transparent image to its nontransparent bounds."""
    bounds = image.getchannel("A").getbbox()
    if bounds is None:
        raise RuntimeError("Cannot crop a fully transparent image.")
    return image.crop(bounds)


def place_contained(canvas: Image.Image, artwork: Image.Image, box: tuple[int, int, int, int]) -> None:
    """Aspect-fit transparent artwork into a box and composite it on a canvas."""
    left, top, right, bottom = box
    width = right - left
    height = bottom - top
    scale = min(width / artwork.width, height / artwork.height)
    target = artwork.resize(
        (round(artwork.width * scale), round(artwork.height * scale)),
        Image.Resampling.LANCZOS,
    )
    x = left + (width - target.width) // 2
    y = top + (height - target.height) // 2
    canvas.alpha_composite(target, (x, y))


def read_wordmark_path() -> str:
    """Read the existing outlined Avenir Next wordmark path for SVG lockups."""
    source = (ROOT / "source" / "animaseek-wordmark.svg").read_text(encoding="utf-8")
    match = WORDMARK_PATH_PATTERN.search(source)
    if match is None:
        raise RuntimeError("The outlined AnimaSeek wordmark path could not be found.")
    return match.group(1)


def write_svg(path: Path, body: str, view_box: str, title: str, description: str) -> None:
    """Write a self-contained accessible SVG file."""
    document = f'''<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="{view_box}" role="img" aria-labelledby="title desc" shape-rendering="geometricPrecision">
  <title id="title">{title}</title>
  <desc id="desc">{description}</desc>
{body}
</svg>
'''
    path.write_text(document, encoding="utf-8")


def color_paths(paths: dict[str, str], body_color: str) -> str:
    """Return the four traced SVG color paths with a selectable body color."""
    return "\n".join(
        (
            f'  <path fill="{HEX["cyan"]}" fill-rule="evenodd" d="{paths["cyan"]}"/>',
            f'  <path fill="{HEX["coral"]}" fill-rule="evenodd" d="{paths["coral"]}"/>',
            f'  <path fill="{HEX["yellow"]}" fill-rule="evenodd" d="{paths["yellow"]}"/>',
            f'  <path fill="{body_color}" fill-rule="evenodd" d="{paths["navy"]}"/>',
        )
    )


def symbol_contract() -> str:
    """Return the durable direction contract embedded in the primary SVG."""
    return '''  <!--
  THESIS: One rising phoenix makes renewal through connection legible without ribbons, feather fragments, or decorative effects.
  OWN-WORLD: Four flat fills—Vacuum Navy, Connection Cyan, Connection Coral, Discovery Yellow—inside four large closed organic forms.
  STORY: Two open wings welcome connection; their meeting becomes one upward body and a yellow flame-tail of new experience.
  FIRST VIEWPORT: A centered broad-V phoenix with generous clear space, readable at app-icon and 40-pixel sizes.
  FORM: User-approved raster-first Phoenix direction; seed key: user-pinned.
  FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, and DESIGN.md
  -->'''


def build_svgs(paths: dict[str, str], union_path: str, wordmark_path: str) -> None:
    """Build self-contained symbol, app-icon, and lockup SVG masters."""
    source = ROOT / "source"
    colored = color_paths(paths, HEX["navy"])
    reversed_colored = color_paths(paths, HEX["warm_white"])
    mono = f'  <path fill="{HEX["navy"]}" fill-rule="evenodd" d="{union_path}"/>'
    mono_reversed = f'  <path fill="{HEX["warm_white"]}" fill-rule="evenodd" d="{union_path}"/>'

    write_svg(
        source / "animaseek-symbol-color.svg",
        f"{symbol_contract()}\n{colored}",
        "0 0 1254 1254",
        "AnimaSeek Phoenix symbol",
        "A simple rising phoenix in cyan, coral, yellow, and deep navy, traced from the approved raster master.",
    )
    write_svg(
        source / "animaseek-symbol-reversed.svg",
        reversed_colored,
        "0 0 1254 1254",
        "AnimaSeek Phoenix reversed symbol",
        "The color Phoenix with a warm-white body for dark backgrounds.",
    )
    write_svg(
        source / "animaseek-symbol-mono.svg",
        mono,
        "0 0 1254 1254",
        "AnimaSeek Phoenix monochrome symbol",
        "A deep-navy single-color Phoenix silhouette.",
    )
    write_svg(
        source / "animaseek-symbol-mono-reversed.svg",
        mono_reversed,
        "0 0 1254 1254",
        "AnimaSeek Phoenix reversed monochrome symbol",
        "A warm-white single-color Phoenix silhouette.",
    )

    icon_scale = 1024 / 1254
    write_svg(
        source / "animaseek-app-icon-any.svg",
        f'  <rect width="1024" height="1024" fill="{HEX["navy"]}"/>\n'
        f'  <g transform="scale({icon_scale:.8f})">\n{reversed_colored.replace("  <path", "    <path")}\n  </g>',
        "0 0 1024 1024",
        "AnimaSeek Phoenix app icon",
        "A full-square deep-navy app icon with the color Phoenix centered inside Apple safe margins.",
    )
    write_svg(
        source / "animaseek-app-icon-dark.svg",
        f'  <g transform="scale({icon_scale:.8f})">\n{reversed_colored.replace("  <path", "    <path")}\n  </g>',
        "0 0 1024 1024",
        "AnimaSeek Phoenix dark appearance app icon",
        "A transparent dark-appearance icon with the color Phoenix and warm-white body.",
    )
    write_svg(
        source / "animaseek-app-icon-tinted.svg",
        '  <rect width="1024" height="1024" fill="#E8E8E8"/>\n'
        f'  <path transform="scale({icon_scale:.8f})" fill="#181818" fill-rule="evenodd" d="{union_path}"/>',
        "0 0 1024 1024",
        "AnimaSeek Phoenix tinted appearance app icon",
        "An opaque grayscale Phoenix app icon for the system tinted appearance.",
    )

    mark_scale = 420 / 1254
    word_scale = 1.157895
    horizontal_body = (
        f'  <g transform="scale({mark_scale:.8f})">\n{colored.replace("  <path", "    <path")}\n  </g>\n'
        f'  <path transform="translate(480 66) scale({word_scale})" fill="{HEX["navy"]}" d="{wordmark_path}"/>'
    )
    horizontal_reversed = (
        f'  <g transform="scale({mark_scale:.8f})">\n{reversed_colored.replace("  <path", "    <path")}\n  </g>\n'
        f'  <path transform="translate(480 66) scale({word_scale})" fill="{HEX["warm_white"]}" d="{wordmark_path}"/>'
    )
    write_svg(
        source / "animaseek-lockup-horizontal.svg",
        horizontal_body,
        "0 0 1600 420",
        "AnimaSeek horizontal Phoenix lockup",
        "The color Phoenix symbol followed by the outlined AnimaSeek wordmark.",
    )
    write_svg(
        source / "animaseek-lockup-horizontal-reversed.svg",
        horizontal_reversed,
        "0 0 1600 420",
        "AnimaSeek reversed horizontal Phoenix lockup",
        "The reversed color Phoenix followed by the warm-white outlined AnimaSeek wordmark.",
    )

    stacked_scale = 900 / 1254
    stacked_word_scale = 1.05
    stacked = (
        f'  <g transform="translate(150 0) scale({stacked_scale:.8f})">\n{colored.replace("  <path", "    <path")}\n  </g>\n'
        f'  <path transform="translate(101 974) scale({stacked_word_scale})" fill="{HEX["navy"]}" d="{wordmark_path}"/>'
    )
    write_svg(
        source / "animaseek-lockup-stacked.svg",
        stacked,
        "0 0 1200 1280",
        "AnimaSeek stacked Phoenix lockup",
        "The color Phoenix centered above the outlined AnimaSeek wordmark.",
    )

    stacked_reversed = (
        f'  <g transform="translate(150 0) scale({stacked_scale:.8f})">\n{reversed_colored.replace("  <path", "    <path")}\n  </g>\n'
        f'  <path transform="translate(101 974) scale({stacked_word_scale})" fill="{HEX["warm_white"]}" d="{wordmark_path}"/>'
    )
    write_svg(
        source / "animaseek-lockup-stacked-reversed.svg",
        stacked_reversed,
        "0 0 1200 1280",
        "AnimaSeek reversed stacked Phoenix lockup",
        "The reversed color Phoenix centered above the warm-white outlined AnimaSeek wordmark.",
    )


def build_rasters(
    loops_by_name: dict[str, list[Loop]],
    union_loops: list[Loop],
) -> dict[str, Image.Image]:
    """Build master, symbol, app-icon, in-app mark, and lockup PNG files."""
    raster = ROOT / "raster"
    catalog = ROOT / "ios" / "AnimaSeekAssets.xcassets"
    normal_colors = {name: PALETTE[name] for name in ORDER}
    reversed_colors = {**normal_colors, "navy": PALETTE["warm_white"]}
    master = render_mark(loops_by_name, normal_colors, SOURCE_SIZE)
    reversed_master = render_mark(loops_by_name, reversed_colors, SOURCE_SIZE)
    master.save(ROOT / "master" / "animaseek-phoenix-raster-master.png")

    symbols = {
        "color": master.resize((1024, 1024), Image.Resampling.LANCZOS),
        "reversed": reversed_master.resize((1024, 1024), Image.Resampling.LANCZOS),
        "mono": render_union(union_loops, PALETTE["navy"], 1024),
        "mono_reversed": render_union(union_loops, PALETTE["warm_white"], 1024),
    }
    symbols["color"].save(raster / "animaseek-symbol-color-1024.png")
    symbols["reversed"].save(raster / "animaseek-symbol-reversed-1024.png")
    symbols["mono"].save(raster / "animaseek-symbol-mono-1024.png")
    symbols["mono_reversed"].save(raster / "animaseek-symbol-mono-reversed-1024.png")

    any_icon = render_mark(loops_by_name, reversed_colors, 1024, PALETTE["navy"])
    dark_icon = render_mark(loops_by_name, reversed_colors, 1024)
    tinted_icon = render_union(union_loops, (24, 24, 24, 255), 1024, (232, 232, 232, 255))
    any_icon.convert("RGB").save(catalog / "AppIcon.appiconset" / "AppIcon-Any-1024.png")
    dark_icon.save(catalog / "AppIcon.appiconset" / "AppIcon-Dark-1024.png")
    tinted_icon.convert("RGB").save(catalog / "AppIcon.appiconset" / "AppIcon-Tinted-1024.png")
    any_icon.resize((512, 512), Image.Resampling.LANCZOS).convert("RGB").save(
        catalog / "LaunchLogo.imageset" / "LaunchLogo.png"
    )
    symbols["color"].resize((512, 512), Image.Resampling.LANCZOS).save(
        catalog / "AnimaSeekMark.imageset" / "AnimaSeekMark.png"
    )
    symbols["reversed"].resize((512, 512), Image.Resampling.LANCZOS).save(
        catalog / "AnimaSeekMark.imageset" / "AnimaSeekMark-Dark.png"
    )

    wordmark = Image.open(raster / "animaseek-wordmark-1900.png").convert("RGBA")
    wordmark_reversed = Image.open(raster / "animaseek-wordmark-reversed-1900.png").convert("RGBA")
    mark_crop = crop_alpha(symbols["color"])
    reversed_crop = crop_alpha(symbols["reversed"])

    horizontal = Image.new("RGBA", (1600, 420), (0, 0, 0, 0))
    place_contained(horizontal, mark_crop, (0, 0, 420, 420))
    place_contained(horizontal, crop_alpha(wordmark), (480, 90, 1580, 330))
    horizontal.save(raster / "animaseek-lockup-horizontal-1600.png")

    horizontal_reversed = Image.new("RGBA", (1600, 420), (0, 0, 0, 0))
    place_contained(horizontal_reversed, reversed_crop, (0, 0, 420, 420))
    place_contained(horizontal_reversed, crop_alpha(wordmark_reversed), (480, 90, 1580, 330))
    horizontal_reversed.save(raster / "animaseek-lockup-horizontal-reversed-1600.png")

    stacked = Image.new("RGBA", (1200, 1280), (0, 0, 0, 0))
    place_contained(stacked, mark_crop, (120, 20, 1080, 940))
    place_contained(stacked, crop_alpha(wordmark), (100, 1000, 1100, 1190))
    stacked.save(raster / "animaseek-lockup-stacked-1200.png")

    stacked_reversed = Image.new("RGBA", (1200, 1280), (0, 0, 0, 0))
    place_contained(stacked_reversed, reversed_crop, (120, 20, 1080, 940))
    place_contained(stacked_reversed, crop_alpha(wordmark_reversed), (100, 1000, 1100, 1190))
    stacked_reversed.save(raster / "animaseek-lockup-stacked-reversed-1200.png")
    # The launch-screen renderer resolves only proper 1x/2x/3x slots, so the storyboard lockup ships
    # at its displayed 250 x 267 pt size instead of one oversized universal image.
    for scale, width in ((1, 250), (2, 500), (3, 750)):
        height = round(width * 1280 / 1200)
        suffix = "" if scale == 1 else f"@{scale}x"
        stacked.resize((width, height), Image.Resampling.LANCZOS).save(
            catalog / "LaunchLockup.imageset" / f"LaunchLockup{suffix}.png"
        )
        stacked_reversed.resize((width, height), Image.Resampling.LANCZOS).save(
            catalog / "LaunchLockup.imageset" / f"LaunchLockup-Dark{suffix}.png"
        )
    return {
        "master": master,
        "reversed": reversed_master,
        "any_icon": any_icon,
        "dark_icon": dark_icon,
        "tinted_icon": tinted_icon,
        "horizontal": horizontal,
        "horizontal_reversed": horizontal_reversed,
        "stacked": stacked,
        "stacked_reversed": stacked_reversed,
    }


def load_font(size: int, index: int = 2) -> ImageFont.FreeTypeFont:
    """Load Avenir Next Demi Bold from the macOS system font collection."""
    return ImageFont.truetype("/System/Library/Fonts/Avenir Next.ttc", size, index=index)


def build_preview(artwork: dict[str, Image.Image]) -> None:
    """Compose a flat identity sheet and a small-size recognition check."""
    width, height = 1800, 1500
    sheet = Image.new("RGBA", (width, height), PALETTE["warm_white"])
    draw = ImageDraw.Draw(sheet)
    title_font = load_font(76)
    label_font = load_font(28)
    detail_font = load_font(22, index=7)
    draw.text((96, 72), "AnimaSeek", font=title_font, fill=PALETTE["navy"])
    draw.text((98, 160), "Phoenix identity · raster-first master", font=detail_font, fill=(69, 78, 89, 255))

    place_contained(sheet, crop_alpha(artwork["master"]), (70, 240, 900, 1060))
    draw.text((100, 1068), "PRIMARY MARK", font=label_font, fill=PALETTE["navy"])

    icon = artwork["any_icon"].resize((440, 440), Image.Resampling.LANCZOS)
    sheet.alpha_composite(icon, (1130, 230))
    draw.text((1130, 690), "APP ICON · ANY", font=label_font, fill=PALETTE["navy"])

    dark_tile = Image.new("RGBA", (440, 440), PALETTE["navy"])
    place_contained(dark_tile, crop_alpha(artwork["dark_icon"]), (18, 18, 422, 422))
    sheet.alpha_composite(dark_tile, (1130, 770))
    draw.text((1130, 1230), "DARK FIELD", font=label_font, fill=PALETTE["navy"])

    small_y = 1260
    draw.text((100, 1198), "SMALL-SIZE CHECK", font=label_font, fill=PALETTE["navy"])
    for index, size in enumerate((40, 60, 120)):
        symbol = artwork["master"].resize((size, size), Image.Resampling.LANCZOS)
        x = 110 + index * 190
        sheet.alpha_composite(symbol, (x, small_y))
        draw.text((x, small_y + size + 14), f"{size} px", font=detail_font, fill=PALETTE["navy"])

    sheet.convert("RGB").save(ROOT / "preview" / "animaseek-identity-sheet.png")

    small = Image.new("RGBA", (720, 280), PALETTE["warm_white"])
    small_draw = ImageDraw.Draw(small)
    for index, size in enumerate((40, 60, 120)):
        x = (70, 270, 500)[index]
        symbol = artwork["master"].resize((size, size), Image.Resampling.LANCZOS)
        small.alpha_composite(symbol, (x, 60))
        small_draw.text((x, 205), f"{size} px", font=detail_font, fill=PALETTE["navy"])
    small.convert("RGB").save(ROOT / "preview" / "animaseek-small-size-check.png")


def write_provenance(approved: Path, clean_master: Path) -> None:
    """Write machine-readable raster-first lineage and generation metadata."""
    payload = {
        "identity": "AnimaSeek Phoenix",
        "approvedOn": "2026-08-17",
        "geometryAuthority": "PNG",
        "approvedSource": {
            "file": approved.name,
            "dimensions": [1254, 1254],
            "sha256": sha256(approved),
            "note": "Original user-approved generated preview, preserved byte-for-byte; its checkerboard is baked into RGB pixels.",
        },
        "cleanRasterMaster": {
            "file": clean_master.name,
            "dimensions": [1254, 1254],
            "sha256": sha256(clean_master),
            "note": "Transparent four-color master segmented and traced from the approved source PNG.",
        },
        "vectorMethod": "Closed raster contours simplified and encoded as quadratic SVG paths; no SVG geometry was used to generate the approved PNG.",
        "palette": {name: value for name, value in HEX.items()},
    }
    (ROOT / "master" / "animaseek-phoenix-provenance.json").write_text(
        json.dumps(payload, indent=2) + "\n",
        encoding="utf-8",
    )


def main() -> None:
    """Preserve, trace, render, and package the approved Phoenix identity."""
    arguments = parse_arguments()
    approved = arguments.approved_png.resolve()
    if not approved.is_file():
        raise FileNotFoundError(approved)
    image = Image.open(approved)
    if image.size != (SOURCE_SIZE, SOURCE_SIZE):
        raise ValueError(f"Expected {SOURCE_SIZE}×{SOURCE_SIZE}, received {image.size}.")

    prepare_directories()
    preserved = ROOT / "master" / "animaseek-phoenix-approved-source.png"
    shutil.copyfile(approved, preserved)
    masks = segment_palette(image)
    loops_by_name = {name: principal_loops(mask) for name, mask in masks.items()}
    union_loops = principal_loops(np.logical_or.reduce(tuple(masks.values())))
    paths = {name: svg_path(loops) for name, loops in loops_by_name.items()}
    wordmark_path = read_wordmark_path()
    build_svgs(paths, svg_path(union_loops), wordmark_path)
    artwork = build_rasters(loops_by_name, union_loops)
    build_preview(artwork)
    clean_master = ROOT / "master" / "animaseek-phoenix-raster-master.png"
    write_provenance(preserved, clean_master)


if __name__ == "__main__":
    main()
