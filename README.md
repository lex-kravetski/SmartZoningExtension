# Smart Zoning Extension

Smart Zoning Extension adds controls for road-side zoning in Cities: Skylines II. The game has a **Zoning Toggle** in the Roads tool for choosing which sides of a road can be zoned while building or upgrading it. This mod brings road-side zoning controls to the standard **Zone** tool and adds previews and priority controls for existing roads.

## How to use

Select the standard **Zone** tool and move the cursor over a road segment. The side nearest the cursor is the side affected by a normal click.

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
- **Middle-click** a road to rebalance priority among enabled competing sides. The mod temporarily disables those sides, then restores them from the least-overlapping to the most-overlapping. This gives the hovered side the opportunity to claim more of the shared area.

The red preview and Alt-clicks use the same competition check. Middle-click only processes competitors that are enabled when clicked.

## Mod setting

In **Options → Mods → Smart Zoning Extension**, the **Enabled** option turns all mod features on or off. It is on by default. Uncheck it to temporarily disable the mod without removing it from your playset.
