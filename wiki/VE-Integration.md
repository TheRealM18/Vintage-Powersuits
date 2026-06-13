# Vintage Engineering Integration

[← Home](Home.md)

This is the **one part you must finish yourself**, and the reason is honest:
Vintage Engineering's exact energy API isn't something this scaffold can
reliably guess. VE is a solo-dev project whose internals shift between
versions, so the adapter is written against an assumed interface rather than
fabricated class names that would compile against nothing real.

Everything else in the mod runs as far as code goes, but **Vintage Engineering
is now a required dependency** (declared in `modinfo.json` and referenced in
the build), so the game will refuse to load this mod unless VE is installed,
and the build will fail unless the VE dll is on disk. The armor still can't
recharge from VE until the adapter below is wired — the dependency is enforced,
the power transfer is not yet implemented.

## Build requirement

Because the `.csproj` now references `vintageengineering.dll`, the build needs
that dll on disk. VE ships as a ZIP, so extract `vintageengineering.dll` from
`VintagestoryData\Mods\vintageengineering_*.zip` and either:

- place it at `VintagestoryData\Mods\vintageengineering.dll`, or
- pass its location to the build:
  `dotnet build -p:VintageEngineeringDll=C:\full\path\to\vintageengineering.dll`

If it's missing, the pre-build check fails with a message telling you exactly
this, rather than a cryptic resolve error.

## The file

`src/Systems/VEPowerAdapter.cs`

It exposes two stub methods and a flag, `VEIntegrationWired`, currently set to
`false`. The item tooltip shows a warning while it's false; flip it to `true`
once you've verified the real calls.

## What to look for in VE

Open the Vintage Engineering `.dll` in your IDE, or in ILSpy / dnSpy, and find
how its charging station moves power into items. You're typically looking for:

- An interface implemented by items that can receive power — something like
  `IElectricalItem` / `IEnergyStorageItem` with a method along the lines of
  `ReceivePower(float watts, float dt, bool simulate)`.
- Or an itemstack-attribute convention the charging station writes into.

## Two integration strategies

**A — Implement VE's interface on the armor.**
Make `ItemVEPowersuit` implement VE's energy interface and forward incoming
power to `VEPowerAdapter.ReceiveFromVE`, which already handles clamping to the
piece's max energy. VE's charging station then pushes power in for you.

**B — Stay self-contained.**
Keep all energy in the mod's own attribute (the current design) and either add
your own charging block/recipe, or convert power from VE inside
`TryDrawFromVE` using VE's public API.

## Finishing checklist

1. Add a reference to the VE `.dll` in `VEPowersuit.csproj` (a commented-out
   block is already there as a template).
2. Implement the real calls in `TryDrawFromVE` and/or `ReceiveFromVE`.
3. Set `VEIntegrationWired = true`.
4. Test charging on a VE charging station.
5. Scale the EU values in [Modules](Modules.md) to match VE's wattage so drain
   rates feel right.
