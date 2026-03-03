# SegmentPlayer

Windows portable media player built with `C# .NET 8 + WPF + LibVLCSharp`.

## Prerequisites

- Windows 10/11
- .NET 8 SDK

## Run

```powershell
dotnet restore
dotnet run --project PortablePlayer.csproj
```

## Portable Packaging

Use the packaging script to build a cleaner folder hierarchy:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

Default output:

- `publish/SegmentPlayer-win-x64-portable/SegmentPlayer.exe` (root launcher)
- `publish/SegmentPlayer-win-x64-portable/app/` (player runtime and dependencies)
- `publish/SegmentPlayer-win-x64-portable/app/config/` (settings)
- `publish/SegmentPlayer-win-x64-portable/app/media_groups/` (media root)

## Folder Layout (portable mode)

- `config/settings.json`: application settings
- `media_groups/`: media groups root
- `cache/thumbnails/`: thumbnail cache

Each subfolder in `media_groups/` is one media group.

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
