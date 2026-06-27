namespace AtomBox.Core.Capabilities;

public sealed record StorageCapabilitySet(StorageCapability Value)
{
    public static StorageCapabilitySet Empty { get; } = new(StorageCapability.None);

    public bool Supports(StorageCapability capability)
    {
        return (Value & capability) == capability;
    }
}
