using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SteamToTwitter
{
    public class TwitterAuthorization
    {
        private readonly string ApiKey;
        private readonly string ApiSecret;
        private readonly string AccessToken;
        private readonly string AccessTokenSecret;

        public TwitterAuthorization(string a, string b, string c, string d)
        {
            ApiKey = c;
            ApiSecret = d;
            AccessToken = a;
            AccessTokenSecret = b;
        }

        public string GetHeader(string uri, string type, NameValueCollection parameters)
        {
            var nonce = Guid.NewGuid().ToString();
            var timestamp = Convert.ToInt64((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString(CultureInfo.InvariantCulture);

            var oauthParameters = new SortedDictionary<string, string>
            {
                { "oauth_consumer_key", ApiKey },
                { "oauth_nonce", nonce },
                { "oauth_signature_method", "HMAC-SHA1" },
                { "oauth_timestamp", timestamp },
                { "oauth_token", AccessToken },
                { "oauth_version", "1.0" }
            };

            var signingParameters = new SortedDictionary<string, string>(oauthParameters);

            foreach (string k in parameters)
            {
                signingParameters.Add(k, parameters[k]);
            }

            var builder = new UriBuilder(uri) { Query = "" };
            var baseUrl = builder.Uri.AbsoluteUri;

            var parameterString = string.Join("&",
                                         from k in signingParameters.Keys
                                         select Uri.EscapeDataString(k) + "=" +
                                         Uri.EscapeDataString(signingParameters[k]));

            var stringToSign = string.Join("&", new[] { type, baseUrl, parameterString }.Select(Uri.EscapeDataString));
            var signingKey = ApiSecret + "&" + AccessTokenSecret;
            var signature = Convert.ToBase64String(new HMACSHA1(Encoding.ASCII.GetBytes(signingKey)).ComputeHash(Encoding.ASCII.GetBytes(stringToSign)));

            oauthParameters.Add("oauth_signature", signature);

            var authHeader = string.Join(", ", from k in oauthParameters.Keys
                                                            select string.Format(@"{0}=""{1}""",
                                                                    Uri.EscapeDataString(k),
                                                                    Uri.EscapeDataString(oauthParameters[k])));

            return authHeader;
        }
    }
}
