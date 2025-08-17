// Module Name: TrainingPage
// Author: Kye Franken
// Date Created: 20 / 06 / 2025
// Date Modified: 18 / 08 / 2025
// Description: Native rendering for BOTH program styles with Monday-start weeks.
//   • Table: original AM/PM planned matrix (blocks as columns, <p> rows).
//   • Exercises: weights view parsed from <div class="exercise"> blocks.
//   • View toggle logic keeps both working; Auto prefers Exercises when present.
//   • Images use an in-page lightbox overlay; videos launch VideoPopup (downloads with cookie).


namespace SportsTraining.Pages
{
    internal class ToolkitMediaElement : View
    {
        public string Source { get; set; }
        public bool ShouldAutoPlay { get; set; }
        public bool ShouldShowPlaybackControls { get; set; }
        public Aspect Aspect { get; set; }
        public LayoutOptions HorizontalOptions { get; set; }
        public LayoutOptions VerticalOptions { get; set; }
    }
}