using VEPowersuit.Items;
using VEPowersuit.Modules;
using VintageEngineering.Electrical;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// Player-side behavior for the suit's per-event modules: jump assist
    /// (upward velocity burst on jump), protection (flat damage reduction,
    /// energy-gated), fall-damage negation, and bee protection (immunity to
    /// bee damage). These need entity hooks (tick / incoming-damage) rather
    /// than the passive drain loop. It does NOT change how power is charged —
    /// it only spends stored power per activation/absorption.
    /// </summary>
    public class EntityBehaviorPowersuit : EntityBehavior
    {
        private bool wasJumping;

        public EntityBehaviorPowersuit(Entity entity) : base(entity) { }

        public override string PropertyName() => "vepowersuitdamage";

        public override void OnGameTick(float deltaTime)
        {
            base.OnGameTick(deltaTime);

            // Jump assist: on the rising edge of the jump control, add a burst of
            // upward velocity and spend a one-shot energy cost. Server-side only
            // so the motion is authoritative.
            if (entity.World.Side != EnumAppSide.Server) return;
            if (entity is not EntityPlayer ep) return;
            var player = ep.Player;
            if (player == null) return;

            bool jumping = ep.Controls.Jump;
            bool risingEdge = jumping && !wasJumping;
            wasJumping = jumping;
            if (!risingEdge) return;
            // Only when actually leaving the ground (avoid firing mid-air/in water).
            if (!entity.OnGround) return;

            var slot = SuitHelper.GetSlotForModule(player, ModuleRegistry.JumpAssist);
            var stack = slot?.Itemstack;
            if (stack == null) return;
            if (!SuitModules.IsEnabled(stack, ModuleRegistry.JumpAssist)) return;

            var chargeable = stack as IChargeableItem;
            ulong have = chargeable?.CurrentPower(stack) ?? 0;
            ulong cost = (ulong)System.Math.Max(0,
                ModuleRegistry.All[ModuleRegistry.JumpAssist].EnergyPerActivation);
            if (have < cost) return;

            chargeable?.SetPower(stack, have - cost);
            // Add upward velocity on top of the vanilla jump impulse.
            entity.Pos.Motion.Y += ModuleRegistry.JumpBoostVelocity;
            slot?.MarkDirty();
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            base.OnEntityReceiveDamage(damageSource, ref damage);

            if (damage <= 0) return;
            if (entity is not EntityPlayer ep) return;
            var player = ep.Player;
            if (player == null) return;

            // ---- Fall-damage negation (chest) ----
            if (damageSource.Type == EnumDamageType.Gravity)
            {
                if (TrySpendForDamage(player, ModuleRegistry.FallDamage,
                        ModuleRegistry.FallEnergyPerDamage, ref damage, 1f))
                    return;
            }

            // ---- Bee protection (helmet): bees deal injury damage on attack ----
            if (damageSource.Source == EnumDamageSource.Entity &&
                damageSource.SourceEntity?.Code?.Path != null &&
                damageSource.SourceEntity.Code.Path.Contains("bee"))
            {
                if (SuitHelper.HasActiveModule(player, ModuleRegistry.BeeProtection) &&
                    SuitModules.IsEnabled(
                        SuitHelper.GetSlotForModule(player, ModuleRegistry.BeeProtection)?.Itemstack,
                        ModuleRegistry.BeeProtection))
                {
                    damage = 0f;
                    return;
                }
            }

            // ---- General protection (chest): absorb a fraction of all damage ----
            TrySpendForDamage(player, ModuleRegistry.Protection,
                ModuleRegistry.ProtectionEnergyPerDamage, ref damage,
                ModuleRegistry.ProtectionDamageReduction);
        }

        /// <summary>
        /// Reduce <paramref name="damage"/> by up to <paramref name="fraction"/>
        /// of its value (fraction=1 absorbs all), spending stored power at
        /// energyPerDamage per absorbed point. Absorbs only as much as the suit
        /// can pay for. Returns true if any reduction was applied.
        /// </summary>
        private static bool TrySpendForDamage(IPlayer player, string moduleCode,
            int energyPerDamage, ref float damage, float fraction)
        {
            var slot = SuitHelper.GetSlotForModule(player, moduleCode);
            var stack = slot?.Itemstack;
            if (stack == null) return false;
            if (!SuitModules.IsEnabled(stack, moduleCode)) return false;
            if (energyPerDamage <= 0) return false;

            var chargeable = stack as IChargeableItem;
            ulong have = chargeable?.CurrentPower(stack) ?? 0;
            if (have == 0) return false;

            float wantAbsorb = damage * fraction;
            float affordable = (float)have / energyPerDamage;
            float absorbed = System.Math.Min(wantAbsorb, affordable);
            if (absorbed <= 0) return false;

            ulong cost = (ulong)System.Math.Ceiling(absorbed * energyPerDamage);
            if (cost > have) cost = have;

            chargeable?.SetPower(stack, have - cost);
            slot?.MarkDirty();
            damage = System.Math.Max(0f, damage - absorbed);
            return true;
        }
    }
}
