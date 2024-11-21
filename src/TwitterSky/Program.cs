using CommandLine;
using System.Xml.Linq;
using TwitterSky;
using TwitterSky.Models;

var options = new TwitterSky.Models.Options();

Parser.Default.ParseArguments<TwitterSky.Models.Options>(args).WithParsed(parsed => options = parsed);

TweetImporter importer = new(options);

await importer.ParseJson();
await importer.ImportTweetAsync();