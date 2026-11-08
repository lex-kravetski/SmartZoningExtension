# Smart Zoning Extension

A Cities: Skylines II mod for managing road-side zoning and resolving competition between nearby zoning areas.

## Features

- Hover over a road while using the Zone tool to preview the selected side. Enabled zoning cells are highlighted; a blue road-side marker indicates that zoning is disabled on that side.
- Red markers show enabled sides of nearby roads whose zoning areas compete with the hovered side.
- Left-click a road side to disable its zoning; right-click to enable it.
- Hold **Alt** while left-clicking to disable competing road sides, or while right-clicking to enable them.
- Middle-click a road side to rebalance competing zoning priorities. The mod temporarily disables the enabled competitors, then restores them from the least-overlapping side to the most-overlapping side so the hovered area can claim more space.
- The **Enabled** option in **Options → Mods → Smart Zoning Extension** turns all mod features on or off. It is enabled by default.

## Build and install locally

The project uses the Cities: Skylines II modding toolchain. From the repository root, run:

```powershell
dotnet build .\ZoneDepth\ZoneDepth.csproj -c Release
```

The toolchain post-processes and deploys the mod to:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\ZoneDepth
```

Enable **Smart Zoning Extension** in the active in-game playset and load a city to use it.
