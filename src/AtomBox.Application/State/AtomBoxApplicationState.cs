using AtomUI.City.State;

namespace AtomBox.Application.State;

public sealed class AtomBoxApplicationState : IAtomBoxApplicationState
{
    private const string OwnerModule = "AtomBox.Application";
    private static readonly StateKey<AccountCatalogState> AccountsKey = new(
        "AtomBox.Application.AccountCatalog");
    private static readonly StateKey<ApplicationSettingsState> SettingsKey = new(
        "AtomBox.Application.Settings");

    private readonly IApplicationStateWriter _writer;

    public AtomBoxApplicationState(
        IStateRegistry registry,
        IApplicationState state,
        IApplicationStateWriterFactory writerFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(writerFactory);

        registry.Add(StateDefinition.Create(
            AccountsKey,
            AccountCatalogState.Uninitialized,
            StateLifetime.Application,
            StateAccessPolicy.OwnerWrite,
            StateSnapshotPolicy.Transient,
            ownerModule: OwnerModule));
        registry.Add(StateDefinition.Create(
            SettingsKey,
            ApplicationSettingsState.Uninitialized,
            StateLifetime.Application,
            StateAccessPolicy.OwnerWrite,
            StateSnapshotPolicy.Transient,
            ownerModule: OwnerModule));

        Accounts = state.Get(AccountsKey);
        Settings = state.Get(SettingsKey);
        _writer = writerFactory.CreateWriter(StateWriteAuthority.Module(OwnerModule));
    }

    public IReadOnlyState<AccountCatalogState> Accounts { get; }

    public IReadOnlyState<ApplicationSettingsState> Settings { get; }

    internal void PublishAccounts(AccountCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _writer.Set(AccountsKey, state);
    }

    internal void PublishSettings(ApplicationSettingsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _writer.Set(SettingsKey, state);
    }
}
