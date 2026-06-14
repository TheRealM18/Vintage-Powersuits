using Vintagestory.API.Common;

namespace VEPowersuit.Blocks
{
    /// <summary>
    /// The Module Installer block. Right-click opens its GUI; all logic lives in
    /// the block entity. This passthrough simply forwards the interaction.
    ///
    /// OnBlockInteractStart fires on BOTH client and server. We forward to the BE
    /// on both sides and return its result — the BE opens the dialog client-side
    /// and (via the standard Open packet + base class) opens the inventory
    /// server-side, which is what actually makes the slots live.
    /// </summary>
    public class BlockModuleInstaller : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (blockSel?.Position == null) return false;

            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position)
                     as BlockEntityModuleInstaller;

            if (be != null)
            {
                return be.OnPlayerRightClick(byPlayer, blockSel);
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        // Allow the right-click to be held/repeated without spamming; we only act
        // on the initial press above. Returning false ends the interaction step.
        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world,
            IPlayer byPlayer, BlockSelection blockSel)
        {
            return false;
        }
    }
}