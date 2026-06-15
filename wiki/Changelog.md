# Changelog

## 0.8.0 — Charging redesign: minimal bind-only patch + diagnostics

### Changed
- **`chargable` back to `false`.** VE's charger now uses its clean, tested
  INTERFACE branch for our suit (reads `IChargeableItem` and calls
  `ReceivePower`). The `chargable: true` durability-redirect approach is gone —
  it relied on VE's durability branch, which never calls the interface, so EU
  never landed.
- **Replaced the reflection patch with `VEChargerBindPatch`** — a tiny
  prefix/postfix that only binds the charger's input stack to the thread-local
  for the duration of VE's original `OnSimTick`, then unbinds. No reflection
  into VE power fields, no branch replacement, no state-machine handling. VE
  does all the charging; we only tell the shared Item instance which stack it's
  charging. Removed `VEChargerDurabilityRedirectPatch`.

### Added
- **Charge diagnostics.** `ItemVEPowersuit.ReceivePower` now logs (VerboseDebug)
  each time EU lands — `charged N EU (before -> after / max)` — and warns if VE
  calls it with no bound stack. Watch `client-debug.log` / `server-debug.log`
  with a suit in a charger to confirm power is registering.

### Why this should finally work
- The suit's stored EU (`EnergyStore`) was always correct; the HUD, flight, and
  module drain read it directly. The only broken link was the charger never
  writing to it, because the binding/patch chain was fragile. This version uses
  VE's own charging code end-to-end and only adds the one thing VE structurally
  cannot do (stack identity), so there is almost nothing left to misfire.
- Power math sanity (VE ticks ~100ms, maxPPS 2000): ~200 EU/tick into a 200000
  buffer. Non-zero and correct once the stack is bound.

## 0.7.0 — Module toggles fixed; creative pre-charged suit

### Fixed
- **GUI buttons now reflect and keep their state.** The dialog never set button
  pressed-state and the toggle handler ignored the on/off value, so buttons
  never looked active. Buttons are now driven by authoritative server state
  (`ModuleStatePacket`), set `Toggleable = true`, and stay lit while the module
  is enabled. Non-installed modules show disabled.
- **Installed vs. enabled split.** The old toggle flipped *installed* state
  (effectively uninstalling). Added `EnergyStore.IsEnabled` / `SetEnabled`
  separate from `HasModule` (installed). The GUI now toggles ENABLED on an
  already-installed module; the tick loop, sprint assist, and flight all gate on
  `IsEnabled`.
- **Flight now responds to the toggle.** Turning the flight module off in the
  GUI (or running out of power) drops the player out of flight. The flight
  hotkey only engages when flight is installed AND enabled.

### Added
- **Creative-only, pre-charged suit variant.** Each armor piece now has a
  `type` variant group (`normal`, `creative`). Only the `creative` variant
  appears in creative tabs; it spawns fully charged and (for testing) with all
  modules pre-installed. The `normal` variant is the craftable one and starts
  empty. Driven by the `fullChargeOnGet` and `defaultModules` byType attributes
  and a new idempotent `ItemVEPowersuit.EnsureInitialized`.
- New packets `ModuleStatePacket` (server→client) and `RequestModuleStatePacket`
  (client→server) so the GUI can fetch live state on open.

### Notes
- Crafting recipes now output the `-normal` variant; variant-specific lang keys
  added. `SuitHelper`/installer key off the `isCore` attribute, not item codes,
  so they're unaffected by the new variant suffix.
- Verified GUI API against VS 1.22.2 docs (`GuiElementToggleButton.SetValue`,
  `.Toggleable`, `.Enabled`; `ITreeAttribute.HasAttribute/RemoveAttribute`).

## 0.6.0 — Single patch: redirect chargeable durability into EU

### Changed
- **`chargable` is now `true`** in all three armor itemtypes. This sends VE's
  charger down its durability charge branch for our pieces.
- **One Harmony patch only.** Removed `VEChargerPatch` and
  `VEChargerLoadFixPatch`. New `VEChargerDurabilityRedirectPatch` intercepts
  `BELVCharger.OnSimTick` and, for our power-only pieces only, redirects the
  power the durability branch would have spent into the suit's EU energy store
  (via `IChargeableItem`), debiting the charger by exactly what the suit
  accepts. It mirrors VE's own guards (not-enough-juice pause, On/Paused state
  transitions). Every other item falls through to VE unchanged.
- Durability stays pinned by the power-only behavior's `OnDamageItem`, so the
  redirected power becomes pure EU, never durability.

### Notes
- Verified against Vintage Engineering source (FlexibleGames/VintageEngineering,
  `release` branch): `BELVCharger.OnSimTick`, `InvCharger.CanContain`.
- The earlier empty-charger load crash is a separate VE-side bug; it is not
  re-introduced by this change, but this version no longer ships a workaround
  for it. Say the word if you want that guard added back as part of this patch.

## 0.5.1 — Charging actually works; charger load-crash workaround

### Fixed
- **Suit now charges.** `ItemVEPowersuit.RatedPower(dt, false)` returned 0,
  but VE's charger calls exactly that (`RatedPower(dt, false)`) to decide how
  much to push into the item — so it computed "push 0" and charged nothing.
  `RatedPower` now always reports the receive rating. This was the core reason
  pieces took no durability (behavior working) but also gained no power.
- **Empty VE chargers no longer get discarded on world load.** VE's
  `BELVCharger.FromTreeAttributes` does `InputSlot.Itemstack.Clone()` with no
  null check; an empty saved charger throws NullReferenceException and the
  block entity is discarded ("Failed loading a blockentity in a chunk"). New
  `VEChargerLoadFixPatch` guards the empty-slot case and loads the charger
  safely; non-empty chargers run VE's original unchanged. (VE-side bug; this is
  a defensive workaround.)

### Changed
- **`VEChargerPatch` rewritten as a thin bind-only prefix/postfix.** Instead of
  reflectively replacing VE's whole charge tick (brittle: any renamed VE member
  silently disabled charging), it now just binds the per-stack context around
  VE's own (correct) `OnSimTick` and lets VE charge through `IChargeableItem`.
- **Removed** `SuitChargeSession.cs` and `SuitChargeAssessor.cs` — the old
  reflective charge/accessor path, now unused and superseded by VE's own logic.

### Notes
- Verified against Vintage Engineering source (FlexibleGames/VintageEngineering,
  `release` branch): `BELVCharger`, `ElectricBEBehavior`, `IChargeableItem`.

## 0.5.0 — Power-only charging behavior

- New `CollectibleBehaviorPowerCharged` (`vepowersuitpowercharged`): an explicit
  per-piece switch that declares a suit piece **power-only**. When attached:
  - The piece charges through the EU energy store via the existing Harmony
    patch — never through VE's durability-topup route. `chargable` stays
    `false` in the itemtype JSON.
  - `OnDamageItem` cancels all vanilla durability loss, so the piece never
    wears down or breaks; only stored EU depletes.
  - The held-item tooltip notes the piece is power-only.
- `VEChargerPatch` now only takes over the charger tick for pieces whose
  `CollectibleBehaviorPowerCharged.WantsPatchCharging` is true; a suit piece
  without the behavior falls through to VE so its JSON `chargable` flag decides.
- `ItemVEPowersuit` gained `PowerCharged`, `IsPowerOnly`, and
  `WantsPatchCharging` accessors and a power-only charge-hint tooltip.
- Behavior registered in `Start` via `RegisterCollectibleBehaviorClass`; the
  three armor itemtypes carry the behavior (with default properties); two new
  lang keys added.
- Config knobs (in the behavior's JSON `properties`): `powerOnly`,
  `patchCharging`, `noDurability` (all default true).

## 0.4.0 — All modules now functional

- Jump Assist: rising-edge jump detection adds upward velocity and spends
  EnergyPerActivation per assisted jump (server-authoritative).
- Fall Damage Negation: intercepts gravity/fall damage and spends energy
  (FallEnergyPerDamage per point) to cancel as much as the suit can afford;
  partial absorption when low on energy.
- Night Vision: client-side driver ramps the game's built-in NightVisionStrength
  shader uniform up/down while worn + powered; per-tick energy drain unchanged.
- Flight and Sprint Assist: unchanged behavior, now share the SuitHelper lookup.
- New EntityBehaviorPowerSuit attached to players on join (jump + fall logic).
- New NightVisionRenderer registered in the Before render stage.
- New SuitHelper centralizes "worn core suit" resolution across all systems.
- ModuleRegistry gained tuning constants (jump boost, sprint bonus, fall cost,
  night-vision strength/ramp); EnergyStore gained MaxPPS already in 0.3.0.

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
