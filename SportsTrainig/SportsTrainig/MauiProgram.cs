// Module Name: MauiProgram
// Author: Kye Franken 
// Date Created: 19 / 06 / 2025
// Date Modified: 06 / 08 / 2025
// Description: Configures and builds the MAUI app including fonts and logging.

using System;
using Microsoft.Extensions.Logging;

namespace SportsTraining
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            // Set the main application class
            builder.UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    // Add custom fonts to the app
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            // Enable debug logging in debug builds
            builder.Logging.AddDebug();
#endif

            // Build and return the configured app
            return builder.Build();
        }
    }
}
