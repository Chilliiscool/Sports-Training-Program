using Microsoft.Maui;
using Microsoft.Maui.Controls;
using SportsTraining.Pages; 

namespace SportsTraining
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            MainPage = new NavigationPage(new WebLoginPage());

        }
    }
}
