using System;
using System.Configuration;
using System.Threading;
using SteamKit2;
using Tweetinvi;

namespace SteamToTwitter
{
    internal static class MainClass
    {
        private const uint TWEET_AFTER = 10;
        private const double LONGITUDE = -122.193798;
        private const double LATITUDE = 47.614033;

        private static readonly System.Timers.Timer Timer = new System.Timers.Timer();
        private static readonly SteamClient Client = new SteamClient();
        private static readonly SteamUser User = Client.GetHandler<SteamUser>();
        private static readonly SteamFriends Friends = Client.GetHandler<SteamFriends>();
        private static bool IsRunning = true;
        private static DateTime LastDowntimeTweet;
        private static DateTime DownSince;

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

            TwitterCredentials.SetCredentials(
                ConfigurationManager.AppSettings["token_AccessToken"],
                ConfigurationManager.AppSettings["token_AccessTokenSecret"],
                ConfigurationManager.AppSettings["token_ConsumerKey"],
                ConfigurationManager.AppSettings["token_ConsumerSecret"]
            );

            var tokenLimits = RateLimit.GetCurrentCredentialsRateLimits();

            Log.WriteInfo("Twitter", "Remaining Twitter requests: {0} of {1}", tokenLimits.ApplicationRateLimitStatusLimit.Remaining, tokenLimits.ApplicationRateLimitStatusLimit.Limit);

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
                var tweet = Tweet.CreateTweet(string.Format("{0} {1}", message, url));

                tweet.PublishWithGeo(LONGITUDE, LATITUDE);

                Log.WriteDebug("Twitter", "Tweet published: {0}", tweet.IsTweetPublished);
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
