using System.Collections.Generic;

namespace VEPowersuit.Modules
{
    /// <summary>A passive or active upgrade installed into a piece of power armor.</summary>
    public class PowerModule
    {
        public string Code;
        public string DisplayLangKey;   // resolved via Lang for tooltips/GUI
        public int EnergyPerTick;       // passive drain while active (per 1s tick)
        public int EnergyPerActivation; // one-shot cost (e.g. jump boost)
        public bool DefaultActive;

        public PowerModule(string code, string langKey, int perTick = 0,
                           int perActivation = 0, bool defaultActive = true)
        {
            Code = code;
            DisplayLangKey = langKey;
            EnergyPerTick = perTick;
            EnergyPerActivation = perActivation;
            DefaultActive = defaultActive;
        }
    }

    /// <summary>
    /// Central catalogue. Add new modules here — the GUI and tick loop read
    /// from this so you don't have to touch them when adding upgrades.
    /// Machine-Muse style: each module is a toggleable capability gated by energy.
    /// </summary>
    public static class ModuleRegistry
    {
        public const string Flight = "flight";
        public const string SprintAssist = "sprintassist";
        public const string JumpAssist = "jumpassist";
        public const string FallDamage = "falldamage";
        public const string NightVision = "nightvision";

        // ---- New modules (added; nothing above is removed) ----
        public const string StepAssist = "stepassist";
        public const string WaterBreathing = "waterbreathing";
        public const string Protection = "protection";
        public const string Heating = "heating";
        public const string Cooling = "cooling";
        public const string SwimAssist = "swimassist";
        public const string Battery = "battery";
        public const string BeeProtection = "beeprotection";
        public const string Feeding = "feeding";

        public static readonly Dictionary<string, PowerModule> All = new()
        {
            [Flight]       = new PowerModule(Flight, "vepowersuit:module-flight", perTick: 50),
            [SprintAssist] = new PowerModule(SprintAssist, "vepowersuit:module-sprintassist", perTick: 10),
            [JumpAssist]   = new PowerModule(JumpAssist, "vepowersuit:module-jumpassist", perActivation: 25),
            [FallDamage]   = new PowerModule(FallDamage, "vepowersuit:module-falldamage", perActivation: 40),
            [NightVision]  = new PowerModule(NightVision, "vepowersuit:module-nightvision", perTick: 5),

            // Movement (leggings).
            [StepAssist]   = new PowerModule(StepAssist, "vepowersuit:module-stepassist", perTick: 2),
            [SwimAssist]   = new PowerModule(SwimAssist, "vepowersuit:module-swimassist", perTick: 8),

            // Life support (helmet).
            [WaterBreathing] = new PowerModule(WaterBreathing, "vepowersuit:module-waterbreathing", perTick: 6),
            [Feeding]        = new PowerModule(Feeding, "vepowersuit:module-feeding", perTick: 0, perActivation: 60),
            [BeeProtection]  = new PowerModule(BeeProtection, "vepowersuit:module-beeprotection", perTick: 1),

            // Core systems (chest).
            [Protection]   = new PowerModule(Protection, "vepowersuit:module-protection", perTick: 4),
            [Heating]      = new PowerModule(Heating, "vepowersuit:module-heating", perTick: 8),
            [Cooling]      = new PowerModule(Cooling, "vepowersuit:module-cooling", perTick: 8),
            // Battery is a passive capacity upgrade; it draws nothing on its own.
            [Battery]      = new PowerModule(Battery, "vepowersuit:module-battery", perTick: 0),
        };

        public static PowerModule? Get(string code)
            => All.TryGetValue(code, out var m) ? m : null;

        // ---- Slot gating ----------------------------------------------------
        // Which armor slot each module is allowed to be installed into. The
        // installer rejects a module/armor combination that isn't listed here.
        // Slot codes match the JSON clothesCategory: armorhead / armorbody / armorlegs.
        public const string SlotHead = "armorhead";
        public const string SlotBody = "armorbody";
        public const string SlotLegs = "armorlegs";

        public static readonly Dictionary<string, string> SlotByModule = new()
        {
            // Helmet
            [NightVision]    = SlotHead,
            [WaterBreathing] = SlotHead,
            [BeeProtection]  = SlotHead,
            [Feeding]        = SlotHead,

            // Chest
            [Flight]      = SlotBody,
            [Protection]  = SlotBody,
            [Heating]     = SlotBody,
            [Cooling]     = SlotBody,
            [Battery]     = SlotBody,
            [FallDamage]  = SlotBody,

            // Leggings
            [SprintAssist] = SlotLegs,
            [JumpAssist]   = SlotLegs,
            [StepAssist]   = SlotLegs,
            [SwimAssist]   = SlotLegs,
        };

        /// <summary>The armor slot a module may be installed into, or null if unknown.</summary>
        public static string? SlotFor(string code)
            => SlotByModule.TryGetValue(code, out var s) ? s : null;

        /// <summary>True if the module is allowed in the given armor clothesCategory slot.</summary>
        public static bool FitsSlot(string code, string? clothesCategory)
            => clothesCategory != null && SlotFor(code) == clothesCategory;

        // ---- Per-module tuning knobs (read by the entity behavior / renderer) ----

        /// <summary>Upward velocity added on an assisted jump (blocks/tick units).</summary>
        public const double JumpBoostVelocity = 0.26;

        /// <summary>
        /// Sprint-assist walk-speed bonus (fraction). +40% while sprinting.
        /// </summary>
        public const float SprintWalkSpeedBonus = 0.40f;

        /// <summary>
        /// Fall-damage module: energy spent per point of fall damage negated.
        /// If the suit can't pay the full cost, it absorbs as much as it can.
        /// </summary>
        public const int FallEnergyPerDamage = 8;

        /// <summary>
        /// Night vision: how strongly to drive the screen brightness (0..1.5).
        /// </summary>
        public const float NightVisionStrength = 0.85f;

        /// <summary>Seconds for night vision to fully fade in / out.</summary>
        public const float NightVisionRampSeconds = 0.6f;

        // ---- New module tuning knobs ----

        /// <summary>Step-assist: how high (blocks) the player can step up while active.</summary>
        public const float StepAssistHeight = 1.2f;

        /// <summary>Swim-assist walk/swim-speed bonus (fraction) while in water.</summary>
        public const float SwimWalkSpeedBonus = 0.50f;

        /// <summary>
        /// Protection module: flat fraction of incoming damage absorbed while
        /// active and powered (0..1). Spends energy per point absorbed.
        /// </summary>
        public const float ProtectionDamageReduction = 0.30f;

        /// <summary>Energy spent per point of damage the protection module absorbs.</summary>
        public const int ProtectionEnergyPerDamage = 20;

        /// <summary>Target body temperature (°C) the heating module holds you at or above.</summary>
        public const float HeatingTargetTemp = 31f;

        /// <summary>Target body temperature (°C) the cooling module holds you at or below.</summary>
        public const float CoolingTargetTemp = 37f;

        /// <summary>
        /// Battery module: fractional bonus to the suit's max power capacity while
        /// installed (applied as a stack flag, read where max power is reported).
        /// </summary>
        public const float BatteryCapacityBonus = 0.50f;

        /// <summary>Feeding module: satiety restored per activation.</summary>
        public const float FeedingSaturationPerActivation = 200f;

        /// <summary>Feeding triggers when current saturation drops below this fraction of max.</summary>
        public const float FeedingHungerThreshold = 0.25f;
    }
}
