# TwitterSky
 Move your Tweets to BlueSKy and keep your history alive.

# Parameters

## `archivePath`
- **Required**: Yes
- **Description**: Path to the tweet.js file from Twitter archive.

## `minDate`
- **Required**: No
- **Description**: Only import tweets posted after this date. Format YYYY-MM-DD.

## `maxDate`
- **Required**: No
- **Description**: Only import tweets posted before this date. Format YYYY-MM-DD.

## `importReplies`
- **Required**: No
- **Description**: Import replies. If false, threads will still be imported.
- **Hidden**: Yes

## `skipSensitive`
- **Required**: No
- **Description**: Skip tweets marked as sensitive.

## `skipRetweets`
- **Required**: No
- **Description**: Skip retweets.
- **Default**: True

## `username`
- **Required**: Yes
- **Description**: BlueSky username.

## `password`
- **Required**: Yes
- **Description**: BlueSky app password.

## `twitterHandles`
- **Required**: Yes
- **Description**: Comma separated list of Twitter handles you used in the past.

Example:
```bash
TwitterSky.exe --archivePath "yourtweetarchive\data\tweets.js" --username "yourbskyusername.bsky.social" --password "yourapppassword" --twitterHandles "handle1,handle2" --minDate "2012-10-10"
```

# Known issues
- I made this app for myself, I tried to include some useful filters but it may not work for everyone. Code might not be the prettiest.
- I only tested this with my own tweet archive. If you encounter any issues, open an issue or even better, submit a PR!
- Rate limiting is not handled properly. If you have a lot of tweets, you might get rate limited and it will not be handled gracefully.

# Thanks to
FishyFlip nuget package for the BlueSky API implementation. https://github.com/drasticactions/FishyFlip
