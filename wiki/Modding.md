# Modding & Extending

[← Home](Home.md)

## Adding a new module

1. **Register it.** Add one entry to `ModuleRegistry.All` in
   `src/Modules/ModuleRegistry.cs`:

   ```csharp
   public const string Shield = "shield";

   [Shield] = new PowerModule(Shield, "vepowersuit:module-shield", perTick: 30),
   ```

2. **Add a lang key** in `assets/vepowersuit/lang/en.json`:

   ```json
   "vepowersuit:module-shield": "Energy Shield"
   ```

3. **Add behavior** (only if it does something custom). Per-tick drain and the
   config-GUI toggle work automatically. For special effects, handle the
   module's code in the server tick loop in `VEPowersuitModSystem.cs`.

That's it — the GUI lists it and the tick loop drains it without further work.

## Granting a module by default on a piece

Add its code to the `defaultModules` array in that piece's itemtype JSON, e.g.
`assets/vepowersuit/itemtypes/chest.json`:

```json
"defaultModules": ["sprintassist", "shield"]
```

## Adding crafting / install recipes

Drop recipe JSON under `assets/vepowersuit/recipes/`. To install a module via
crafting, set the module flag in `OnCreatedByCrafting` (in
`ItemVEPowersuit.cs`) based on the inputs, or build a custom interaction.

## Fleshing out the HUD energy bar

`src/Gui/HudEnergyBar.cs` is a stub. Compose a `StatBar` element and update its
value from `mod.LastEnergy / mod.LastMaxEnergy` in `OnRenderGUI`. Register the
HUD in `StartClientSide` if you want it always visible.

## Changing flight to jetpack thrust

The default uses the engine free-move flag. For held-key upward thrust
instead, replace the body of `StartFlight` with per-tick upward velocity
applied while the key is held, and remove the `FreeMove` calls. See
[Architecture](Architecture.md) for where this lives.

## Keeping the wiki current

When you change behavior, update the matching page **and** add a line to the
[Changelog](Changelog.md). The pages most likely to drift:

- New module → [Modules](Modules.md) + [Modding](Modding.md)
- New piece or stat change → [Features](Features.md)
- VE work → [VE Integration](VE-Integration.md)
- Code restructure → [Architecture](Architecture.md)
