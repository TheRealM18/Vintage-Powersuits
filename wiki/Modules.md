# Modules

[← Home](Home.md)

Modules are toggleable upgrades, Machine-Muse style. Each is gated by energy:
some drain continuously while active, others cost a one-shot amount per use.
All module state is stored on the armor itemstack.

## Built-in modules

| Module | Code | Cost | Notes |
|--------|------|------|-------|
| Flight | `flight` | 50 EU/tick | Only drains while actually flying |
| Sprint Assist | `sprintassist` | 10 EU/tick | Only drains while sprinting; +40% walk speed |
| Jump Assist | `jumpassist` | 25 EU/activation | One-shot per jump |
| Fall Damage Negation | `falldamage` | 40 EU/activation | One-shot when triggered |
| Night Vision | `nightvision` | 5 EU/tick | Passive while active |

> A "tick" is one second (the server runs a 1 Hz maintenance loop).

## How costs apply

- **Per-tick** modules are charged once per second while their condition holds
  (flying, sprinting, or simply active).
- **Per-activation** modules charge a lump sum at the moment they fire.
- If a per-tick module can't pay, its capability shuts off (flight, for
  example, drops you out of the air).

## Where they live in code

Every module is one entry in `ModuleRegistry.All` inside
`src/Modules/ModuleRegistry.cs`. The config GUI and the server tick loop both
read from that registry, so adding a module in one place propagates
everywhere. See [Modding](Modding.md) for the step-by-step.

## Installing modules onto armor

Default modules are granted on craft (defined per-piece in the itemtype JSON
under `defaultModules`). A crafting/smithing path for adding more modules is
**not yet defined** — recipes under `assets/vepowersuit/recipes/` plus handling
in `OnCreatedByCrafting` are the intended approach.
