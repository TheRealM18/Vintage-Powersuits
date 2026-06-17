using VEPowersuit.Items;
using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// Thin accessor over the power VE's ItemChargable stores on the stack
    /// (attribute "currentpower"). This is NOT a custom storage system — it just
    /// reads/writes the same attribute VE owns, so the rest of the suit (HUD,
    /// flight, module drain) has a clean call site. Max/PPS come from the item's
    /// JSON via ItemChargable.
    /// </summary>
    public static class SuitPower
    {
        public const string Key = "currentpower";

        public static long Get(ItemStack stack)
            => stack?.Attributes?.GetLong(Key, 0) ?? 0;

        public static long Max(ItemStack stack)
            => stack?.Collectible is ItemVEPowersuit suit ? (long)suit.MaxPower : 0;

        public static void Set(ItemStack stack, long value)
        {
            if (stack == null) return;
            long max = Max(stack);
            if (value < 0) value = 0;
            if (max > 0 && value > max) value = max;
            stack.Attributes.SetLong(Key, value);
        }

        /// <summary>Consume up to amount; returns true if there was enough.</summary>
        public static bool TryConsume(ItemStack stack, long amount)
        {
            long cur = Get(stack);
            if (cur < amount) return false;
            Set(stack, cur - amount);
            return true;
        }
    }
}
