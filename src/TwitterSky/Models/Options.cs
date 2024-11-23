using CommandLine;

namespace TwitterSky.Models;

public class Options
{
    [Option("archivePath", Required = true, HelpText = "Path to the tweet.js file from Twitter")]
    public string? ArchivePath { get; set; }

    [Option("minDate", Required = false, HelpText = "Only import tweets posted after this date. Format YYYY-MM-DD")]
    public string? MinDate { get; set; }

    [Option("maxDate", Required = false, HelpText = "Only import tweets posted before this date. Format YYYY-MM-DD")]
    public string? MaxDate { get; set; }

    [Option("importReplies", Required = false, HelpText = "Import replies. If false, threads will still be imported", Hidden = true)]
    public bool ImportReplies { get; set; }

    [Option("importThreads", Required = false, HelpText = "Import threads. If false, only the first tweet in a thread will be imported", Default = true)]
    public bool ImportThreads { get; set; } = true;

    [Option("skipSensitive", Required = false, HelpText = "Skip tweets marked as sensitive")]
    public bool SkipSensitive { get; set; }

    [Option("skipRetweets", Required = false, HelpText = "Skip retweets")]
    public bool SkipRetweets { get; set; } = true;

    [Option("username", Required = true, HelpText = "BlueSky username")]
    public string? Username { get; set; }

    [Option("password", Required = true, HelpText = "BlueSky app password")]
    public string? Password { get; set; }

    [Option("twitterHandles", Required = true, HelpText = "Comma separated list of Twitter handles you used in the past")]
    public string? TwitterHandles { get; set; }

    [Option("skipWords", Required = false, HelpText = "Comma separated list of words to skip. Tweets containing these words will not be imported")]
    public string? SkipWords { get; set; }

    [Option('v', "verbose", Required = false, HelpText = "Prints all messages to standard output")]
    public bool Verbose { get; set; }
}
