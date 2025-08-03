using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SportsTraining
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(Pages.LoginPage), typeof(Pages.LoginPage));
            Routing.RegisterRoute(nameof(Pages.MainPage), typeof(Pages.MainPage));
            Routing.RegisterRoute(nameof(Pages.TrainingPage), typeof(Pages.TrainingPage));

            this.Navigated += AppShell_Navigated;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (!SessionManager.IsLoggedIn)
            {
                await Shell.Current.GoToAsync("//LoginPage");
                return;
            }

            string cookie = SessionManager.GetCookie();

            try
            {
                var sessions = await VisualCoachingService.GetSessionsForDate(cookie, DateTime.Today.ToString("yyyy-MM-dd"));
                Debug.WriteLine($"Loaded {sessions.Count} sessions on startup.");
            }
            catch (UnauthorizedAccessException)
            {
                // Session expired
                SessionManager.ClearCookie();
                await Shell.Current.GoToAsync("//LoginPage");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading sessions in AppShell OnAppearing: {ex.Message}");
            }
        }

        private void AppShell_Navigated(object sender, ShellNavigatedEventArgs e)
        {
            // Optional UI updates on navigation (title bar, etc.)
        }
    }
}
