using VEPowersuit.Items;
using VintageEngineering.Electrical;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// Shared helpers for locating the worn power suit and its module state.
    /// Centralizes the "find the core chest piece in the character inventory"
    /// logic so the mod system, the entity behavior, and the client renderer
    /// all agree on what counts as an active suit.
    /// </summary>
    public static class SuitHelper
    {
        /// <summary>
        /// Returns the inventory slot holding the worn CORE power-suit piece
        /// (the chestplate, flagged isCore in JSON), or null if none is worn.
        /// </summary>
        public static ItemSlot? GetCoreSlot(IPlayer player)
        {
            var inv = player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (inv == null) return null;

            foreach (var slot in inv)
            {
                if (slot?.Itemstack?.Collectible is ItemVEPowersuit pa && pa.IsCore)
                    return slot;
            }
            return null;
        }

        /// <summary>The worn core suit's itemstack, or null.</summary>
        public static ItemStack? GetCoreStack(IPlayer player)
            => GetCoreSlot(player)?.Itemstack;

        /// <summary>
        /// True if the player wears a core suit that has the given module
        /// installed AND currently has at least minEnergy stored.
        /// </summary>
        public static bool HasActiveModule(IPlayer player, string moduleCode, int minEnergy = 1)
        {
            ItemStack? stack = GetCoreStack(player);
            if (stack == null) return false;
            if (!SuitModules.IsInstalled(stack, moduleCode)) return false;
            return ((int)((stack as IChargeableItem)?.CurrentPower(stack) ?? 0)) >= minEnergy;
        }
    }
}
