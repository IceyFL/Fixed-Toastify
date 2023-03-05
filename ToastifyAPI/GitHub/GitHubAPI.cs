﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using ToastifyAPI.Core;
using ToastifyAPI.GitHub.Model;
using ToastifyAPI.Helpers;

namespace ToastifyAPI.GitHub
{
    public class GitHubAPI
    {
        #region Static Fields and Properties

        private static readonly Encoding encoding = Encoding.UTF8;

        private static List<Emoji> emojis;

        private static string ApiBase { get; } = "https://api.github.com";

        #endregion

        private readonly IProxyConfig proxyConfig;

        public GitHubAPI(IProxyConfig proxyConfig)
        {
            this.proxyConfig = proxyConfig;

            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        public string GetFullEndpointUrl(string endpoint)
        {
            return $"{ApiBase}{endpoint}";
        }

        public string GetFullEndpointUrl(string endpoint, RepoInfo repo)
        {
            return this.GetFullEndpointUrl(repo.Format(endpoint));
        }

        public T DownloadJson<T>(string url) where T : BaseModel
        {
            HttpClientHandler httpClientHandler = Net.CreateHttpClientHandler(this.proxyConfig);
            using (var http = new HttpClient(httpClientHandler))
            {
                AddDefaultHeaders(http);

                using (HttpResponseMessage response = http.GetAsync(url).Result)
                {
                    byte[] raw = response.Content.ReadAsByteArrayAsync().Result;
                    string json = raw.Length > 0 ? encoding.GetString(raw) : "{}";

                    var result = JsonConvert.DeserializeObject<T>(json);
                    result.HttpResponseHeaders = response.Headers;
                    result.HttpStatusCode = response.StatusCode;
                    return result;
                }
            }
        }

        public CollectionData<T> DownloadCollectionJson<T>(string url) where T : BaseModel
        {
            HttpClientHandler httpClientHandler = Net.CreateHttpClientHandler(this.proxyConfig);
            using (var http = new HttpClient(httpClientHandler))
            {
                AddDefaultHeaders(http);

                using (HttpResponseMessage response = http.GetAsync(url).Result)
                {
                    byte[] raw = response.Content.ReadAsByteArrayAsync().Result;
                    string json = raw.Length > 0 ? encoding.GetString(raw) : "{}";

                    var result = JsonConvert.DeserializeObject<T[]>(json);
                    var collection = new CollectionData<T>
                    {
                        Collection = result.ToList(),
                        HttpResponseHeaders = response.Headers,
                        HttpStatusCode = response.StatusCode
                    };
                    return collection;
                }
            }
        }

        private T DownloadJsonInternal<T>(string url)
        {
            HttpClientHandler httpClientHandler = Net.CreateHttpClientHandler(this.proxyConfig);
            using (var http = new HttpClient(httpClientHandler))
            {
                AddDefaultHeaders(http);

                using (HttpResponseMessage response = http.GetAsync(url).Result)
                {
                    byte[] raw = response.Content.ReadAsByteArrayAsync().Result;
                    string json = raw.Length > 0 ? encoding.GetString(raw) : "{}";

                    var result = JsonConvert.DeserializeObject<T>(json);
                    return result;
                }
            }
        }

        #region Static Members

        private static HttpResponseMessage HttpHead(string url, IProxyConfig proxyConfig = null)
        {
            var httpClientHandler = new HttpClientHandler();
            if (proxyConfig != null)
                httpClientHandler.Proxy = proxyConfig.CreateWebProxy();

            using (HttpClient httpClient = new HttpClient(handler: httpClientHandler, disposeHandler: true))
            {
                AddDefaultHeaders(httpClient);
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                return httpClient.Send(new HttpRequestMessage(HttpMethod.Head, url));
            }
        }

        private static void AddDefaultHeaders(HttpClient httpClient)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "aleab/toastify");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "aleab/toastify");
        }

        #endregion

        #region GitHubify

        private static readonly Regex mentionRegex = new Regex(@"\B@(\w+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex issueOrPullRegex = new Regex(@"\B#([0-9]+)\b", RegexOptions.Compiled);

        private static readonly Regex emojiRegex = new Regex(@":([^\s]+):", RegexOptions.Compiled);

        public string GitHubify(string ghText)
        {
            // Mentions (@)
            ghText = mentionRegex.Replace(ghText, match =>
            {
                string username = match.Groups[1].Value;
                string pattern = this.GitHubify_Mention(username);
                return match.Result(pattern);
            });

            // Issues or PRs (#)
            ghText = issueOrPullRegex.Replace(ghText, match =>
            {
                string sNumber = match.Groups[1].Value;
                int number = int.Parse(sNumber);
                string pattern = this.GitHubify_IssueOrPull(number);
                return match.Result(pattern);
            });

            // Emojis (::)
            ghText = emojiRegex.Replace(ghText, match =>
            {
                string emojiName = match.Groups[1].Value;
                string pattern = this.GitHubify_Emoji(emojiName);
                return match.Result(pattern);
            });

            return ghText;
        }

        /// <summary>
        ///     Returns a replace pattern for a mention to the specified username
        /// </summary>
        /// <param name="username">The username</param>
        /// <returns>A regex replace pattern</returns>
        private string GitHubify_Mention(string username)
        {
            string url = $"https://github.com/{username}";

            HttpResponseMessage response = null;
            try
            {
                response = HttpHead(url, this.proxyConfig);
                return $"[@$1]({url})";
            }
            catch
            {
                return "@$1";
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        ///     Returns a replace pattern for the specified issue or PR number
        /// </summary>
        /// <param name="number">The issue or PR number</param>
        /// <param name="pull">Whether it's a pull request or not</param>
        /// <returns>A regex replace pattern</returns>
        private string GitHubify_IssueOrPull(int number, bool pull = false)
        {
            string s = $"{(pull ? "pull" : "issues")}/{number}";
            string url = $"https://github.com/aleab/toastify/{s}";

            HttpResponseMessage response = null;
            try
            {
                response = HttpHead(url, this.proxyConfig);
                return $"[#$1]({url})";
            }
            catch
            {
                return pull ? "#$1" : this.GitHubify_IssueOrPull(number, true);
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        ///     Returns a replace pattern for the specified commit hash
        /// </summary>
        /// <param name="hash">The commit hash</param>
        /// <returns>A regex replace pattern</returns>
        private string GitHubify_Hash(string hash)
        {
            string url = $"https://github.com/aleab/toastify/commit/{hash}";

            HttpResponseMessage response = null;
            try
            {
                response = HttpHead(url, this.proxyConfig);
                return $"[`$1`]({url})";
            }
            catch
            {
                return "$_";
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        ///     Returns a replace pattern for the specified emoji
        /// </summary>
        /// <param name="emojiName">The emoji name</param>
        /// <returns>A regex replace pattern</returns>
        private string GitHubify_Emoji(string emojiName)
        {
            // TODO: Use inline images instead of unicode characters, which are just rendered in B/W
            if (emojis == null)
            {
                const string url = "https://api.github.com/emojis";
                var list = this.DownloadJsonInternal<Dictionary<string, string>>(url);
                emojis = list.Count <= 0
                    ? new List<Emoji>(0)
                    : list.Select(kvp => new Emoji { Name = kvp.Key, Url = kvp.Value }).ToList();
            }

            if (emojis.Count <= 0)
                return string.Empty;

            Emoji emoji = emojis.SingleOrDefault(e => e.Name == emojiName);
            if (emoji == null)
                return "$_";

            string unicodeString = emoji.GetAsUnicodeString();
            return string.IsNullOrWhiteSpace(unicodeString) ? string.Empty : unicodeString;
        }

        #endregion GitHubify
    }
}