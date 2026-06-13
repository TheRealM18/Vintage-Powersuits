# Getting Started

[← Home](Home.md)

## Requirements

- Vintage Story 1.20 or newer (tested against 1.22.x)
- **Vintage Engineering — required.** The mod declares it as a dependency, so
  the game won't load without it, and the build won't compile without its dll
  (see [VE Integration](VE-Integration.md) for extracting the dll).
- .NET SDK matching the target framework (net10.0), only for building

## Building from source

1. Install the .NET SDK (net10.0).
2. Set the `VINTAGE_STORY` environment variable to your game install folder
   (the folder containing `VintagestoryAPI.dll`).
3. Make `vintageengineering.dll` available — extract it from VE's zip in
   `VintagestoryData\Mods` and place it at
   `VintagestoryData\Mods\vintageengineering.dll`, or pass
   `-p:VintageEngineeringDll=...` to the build.
4. From the project folder, run:

   ```
   dotnet build -c Release
   ```

   The compiled `VEPowersuit.dll` lands in the build output folder.

## Installing into the game

Package the built `VEPowersuit.dll` together with the `assets/` folder and
`modinfo.json` into a `.zip`, then drop it into your mods folder:

| OS | Mods folder |
|----|-------------|
| Windows | `%appdata%/VintagestoryData/Mods` |
| Linux | `~/.config/VintagestoryData/Mods` |
| macOS | `~/Library/Application Support/VintagestoryData/Mods` |

You can also drop the uncompiled project folder in directly — the game can
compile code mods at load — but building a `.dll` yourself lets you debug.

## First use in-game

1. Craft or spawn the armor pieces (creative inventory under General/Items).
2. Equip the chestplate — it is the **core** piece the flight and sprint
   systems look for.
3. Press **U** to open the module config panel.
4. Press **R** to toggle flight (requires the Flight module and stored energy).

> Until [VE charging](VE-Integration.md) is wired, pieces hold energy but have
> no way to recharge in survival. Use creative mode or finish the adapter.
