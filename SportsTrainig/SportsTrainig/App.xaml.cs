using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace SportsTraining
{
    public partial class App : Application
    {
        const string ThemeKey = "UserPreferredTheme";

        public App()
        {
            InitializeComponent();

            // Load saved theme preference
            string savedTheme = Preferences.Get(ThemeKey, "Light");
            Application.Current.UserAppTheme = savedTheme == "Dark" ? AppTheme.Dark : AppTheme.Light;

            MainPage = new AppShell(); // or your starting page
        }
    }
}