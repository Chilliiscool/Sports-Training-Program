using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace SportsTraining.Pages
{
    [QueryProperty(nameof(SessionUrl), "url")]
    public partial class SessionDetailPage : ContentPage
    {
        public string SessionUrl { get; set; }

        public SessionDetailPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            string cookie = Preferences.Get("VCP_Cookie", string.Empty);
            if (string.IsNullOrEmpty(cookie))
            {
                await DisplayAlert("Error", "Please log in again.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(SessionUrl))
            {
                await DisplayAlert("Error", "No session URL provided.", "OK");
                return;
            }

            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            await LoadSessionDetailsAsync(cookie, SessionUrl);

            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }

        private async Task LoadSessionDetailsAsync(string cookie, string url)
        {
            try
            {
                var html = await VisualCoachingService.GetRawSessionHtml(cookie, url);

                if (string.IsNullOrWhiteSpace(html))
                {
                    await DisplayAlert("Error", "No session content found.", "OK");
                    return;
                }

                var titleMatch = Regex.Match(html, @"<h1>(.*?)</h1>", RegexOptions.Singleline);
                if (titleMatch.Success)
                {
                    SessionTitleLabel.Text = System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
                }

                var paragraphMatches = Regex.Matches(html, @"<p.*?>(.*?)</p>", RegexOptions.Singleline);
                SessionContentStack.Children.Clear();

                foreach (Match pMatch in paragraphMatches)
                {
                    string text = Regex.Replace(pMatch.Groups[1].Value, "<.*?>", string.Empty);
                    text = System.Net.WebUtility.HtmlDecode(text).Trim();

                    if (!string.IsNullOrEmpty(text))
                    {
                        SessionContentStack.Children.Add(new Label
                        {
                            Text = text,
                            FontSize = 16,
                            TextColor = Microsoft.Maui.Graphics.Colors.Black,
                            HorizontalOptions = LayoutOptions.Center
                        });
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Error loading session details: {ex.Message}");
                await DisplayAlert("Error", "Failed to load session details.", "OK");
            }
        }
    }
}
