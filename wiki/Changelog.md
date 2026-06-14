# Changelog

## 0.3.0 — Vintage Engineering power integration

- `ItemVEPowersuit` now implements `VintageEngineering.Electrical.IChargeableItem`.
  VE chargers (LV/MV/HV) charge the suit directly into its EU energy store.
- Added `VEPowerAdapter` real power math: `RatedReceive`, `ReceiveFromVE`
  (leftover contract), `ExtractToVE` (remainder contract) — matching VE's
  charger expectations from `BELVCharger.OnSimTick`.
- Added `VEChargerPatch` Harmony patch wrapping `BELVCharger.OnSimTick` to bind
  the charged stack per-tick, making VE's parameterless `IChargeableItem`
  getters per-stack correct. Reflective + self-disabling if VE shifts the API.
- `EnergyStore` gained `MaxPPS` storage; armor itemtypes gained `maxPPS` (2000)
  and `chargable: false`.
- `VEIntegrationWired` flipped to true; tooltip now shows a charging hint.

[← Home](Home.md)

All notable changes to the mod are recorded here. Newest first.

## [0.2.0] — Wearable armor fixed; 3 pieces; handbook guide

### Fixed
- **Armor is now actually wearable.** The itemtypes were missing the
  `Wearable` behavior and used invented attribute keys (`wearableAttachment`,
  `clothescategory: "torso"`, `armorSlot`). Rewrote all three to mirror the
  vanilla seraph armor: added `behaviors: [{ "name": "Wearable" }]`, the real
  `clothesCategory` values (`armorbody` / `armorhead` / `armorlegs`),
  `attachableToEntity` with `categoryCode` + worn shape, and a correct
  `protectionModifiers` schema (relativeProtection / flatDamageReduction /
  protectionTier).
- Worn models now reference the vanilla plate-armor shapes
  (`game:entity/humanoid/seraph/armor/plate/{body|head|legs}`) so equipped
  pieces render on the character.

### Changed
- **Reduced to three armor pieces** (head, body, legs) — Vintage Story has only
  three armor slots; there is no foot/boots slot. Removed the leftover
  `boots.png` and the stale boots lang entry. Fall Damage Negation moved to the
  leggings.

### Added
- **In-game handbook guide** explaining how to install modules
  (`config/handbook/101-modules.json` + lang). Each armor piece's handbook page
  also carries an extra section with the same instructions, so players can find
  out how modules work from the item itself.

### Notes
- Verified against the vanilla `wearable/seraph/armor.json` reference and the
  current 1.22.3 environment from the GitHub repo.

## [0.1.12] — (prior state from repo)

## [0.1.10] — Installer networking reworked; warnings cleared

### Fixed
- The installer GUI no longer calls `SendBlockEntityPacket` (whose overloads
  vary by version and caused CS1503). The install now routes through the mod's
  own stable network channel: GUI close → `InstallModulePacket` → server
  handler → `BlockEntityModuleInstaller.TryInstall`. Slot networking is handled
  entirely by the verified `GuiDialogBlockEntityInventory` base class.
- The stale pre-fix `BlockModuleInstaller.cs` calling `OnInteract` (CS1061) is
  superseded; the block now calls `OnPlayerRightClick`. (If your working folder
  still errors here, an old copy of the file is lingering — replace the whole
  `src` folder from this zip.)
- Cleared warnings: `ModuleCode` initialized (CS8618); `VEIntegrationWired`
  back to `static readonly` (CS0162, reverted again in an intermediate copy);
  `ModuleRegistry.Get` return type nullable (CS8603).

### Changed
- Module install now triggers when the installer GUI is closed, rather than via
  a custom button — the inventory base class does not compose a button, and
  this avoids unverified GUI-composition calls. The block hint text reflects
  this ("close to install").

## [0.1.9] — Installer block compile fix

### Fixed
- `BlockEntityModuleInstaller` now implements the abstract
  `BlockEntityOpenableContainer.OnPlayerRightClick(IPlayer, BlockSelection)`
  (compile error CS0534). Replaced the earlier custom `OnInteract` method with
  this required override, which opens the installer GUI client-side and clears
  the cached dialog on close. The block forwards its interaction to it.

## [0.1.8] — Items, recipes, models, and the Module Installer block

### Added
- **Module items**: a `powermodule` item with five variants (flight,
  sprintassist, jumpassist, falldamage, nightvision), each carrying its
  `moduleCode` in attributes.
- **Grid recipes** (end-game tier, gated behind steel plates + temporal gears):
  one per armor piece, one per module, and one for the installer block. Module
  recipes use a plate/gear ring with a thematic core (flight uses a large
  temporal gear).
- **Temporary models/textures**: procedurally generated placeholder item icons
  for all four armor pieces and the module, plus three block textures for the
  installer. Worn models point at vanilla plate-armor shapes
  (`game:entity/humanoid/seraph/armor/plate/*`) via `attachShape`.
- **Module Installer block**: a GUI block (`BlockModuleInstaller` +
  `BlockEntityModuleInstaller`) with two slots (armor + module) and an Install
  button. Installing reads the module's code and sets the flag on the armor via
  `EnergyStore.SetModule`, consuming the module item. Server-authoritative.
- Block + blockentity classes registered in `Start`; blocktype JSON, recipe,
  and all lang entries added.

### Needs in-game verification
- The worn-armor `attachShape` wiring and the `GuiDialogBlockEntity`
  constructor/slot-grid calls are written against the documented API but should
  be confirmed against your assemblies — armor-attach element names and GUI
  signatures occasionally differ by version.
- Recipes use verified vanilla ingredient codes; swap to true VE machine
  outputs once you confirm their item codes from the VE assembly.

## [0.1.7] — Build fix: stale pre-rename files + game dependency

### Fixed
- Build was failing with `PowerArmor` namespace and missing-packet errors
  because the old pre-rename source files (`src/PowerArmorModSystem.cs`,
  `src/Items/ItemPowerArmor.cs`) were still present in the working folder
  alongside the renamed ones, so both got compiled. Added a `cleanup-old-files.ps1`
  helper to delete them, and a `<Compile Remove>` safety net in the `.csproj`
  so those filenames are never compiled even if they reappear.

### Changed
- `modinfo.json` `game` dependency set to `1.22.3` (the current stable, which
  requires .NET 10). The 1.22.3 patch fixed the false "incompatible" warning
  for mods declaring a game dependency, so pinning it is safe again.
- Confirmed `TargetFramework` is `net10.0`, required by Vintage Story 1.22+.

## [0.1.6] — Require Vintage Engineering

### Changed
- Vintage Engineering is now a **hard dependency**. `modinfo.json` declares
  `vintageengineering` (any version), so the game will not load the mod unless
  VE is present.
- The `.csproj` now references `vintageengineering.dll` as a build dependency
  via a `VintageEngineeringDll` property (default
  `VintagestoryData\Mods\vintageengineering.dll`, overridable with
  `-p:VintageEngineeringDll=...`).
- Added a pre-build check that fails with a clear message if either
  `VintagestoryAPI.dll` or `vintageengineering.dll` is missing.

### Note
- The VE reference is currently inert at runtime — the `VEPowerAdapter` is
  still a stub, so no VE types are used yet. The dependency is enforced at load
  and build time; actual power transfer comes when the adapter is wired.

## [0.1.5] — Project-wide rename to VEPowersuit

### Changed
- Renamed all identifiers from the old `PowerArmor` / `powerarmor` naming to
  `VEPowersuit` / `vepowersuit` for consistency:
  - Asset domain `assets/vepowersuit/`, lang prefix `vepowersuit:`
  - Item codes `vepowersuit-chest` etc., with matching lang keys
  - C# namespaces `VEPowersuit.*`, classes `VEPowersuitModSystem` and
    `ItemVEPowersuit`, source files renamed to match
  - Item-class registration string and JSON `"class"` both `VEPowersuit`
  - Network channel, hotkey, stat, and GUI codes to `vepowersuit_*`
- Assembly name `VEPowersuit`, modid `vepowersuit`.

> Maintenance note: the changelog and mod version have repeatedly reverted
> between uploaded copies. Keep one canonical project copy to avoid losing
> history — entries 0.1.1–0.1.4 (wearable base class, OnCreatedByCrafting
> signature, EntityPlayer namespace, build-reference fixes) were applied in the
> code but their changelog lines were lost in an intermediate version.

## [0.1.0] — Initial scaffold

### Added
- Four armor pieces: Chestplate (core), Leggings, Helmet, Boots — each with
  its own energy capacity and a default module.
- Per-itemstack energy storage that persists across saves (`EnergyStore`).
- Module system with a central registry (`ModuleRegistry`): Flight, Sprint
  Assist, Jump Assist, Fall Damage Negation, Night Vision.
- Server-side 1 Hz tick: energy drain, capability gating, sprint-assist speed
  bonus, energy sync to client.
- Flight toggle on **R** (server-authoritative).
- Module config GUI on **U**.
- Client/server networking for flight, module toggles, and energy sync.
- Energy HUD stub.
- Vintage Engineering adapter stub (`VEPowerAdapter`) — charging not yet wired.

### Known limitations
- VE charging not implemented; armor can't recharge in survival yet.
- Models and textures are placeholders.
- No crafting recipes for installing additional modules.
- Flight relies on the engine free-move flag; may need adjustment on
  restricted servers.

---

> **Maintenance note:** add a new dated section at the top for each change.
> Group entries under Added / Changed / Fixed / Removed. Update the relevant
> wiki page in the same edit.
