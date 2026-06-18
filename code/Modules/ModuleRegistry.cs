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

        public static readonly Dictionary<string, PowerModule> All = new()
        {
            [Flight]       = new PowerModule(Flight, "vepowersuit:module-flight", perTick: 50),
            [SprintAssist] = new PowerModule(SprintAssist, "vepowersuit:module-sprintassist", perTick: 10),
            [JumpAssist]   = new PowerModule(JumpAssist, "vepowersuit:module-jumpassist", perActivation: 25),
            [FallDamage]   = new PowerModule(FallDamage, "vepowersuit:module-falldamage", perActivation: 40),
            [NightVision]  = new PowerModule(NightVision, "vepowersuit:module-nightvision", perTick: 5),
        };

        public static PowerModule? Get(string code)
            => All.TryGetValue(code, out var m) ? m : null;

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
    }
}
