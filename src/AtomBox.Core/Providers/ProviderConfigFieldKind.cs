namespace AtomBox.Core.Providers;

public enum ProviderConfigFieldKind
{
    Text = 0,
    Number = 1,
    Boolean = 2,
    SecretRef = 3,
    Endpoint = 4,
    Region = 5,
    Bucket = 6
}
