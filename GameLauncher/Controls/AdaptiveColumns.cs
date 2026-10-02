using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;
using System.Windows;
using System.Windows.Controls;
namespace GameLauncher.Controls;
/// <summary>Two columns that become one readable, scrollable column below the breakpoint.</summary>
public sealed class AdaptiveColumns : Panel
{
    public static readonly DependencyProperty BreakpointProperty = DependencyProperty.Register(nameof(Breakpoint), typeof(double), typeof(AdaptiveColumns), new FrameworkPropertyMetadata(820d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty FirstRatioProperty = DependencyProperty.Register(nameof(FirstRatio), typeof(double), typeof(AdaptiveColumns), new FrameworkPropertyMetadata(.5d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double Breakpoint { get => (double)GetValue(BreakpointProperty); set => SetValue(BreakpointProperty, value); }
    public double FirstRatio { get => (double)GetValue(FirstRatioProperty); set => SetValue(FirstRatioProperty, value); }
    public bool SecondFirstWhenStacked { get; set; }
    private const double Gap = 20;
    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? Breakpoint : available.Width;
        bool wide = width >= Breakpoint;
        double height = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            double childWidth = wide ? Math.Max(0, width - Gap) * (i == 0 ? FirstRatio : 1 - FirstRatio) : width;
            InternalChildren[i].Measure(new Size(childWidth, double.PositiveInfinity));
            height = wide ? Math.Max(height, InternalChildren[i].DesiredSize.Height) : height + InternalChildren[i].DesiredSize.Height + (i == 0 ? 0 : Gap);
        }
        return new Size(width, height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        bool wide = final.Width >= Breakpoint;
        double first = Math.Max(0, final.Width - Gap) * FirstRatio, y = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int index = !wide && SecondFirstWhenStacked && InternalChildren.Count == 2 ? 1 - i : i;
            var child = InternalChildren[index];
            System.Windows.Input.KeyboardNavigation.SetTabIndex(child,i);
            child.Arrange(wide ? new Rect(i == 0 ? 0 : first + Gap, 0, i == 0 ? first : Math.Max(0, final.Width-first-Gap), final.Height) : new Rect(0, y, final.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height + Gap;
        }
        return final;
    }
}
