using AtomBox.Core.Settings;
using AtomBox.Core.Fingerprints;

namespace AtomBox.Application.Settings;

public sealed record GetApplicationSettingsRequest;

public sealed record UpdateApplicationSettingsRequest(ApplicationSettings Settings);

public sealed record ResetApplicationSettingsRequest;

public sealed record ApplicationSettingsResult(ApplicationSettings Settings);

public sealed record GetUploadFingerprintIndexStatisticsRequest;

public sealed record ClearUploadFingerprintIndexRequest;

public sealed record UploadFingerprintIndexStatisticsResult(FileFingerprintIndexStatistics Statistics);
