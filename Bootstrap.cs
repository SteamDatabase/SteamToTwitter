using System;
using System.Configuration;
using System.Threading;
using SteamKit2;
using TinyTwitter;

namespace SteamToTwitter
{
    internal static class Bootstrap
    {
        private static readonly SteamClient Client = new SteamClient();
        private static readonly SteamUser User = Client.GetHandler<SteamUser>();
        private static readonly SteamFriends Friends = Client.GetHandler<SteamFriends>();
        private static bool IsRunning = true;
        private static TinyTwitter.TinyTwitter Twitter;

        public static void Main()
        {
            Log.WriteInfo("Program", "Starting...");

            Console.CancelKeyPress += delegate
            {
                Log.WriteInfo("Program", "Exiting...");

                try
                {
                    User.LogOff();
                    Client.Disconnect();
                }
                catch
                {
                    Log.WriteError("Steam", "Failed to disconnect from Steam");
                }

                IsRunning = false;
            };

            var oauth = new OAuthInfo
            {
                AccessToken = ConfigurationManager.AppSettings["token_AccessToken"],
                AccessSecret = ConfigurationManager.AppSettings["token_AccessTokenSecret"],
                ConsumerKey = ConfigurationManager.AppSettings["token_ConsumerKey"],
                ConsumerSecret = ConfigurationManager.AppSettings["token_ConsumerSecret"]
            };

            Twitter = new TinyTwitter.TinyTwitter(oauth);

            var callbackManager = new CallbackManager(Client);

            callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
            callbackManager.Subscribe<SteamUser.AccountInfoCallback>(OnAccountInfo);
            callbackManager.Subscribe<SteamFriends.ClanStateCallback>(OnClanState);

            Client.Connect();

            while (IsRunning)
            {
                callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(5));
            }
        }

        private static void OnConnected(SteamClient.ConnectedCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                Log.WriteInfo("Steam", "Could not connect to Steam: {0}", callback.Result);

                IsRunning = false;

                return;
            }

            Log.WriteInfo("Steam", "Connected to Steam, logging in...");

            User.LogOn(new SteamUser.LogOnDetails
            {
                Username = ConfigurationManager.AppSettings["steam_Username"],
                Password = ConfigurationManager.AppSettings["steam_Password"]
            });
        }

        private static void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            if (!IsRunning)
            {
                Log.WriteInfo("Steam", "Shutting down...");

                return;
            }

            Log.WriteInfo("Steam", "Disconnected from Steam. Retrying...");

            Thread.Sleep(TimeSpan.FromSeconds(15));

            Client.Connect();
        }

        private static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                Log.WriteError("Steam", "Failed to login: {0}", callback.Result);

                Thread.Sleep(TimeSpan.FromSeconds(2));

                return;
            }

            Log.WriteInfo("Steam", "Logged in, current valve time is {0} UTC", callback.ServerTime.ToString());
        }

        private static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Log.WriteInfo("Steam", "Logged off from Steam");
        }

        private static void OnAccountInfo(SteamUser.AccountInfoCallback callback)
        {
            Friends.SetPersonaState(EPersonaState.Busy);
        }

        public static void OnClanState(SteamFriends.ClanStateCallback callback)
        {
            if (callback.Announcements.Count == 0)
            {
                return;
            }

            var groupName = callback.ClanName;

            if (string.IsNullOrEmpty(groupName))
            {
                groupName = Friends.GetClanName(callback.ClanID);
            }

            foreach (var announcement in callback.Announcements)
            {
                var message = announcement.Headline.Trim();

                if (!string.IsNullOrEmpty(groupName) && !announcement.Headline.Contains(groupName.Replace("Steam", string.Empty).Trim()))
                {
                    message = string.Format("{0}:\n{1}", groupName, announcement.Headline);
                }

                // 117 is a magical tweet length number
                if (message.Length > 117)
                {
                    message = string.Format("{0}…", message.Substring(0, 116));
                }

                var url = string.Format("http://steamcommunity.com/gid/{0}/announcements/detail/{1}", callback.ClanID, announcement.ID);

                Log.WriteInfo("Twitter", "Tweeting \"{0}\" - {1}", message, url);

                Twitter.UpdateStatus(string.Format("{0} {1}", message, url));
            }
        }
    }
}
