using Isotope.UI.Core;

namespace Isotope.UI;

// The Command factories (#29), as part of our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs:
// upstream's names and parameters, returning the same records. A button's command is Avalonia's ICommand
// (CommandBindings); a menu item's is copied into its data, as upstream's.

public static partial class Factories
{
    /// <summary>
    /// A push button driven by a command: its label is the command's, a click runs it, and it is disabled while the
    /// command can't run. <c>var save = ctx.UseCommand(new Command { Label = "Save", ExecuteAsync = SaveAsync });
    /// Button(save)</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    public static ButtonElement Button(Core.Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new(command.Label) { Command = command };
    }

    /// <summary>A hyperlink button driven by a command.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    public static HyperlinkButtonElement HyperlinkButton(Core.Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new(command.Label) { Command = command };
    }

    /// <summary>A repeat button driven by a command: each repeat runs it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    public static RepeatButtonElement RepeatButton(Core.Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new(command.Label) { Command = command };
    }

    /// <summary>
    /// A toggle button driven by a command: each toggle (on and off) runs it. <paramref name="isChecked"/> is written
    /// when it changes.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    public static ToggleButtonElement ToggleButton(Core.Command command, bool isChecked = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new(command.Label, isChecked) { Command = command };
    }

    /// <summary>
    /// A menu item driven by a command: its text, action, enabled state, shortcut and description are the command's.
    /// Unlike upstream's, which takes only <see cref="Core.Command.Execute"/>, an <see cref="Core.Command.ExecuteAsync"/>
    /// runs too, as on a button.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    public static MenuFlyoutItemData MenuItem(Core.Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new(command.Label, CommandBindings.Invokable(command) is null ? null : () => CommandBindings.Invoke(command))
        {
            IsEnabled = command.IsEnabled,
            KeyboardAccelerators = command.Accelerator is { } accelerator ? [accelerator] : null,
            Description = command.Description,
        };
    }

    /// <summary>A menu item driven by a parameterized command, run with <paramref name="parameter"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    public static MenuFlyoutItemData MenuItem<T>(Core.Command<T> command, T parameter)
    {
        ArgumentNullException.ThrowIfNull(command);
        Action? run = command.Execute is { } execute ? () => execute(parameter)
            : command.ExecuteAsync is { } executeAsync ? () => _ = executeAsync(parameter)
            : null;
        return new(command.Label, run)
        {
            IsEnabled = command.IsEnabled,
            KeyboardAccelerators = command.Accelerator is { } accelerator ? [accelerator] : null,
            Description = command.Description,
        };
    }
}
