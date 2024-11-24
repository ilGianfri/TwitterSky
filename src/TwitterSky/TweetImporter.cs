using FishyFlip;
using FishyFlip.Models;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using TwitterSky.Models;
using TwitterSky.Utilities;

namespace TwitterSky
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TweetImporter"/> class.
    /// </summary>
    /// <param name="_options">The _options for importing tweets.</param>
    public class TweetImporter
    {
        private List<TweetArchiveModel>? _tweetArchive;
        private string _lastParsedTweetId = string.Empty;
        private readonly CancellationTokenSource _cts = new();
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
        private Dictionary<string, CreatePostResponse?>? _tweetIdToBskyId = [];

        private readonly Options _options;
        private readonly CmdUtil _cmd;

        public TweetImporter(Options options)
        {
            _options = options;
            _cmd = new CmdUtil(options.Verbose);
        }

        /// <summary>
        /// Parses the JSON file containing the tweet archive.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the tweet.js file is not found.</exception>
        public async Task ParseJsonAsync()
        {
            await Task.Run(() =>
            {
                if (!string.IsNullOrEmpty(_options.ArchivePath))
                {
                    _cmd.PrintInfo($"Parsing {_options.ArchivePath}", true);

                    // If we receive a path to the main folder, we need to append /data/tweet.js otherwise we just use the path
                    if (Directory.Exists(_options.ArchivePath))
                    {
                        _options.ArchivePath = Path.Combine(_options.ArchivePath, "data", "tweet.js");

                        _cmd.PrintInfo($"Path to tweet.js file: {_options.ArchivePath}", true);
                    }

                    // Make sure the file exists
                    if (!File.Exists(_options.ArchivePath))
                    {
                        _cmd.PrintError($"File {_options.ArchivePath} not found.");
                        return;
                    }

                    string json = File.ReadAllText(_options.ArchivePath);

                    _cmd.PrintInfo("Parsing JSON...", true);

                    // Remove the window.YTD.tweets.part0 = at the beginning of the file
                    int start = json.IndexOf('[');
                    json = json[start..];

                    _tweetArchive = JsonSerializer.Deserialize<List<TweetArchiveModel>>(json);

                    if (_tweetArchive is null)
                    {
                        _cmd.PrintError("Failed to parse JSON.");
                        return;
                    }

                    _cmd.PrintSuccess("JSON parsed successfully.", true);
                    _cmd.PrintInfo($"Found {_tweetArchive!.Count} tweets in the archive.");
                    _cmd.PrintInfo("Filtering tweets...");

                    int initialCount = _tweetArchive.Count;

                    // Filter out tweets that are not in the date range
                    if (!string.IsNullOrEmpty(_options.MinDate))
                    {
                        _cmd.PrintInfo($"Min date {_options.MinDate} has been specified.", true);
                        _tweetArchive = _tweetArchive.Where(x => DateTime.ParseExact(x.Tweet.CreatedAt, TW_DATE_FORMAT, CultureInfo.InvariantCulture) >= DateTime.Parse(_options.MinDate)).ToList();
                        _cmd.PrintInfo($"Removed {initialCount - _tweetArchive.Count} tweets before {_options.MinDate}.");
                        initialCount = _tweetArchive.Count;
                    }

                    if (!string.IsNullOrEmpty(_options.MaxDate))
                    {
                        _cmd.PrintInfo($"Man date {_options.MinDate} has been specified.", true);
                        _tweetArchive = _tweetArchive.Where(x => DateTime.ParseExact(x.Tweet.CreatedAt, TW_DATE_FORMAT, CultureInfo.InvariantCulture) <= DateTime.Parse(_options.MaxDate)).ToList();
                        _cmd.PrintInfo($"Removed {initialCount - _tweetArchive.Count} tweets after {_options.MaxDate}.");
                        initialCount = _tweetArchive.Count;
                    }

                    // Find tweets that are replies to other tweets in the archive
                    if (_options.ImportThreads)
                    {
                        _cmd.PrintInfo("Finding replies to other tweets in the archive... (threads you posted, no not the meta app, replies to yourself)");

                        if (File.Exists("tweetIdToBskyId.json"))
                        {
                            _cmd.PrintInfo("Reading tweetIdToBskyId.json...", true);
                            _tweetIdToBskyId = JsonSerializer.Deserialize<Dictionary<string, CreatePostResponse?>>(File.ReadAllText("tweetIdToBskyId.json"));
                            _cmd.PrintInfo($"Found {_tweetIdToBskyId.Count} replies to other tweets in the archive.");
                        }
                        else
                        {
                            // Find all tweets that are replies to other tweets in the archive
                            List<TweetArchiveModel> replies = _tweetArchive.Where(x => !string.IsNullOrEmpty(x.Tweet.InReplyToStatusId)).ToList();
                            foreach (TweetArchiveModel reply in replies)
                            {
                                // Find the parent tweet in the archive
                                TweetArchiveModel? parentTweet = _tweetArchive.FirstOrDefault(x => x.Tweet.Id == reply.Tweet.InReplyToStatusId);
                                if (parentTweet is not null)
                                {
                                    // Add the reply to dictionary to keep track of them
                                    _cmd.PrintInfo($"Found reply to tweet {reply.Tweet.InReplyToStatusId} in the archive.", true);
                                    _tweetIdToBskyId.TryAdd(reply.Tweet.Id, null);
                                }
                            }
                            _cmd.PrintInfo($"Found {_tweetIdToBskyId.Count} replies to other tweets in the archive.");

                            // Save this dictionary to a file so we can keep track of the replies we posted if we need to resume the import
                            File.WriteAllText("tweetIdToBskyId.json", JsonSerializer.Serialize(_tweetIdToBskyId));
                        }
                    }

                    // Filter out replies if the user doesn't want them
                    if (!_options.ImportReplies)
                    {
                        _cmd.PrintWarning("Importing replies has been disabled.", true);

                        _twitterHandles = [.. _options.TwitterHandles!.Split(',')];

                        // Find all tweets that are not replies or are replies to one of the user's handles
                        _tweetArchive = _tweetArchive.Where(x => string.IsNullOrEmpty(x.Tweet.InReplyToStatusId) // Not a reply
                        //|| _twitterHandles.Any(y => x.Tweet.FullText.StartsWith($"@{y}", StringComparison.OrdinalIgnoreCase)) // Reply to one of the user's handles
                        && (x.Tweet.InReplyToScreenName is null || _twitterHandles.Contains(x.Tweet.InReplyToScreenName))).ToList(); // Reply to one of the user's handles

                        _cmd.PrintInfo($"Removed {initialCount - _tweetArchive.Count} replies.");
                        initialCount = _tweetArchive.Count;
                    }

                    // Filter out sensitive tweets if the user doesn't want them
                    if (_options.SkipSensitive)
                    {
                        _cmd.PrintInfo("Skipping sensitive tweets.", true);
                        _tweetArchive = _tweetArchive.Where(x => !x.Tweet.PossiblySensitive).ToList();
                        _cmd.PrintInfo($"Removed {initialCount - _tweetArchive.Count} sensitive tweets.");
                        initialCount = _tweetArchive.Count;
                    }

                    // Filter out retweets if the user doesn't want them
                    if (_options.SkipRetweets)
                    {
                        _cmd.PrintInfo("Skipping retweets.", true);
                        _tweetArchive = _tweetArchive.Where(x => !x.Tweet.FullText.StartsWith("RT @")).ToList();
                        _cmd.PrintInfo($"Removed {initialCount - _tweetArchive.Count} retweets.");
                        initialCount = _tweetArchive.Count;
                    }

                    if (!string.IsNullOrEmpty(_options.SkipWords))
                    {
                        _cmd.PrintInfo("Removing tweets containing skip words...", true);
                        List<string> skipWords = [.. _options.SkipWords.Split(',')];

                        // Remove tweets containing skip words (split by space to avoid partial matches)
                        _tweetArchive = _tweetArchive.Where(x => !skipWords.Any(y => x.Tweet.FullText.Replace("?", " ").Replace("!", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(y, StringComparer.OrdinalIgnoreCase))).ToList();
                        _cmd.PrintInfo($"Removed {initialCount - _tweetArchive.Count} tweets containing skip words.");
                    }

                    //Read the last parsed tweet id from a file
                    if (File.Exists("lastParsedTweetId.txt"))
                    {
                        _cmd.PrintInfo("Reading last parsed tweet id... It seems this is not our first rodeo.", true);
                        _lastParsedTweetId = File.ReadAllText("lastParsedTweetId.txt");

                        _cmd.PrintInfo($"Last parsed tweet id: {_lastParsedTweetId}");
                    }

                    // If we have a last parsed tweet id, we need to find it in the list and start from the next one
                    if (!string.IsNullOrEmpty(_lastParsedTweetId))
                    {
                        _cmd.PrintInfo("Removing tweets before the last parsed tweet...", true);
                        // Once we find the last parsed tweet, we need to remove it and all the previous ones
                        _tweetArchive = _tweetArchive.Where(x => Convert.ToUInt64(x.Tweet.Id) > Convert.ToUInt64(_lastParsedTweetId) || _tweetIdToBskyId.ContainsKey(x.Tweet.Id)).ToList();
                        _cmd.PrintInfo($"Removed {initialCount - _tweetArchive.Count} tweets before the last parsed tweet.");
                        initialCount = _tweetArchive.Count;
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
            if (urls == null || urls.Count == 0)
            {
                _cmd.PrintInfo("No URLs to parse.", true);
                return (tweetContent, []);
            }

            _cmd.PrintInfo("Looking for URLs to replace...", true);

            List<Facet> facets = [];
            if (urls.Count > 0)
            {
                foreach (Url url in urls)
                {
                    // Replace url with expanded url
                    tweetContent = tweetContent.Replace(url.UrlUrl.ToString(), url.ExpandedUrl.ToString());
                }

                facets.AddRange(Facet.ForUris(tweetContent));

                _cmd.PrintInfo($"Replaced {urls.Count} URLs.");
            }

            return (tweetContent, facets);
        }

        /// <summary>
        /// Gets the hashtags from the tweet content and creates facets for them.
        /// </summary>
        /// <param name="tweetContent">The content of the tweet.</param>
        /// <param name="hashtags">List of hashtags to replace</param>
        /// <returns>A list of facets</returns>
        private List<Facet> GetHashtags(string tweetContent, List<Hashtag>? hashtags)
        {
            if (hashtags == null || hashtags.Count == 0)
            {
                _cmd.PrintInfo("No hashtags to parse.", true);
                return [];
            }

            List<Facet> facets = [];
            _cmd.PrintInfo($"Found {hashtags.Count} hashtags.");
            facets.AddRange(Facet.ForHashtags(tweetContent));

            return facets;
        }

        /// <summary>
        /// Requests the cancellation of the import operation.
        /// </summary>
        public void CancelImport()
        {
            _cmd.PrintWarning("Cancelling import...", true);
            _cts.Cancel();
        }

        /// <summary>
        /// Imports tweets asynchronously.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task ImportTweetAsync()
        {
            _bskySession = await _bskyProtocol.AuthenticateWithPasswordAsync(_options.Username!, _options.Password!, _cts.Token);

            if (_bskySession is null)
            {
                _cmd.PrintError("Failed to authenticate. Please verify your credentials.");
                return;
            }

            _cmd.PrintSuccess($"Authenticated. Hello {_bskySession.Handle}!");

            // Order the tweets so the oldest one is first
            _tweetArchive = [.. _tweetArchive!.OrderBy(x => DateTime.ParseExact(x.Tweet.CreatedAt, TW_DATE_FORMAT, CultureInfo.InvariantCulture))];

            foreach (TweetArchiveModel tweet in _tweetArchive)
            {
                if (_cts.Token.IsCancellationRequested)
                {
                    _cmd.PrintWarning("Import cancelled.");
                    break;
                }

                string content = tweet.Tweet.FullText;

                _cmd.PrintInfo($"Importing tweet {tweet.Tweet.Id}: {content}", true);

                List<Facet> facets = [];
                // Check if the tweet contains a url
                if (tweet.Tweet.Entities?.Urls.Count > 0)
                {
                    (string, List<Facet>) x = ReplaceUrls(content, tweet.Tweet.Entities.Urls);
                    content = x.Item1;

                    // If we have facets, we need to add them to the post.
                    facets = x.Item2;
                }

                //continue;

                List<string> imageUrls = [];
                // Check if the tweet contains an image
                if (tweet.Tweet.ExtendedEntities?.Media?.Count > 0)
                {
                    _cmd.PrintInfo($"Tweet contains {tweet.Tweet.ExtendedEntities?.Media?.Count} images.", true);

                    foreach (Media media in tweet.Tweet.ExtendedEntities!.Media)
                    {
                        imageUrls.Add(media.MediaUrl.ToString());

                        // Remove image url from tweet content
                        content = content.Replace(media.Url.ToString(), "");
                    }
                }

                CreatePostResponse? parentPostId = null;
                // Check if the tweet is a reply to another post from the same user
                if (!string.IsNullOrEmpty(tweet.Tweet.InReplyToStatusId) && _tweetIdToBskyId.TryGetValue(tweet.Tweet.InReplyToStatusId, out CreatePostResponse? value))
                {
                    // If the tweet is a reply to another tweet in the archive, we need to add the reply to the thread
                    _cmd.PrintInfo($"Tweet is a reply to another tweet in the archive: {tweet.Tweet.InReplyToStatusId}", true);
                    parentPostId = value;
                }

                // Get the hashtags from the tweet content
                facets.AddRange(GetHashtags(content, tweet.Tweet?.Entities?.Hashtags));

                await PostToBskyAsync(tweet.Tweet!.Id, content, imageUrls, DateTime.ParseExact(tweet.Tweet.CreatedAt, TW_DATE_FORMAT, CultureInfo.InvariantCulture), facets, parentPostId);

                await SaveLastParsedTweet(tweet.Tweet.Id);


                //if (_postedTweets >= 1650)
                //{
                //    _cmd.PrintWarning("Rate limit reached. Sleeping for 60 minutes...");
                //    await Task.Delay(TimeSpan.FromMinutes(60));
                //    _postedTweets = 0;
                //}
            }

            _cmd.PrintSuccess("Import completed.");
        }

        /// <summary>
        /// Posts the tweet content to BlueSky asynchronously.
        /// </summary>
        /// <param name="textContent">The text content of the tweet.</param>
        /// <param name="imageUrls">The list of image URLs in the tweet.</param>
        /// <param name="createdAt">The creation date of the tweet.</param>
        /// <param name="facets">The list of facets for the tweet.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task PostToBskyAsync(string tweetId, string textContent, List<string> imageUrls, DateTime createdAt, List<Facet> facets, CreatePostResponse? inReplyTo)
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
                    _cmd.PrintInfo($"Uploading image...", true);
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
                               // Converts the blob to an image.
                               Image? image = success.Blob.ToImage();
                               bskyImages.Add(image);

                               _cmd.PrintSuccess($"Image uploaded.");
                           },
                           async error =>
                           {
                               _cmd.PrintError($"{error.StatusCode} {error.Detail}");

                               if (error.StatusCode == 429)
                               {
                                   await HandleRateLimit();

                                   // Retry the post
                                   await PostToBskyAsync(tweetId, textContent, imageUrls, createdAt, facets, inReplyTo);
                               }
                           }
                    );

                    // Create a post with the images
                    postResult = await _bskyProtocol.Repo.CreatePostAsync(textContent, facets: [.. facets], createdAt: createdAt, reply: reply, embed: new ImagesEmbed(bskyImages.Select(x => new ImageEmbed(x, "")).ToArray()));

                    _cmd.PrintSuccess($"{createdAt.ToShortDateString()}: {textContent} with {imageUrls.Count} images");
                }
            }
            else
            {
                postResult = await _bskyProtocol.Repo.CreatePostAsync(textContent, facets: [.. facets], createdAt: createdAt, reply: reply);

                _cmd.PrintSuccess($"{createdAt.ToShortDateString()}: {textContent}");
            }

            if (postResult?.Value is ATError error)
            {
                _cmd.PrintError($"Failed to post. Reason: {error.Detail}");

                if (error.StatusCode == 429)
                {
                    await HandleRateLimit();

                    // Retry the post
                    await PostToBskyAsync(tweetId, textContent, imageUrls, createdAt, facets, inReplyTo);
                }
                return;
            }

            // If it's a thread, we need to update the tweet id to bsky id mapping
            if (_tweetIdToBskyId.ContainsKey(tweetId) && postResult?.Value is CreatePostResponse postResponse)
            {
                _tweetIdToBskyId[tweetId] = postResponse;
            }

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
            _cmd.PrintInfo("Saving last parsed tweet id...", true);

            _lastParsedTweetId = tweetId;

            if (_lastParsedTweetId != null)
                await File.WriteAllTextAsync("lastParsedTweetId.txt", _lastParsedTweetId);

            File.WriteAllText("tweetIdToBskyId.json", JsonSerializer.Serialize(_tweetIdToBskyId));

            _cmd.PrintInfo($"Last parsed tweet id saved: {_lastParsedTweetId}");
        }

        /// <summary>
        /// When the rate limit is reached, this method will handle the wait time.
        /// </summary>
        private async Task HandleRateLimit()
        {
            // An account may create at most 1,666 records per hour and 11,666 records per day
            // https://docs.bsky.app/docs/advanced-guides/rate-limits
            // Absolutely untested code, it won't consider if the user uses the account from other places while importing
            // It will just wait 30 minutes if the limit is reached but I don't think it's enough

            _cmd.PrintWarning("Rate limit reached. Sleeping for 30 minutes...");
            await Task.Delay(TimeSpan.FromMinutes(30));

            _postedTweets = 0;

            _cmd.PrintInfo("Resuming import...");
        }

        internal async Task AskPostThankYouAsync()
        {
            string message = "I've imported my tweets to BlueSky using #TwitterSky! https://github.com/ilGianfri/TwitterSky/";

            _cmd.PrintInfo("Do you want your followers to know you imported your tweets to BlueSky? (Y/N)");
            _cmd.PrintInfo($"This will post the following message: \"{message}\"");

            string? response = Console.ReadLine();

            if (response.StartsWith("y", StringComparison.OrdinalIgnoreCase))
            {
                List<Facet> facets = [];
                facets.AddRange(Facet.ForHashtags(message));
                facets.AddRange(Facet.ForUris(message));

                await PostToBskyAsync("0", message, [], DateTime.Now, facets, null);
            }
        }
    }
}