using System.Reflection;
using HarmonyLib;
using VEPowersuit.Items;
using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// Harmony prefix on Vintage Engineering's <c>BELVCharger.OnSimTick(float dt)</c>.
    ///
    /// WHAT THIS PATCH DOES (and nothing more):
    ///   • Check whether the item in the charger's input slot is one of our
    ///     power suits.
    ///   • If yes, hand off to <see cref="SuitChargeSession.TryCharge"/(which
    ///     uses only our own classes) and return <c>false</cto skip VE's
    ///     original method.
    ///   • For every other item, return <c>true</cso VE runs completely
    ///     unchanged.
    ///
    /// ALL VE-reflection logic lives in <see cref="VEChargerAccessor"/>.
    /// ALL charge-transfer logic lives in <see cref="SuitChargeSession"/>.
    /// This file must not contain either.
    ///
    /// Fail-safe: if the accessor cannot be constructed (VE renamed something),
    /// we return <c>true</cand let VE try — the charger keeps running.
    /// </summary>
    [HarmonyPatch]
    public static class VEChargerPatch
    {
        public static bool Prepare() => true;

        public static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("VintageEngineering.BELVCharger");
            if (t == null) return null;
            return AccessTools.Method(t, "OnSimTick", new[] { typeof(float) });
        }

        /// <summary>
        /// Prefix. Returns <c>false</cto suppress VE's method when we fully
        /// handled charging our suit; <c>true</cto let VE run for everything
        /// else (or if we hit an unexpected error).
        /// </summary>
        public static bool Prefix(object __instance, float dt)
        {
            try
            {
                // ── 1. Is this our item? ──────────────────────────────────
                var accessor = VEChargerAccessor.TryCreate(__instance);
                if (accessor == null) return true; // VE internals unavailable

                ItemStack? stack = accessor.GetInputStack();
                if (stack?.Collectible is not ItemVEPowersuit suit)
                    return true; // not our item — VE handles it normally

                // ── 2. Delegate all charge logic to our own classes ───────
                return !SuitChargeSession.TryCharge(accessor, stack, suit, dt);
            }
            catch
            {
                // Unexpected error — never break the charger; let VE run.
                return true;
            }
        }
    }
}