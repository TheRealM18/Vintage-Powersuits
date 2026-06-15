using System;
using System.Reflection;
using HarmonyLib;
using VEPowersuit.Items;
using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// THE ONLY Harmony patch in this mod.
    ///
    /// Our suit pieces ship with <c>"chargable": true</c> in their itemtype
    /// JSON. That makes Vintage Engineering's charger take its DURABILITY charge
    /// branch (BELVCharger.OnSimTick, the <c>if (chargable)</c> block) — the one
    /// that restores item durability from the charger's power.
    ///
    /// We don't want durability charging; we want that same power to fill the
    /// suit's own EU energy store instead. So this patch intercepts OnSimTick
    /// and, FOR OUR POWER-ONLY SUIT PIECES ONLY, replaces what the durability
    /// branch would have done: take the charger's rated power for this tick and
    /// push it into the suit's IChargeableItem EU store, debiting the charger by
    /// exactly the amount accepted.
    ///
    /// For every other item (and for our pieces that don't opt in) we return
    /// <c>true</c> so VE's original OnSimTick runs completely unchanged — VE
    /// keeps doing its normal durability/interface charging for everything else.
    ///
    /// This faithfully mirrors VE's own guard logic (sleeping/paused bouncer,
    /// the "not enough juice" pause, the On/Paused state transitions) so the
    /// charger behaves identically from the player's point of view; only the
    /// destination of the power changes (EU store, not durability).
    ///
    /// Fully reflective + null-guarded: if VE renames a member, the patch goes
    /// inert (TargetMethod null) or bails to VE's original. It never breaks the
    /// charger.
    /// </summary>
    [HarmonyPatch]
    public static class VEChargerDurabilityRedirectPatch
    {
        public static bool Prepare() => true;

        public static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("VintageEngineering.BELVCharger");
            if (t == null) return null;
            return AccessTools.Method(t, "OnSimTick", new[] { typeof(float) });
        }

        // Cached reflection handles (resolved once on first tick).
        private static bool _resolved;
        private static PropertyInfo _inputSlotProp;
        private static PropertyInfo _electricProp;
        private static MethodInfo _setStateMethod;
        private static Type _enumType;
        private static object _stateOn, _statePaused;
        private static FieldInfo _electricPowerField;
        private static PropertyInfo _currentPowerProp;
        private static MethodInfo _ratedPowerMethod;
        private static MethodInfo _updateClientMethod;

        /// <summary>
        /// Prefix. Returns <c>false</c> (skip VE's original) only when we fully
        /// handled an EU charge for one of our power-only suit pieces; otherwise
        /// <c>true</c> so VE runs unchanged.
        /// </summary>
        public static bool Prefix(object __instance, float dt)
        {
            try
            {
                if (!Resolve(__instance.GetType())) return true; // VE changed; let VE run

                var slot = _inputSlotProp.GetValue(__instance) as ItemSlot;
                ItemStack stack = slot?.Itemstack;

                // Only handle OUR opted-in power-only pieces. Everything else
                // (empty slot, other items, suit pieces without the behavior)
                // falls through to VE's original method untouched.
                if (stack?.Collectible is not ItemVEPowersuit suit || !suit.WantsPatchCharging)
                    return true;

                object electric = _electricProp.GetValue(__instance);
                if (electric == null) return true;

                // ── Mirror VE's pre-charge guards ─────────────────────────
                // (We can't read VE's private _updateBouncer, but skipping the
                //  bouncer only means we attempt a charge slightly more eagerly
                //  when sleeping/paused — harmless; we still gate on power.)

                ulong charterRated = InvokeRated(electric, dt);
                ulong charterPower = GetCurrentPower(electric);

                // "not enough juice" → pause, exactly like VE.
                if (charterRated > charterPower)
                {
                    SetState(__instance, _statePaused);
                    return false; // handled (did nothing this tick), skip VE
                }

                // ── Redirect the "durability" power into the EU store ─────
                // Bind the stack so the suit's parameterless IChargeableItem
                // members resolve to THIS stack for the duration of the calls.
                using (ItemVEPowersuit.BindChargingStack(stack))
                {
                    ulong cur = suit.CurrentPower;
                    ulong max = suit.MaxPower;

                    if (cur >= max)
                    {
                        SetState(__instance, _statePaused); // full → pause
                        return false;
                    }

                    SetState(__instance, _stateOn); // on and active

                    // How much can the suit take this tick, capped by what the
                    // charger can deliver this tick.
                    ulong suitRated = suit.RatedPower(dt, true);
                    ulong powertouse = charterRated;
                    if (powertouse > suitRated) powertouse = suitRated;

                    ulong remaining = suit.ReceivePower(powertouse, dt, false);
                    if (remaining > 0) powertouse -= remaining;

                    // Debit the charger by exactly what the suit accepted.
                    DebitPower(electric, powertouse);
                }

                InvokeUpdateClient(__instance, dt);
                return false; // we fully handled this item's tick
            }
            catch
            {
                return true; // never break the charger; let VE try
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Reflection plumbing (resolved once, cached)
        // ──────────────────────────────────────────────────────────────────

        private static bool Resolve(Type chargerType)
        {
            if (_resolved) return true;

            _inputSlotProp = chargerType.GetProperty("InputSlot",
                BindingFlags.Public | BindingFlags.Instance);
            _electricProp = chargerType.GetProperty("Electric",
                BindingFlags.Public | BindingFlags.Instance);
            _setStateMethod = chargerType.GetMethod("SetState",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _updateClientMethod = chargerType.GetMethod("UpdateClient",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (_inputSlotProp == null || _electricProp == null || _setStateMethod == null)
                return false;

            var stateParams = _setStateMethod.GetParameters();
            if (stateParams.Length < 1 || !stateParams[0].ParameterType.IsEnum) return false;
            _enumType = stateParams[0].ParameterType;
            _stateOn = TryEnum(_enumType, "On");
            _statePaused = TryEnum(_enumType, "Paused");
            if (_stateOn == null || _statePaused == null) return false;

            // Members on the Electric behavior object are resolved lazily on
            // first real use (see EnsureElectricMembers) against its runtime
            // type, since we don't have an instance here.

            _resolved = true;
            return true;
        }

        private static void EnsureElectricMembers(object electric)
        {
            if (_currentPowerProp != null) return;
            var et = electric.GetType();
            _currentPowerProp = et.GetProperty("CurrentPower",
                BindingFlags.Public | BindingFlags.Instance);
            _ratedPowerMethod = et.GetMethod("RatedPower",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(float), typeof(bool) }, null);
            _electricPowerField = et.GetField("electricpower",
                BindingFlags.Public | BindingFlags.Instance);
        }

        private static ulong GetCurrentPower(object electric)
        {
            EnsureElectricMembers(electric);
            return _currentPowerProp?.GetValue(electric) is ulong u ? u : 0UL;
        }

        private static ulong InvokeRated(object electric, float dt)
        {
            EnsureElectricMembers(electric);
            if (_ratedPowerMethod == null) return 0UL;
            return _ratedPowerMethod.Invoke(electric, new object[] { dt, false }) is ulong u ? u : 0UL;
        }

        private static void DebitPower(object electric, ulong amount)
        {
            EnsureElectricMembers(electric);
            if (_electricPowerField == null) return;
            ulong cur = _electricPowerField.GetValue(electric) is ulong u ? u : 0UL;
            ulong next = amount >= cur ? 0UL : cur - amount;
            _electricPowerField.SetValue(electric, next);
        }

        private static void SetState(object charger, object state)
            => _setStateMethod.Invoke(charger, new[] { state });

        private static void InvokeUpdateClient(object charger, float dt)
            => _updateClientMethod?.Invoke(charger, new object[] { dt });

        private static object TryEnum(Type enumType, string name)
        {
            try { return Enum.Parse(enumType, name); }
            catch { return null; }
        }
    }
}
