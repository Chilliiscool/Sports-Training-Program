// Module Name: ProgramSession
// Author: Kye Franken 
// Date Created: 21 / 07 / 2025
// Date Modified: 06 / 08 / 2025
// Description: Represents a brief overview of a training session with title and URL.

using System;

namespace SportsTraining.Models
{
    public class ProgramSession
    {
        // Title of the training session
        public string SessionTitle { get; set; }

        // URL to access the session details
        public string Url { get; set; }
    }
}
