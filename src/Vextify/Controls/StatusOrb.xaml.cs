using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace Vextify.Controls;

public enum OrbState { Off, Active, Working, Error }

/// <summary>
/// Vextify's signature element: one orb that says whether Spotify is modded. Color is never the only signal:
/// the glyph changes with the state and the page pairs it with a text headline.
/// While active it moves very quietly (float, breathing glow, a slow pulse ring); the check pops in when it appears.
/// All motion is transform/opacity only, runs only while the window is visible, and stops with Windows' "Show animations" off.
/// </summary>
public partial class StatusOrb : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(OrbState), typeof(StatusOrb),
        new PropertyMetadata(OrbState.Off, (d, _) => ((StatusOrb)d).Apply()));

    public OrbState State
    {
        get => (OrbState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private static readonly Duration Fade = new(TimeSpan.FromMilliseconds(450));
    private OrbState _shown = OrbState.Off;

    public StatusOrb()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
        Unloaded += (_, _) => StopAmbient();
        // No animation clock while the window is hidden in the tray or minimized.
        IsVisibleChanged += (_, _) => { if (IsVisible) Apply(); else StopAmbient(); };
    }

    /// <summary>Respect Windows' "Show animations" setting.</summary>
    private static bool Animate => SystemParameters.ClientAreaAnimation;

    private void Apply()
    {
        if (!IsLoaded) return;
        var s = State;
        var changed = s != _shown;
        _shown = s;

        Crossfade(ActiveDisc, s is OrbState.Active or OrbState.Working ? 1 : 0);
        Crossfade(ErrorDisc, s == OrbState.Error ? 1 : 0);
        Crossfade(Glow, s switch { OrbState.Active => 0.55, OrbState.Working => 0.35, _ => 0.0 });

        Glyph.Symbol = s switch
        {
            OrbState.Active => SymbolRegular.Checkmark24,
            OrbState.Working => SymbolRegular.ArrowSync24,
            OrbState.Error => SymbolRegular.Warning24,
            _ => SymbolRegular.Power24,
        };
        Glyph.Opacity = s == OrbState.Off ? 0.6 : 1;
        Ring.Visibility = s == OrbState.Working ? Visibility.Visible : Visibility.Collapsed;

        AutomationProperties.SetName(this, s switch
        {
            OrbState.Active => "Status: Mods sind aktiv",
            OrbState.Working => "Status: Vextify arbeitet",
            OrbState.Error => "Status: Fehler",
            _ => "Status: Mods sind nicht aktiv",
        });

        if (s == OrbState.Active && IsVisible)
        {
            StartAmbient();
            if (changed) PopGlyph();
        }
        else StopAmbient();
    }

    private static void Crossfade(UIElement target, double to)
    {
        if (!Animate) { target.BeginAnimation(OpacityProperty, null); target.Opacity = to; return; }
        target.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Fade) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });
    }

    private void StartAmbient()
    {
        if (!Animate) return;

        // glow breathes
        var breathe = new DoubleAnimation(1.0, 1.08, TimeSpan.FromSeconds(3.4))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        GlowScale.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
        GlowScale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);

        // orb floats a few pixels
        Float.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-4, 4, TimeSpan.FromSeconds(4.2))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });

        // a ring leaves the orb and fades
        var grow = new DoubleAnimation(1.0, 1.42, TimeSpan.FromSeconds(3.8))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        RippleScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        RippleScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        Ripple.BeginAnimation(OpacityProperty, new DoubleAnimation(0.42, 0, TimeSpan.FromSeconds(3.8))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>The check scales in with a small overshoot when the orb turns active.</summary>
    private void PopGlyph()
    {
        if (!Animate) return;
        var pop = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(520))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 },
        };
        GlyphScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        GlyphScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void StopAmbient()
    {
        foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            GlowScale.BeginAnimation(prop, null);
            RippleScale.BeginAnimation(prop, null);
            GlyphScale.BeginAnimation(prop, null);
        }
        Float.BeginAnimation(TranslateTransform.YProperty, null);
        Ripple.BeginAnimation(OpacityProperty, null);
        GlowScale.ScaleX = GlowScale.ScaleY = 1;
        RippleScale.ScaleX = RippleScale.ScaleY = 1;
        GlyphScale.ScaleX = GlyphScale.ScaleY = 1;
        Float.Y = 0;
        Ripple.Opacity = 0;
    }
}
