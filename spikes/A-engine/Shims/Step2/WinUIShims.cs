// Spike A step 2: stand-ins for WinUI concepts that have no single Avalonia type to alias to.
// Declared in mirror namespaces because upstream names them fully qualified. Empty first; the
// build's missing-member errors list what each must provide. Public, because upstream exposes them in public hook signatures (see FINDINGS).
#pragma warning disable
namespace Microsoft.UI { public readonly partial struct WindowId { } }
namespace Microsoft.UI.Dispatching { public sealed partial class DispatcherQueue { } public enum DispatcherQueuePriority { Low, Normal, High } }
namespace Microsoft.UI.Input { public static partial class InputKeyboardSource { } }
namespace Microsoft.UI.Xaml
{
    public enum ElementTheme { Default, Light, Dark }
    public enum ApplicationTheme { Light, Dark }
    public enum FocusState { Unfocused, Pointer, Keyboard, Programmatic }
    public sealed partial class Application { }
    public delegate void RoutedEventHandler(object sender, Avalonia.Interactivity.RoutedEventArgs e);
    public sealed partial class WindowSizeChangedEventArgs { }
    public sealed partial class EffectiveViewportChangedEventArgs { }
    public partial interface IElementFactory { }
}
namespace Microsoft.UI.Xaml.Input { public sealed partial class LosingFocusEventArgs { } public sealed partial class KeyRoutedEventArgs { } }
namespace Microsoft.UI.Xaml.Media { public partial class SystemBackdrop { } public static partial class VisualTreeHelper { } }
namespace Microsoft.UI.Xaml.Automation { public static partial class AutomationProperties { } }
namespace Microsoft.UI.Xaml.Automation.Peers { public enum AutomationLiveSetting { Off, Polite, Assertive } public enum AccessibilityView { Raw, Control, Content } }
namespace Microsoft.UI.Xaml.Controls { public partial class ItemsRepeater : Avalonia.Controls.Panel { } public partial class XamlControlsResources { } }
namespace Windows.Foundation { public delegate void TypedEventHandler<TSender, TResult>(TSender sender, TResult args); }
namespace Windows.UI.ViewManagement { public sealed partial class UISettings { } public sealed partial class AccessibilitySettings { } }
namespace Windows.UI.Core { public enum CoreVirtualKeyStates { None = 0, Down = 1, Locked = 2 } }
namespace Windows.System { public enum VirtualKey { None = 0, Left = 37, Menu = 18, GoBack = 166 } }
namespace Microsoft.Windows.Storage.Pickers
{
    public sealed partial class PickFileResult { } public sealed partial class PickFolderResult { }
    public enum PickerLocationId { DocumentsLibrary, ComputerFolder, Desktop, Downloads, HomeGroup, MusicLibrary, PicturesLibrary, VideosLibrary, Objects3D, Unspecified }
    public enum PickerViewMode { List, Thumbnail }
    public sealed partial class FileOpenPicker { } public sealed partial class FolderPicker { } public sealed partial class FileSavePicker { }
}
