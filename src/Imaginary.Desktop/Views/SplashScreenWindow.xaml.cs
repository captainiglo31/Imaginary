using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace Imaginary.Desktop.Views;

public partial class SplashScreenWindow : Window
{
    public SplashScreenWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        MouseLeftButtonDown += (s, e) =>
        {
            try { DragMove(); } catch { }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Resources["FadeInStoryboard"] is Storyboard sb)
        {
            sb.Begin();
        }
    }

    public async Task FadeOutAndCloseAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        var fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(250)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) =>
        {
            tcs.TrySetResult(true);
            try { Close(); } catch { }
        };

        RootBorder.BeginAnimation(OpacityProperty, fadeOut);
        await tcs.Task;
    }
}
