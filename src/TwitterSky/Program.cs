using CommandLine;
using TwitterSky;
using TwitterSky.Models;

Options options = new();

Parser.Default.ParseArguments<Options>(args).WithParsed(parsed => options = parsed);

// Handle ctrl + c gracefully
Console.CancelKeyPress += (sender, eventArgs) =>
{
    Console.WriteLine("Import cancelled, finishing up and exiting...");
    Environment.Exit(0);
};

TweetImporter importer = new(options);

await importer.ParseJson();
await importer.ImportTweetAsync();