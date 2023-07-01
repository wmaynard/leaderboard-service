# Ladder Features

Leaderboards, as we have designed them, favor engagement but not necessarily skill.  Design requested a feature specific to PvP where scores behave differently from standard leaderboards, and also defined a rewards flow that's more appropriate for the game server to honor instead.  Consequently, the Ladder was born.  The primary goal of the API design here is to keep the requirements as simple as possible; if we decide to later expand this functionality into a larger feature at a later date, we will evaluate it then.

## A Ladder is not a Leaderboard

While this is a new feature of the Leaderboard service, it's important to note that there's no crossover between what we understand a Leaderboard to be and what the ladder is.  A Leaderboard by its very nature has complex rules, rewards, sharding, tiers.... but the Ladder has none of this.

The Ladder is quite naive.  It's merely a point-tracking system with custom scoring rules and an easy ability to query large numbers of records.  Currently, we only have plans to support exactly one Ladder - for PvP.

## Glossary

| Term   | Definition                                                                                                                                                                                                        |
|:-------|:------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Cache  | Queries that are expensive to run frequently will be stored for an amount of time to avoid taxing the database.                                                                                                   |
| Reset  | An event where player scores are capped to a maximum score.                                                                                                                                                       |
| Season | Defined by game data; as far as Platform is concerned, these are arbitrary timestamps.  When one of them passes, some event has to happen.  In the case of the Ladder, scores above the max score will be capped. |
| Step   | A threshold that caps point gains / losses.  You must be at a step's value exactly before continuing past it.  Unlike a Leaderboard, when you hit a scoring event, you don't necessarily get that many points.    |


## Dynamic Config Values

| Key                   | Description                                                                                                                           |
|:----------------------|:--------------------------------------------------------------------------------------------------------------------------------------|
| ladderCacheDuration   | Caches the top scores query if specified.  If this value is 0 or blank, the top scores will be calculated each time when requested.   |
| ladderFinalStep       | If a player is beyond the final step, they can gain points indefinitely without being limited by steps.                               |
| ladderResetMaxScore   | When a ladder reset is triggered, player scores above this number will be set to it.                                                  |
| ladderResetTimestamps | A CSV of Unix timestamps.  When the current Unix timestamp exceeds one of these, the ladder will reset and remove earlier timestamps. |
| ladderStepSize        | The number of points in each step.                                                                                                    |
| ladderTopPlayerCount  | The number of players to return                                                                                                       |

## Scoring on the Ladder

Additional rules apply when adding points to a ladder.  A ladder has "steps", and you have to hit each step before moving beyond it.  For example, if steps are separated by 100-point intervals, and we have 6 steps, our ladder would look like:

```
600
500
400
300
200
100
0
```

Now assume you're a player who has 175 points, and you've just scored 50 points.  On a Leaderboard, this would put you at 225, but on the ladder, you _must_ stop at 200, since that's the next step.  The ladder accepts your score, but only gives you 25 points.

Luckily for you, you win your next match, too, and get another 10 points.  Since you're standing on the 200 step, you're allowed to go higher.  Since you've still got a ways to go before the next step, you now have 210 points.

The third match went very poorly, and you lost 90 points.  Luckily, the same step that previously hampered you is now your safety net.  You only slip a total of 10 points, back down to 200.

Beyond the final step, there is no cap; you can climb as far as you want to go.

### Making a Scoring Request

The endpoints for Ladder are designed to be similar to the other Leaderboards endpoints, with a new path of `/ladder/`.

Ladder endpoints will return objects representing player's positions in the ladder.  In the case of `/score`, only the requesting player is returned.

```
PATCH /leaderboard/ladder/score
{
    "score": 30
}

HTTP 200
{
    "player": {
        "accountId": "649db8b02f66082522b1e29d",
        "score": 100,
        "maxScore": 100,
        "lastUpdated": 1688168082,
        "stepScore": 0,
        "step": 1,
        "id": "649df0042f66082522dc7aa4"
    }
}
```

Some of these fields are derivative for QOL:

* `stepScore` is `score % {step size}`, and represents how far along the user is in the current step.
* `step` is `score / {step size}`

These could be calculated based off of the score, so it's up to the API consumer whether or not to use them.

`maxScore` represents the highest score the player has hit since the last reset.  At the time of this writing we haven't talked about a need for this, so it may just be a placeholder for the time being.

### Viewing the Top Players

The `/ranking` endpoints returns the same format as the scoring endpoint, but instead of one player, it returns up to the `{ladderTopPlayerCount}` players.  One difference however is that `rank` is now included.

If the requesting player is not in the top players, they will be appended to the end with appropriate `rank` calculated.

```
GET /leaderboard/ladder/ranking

HTTP 200
{
    "players": [
        {
            "accountId": "649db8b02f66082522b1e29d",
            "score": 100,
            "maxScore": 100,
            "lastUpdated": 1688168082,
            "stepScore": 0,
            "step": 1,
            "rank": 1,
            "id": "649df0042f66082522dc7aa4"
        },
        {
            "accountId": "6372be1259c472bca7e60149",
            "score": 0,
            "maxScore": 0,
            "lastUpdated": 1687997360,
            "stepScore": 0,
            "step": 0,
            "rank": 2,
            "id": "649b81a32f6608252211edd0"
        },
    ....
    ]
}
```

The number of players is configured from Dynamic Config.  Depending on DC settings, this query may be set to be cached, which will be covered in the next section.  Consequently, this request isn't necessarily always reflective of real-time data if the caching is enabled.

### Caching the Top Players

One of the biggest technical barriers to a feature like Leaderboards is being able to scale with a global user base.  If we have 100 million players, finding the top 100 of them requires the database to scan at least 100 records; and this only happens if the database has been configured properly with indexes.  This by itself isn't really a big problem; databases are designed to optimize requests like that.  But, we also have to perform data transformations on that data, such as adding `rank`, which adds a small loop and processing time on our end.  When this happens millions of times, especially during periods of high traffic, this can alleviate some auto-scaling needs.

This is where caching can help.  We can run our query, make our transformation, and store it as-is on the database.  Then, in future requests, we can just look for and grab a single record.  The cache duration can be configured from DC; once the cache duration is past, the query will be run fresh again.

Note that Memcached is a better long-term solution, but isn't yet available in platform-common due to dependency conflicts.

### Defining The End of Season (AKA ladder resets)

The game server doesn't have an event that fires when a season ends.  Consequently, we needed a way to know when seasons ended so that we could globally drop scores as necessary.

Ladder achieves this through a Dynamic Config field, `ladderResetTimestamps`.  This should be a CSV field of UNIX timestamps - preferably in ascending order just for clarity.  When the Ladder service detects that one of these timestamps is in the past, it will reset all the player scores, then update the DC field to remove past timestamps.

If you have access to platform-common-1.3.67+, you can use the following code example to load resets in:

```
DynamicConfig.Instance?.Update(Audience.LeaderboardService, "ladderResetTimestamps", "1688119417,1688120417,1688121417");
```

Note that with enough time, this field will eventually be empty, and resets will never happen.  For a long-term solution, this will need to be updated on a regular basis or projected far into the future to work without intervention.