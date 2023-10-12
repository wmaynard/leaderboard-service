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

## Ladder Seasons

Ladder seasons can be defined by using the endpoint `POST /leaderboard/admin/seasons`.  Seasons have the following rules:

1. A Season has a **Fallback Score**.  Anyone under this value has their score dropped to 0.  Anyone above has their score reset to the fallback score.
2. Only one season can be active at a time.  This is dictated by its **End Time**.  Platform does not use the definitions provided for any logic outside of what happens when a season passes its end time.  In other words, there's no logic for a "start time".
3. If no season is active, logs will be sent - noisily - to draw attention to the problem.  It is assumed though not required that Ladder is operating with an active season at all times.
4. A season can contain rewards.  Rewards can only go out to a certain number of top players.

### When A Season Ends

1. All historical records with a matching season ID are deleted.  Players can only have one historical record per season.  In practice, this should not delete any records - but is a safeguard to make sure the database is clean.
2. All players with a **Seasonal Max Score** over 0 have a historical record of their season stats created in a separate collection.  Players with a max score of 0 are considered to be inactive, and as such do not generate data.
3. All players below the **Fallback Score** have their Score / Max set to 0.
4. All players above the **Fallback Score** have their Score / Max set to the fallback value.
5. Rewards, if any are defined, are granted to relevant players.
   1. Rewards are first granted within the leaderboard-service database and marked as unsent.
   2. Unsent rewards are periodically checked / delivery attempts made on a timer.  Consequently, rewards may not be instant; the more players we have, the longer this process will take.
   3. When leaderboard-service receives a 200 status code from mail-service, the rewards for that particular player are marked as sent.

**No changes can be made to seasons that have ended.**

### Defining a Season

Using the admin endpoint, send an array of Seasons.  Each season has:

| Field        | Description                                                                                                                                                                                                                                                          | Required       |
|:-------------|:---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|:---------------|
| endTime      | A Unix timestamp when the season will end.  Note that the season will not end _exactly_ at this time, since the service is just checking to see if the current timestamp is after the end time occasionally, so there may be a few minutes of delay.                 | Yes (non-zero) |
| nextSeasonId | A string identifier for the following season.  This is not used by the service but is provided in case we want it for tracking, or decide to make use of it in the future.                                                                                           | No             |
| rewards      | A list of rewards that go out to top players when a season ends.  The original specification was only for the top 100 players, but the service actually supports up to the top 10,000.  Uses the same model as Leaderboards rewards, though some fields are unused.* | No             |
| seasonId     | A unique string identifier for the given season.                                                                                                                                                                                                                     | Yes            |

For rewards, only `minimumRank` is honored for criteria in deciding reward eligibility.

Request example:

```
POST /leaderboard/admin/ladder/seasons
{
    "seasons": [
        {
            "seasonId": "foo",
            "nextSeasonId": "bar",
            "endTime": 1696926645,
            "rewards": [
                {
                    "subject": "placeholder",
                    "body": "placeholder",
                    "banner": "placeholder",
                    "icon": "placeholder",
                    "internalNote": "ldr_pvp_season_01 reward",
                    "minimumRank": 10,
                    "attachments": [
                        {
                            "type": "Currency",
                            "rewardId": "pvp_shop_currency",
                            "quantity": 10
                        },
                        ...
                    ]
                },
                ...
            ]
        },
        {
            "seasonId": "bar",
            "nextSeasonId": "yetAnotherSeason",
            "endTime": 1697026645,
            "rewards": []
        }
    ]
}
```

#### Important note: Dynamic Config was previously used to define season behavior.  This is no longer the case.

### When Seasons are Defined

1. Seasons that have already ended but are re-sent or changed are ignored.  You cannot change a season that has ended.  _Best practice: do not send seasons that are old to keep the request size down._
2. **All currently open seasons are deleted**.  This does not alter current player scores.
3. Create new season definitions for remaining data.

**CAUTION:** if you send an update request that changes an active season to have an `endTime` that's in the past, this will **immediately cause a season reset.**  This may be intentional to end a season immediately with a release.  There is no validation or warning on this to support a design decision to end a season early, and this is independent of environment.  A push to A1 will affect A2 and vice versa, so an internal build will impact live players.

### Retrieving Historical Season Data

Historical season data is tracked for up to 3 months.

```
GET /leaderboard/ladder/history?count=5

HTTP 200
{
    "history": [
        {
            "score": 50,
            "maxScore": 50,
            "accountId": "638a57843090a47d42265b65",
            "lastUpdated": 1697097084,
            "season": { ... },
            "id": "6527a5957005a2920158db14",
            "createdOn": 1697097109
        }
    ]
}
```

The full season definition is stored and returned with each record.  It's the same model that's used in the creation of the season definition.

### Retrieving Multiple Accounts' Scores

Requires an admin token, and creates records if they are not found.

```
GET /admin/ladder/scores?accountIds=deadbeefdeadbeefdeadbeef,6375681659c472bca7dabc40

HTTP 200
{
    "players": [
        {
            "accountId": "deadbeefdeadbeefdeadbeef",
            "score": 0,
            "maxScore": 0,
            "lastUpdated": 0,
            "id": "6527b227604f8fcda11d67b3",
            "createdOn": 1697100327
        },
        {
            "accountId": "6375681659c472bca7dabc40",
            "score": 50,
            "maxScore": 75,
            "lastUpdated": 1697100327,
            "id": "6527b227604f8fcda11d67b4",
            "createdOn": 1697100327
        }
    ]
}
```