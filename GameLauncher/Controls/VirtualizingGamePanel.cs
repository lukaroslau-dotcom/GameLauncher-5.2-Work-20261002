using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Size=System.Windows.Size;
using Point=System.Windows.Point;
using Vector=System.Windows.Vector;
namespace GameLauncher.Controls;
/// <summary>Only creates the visible card rows plus one buffer row.</summary>
public sealed class VirtualizingGamePanel : VirtualizingPanel, IScrollInfo
{
    private double RowHeight=374;
    private Size viewport,extent;
    private double offset,cellWidth;
    private int columns=1;
    protected override Size MeasureOverride(Size available)
    {
        var owner=ItemsControl.GetItemsOwner(this);
        if(owner==null)return new Size();
        viewport=new Size(double.IsInfinity(available.Width)?800:available.Width,double.IsInfinity(available.Height)?600:available.Height);
        columns=Math.Max(1,(int)(viewport.Width/320));cellWidth=viewport.Width/columns;
        RowHeight=Math.Ceiling(Math.Max(0,cellWidth-32)*9/16)+198;
        extent=new Size(viewport.Width,Math.Ceiling(owner.Items.Count/(double)columns)*RowHeight);
        offset=Math.Clamp(offset,0,Math.Max(0,extent.Height-viewport.Height));ScrollOwner?.InvalidateScrollInfo();
        int first=Math.Max(0,((int)(offset/RowHeight)-1)*columns);
        int last=Math.Min(owner.Items.Count-1,((int)((offset+viewport.Height)/RowHeight)+2)*columns-1);
        // WPF can measure a virtualizing panel once before it attaches its
        // generator (for example directly after changing the ItemsSource).
        // Wait for the next layout pass instead of dereferencing it here.
        var generator=ItemContainerGenerator;
        if(generator is null)
        {
            Dispatcher.BeginInvoke(new Action(InvalidateMeasure));
            return viewport;
        }
        for(int i=InternalChildren.Count-1;i>=0;i--)
        {
            int index=generator.IndexFromGeneratorPosition(new GeneratorPosition(i,0));
            if(index<first||index>last){generator.Remove(new GeneratorPosition(i,0),1);RemoveInternalChildRange(i,1);}
        }
        if(last>=first)
        {
            var start=generator.GeneratorPositionFromIndex(first);int childIndex=start.Offset==0?start.Index:start.Index+1;
            using(generator.StartAt(start,GeneratorDirection.Forward,true))
                for(int index=first;index<=last;index++,childIndex++)
                {
                    var child=(UIElement)generator.GenerateNext(out bool created);
                    if(created){if(childIndex>=InternalChildren.Count)AddInternalChild(child);else InsertInternalChild(childIndex,child);generator.PrepareItemContainer(child);}
                    child.Measure(new Size(cellWidth,RowHeight));
                }
        }
        return viewport;
    }
    protected override Size ArrangeOverride(Size final)
    {
        for(int i=0;i<InternalChildren.Count;i++)
        {
            int index=ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i,0));
            InternalChildren[i].Arrange(new Rect(index%columns*cellWidth,index/columns*RowHeight-offset,cellWidth,RowHeight));
        }
        return final;
    }
    protected override void OnItemsChanged(object sender,ItemsChangedEventArgs args){RemoveInternalChildRange(0,InternalChildren.Count);ItemContainerGenerator.RemoveAll();offset=0;InvalidateMeasure();}
    public ScrollViewer? ScrollOwner{get;set;}
    public bool CanHorizontallyScroll{get;set;}
    public bool CanVerticallyScroll{get;set;}
    public double ExtentWidth=>extent.Width;public double ExtentHeight=>extent.Height;
    public double ViewportWidth=>viewport.Width;public double ViewportHeight=>viewport.Height;
    public double HorizontalOffset=>0;public double VerticalOffset=>offset;
    public void SetHorizontalOffset(double value){}
    public void SetVerticalOffset(double value){if(double.IsNaN(value))return;offset=Math.Clamp(value,0,Math.Max(0,extent.Height-viewport.Height));InvalidateMeasure();ScrollOwner?.InvalidateScrollInfo();}
    public void LineUp()=>SetVerticalOffset(offset-48);public void LineDown()=>SetVerticalOffset(offset+48);
    public void PageUp()=>SetVerticalOffset(offset-viewport.Height);public void PageDown()=>SetVerticalOffset(offset+viewport.Height);
    public void MouseWheelUp()=>SetVerticalOffset(offset-144);public void MouseWheelDown()=>SetVerticalOffset(offset+144);
    public void LineLeft(){}public void LineRight(){}public void PageLeft(){}public void PageRight(){}public void MouseWheelLeft(){}public void MouseWheelRight(){}
    public Rect MakeVisible(Visual visual,Rect rectangle)
    {
        var owner=ItemsControl.GetItemsOwner(this);var container=ItemsControl.ContainerFromElement(owner,visual);
        int index=owner.ItemContainerGenerator.IndexFromContainer(container);if(index<0)return rectangle;
        double top=index/columns*RowHeight;
        if(top<offset)SetVerticalOffset(top);else if(top+RowHeight>offset+viewport.Height)SetVerticalOffset(top+RowHeight-viewport.Height);
        return new Rect(0,Math.Max(0,top-offset),cellWidth,Math.Min(RowHeight,viewport.Height));
    }
}
