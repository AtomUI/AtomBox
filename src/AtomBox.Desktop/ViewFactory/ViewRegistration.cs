using Avalonia.Controls;

namespace AtomBox.Desktop.ViewFactory;

public sealed record ViewRegistration(Type ViewModelType, Func<object, Control> ViewFactory);
