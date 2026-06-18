using VEPowersuit.Modules;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// Client-side driver for the Night Vision module. Each frame it checks
    /// whether the local player wears a core suit with the night-vision module
    /// installed and energy remaining, then ramps the game's built-in
    /// NightVisionStrength shader uniform up or down accordingly. This is the
    /// same uniform the vanilla temporal-vision effect uses, so it reads as a
    /// clean brightness boost with no custom shaders required.
    ///
    /// The per-tick ENERGY drain for night vision is handled server-side in the
    /// mod system's 1Hz tick; this class only drives the visual.
    /// </summary>
    public class NightVisionRenderer : IRenderer
    {
        private readonly ICoreClientAPI capi;
        private float current; // 0..NightVisionStrength

        public double RenderOrder => 0.0;
        public int RenderRange => 0;

        public NightVisionRenderer(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            var player = capi.World?.Player;
            if (player == null) return;

            bool active = SuitHelper.HasActiveModule(player, ModuleRegistry.NightVision);

            float target = active ? ModuleRegistry.NightVisionStrength : 0f;

            // Smooth ramp so it fades in/out instead of snapping.
            float ramp = ModuleRegistry.NightVisionRampSeconds <= 0
                ? 1f
                : dt / ModuleRegistry.NightVisionRampSeconds;

            if (current < target) current = System.Math.Min(target, current + ramp * ModuleRegistry.NightVisionStrength);
            else if (current > target) current = System.Math.Max(target, current - ramp * ModuleRegistry.NightVisionStrength);

            capi.Render.ShaderUniforms.NightVisionStrength = current;
        }

        public void Dispose()
        {
            // Make sure we don't leave the screen brightened after unload.
            if (capi?.Render?.ShaderUniforms != null)
                capi.Render.ShaderUniforms.NightVisionStrength = 0f;
        }
    }
}
