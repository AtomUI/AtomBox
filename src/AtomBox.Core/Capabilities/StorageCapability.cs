namespace AtomBox.Core.Capabilities;

[Flags]
public enum StorageCapability
{
    None = 0,
    List = 1 << 0,
    Upload = 1 << 1,
    Download = 1 << 2,
    Delete = 1 << 3,
    CreateFolder = 1 << 4,
    Rename = 1 << 5,
    Search = 1 << 6,
    Share = 1 << 7,
    MultipartUpload = 1 << 8,
    Move = 1 << 9
}
