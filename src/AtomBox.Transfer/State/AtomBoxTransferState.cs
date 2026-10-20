using AtomUI.City.State;

namespace AtomBox.Transfer.State;

public sealed class AtomBoxTransferState : IAtomBoxTransferState
{
    private const string OwnerModule = "AtomBox.Transfer";
    private static readonly StateKey<TransferQueueState> QueueKey = new(
        "AtomBox.Transfer.Queue");
    private static readonly StateKey<TransferRuntimeSnapshot> RuntimeKey = new(
        "AtomBox.Transfer.Runtime");

    private readonly IApplicationStateWriter _writer;

    public AtomBoxTransferState(
        IStateRegistry registry,
        IApplicationState state,
        IApplicationStateWriterFactory writerFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(writerFactory);

        registry.Add(StateDefinition.Create(
            QueueKey,
            TransferQueueState.Uninitialized,
            StateLifetime.Application,
            StateAccessPolicy.OwnerWrite,
            StateSnapshotPolicy.Transient,
            ownerModule: OwnerModule));

        registry.Add(StateDefinition.Create(
            RuntimeKey,
            TransferRuntimeSnapshot.Initial,
            StateLifetime.Application,
            StateAccessPolicy.OwnerWrite,
            StateSnapshotPolicy.Transient,
            ownerModule: OwnerModule));

        Queue = state.Get(QueueKey);
        Runtime = state.Get(RuntimeKey);
        _writer = writerFactory.CreateWriter(StateWriteAuthority.Module(OwnerModule));
    }

    public IReadOnlyState<TransferQueueState> Queue { get; }

    public IReadOnlyState<TransferRuntimeSnapshot> Runtime { get; }

    internal void Publish(TransferQueueState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _writer.Set(QueueKey, state);
    }

    internal void Update(Func<TransferQueueState, TransferQueueState> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);
        _writer.Update(QueueKey, updater);
    }

    internal void PublishRuntime(TransferRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _writer.Set(RuntimeKey, snapshot);
    }
}
