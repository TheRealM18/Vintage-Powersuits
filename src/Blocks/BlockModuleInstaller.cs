using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VEPowersuit.Blocks
{
    /// <summary>
    /// The Module Installer block. Right-click opens its GUI (handled by the
    /// block entity). Pure passthrough block; all logic lives in the BE.
    /// </summary>
    public class BlockModuleInstaller : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position)
                is BlockEntityModuleInstaller be)
            {
                return be.OnPlayerRightClick(byPlayer, blockSel);
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }
}
