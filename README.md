# Delaware Code Viewer

Local **VB.NET WPF** desktop app for browsing and searching the Delaware Code (Titles 1–31).

## Features

- Hierarchical navigation: Title → Part → Chapter → Subchapter → Section → subdivisions (e.g. `(a)`, `(1)`, `(i)`)
- Full-text search across all loaded statute text
- Works entirely offline from files committed to this repository

## Data source (important)

Authenticated title PDFs were downloaded **once** from [delcode.delaware.gov](https://delcode.delaware.gov/) and saved under `data/pdfs/`.

- There are **31 title PDFs** (`title1.pdf` … `title31.pdf`).
- The download script **will not re-fetch** a title if the local file already exists.
- Do **not** point the running app at the live website; it only reads local files.

Manifest: `data/download_manifest.json`

Parsed hierarchy used by the app: `data/titles/*.json` (generated from the local PDFs).

### Re-parse without re-downloading

```bash
python3 -m pip install pypdf
python3 scripts/parse_delaware_pdfs.py
```

### Download script (safe to re-run)

```bash
python3 scripts/download_title_pdfs.py
```

This skips any title PDF that is already present.

## Requirements

- Windows (WPF)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (optional) or `dotnet` CLI

## Run locally

```bash
dotnet restore StatuteViewer.sln
dotnet run --project StatuteViewer/StatuteViewer.vbproj
```

Or open `StatuteViewer.sln` in Visual Studio and press F5.

The app copies `data/titles` into the build output and resolves that folder at runtime.

## Project layout

| Path | Purpose |
|------|---------|
| `StatuteViewer/` | WPF VB.NET application |
| `data/pdfs/` | Archived official title PDFs (source of truth) |
| `data/titles/` | Parsed JSON hierarchy for the UI |
| `scripts/download_title_pdfs.py` | One-time / idempotent PDF archive helper |
| `scripts/parse_delaware_pdfs.py` | Offline PDF → JSON parser |

## Notes

- The Delaware Constitution PDF is not included (Titles 1–31 only, per project scope).
- Parsing is heuristic based on LexisNexis PDF text extraction; odd edge cases in outline nesting can appear in older or unusually formatted chapters.
- Layman’s summaries were deferred and are not part of this version.
