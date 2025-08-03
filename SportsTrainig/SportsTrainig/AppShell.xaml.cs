using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Pages;
using SportsTraining.Services;
using System;

namespace SportsTraining
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));
            Routing.RegisterRoute(nameof(MainPage), typeof(MainPage));
            Routing.RegisterRoute(nameof(TrainingPage), typeof(TrainingPage));

            this.Navigated += AppShell_Navigated;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            string cookie = SessionManager.GetCookie();

            if (string.IsNullOrEmpty(cookie))
            {
                await Shell.Current.GoToAsync("//LoginPage");
                return;
            }

            // Optional: preload today's sessions
            _ = VisualCoachingService.GetSessionsForDate(cookie, DateTime.Today.ToString("yyyy-MM-dd"));
        }

        private void AppShell_Navigated(object sender, ShellNavigatedEventArgs e)
        {
            var currentRoute = Shell.Current.CurrentState.Location.ToString().ToLower();
            bool isLoginPage = currentRoute.Contains("loginpage");

            ShellTitleView.IsVisible = !isLoginPage;

            if (!isLoginPage)
            {
                if (currentRoute.Contains("mainpage"))
                    TitleLabel.Text = "Home";
                else if (currentRoute.Contains("trainingpage"))
                    TitleLabel.Text = "Training";
                else if (currentRoute.Contains("progresspage"))
                    TitleLabel.Text = "Progress";
                else if (currentRoute.Contains("settingspage"))
                    TitleLabel.Text = "Settings";
                else
                    TitleLabel.Text = "SportsTraining";

                string selectedCompany = Preferences.Get("SelectedCompany", "Normal");
                LogoImage.IsVisible = selectedCompany == "ETPA";
            }
            else
            {
                ShellTitleView.IsVisible = true;
                TitleLabel.Text = "Login";
                LogoImage.IsVisible = false;
            }
        }
    }
}
