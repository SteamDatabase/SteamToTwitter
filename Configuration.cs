using System;

namespace SteamToTwitter
{
    internal class Configuration
    {
#pragma warning disable 0649 // JSON
        public TinyTwitter.OAuthInfo Twitter;
        public string SteamUsername;
        public string SteamPassword;
#pragma warning restore 0649
    }
}
