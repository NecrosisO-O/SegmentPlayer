# SegmentPlayer

Windows portable media player built with `C# .NET 8 + WPF + LibVLCSharp`.

## Features

- Video groups and image groups (`playlist.json` based playback).
- Auto mode and manual mode with toolbar controls:
  - `Prev Group`, `Prev`, `Next`, `Next Group`
  - mode toggle
  - navigation panel jump
- Center playback indicator:
  - `A` for auto
  - `M` for manual
  - `A X` for infinite state
  - `A current/total` for finite loops or seconds
- Group selection page as a list view with:
  - preview thumbnail
  - group name
  - media type
  - item count
  - validity status
  - row actions (`Play` / `Edit`)
- Playlist editor:
  - reorder items
  - edit loop/seconds value
  - one-level adjacent grouping
  - normalize group order
- Startup scan and runtime refresh:
  - auto-generate missing `playlist.json`
  - detect mixed media groups as invalid
  - merge new/missing files on refresh
- UI localization: `zh-CN` and `en-US` (takes effect on next launch).

## Supported Media

- Video: `.mp4`, `.gif`
- Image: `.jpg`, `.jpeg`, `.png`, `.bmp`, `.webp`

## Development Prerequisites

- Windows 10/11
- .NET 8 SDK

## Run From Source

```powershell
dotnet restore
dotnet run --project PortablePlayer.csproj
```

## Build Portable Package

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

Default output:

- `publish/SegmentPlayer-win-x64-portable/SegmentPlayer.exe` (root launcher)
- `publish/SegmentPlayer-win-x64-portable/app/` (runtime and dependencies)
- `publish/SegmentPlayer-win-x64-portable/app/config/` (settings)
- `publish/SegmentPlayer-win-x64-portable/app/media_groups/` (media root)
- `publish/SegmentPlayer-win-x64-portable/app/cache/thumbnails/` (thumbnail cache)
- `publish/SegmentPlayer-win-x64-portable/app/logs/` (runtime logs)

## Portable Data Layout

In portable mode, data is stored under the packaged `app/` folder:

- `app/config/settings.json`: application settings
- `app/media_groups/`: media groups root
- `app/cache/thumbnails/`: thumbnail cache
- `app/logs/`: runtime logs

Each first-level subfolder in `app/media_groups/` is one media group.

## playlist.json

```json
{
  "version": 1,
  "mediaType": "video",
  "items": [
    { "file": "001.mp4", "loops": 1, "group": 1 },
    { "file": "002.mp4", "loops": -1, "group": 1 },
    { "file": "003.mp4", "loops": 0, "group": null }
  ]
}
```

Rules:

- `mediaType`: `video` or `image` (no mixing in one group)
- Video `loops`: `-1` infinite, `0` skip, positive integer = loop count
- Image `loops`: `-1` infinite, `0` skip, positive decimal (1 decimal) = seconds
- `group`: integer or `null`
