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

    [Option("importReplies", Required = false, HelpText = "Import replies", Hidden = true)]
    public bool ImportReplies { get; set; }

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
}
