using FishyFlip;
using FishyFlip.Models;
using Microsoft.Extensions.Logging.Debug;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using TwitterSky.Models;
using static System.Net.Mime.MediaTypeNames;

namespace TwitterSky
{
    public class TweetImporter
    {
        private readonly Options options;
        private List<TweetArchiveModel>? _tweetArchive;
        private string _lastParsedTweetId = string.Empty;
        private CancellationToken _cancellationToken = new();
        private List<string> _twitterHandles = new();

        private readonly ATProtocol _bskyProtocol = new ATProtocolBuilder()
            .WithInstanceUrl(new Uri("https://bsky.social"))
            .EnableAutoRenewSession(true)
            .Build();

        private Session? _bskySession;

        private const string TW_DATE_FORMAT = "ddd MMM dd HH:mm:ss zzz yyyy";

        public TweetImporter(Options options)
        {
            this.options = options;
        }

        public async Task ParseJson()
        {
            if (!string.IsNullOrEmpty(options.ArchivePath))
            {
                //If we receive a path to the main folder, we need to append /data/tweet.js otherwise we just use the path
                if (Directory.Exists(options.ArchivePath))
                {
                    options.ArchivePath = Path.Combine(options.ArchivePath, "data", "tweet.js");
                }

                //Make sure the file exists
                if (!File.Exists(options.ArchivePath))
                {
                    throw new FileNotFoundException("tweet.js file not found");
                }

                string json = File.ReadAllText(options.ArchivePath);

                //Remove the window.YTD.tweets.part0 = at the beginning of the file
                int start = json.IndexOf('[');
                json = json[start..];

                _tweetArchive = JsonSerializer.Deserialize<List<TweetArchiveModel>>(json);

                Console.WriteLine($"Found {_tweetArchive.Count} tweets in the archive. Applying filters...");

                //Filter out tweets that are not in the date range
                if (!string.IsNullOrEmpty(options.MinDate))
                {
                    _tweetArchive = _tweetArchive.Where(x => DateTime.ParseExact(x.Tweet.CreatedAt, TW_DATE_FORMAT, CultureInfo.InvariantCulture) >= DateTime.Parse(options.MinDate)).ToList();

                    Console.WriteLine($"Found {_tweetArchive.Count} tweets after {options.MinDate}.");
                }

                if (!string.IsNullOrEmpty(options.MaxDate))
                {
                    _tweetArchive = _tweetArchive.Where(x => DateTime.ParseExact(x.Tweet.CreatedAt, TW_DATE_FORMAT, CultureInfo.InvariantCulture) <= DateTime.Parse(options.MaxDate)).ToList();
                    Console.WriteLine($"Found {_tweetArchive.Count} tweets before {options.MaxDate}.");
                }

                //Filter out replies if the user doesn't want them
                if (!options.ImportReplies)
                {
                    _twitterHandles = options.TwitterHandles.Split(',').ToList();

                    //Find all tweets that are not replies or are replies to one of the user's handles
                    _tweetArchive = _tweetArchive.Where(x => string.IsNullOrEmpty(x.Tweet.InReplyToStatusId) || _twitterHandles.Any(y => x.Tweet.FullText.StartsWith($"@{y}", StringComparison.OrdinalIgnoreCase))).ToList();

                    Console.WriteLine($"Replies filtered out. {_tweetArchive.Count} remaining tweets to import.");
                }

                //Filter out sensitive tweets if the user doesn't want them
                if (options.SkipSensitive)
                {
                    _tweetArchive = _tweetArchive.Where(x => !x.Tweet.PossiblySensitive).ToList();
                    Console.WriteLine($"Sensitive tweets filtered out. {_tweetArchive.Count} remaining tweets to import.");
                }

                //Filter out retweets if the user doesn't want them
                if (options.SkipRetweets)
                {
                    _tweetArchive = _tweetArchive.Where(x => !x.Tweet.FullText.StartsWith("RT @")).ToList();
                    Console.WriteLine($"Retweets filtered out. {_tweetArchive.Count} remaining tweets to import.");
                }

                //If we have a last parsed tweet id, we need to find it in the list and start from the next one
                if (!string.IsNullOrEmpty(_lastParsedTweetId))
                {
                    var lastParsedTweet = _tweetArchive.FirstOrDefault(x => x.Tweet.Id == _lastParsedTweetId);
                    if (lastParsedTweet is not null)
                    {
                        int index = _tweetArchive.IndexOf(lastParsedTweet);
                        _tweetArchive = _tweetArchive[index..];
                    }
                }
            }
        }

        private (string, List<Facet>) ReplaceUrls(string tweetContent, List<Url> urls)
        {
            List<Facet> facets = [];
            if (urls.Count > 0)
            {
                foreach (var url in urls)
                {
                    //Replace url with expanded url
                    tweetContent = tweetContent.Replace(url.UrlUrl.ToString(), url.ExpandedUrl.ToString());

                    // To insert a link, we need to find the start and end of the link text.
                    // This is done as a "ByteSlice."
                    int promptStart = tweetContent.IndexOf(url.ExpandedUrl.ToString(), StringComparison.InvariantCulture);
                    int promptEnd = promptStart + Encoding.Default.GetBytes(url.ExpandedUrl.ToString()).Length;
                    var index = new FacetIndex(promptStart, promptEnd);
                    var link = FacetFeature.CreateLink(url.ExpandedUrl.ToString());
                    facets.Add(new Facet(index, link));
                }
            }

            return (tweetContent, facets);
        }

        public async Task ImportTweetAsync()
        {
            _bskySession = await _bskyProtocol.AuthenticateWithPasswordAsync(options.Username, options.Password, _cancellationToken);

            if (_bskySession is null)
            {
                Console.WriteLine("Failed to authenticate.");
                return;
            }

            Console.WriteLine($"Authenticated. Hello {_bskySession.Handle}!");

            //Order the tweets so the oldest one is first
            _tweetArchive = [.. _tweetArchive!.OrderBy(x => DateTime.ParseExact(x.Tweet.CreatedAt, TW_DATE_FORMAT, CultureInfo.InvariantCulture))];


            foreach (var tweet in _tweetArchive)
            {
                string content = tweet.Tweet.FullText;
                List<Facet> facets = [];
                //Check if the tweet contains a url
                if (tweet.Tweet.Entities.Urls.Count > 0)
                {
                    (string, List<Facet>) x = ReplaceUrls(content, tweet.Tweet.Entities.Urls);
                    content = x.Item1;

                    // If we have facets, we need to add them to the post.
                    facets = x.Item2;
                }

                List<string> imageUrls = new();
                //Check if the tweet contains an image
                if (tweet.Tweet.ExtendedEntities?.Media?.Count > 0)
                {
                    foreach (var media in tweet.Tweet.ExtendedEntities.Media)
                    {
                        imageUrls.Add(media.MediaUrl.ToString());

                        //Remove image url from tweet content
                        content = content.Replace(media.Url.ToString(), "");
                    }
                }

                //Check if the tweet is a reply
                if (!string.IsNullOrEmpty(tweet.Tweet.InReplyToStatusId))
                {
                    //TODO: find the already posted bsky post and reply to it
                }

                await PostToBskyAsync(content, imageUrls, DateTime.ParseExact(tweet.Tweet.CreatedAt, TW_DATE_FORMAT, CultureInfo.InvariantCulture), facets);

                await SaveLastParsedTweet(tweet.Tweet.Id);

                //Sleep for 5 seconds to avoid rate limiting
                Thread.Sleep(TimeSpan.FromSeconds(15));
            }
        }

        private async Task PostToBskyAsync(string textContent, List<string> imageUrls, DateTime createdAt, List<Facet> facets)
        {
            if (imageUrls != null && imageUrls.Count > 0)
            {
                List<FishyFlip.Models.Image> bskyImages = new();
                foreach (var image in imageUrls)
                {
                    //Open the image from the url as a stream
                    Stream stream = await new HttpClient().GetStreamAsync(image);
                    byte[] streamBytes;
                    using (var memoryStream = new MemoryStream())
                    {
                        await stream.CopyToAsync(memoryStream);
                        streamBytes = memoryStream.ToArray();
                    }
                    StreamContent content = new(new MemoryStream(streamBytes));
                    content.Headers.ContentLength = streamBytes.Length;
                    content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

                    // Bluesky uses the content type header for setting the blob type.
                    // As of this writing, it does not verify what kind of blob gets uploaded.
                    // But you should be careful about setting generic types or using the wrong one.
                    // If you do not set a type, it will return an error.
                    content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    Result<UploadBlobResponse> blobResult = await _bskyProtocol.Repo.UploadBlobAsync(content);
                    await blobResult.SwitchAsync(
                           async success =>
                           {
                               // Blob is uploaded.
                               Console.WriteLine($"Uploaded image. Blob: {success.Blob.Type}");
                               // Converts the blob to an image.
                               FishyFlip.Models.Image? image = success.Blob.ToImage();
                               bskyImages.Add(image);
                           },
                           async error =>
                           {
                               Console.WriteLine($"Error: {error.StatusCode} {error.Detail}");
                           }
                    );

                    // Create a post with the images
                    var imagePostResult = await _bskyProtocol.Repo.CreatePostAsync(textContent, facets: [.. facets], createdAt: createdAt, embed: new ImagesEmbed(bskyImages.Select(x => new ImageEmbed(x, "")).ToArray()));
                }
            }
            else
            {
                var postResult = await _bskyProtocol.Repo.CreatePostAsync(textContent, facets: [.. facets], createdAt: createdAt);
            }
        }

        public async Task SaveLastParsedTweet(string tweetId)
        {
            _lastParsedTweetId = tweetId;

            if (_lastParsedTweetId != null)
                await File.WriteAllTextAsync("lastParsedTweetId.txt", _lastParsedTweetId);
        }
    }
}