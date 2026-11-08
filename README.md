# Smart Zoning Extension

The game's **Roads** tool has a **zoning toggle** for choosing which sides of a road can be zoned while building or upgrading it. Smart Zoning Extension brings this control to the standard **Zones Tool** and adds previews and priority controls for existing roads.

This means you no longer have to switch to the Roads tool or use paths to change zoning on existing roads.


## How to use

Select the standard **Zones Tool**, select a zone to painting and hover over a road segment to disable or enable this segment zoning, as you would when using the road zoning toggle. 

The side closest to the cursor is the side affected by clicks.

Note: All screenshots were taken with the overlay turned off manually. The mod does not disable the colorful overlays that the game automatically displays when you switch to the **Zones Tool**.


### Preview

- Zoning cells on the hovered side are highlighted.

- Red highlights mark enabled sides of nearby roads whose zoning competes for the hovered area.

- If zoning is disabled on the hovered side, a blue overlay marks that side of the road and previews where zoning will be restored.

- The preview is hidden while a mouse button is held, so it does not interfere with painting or erasing zones.

### Controls

- **Left-click** a road to disable zoning on the side nearest the cursor.

- **Right-click** a road to enable zoning on the side nearest the cursor.

- **Alt + left-click** a road to disable zoning on its enabled competitors highlighted in red. The hovered side is left unchanged.

- **Alt + right-click** a road to enable zoning on competing sides, including sides that are currently disabled.

- **Middle-click** a road to rebalance priority among enabled competing sides in favour of the hovered side. The mod temporarily disables those sides, then restores them starting with the side that overlaps the hovered area least and ending with the one that overlaps it most. This gives the hovered side an opportunity to claim more of the shared area. Rebalancing is intended to preserve cells that are already zoned, but it relies on the game's road-side zoning mechanism and may occasionally leave some cells unzoned.

The red preview and Alt-clicks use the same competition check. Middle-click only processes competitors that are enabled when clicked.

## Mod setting

In **Options → Mods → Smart Zoning Extension**, the **Enabled** option turns all mod features on or off. It is on by default. Uncheck it to temporarily disable the mod without removing it from your playset.
