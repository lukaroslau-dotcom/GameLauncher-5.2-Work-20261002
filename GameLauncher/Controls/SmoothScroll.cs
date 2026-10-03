using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
namespace GameLauncher.Controls;

public static class SmoothScroll
{
    public static bool ReducedMotion { get; set; }
    public static readonly DependencyProperty PositionProperty = DependencyProperty.RegisterAttached("Position", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0d, (d,e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));
    private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached("Target", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0d));
    public static void Attach(Window window) => window.PreviewMouseWheel += Wheel;
    private static void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        DependencyObject? node = e.OriginalSource as DependencyObject;
        ScrollViewer? scroll = null;
        while (node != null)
        {
            if (node is System.Windows.Controls.Primitives.TextBoxBase or Slider or System.Windows.Controls.ComboBox) return;
            if (node is ScrollViewer candidate && candidate.ScrollableHeight > 0 &&
                (e.Delta < 0 ? candidate.VerticalOffset < candidate.ScrollableHeight : candidate.VerticalOffset > 0)) { scroll = candidate; break; }
            node = node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        if (scroll == null) return;
        // The card panel uses pixel offsets; ordinary text/list controls retain native scrolling.
        bool cards = scroll.CanContentScroll && FindPanel(scroll);
        if (scroll.CanContentScroll && !cards) return;
        double current = scroll.VerticalOffset;
        double previous = (double)scroll.GetValue(TargetProperty);
        double origin = Math.Abs((double)scroll.GetValue(PositionProperty)-current) < 1 ? previous : current;
        double target = Math.Clamp(origin - e.Delta * .9, 0, scroll.ScrollableHeight);
        scroll.BeginAnimation(PositionProperty, null);
        scroll.SetValue(PositionProperty, current);
        scroll.SetValue(TargetProperty, target);
        if (ReducedMotion || !SystemParameters.ClientAreaAnimation) scroll.SetValue(PositionProperty, target);
        else scroll.BeginAnimation(PositionProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        e.Handled = true;
    }
    private static bool FindPanel(DependencyObject root)
    {
        if (root is VirtualizingGamePanel) return true;
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(root); i++) if (FindPanel(VisualTreeHelper.GetChild(root,i))) return true;
        return false;
    }
}
