using System;
using System.Reflection;
using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// Reflective accessor for Vintage Engineering's <c>BELVCharger</c> block entity.
    ///
    /// ALL knowledge of VE's internals lives here. Every other class in this mod
    /// talks to this type instead of using reflection directly. If VE renames a
    /// member, only this file needs updating.
    ///
    /// Each method is intentionally narrow and returns a plain C# primitive (or
    /// null) so callers never need to import VE types.
    ///
    /// Fail-safe contract: every method that can fail returns a safe default
    /// (null / 0 / false) rather than throwing, so the caller can treat a null
    /// result as "VE is unavailable this tick" and hand control back to VE.
    /// </summary>
    public sealed class VEChargerAccessor
    {
        // The live BELVCharger instance for this tick.
        private readonly object _charger;

        // Cached reflection targets — resolved once per accessor instance.
        private readonly object _electric;          // IElectricBlockEntity / IBEPower
        private readonly PropertyInfo _currentPowerProp;
        private readonly MethodInfo _ratedPowerMethod;
        private readonly FieldInfo _electricPowerField;
        private readonly MethodInfo _setStateMethod;
        private readonly Type _enumBEState;

        // Pre-resolved enum values for the two states we need.
        private readonly object _stateOn;
        private readonly object _statePaused;

        private VEChargerAccessor(object charger, object electric,
            PropertyInfo currentPower, MethodInfo ratedPower,
            FieldInfo electricPowerField,
            MethodInfo setState, Type enumBEState,
            object stateOn, object statePaused)
        {
            _charger          = charger;
            _electric         = electric;
            _currentPowerProp = currentPower;
            _ratedPowerMethod = ratedPower;
            _electricPowerField = electricPowerField;
            _setStateMethod   = setState;
            _enumBEState      = enumBEState;
            _stateOn          = stateOn;
            _statePaused      = statePaused;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Factory
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Try to build an accessor for a running <c>BELVCharger</c> instance.
        /// Returns <c>null</c> if any required VE member is missing — the caller
        /// should return <c>true</c> so VE runs its own logic.
        /// </summary>
        public static VEChargerAccessor? TryCreate(object charger)
        {
            if (charger == null) return null;

            // ── Electric behavior ──────────────────────────────────────────
            var electric = GetProp(charger, "Electric");
            if (electric == null) return null;

            var electricType = electric.GetType();

            var currentPowerProp = electricType.GetProperty("CurrentPower",
                BindingFlags.Public | BindingFlags.Instance);
            if (currentPowerProp == null) return null;

            var ratedPowerMethod = electricType.GetMethod("RatedPower",
                BindingFlags.Public | BindingFlags.Instance);
            if (ratedPowerMethod == null) return null;

            var electricPowerField = electricType.GetField("electricpower",
                BindingFlags.Public | BindingFlags.Instance);
            if (electricPowerField == null) return null;

            // ── SetState ──────────────────────────────────────────────────
            var setStateMethod = charger.GetType().GetMethod("SetState",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (setStateMethod == null) return null;

            var stateParams = setStateMethod.GetParameters();
            if (stateParams.Length < 1 || !stateParams[0].ParameterType.IsEnum) return null;

            var enumType = stateParams[0].ParameterType;

            object? stateOn    = TryParseEnum(enumType, "On");
            object? statePaused = TryParseEnum(enumType, "Paused");
            if (stateOn == null || statePaused == null) return null;

            return new VEChargerAccessor(
                charger, electric,
                currentPowerProp, ratedPowerMethod, electricPowerField,
                setStateMethod, enumType,
                stateOn, statePaused);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public API — plain primitives only, no VE types leak out
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The ItemStack sitting in the charger's input slot, or <c>null</c>.
        /// </summary>
        public ItemStack? GetInputStack()
        {
            var slot = GetProp(_charger, "InputSlot") as ItemSlot;
            return slot?.Itemstack;
        }

        /// <summary>
        /// How many EU the charger currently holds (its <c>CurrentPower</c> property).
        /// </summary>
        public ulong GetCurrentPower()
        {
            var v = _currentPowerProp.GetValue(_electric);
            return v is ulong u ? u : 0UL;
        }

        /// <summary>
        /// The charger's rated delivery per tick
        /// (<c>electric.RatedPower(dt, isInsert: false)</c>).
        /// </summary>
        public ulong GetRatedPower(float dt)
        {
            var result = _ratedPowerMethod.Invoke(_electric, new object[] { dt, false });
            return result is ulong u ? u : 0UL;
        }

        /// <summary>
        /// Subtract <paramref name="used"/> EU from the charger's internal power
        /// field (<c>electricpower</c>), clamped to 0.
        /// </summary>
        public void DebitPower(ulong used)
        {
            if (_electricPowerField.GetValue(_electric) is ulong cur)
            {
                ulong next = used > cur ? 0UL : cur - used;
                _electricPowerField.SetValue(_electric, next);
            }
        }

        /// <summary>Set the charger's state machine to "On".</summary>
        public void SetStateOn()     => InvokeSetState(_stateOn);

        /// <summary>Set the charger's state machine to "Paused".</summary>
        public void SetStatePaused() => InvokeSetState(_statePaused);

        // ─────────────────────────────────────────────────────────────────────
        //  Internals
        // ─────────────────────────────────────────────────────────────────────

        private void InvokeSetState(object enumVal)
        {
            var p = _setStateMethod.GetParameters();
            var args = new object[p.Length];
            args[0] = enumVal;
            for (int i = 1; i < p.Length; i++)
                args[i] = p[i].HasDefaultValue ? p[i].DefaultValue! : GetDefault(p[i].ParameterType);
            _setStateMethod.Invoke(_charger, args);
        }

        private static object? GetProp(object obj, string name)
        {
            if (obj == null) return null;
            var prop = obj.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance);
            return prop?.GetValue(obj);
        }

        private static object? TryParseEnum(Type enumType, string name)
        {
            try { return Enum.Parse(enumType, name); }
            catch { return null; }
        }

        private static object GetDefault(Type t)
            => t.IsValueType ? Activator.CreateInstance(t)! : null!;
    }
}