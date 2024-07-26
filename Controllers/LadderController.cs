using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Utilities.JsonTools;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers;

[Route("leaderboard/ladder"), RequireAuth]
public class LadderController : PlatformController
{
    #pragma warning disable
    private readonly LadderService _ladder;
    private readonly SeasonDefinitionService _seasons;
    private readonly LadderHistoryService _history;
    #pragma warning restore


    public LadderController(LadderService ladder) => _ladder = ladder;

    [HttpPatch, Route("score")]
    public ActionResult AddLadderScore()
    {
        long score = Require<long>("score");

        return Ok(new RumbleJson
        {
            { "player", _ladder.AddScore(Token.AccountId, score) }
        });
    }

    [HttpGet, Route("ranking")]
    public ActionResult GetLadderRanking()
    {
        List<LadderInfo> scores = _ladder.GetRankings(Token.AccountId);
        long cacheExpiration = Math.Max(scores.Min(score => score.CachedUntil), Timestamp.Now);
        
        return Ok(new RumbleJson
        {
            { "players", scores },
            { "cacheExpiration", cacheExpiration }
        });
    }

    [HttpGet, Route("history")]
    public ActionResult GetLadderHistory()
    {
        int count = Math.Max(Optional<int>("count"), 1);
        
        return Ok(new RumbleJson
        {
            { "history", _history.GetHistoricalSeasons(Token.AccountId, count) }
        });
    }
}