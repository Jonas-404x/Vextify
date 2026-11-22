using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Vextify;

/// <summary>
/// Eased mouse-wheel scrolling for every ScrollViewer in the window. A wheel notch moves a target offset and the view glides
/// toward it. The animation hooks into the render loop only while it is moving, so an idle window costs nothing.
/// Honors Windows' "Show animations" setting (falls back to the stock behavior when it is off).
/// </summary>
public static class SmoothScroll
{
    private sealed class State
    {
        public double Target;
        public bool Active;
        public DateTime Last;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, State> States = new();

    public static void Attach(UIElement root) => root.PreviewMouseWheel += OnWheel;

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || !SystemParameters.ClientAreaAnimation || Keyboard.Modifiers != ModifierKeys.None) return;

        var sv = FindScrollable(e.OriginalSource as DependencyObject);
        if (sv == null) return;
        e.Handled = true;

        var st = States.GetOrCreateValue(sv);
        if (!st.Active) st.Target = sv.VerticalOffset;
        st.Target = Math.Clamp(st.Target - e.Delta * 0.9, 0, sv.ScrollableHeight);
        if (st.Active) return;

        st.Active = true;
        st.Last = DateTime.UtcNow;

        void Tick(object? s, EventArgs a)
        {
            var now = DateTime.UtcNow;
            var dt = Math.Min((now - st.Last).TotalSeconds, 0.05);
            st.Last = now;

            var current = sv.VerticalOffset;
            var next = current + (st.Target - current) * (1 - Math.Exp(-dt / 0.085));
            if (Math.Abs(st.Target - next) < 0.4)
            {
                next = st.Target;
                CompositionTarget.Rendering -= Tick;
                st.Active = false;
            }
            sv.ScrollToVerticalOffset(next);
        }

        CompositionTarget.Rendering += Tick;
    }

    private static ScrollViewer? FindScrollable(DependencyObject? start)
    {
        for (var d = start; d != null; d = Parent(d))
            if (d is ScrollViewer sv && sv.ScrollableHeight > 0.5 && sv.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
                return sv;
        return null;
    }

    private static DependencyObject? Parent(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
}
