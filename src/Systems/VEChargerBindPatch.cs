using HarmonyLib;
using System.Reflection;
using VEPowersuit.Items;
using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// THE ONLY Harmony patch in this mod, and it is deliberately tiny.
    ///
    /// Our suit pieces use <c>"chargable": false</c>, so Vintage Engineering's
    /// charger takes its clean, tested INTERFACE branch (BELVCharger.OnSimTick,
    /// the <c>else</c> block): it reads our <c>IChargeableItem</c> members
    /// (CurrentPower / MaxPower / RatedPower) and calls <c>ReceivePower(...)</c>
    /// to push EU into the suit. We do NOT replace, reimplement, or reflect into
    /// any of VE's power logic — VE does all the work correctly.
    ///
    /// The ONLY gap VE leaves: <c>chargeableItem</c> is the shared Item instance
    /// (InputSlot.Itemstack.Collectible), and VE never tells it WHICH stack it's
    /// charging. Our IChargeableItem getters are parameterless, so they need to
    /// know the stack. VE reads them all synchronously inside OnSimTick, so we
    /// simply bind the charger's input stack to a thread-local for the duration
    /// of VE's original method:
    ///
    ///   Prefix  → if InputSlot holds one of our suit pieces, bind that stack.
    ///   Postfix → unbind.
    ///
    /// The prefix ALWAYS returns void (VE's original always runs). This is the
    /// whole patch. No reflection into VE power fields, no state machine, no
    /// branch replacement — the previous versions' fragility is gone.
    /// </summary>
    [HarmonyPatch]
    public static class VEChargerBindPatch
    {
        public static bool Prepare() => true;

        public static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("VintageEngineering.BELVCharger");
            if (t == null) return null;
            return AccessTools.Method(t, "OnSimTick", new[] { typeof(float) });
        }

        public static void Prefix(object __instance, out VEChargeContext? __state)
        {
            __state = null;
            try
            {
                var slot = AccessTools.Property(__instance.GetType(), "InputSlot")
                    ?.GetValue(__instance) as ItemSlot;
                ItemStack stack = slot?.Itemstack;

                if (stack?.Collectible is ItemVEPowersuit)
                    __state = ItemVEPowersuit.BindChargingStack(stack);
            }
            catch
            {
                __state = null; // never break the charger
            }
        }

        public static void Postfix(VEChargeContext? __state)
        {
            __state?.Dispose();
        }
    }
}
