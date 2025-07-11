namespace SportsTraining
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            string selectedCompany = Preferences.Get("SelectedCompany", "Normal");
            LogoImage.IsVisible = selectedCompany == "ETPA";
        }
    }

}
