<div align="center">
<a href="https://github.com/kivylius/asset-manager/" rel="noopener" target="_blank"><img src="https://img.shields.io/badge/NOTICE:-This_project_is_in_early_development!-orange.svg" alt="This_project_is_in_early_development"></a>
<p> </p>
</div>

<!-- markdownlint-disable-next-line -->
<p align="center">
  <a href="https://github.com/kivylius/asset-manager/" rel="noopener" target="_blank"><img src="cover.png" alt="asset maanger cover" style="border-radius:8px"></a>
</p>

<h1 align="center">Asset-Manager (Godot)</h1>

<div align="center">

[![License](https://img.shields.io/badge/license-GPL--3.0-blue.svg)](https://github.com/kivylius/asset-manager/blob/main/LICENSE)
[![Versions](https://img.shields.io/badge/versions-v1.0.6-green.svg)](https://github.com/kivylius/asset-manager/releases)
[![Platform](https://img.shields.io/badge/godot-4.x-red.svg)](https://github.com/kivylius/asset-manager)
[![Language](https://img.shields.io/badge/Language-GDscript-pink.svg)](https://github.com/kivylius/asset-manager)
[![Made with Godot](https://img.shields.io/badge/Made%20with-Godot-478CBF?style=flat&logo=godot%20engine&logoColor=white)](https://godotengine.org)

</div>

Manage all your Godot assets in a single place, get previews and send files directly into project(s). Supports Godot 4.x on Mac/Linux & Windows.

## About this fork

This fork makes the addon usable on very large libraries, including ones on a
network share. It was built against a 318,000-file library on a NAS, where the
original froze the editor for hours on Rebuild.

- Scanning, the index, search and sidebar counts run in C# on background
  threads. The editor stays responsive during a scan.
- **Scan** replaces Rebuild. It only touches new, changed and removed files,
  and it can be stopped. A first scan of 318k files over SMB took under 3
  minutes, and a rescan with nothing changed reported no changes.
- **Rescan Folder** (right-click a folder in the sidebar) scans one folder.
  Add and Send to Asset Manager scan only the pack they just wrote.
- Thumbnails are made on demand for the page you are looking at, never for the
  whole library up front. They are cached in the workspace as before, so
  thumbnails made by the original addon are reused.
- Tag edits write a small `tags.bin` instead of rewriting the whole index.
  An existing `index.db` is migrated on first open.

The trade-off: this fork needs the .NET (C#) build of Godot.

## Key Features

- Shared asset library outside any project
- Live preview for models, materials, shaders, effects/vfx, hdri's, audio, video, images & themes
- Drag-and-drop any asset pack or file straight in
- Search & filtering across your whole assets directory
- Multi-workspace support & shared assets for collaboration
- Tagging assets with custom names
- On-demand thumbnails for all asset types
- Export destination per project/game
- Grid & list toggable views
- Right-click to send project assets to the library

## Requirements:

- Godot version: the .NET build, 4.7+ (this fork is built and tested on 4.7.1 .NET)
- .NET SDK 8 or newer, to build the C# part
- Platform: Windows, macOS, Linux (no native code, should work anywhere)

A project using the addon must be a C# project: in Godot, use
*Project > Tools > C# > Create C# solution* once, then build.

## Installation

You can clone this repo and add `/addons/**` to your project or use the Godot Asset Store:

[![Godot Engine](https://img.shields.io/badge/Godot-Asset_Store_⧉-%23FFFFFF.svg?logo=godot-engine)](https://store.godotengine.org/asset/kivylius/asset-manager/)

## Development

The repo root is a Godot .NET project hosting the addon.

- `addons/asset_manager/core/catalog/` is the C# catalog. Its top level has no
  Godot dependency; `godot/` holds the thin bridge the GDScript UI calls.
- `dotnet test tests/AssetManager.Tests` runs the catalog's unit tests.
- `tests/smoke/smoke_catalog.gd` checks a real workspace headlessly. It
  measures main-thread frame gaps during a full scan, checks that a rescan
  with nothing changed is a no-op, and times a query and one page of
  thumbnails:

  ```
  godot --headless --path . -s res://tests/smoke/smoke_catalog.gd -- <workspace>
  ```

Two rules for the C#/GDScript boundary, both needed to avoid a crash when the
4.7.1 editor exits:

- Pass only strings, numbers and packed arrays across it. Nested data goes as
  JSON.
- Connect bound methods to the C# signals, never lambdas.

## Credits

Forked from [Global-Asset-Manager](https://github.com/sn1ks0h/Global-Asset-Manager)

---

Copyright (C) 2026 Kivylius

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
