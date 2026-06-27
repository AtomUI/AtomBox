namespace AtomBox.Transfer.Policies;

public sealed record TransferRetryPolicy(int MaxRetryCount = 3);
