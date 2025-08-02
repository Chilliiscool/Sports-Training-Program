using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Pages;
using System;

namespace SportsTraining
{
    public partial class AppShell : Shell
    {
        private HorizontalStackLayout shellTitleView;
        private Label titleLabel;
        private Image logoImage;

        public AppShell()
        {
            InitializeComponent();

            // Register routes for navigation
            Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));
            Routing.RegisterRoute(nameof(MainPage), typeof(MainPage));
            Routing.RegisterRoute(nameof(TrainingPage), typeof(TrainingPage)); // <-- Added route registration

            // Find named controls from XAML
            shellTitleView = this.FindByName<HorizontalStackLayout>("ShellTitleView");
            titleLabel = this.FindByName<Label>("TitleLabel");
            logoImage = this.FindByName<Image>("LogoImage");

            if (shellTitleView == null) throw new Exception("ShellTitleView not found!");
            if (titleLabel == null) throw new Exception("TitleLabel not found!");
            if (logoImage == null) throw new Exception("LogoImage not found!");

            // Hook navigation event
            this.Navigated += AppShell_Navigated;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Navigate to LoginPage as the app start root
            // Because LoginPage is a ShellContent, absolute routing works here
            await Shell.Current.GoToAsync("//LoginPage");
        }

        private void AppShell_Navigated(object sender, ShellNavigatedEventArgs e)
        {
            var currentRoute = Shell.Current.CurrentState.Location.ToString().ToLower();
            bool isLoginPage = currentRoute.Contains("loginpage");

            // Show/hide title view based on current page
            shellTitleView.IsVisible = !isLoginPage;

            if (!isLoginPage)
            {
                if (currentRoute.Contains("mainpage"))
                    titleLabel.Text = "Home";
                else if (currentRoute.Contains("trainingpage"))
                    titleLabel.Text = "Training";
                else if (currentRoute.Contains("progresspage"))
                    titleLabel.Text = "Progress";
                else if (currentRoute.Contains("settingspage"))
                    titleLabel.Text = "Settings";
                else
                    titleLabel.Text = "SportsTraining";

                string selectedCompany = Preferences.Get("SelectedCompany", "Normal");
                logoImage.IsVisible = selectedCompany == "ETPA";
            }
            else
            {
                shellTitleView.IsVisible = true;
                titleLabel.Text = "Login";
                logoImage.IsVisible = false;
            }
        }
    }
}
