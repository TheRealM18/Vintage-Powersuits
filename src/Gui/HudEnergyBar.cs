using Vintagestory.API.Client;

namespace VEPowersuit.Gui
{
    /// <summary>
    /// Minimal always-on energy readout. Registered as an in-game HUD element.
    /// Reads the last-synced values from the mod system (no per-frame slot reads).
    /// Hook this up in StartClientSide if you want it visible; left optional so
    /// the core mod stays lean.
    /// </summary>
    public class HudEnergyBar : HudElement
    {
        private readonly VEPowersuitModSystem mod;

        public HudEnergyBar(ICoreClientAPI capi, VEPowersuitModSystem mod) : base(capi)
        {
            this.mod = mod;
        }

        public override void OnRenderGUI(float deltaTime)
        {
            base.OnRenderGUI(deltaTime);
            // For a real bar, compose a StatBar element and update its value
            // from mod.LastEnergy / mod.LastMaxEnergy here.
        }

        public override bool ShouldReceiveKeyboardEvents() => false;
    }
}
