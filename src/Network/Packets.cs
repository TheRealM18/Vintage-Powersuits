using ProtoBuf;

namespace VEPowersuit.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ToggleModulePacket
    {
        public string ModuleCode;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ToggleFlightPacket
    {
        public bool WantFlying;
    }

    /// <summary>Server -> client, so the HUD can show live energy without reading the slot every frame.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class EnergySyncPacket
    {
        public int Energy;
        public int MaxEnergy;
        public bool Flying;
    }
}
