using Microsoft.Maui.Controls;
using System;

namespace SportsTraining.Pages
{
    public partial class ProgressPage : ContentPage
    {
        public ProgressPage()
        {
            InitializeComponent();  // This loads the XAML content
        }
        protected override void OnAppearing()
        {
            base.OnAppearing();

            string savedCompanie = Preferences.Get("SelectedCompany", "Normal");

            if (savedCompanie == "ETPA")
            {
                LogoImage.IsVisible = true;
            }
            else
            {
                LogoImage.IsVisible = false;
            }
        }

    }
}
