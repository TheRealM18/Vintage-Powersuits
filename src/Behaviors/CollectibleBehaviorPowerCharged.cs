using System.Text;
using VEPowersuit.Systems;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace VEPowersuit.Behaviors
{
    /// <summary>
    /// Marks a collectible (a power-suit piece) as <b>power-only</b>: its
    /// "fuel" is the suit's own EU energy store (<see cref="EnergyStore"/>) and
    /// NOT vanilla durability.
    ///
    /// WHY THIS EXISTS
    /// ----------------
    /// The itemtype JSON sets <c>"chargable": false</c> so Vintage Engineering's
    /// charger never runs its built-in <i>durability-topup</i> charge route
    /// (the route gated on that flag, which would treat the suit like a tool
    /// being repaired). Instead, <see cref="Systems.VEChargerDurabilityRedirectPatch"/> intercepts
    /// the charger tick for our suit and pushes power straight into the EU store
    /// through VE's <c>IChargeableItem</c> interface.
    ///
    /// This behavior is the explicit, per-piece switch the suit author asked
    /// for: "if this behavior is on, no durability — only power — and it can
    /// charge via my Harmony patch." When attached:
    ///
    ///   - <see cref="IsPowerOnly"/> is true: the piece is a battery; its
    ///     condition is its stored EU.
    ///   - <see cref="WantsPatchCharging"/> is true: <see cref="Systems.VEChargerDurabilityRedirectPatch"/>
    ///     is authorized to take over the charger tick and route to EU,
    ///     regardless of the JSON <c>chargable</c> value. So you keep
    ///     <c>chargable: false</c> (no VE durability charging) and still charge.
    ///   - <see cref="OnDamageItem"/> cancels ALL vanilla durability loss, so the
    ///     piece never wears down or breaks from use/combat: only EU depletes.
    ///
    /// CONFIG (optional, in the behavior's JSON "properties")
    ///   "powerOnly":     bool (default true) - treat condition as EU.
    ///   "patchCharging": bool (default true) - allow the charger patch takeover.
    ///   "noDurability":  bool (default true) - cancel durability loss entirely.
    /// </summary>
    public class CollectibleBehaviorPowerCharged : CollectibleBehavior
    {
        public const string Name = "vepowersuitpowercharged";

        private bool powerOnly = true;
        private bool patchCharging = true;
        private bool noDurability = true;

        public CollectibleBehaviorPowerCharged(CollectibleObject collObj) : base(collObj) { }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            powerOnly     = properties["powerOnly"]?.AsBool(true) ?? true;
            patchCharging = properties["patchCharging"]?.AsBool(true) ?? true;
            noDurability  = properties["noDurability"]?.AsBool(true) ?? true;
        }

        /// <summary>The piece is a battery: condition == stored EU, not durability.</summary>
        public bool IsPowerOnly => powerOnly;

        /// <summary>VEChargerDurabilityRedirectPatch may take over the charger tick for this piece.</summary>
        public bool WantsPatchCharging => patchCharging;

        /// <summary>True if vanilla durability loss is cancelled for this piece.</summary>
        public bool CancelsDurability => noDurability;

        /// <summary>
        /// Cancel vanilla durability loss. The engine calls this whenever it
        /// wants to spend durability (wear, taking a hit, etc.). Setting
        /// <paramref name="amount"/> to 0 and signalling PreventDefault keeps a
        /// power-only piece from ever degrading - its only real fuel is EU.
        /// </summary>
        public override void OnDamageItem(IWorldAccessor world, Entity byEntity,
            ItemSlot itemslot, ref int amount, ref EnumHandling bhHandling)
        {
            if (!noDurability) return;
            amount = 0;
            bhHandling = EnumHandling.PreventDefault;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc,
            IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            if (!powerOnly || inSlot?.Itemstack == null) return;
            dsc.AppendLine(Lang.Get("vepowersuit:power-only-hint"));
        }
    }
}
