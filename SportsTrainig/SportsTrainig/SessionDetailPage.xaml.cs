// Module Name: SessionDetailPage
// Author: Kye Franken 
// Date Created: 28 / 07 / 2025
// Date Modified: 06 / 08 / 2025
// Description: Loads and displays a training session's title and paragraph content from raw HTML, parsed from a session URL.

using Microsoft.Maui.Controls;
using SportsTraining.Services;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SportsTraining.Pages
{
    // Enables passing a session URL as a query parameter to this page
    [QueryProperty(nameof(SessionUrl), "url")]
    public partial class SessionDetailPage : ContentPage
    {
        // The URL of the session to load
        public string SessionUrl { get; set; }

        public SessionDetailPage()
        {
            InitializeComponent(); // Load XAML content
        }

        // Lifecycle method: runs each time the page appears
        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Get the saved cookie from the session manager
            string cookie = SessionManager.GetCookie();
            if (string.IsNullOrEmpty(cookie))
            {
                await DisplayAlert("Error", "Please log in again.", "OK");
                await Shell.Current.GoToAsync("//LoginPage");
                return;
            }

            // Ensure the session URL is passed in
            if (string.IsNullOrEmpty(SessionUrl))
            {
                await DisplayAlert("Error", "No session URL provided.", "OK");
                return;
            }

            // Show loading spinner
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            // Load and display the session details
            await LoadSessionDetailsAsync(cookie, SessionUrl);

            // Hide loading spinner
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }

        // Loads and parses the HTML content of a session
        private async Task LoadSessionDetailsAsync(string cookie, string url)
        {
            try
            {
                var html = await VisualCoachingService.GetRawSessionHtml(cookie, url);

                // Handle empty or expired sessions
                if (string.IsNullOrWhiteSpace(html))
                {
                    if (string.IsNullOrEmpty(SessionManager.GetCookie()))
                    {
                        await DisplayAlert("Session Expired", "Your session has expired. Please log in again.", "OK");
                        await Shell.Current.GoToAsync("//LoginPage");
                        return;
                    }

                    await DisplayAlert("Error", "No session content found.", "OK");
                    return;
                }

                // Extract the session title from <h1> tags
                var titleMatch = Regex.Match(html, @"<h1>(.*?)</h1>", RegexOptions.Singleline);
                if (titleMatch.Success)
                {
                    SessionTitleLabel.Text = System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
                }

                // Find all paragraph tags and display them as labels
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
