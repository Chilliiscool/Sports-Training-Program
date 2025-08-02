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
                    return;
                }

                if (string.IsNullOrEmpty(_url))
                {
                    await DisplayAlert("Error", "Session URL is missing.", "OK");
                    return;
                }

                var detail = await VisualCoachingService.GetSessionSummaryFromUrl(cookie, _url);

                TitleLabel.Text = detail?.SessionTitle ?? "Training Session";
                DetailLabel.Text = detail?.HtmlSummary ?? "No session content found.";
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
