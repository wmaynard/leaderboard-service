using System;
using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers;

[Route("leaderboard/ladder"), RequireAuth]
public class LadderController : PlatformController
{
    #pragma warning disable
    private readonly LadderService _ladder;
    private readonly LadderDefinitionService _seasons;
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
    public ActionResult GetLadderRanking() => Ok(new RumbleJson
    {
        { "players", _ladder.GetRankings(Token.AccountId) }
    });

    [HttpGet, Route("history")]
    public ActionResult GetLadderHistory() => Ok(_history.GetHistoricalSeasons(Token.AccountId));
}