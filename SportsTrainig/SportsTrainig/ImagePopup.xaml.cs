using CommunityToolkit.Maui.Views;
using System;

namespace SportsTraining.Popups
{
    public partial class ImagePopup : Popup
    {
        public string ImageUrl { get; }
        public string Title { get; }

        public ImagePopup(string imageUrl, string title = "Image")
        {
            InitializeComponent();
            ImageUrl = imageUrl;
            Title = title;

            TitleLabel.Text = Title;
            PopupImage.Source = ImageUrl;
        }

        private void OnCloseClicked(object sender, EventArgs e)
        {
            Close();
        }

        private void Close()
        {
            throw new NotImplementedException();
        }
    }
}
