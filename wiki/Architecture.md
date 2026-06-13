# Architecture

[← Home](Home.md)

## File map

```
VEPowersuit/
├── modinfo.json                  Mod metadata + dependencies
├── VEPowersuit.csproj             Build config & assembly references
├── assets/vepowersuit/
│   ├── itemtypes/                One JSON per armor piece
│   ├── lang/en.json              Display strings
│   ├── textures/                 (placeholder)
│   └── shapes/                   (placeholder)
└── src/
    ├── VEPowersuitModSystem.cs    Entry point: registration, ticking, flight, keybinds, net
    ├── Items/ItemVEPowersuit.cs   The wearable item class
    ├── Modules/ModuleRegistry.cs Module catalogue
    ├── Systems/
    │   ├── EnergyStore.cs        Energy + module state on the itemstack
    │   └── VEPowerAdapter.cs     VE charging hook (you finish this)
    ├── Network/Packets.cs        Client <-> server packet types
    └── Gui/
        ├── GuiDialogModules.cs   Module config panel (U)
        └── HudEnergyBar.cs       Energy HUD stub
```

## How a frame of gameplay flows

1. **Server tick (1 Hz)** in `VEPowersuitModSystem.OnServerTick` finds each
   online player's core chestplate, drains active per-tick modules, applies
   sprint assist, marks the slot dirty, and syncs energy to the client.

2. **Keypress** — **R** sends a `ToggleFlightPacket`; **U** opens the module
   GUI. The server validates (module present? energy left?) before acting, so
   the client can't force flight on its own.

3. **Module toggle** — the GUI sends a `ToggleModulePacket`; the server flips
   the module flag stored on the armor.

4. **Energy sync** — the server pushes an `EnergySyncPacket` so the client HUD
   can show live values without reading the inventory slot every frame.

## State & persistence

All energy and module state lives in the itemstack's `TreeAttribute` via
`EnergyStore`. This means it persists in saves and travels with the item —
no separate database or block-entity bookkeeping.

## Why the chestplate is special

`ItemVEPowersuit.IsCore` (set in the chestplate JSON) marks the piece the
movement systems read from. `GetChestSlot` scans the character inventory for
the core piece; if it's missing, flight stops. Other pieces contribute
protection, capacity, and their own default modules but aren't queried for
flight/sprint.

## Verified vs. assumed API

The core Vintage Story calls (hotkeys, networking, wearable items, tick
listeners, tree attributes) are written against the documented, stable 1.20+
API. The **only** assumed surface is `VEPowerAdapter.cs` — see
[VE Integration](VE-Integration.md).
