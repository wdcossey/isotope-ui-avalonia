using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Vendor.Controls;

/// <summary>
/// A templated control: a card with a <see cref="Header"/>, a content slot and an <see cref="Accent"/> stripe down its
/// left side. <see cref="Dismiss"/> (the card's close button) raises <see cref="Dismissed"/>. Its template is the
/// control's default, so it needs no theme of its own.
/// </summary>
public class CalloutCard : ContentControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<CalloutCard, string?>(nameof(Header));

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<CalloutCard, IBrush?>(nameof(Accent));

    /// <summary>A routed event, as Avalonia's own controls raise theirs (Button.Click).</summary>
    public static readonly RoutedEvent<RoutedEventArgs> DismissedEvent =
        RoutedEvent.Register<CalloutCard, RoutedEventArgs>(nameof(Dismissed), RoutingStrategies.Bubble);

    static CalloutCard()
    {
        TemplateProperty.OverrideDefaultValue<CalloutCard>(new FuncControlTemplate<CalloutCard>((card, scope) =>
        {
            var header = new TextBlock { Name = "PART_Header", FontWeight = FontWeight.SemiBold };
            header[!TextBlock.TextProperty] = card[!HeaderProperty];
            var close = new Button { Name = "PART_Dismiss", Content = "x", HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (_, _) => card.Dismiss();
            var presenter = new ContentPresenter { Name = "PART_ContentPresenter" };
            presenter[!ContentPresenter.ContentProperty] = card[!ContentProperty];
            presenter[!ContentPresenter.ContentTemplateProperty] = card[!ContentTemplateProperty];
            var stripe = new Border { Width = 4 };
            stripe[!Border.BackgroundProperty] = card[!AccentProperty];
            var top = new DockPanel { Children = { close, header } };
            DockPanel.SetDock(close, Dock.Right);
            var body = new DockPanel { Children = { top, presenter } };
            DockPanel.SetDock(top, Dock.Top);
            var row = new DockPanel { Children = { stripe, body } };
            DockPanel.SetDock(stripe, Dock.Left);
            var frame = new Border { Name = "PART_Frame", Child = row, Padding = new Thickness(8) };
            frame[!Border.BackgroundProperty] = card[!BackgroundProperty];
            frame[!Border.BorderBrushProperty] = card[!BorderBrushProperty];
            frame[!Border.BorderThicknessProperty] = card[!BorderThicknessProperty];
            scope.Register(header.Name, header);
            scope.Register(close.Name, close);
            scope.Register(presenter.Name, presenter);
            scope.Register(frame.Name, frame);
            return frame;
        }));
    }

    public string? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    /// <summary>Raised when the card's close button is pressed.</summary>
    public event EventHandler<RoutedEventArgs>? Dismissed
    {
        add => AddHandler(DismissedEvent, value);
        remove => RemoveHandler(DismissedEvent, value);
    }

    /// <summary>Presses the close button.</summary>
    public void Dismiss() => RaiseEvent(new RoutedEventArgs(DismissedEvent));
}
