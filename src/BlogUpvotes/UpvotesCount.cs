namespace BlogUpvotes
{
    public partial class Upvote
    {
        public class UpvotesCount
        {

#pragma warning disable IDE1006 // Naming Styles
            public string id { get; set; } //must be lowercase for the actual document id
#pragma warning restore IDE1006 // Naming Styles
            public string PartitionKey { get; set; }
            public int Count { get; set; }
        }
    }
}

