using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace FocLauncher.Utilities
{
    internal static class WorkshopImageResolver
    {
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EMPIRE AT WAR Launcher",
            "WorkshopImages");

        private static readonly string[] ImageExtensions = { ".bmp", ".gif", ".ico", ".jpeg", ".jpg", ".png" };

        public static async Task<string?> ResolveAsync(string workshopId, HtmlDocument? workshopPage)
        {
            if (!long.TryParse(workshopId, out _))
                return null;

            var cachedFile = Path.Combine(CacheDirectory, workshopId + ".jpg");
            if (File.Exists(cachedFile) && new FileInfo(cachedFile).Length > 0)
                return cachedFile;

            workshopPage ??= await HtmlDownloader.GetSteamModPageDocumentAsync(workshopId).ConfigureAwait(false);
            var imageUrl = GetPreviewImageUrl(workshopPage);
            if (string.IsNullOrEmpty(imageUrl))
                return null;

            var temporaryFile = cachedFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) EMPIRE AT WAR Launcher";
                    var data = await client.DownloadDataTaskAsync(new Uri(imageUrl)).ConfigureAwait(false);
                    if (data == null || data.Length == 0)
                        return null;

                    await Task.Run(() => File.WriteAllBytes(temporaryFile, data)).ConfigureAwait(false);
                }

                if (File.Exists(cachedFile))
                    return cachedFile;

                File.Move(temporaryFile, cachedFile);
                return cachedFile;
            }
            catch
            {
                return File.Exists(cachedFile) ? cachedFile : null;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryFile))
                        File.Delete(temporaryFile);
                }
                catch
                {
                    // Cache cleanup failure must not affect launcher startup.
                }
            }
        }

        private static string? GetPreviewImageUrl(HtmlDocument? workshopPage)
        {
            if (workshopPage == null)
                return null;

            var meta = workshopPage.DocumentNode.SelectSingleNode(
                "//meta[translate(@property, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')='og:image']")?.GetAttributeValue("content", null);
            var imageUrl = NormalizeUrl(meta);
            if (!string.IsNullOrEmpty(imageUrl))
                return imageUrl;

            var twitter = workshopPage.DocumentNode.SelectSingleNode(
                "//meta[translate(@name, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')='twitter:image']")?.GetAttributeValue("content", null);
            imageUrl = NormalizeUrl(twitter);
            if (!string.IsNullOrEmpty(imageUrl))
                return imageUrl;

            var image = workshopPage.DocumentNode.SelectSingleNode(
                "//img[contains(@class, 'workshopItemPreviewImage')]")?.GetAttributeValue("src", null);
            imageUrl = NormalizeUrl(image);
            if (!string.IsNullOrEmpty(imageUrl))
                return imageUrl;

            var style = workshopPage.DocumentNode.SelectSingleNode(
                "//*[contains(@class, 'workshopItemPreviewImage')]")?.GetAttributeValue("style", null);
            var match = style == null
                ? null
                : Regex.Match(style, "url\\(['\"]?(?<url>[^'\")]+)", RegexOptions.IgnoreCase);
            return NormalizeUrl(match?.Groups["url"].Value);
        }

        private static string? NormalizeUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = WebUtility.HtmlDecode(value.Trim());
            if (value.StartsWith("//", StringComparison.Ordinal))
                value = "https:" + value;

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return null;

            return uri.AbsoluteUri;
        }
    }
}
