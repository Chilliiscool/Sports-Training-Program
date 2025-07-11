using System;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace SportsTraining.Pages
{
    public partial class TrainingPage : ContentPage, INotifyPropertyChanged
    {
        private int counter;

        public int Counter
        {
            get => counter;
            set
            {
                if (counter != value)
                {
                    counter = value;
                    OnPropertyChanged(nameof(Counter));
                }
            }
        }

        public ICommand IncrementCommand { get; }
        public ICommand ResetCommand { get; }

        public TrainingPage()
        {
            InitializeComponent();

            // Initialize commands
            IncrementCommand = new Command(() => Counter++);
            ResetCommand = new Command(() => Counter = 0);

            // Set BindingContext to self to enable bindings in XAML
            BindingContext = this;

            // Initialize counter
            Counter = 0;
        }

        public new event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
