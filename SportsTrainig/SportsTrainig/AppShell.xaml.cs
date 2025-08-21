// Module Name: AppShell
// Author: Kye Franken
// Date Created: 19 / 06 / 2025
// Date Modified: 15 / 08 / 2025
// Description: Shell + a flyout toggle ("Show/Hide Day/Date Panel") that pages listen to.
//              Uses Preferences key ShowDatesPanel (default: false = hidden).

using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using System;

namespace SportsTraining
{
    public partial class AppShell : Shell
    {
        private const string ShowDatesPrefKey = "ShowDatesPanel";
        private readonly MenuItem _toggleDatesItem;

        [Obsolete]
        public AppShell()
        {
            InitializeComponent();

            // Routes
            Routing.RegisterRoute(nameof(Pages.LoginPage), typeof(Pages.LoginPage));
            Routing.RegisterRoute(nameof(Pages.MainPage), typeof(Pages.MainPage));
            Routing.RegisterRoute(nameof(Pages.TrainingPage), typeof(Pages.TrainingPage));
            Routing.RegisterRoute(nameof(Pages.VideoPage), typeof(Pages.VideoPage));


            // Ensure default (hidden) unless user changed it
            bool show = Preferences.Get(ShowDatesPrefKey, false);

            // Add a Flyout menu toggle
            _toggleDatesItem = new MenuItem { Text = show ? "Hide Day/Date Panel" : "Show Day/Date Panel" };
            _toggleDatesItem.Clicked += (_, __) =>
            {
                bool current = Preferences.Get(ShowDatesPrefKey, true);
                bool next = !current;
                Preferences.Set(ShowDatesPrefKey, next);
                _toggleDatesItem.Text = next ? "Hide Day/Date Panel" : "Show Day/Date Panel";

                // Broadcast to interested pages (e.g., TrainingPage)
                MessagingCenter.Send(this, "ShowDatesPanelChanged", next);
            };

            // Put the toggle at the bottom of the flyout
            Items.Add(new MenuItem { Text = "-" }); // simple divider
            Items.Add(_toggleDatesItem);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (!SessionManager.IsLoggedIn)
            {
                await Shell.Current.GoToAsync("//LoginPage");
                return;
            }

            try
            {
                string cookie = SessionManager.GetCookie();
                _ = await VisualCoachingService.GetSessionsForDate(cookie, DateTime.Today.ToString("yyyy-MM-dd"));
            }
            catch (UnauthorizedAccessException)
            {
                SessionManager.ClearCookie();
                await Shell.Current.GoToAsync("//LoginPage");
            }
        }
    }
}
