using System;
using System.Reflection;
using HarmonyLib;
using VEPowersuit.Items;
using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// Harmony patch over Vintage Engineering's charger tick
    /// (BELVCharger.OnSimTick) that makes our power suit charge into its own EU
    /// energy store via the IChargeableItem interface.
    ///
    /// WHY A FULL TAKEOVER (and not just a wrapper):
    /// The suit's itemtype JSON sets BOTH "chargable": true (a flag the user
    /// wants visible to VE / other mods) AND implements IChargeableItem. VE's
    /// charger checks "chargable" FIRST and, if true, charges the item's vanilla
    /// DURABILITY bar instead of calling the interface — which would leave our
    /// real EU store empty. So for our suit specifically we bypass VE's method
    /// entirely: we read the charger's available power, feed it through the
    /// suit's IChargeableItem.ReceivePower (EU store), debit the charger, and
    /// return false to skip VE's original OnSimTick. For every other item the
    /// prefix returns true and VE runs completely unchanged.
    ///
    /// If VE ever renames/relocates the method or its members, the patch fails
    /// safe: TargetMethod returns null (no patch), or the reflective member
    /// lookups return null and we fall through to VE's own logic.
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
        /// Prefix. Returns false to SKIP VE's original method (we fully handled
        /// charging our suit), true to let VE run normally for anything else.
        /// </summary>
        public static bool Prefix(object __instance, float dt)
        {
            try
            {
                var stack = TryGetInputStack(__instance);
                if (stack?.Collectible is not ItemVEPowersuit suit)
                    return true; // not our item — VE handles it normally.

                // Resolve VE's electric behavior + its members reflectively.
                object electric = GetProp(__instance, "Electric");
                if (electric == null) return true; // can't drive it; let VE try.

                ulong charged = TryChargeSuit(__instance, electric, stack, suit, dt);

                // We handled it (even if 0 was moved, e.g. suit full); skip VE.
                return false;
            }
            catch
            {
                // Anything unexpected: don't break the charger, let VE run.
                return true;
            }
        }

        private static ulong TryChargeSuit(object charger, object electric,
            ItemStack stack, ItemVEPowersuit suit, float dt)
        {
            // How much can the charger deliver this tick, and does it have it?
            ulong rated = InvokeRatedPower(electric, dt);
            ulong stored = GetULong(electric, "CurrentPower");
            if (rated == 0 || stored < rated)
            {
                SetState(charger, "Paused");
                return 0;
            }

            // Bind the stack so the suit's parameterless IChargeableItem getters
            // resolve to THIS piece, then push power into its EU store.
            using (ItemVEPowersuit.BindChargingStack(stack))
            {
                ulong cur = suit.CurrentPower;
                ulong max = suit.MaxPower;
                if (cur >= max)
                {
                    SetState(charger, "Paused");
                    return 0;
                }

                SetState(charger, "On");

                ulong wantByItem = suit.RatedPower(dt, isInsert: true);
                ulong toUse = Math.Min(rated, wantByItem == 0 ? rated : wantByItem);

                ulong remaining = suit.ReceivePower(toUse, dt, simulate: false);
                ulong used = toUse - remaining;

                if (used > 0) DebitCharger(electric, used);
                return used;
            }
        }

        // ---------------- reflection helpers ----------------

        private static ItemStack TryGetInputStack(object charger)
        {
            var slot = GetProp(charger, "InputSlot") as ItemSlot;
            return slot?.Itemstack;
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null) return null;
            var p = obj.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance);
            return p?.GetValue(obj);
        }

        private static ulong GetULong(object obj, string propName)
        {
            var v = GetProp(obj, propName);
            return v is ulong u ? u : 0UL;
        }

        private static ulong InvokeRatedPower(object electric, float dt)
        {
            var m = electric.GetType().GetMethod("RatedPower",
                BindingFlags.Public | BindingFlags.Instance);
            if (m == null) return 0;
            // RatedPower(float dt, bool isInsert = false)
            var result = m.Invoke(electric, new object[] { dt, false });
            return result is ulong u ? u : 0UL;
        }

        private static void DebitCharger(object electric, ulong used)
        {
            // public ulong electricpower; subtract what we consumed.
            var f = electric.GetType().GetField("electricpower",
                BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return;
            if (f.GetValue(electric) is ulong cur)
            {
                ulong next = used > cur ? 0UL : cur - used;
                f.SetValue(electric, next);
            }
        }

        private static void SetState(object charger, string enumMemberName)
        {
            // BELVCharger.SetState(EnumBEState). Resolve the enum value by name
            // so we don't hard-reference VE's enum type.
            var method = charger.GetType().GetMethod("SetState",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null) return;

            var p = method.GetParameters();
            if (p.Length < 1 || !p[0].ParameterType.IsEnum) return;

            object enumVal;
            try { enumVal = Enum.Parse(p[0].ParameterType, enumMemberName); }
            catch { return; }

            // Some overloads take extra optional args; fill with defaults.
            var args = new object[p.Length];
            args[0] = enumVal;
            for (int i = 1; i < p.Length; i++)
                args[i] = p[i].HasDefaultValue ? p[i].DefaultValue : GetDefault(p[i].ParameterType);

            method.Invoke(charger, args);
        }

        private static object GetDefault(Type t)
            => t.IsValueType ? Activator.CreateInstance(t) : null;
    }
}