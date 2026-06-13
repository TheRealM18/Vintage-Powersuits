# Content: Items, Recipes & the Module Installer

[← Home](Home.md)

## Armor pieces

Four craftable pieces (see [Features](Features.md) for stats). Each is made on
the crafting grid from steel plates and temporal gears — deliberately end-game,
matching the Vintage Engineering tier. The chest and several pieces also embed
a module item in their recipe.

## Module items

A `powermodule` item with five variants, one per module:
flight, sprint assist, jump assist, fall damage negation, night vision. Each is
crafted from a steel-plate / temporal-gear ring around a thematic core (the
flight module uses a large temporal gear). The module item stores its
`moduleCode` so the installer knows what to apply.

## The Module Installer block

Craft the installer, place it, and right-click to open its GUI. It has two
slots:

- **Armor** — insert a powersuit piece
- **Module** — insert a power-module item

Click **Install Module**. The module's capability is written onto the armor and
the module item is consumed. Installing a module the armor already has is
rejected. The operation runs server-side so it can't be spoofed.

## Recipe ingredients & balance

All recipes currently use **vanilla** ingredients (steel plates, temporal
gears) that are verified to exist. This gates the gear behind late-game
metalworking. To make it true Vintage Engineering end-game, swap the
ingredient codes for VE machine outputs (e.g. circuit boards, processed plates)
once you confirm those codes from the VE assembly — see
[VE Integration](VE-Integration.md).

## Temporary models

Item icons and block textures are procedural placeholders so everything is
visible now. Worn armor reuses the vanilla plate-armor models via `attachShape`
pointing at `game:entity/humanoid/seraph/armor/plate/*`. Replace these with
custom shapes/textures when ready; nothing else depends on them.
