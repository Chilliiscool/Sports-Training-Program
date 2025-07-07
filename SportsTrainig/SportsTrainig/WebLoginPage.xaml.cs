using Microsoft.Maui.Controls;

namespace SportsTraining.Pages
{
    public partial class WebLoginPage : ContentPage
    {
        public WebLoginPage()
        {
            InitializeComponent();
        }

        private async void OnWebViewNavigated(object sender, WebNavigatedEventArgs e)
        {
            if (e.Url.Contains("/Application#/home"))
            {
                // user has logged in successfully
                await Navigation.PushAsync(new MainPage());
            }
        }
    }
}
