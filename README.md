# Hideout We Deserve - UI Improvements

The first in a series of hideout QoL and redesign mods. SPT 4.1.6

## Features

- Station upgrade requirements on hover
- Active crafts with remaining time on hover
- Hide the bottom scroll bar
- Open station menu without zooming
- Item markers for current and future upgrade requirements
- Item markers for favorited craft ingredients
- Craft folders (up to 5) - toggle: `EnableFolders`
- Active craft bar - toggle: `EnableActiveCraftBar`

Hideout-only overlays and controls are suspended during raids. Item markers and
their tooltips remain available in raid, using a snapshot of hideout requirements
that is refreshed after returning to the menus. If no snapshot is available yet,
the mod loads one when the hideout data becomes available. Requirement counts
can therefore be temporarily out of date during a raid.

## Install

Download `Hideout-We-Deserve-UI-Improvements-2.0.0.zip` from [Releases](https://github.com/HeroicLarvy/Hideout-We-Deserve-UI-Improvements/releases), then extract it into your SPT folder. The DLL is already inside `BepInEx\plugins`.

## Compatibility
Should be compatible with literally everything but I have extensively tested it with:
- Tyfon's UI fixes
- Fika
