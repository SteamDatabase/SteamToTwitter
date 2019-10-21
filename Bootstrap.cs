using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using SteamKit2;

namespace SteamToTwitter
{
    internal static class Bootstrap
    {
        private static readonly SteamClient Client = new SteamClient();
        private static readonly SteamUser User = Client.GetHandler<SteamUser>();
        private static readonly SteamFriends Friends = Client.GetHandler<SteamFriends>();
        private static bool IsRunning = true;
        private static TinyTwitter.TinyTwitter Twitter;
        private static Configuration Configuration;
        private static string authCode, twoFactorAuth;

        public static void Main()
        {
            Console.Title = "SteamToTwitter";

            Log("Starting...");

            Console.CancelKeyPress += delegate
            {
                Log("Exiting...");

                try
                {
                    User.LogOff();
                    Client.Disconnect();
                }
                catch
                {
                    Log("Failed to disconnect from Steam");
                }

                IsRunning = false;
            };

            Configuration = JsonSerializer.Deserialize<Configuration>(File.ReadAllText("settings.json"));

            Twitter = new TinyTwitter.TinyTwitter(Configuration.Twitter);

            var callbackManager = new CallbackManager(Client);

            callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
            callbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);
            callbackManager.Subscribe<SteamUser.AccountInfoCallback>(OnAccountInfo);
            callbackManager.Subscribe<SteamFriends.ClanStateCallback>(OnClanState);

            Client.Connect();

            var reconnectTime = TimeSpan.FromHours(6);
            new Timer(_ => Client.Disconnect(), null, reconnectTime, reconnectTime);

            while (IsRunning)
            {
                callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(5));
            }
        }

        private static void OnConnected(SteamClient.ConnectedCallback callback)
        {
            Log("Connected to Steam, logging in...");

            byte[] sentryHash = null;

            if (File.Exists("sentry.bin"))
            {
                byte[] sentryFile = File.ReadAllBytes("sentry.bin");
                sentryHash = CryptoHelper.SHAHash(sentryFile);
            }

            User.LogOn(new SteamUser.LogOnDetails
            {
                AuthCode = authCode,
                TwoFactorCode = twoFactorAuth,
                SentryFileHash = sentryHash,
                Username = Configuration.SteamUsername,
                Password = Configuration.SteamPassword
            });
        }

        private static void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            if (!IsRunning)
            {
                Log("Shutting down...");

                return;
            }

            Log("Disconnected from Steam. Retrying...");

            Thread.Sleep(TimeSpan.FromSeconds(15));

            Client.Connect();
        }

        private static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result == EResult.AccountLoginDeniedNeedTwoFactor)
            {
                Console.Write("Please enter your 2 factor auth code from your authenticator app: ");
                twoFactorAuth = Console.ReadLine();
                return;
            }

            if (callback.Result == EResult.AccountLogonDenied)
            {
                Console.Write("Please enter the auth code sent to the email at {0}: ", callback.EmailDomain);
                authCode = Console.ReadLine();
                return;
            }

            if (callback.Result != EResult.OK)
            {
                Log("Failed to login: {0}", callback.Result);

                Thread.Sleep(TimeSpan.FromSeconds(2));

                return;
            }

            Log("Logged in, current valve time is {0} UTC", callback.ServerTime.ToString());
        }

        private static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Log("Logged off from Steam: {0}", callback.Result);
        }

        private static void OnAccountInfo(SteamUser.AccountInfoCallback callback)
        {
            Friends.SetPersonaState(EPersonaState.Busy);
        }

        private static void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback)
        {
            Log("Updating sentryfile so that you don't need to authenticate with SteamGuard next time.");

            byte[] sentryHash = CryptoHelper.SHAHash(callback.Data);

            File.WriteAllBytes("sentry.bin", callback.Data);

            User.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
            {
                JobID = callback.JobID,
                FileName = callback.FileName,
                BytesWritten = callback.BytesToWrite,
                FileSize = callback.Data.Length,
                Offset = callback.Offset,
                Result = EResult.OK,
                LastError = 0,
                OneTimePassword = callback.OneTimePassword,
                SentryFileHash = sentryHash,
            });
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

                var url = $"http://steamcommunity.com/gid/{callback.ClanID.ConvertToUInt64()}/announcements/detail/{announcement.ID}";

                for (var i = 0; i < 2; i++)
                {
                    try
                    {
                        Log("Tweeting \"{0}\" - {1}", message, url);

                        Twitter.UpdateStatus(string.Format("{0} {1}", message, url));

                        break;
                    }
                    catch (Exception e)
                    {
                        Log("Exception: {0}", e.Message);
                    }
                }
            }
        }

        public static void Log(string format, params object[] args)
        {
            Console.WriteLine("[" + DateTime.Now.ToString("R") + "] " + string.Format(format, args));
        }
    }
}
