// Module Name: SessionManager
// Author: Kye Franken 
// Date Created: 03 / 08 / 2025
// Date Modified: 06 / 08 / 2025
// Description: Handles saving, retrieving, and clearing the VisualCoaching login cookie using device preferences.

using Microsoft.Maui.Storage;

namespace SportsTraining.Services
{
    public static class SessionManager
    {
        private const string CookieKey = "VCP_Cookie";

        // Saves the login cookie to device storage
        public static void SaveCookie(string cookie)
        {
            Preferences.Set(CookieKey, cookie);
        }

        // Retrieves the saved login cookie, or null if not found
        public static string? GetCookie()
        {
            return Preferences.Get(CookieKey, null);
        }

        // Returns true if a cookie is stored (user is logged in)
        public static bool IsLoggedIn => !string.IsNullOrEmpty(GetCookie());

        // Clears the saved login cookie from device storage
        public static void ClearCookie()
        {
            Preferences.Remove(CookieKey);
        }
    }
}
