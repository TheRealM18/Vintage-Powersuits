using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// All energy + module state lives in the ItemStack's TreeAttribute so it
    /// persists across saves and travels with the item. Energy unit is generic
    /// ("EU"); scale it to whatever matches VE's wattage when you wire the adapter.
    /// </summary>
    public static class EnergyStore
    {
        private const string EnergyKey = "paEnergy";
        private const string MaxKey = "paMaxEnergy";
        private const string ModulesKey = "paModules";

        public static int GetEnergy(ItemStack stack)
            => stack?.Attributes?.GetInt(EnergyKey, 0) ?? 0;

        public static void SetEnergy(ItemStack stack, int value)
        {
            if (stack == null) return;
            int max = GetMaxEnergy(stack);
            if (value < 0) value = 0;
            if (value > max) value = max;
            stack.Attributes.SetInt(EnergyKey, value);
        }

        public static int GetMaxEnergy(ItemStack stack)
        {
            int fromAttr = stack?.Attributes?.GetInt(MaxKey, 0) ?? 0;
            if (fromAttr > 0) return fromAttr;
            // Fallback to the value defined in the itemtype JSON attributes.
            int fromJson = stack?.Collectible?.Attributes?["maxEnergy"]?.AsInt(100000) ?? 100000;
            return fromJson;
        }

        public static void SetMaxEnergy(ItemStack stack, int value)
            => stack?.Attributes?.SetInt(MaxKey, value);

        /// <summary>Spend energy if available. Returns false if insufficient.</summary>
        public static bool TryConsume(ItemStack stack, int amount)
        {
            int cur = GetEnergy(stack);
            if (cur < amount) return false;
            SetEnergy(stack, cur - amount);
            return true;
        }

        public static bool HasModule(ItemStack stack, string moduleCode)
        {
            var tree = stack?.Attributes?.GetTreeAttribute(ModulesKey);
            return tree != null && tree.GetBool(moduleCode, false);
        }

        public static void SetModule(ItemStack stack, string moduleCode, bool installed)
        {
            if (stack == null) return;
            var tree = stack.Attributes.GetOrAddTreeAttribute(ModulesKey);
            tree.SetBool(moduleCode, installed);
        }
    }
}
