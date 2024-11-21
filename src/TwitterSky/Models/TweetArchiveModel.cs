using System.Text.Json.Serialization;

namespace TwitterSky.Models
{
    public partial class TweetArchiveModel
    {
        [JsonPropertyName("tweet")]
        public Tweet Tweet { get; set; }
    }

    public partial class Tweet
    {

        [JsonPropertyName("retweeted")]
        public bool Retweeted { get; set; }

        [JsonPropertyName("entities")]
        public Entities? Entities { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("in_reply_to_status_id")]
        public string InReplyToStatusId { get; set; }

        [JsonPropertyName("possibly_sensitive")]
        public bool PossiblySensitive { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; }

        [JsonPropertyName("full_text")]
        public string FullText { get; set; }

        [JsonPropertyName("extended_entities")]
        public ExtendedEntities ExtendedEntities { get; set; }
    }

    public partial class Entities
    {
        [JsonPropertyName("hashtags")]
        public List<object> Hashtags { get; set; }

        [JsonPropertyName("user_mentions")]
        public List<object> UserMentions { get; set; }

        [JsonPropertyName("urls")]
        public List<Url> Urls { get; set; }
    }

    public partial class ExtendedEntities
    {
        [JsonPropertyName("media")]
        public List<Media> Media { get; set; }
    }

    public partial class Media
    {
        [JsonPropertyName("expanded_url")]
        public Uri ExpandedUrl { get; set; }

        [JsonPropertyName("url")]
        public Uri Url { get; set; }

        [JsonPropertyName("media_url")]
        public Uri MediaUrl { get; set; }

        [JsonPropertyName("id_str")]
        public string IdStr { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("media_url_https")]
        public Uri MediaUrlHttps { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("display_url")]
        public string DisplayUrl { get; set; }
    }

    public partial class Url
    {
        [JsonPropertyName("url")]
        public Uri UrlUrl { get; set; }

        [JsonPropertyName("expanded_url")]
        public Uri ExpandedUrl { get; set; }

        [JsonPropertyName("display_url")]
        public string DisplayUrl { get; set; }
    }
}
