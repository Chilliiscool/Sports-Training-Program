using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;                 // base Toolkit
using static CommunityToolkit.Maui.Views.MediaElement;    // MediaElement

namespace SportsTraining
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()                 // base Toolkit
                .UseMauiCommunityToolkitMediaElement()     // MediaElement renderer
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif
            return builder.Build();
        }
    }
}
