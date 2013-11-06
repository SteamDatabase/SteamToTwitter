using System;
using System.Configuration;
using System.Threading;
using SteamKit2;
using TweetinCore.Interfaces.TwitterToken;
using Tweetinvi;
using TwitterToken;

namespace SteamToTwitter
{
    internal static class MainClass
    {
        private static bool IsRunning = true;
        private static IToken TwitterToken;
        private static DateTime LastDowntimeTweet;
        private static DateTime DownSince;
        private static readonly System.Timers.Timer Timer = new System.Timers.Timer();
        private static readonly SteamClient Client = new SteamClient();
        private static readonly SteamUser User = Client.GetHandler<SteamUser>();
        private static readonly SteamFriends Friends = Client.GetHandler<SteamFriends>();

        public static void Main()
        {
            Console.CancelKeyPress += delegate
            {
                Console.WriteLine("Exiting...");

                try
                {
                    Client.Disconnect();
                }
                catch
                {
                    Console.WriteLine("Failed to disconnect from Steam");
                }

                IsRunning = false;
            };

            TwitterToken = new Token(
                ConfigurationManager.AppSettings["token_AccessToken"],
                ConfigurationManager.AppSettings["token_AccessTokenSecret"],
                ConfigurationManager.AppSettings["token_ConsumerKey"],
                ConfigurationManager.AppSettings["token_ConsumerSecret"]
            );

            ITokenRateLimits tokenLimits = TwitterToken.GetRateLimit();

            Console.WriteLine("Remaning Twitter requests: {0} of {1}", tokenLimits.ApplicationRateLimitStatusLimit.Remaining, tokenLimits.ApplicationRateLimitStatusLimit.Limit);

            Timer.Elapsed += new System.Timers.ElapsedEventHandler(OnTimer);
            Timer.Interval = TimeSpan.FromMinutes(10).TotalMilliseconds;

            var CallbackManager = new CallbackManager(Client);

            CallbackManager.Register(new Callback<SteamClient.ConnectedCallback>(OnConnected));
            CallbackManager.Register(new Callback<SteamClient.DisconnectedCallback>(OnDisconnected));
            CallbackManager.Register(new Callback<SteamUser.LoggedOnCallback>(OnLoggedOn));
            CallbackManager.Register(new Callback<SteamUser.LoggedOffCallback>(OnLoggedOff));
            CallbackManager.Register(new Callback<SteamFriends.ClanStateCallback>(OnClanState));

            Client.Connect();

            while (IsRunning)
            {
                CallbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }
        }

        private static bool PublishTweet(string tweet, string url)
        {
            // 117 is a magical tweet length number
            if (tweet.Length > 117)
            {
                tweet = string.Format("{0}…", tweet.Substring(0, 116));
            }

            return new Tweet(string.Format("{0} {1}", tweet, url)).Publish(TwitterToken);
        }

        private static void OnTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            TimeSpan timeDiff = DateTime.Now - LastDowntimeTweet;

            if (timeDiff.TotalHours < 2)
            {
                Console.WriteLine("We could tweet about Steam downtime, but 2 hours haven't passed since last tweet");

                return;
            }

            LastDowntimeTweet = DateTime.Now;

            Console.WriteLine("Tweeting about Steam downtime...");

            PublishTweet(string.Format("Steam appears to be down since {0}", DownSince.ToLongTimeString()), "http://steamstat.us/");
        }

        private static void OnConnected(SteamClient.ConnectedCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                Console.WriteLine("Could not connect to Steam: {0}", callback.Result);

                IsRunning = false;

                return;
            }

            Console.WriteLine("Connected to Steam, logging in...");

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

            DownSince = DateTime.Now;

            Timer.Start();

            const uint RETRY_DELAY = 15;

            Console.WriteLine("Disconnected from Steam. Retrying in {0} seconds...", RETRY_DELAY);

            Thread.Sleep(TimeSpan.FromSeconds(RETRY_DELAY));

            Client.Connect();
        }

        private static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                Console.WriteLine("Failed to login: {0}", callback.Result);

                Thread.Sleep(TimeSpan.FromSeconds(2));

                return;
            }

            Timer.Stop();

            string serverTime = callback.ServerTime.ToString();

            Console.WriteLine("Logged in, current valve time is {0} UTC", serverTime);
        }

        private static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Console.WriteLine("Logged off from Steam");
        }

        public static void OnClanState(SteamFriends.ClanStateCallback callback)
        {
            if (callback.Announcements.Count == 0)
            {
                return;
            }

            string groupName = callback.ClanName;

            if (string.IsNullOrEmpty(groupName))
            {
                groupName = Friends.GetClanName(callback.ClanID);
            }

            foreach (var announcement in callback.Announcements)
            {
                string message = string.IsNullOrEmpty(groupName) ? announcement.Headline : string.Format("[{0}] {1}", groupName, announcement.Headline);

                PublishTweet(message, string.Format("http://steamcommunity.com/gid/{0}/announcements/detail/{1}", callback.ClanID, announcement.ID));

                Console.WriteLine("Group Announcement: {0} \"{1}\"", groupName, announcement.Headline);
            }
        }
    }
}
