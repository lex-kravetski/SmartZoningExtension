# Smart Zoning Extension

Smart Zoning Extension adds road-side zoning controls to Cities: Skylines II. It uses the game's built-in road-side zoning state, but lets you work with it directly in the regular **Zone** tool instead of switching to the Roads tool.

The equivalent vanilla road-building option is the **zoning toggle**: it controls whether zoning is allowed on both sides of a road, one side, or neither while building or upgrading roads. This mod adds extra controls for existing road segments while the Zone tool is active. [Official patch notes](https://store.steampowered.com/news/posts/?enddate=1773824603&feed=steam_community_announcements)

## Using the mod

1. Enable **Smart Zoning Extension** in your active playset and load a city.
2. In **Options → Mods → Smart Zoning Extension**, leave **Enabled** checked. Uncheck it to temporarily turn off all mod features without removing the mod.
3. Select the game's standard **Zone** tool and move the cursor over a road segment. The side of the road nearest the cursor is the active side for clicks.

### Hover preview

- When zoning is enabled on the hovered side, the game's zoning cells are highlighted. Enabled sides of other roads that compete for those cells are marked red.
- When zoning is disabled on the hovered side, a blue overlay marks that half of the road and shows where zoning will be enabled by a right-click.
- The preview is hidden while a mouse button is held, so it does not interfere with painting or erasing zones.

### Mouse controls

| Input | Effect |
| --- | --- |
| Left-click a road | Disable zoning on the side nearest the cursor. |
| Right-click a road | Enable zoning on the side nearest the cursor. |
| **Alt** + left-click | Disable zoning on the enabled sides of competing roads. The hovered side is left unchanged. |
| **Alt** + right-click | Enable zoning on competing road sides, including sides that are currently disabled. |
| Middle-click a road | Rebalance zoning priority for the enabled competing sides. The mod temporarily disables them, then restores them from the least-overlapping side to the most-overlapping side. This gives the hovered side a chance to claim more of the shared area. |

The red preview and Alt-click actions use the same competition check, so the highlighted road sides correspond to the sides the Alt-click will affect. The middle-click considers enabled competitors only.

## Build and install locally

The project uses the Cities: Skylines II modding toolchain. From the repository root, run:

```powershell
dotnet build .\ZoneDepth\ZoneDepth.csproj -c Release
```

The toolchain post-processes and deploys the mod to:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\ZoneDepth
```
