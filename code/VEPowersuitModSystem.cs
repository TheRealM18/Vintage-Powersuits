using System.Collections.Generic;
using VEPowersuit.Items;
using VEPowersuit.Modules;
using VEPowersuit.Network;
using VEPowersuit.Systems;
using VintageEngineering.Electrical;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VEPowersuit
{
    public class VEPowersuitModSystem : ModSystem
    {
        private const string Channel = "vepowersuit";

        private ICoreServerAPI? sapi;
        private ICoreClientAPI? capi;
        private IServerNetworkChannel? serverChannel;
        private IClientNetworkChannel? clientChannel;
        private Systems.NightVisionRenderer? nightVision;

        // Static client-channel ref so block entities (which don't hold the mod
        // system instance) can send the install request through our own stable
        // network channel rather than the version-finicky block-entity packet API.
        private static IClientNetworkChannel? staticClientChannel;

        /// <summary>Called from the installer block's GUI to request an install.</summary>
        public static void SendInstall(Vintagestory.API.Client.ICoreClientAPI capi,
            Vintagestory.API.MathTools.BlockPos pos)
        {
            staticClientChannel?.SendPacket(new InstallModulePacket { X = pos.X, Y = pos.Y, Z = pos.Z });
        }

        // Tracks players currently flying via power armor (server-authoritative).
        private readonly HashSet<string> flyingPlayers = new();

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            // Register the armor item class so itemtype JSON "class": "VEPowersuit" resolves.
            api.RegisterItemClass("ItemVEPowersuit", typeof(ItemVEPowersuit));

            // Module installer block + its block entity.
            api.RegisterBlockClass("VEPowersuitModuleInstaller", typeof(Blocks.BlockModuleInstaller));
            api.RegisterBlockEntityClass("VEPowersuitModuleInstaller", typeof(Blocks.BlockEntityModuleInstaller));

            // Damage-handling behavior (protection / fall / bee protection).
            api.RegisterEntityBehaviorClass("vepowersuitdamage", typeof(Systems.EntityBehaviorPowersuit));
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        // ---------------- SERVER ----------------
        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            serverChannel = api.Network.RegisterChannel(Channel)
                .RegisterMessageType<ToggleModulePacket>()
                .RegisterMessageType<ToggleFlightPacket>()
                .RegisterMessageType<EnergySyncPacket>()
                .RegisterMessageType<InstallModulePacket>()
                .RegisterMessageType<ModuleStatePacket>()
                .RegisterMessageType<RequestModuleStatePacket>()
                .SetMessageHandler<ToggleModulePacket>(OnToggleModule)
                .SetMessageHandler<ToggleFlightPacket>(OnToggleFlight)
                .SetMessageHandler<InstallModulePacket>(OnInstallModule)
                .SetMessageHandler<RequestModuleStatePacket>(OnRequestModuleState);

            // 1Hz drain/maintenance tick.
            api.Event.RegisterGameTickListener(OnServerTick, 1000);
            api.Event.PlayerDisconnect += p => StopFlight(p);

            // Attach the per-event behavior (jump/fall/protection/bee). Both
            // events are used because entity readiness can differ by load order;
            // attachment is idempotent thanks to the GetBehavior null-check.
            api.Event.PlayerJoin += OnPlayerJoin;
            api.Event.PlayerNowPlaying += OnPlayerJoin;
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
            var e = player?.Entity;
            if (e == null) return;
            // Add the per-event behavior once per entity.
            if (e.GetBehavior<Systems.EntityBehaviorPowersuit>() == null)
                e.AddBehavior(new Systems.EntityBehaviorPowersuit(e));
        }

        private ItemSlot? GetChestSlot(IPlayer player)
        {
            // Body-armor slot (the chestplate, flagged isCore in JSON). Shared
            // with the entity behavior and night-vision renderer via SuitHelper
            // so all three agree on what the "active suit" is.
            return SuitHelper.GetCoreSlot(player);
        }

        private void OnServerTick(float dt)
        {
            if (sapi == null) return;
            foreach (IPlayer player in sapi.World.AllOnlinePlayers)
            {
                // AllOnlinePlayers also returns players still connecting, whose
                // entity/behaviors may not be initialized yet. Skip anyone not
                // fully in-world to avoid NREs inside GetBehavior etc.
                if (player is IServerPlayer sp && sp.ConnectionState != EnumClientState.Playing) continue;
                if (player?.Entity == null) continue;

                ItemSlot? coreSlot = GetChestSlot(player);
                if (coreSlot == null) { StopFlight(player); continue; }
                ItemStack? coreStack = coreSlot.Itemstack;
                if (coreStack == null) { StopFlight(player); continue; }

                bool flying = flyingPlayers.Contains(player.PlayerUID);

                // Drain + maintain every worn suit piece. Each module only
                // matters on the piece whose slot it belongs to; flight/sprint
                // gating is unchanged.
                foreach (ItemSlot slot in SuitHelper.GetWornSuitSlots(player))
                {
                    ItemStack? stack = slot.Itemstack;
                    if (stack == null) continue;

                    // Seed a fresh stack (e.g. a creative-tab suit) so it's charged
                    // and configured the first time it's worn.
                    if (stack.Collectible is ItemVEPowersuit suit0)
                        suit0.EnsureInitialized(stack);

                    string? cat = (stack.Collectible as ItemVEPowersuit)?.ClothesCategory;

                    // Drain per-tick modules. (Power math below is unchanged.)
                    foreach (var kv in ModuleRegistry.All)
                    {
                        var mod = kv.Value;
                        if (mod.EnergyPerTick <= 0) continue;
                        // Only drain a module on the piece that actually hosts its slot.
                        if (ModuleRegistry.SlotFor(kv.Key) is string ms && cat != null && ms != cat) continue;
                        // Must be installed AND switched on in the GUI.
                        if (!SuitModules.IsEnabled(stack, kv.Key)) continue;

                        // Flight only drains while actually flying.
                        if (kv.Key == ModuleRegistry.Flight && !flying) continue;
                        // Sprint assist only drains while sprinting.
                        if (kv.Key == ModuleRegistry.SprintAssist &&
                            !player.Entity.Controls.Sprint) continue;
                        // Swim assist only drains while in/under liquid.
                        if (kv.Key == ModuleRegistry.SwimAssist &&
                            !(player.Entity.FeetInLiquid || player.Entity.Swimming)) continue;

                        IChargeableItem? suitpart = stack as IChargeableItem;
                        if (suitpart == null) continue;

                        ulong rated = suitpart?.RatedPower(stack, dt, false) ?? 0;
                        if (suitpart?.CurrentPower(stack) == 0 || suitpart?.CurrentPower(stack) < rated)
                        {
                            // suit is low on power
                            if (kv.Key == ModuleRegistry.Flight) StopFlight(player);
                        }
                        else
                        {
                            ulong? newpower = suitpart?.CurrentPower(stack) - rated;
                            suitpart?.SetPower(stack, newpower.GetValueOrDefault());
                        }
                    }

                    slot.MarkDirty();
                }

                // If flight got switched off in the GUI (or uninstalled) while
                // airborne, drop the player out of flight.
                ItemStack? flightStack = SuitHelper.GetSlotForModule(player, ModuleRegistry.Flight)?.Itemstack ?? coreStack;
                if (flying && !SuitModules.IsEnabled(flightStack, ModuleRegistry.Flight))
                {
                    StopFlight(player);
                    flying = false;
                }

                // Apply movement + life-support module effects (per worn piece).
                ApplySprintAssist(player, SuitHelper.GetSlotForModule(player, ModuleRegistry.SprintAssist)?.Itemstack);
                ApplyStepAssist(player);
                ApplySwimAssist(player);
                ApplyTemperature(player);
                ApplyWaterBreathing(player);
                ApplyFeeding(player, dt);

                SyncEnergy(player, coreStack, flying);
            }
        }

        // ---- New module effect appliers -------------------------------------

        private void ApplyStepAssist(IPlayer player)
        {
            if (player?.Entity == null) return;
            var physics = player.Entity.GetBehavior<Vintagestory.GameContent.EntityBehaviorControlledPhysics>();
            if (physics == null) return;

            bool active = SuitHelper.HasActiveModule(player, ModuleRegistry.StepAssist)
                          && SuitModules.IsEnabled(
                                 SuitHelper.GetSlotForModule(player, ModuleRegistry.StepAssist)?.Itemstack,
                                 ModuleRegistry.StepAssist);

            // Stash the original step height once so we can restore it when off.
            const string key = "vepowersuit:origStep";
            var wa = player.Entity.WatchedAttributes;
            if (active)
            {
                if (!wa.HasAttribute(key)) wa.SetFloat(key, physics.StepHeight);
                physics.StepHeight = ModuleRegistry.StepAssistHeight;
            }
            else if (wa.HasAttribute(key))
            {
                physics.StepHeight = wa.GetFloat(key);
                wa.RemoveAttribute(key);
            }
        }

        private void ApplySwimAssist(IPlayer player)
        {
            if (player?.Entity == null) return;
            bool inLiquid = player.Entity.FeetInLiquid || player.Entity.Swimming;
            bool active = inLiquid
                          && SuitHelper.HasActiveModule(player, ModuleRegistry.SwimAssist)
                          && SuitModules.IsEnabled(
                                 SuitHelper.GetSlotForModule(player, ModuleRegistry.SwimAssist)?.Itemstack,
                                 ModuleRegistry.SwimAssist);

            float bonus = active ? ModuleRegistry.SwimWalkSpeedBonus : 0f;
            player.Entity.Stats.Set("walkspeed", "vepowersuit_swim", bonus, true);
        }

        private void ApplyTemperature(IPlayer player)
        {
            if (player?.Entity == null) return;
            var bt = player.Entity.GetBehavior<Vintagestory.GameContent.EntityBehaviorBodyTemperature>();
            if (bt == null) return;

            bool heating = SuitHelper.HasActiveModule(player, ModuleRegistry.Heating)
                           && SuitModules.IsEnabled(
                                  SuitHelper.GetSlotForModule(player, ModuleRegistry.Heating)?.Itemstack,
                                  ModuleRegistry.Heating);
            bool cooling = SuitHelper.HasActiveModule(player, ModuleRegistry.Cooling)
                           && SuitModules.IsEnabled(
                                  SuitHelper.GetSlotForModule(player, ModuleRegistry.Cooling)?.Itemstack,
                                  ModuleRegistry.Cooling);

            // Body temperature is stored in the bodyTemp tree under "bodytemp".
            var wa = player.Entity.WatchedAttributes;
            var tree = wa.GetTreeAttribute("bodyTemp");
            if (tree == null) return;

            float cur = tree.GetFloat("bodytemp", 37f);
            float target = cur;
            if (heating && cur < ModuleRegistry.HeatingTargetTemp) target = ModuleRegistry.HeatingTargetTemp;
            if (cooling && cur > ModuleRegistry.CoolingTargetTemp) target = ModuleRegistry.CoolingTargetTemp;

            if (target != cur)
            {
                tree.SetFloat("bodytemp", target);
                wa.MarkPathDirty("bodyTemp");
            }
        }

        private void ApplyFeeding(IPlayer player, float dt)
        {
            if (player?.Entity == null) return;
            ItemSlot? slot = SuitHelper.GetSlotForModule(player, ModuleRegistry.Feeding);
            ItemStack? stack = slot?.Itemstack;
            if (stack == null) return;
            if (!SuitModules.IsEnabled(stack, ModuleRegistry.Feeding)) return;

            // Hunger is tracked in WatchedAttributes (currentsaturation / maxsaturation).
            var wa = player.Entity.WatchedAttributes;
            var tree = wa.GetTreeAttribute("hunger");
            float max = tree?.GetFloat("maxsaturation", 1500f) ?? 1500f;
            float cur = tree?.GetFloat("currentsaturation", max) ?? max;
            if (max <= 0 || cur >= max * ModuleRegistry.FeedingHungerThreshold) return;

            // Spend a one-shot activation cost, then top the player up.
            var chargeable = stack as IChargeableItem;
            ulong rated = chargeable?.RatedPower(stack, dt, false) ?? 0;
            ulong cost = (ulong)System.Math.Max(ModuleRegistry.All[ModuleRegistry.Feeding].EnergyPerActivation, (int)rated);
            ulong have = chargeable?.CurrentPower(stack) ?? 0;
            if (have < cost) return;

            chargeable?.SetPower(stack, have - cost);
            // ReceiveSaturation is the supported way to restore satiety.
            player.Entity.ReceiveSaturation(ModuleRegistry.FeedingSaturationPerActivation,
                Vintagestory.API.Common.EnumFoodCategory.Unknown, 0f, 1f);
            slot?.MarkDirty();
        }

        /// <summary>True if the player has the bee-protection module active (read by damage hook).</summary>
        public bool HasBeeProtection(IPlayer player)
            => SuitHelper.HasActiveModule(player, ModuleRegistry.BeeProtection)
               && SuitModules.IsEnabled(
                      SuitHelper.GetSlotForModule(player, ModuleRegistry.BeeProtection)?.Itemstack,
                      ModuleRegistry.BeeProtection);

        private void ApplyWaterBreathing(IPlayer player)
        {
            if (player?.Entity == null) return;
            bool active = SuitHelper.HasActiveModule(player, ModuleRegistry.WaterBreathing)
                          && SuitModules.IsEnabled(
                                 SuitHelper.GetSlotForModule(player, ModuleRegistry.WaterBreathing)?.Itemstack,
                                 ModuleRegistry.WaterBreathing);
            if (!active) return;

            // Top oxygen back up so the player never drowns while powered.
            var wa = player.Entity.WatchedAttributes;
            float max = wa.GetFloat("maxoxygen", 40000f);
            if (wa.GetFloat("oxygen", max) < max)
            {
                wa.SetFloat("oxygen", max);
                wa.MarkPathDirty("oxygen");
            }
        }


        private void ApplySprintAssist(IPlayer player, ItemStack? stack)
        {
            bool active = SuitModules.IsEnabled(stack, ModuleRegistry.SprintAssist)
                          && player.Entity.Controls.Sprint
                          && (stack as IChargeableItem)?.CurrentPower(stack) > 0;

            const string statCode = "vepowersuit_sprint";
            float bonus = active ? ModuleRegistry.SprintWalkSpeedBonus : 0f;
            player.Entity.Stats.Set("walkspeed", statCode, bonus, true);
        }

        private void OnToggleFlight(IServerPlayer player, ToggleFlightPacket packet)
        {
            // Flight lives on the chest; resolve the hosting piece explicitly.
            var slot = SuitHelper.GetSlotForModule(player, ModuleRegistry.Flight) ?? GetChestSlot(player);
            if (slot == null) return;
            var stack = slot.Itemstack;

            // Flight must be installed AND switched on in the GUI to engage.
            if (!SuitModules.IsEnabled(stack, ModuleRegistry.Flight)) { StopFlight(player); return; }

            if (packet.WantFlying && (stack as IChargeableItem)?.CurrentPower(stack) > 0)
                StartFlight(player);
            else
                StopFlight(player);

            SyncEnergy(player, stack, flyingPlayers.Contains(player.PlayerUID));
        }

        private void StartFlight(IPlayer player)
        {
            flyingPlayers.Add(player.PlayerUID);
            var e = player.Entity;
            // NoClip stays off; we want collision flight, not spectator.
            e.Controls.IsFlying = true;
            e.Controls.NoClip = false;
            player.WorldData.FreeMove = true; // server-tracked free move
        }

        private void StopFlight(IPlayer player)
        {
            if (player == null) return;
            flyingPlayers.Remove(player.PlayerUID);
            var e = player.Entity;
            if (e != null)
            {
                e.Controls.IsFlying = false;
                player.WorldData.FreeMove = false;
            }
        }

        private void OnToggleModule(IServerPlayer player, ToggleModulePacket packet)
        {
            // Resolve the piece that hosts this module's slot (helmet/chest/legs).
            var slot = SuitHelper.GetSlotForModule(player, packet.ModuleCode) ?? GetChestSlot(player);
            if (slot == null) return;
            var stack = slot.Itemstack;

            // GUI toggles ENABLED state of an already-installed module.
            // (Installation happens at the installer block, not here.)
            if (!SuitModules.IsInstalled(stack, packet.ModuleCode))
            {
                // Not installed: nothing to toggle; just resync so the client
                // GUI corrects itself.
                SendModuleState(player, null);
                return;
            }

            bool now = packet.DesiredOn;
            SuitModules.SetEnabled(stack, packet.ModuleCode, now);

            // If they just turned flight off, make sure they stop flying.
            if (packet.ModuleCode == ModuleRegistry.Flight && !now)
            {
                StopFlight(player);
                // Push an immediate energy/flight sync so the client's LastFlying
                // (used by the flight hotkey) doesn't lag a full tick behind.
                SyncEnergy(player, stack, false);
            }

            slot.MarkDirty();
            SendModuleState(player, null);
        }

        private void OnRequestModuleState(IServerPlayer player, RequestModuleStatePacket packet)
        {
            SendModuleState(player, null);
        }

        /// <summary>
        /// Send the installed+enabled state of all modules to the player's GUI.
        /// Each module is resolved on the worn piece that hosts its slot, so the
        /// panel reflects modules across helmet, chest, and leggings.
        /// The <paramref name="_"/> parameter is ignored (kept for call sites).
        /// </summary>
        private void SendModuleState(IServerPlayer player, ItemStack? _)
        {
            var codes = new List<string>();
            var installed = new List<bool>();
            var enabled = new List<bool>();

            foreach (var kv in ModuleRegistry.All)
            {
                var pieceStack = SuitHelper.GetSlotForModule(player, kv.Key)?.Itemstack;
                codes.Add(kv.Key);
                bool inst = pieceStack != null && SuitModules.IsInstalled(pieceStack, kv.Key);
                installed.Add(inst);
                enabled.Add(inst && SuitModules.IsEnabled(pieceStack, kv.Key));
            }

            serverChannel?.SendPacket(new ModuleStatePacket
            {
                Codes = codes.ToArray(),
                Installed = installed.ToArray(),
                Enabled = enabled.ToArray()
            }, player);
        }

        private void OnInstallModule(IServerPlayer player, InstallModulePacket packet)
        {
            var pos = new Vintagestory.API.MathTools.BlockPos(packet.X, packet.Y, packet.Z);
            // Basic sanity: only allow if the player is reasonably near the block.
            if (player.Entity != null)
            {
                var p = player.Entity.Pos;
                double dx = p.X - (packet.X + 0.5);
                double dy = p.Y - (packet.Y + 0.5);
                double dz = p.Z - (packet.Z + 0.5);
                if (dx * dx + dy * dy + dz * dz > 12 * 12) return;
            }

            if (sapi?.World.BlockAccessor.GetBlockEntity(pos)
                is Blocks.BlockEntityModuleInstaller be)
            {
                be.TryInstall(out _);
            }
        }

        private void SyncEnergy(IPlayer player, ItemStack? stack, bool flying)
        {            
            if (player is IServerPlayer sp)
                serverChannel?.SendPacket(new EnergySyncPacket
                {
                    Energy = (int)((stack as IChargeableItem)?.CurrentPower(stack) ?? 0),
                    MaxEnergy = (int)((stack as IChargeableItem)?.MaxPower ?? 0),
                    Flying = flying
                }, sp);
        }

        // ---------------- CLIENT ----------------
        public int LastEnergy { get; private set; }
        public int LastMaxEnergy { get; private set; }
        public bool LastFlying { get; private set; }

        // Latest module install/enable state from the server, keyed by code.
        public readonly Dictionary<string, (bool installed, bool enabled)> ModuleState = new();

        private Gui.GuiDialogModules? openModuleDialog;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            clientChannel = api.Network.RegisterChannel(Channel)
                .RegisterMessageType<ToggleModulePacket>()
                .RegisterMessageType<ToggleFlightPacket>()
                .RegisterMessageType<EnergySyncPacket>()
                .RegisterMessageType<InstallModulePacket>()
                .RegisterMessageType<ModuleStatePacket>()
                .RegisterMessageType<RequestModuleStatePacket>()
                .SetMessageHandler<EnergySyncPacket>(OnEnergySync)
                .SetMessageHandler<ModuleStatePacket>(OnModuleState);
            staticClientChannel = clientChannel;

            api.Input.RegisterHotKey("vepowersuit_flight", Lang.Get("vepowersuit:hotkey-flight"),
                GlKeys.R, HotkeyType.CharacterControls);
            api.Input.SetHotKeyHandler("vepowersuit_flight", OnFlightHotkey);

            api.Input.RegisterHotKey("vepowersuit_gui", Lang.Get("vepowersuit:hotkey-gui"),
                GlKeys.U, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("vepowersuit_gui", OnGuiHotkey);

            // Client-side Night Vision driver. Runs before the main opaque pass
            // each frame so the brightness uniform is set before the world draws.
            nightVision = new NightVisionRenderer(api);
            api.Event.RegisterRenderer(nightVision, EnumRenderStage.Before, "vepowersuit-nightvision");
        }

        private bool OnFlightHotkey(KeyCombination comb)
        {
            // Toggle: ask server for the opposite of current state.
            clientChannel?.SendPacket(new ToggleFlightPacket { WantFlying = !LastFlying });
            return true;
        }

        private bool OnGuiHotkey(KeyCombination comb)
        {
            if (openModuleDialog != null && openModuleDialog.IsOpened())
            {
                openModuleDialog.TryClose();
                return true;
            }
            if (capi == null || clientChannel == null) return false;
            openModuleDialog = new Gui.GuiDialogModules(capi, this, clientChannel);
            openModuleDialog.TryOpen();
            // Ask the server for current module state so buttons render correctly.
            clientChannel.SendPacket(new RequestModuleStatePacket());
            return true;
        }

        private void OnModuleState(ModuleStatePacket p)
        {
            ModuleState.Clear();            
            int n = p.Codes?.Length ?? 0;            
            for (int i = 0; i < n; i++)
            {
                bool inst = p?.Installed != null && i < p.Installed.Length && p.Installed[i];
                bool en = p?.Enabled != null && i < p.Enabled.Length && p.Enabled[i];
                if (p == null || p.Codes == null || p.Codes[i] == null) continue;
                ModuleState[p.Codes[i]] = (inst, en);
            }
            // Refresh the GUI if it's open so buttons reflect the new state.
            openModuleDialog?.RefreshStates();
        }

        private void OnEnergySync(EnergySyncPacket p)
        {
            LastEnergy = p.Energy;
            LastMaxEnergy = p.MaxEnergy;
            LastFlying = p.Flying;
        }
    }
}