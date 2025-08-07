// Module Name: ProgressPage
// Author: Kye Franken 
// Date Created: 20 / 06 / 2025
// Date Modified: 06 / 08 / 2025
// Description: Displays the user's progress page and shows a logo if the selected company is "ETPA".

using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage; // Required for Preferences
using System;

namespace SportsTraining.Pages
{
    public partial class ProgressPage : ContentPage
    {
        public ProgressPage()
        {
            InitializeComponent();  // Loads the associated XAML layout
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Retrieve the saved company name from device preferences
            string savedCompany = Preferences.Get("SelectedCompany", "Normal");

            // Show the ETPA logo only if the selected company is "ETPA"
            LogoImage.IsVisible = savedCompany == "ETPA";
        }
    }
}
