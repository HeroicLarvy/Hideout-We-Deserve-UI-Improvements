# Hideout We Deserve - UI Improvements

Small quality-of-life improvements for the SPT 4.1.6 hideout. Client-side, and Fika compatible when installed by each player. Tyfon UI Fixes is supported but not required.

## What you get

- Hover over a hideout station to see its upgrade requirements, including item counts and the other stations or traders needed.
- See active crafts and their remaining time, or **Completed** when an item is ready to claim.
- Hide the bottom scroll bar.
- Open a station menu without zooming the camera.
- Mark items used by current and future hideout upgrade requirements. Hover over the marker to replace the normal item-name tooltip with the station, level transition, and owned/needed count. Markers remain visible even when your stash already meets a requirement, so the count can show more owned than needed. The tooltip uses the current/future marker icons to identify each section, separated by a divider. Current stages use green, later stages use blue, and items needed for both use a split marker.
- Mark ingredients for favorited hideout crafts with a separate toggle and color. The tooltip shows each crafted output and the hovered ingredient's owned/needed count.
- Optionally include roubles, dollars, and euros in hideout and favorite-craft markers. Currency is excluded by default; enable the master setting and the individual currency toggles to show markers when a currency is needed. Choose any item-icon corner for markers; bottom-left is the default.
- Grid-item markers are drawn as an overlay anchored to the item image and follow the item through inventory rebinds. The mod leaves the native wishlist view and item visibility under game/Tyfon control.

## Install

Download `Hideout-We-Deserve-UI-Improvements-1.0.0.zip` from [Releases](https://github.com/HeroicLarvy/Hideout-We-Deserve-UI-Improvements/releases), then extract it into your SPT folder. The DLL is already inside `BepInEx\plugins`.

Tyfon UI Fixes is optional. This mod does not require it to load.

The raid item markers are in development on this branch and are not included in the 1.0.0 release ZIP.

For Fika, install the mod on each player's client.

## Settings

Settings are available in the BepInEx configuration file:

`BepInEx\config\com.heroiclarvy.hideoutwedeserveuiimprovements.cfg`

In the `Item Markers` section, configure current, future, and favorited-craft ingredient markers independently and choose their colors and corner. The currency master toggle is followed by the rouble, dollar, and euro options. Currency is excluded by default.

## License

MIT. See [LICENSE](./LICENSE).
