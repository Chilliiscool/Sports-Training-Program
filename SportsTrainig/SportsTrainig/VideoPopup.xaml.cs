using CommunityToolkit.Maui.Views;   // Popup
using CommunityToolkit.Maui.Media;   // MediaElement, MediaSource
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

            // Optional logging
            Player.MediaOpened += (_, __) => System.Diagnostics.Debug.WriteLine("[Video] Opened");
            Player.StateChanged += (_, __) => System.Diagnostics.Debug.WriteLine("[Video] State=" + Player.CurrentState);
            Player.MediaFailed += (_, e) => System.Diagnostics.Debug.WriteLine("[Video] Failed: " + e?.ErrorMessage);

            if (!string.IsNullOrWhiteSpace(sourcePathOrUrl) && System.IO.File.Exists(sourcePathOrUrl))
                Player.Source = MediaSource.FromFile(sourcePathOrUrl);
            else
                Player.Source = MediaSource.FromUri(sourcePathOrUrl);

            this.Closed += (_, __) => CleanupPlayer();
        }

        void CleanupPlayer()
        {
            try
            {
                // Detach events first so late callbacks don't hit managed code
                try { Player.MediaOpened -= (_, __) => { }; } catch { }
                try { Player.StateChanged -= (_, __) => { }; } catch { }
                try { Player.MediaFailed -= (_, __) => { }; } catch { }

                // Disconnect native handler BEFORE touching playback state
                try { Player?.Handler?.DisconnectHandler(); } catch { }

                // Now clear the source; avoid Stop() to dodge Media3 discontinuity callback
                try { Player.Source = null; } catch { }
            }
            catch { /* swallow during teardown */ }
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            CleanupPlayer();
            await CloseAsync();
        }
    }
}
