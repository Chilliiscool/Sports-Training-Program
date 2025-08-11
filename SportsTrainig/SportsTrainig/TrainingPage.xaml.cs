// Module Name: TrainingPage
// Author: Kye Franken 
// Date Created: 20 / 06 / 2025
// Date Modified: 11 / 08 / 2025
// Description: Displays a training session in a WebView using a session URL and cookie for authentication.
// Forces the "first link" by coercing the query param i=0 before loading.

using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Linq;

namespace SportsTraining.Pages
{
    // Enables passing the session URL as a query parameter when navigating to this page
    [QueryProperty(nameof(Url), "url")]
    public partial class TrainingPage : ContentPage, INotifyPropertyChanged
    {
        private string _url;

        // Decodes and sets the URL passed into the page, then loads session data
        public string Url
        {
            get => WebUtility.UrlDecode(_url);
            set
            {
                // Always force the first link (i=0), regardless of what was passed in
                var decoded = WebUtility.UrlDecode(value);
                _url = ForceFirstIndex(decoded);
                LoadSessionDetails();
            }
        }

        public TrainingPage()
        {
            InitializeComponent(); // Loads the associated XAML layout
        }

        // Ensure query param i=0 (the "first link")
        private static string ForceFirstIndex(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            int q = raw.IndexOf('?');
            if (q < 0)
            {
                // no query yet — add i=0
                return raw + "?i=0";
            }

            var path = raw.Substring(0, q);
            var query = raw.Substring(q + 1);

            var parts = query
                .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            bool foundI = false;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].StartsWith("i=", StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = "i=0";
                    foundI = true;
                }
            }

            if (!foundI)
            {
                parts.Add("i=0");
            }

            // Optional: keep format/version stable so VC renders correctly
            if (!parts.Any(p => p.StartsWith("format=", StringComparison.OrdinalIgnoreCase)))
                parts.Add("format=Tablet");
            if (!parts.Any(p => p.StartsWith("version=", StringComparison.OrdinalIgnoreCase)))
                parts.Add("version=2");

            return path + "?" + string.Join("&", parts);
        }

        // Loads and displays the training session's HTML content
        private async void LoadSessionDetails()
        {
            Debug.WriteLine($"[TrainingPage] Loading session with URL: {_url}");
            Debug.WriteLine($"[TrainingPage] Using cookie: '{SessionManager.GetCookie()}'");

            try
            {
                string cookie = SessionManager.GetCookie() ?? "";

                if (string.IsNullOrEmpty(cookie))
                {
                    await DisplayAlert("Error", "No cookie found. Please login.", "OK");
                    await Shell.Current.GoToAsync("//LoginPage");
                    return;
                }

                if (string.IsNullOrEmpty(_url))
                {
                    await DisplayAlert("Error", "Session URL is missing.", "OK");
                    return;
                }

                string htmlContent = await VisualCoachingService.GetRawSessionHtml(cookie, _url);

                if (string.IsNullOrWhiteSpace(htmlContent))
                {
                    Debug.WriteLine("[TrainingPage] Empty or expired session HTML.");
                    await DisplayAlert("Session Expired", "Your session has expired. Please log in again.", "OK");
                    SessionManager.ClearCookie();
                    await Shell.Current.GoToAsync("//LoginPage");
                    return;
                }

                TitleLabel.Text = "Training Session";
                SessionWebView.Source = new HtmlWebViewSource { Html = htmlContent };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrainingPage] Error loading session: {ex.Message}");
                await DisplayAlert("Error", $"Failed to load session: {ex.Message}", "OK");
            }
        }

        // Shows a specific logo if the selected company is "ETPA"
        protected override void OnAppearing()
        {
            base.OnAppearing();

            string savedCompany = Preferences.Get("SelectedCompany", "Normal");
            LogoImage.IsVisible = savedCompany == "ETPA";
        }
    }
}
