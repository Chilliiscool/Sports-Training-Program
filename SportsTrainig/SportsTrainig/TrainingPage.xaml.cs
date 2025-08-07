// Module Name: TrainingPage
// Author: Kye Franken 
// Date Created: 20 / 06 / 2025
// Date Modified: 06 / 08 / 2025
// Description: Displays a training session in a WebView using a session URL and cookie for authentication. 
// Handles missing cookies, expired sessions, and shows company-specific branding.

using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using System;
using System.ComponentModel;
using System.Net;

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
                _url = value;
                LoadSessionDetails();
            }
        }

        public TrainingPage()
        {
            InitializeComponent(); // Loads the associated XAML layout
        }

        // Loads and displays the training session's HTML content
        private async void LoadSessionDetails()
        {
            try
            {
                // Retrieve saved login cookie
                string cookie = Preferences.Get("VCP_Cookie", "");
                if (string.IsNullOrEmpty(cookie))
                {
                    await DisplayAlert("Error", "No cookie found. Please login.", "OK");
                    await Shell.Current.GoToAsync("//LoginPage");
                    return;
                }

                // Ensure session URL was passed in
                if (string.IsNullOrEmpty(_url))
                {
                    await DisplayAlert("Error", "Session URL is missing.", "OK");
                    return;
                }

                // Fetch raw HTML content of the training session
                string htmlContent = await VisualCoachingService.GetRawSessionHtml(cookie, _url);

                // Handle expired or unauthorized session
                if (string.IsNullOrWhiteSpace(htmlContent))
                {
                    await DisplayAlert("Session Expired", "Your session has expired. Please log in again.", "OK");
                    Preferences.Remove("VCP_Cookie");
                    await Shell.Current.GoToAsync("//LoginPage");
                    return;
                }

                // Display session in the WebView
                TitleLabel.Text = "Training Session";
                SessionWebView.Source = new HtmlWebViewSource { Html = htmlContent };
            }
            catch (Exception ex)
            {
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
