using System;

namespace SteamToTwitter
{
    internal class Configuration
    {
        public TinyTwitter.OAuthInfo Twitter { get; set; }
        public string SteamUsername { get; set; }
        public string SteamPassword { get; set; }
    }
}
