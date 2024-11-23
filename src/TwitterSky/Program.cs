using CommandLine;
using TwitterSky;
using TwitterSky.Models;

Options options = new();

Parser.Default.ParseArguments<Options>(args).WithParsed(parsed => options = parsed);

TweetImporter importer = new(options);

// Handle ctrl + c gracefully
Console.CancelKeyPress += (sender, eventArgs) =>
{
    importer.CancelImport();
    Environment.Exit(0);
};

await importer.ParseJson();
await importer.ImportTweetAsync();