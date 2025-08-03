using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using System;
using System.ComponentModel;
using System.Net;

namespace SportsTraining.Pages
{

    [QueryProperty(nameof(Url), "url")]
    public partial class TrainingPage : ContentPage, INotifyPropertyChanged
    {
        private string _url;

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
            InitializeComponent();
        }

        private async void LoadSessionDetails()
        {
            try
            {
                string cookie = Preferences.Get("VCP_Cookie", "");
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

                // Fetch raw HTML content of the session page
                string htmlContent = await VisualCoachingService.GetRawSessionHtml(cookie, _url);

                if (string.IsNullOrWhiteSpace(htmlContent))
                {
                    // Possibly unauthorized or session expired
                    await DisplayAlert("Session Expired", "Your session has expired. Please log in again.", "OK");
                    Preferences.Remove("VCP_Cookie");
                    await Shell.Current.GoToAsync("//LoginPage");
                    return;
                }

                // Set title (you can parse HTML for a better title if desired)
                TitleLabel.Text = "Training Session";

                // Load the HTML into the WebView
                SessionWebView.Source = new HtmlWebViewSource { Html = htmlContent };
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load session: {ex.Message}", "OK");
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            string savedCompany = Preferences.Get("SelectedCompany", "Normal");
            LogoImage.IsVisible = savedCompany == "ETPA";
        }
    }
}
