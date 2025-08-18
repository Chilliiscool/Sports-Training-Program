using Microsoft.Maui.Controls;

namespace SportsTraining.Controls
{
    public class VideoPlayerView : View
    {
        public static readonly BindableProperty SourceProperty =
            BindableProperty.Create(nameof(Source), typeof(string), typeof(VideoPlayerView), default(string));

        public static readonly BindableProperty AutoPlayProperty =
            BindableProperty.Create(nameof(AutoPlay), typeof(bool), typeof(VideoPlayerView), true);

        public static readonly BindableProperty ShowControlsProperty =
            BindableProperty.Create(nameof(ShowControls), typeof(bool), typeof(VideoPlayerView), true);

        public string? Source { get => (string?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
        public bool AutoPlay { get => (bool)GetValue(AutoPlayProperty); set => SetValue(AutoPlayProperty, value); }
        public bool ShowControls { get => (bool)GetValue(ShowControlsProperty); set => SetValue(ShowControlsProperty, value); }
    }
}
