using Android.Content;
using Android.Views;
using Android.Widget;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.UI;
using Microsoft.Maui.Handlers;
using SportsTraining.Controls;
using AView = Android.Views.View;

namespace SportsTraining.Platforms.Android
{
    class VideoPlayerPlatformView : FrameLayout
    {
        public PlayerView PlayerView { get; }
        public IExoPlayer? Player { get; private set; }

        public VideoPlayerPlatformView(Context context) : base(context)
        {
            SetBackgroundColor(Android.Graphics.Color.Black);
            PlayerView = new PlayerView(context)
            {
                UseController = true,
                LayoutParameters = new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent)
            };
            AddView(PlayerView);
        }

        public void Init(bool showControls, bool autoPlay)
        {
            Release();
            Player = new ExoPlayer.Builder(Context).Build();
            PlayerView.UseController = showControls;
            PlayerView.Player = Player;
            Player.PlayWhenReady = autoPlay;
        }

        public void SetSource(string? url, bool autoPlay)
        {
            if (Player == null) return;
            if (string.IsNullOrWhiteSpace(url)) { Player.ClearMediaItems(); return; }
            var item = MediaItem.FromUri(url);
            Player.SetMediaItem(item);
            Player.Prepare();
            Player.PlayWhenReady = autoPlay;
        }

        public void Release()
        {
            try { PlayerView.Player = null; Player?.Release(); Player?.Dispose(); } catch { }
            Player = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Release();
            base.Dispose(disposing);
        }
    }

    public class VideoPlayerViewHandler : ViewHandler<VideoPlayerView, AView>
    {
        VideoPlayerPlatformView? _native;

        protected override AView CreatePlatformView()
        {
            _native = new VideoPlayerPlatformView(Context);
            return _native;
        }

        protected override void ConnectHandler(AView platformView)
        {
            base.ConnectHandler(platformView);
            if (_native == null || VirtualView == null) return;
            _native.Init(VirtualView.ShowControls, VirtualView.AutoPlay);
            UpdateSource();
        }

        protected override void DisconnectHandler(AView platformView)
        {
            _native?.Release();
            _native?.Dispose();
            _native = null;
            base.DisconnectHandler(platformView);
        }

        public static void MapSource(VideoPlayerViewHandler h, VideoPlayerView v, object? _) => h.UpdateSource();
        public static void MapAutoPlay(VideoPlayerViewHandler h, VideoPlayerView v, object? _) => h._native?.SetSource(v.Source, v.AutoPlay);
        public static void MapShowControls(VideoPlayerViewHandler h, VideoPlayerView v, object? _)
        { if (h._native?.PlayerView != null) h._native.PlayerView.UseController = v.ShowControls; }

        void UpdateSource() { if (_native != null && VirtualView != null) _native.SetSource(VirtualView.Source, VirtualView.AutoPlay); }
    }
}
