using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Media;   // MediaElement, MediaSource
using CommunityToolkit.Maui.Views;   // Popup
using Microsoft.Maui.Controls;
using System;

namespace SportsTraining.Popups
{
    public partial class VideoPopup : Popup
    {
        public VideoPopup(string sourcePathOrUrl, string title = "Video")
        {
            InitializeComponent();

            TitleText.Text = string.IsNullOrWhiteSpace(title) ? "Video" : title;

            // Named handlers = easy to unsubscribe
            Player.MediaOpened += OnMediaOpened;
            Player.StateChanged += OnStateChanged;
            Player.MediaFailed += OnMediaFailed;

            if (!string.IsNullOrWhiteSpace(sourcePathOrUrl) && System.IO.File.Exists(sourcePathOrUrl))
                Player.Source = MediaSource.FromFile(sourcePathOrUrl);
            else
                Player.Source = MediaSource.FromUri(sourcePathOrUrl);

            this.Closed += (_, __) => CleanupPlayer();
        }

        void OnMediaOpened(object? s, EventArgs e) =>
            System.Diagnostics.Debug.WriteLine("[Video] Opened");

        void OnStateChanged(object? s, EventArgs e) =>
            System.Diagnostics.Debug.WriteLine("[Video] State=" + Player.CurrentState);

        void OnMediaFailed(object? s, MediaFailedEventArgs e) =>
            System.Diagnostics.Debug.WriteLine("[Video] Failed: " + e?.ErrorMessage);

        void CleanupPlayer()
        {
            try
            {
                // Detach named handlers
                try { Player.MediaOpened -= OnMediaOpened; } catch { }
                try { Player.StateChanged -= OnStateChanged; } catch { }
                try { Player.MediaFailed -= OnMediaFailed; } catch { }

                // Disconnect native handler BEFORE clearing source
                try { Player?.Handler?.DisconnectHandler(); } catch { }

                // Clear source to release resources
                try { Player.Source = null; } catch { }
            }
            catch { /* swallow during teardown */ }
        }

        private void OnCloseClicked(object sender, EventArgs e)
        {
            // If your toolkit version uses Close() instead, just swap it.
            CloseAsync();
        }
    }
}
