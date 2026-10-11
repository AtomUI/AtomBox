using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Threading;
using CityLocalizationService = AtomUI.City.Localization.ILocalizationService;
using CityLocalizedText = AtomUI.City.Localization.ILocalizedText;
using CityLocalizedTextChangedEventArgs = AtomUI.City.Localization.LocalizedTextChangedEventArgs;

namespace AtomBox.Desktop.Localization;

public sealed class AtomBoxLocalizedTextProjection : IAtomBoxLocalizedText, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> EmptySnapshot =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    private readonly CityLocalizationService _city;
    private readonly object _gate = new();
    private readonly Dictionary<string, CityLocalizedText> _handles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pending = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string> _snapshot = EmptySnapshot;
    private CultureInfo _currentCulture;
    private long _revision;
    private long _pendingRevision = -1;
    private bool _initialized;
    private bool _disposed;

    public AtomBoxLocalizedTextProjection(CityLocalizationService city)
    {
        _city = city ?? throw new ArgumentNullException(nameof(city));
        _currentCulture = city.CurrentCulture;
        _revision = city.CultureRevision;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public CultureInfo CurrentCulture => _currentCulture;

    public long Revision => Interlocked.Read(ref _revision);

    public async ValueTask InitializeAsync(
        IEnumerable<string> resourceKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceKeys);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }
        }

        var handles = new List<CityLocalizedText>();
        try
        {
            foreach (var key in resourceKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                handles.Add(await _city.CreateTextAsync(key, cancellationToken).ConfigureAwait(false));
            }

            await RefreshToOneRevisionAsync(handles, cancellationToken).ConfigureAwait(false);
            var snapshot = CreateSnapshot(handles);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_initialized)
                {
                    return;
                }

                foreach (var handle in handles)
                {
                    _handles.Add(handle.Key, handle);
                    handle.Changed += HandleTextChanged;
                }

                _snapshot = snapshot;
                _currentCulture = _city.CurrentCulture;
                _revision = _city.CultureRevision;
                _initialized = true;
                handles.Clear();
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                handle.Dispose();
            }
        }
    }

    public string GetString(string catalog, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullKey = string.Concat(catalog, ".", key);
        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot.TryGetValue(fullKey, out var value)
            ? value
            : Missing(fullKey);
    }

    public string Format(string catalog, string key, params object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Format(CurrentCulture, GetString(catalog, key), arguments);
    }

    public void Dispose()
    {
        CityLocalizedText[] handles;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            handles = _handles.Values.ToArray();
            _handles.Clear();
            _pending.Clear();
        }

        foreach (var handle in handles)
        {
            handle.Changed -= HandleTextChanged;
            handle.Dispose();
        }
    }

    private async ValueTask RefreshToOneRevisionAsync(
        IReadOnlyList<CityLocalizedText> handles,
        CancellationToken cancellationToken)
    {
        while (handles.Count > 0)
        {
            var revision = _city.CultureRevision;
            if (handles.All(handle => handle.Revision == revision)
                && revision == _city.CultureRevision)
            {
                return;
            }

            foreach (var handle in handles)
            {
                await handle.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> CreateSnapshot(
        IEnumerable<CityLocalizedText> handles)
    {
        return new ReadOnlyDictionary<string, string>(
            handles.ToDictionary(
                handle => handle.Key,
                handle => handle.IsMissing ? Missing(handle.Key) : handle.Value,
                StringComparer.Ordinal));
    }

    private void HandleTextChanged(object? sender, CityLocalizedTextChangedEventArgs args)
    {
        if (sender is not CityLocalizedText handle)
        {
            return;
        }

        IReadOnlyDictionary<string, string>? nextSnapshot = null;
        CultureInfo? nextCulture = null;
        lock (_gate)
        {
            if (_disposed || !_initialized || args.Revision < _revision)
            {
                return;
            }

            if (_pendingRevision != args.Revision)
            {
                _pendingRevision = args.Revision;
                _pending.Clear();
            }

            _pending[handle.Key] = args.IsMissing ? Missing(handle.Key) : args.Value;
            if (_pending.Count == _handles.Count)
            {
                nextSnapshot = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(_pending, StringComparer.Ordinal));
                nextCulture = args.Culture;
                _pending.Clear();
            }
        }

        if (nextSnapshot is not null && nextCulture is not null)
        {
            Publish(nextSnapshot, nextCulture, args.Revision);
        }
    }

    private void Publish(
        IReadOnlyDictionary<string, string> snapshot,
        CultureInfo culture,
        long revision)
    {
        if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            PublishCore(snapshot, culture, revision);
            return;
        }

        Dispatcher.UIThread
            .InvokeAsync(() => PublishCore(snapshot, culture, revision))
            .GetAwaiter()
            .GetResult();
    }

    private void PublishCore(
        IReadOnlyDictionary<string, string> snapshot,
        CultureInfo culture,
        long revision)
    {
        lock (_gate)
        {
            if (_disposed || revision < _revision)
            {
                return;
            }

            Volatile.Write(ref _snapshot, snapshot);
            _currentCulture = culture;
            Interlocked.Exchange(ref _revision, revision);
        }

        OnPropertyChanged(nameof(CurrentCulture));
        OnPropertyChanged(nameof(Revision));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Missing(string key) => $"[[{key}]]";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
