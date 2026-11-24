using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Isotope.UI.Core.V1Protocol.Descriptor;

namespace Isotope.UI.Core;

/// <summary>
/// Binds a <see cref="Command"/> to the button family (#29), as upstream's <c>Core/CommandBindings.cs</c> (tier 3: it
/// writes WinUI's keyboard accelerators and access keys), mapped to Avalonia's <see cref="ICommand"/>: the button's
/// <c>Command</c> is an adapter whose <see cref="ICommand.CanExecute"/> is <see cref="Command.IsEnabled"/>, so Avalonia
/// disables the button itself, and whose <see cref="ICommand.Execute"/> runs the current render's command.
/// </summary>
/// <remarks>
/// As upstream's: an explicit callback (<c>OnClick</c>, a toggle callback) wins over the command's action; the
/// command's <see cref="Command.Description"/> is the tooltip and help text, and its
/// <see cref="Command.Accelerator"/> the keyboard shortcut (Avalonia's <c>HotKey</c>); the metadata is written when it
/// changes, never for a change of the delegates alone. Unlike upstream's: the access key isn't written (Avalonia's come
/// from an underscore in the content); the tooltip is written with the tooltip modifier, as one value whose modifier
/// wins (<c>Reconciler.ApplyModifiers</c>), so either can come and go; and only what the command wrote is cleared when
/// it goes.
/// </remarks>
internal static class CommandBindings
{
    /// <summary>Runs <see cref="Command.Execute"/>, or starts <see cref="Command.ExecuteAsync"/> without awaiting it.</summary>
    internal static void Invoke(Command cmd)
    {
        if (cmd.Execute is not null) cmd.Execute();
        else if (cmd.ExecuteAsync is not null) _ = cmd.ExecuteAsync();
    }

    /// <summary>A non-null delegate when <paramref name="cmd"/> has an action to run.</summary>
    internal static Delegate? Invokable(Command? cmd) =>
        cmd is null ? null : (Delegate?)cmd.Execute ?? cmd.ExecuteAsync;

    /// <summary>The callback a click runs: the explicit one, else the command's action.</summary>
    internal static Delegate? EffectiveCallback(Delegate? userCallback, Command? cmd) =>
        userCallback ?? Invokable(cmd);

    /// <summary>Whether two commands render the same: every member but the delegates (upstream's comparison).</summary>
    internal static bool CommandsEqual(Command? a, Command? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.Label == b.Label
            && a.IsEnabled == b.IsEnabled
            && a.Description == b.Description
            && a.AccessKey == b.AccessKey
            && Equals(a.Icon, b.Icon)
            && Equals(a.Accelerator, b.Accelerator);
    }

    /// <summary>The command a button-family element carries, or null.</summary>
    internal static Command? CommandOf(Element? element) => element switch
    {
        ButtonElement b => b.Command,
        ToggleButtonElement t => t.Command,
        RepeatButtonElement r => r.Command,
        HyperlinkButtonElement h => h.Command,
        _ => null,
    };

    /// <summary>Avalonia's gesture for an accelerator.</summary>
    internal static KeyGesture? Gesture(KeyboardAcceleratorData? accelerator) =>
        accelerator is null ? null : new KeyGesture(accelerator.Key, accelerator.Modifiers);

    /// <summary>
    /// The command entry of a button's descriptor: on mount, and on an update whose command renders differently, the
    /// control's <c>Command</c> adapter, its enabled state and the command's metadata. <paramref name="hasUserCallback"/>
    /// says whether the element's own callback takes the click instead of the command's action.
    /// </summary>
    internal static ControlDescriptor<TElement, TControl> OneWayCommand<TElement, TControl>(
        this ControlDescriptor<TElement, TControl> d,
        Func<TElement, Command?> getCommand,
        Func<TElement, bool> hasUserCallback)
        where TElement : Element
        where TControl : Button, new()
        => d.ImperativeBridged(
            mount: (_, c, e) => Apply(c, null, getCommand(e), getCommand, hasUserCallback),
            update: (_, c, o, n) =>
            {
                var oldCmd = getCommand(o);
                var newCmd = getCommand(n);
                if (!CommandsEqual(oldCmd, newCmd)) Apply(c, oldCmd, newCmd, getCommand, hasUserCallback);
            });

    private sealed class CommandOwner;

    // The adapter this binding set as a button's Command.
    private static readonly AttachedProperty<ElementCommand?> AdapterProperty =
        AvaloniaProperty.RegisterAttached<CommandOwner, Button, ElementCommand?>("IsotopeCommand");

    private static void Apply<TElement>(Button c, Command? oldCmd, Command? newCmd,
        Func<TElement, Command?> getCommand, Func<TElement, bool> hasUserCallback) where TElement : Element
    {
        if (newCmd is null)
        {
            if (c.GetValue(AdapterProperty) is { } gone && ReferenceEquals(c.Command, gone)) c.ClearValue(Button.CommandProperty);
            c.ClearValue(AdapterProperty);
            ClearMetadata(c, oldCmd);
            return;
        }
        if (c.GetValue(AdapterProperty) is not { } adapter)
        {
            adapter = new ElementCommand(c, e => e is TElement t ? getCommand(t) : null, e => e is TElement t && hasUserCallback(t));
            c.SetValue(AdapterProperty, adapter);
        }
        // Its enabled state first: Avalonia asks CanExecute when the command is set.
        adapter.SetCanExecute(newCmd.IsEnabled);
        if (!ReferenceEquals(c.Command, adapter)) c.Command = adapter;

        if (!Equals(oldCmd?.Accelerator, newCmd.Accelerator))
        {
            if (Gesture(newCmd.Accelerator) is { } gesture) c.HotKey = gesture;
            else if (Equals(c.HotKey, Gesture(oldCmd?.Accelerator))) c.ClearValue(Button.HotKeyProperty);
        }
        if (oldCmd?.Description != newCmd.Description)
        {
            if (newCmd.Description is { } description) AutomationProperties.SetHelpText(c, description);
            else ClearMetadata(c, oldCmd, acceleratorToo: false);
        }
    }

    // Clears only what the old command wrote and nothing else has replaced since.
    private static void ClearMetadata(Button c, Command? oldCmd, bool acceleratorToo = true)
    {
        if (oldCmd is null) return;
        if (oldCmd.Description is { } description && AutomationProperties.GetHelpText(c) == description)
            c.ClearValue(AutomationProperties.HelpTextProperty);
        if (acceleratorToo && Gesture(oldCmd.Accelerator) is { } gesture && Equals(c.HotKey, gesture))
            c.ClearValue(Button.HotKeyProperty);
    }

    /// <summary>
    /// The <see cref="ICommand"/> a button with a <see cref="Command"/> holds: enabled while the command is, running the
    /// command its control's current element carries unless the element's own callback takes the click.
    /// </summary>
    private sealed class ElementCommand(Button owner, Func<Element, Command?> getCommand, Func<Element, bool> hasUserCallback) : ICommand
    {
        private bool _canExecute = true;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute;

        public void Execute(object? parameter)
        {
            if (Reconciler.GetMountedElement(owner) is not { } live || hasUserCallback(live)) return;
            if (getCommand(live) is { } cmd) Invoke(cmd);
        }

        internal void SetCanExecute(bool value)
        {
            if (_canExecute == value) return;
            _canExecute = value;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
