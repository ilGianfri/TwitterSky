# TwitterSky

Move your Tweets to BlueSky and keep your history alive.

TwitterSky is a simple command line tool to import your tweets to BlueSky. It uses your Twitter archive to import your tweets. 
You can apply various filters to only import tweets you want.

TwitterSky will import your tweets with the original date and time, at the time of writing this, BlueSky respects the original date and time of the tweet and orders them accordingly in the user's profile.

Please read the known issues and FAQ sections before using this tool.

# Prerequisites
~~You need to have dotnet 9.0 runtime installed. You can download it from https://dotnet.microsoft.com/download/dotnet/9.0~~ It's now self-contained, no need to install anything.

# Known issues
- I made this app for myself, I tried to include some useful filters but it may not work for everyone. Code might not be the prettiest.
- I only tested this with my own tweet archive. If you encounter any issues, open an issue or even better, submit a PR!
- Threads might not be imported correctly especially if you get rate limited and close the app. You might end up with a thread that is not complete. PR welcome!
- Rate limiting is not handled properly. If you have a lot of tweets, you might get rate limited and it will not be handled gracefully. Should probably look at the RateLimit headers and retry after a while.

This comes with **absolutely no warranty**.

# How to use
1. Download your Twitter archive from https://twitter.com/settings/your_twitter_data
2. Extract the archive
3. Create an app password from your [BlueSky settings](https://bsky.app/settings/app-passwords)
4. Run TwitterSky from the terminal with the parameters below

# Parameters

| Parameter       | Required | Description                                                                 | Default |
|-----------------|----------|-----------------------------------------------------------------------------|---------|
| `archivePath`   | Yes      | Path to the tweet.js file from Twitter archive.                             |        |
| `minDate`       | No       | Only import tweets posted after this date. Format YYYY-MM-DD.               |         |
| `maxDate`       | No       | Only import tweets posted before this date. Format YYYY-MM-DD.              |         |
| `importReplies` | No       | Import replies. If false, threads will still be imported.                   | False   |
| `skipSensitive` | No       | Skip tweets marked as sensitive.                                            | False   |
| `skipRetweets`  | No       | Skip retweets.                                                              | True    |
| `username`      | Yes      | BlueSky username.                                                           |         |
| `password`      | Yes      | BlueSky app password.                                                       |         |
| `twitterHandles`| Yes      | Comma separated list of Twitter handles you used in the past.               |         |
| `skipWords`     | No       | Comma separated list of words to skip. Tweets containing these words will not be imported. |         |
| `importThreads` | No       | Import threads. If false, only the first tweet in a thread will be imported. |  True       |
| `verbose`       | No       | Print verbose output.                                                       | False   |

Example:
```bash
TwitterSky.exe --archivePath "yourtweetarchive\data\tweets.js" --username "yourbskyusername.bsky.social" --password "yourapppassword" --twitterHandles "handle1,handle2" --minDate "2012-10-10" --skipWords "pizza,hungry,money" --verbose
```

# Thanks to
FishyFlip nuget package for the BlueSky API implementation. https://github.com/drasticactions/FishyFlip

# FAQ

| **Q** | **A** |
|-------|-------|
| Why do I need to provide my BlueSky username and app password? | BlueSky API requires authentication. You can create an app password from your BlueSky settings. No other action is taken with your credentials other than posting your tweets to BlueSky. |
| Why do I need to provide my Twitter archive? | TwitterSky needs your tweets to import them to BlueSky. |
| Why do I need to provide my Twitter handles? | TwitterSky uses them to figure out which tweets are replies to other users and which ones are part of a thread you posted. |
| Are posts imported with the original date? | Yes, TwitterSky uses the original tweet date from the archive. We pass the date to BlueSky, they decide how to handle it. As of now, BlueSky imports them with the original date but shows an "Archived" label on the post. |

## Example of an imported tweet displaying a warning in BlueSky
![image](https://github.com/user-attachments/assets/3d95efd7-cfd5-4c2c-aa78-27817041b11b)

![image](https://github.com/user-attachments/assets/40e54ae2-d170-48e4-9a3a-7ed590d3b8f8)
