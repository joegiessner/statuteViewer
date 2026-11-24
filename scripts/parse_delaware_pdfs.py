#!/usr/bin/env python3
"""Parse archived Delaware Code title PDFs into hierarchical JSON.

Reads ONLY from local files under data/pdfs/. Never contacts delcode.delaware.gov.
"""

from __future__ import annotations

import json
import re
import sys
import time
from dataclasses import dataclass, field, asdict
from pathlib import Path

from pypdf import PdfReader

ROOT = Path(__file__).resolve().parents[1]
PDF_DIR = ROOT / "data" / "pdfs"
OUT_DIR = ROOT / "data" / "titles"

TITLE_NAMES = {
    1: "General Provisions",
    2: "Transportation",
    3: "Agriculture",
    4: "Alcoholic Liquors and Marijuana",
    5: "Banking",
    6: "Commerce and Trade",
    7: "Conservation",
    8: "Corporations",
    9: "Counties",
    10: "Courts and Judicial Procedure",
    11: "Crimes and Criminal Procedure",
    12: "Decedents' Estates and Fiduciary Relations",
    13: "Domestic Relations",
    14: "Education",
    15: "Elections",
    16: "Health and Safety",
    17: "Highways",
    18: "Insurance Code",
    19: "Labor",
    20: "Military and Civil Defense",
    21: "Motor Vehicles",
    22: "Municipalities",
    23: "Navigation and Waters",
    24: "Professions and Occupations",
    25: "Property",
    26: "Public Utilities",
    27: "Religion",
    28: "Sports and Amusements",
    29: "State Government",
    30: "State Taxes",
    31: "Welfare",
}

# Running headers / footers / cover noise
SKIP_LINE = re.compile(
    r"^(Cover Page|Page\s+\d+|Title\s+\d+\s*-\s*.+|NOTICE:|DISCLAIMER:|"
    r"Chap\.\s+\d+|Subchap\.\s+[IVXLC\d]+|"
    r"The Delaware Code appearing on this site|"
    r"editorial staff of LexisNexis|"
    r"Assembly, and is considered an official|"
    r"includes all acts enacted as of|"
    r"With respect to the Delaware Code documents|"
    r"the State of Delaware nor any of its employees|"
    r"warranties of merchantability|"
    r"that its use would not infringe|"
    r"Please seek legal counsel)",
    re.I,
)

PART_RE = re.compile(r"^Part\s+([IVXLC]+|\d+)\b\.?\s*(.*)$", re.I)
CHAPTER_RE = re.compile(r"^Chapter\s+(\d+[A-Za-z]?)\b\.?\s*(.*)$", re.I)
SUBCHAPTER_RE = re.compile(r"^Subchapter\s+([IVXLC]+|\d+[A-Za-z]?)\b\.?\s*(.*)$", re.I)
SENTENCE_CONTINUATION = re.compile(
    r"^(of|shall|may|is|are|to|for|and|or|the|this|that|which|in|on|under)\b",
    re.I,
)
SECTION_RE = re.compile(
    r"^§§?\s*(\d+[A-Za-z]*(?:\s*[-–—]\s*\d+[A-Za-z]*)?)\.?\s*(.*)$"
)
HISTORY_RE = re.compile(
    r"^\((?:\d+\s+Del\.\s*C\.|Code\s+\d+|Del\.\s*Laws)",
    re.I,
)
# Outline markers: ---(a), (a), ---(1), (i), etc.
MARKER_RE = re.compile(
    r"^(?:---\s*)?"
    r"(?:"
    r"\(([a-z])\)|"          # (a)
    r"\(([0-9]+)\)|"         # (1)
    r"\(([A-Z])\)|"          # (A)
    r"\(([ivxlcdm]+)\)|"     # (i) roman
    r"\(([a-z]{2})\)|"       # (aa)
    r"([a-z])\.\s|"          # a.
    r"([0-9]+)\.\s"          # 1.
    r")"
    r"\s*(.*)$"
)

FOOTER_TITLE_RE = re.compile(r"^Title\s+\d+\b", re.I)


@dataclass
class Node:
    id: str
    kind: str
    number: str
    heading: str
    text: str = ""
    children: list = field(default_factory=list)

    def append_text(self, chunk: str) -> None:
        chunk = clean_spaces(chunk)
        if not chunk:
            return
        if self.text:
            # Join wrapped lines with a space unless ending mid-hyphenation-ish
            if self.text.endswith("-") and chunk[:1].islower():
                self.text = self.text[:-1] + chunk
            else:
                self.text += " " + chunk
        else:
            self.text = chunk


def clean_spaces(s: str) -> str:
    return re.sub(r"[ \t]+", " ", s).strip()


def match_structural(header_re: re.Pattern, line: str):
    """Match Part/Chapter/Subchapter headers, ignoring mid-sentence references."""
    m = header_re.match(line)
    if not m:
        return None
    rest = clean_spaces(m.group(2) or "")
    if not rest:
        return m
    # e.g. "Part I of this title shall be known..." is body text, not a header
    if SENTENCE_CONTINUATION.match(rest):
        return None
    if len(rest) > 100 and rest.endswith("."):
        return None
    return m


def marker_rank(groups: tuple) -> tuple[int, str]:
    """Return (rank, display_label) for nesting. Lower rank = higher level."""
    letter, num, upper, roman, double, letter_dot, num_dot, rest = groups
    if letter:
        return 10, f"({letter})"
    if double:
        return 11, f"({double})"
    if num:
        return 20, f"({num})"
    if upper:
        return 30, f"({upper})"
    if roman:
        # Prefer roman lowercase interpretation
        return 40, f"({roman})"
    if letter_dot:
        return 30, f"{letter_dot}."
    if num_dot:
        return 20, f"{num_dot}."
    return 99, "?"


def extract_pdf_text(path: Path) -> str:
    reader = PdfReader(str(path))
    pages = []
    for page in reader.pages:
        pages.append(page.extract_text() or "")
    return "\n".join(pages)


def look_like_heading_continuation(line: str) -> bool:
    if not line:
        return False
    if SKIP_LINE.match(line) or FOOTER_TITLE_RE.match(line):
        return False
    if (
        match_structural(PART_RE, line)
        or match_structural(CHAPTER_RE, line)
        or match_structural(SUBCHAPTER_RE, line)
    ):
        return False
    if SECTION_RE.match(line) or MARKER_RE.match(line) or HISTORY_RE.match(line):
        return False
    # Likely a chapter/subchapter title line in ALL CAPS or Title Case
    return True


def parse_title(title_num: int, pdf_path: Path) -> dict:
    raw = extract_pdf_text(pdf_path)
    lines = [clean_spaces(ln) for ln in raw.splitlines()]
    lines = [ln for ln in lines if ln]

    title_name = TITLE_NAMES[title_num]
    root = Node(
        id=f"title-{title_num}",
        kind="title",
        number=str(title_num),
        heading=title_name,
    )

    current_part: Node | None = None
    current_chapter: Node | None = None
    current_subchapter: Node | None = None
    current_section: Node | None = None
    # stack of (rank, node) for outline under a section
    outline_stack: list[tuple[int, Node]] = []
    pending_heading_for: Node | None = None
    seq = 0

    def next_id(prefix: str) -> str:
        nonlocal seq
        seq += 1
        return f"{prefix}-{title_num}-{seq}"

    def container_for_section() -> Node:
        if current_subchapter is not None:
            return current_subchapter
        if current_chapter is not None:
            return current_chapter
        if current_part is not None:
            return current_part
        return root

    def active_text_node() -> Node:
        if outline_stack:
            return outline_stack[-1][1]
        if current_section is not None:
            return current_section
        return container_for_section()

    i = 0
    while i < len(lines):
        line = lines[i]

        if SKIP_LINE.match(line) or line == "Cover Page":
            i += 1
            continue

        # Absorb pending ALL-CAPS / descriptive heading onto last structural node
        if pending_heading_for is not None and look_like_heading_continuation(line):
            if not pending_heading_for.heading:
                pending_heading_for.heading = line
            elif len(pending_heading_for.heading) < 120 and not pending_heading_for.text:
                # allow multi-line heading
                pending_heading_for.heading = clean_spaces(
                    pending_heading_for.heading + " " + line
                )
            else:
                pending_heading_for = None
                # fall through to normal handling of this line
                pass
            if pending_heading_for is not None:
                i += 1
                continue

        pending_heading_for = None

        m = match_structural(PART_RE, line)
        if m:
            current_part = Node(
                id=next_id("part"),
                kind="part",
                number=m.group(1),
                heading=clean_spaces(m.group(2)),
            )
            root.children.append(current_part)
            current_chapter = None
            current_subchapter = None
            current_section = None
            outline_stack = []
            pending_heading_for = current_part if not current_part.heading else None
            i += 1
            continue

        m = match_structural(CHAPTER_RE, line)
        if m:
            parent = current_part if current_part is not None else root
            current_chapter = Node(
                id=next_id("chapter"),
                kind="chapter",
                number=m.group(1),
                heading=clean_spaces(m.group(2)),
            )
            parent.children.append(current_chapter)
            current_subchapter = None
            current_section = None
            outline_stack = []
            pending_heading_for = current_chapter if not current_chapter.heading else None
            i += 1
            continue

        m = match_structural(SUBCHAPTER_RE, line)
        if m:
            parent = current_chapter if current_chapter is not None else (
                current_part if current_part is not None else root
            )
            current_subchapter = Node(
                id=next_id("subchapter"),
                kind="subchapter",
                number=m.group(1),
                heading=clean_spaces(m.group(2)),
            )
            parent.children.append(current_subchapter)
            current_section = None
            outline_stack = []
            pending_heading_for = (
                current_subchapter if not current_subchapter.heading else None
            )
            i += 1
            continue

        m = SECTION_RE.match(line)
        if m:
            parent = container_for_section()
            heading = clean_spaces(m.group(2))
            current_section = Node(
                id=next_id("section"),
                kind="section",
                number=m.group(1).replace(" ", ""),
                heading=heading,
            )
            parent.children.append(current_section)
            outline_stack = []
            pending_heading_for = None
            i += 1
            continue

        if HISTORY_RE.match(line) and current_section is not None:
            # Keep history notes on the section as a trailing annotation
            hist = line
            # continue absorbing wrapped history lines that start mid-paren content
            j = i + 1
            while j < len(lines):
                nxt = lines[j]
                if (
                    SKIP_LINE.match(nxt)
                    or match_structural(PART_RE, nxt)
                    or match_structural(CHAPTER_RE, nxt)
                    or match_structural(SUBCHAPTER_RE, nxt)
                    or SECTION_RE.match(nxt)
                    or MARKER_RE.match(nxt)
                    or HISTORY_RE.match(nxt)
                ):
                    break
                # history wrap: typically continues until closing paren
                hist = clean_spaces(hist + " " + nxt)
                j += 1
                if hist.rstrip().endswith(")"):
                    break
            current_section.append_text("\n\n[History] " + hist)
            i = j
            continue

        m = MARKER_RE.match(line)
        if m and current_section is not None:
            groups = m.groups()
            rank, label = marker_rank(groups)
            body = clean_spaces(groups[-1] or "")
            # Strip leading --- artifact if body empty and original had only dashes
            if line.startswith("---") and not any(groups[:-1]):
                # lead-in paragraph without a lettered marker
                current_section.append_text(line.lstrip("-").strip())
                i += 1
                continue

            node = Node(
                id=next_id("unit"),
                kind="unit",
                number=label,
                heading=label,
                text=body,
            )
            while outline_stack and outline_stack[-1][0] >= rank:
                outline_stack.pop()
            if outline_stack:
                outline_stack[-1][1].children.append(node)
            else:
                current_section.children.append(node)
            outline_stack.append((rank, node))
            i += 1
            continue

        # Lead-in text with --- but no marker
        if line.startswith("---") and current_section is not None:
            active_text_node().append_text(line.lstrip("-").strip())
            i += 1
            continue

        # Ordinary continuation text
        if current_section is not None or current_chapter is not None:
            # Ignore bare page numbers / short noise
            if re.fullmatch(r"\d+", line):
                i += 1
                continue
            active_text_node().append_text(line)
        i += 1

    def count_kinds(node: Node, counts: dict) -> None:
        counts[node.kind] = counts.get(node.kind, 0) + 1
        for child in node.children:
            count_kinds(child, counts)

    counts: dict[str, int] = {}
    count_kinds(root, counts)

    return {
        "titleNumber": title_num,
        "titleName": title_name,
        "sourcePdf": f"data/pdfs/title{title_num}.pdf",
        "parsedAtUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "stats": counts,
        "root": asdict(root),
    }


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    # Remove legacy per-title search dumps if present (search is done in-app).
    for stale in OUT_DIR.glob("*.search.json"):
        stale.unlink()

    index = {
        "source": "Local PDFs archived from https://delcode.delaware.gov/ (downloaded once).",
        "titles": [],
    }

    for n in range(1, 32):
        pdf = PDF_DIR / f"title{n}.pdf"
        if not pdf.exists():
            print(f"MISSING PDF: {pdf}", file=sys.stderr)
            return 1
        print(f"Parsing title {n}/31 ...", flush=True)
        data = parse_title(n, pdf)
        out_path = OUT_DIR / f"title{n}.json"
        out_path.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")

        index["titles"].append(
            {
                "number": n,
                "name": TITLE_NAMES[n],
                "file": f"title{n}.json",
                "stats": data["stats"],
                "pdf": f"../pdfs/title{n}.pdf",
            }
        )
        print(f"  stats: {data['stats']}", flush=True)

    (OUT_DIR / "index.json").write_text(json.dumps(index, indent=2), encoding="utf-8")
    print("Wrote", OUT_DIR / "index.json")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
