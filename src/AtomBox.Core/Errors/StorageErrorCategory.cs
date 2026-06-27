namespace AtomBox.Core.Errors;

public enum StorageErrorCategory
{
    Unknown = 0,
    Validation = 1,
    Authentication = 2,
    Authorization = 3,
    NotFound = 4,
    Conflict = 5,
    Network = 6,
    Provider = 7,
    Infrastructure = 8,
    Canceled = 9
}
