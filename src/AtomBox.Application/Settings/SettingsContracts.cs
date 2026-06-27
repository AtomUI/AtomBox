using AtomBox.Core.Settings;

namespace AtomBox.Application.Settings;

public sealed record GetApplicationSettingsRequest;

public sealed record UpdateApplicationSettingsRequest(ApplicationSettings Settings);

public sealed record ResetApplicationSettingsRequest;

public sealed record ApplicationSettingsResult(ApplicationSettings Settings);
