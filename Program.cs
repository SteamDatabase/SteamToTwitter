using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Net;
using System.Text;
using System.Threading;
using SteamKit2;

namespace SteamToTwitter
{
    internal static class MainClass
    {
        private const string TWEET_URI = "https://api.twitter.com/1.1/statuses/update.json";
        private const uint TWEET_AFTER = 10;

        private static readonly System.Timers.Timer Timer = new System.Timers.Timer();
        private static readonly SteamClient Client = new SteamClient();
        private static readonly SteamUser User = Client.GetHandler<SteamUser>();
        private static readonly SteamFriends Friends = Client.GetHandler<SteamFriends>();
        private static bool IsRunning = true;
        private static DateTime LastDowntimeTweet;
        private static DateTime DownSince;
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

            Timer.Elapsed += OnTimer;
            Timer.Interval = TimeSpan.FromMinutes(TWEET_AFTER).TotalMilliseconds;

            var CallbackManager = new CallbackManager(Client);

            CallbackManager.Register(new Callback<SteamClient.ConnectedCallback>(OnConnected));
            CallbackManager.Register(new Callback<SteamClient.DisconnectedCallback>(OnDisconnected));
            CallbackManager.Register(new Callback<SteamUser.LoggedOnCallback>(OnLoggedOn));
            CallbackManager.Register(new Callback<SteamUser.LoggedOffCallback>(OnLoggedOff));
            CallbackManager.Register(new Callback<SteamUser.AccountInfoCallback>(OnAccountInfo));
            CallbackManager.Register(new Callback<SteamFriends.ClanStateCallback>(OnClanState));

            Client.Connect();

            while (IsRunning)
            {
                CallbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(5));
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

                    Log.WriteDebug("Twitter", "Response: {0}", Encoding.UTF8.GetString(responsebytes));
                }
            }
            catch (Exception e)
            {
                Log.WriteError("Twitter", "EXCEPTION: {0}", e.Message);
            }
        }

        private static void OnTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            TimeSpan timeDiff = DateTime.Now - LastDowntimeTweet;

            if (timeDiff.TotalHours < 2)
            {
                Log.WriteInfo("Downtime", "We could tweet about Steam downtime, but 2 hours haven't passed since last tweet");

                return;
            }

            LastDowntimeTweet = DateTime.Now;

            var diff = LastDowntimeTweet.Subtract(DownSince);

            Log.WriteInfo("Downtime", "Tweeting about Steam downtime...");

            PublishTweet(string.Format("Steam appears to be down since {0} UTC ({1} minutes ago)", DownSince.ToLongTimeString(), diff.Minutes), "http://steamstat.us/");
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

            if (!Timer.Enabled)
            {
                DownSince = DateTime.Now;

                Timer.Start();
            }
                
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

            Timer.Stop();

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
                var message = announcement.Headline;

                if (!string.IsNullOrEmpty(groupName) && !announcement.Headline.Contains(groupName.Replace("Steam", string.Empty).Trim()))
                {
                    message = string.Format("{0}:\n{1}", groupName, announcement.Headline);
                }

                PublishTweet(message, string.Format("http://steamcommunity.com/gid/{0}/announcements/detail/{1}", callback.ClanID, announcement.ID));
            }
        }
    }
}
