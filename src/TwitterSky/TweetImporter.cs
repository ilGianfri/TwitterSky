using FishyFlip;
using FishyFlip.Models;
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
        private CancellationTokenSource _cts = new ();
        private List<string> _twitterHandles = [];

        private readonly ATProtocol _bskyProtocol = new ATProtocolBuilder()
            .WithInstanceUrl(new Uri("https://bsky.social"))
            .EnableAutoRenewSession(true)
            .Build();

        private Session? _bskySession;

        /// <summary>
        /// The number of tweets that have been posted to BlueSky, used for rate limiting.
        /// </summary>
        private int _postedTweets = 0;

        /// <summary>
        /// Twitter datetimes are in this format
        /// </summary>
        private const string TW_DATE_FORMAT = "ddd MMM dd HH:mm:ss zzz yyyy";

        /// <summary>
        /// Dictionary to map tweet ids to bsky ids (for replies threads)
        /// </summary>
        private readonly Dictionary<string, CreatePostResponse?> _tweetIdToBskyId = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="TweetImporter"/> class.
        /// </summary>
        /// <param name="options">The options for importing tweets.</param>
        public TweetImporter(Options options)
        {
            this.options = options;
        }

        /// <summary>
        /// Parses the JSON file containing the tweet archive.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the tweet.js file is not found.</exception>
        public async Task ParseJson()
        {
            await Task.Run(() =>
            {
                if (!string.IsNullOrEmpty(options.ArchivePath))
                {
                    // If we receive a path to the main folder, we need to append /data/tweet.js otherwise we just use the path
                    if (Directory.Exists(options.ArchivePath))
                    {
                        options.ArchivePath = Path.Combine(options.ArchivePath, "data", "tweet.js");
                    }

                    // Make sure the file exists
                    if (!File.Exists(options.ArchivePath))
                    {
                        throw new FileNotFoundException("tweet.js file not found");
                    }

                    string json = File.ReadAllText(options.ArchivePath);

                    // Remove the window.YTD.tweets.part0 = at the beginning of the file
                    int start = json.IndexOf('[');
                    json = json[start..];

                    _tweetArchive = JsonSerializer.Deserialize<List<TweetArchiveModel>>(json);

                    Console.WriteLine($"Found {_tweetArchive!.Count} tweets in the archive. Applying filters...");

                    // Filter out tweets that are not in the date range
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

                    // Filter out replies if the user doesn't want them
                    if (!options.ImportReplies)
                    {
                        _twitterHandles = [.. options.TwitterHandles!.Split(',')];

                        // Find all tweets that are not replies or are replies to one of the user's handles
                        _tweetArchive = _tweetArchive.Where(x => string.IsNullOrEmpty(x.Tweet.InReplyToStatusId) || _twitterHandles.Any(y => x.Tweet.FullText.StartsWith($"@{y}", StringComparison.OrdinalIgnoreCase))).ToList();

                        Console.WriteLine($"Replies filtered out. {_tweetArchive.Count} remaining tweets to import.");
                    }

                    // Filter out sensitive tweets if the user doesn't want them
                    if (options.SkipSensitive)
                    {
                        _tweetArchive = _tweetArchive.Where(x => !x.Tweet.PossiblySensitive).ToList();
                        Console.WriteLine($"Sensitive tweets filtered out. {_tweetArchive.Count} remaining tweets to import.");
                    }

                    // Filter out retweets if the user doesn't want them
                    if (options.SkipRetweets)
                    {
                        _tweetArchive = _tweetArchive.Where(x => !x.Tweet.FullText.StartsWith("RT @")).ToList();
                        Console.WriteLine($"Retweets filtered out. {_tweetArchive.Count} remaining tweets to import.");
                    }

                    //Read the last parsed tweet id from a file
                    if (File.Exists("lastParsedTweetId.txt"))
                    {
                        _lastParsedTweetId = File.ReadAllText("lastParsedTweetId.txt");
                    }

                    // If we have a last parsed tweet id, we need to find it in the list and start from the next one
                    if (!string.IsNullOrEmpty(_lastParsedTweetId))
                    {
                        // Once we find the last parsed tweet, we need to remove it and all the previous ones
                        _tweetArchive = _tweetArchive.Where(x => Convert.ToUInt64(x.Tweet.Id) > Convert.ToUInt64(_lastParsedTweetId)).ToList();

                        Console.WriteLine($"Found last parsed tweet. {_tweetArchive.Count} remaining tweets to import.");
                    }

                    // Find tweets that are replies to other tweets in the archive
                    if (options.ImportReplies)
                    {
                        // Find all tweets that are replies to other tweets in the archive
                        List<TweetArchiveModel> replies = _tweetArchive.Where(x => !string.IsNullOrEmpty(x.Tweet.InReplyToStatusId)).ToList();
                        foreach (TweetArchiveModel reply in replies)
                        {
                            TweetArchiveModel? parentTweet = _tweetArchive.FirstOrDefault(x => x.Tweet.Id == reply.Tweet.InReplyToStatusId);
                            if (parentTweet is not null)
                            {
                                _tweetIdToBskyId.TryAdd(reply.Tweet.Id, null);
                            }
                        }
                        Console.WriteLine($"Found {_tweetIdToBskyId.Count} replies to other tweets in the archive.");
                    }
                }
            });
        }

        /// <summary>
        /// Replaces URLs in the tweet content with their expanded versions and creates facets for them.
        /// </summary>
        /// <param name="tweetContent">The content of the tweet.</param>
        /// <param name="urls">The list of URLs to replace.</param>
        /// <returns>A tuple containing the updated tweet content and the list of facets.</returns>
        private (string, List<Facet>) ReplaceUrls(string tweetContent, List<Url> urls)
        {
            List<Facet> facets = [];
            if (urls.Count > 0)
            {
                foreach (Url url in urls)
                {
                    // Replace url with expanded url
                    tweetContent = tweetContent.Replace(url.UrlUrl.ToString(), url.ExpandedUrl.ToString());

                    // To insert a link, we need to find the start and end of the link text.
                    // This is done as a "ByteSlice."
                    int promptStart = tweetContent.IndexOf(url.ExpandedUrl.ToString(), StringComparison.InvariantCulture);
                    int promptEnd = promptStart + Encoding.Default.GetBytes(url.ExpandedUrl.ToString()).Length;
                    FacetIndex index = new(promptStart, promptEnd);
                    FacetFeature link = FacetFeature.CreateLink(url.ExpandedUrl.ToString());
                    facets.Add(new Facet(index, link));
                }
            }

            return (tweetContent, facets);
        }

        public void CancelImport()
        {
            _cts.Cancel();
        }

        /// <summary>
        /// Imports tweets asynchronously.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task ImportTweetAsync()
        {
            _bskySession = await _bskyProtocol.AuthenticateWithPasswordAsync(options.Username!, options.Password!, _cts.Token);

            if (_bskySession is null)
            {
                Console.WriteLine("Failed to authenticate.");
                return;
            }

            Console.WriteLine($"Authenticated. Hello {_bskySession.Handle}!");

            // Order the tweets so the oldest one is first
            _tweetArchive = [.. _tweetArchive!.OrderBy(x => DateTime.ParseExact(x.Tweet.CreatedAt, TW_DATE_FORMAT, CultureInfo.InvariantCulture))];

            foreach (TweetArchiveModel tweet in _tweetArchive)
            {
                if (_cts.Token.IsCancellationRequested)
                {
                    Console.WriteLine("Import cancelled.");
                    break;
                }

                string content = tweet.Tweet.FullText;
                List<Facet> facets = [];
                // Check if the tweet contains a url
                if (tweet.Tweet.Entities?.Urls.Count > 0)
                {
                    (string, List<Facet>) x = ReplaceUrls(content, tweet.Tweet.Entities.Urls);
                    content = x.Item1;

                    // If we have facets, we need to add them to the post.
                    facets = x.Item2;
                }

                List<string> imageUrls = [];
                // Check if the tweet contains an image
                if (tweet.Tweet.ExtendedEntities?.Media?.Count > 0)
                {
                    foreach (Media media in tweet.Tweet.ExtendedEntities.Media)
                    {
                        imageUrls.Add(media.MediaUrl.ToString());

                        // Remove image url from tweet content
                        content = content.Replace(media.Url.ToString(), "");
                    }
                }

                CreatePostResponse? parentPostId = null;
                // Check if the tweet is a reply to another post from the same user
                if (!string.IsNullOrEmpty(tweet.Tweet.InReplyToStatusId) && _tweetIdToBskyId.ContainsKey(tweet.Tweet.InReplyToStatusId))
                {
                    // If the tweet is a reply to another tweet in the archive, we need to add the reply to the thread
                    parentPostId = _tweetIdToBskyId[tweet.Tweet.InReplyToStatusId];
                }

                await PostToBskyAsync(tweet.Tweet.Id, content, imageUrls, DateTime.ParseExact(tweet.Tweet.CreatedAt, TW_DATE_FORMAT, CultureInfo.InvariantCulture), facets, parentPostId);

                await SaveLastParsedTweet(tweet.Tweet.Id);

                // An account may create at most 1,666 records per hour and 11,666 records per day
                // https://docs.bsky.app/docs/advanced-guides/rate-limits
                // Absolutely untested code, it won't consider if the user uses the account from other places while importing
                // It will just wait 15 minutes if the limit is reached but I don't think it's enough
                if (_postedTweets >= 1665)
                {
                    Console.WriteLine("Rate limit reached. Sleeping for 15 minutes...");
                    await Task.Delay(TimeSpan.FromMinutes(15));
                    _postedTweets = 0;
                }
            }
        }

        /// <summary>
        /// Posts the tweet content to BlueSky asynchronously.
        /// </summary>
        /// <param name="textContent">The text content of the tweet.</param>
        /// <param name="imageUrls">The list of image URLs in the tweet.</param>
        /// <param name="createdAt">The creation date of the tweet.</param>
        /// <param name="facets">The list of facets for the tweet.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task PostToBskyAsync(string tweetId, string textContent, List<string> imageUrls, DateTime createdAt, List<Facet> facets, CreatePostResponse inReplyTo)
        {
            Reply? reply = null;
            if (inReplyTo != null)
            {
                reply = new Reply(new ReplyRef(inReplyTo!.Cid!, inReplyTo!.Uri!), new ReplyRef(inReplyTo!.Cid!, inReplyTo.Uri!));
            }

            Result<CreatePostResponse>? postResult = null;
            if (imageUrls != null && imageUrls.Count > 0)
            {
                List<FishyFlip.Models.Image> bskyImages = [];
                foreach (string image in imageUrls)
                {
                    // Open the image from the url as a stream
                    Stream stream = await new HttpClient().GetStreamAsync(image);
                    byte[] streamBytes;
                    using (MemoryStream memoryStream = new())
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
                               Console.WriteLine($"Uploading image...");
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
                    postResult = await _bskyProtocol.Repo.CreatePostAsync(textContent, facets: [.. facets], createdAt: createdAt, reply: reply, embed: new ImagesEmbed(bskyImages.Select(x => new ImageEmbed(x, "")).ToArray()));
                }
            }
            else
            {
                postResult = await _bskyProtocol.Repo.CreatePostAsync(textContent, facets: [.. facets], createdAt: createdAt, reply: reply);
            }

            if (_tweetIdToBskyId.ContainsKey(tweetId) && postResult.Value is CreatePostResponse postResponse)
            {
                _tweetIdToBskyId[tweetId] = postResponse;
            }

            Console.WriteLine($"{createdAt.ToShortDateString()}: {textContent}");

            // Increment the number of posted tweets
            _postedTweets++;
        }

        /// <summary>
        /// Saves the ID of the last parsed tweet to a file.
        /// </summary>
        /// <param name="tweetId">The ID of the last parsed tweet.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task SaveLastParsedTweet(string tweetId)
        {
            _lastParsedTweetId = tweetId;

            if (_lastParsedTweetId != null)
                await File.WriteAllTextAsync("lastParsedTweetId.txt", _lastParsedTweetId);
        }
    }
}