// Module Name: App
// Author: Kye Franken 
// Date Created: 19 / 06 / 2025
// Date Modified: 06 / 08 / 2025
// Description: Initializes the app, applies user theme preferences, and sets the main page Shell.

using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace SportsTraining
{
    public partial class App : Application
    {
        // Key for storing the user's preferred theme in app preferences
        const string ThemeKey = "UserPreferredTheme";

        public App()
        {
            InitializeComponent();

            // Load saved theme preference and apply app-wide
            string savedTheme = Preferences.Get(ThemeKey, "Light");
            Application.Current.UserAppTheme = savedTheme == "Dark" ? AppTheme.Dark : AppTheme.Light;

            // Set main page to the Shell which manages app navigation
            MainPage = new AppShell();
        }

        // Checks login status based on stored cookie and navigates accordingly
        private async void CheckLoginStatus()
        {
            string cookie = Preferences.Get("VCP_Cookie", null);

            if (!string.IsNullOrEmpty(cookie))
            {
                await Shell.Current.GoToAsync("//MainPage");
            }
            else
            {
                await Shell.Current.GoToAsync("//LoginPage");
            }
        }
    }
}
