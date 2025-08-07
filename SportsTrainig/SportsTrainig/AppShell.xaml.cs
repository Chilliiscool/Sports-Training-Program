// Module Name: AppShell
// Author: Kye Franken 
// Date Created: 19 / 06 / 2025
// Date Modified: 06 / 08 / 2025
// Description: Defines the main application shell for navigation, registers routes, manages session validation on startup.

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

            // Register app navigation routes
            Routing.RegisterRoute(nameof(Pages.LoginPage), typeof(Pages.LoginPage));
            Routing.RegisterRoute(nameof(Pages.MainPage), typeof(Pages.MainPage));
            Routing.RegisterRoute(nameof(Pages.TrainingPage), typeof(Pages.TrainingPage));

            // Subscribe to navigated event for potential UI updates after navigation
            this.Navigated += AppShell_Navigated;
        }

        // Runs when the shell appears - checks login status and loads sessions
        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // If user not logged in, redirect to LoginPage
            if (!SessionManager.IsLoggedIn)
            {
                await Shell.Current.GoToAsync("//LoginPage");
                return;
            }

            string cookie = SessionManager.GetCookie();

            try
            {
                // Attempt to load sessions for today using saved cookie
                var sessions = await VisualCoachingService.GetSessionsForDate(cookie, DateTime.Today.ToString("yyyy-MM-dd"));
                Debug.WriteLine($"Loaded {sessions.Count} sessions on startup.");
            }
            catch (UnauthorizedAccessException)
            {
                // Cookie expired or unauthorized - clear and redirect to login
                SessionManager.ClearCookie();
                await Shell.Current.GoToAsync("//LoginPage");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading sessions in AppShell OnAppearing: {ex.Message}");
            }
        }

        // Optional event handler after navigation occurs
        private void AppShell_Navigated(object sender, ShellNavigatedEventArgs e)
        {
            // Implement UI updates on navigation if needed (e.g., update title bar)
        }
    }
}
