namespace Rumble.Platform.LeaderboardService.Models;

public struct PopulationStats
{
    public long ActivePlayers { get; set; }
    public double MeanScore { get; set; }
    public double Variance { get; set; }
    public double StandardDeviation { get; set; }
    public double SumOfSquares { get; set; }
    public long TotalScore { get; set; }
}