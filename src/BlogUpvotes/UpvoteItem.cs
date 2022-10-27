namespace BlogUpvotes
{
    public partial class Upvote
    {
        public class UpvoteItem
        {
            public string ClientIp { get; set; }
            public string Page { get; set; }
            public string Timestamp { get; set; }
            public bool CanVote { get; set; } = false;
            public string PartitionKey { get; set; }
        }
    }
}

