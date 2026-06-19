using System.Collections.Generic;
using VEPowersuit.Items;
using VEPowersuit.Modules;
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
        /// Enumerate every worn power-suit piece (helmet, chest, leggings).
        /// Modules can live on any of the three pieces, so systems that work
        /// per-module iterate these and match by slot.
        /// </summary>
        public static IEnumerable<ItemSlot> GetWornSuitSlots(IPlayer player)
        {
            var inv = player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (inv == null) yield break;

            foreach (var slot in inv)
            {
                if (slot?.Itemstack?.Collectible is ItemVEPowersuit)
                    yield return slot;
            }
        }

        /// <summary>
        /// Returns the worn slot whose armor piece can host the given module
        /// (its clothesCategory matches the module's allowed slot), or null.
        /// </summary>
        public static ItemSlot? GetSlotForModule(IPlayer player, string moduleCode)
        {
            string? want = ModuleRegistry.SlotFor(moduleCode);
            if (want == null) return null;

            foreach (var slot in GetWornSuitSlots(player))
            {
                string? cat = (slot.Itemstack?.Collectible as ItemVEPowersuit)?.ClothesCategory;
                if (cat == want) return slot;
            }
            return null;
        }

        /// <summary>
        /// True if the player wears the right piece for this module, the module
        /// is installed on it, and that piece currently has >= minEnergy stored.
        /// </summary>
        public static bool HasActiveModule(IPlayer player, string moduleCode, int minEnergy = 1)
        {
            ItemSlot? slot = GetSlotForModule(player, moduleCode);
            ItemStack? stack = slot?.Itemstack;
            if (stack == null)
            {
                // Backwards-compatible fallback for the original modules that
                // were stored on the core chest piece.
                stack = GetCoreStack(player);
                if (stack == null) return false;
            }
            if (!SuitModules.IsInstalled(stack, moduleCode)) return false;
            return ((int)((stack as IChargeableItem)?.CurrentPower(stack) ?? 0)) >= minEnergy;
        }
    }
}
