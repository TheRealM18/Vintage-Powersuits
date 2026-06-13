# Features

[← Home](Home.md)

## Armor pieces

Four wearable pieces, each storing its own energy:

| Piece | Core? | Max energy | Default module |
|-------|-------|-----------|----------------|
| Powered Chestplate | Yes | 200,000 EU | Sprint Assist |
| Powered Leggings | No | 80,000 EU | Jump Assist |
| Powered Helmet | No | 60,000 EU | Night Vision |
| Powered Boots | No | 60,000 EU | Fall Damage Negation |

The **chestplate is the core**: flight and sprint logic read from it. The
other pieces add protection, energy capacity, and their own default modules.

Energy is stored per itemstack and persists across saves — it travels with
the item. The unit is a generic "EU"; scale it to match VE wattage when you
wire charging.

## Flight

Press **R** to toggle. Requires the Flight module installed on the core piece
and energy remaining. Flight drains energy every tick while active and cuts
out when energy hits zero. It is server-authoritative, so it can't be spoofed
client-side.

> Flight uses the engine's free-move flag. Some multiplayer servers restrict
> this; if it doesn't behave, see [Architecture](Architecture.md) for swapping
> in jetpack-style thrust.

## Sprint assist

While the core piece has the Sprint Assist module and you hold sprint, walk
speed gets a +40% bonus and energy drains. Releasing sprint or running out of
energy removes the bonus automatically.

## Module config

Press **U** for a panel listing your installed modules with toggle buttons.
Toggling sends a packet to the server, which updates the module state on the
armor.

## HUD energy readout

A HUD element scaffold exists (`HudEnergyBar`) that reads the last synced
energy values. It is intentionally minimal — see [Modding](Modding.md) to flesh
it into a live bar.
