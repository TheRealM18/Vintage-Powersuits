using System;
using HarmonyLib;
using VEPowersuit.Items;
using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// Vintage Engineering's IChargeableItem getters (CurrentPower, MaxPower,
    /// RatedPower, ReceivePower) take no ItemStack, but one Item instance is
    /// shared by every stack. VE's charger (BELVCharger.OnSimTick) services a
    /// single slot synchronously per tick, so we wrap that whole method and
    /// bind the slot's stack as ItemVEPowersuit's active charging stack for its
    /// duration. This is what makes the interface route per-stack-correct on an
    /// UNMODIFIED Vintage Engineering install.
    ///
    /// If VE ever renames/relocates this method, the patch silently no-ops
    /// (TargetMethod returns null) and the suit falls back to VE's durability
    /// charging route enabled by "chargable": true in the itemtype JSON.
    /// </summary>
    [HarmonyPatch]
    public static class VEChargerPatch
    {
        public static bool Prepare() => true;

        public static System.Reflection.MethodBase TargetMethod()
        {
            // Resolve BELVCharger.OnSimTick(float) reflectively so we don't hard
            // link against a type that may shift between VE versions.
            var t = AccessTools.TypeByName("VintageEngineering.BELVCharger");
            if (t == null) return null;
            return AccessTools.Method(t, "OnSimTick", new[] { typeof(float) });
        }

        // Pull the slot-0 stack off the charger BE and bind it before the tick.
        public static void Prefix(object __instance, out VEChargeContext __state)
        {
            __state = default;
            var stack = TryGetInputStack(__instance);
            if (stack?.Collectible is ItemVEPowersuit)
                __state = ItemVEPowersuit.BindChargingStack(stack);
        }

        public static void Finalizer(VEChargeContext __state)
        {
            __state.Dispose();
        }

        private static ItemStack TryGetInputStack(object charger)
        {
            try
            {
                // BELVCharger exposes: public ItemSlot InputSlot => inventory[0];
                var prop = charger.GetType().GetProperty("InputSlot");
                var slot = prop?.GetValue(charger) as ItemSlot;
                return slot?.Itemstack;
            }
            catch
            {
                return null;
            }
        }
    }
}
