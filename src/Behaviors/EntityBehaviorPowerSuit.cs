using System;
using VEPowersuit.Modules;
using VEPowersuit.Systems;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VEPowersuit.Behaviors
{
    /// <summary>
    /// Attached at runtime to every player entity. Implements the two module
    /// effects that are driven by entity-level events rather than the central
    /// 1Hz tick:
    ///
    ///   • Jump Assist  — on the rising edge of the Jump control while grounded,
    ///                    adds upward velocity and charges EnergyPerActivation.
    ///   • Fall Damage  — intercepts incoming fall damage and spends energy to
    ///                    cancel as much of it as the suit can afford.
    ///
    /// All energy access is server-authoritative; the behavior no-ops on the
    /// client side so the two sides don't double-charge.
    /// </summary>
    public class EntityBehaviorPowerSuit : EntityBehavior
    {
        public const string Name = "vepowersuitplayer";

        private bool prevJump;

        public EntityBehaviorPowerSuit(Entity entity) : base(entity) { }

        public override string PropertyName() => Name;

        private bool IsServer => entity?.World?.Side == EnumAppSide.Server;

        private IPlayer Player =>
            entity is EntityPlayer ep ? ep.Player : null;

        public override void OnGameTick(float deltaTime)
        {
            base.OnGameTick(deltaTime);
            if (!IsServer) return;

            var player = Player;
            if (player?.Entity == null) return;

            var controls = player.Entity.Controls;
            bool jumpNow = controls != null && controls.Jump;

            // Rising edge: pressed this tick, wasn't pressed last tick, and the
            // player is actually on the ground (so it boosts a real jump, not a
            // mid-air hold). We let the engine apply its normal jump impulse and
            // add ours on top for a higher leap.
            bool grounded = player.Entity.OnGround;

            if (jumpNow && !prevJump && grounded)
            {
                TryJumpAssist(player);
            }

            prevJump = jumpNow;
        }

        private void TryJumpAssist(IPlayer player)
        {
            var stack = SuitHelper.GetCoreStack(player);
            if (stack == null) return;
            if (!EnergyStore.HasModule(stack, ModuleRegistry.JumpAssist)) return;

            var mod = ModuleRegistry.Get(ModuleRegistry.JumpAssist);
            int cost = mod?.EnergyPerActivation ?? 0;

            if (!EnergyStore.TryConsume(stack, cost)) return;

            // Add upward velocity on top of the normal jump impulse.
            var pos = player.Entity.SidedPos;
            pos.Motion.Y += ModuleRegistry.JumpBoostVelocity;

            SuitHelper.GetCoreSlot(player)?.MarkDirty();
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            base.OnEntityReceiveDamage(damageSource, ref damage);

            if (!IsServer) return;
            if (damage <= 0) return;
            if (damageSource == null) return;

            // Only handle fall damage.
            if (damageSource.Source != EnumDamageSource.Fall &&
                damageSource.Type != EnumDamageType.Gravity)
                return;

            var player = Player;
            if (player == null) return;

            var stack = SuitHelper.GetCoreStack(player);
            if (stack == null) return;
            if (!EnergyStore.HasModule(stack, ModuleRegistry.FallDamage)) return;

            int available = EnergyStore.GetEnergy(stack);
            if (available <= 0) return;

            // How much damage can we afford to cancel?
            int costPer = Math.Max(1, ModuleRegistry.FallEnergyPerDamage);
            float affordableDamage = available / (float)costPer;

            float negated = Math.Min(damage, affordableDamage);
            int energySpent = (int)Math.Ceiling(negated * costPer);

            EnergyStore.TryConsume(stack, energySpent);
            damage -= negated;
            if (damage < 0) damage = 0;

            SuitHelper.GetCoreSlot(player)?.MarkDirty();
        }
    }
}
