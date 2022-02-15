using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Reward : PlatformDataModel
	{
		public int Tier { get; set; }
		public Item[] Contents { get; set; } // TODO: GenericData?
		public int MinimumRank { get; set; }
		public int MinimumPercentile { get; set; }
		public long TimeAwarded { get; set; }
		public string LeaderboardId { get; set; }
		internal Status SentStatus { get; set; }
			
		internal enum Status { NotSent, IsSending, Sent }
	}
}