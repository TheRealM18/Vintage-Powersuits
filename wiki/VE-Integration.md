# Vintage Engineering Integration

[← Home](Home.md)

**Status: wired and implemented** against Vintage Engineering's real
`IChargeableItem` API (FlexibleGames/VintageEngineering, VE 0.5.x, game 1.22.3).

## How it works

`ItemVEPowersuit` implements `VintageEngineering.Electrical.IChargeableItem`.
When you drop a suit piece into a VE charger (LV/MV/HV), VE's charger block
entity (`BELVCharger.OnSimTick`) detects the interface on the item's collectible
and pushes power in via `RatedPower` / `ReceivePower`. Power flows straight into
the suit's own EU energy store (`EnergyStore`), the same value the modules drain.

### The per-stack binding

VE's `IChargeableItem` getters (`CurrentPower`, `MaxPower`, `MaxPPS`,
`RatedPower`) take no `ItemStack`, but a single `Item` instance is shared by
every stack in the world. To resolve power for the *specific* suit being
charged, a small Harmony patch (`VEChargerPatch`) wraps `BELVCharger.OnSimTick`
and binds that charger's input stack to a thread-local context for the duration
of the tick. The interface members read/write that bound stack. The patch is
reflective and self-disabling: if VE renames the method, it no-ops cleanly.

### Files

- `src/Items/ItemVEPowersuit.cs` — implements `IChargeableItem`.
- `src/Systems/VEPowerAdapter.cs` — the EU↔VE power math (rated receive,
  receive-with-leftover, extract-with-remainder), matching VE's contracts.
- `src/Systems/VEChargerPatch.cs` — Harmony patch for per-stack binding.
- `src/Systems/EnergyStore.cs` — adds `MaxPPS` storage.

## Tuning

- `maxEnergy` (per piece, in the itemtype JSON) — buffer capacity in EU.
- `maxPPS` (per piece, itemtype JSON) — max EU/second the charger can push in.
  `0` means no per-tick cap.
- Module drain rates live in `src/Modules/ModuleRegistry.cs`. Scale these and
  `maxPPS` together so charge time vs. flight time feels right against VE's
  generator output.

## Build requirement

The `.csproj` references `vintageengineering.dll`. VE ships zipped, so extract
`vintageengineering.dll` from `VintagestoryData\Mods\vintageengineering_*.zip`
and either place it at `VintagestoryData\Mods\vintageengineering.dll` or pass
`-p:VintageEngineeringDll=C:\full\path\to\vintageengineering.dll` to the build.
`0Harmony.dll` (already referenced) ships with the game.
