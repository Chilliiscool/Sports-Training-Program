using Microsoft.Maui.Controls;

namespace SportsTraining.Pages
{
    public partial class VideoPage : ContentPage
    {
        public string VideoUrl { get; }
        public string TitleText { get; }

        public VideoPage(string videoUrl, string title = "Video")
        {
            InitializeComponent();
            VideoUrl = videoUrl;
            TitleText = title;

            // Simple binding context
            BindingContext = new
            {
                VideoUrl = VideoUrl,
                Title = TitleText
            };
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try { Player?.Stop(); } catch { }
        }

        void OnPlayClicked(object sender, EventArgs e) { try { Player?.Play(); } catch { } }
        void OnPauseClicked(object sender, EventArgs e) { try { Player?.Pause(); } catch { } }
        void OnStopClicked(object sender, EventArgs e) { try { Player?.Stop(); } catch { } }
    }
}
