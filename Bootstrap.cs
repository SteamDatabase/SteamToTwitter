using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Net;
using System.Text;
using System.Threading;
using SteamKit2;

namespace SteamToTwitter
{
    internal static class Bootstrap
    {
        private const string TWEET_URI = "https://api.twitter.com/1.1/statuses/update.json";

        private static readonly SteamClient Client = new SteamClient();
        private static readonly SteamUser User = Client.GetHandler<SteamUser>();
        private static readonly SteamFriends Friends = Client.GetHandler<SteamFriends>();
        private static bool IsRunning = true;
        private static TwitterAuthorization TwitterAuthorization;

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

            TwitterAuthorization = new TwitterAuthorization(
                ConfigurationManager.AppSettings["token_AccessToken"],
                ConfigurationManager.AppSettings["token_AccessTokenSecret"],
                ConfigurationManager.AppSettings["token_ConsumerKey"],
                ConfigurationManager.AppSettings["token_ConsumerSecret"]
            );
                
            var callbackManager = new CallbackManager(Client);

            callbackManager.Register(new Callback<SteamClient.ConnectedCallback>(OnConnected));
            callbackManager.Register(new Callback<SteamClient.DisconnectedCallback>(OnDisconnected));
            callbackManager.Register(new Callback<SteamUser.LoggedOnCallback>(OnLoggedOn));
            callbackManager.Register(new Callback<SteamUser.LoggedOffCallback>(OnLoggedOff));
            callbackManager.Register(new Callback<SteamUser.AccountInfoCallback>(OnAccountInfo));
            callbackManager.Register(new Callback<SteamFriends.ClanStateCallback>(OnClanState));

            Client.Connect();

            while (IsRunning)
            {
                callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(5));
            }
        }

        private static void PublishTweet(string message, string url)
        {
            // 117 is a magical tweet length number
            if (message.Length > 117)
            {
                message = string.Format("{0}…", message.Substring(0, 116));
            }

            Log.WriteInfo("Twitter", "Tweeting \"{0}\" - {1}", message, url);

            try
            {
                using(var webClient = new WebClient())
                {
                    var parameters = new NameValueCollection();
                    parameters.Add("status", string.Format("{0} {1}", message, url));

                    var authHeader = TwitterAuthorization.GetHeader(TWEET_URI, parameters);

                    webClient.Headers.Add(string.Format("Authorization: OAuth {0}", authHeader));  

                    var responsebytes = webClient.UploadValues(TWEET_URI, "POST", parameters);
                    var response = Encoding.UTF8.GetString(responsebytes);

                    // Parsing JSON is for silly people! (it's for debugging purposes only anyway
                    if(response.Contains("\"created_at\""))
                    {
                        Log.WriteDebug("Twitter", "Tweet sent");
                    }
                    else
                    {
                        Log.WriteDebug("Twitter", "Response: {0}", response);
                    }
                }
            }
            catch (Exception e)
            {
                Log.WriteError("Twitter", "EXCEPTION: {0}", e.Message);
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

                PublishTweet(message, string.Format("http://steamcommunity.com/gid/{0}/announcements/detail/{1}", callback.ClanID, announcement.ID));
            }
        }
    }
}
