#!/usr/bin/env python3
"""Convert Android string resources into a neutral .NET RESX catalog."""

from argparse import ArgumentParser, Namespace
from collections import Counter
from pathlib import Path
from xml.etree import ElementTree


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE = REPOSITORY_ROOT / "Seeker" / "Resources" / "values" / "strings.xml"
DEFAULT_DESTINATION = REPOSITORY_ROOT / "Common" / "Localization" / "StringResources.resx"
XML_SPACE = "{http://www.w3.org/XML/1998/namespace}space"


def parse_arguments() -> Namespace:
    """Parse optional source and destination paths from the command line."""
    parser = ArgumentParser(
        description="Convert Android <string> resources to a .NET RESX file."
    )
    parser.add_argument(
        "source",
        nargs="?",
        type=Path,
        default=DEFAULT_SOURCE,
        help=f"Android strings.xml path (default: {DEFAULT_SOURCE})",
    )
    parser.add_argument(
        "destination",
        nargs="?",
        type=Path,
        default=DEFAULT_DESTINATION,
        help=f"Output RESX path (default: {DEFAULT_DESTINATION})",
    )
    return parser.parse_args()


ANDROID_WHITESPACE = " \t\n\r\x0b\x0c"


def render_android_string(value: str) -> str:
    """Render raw Android resource text: escapes, quote spans, and whitespace.

    Order contract: Android evaluates backslash escapes, double-quote spans,
    and whitespace collapsing in ONE left-to-right pass over the raw value.
    These stages must not be split into separate passes — decoding escapes
    first would make an escaped literal quote (``\\"``) indistinguishable
    from an unescaped ``"``, which toggles a whitespace-preserving span and
    is removed from the rendered string. Outside quoted spans, each run of
    whitespace collapses to a single space (leading and trailing runs are
    dropped); inside quoted spans whitespace is preserved verbatim. Unknown
    escape sequences are kept unchanged.
    """
    rendered: list[str] = []
    escape_values = {
        "n": "\n",
        "r": "\r",
        "t": "\t",
        "'": "'",
        '"': '"',
        "\\": "\\",
        "@": "@",
        "?": "?",
    }
    in_quoted_span = False
    pending_space = False
    index = 0

    def flush_pending_space() -> None:
        nonlocal pending_space
        if pending_space:
            if rendered:
                rendered.append(" ")
            pending_space = False

    def append_rendered(text: str) -> None:
        flush_pending_space()
        rendered.append(text)

    while index < len(value):
        character = value[index]

        if character == "\\" and index + 1 < len(value):
            escape_code = value[index + 1]
            if escape_code == "u" and index + 5 < len(value):
                code_point = value[index + 2 : index + 6]
                if all(
                    digit in "0123456789abcdefABCDEF" for digit in code_point
                ):
                    append_rendered(chr(int(code_point, 16)))
                    index += 6
                    continue

            if escape_code in escape_values:
                append_rendered(escape_values[escape_code])
            else:
                append_rendered("\\" + escape_code)
            index += 2
            continue

        if character == '"':
            # An unescaped quote toggles the span and never reaches the output.
            flush_pending_space()
            in_quoted_span = not in_quoted_span
            index += 1
            continue

        if not in_quoted_span and character in ANDROID_WHITESPACE:
            pending_space = True
            index += 1
            continue

        append_rendered(character)
        index += 1

    return "".join(rendered)


def read_android_strings(source: Path) -> list[tuple[str, str]]:
    """Read ordered Android strings, flattening presentation-only child markup."""
    document = ElementTree.parse(source)
    root = document.getroot()
    if root.tag != "resources":
        raise ValueError(f"Expected a <resources> root in {source}")

    entries = [
        (
            element.attrib["name"],
            render_android_string("".join(element.itertext())),
        )
        for element in root.findall("string")
    ]
    names = [name for name, _ in entries]
    duplicate_names = sorted(
        name for name, count in Counter(names).items() if count > 1
    )
    if duplicate_names:
        raise ValueError(f"Duplicate string names: {', '.join(duplicate_names)}")

    return entries


def build_resx(entries: list[tuple[str, str]]) -> ElementTree.ElementTree:
    """Build a RESX document containing only culture-neutral string values."""
    root = ElementTree.Element("root")
    root.append(
        ElementTree.Comment(
            " Generated from Seeker/Resources/values/strings.xml by "
            "scripts/convert_android_strings.py. "
        )
    )

    headers = {
        "resmimetype": "text/microsoft-resx",
        "version": "2.0",
        "reader": (
            "System.Resources.ResXResourceReader, System.Windows.Forms, "
            "Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
        ),
        "writer": (
            "System.Resources.ResXResourceWriter, System.Windows.Forms, "
            "Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
        ),
    }
    for name, value in headers.items():
        header = ElementTree.SubElement(root, "resheader", {"name": name})
        ElementTree.SubElement(header, "value").text = value

    for name, value in entries:
        data = ElementTree.SubElement(
            root,
            "data",
            {"name": name, XML_SPACE: "preserve"},
        )
        ElementTree.SubElement(data, "value").text = value

    document = ElementTree.ElementTree(root)
    ElementTree.indent(document, space="  ")
    return document


def write_resx(document: ElementTree.ElementTree, destination: Path) -> None:
    """Write a formatted UTF-8 RESX document, creating its directory if needed."""
    destination.parent.mkdir(parents=True, exist_ok=True)
    document.write(
        destination,
        encoding="utf-8",
        xml_declaration=True,
        short_empty_elements=False,
    )


def main() -> None:
    """Convert the requested Android catalog and report the generated count."""
    arguments = parse_arguments()
    entries = read_android_strings(arguments.source)
    write_resx(build_resx(entries), arguments.destination)
    print(f"Wrote {len(entries)} strings to {arguments.destination}")


if __name__ == "__main__":
    main()
