# Hideout We Deserve - UI Improvements

Client-side hideout UI improvements for SPT 4.1.6 by **HeroicLarvy**. The plugin is designed for local client use and is compatible with Fika when installed by each player.

## Features

- Hover over hideout stations to view active/completed crafts and upgrade requirements.
- Toggle the bottom hideout station bar, hover item names, and craft/upgrade details.
- Open station menus without zooming the camera.

## Source

This GitHub repository contains the complete C# plugin source and project file. Compiled binaries are distributed separately in the release ZIP.

Build with the .NET 8 SDK and your SPT installation path:

```powershell
dotnet build HideoutWeDeserveUIImprovements.csproj -c Release -p:SPTPath="D:\Random Games\SPT"
```

The generated DLL is written to `bin\Release\netstandard2.1\HideoutWeDeserveUIImprovements.dll`.

## Install a release

Download `Hideout We Deserve - UI Improvements.zip` and extract it into the SPT installation folder. The archive already contains the `BepInEx\plugins\HideoutWeDeserveUIImprovements.dll` path.

## Configuration

The plugin settings are created in:

```text
BepInEx\config\com.heroiclarvy.hideoutwedeserveuiimprovements.cfg
```

## License

MIT. See [LICENSE](./LICENSE).
