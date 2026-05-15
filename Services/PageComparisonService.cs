using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace AuditApp.Services
{
    /// <summary>
    /// Service for comparing content between source and test pages
    /// Uses similarity scoring based on text content extraction
    /// </summary>
    public class PageComparisonService
    {
        private readonly UrlUtilityService _urlService;

        public PageComparisonService(UrlUtilityService urlService)
        {
            _urlService = urlService;
        }

        /// <summary>
        /// Compare two URLs and return similarity score (0.0 to 1.0)
        /// </summary>
        public async Task<double> CompareUrlsAsync(string sourceUrl, string testUrl)
        {
            try
            {
                var sourceContent = await FetchPageAsync(sourceUrl);
                var testContent = await FetchPageAsync(testUrl);

                if (string.IsNullOrEmpty(sourceContent) || string.IsNullOrEmpty(testContent))
                    return 0.0;

                return CalculateSimilarity(sourceContent, testContent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARN] Failed to compare {sourceUrl} vs {testUrl}: {ex.Message}");
                return 0.0;
            }
        }

        /// <summary>
        /// Fetch and extract text content from a page
        /// </summary>
        private async Task<string> FetchPageAsync(string url)
        {
            try
            {
                using (var handler = _urlService.GetHandlerForUrl(url))
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
                {
                    var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead);
                    if (!response.IsSuccessStatusCode)
                        return "";

                    var html = await response.Content.ReadAsStringAsync();
                    return ExtractTextContent(html);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARN] Failed to fetch {url}: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Extract visible text from HTML (remove scripts, styles, etc.)
        /// </summary>
        private string ExtractTextContent(string html)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Remove script and style tags
                foreach (var node in doc.DocumentNode.SelectNodes("//script | //style | //noscript")?.ToList() ?? new List<HtmlNode>())
                {
                    node.Remove();
                }

                // Get inner text
                var text = doc.DocumentNode.InnerText;

                // Normalize whitespace
                text = Regex.Replace(text, @"\s+", " ").Trim();

                return text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARN] Failed to extract text: {ex.Message}");
                // Fallback: just return stripped HTML
                return Regex.Replace(html, @"<[^>]*>", " ");
            }
        }

        /// <summary>
        /// Calculate similarity between two text strings using SequenceMatcher logic
        /// Returns a score from 0.0 to 1.0
        /// </summary>
        private double CalculateSimilarity(string source, string test)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(test))
                return 0.0;

            // Simple approach: character-level similarity
            // For better results, use Levenshtein distance or other metrics

            // Tokenize into words
            var sourceWords = TokenizeText(source);
            var testWords = TokenizeText(test);

            if (sourceWords.Count == 0 || testWords.Count == 0)
                return 0.0;

            // Use longest common subsequence approach
            int matches = 0;
            var testWordSet = new HashSet<string>(testWords);

            foreach (var word in sourceWords)
            {
                if (testWordSet.Contains(word))
                    matches++;
            }

            double similarity = (double)matches / Math.Max(sourceWords.Count, testWords.Count);
            return Math.Min(1.0, similarity);
        }

        /// <summary>
        /// Tokenize text into words
        /// </summary>
        private List<string> TokenizeText(string text)
        {
            // Simple tokenization: split on whitespace and punctuation
            var words = Regex.Split(text, @"[\s\p{P}]+")
                .Where(w => !string.IsNullOrEmpty(w) && w.Length > 2) // Skip short words
                .Select(w => w.ToLower())
                .ToList();

            return words;
        }
    }
}
