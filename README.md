# leaderboard-service

An API for ranking players based on various criteria.

# Introduction

Leaderboards send regular rewards to deserving players.  This is the players' driving motivation behind all sorts of in-game activities, including PvP.  Leaderboards enable quantitative competition among participating players and encourage regular engagement with the game.

Once a Leaderboard has been created, it is a self-governing entity.  It manages its list of players, scores, and hands out rewards on its own and lives on indefinitely assuming it has a rollover attribute.  The only interaction the game server has with leaderboards is to:

1. Create Leaderboards with rules
2. Update Leaderboard rules
3. Send scores for a specific leaderboard and player

Due to the sheer scale that leaderboards will face upon a global release, all queries **must** be optimized, and leaderboard data will need to be cached so as to not re-evaluate it constantly.  Except in the case of aggregation, wherever possible, the MongoDB queries should perform updates directly, sort, and limit results.  Performance with 200 daily active users is negligible, but with traffic in the millions, the service has a risk of scaling very poorly with calculations.  Sharding (covered below) largely avoids this problem.  Global leaderboards should never have to evaluate more than a handful of records at a time to avoid becoming compute-bound.

By default, the service returns both top and nearby ranks.  While some game leaderboards will allow users to navigate any number of leaderboard records through pagination, the service should not support this.  If there's a feature request for pagination, this can be achieved somewhat by retrieving slightly more records and allowing the client to add "pages" with a cap.  Players generally won't care about who is 261,357th in a leaderboard - they'll only care about the best players and the scores around them.

# Glossary

| Term               | Definition                                                                                                                                                                                         |
|:-------------------|:---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| AccountId / aid    | The MongoDB identifier for an account.                                                                                                                                                             |
| Archive            | A stored state of a completed leaderboard.  When a leaderboard completes its rollover, a full copy of it is kept as an Archive.                                                                    |
| Demotion           | If a user underperformed in a leaderboard, they move down in tier, which yields lesser rewards.                                                                                                    |
| Enrollment         | A service record of a player's exploits within the leaderboard system, containing information about their tier and times participated.                                                             |
| Global Leaderboard | A leaderboard containing every active player in the game.                                                                                                                                          |
| Minimum Percentile | For reward distribution.  A value of 0% means all players receive the reward.  A value of 95% in a leaderboard of 300 players means the top 15 receive the reward.                                 |
| Nearby Rank        | Ranks close to the player in either direction.                                                                                                                                                     |
| Promotion          | If a user excelled in a leaderboard, they move up in tier, yielding greater rewards.                                                                                                               |
| Rank               | The relative position of a player in the leaderboard.  If three players have the same score, they share the same rank, and the rank below them skips two.                                          |
| Reward             | Items to send to players meeting the reward rules.  Rewards are distributed via mailbox-service and contain all information necessary, including subject and message body, to send to mailbox.     |
| Rollover           | A time that a leaderboard locks down, distributes rewards, archives itself, and resets its scores.                                                                                                 |
| Rules              | A generic term for how a leaderboard governs itself.  Can refer to either reward distribution or rollover type.                                                                                    |
| Score              | An arbitrary numeric value indicating a player's progress.  Like in an arcade machine, scores should be varied enough that ties are rare.                                                          |
| Season             | A period of time where all leaderboards end, distribute rewards, and demote all players - with higher tier players being demoted more - to encourage more tier-climbing in the next season.        |
| Shard              | A subset of a leaderboard's players.  Shards are useful for driving competition and seeing immediate results.  Sharded leaderboards operate the same as regular ones, but limited in player count. |
| Tier               | A subset of a leaderboard type, ideally grouping players of similar skill together.  Higher tiers yield better rewards.                                                                            |
| Winner             | A player who received at least one reward from a leaderboard's rollover.                                                                                                                           |

# environment.json

To run locally, all platform-services require an `environment.json` file in the top directory of the solution.  This includes the `PLATFORM_COMMON` value, which is shared across all services with later platform-common packages.

```
{
  "MONGODB_NAME": "leaderboard-service-107",
  "RUMBLE_COMPONENT": "leaderboard-service",
  "RUMBLE_DEPLOYMENT": "wmaynard_local",
  "PLATFORM_COMMON": {
    "MONGODB_URI": {
      "*": "mongodb://localhost:27017/leaderboard-service-107?retryWrites=true&w=majority&minPoolSize=2"
    },
    "CONFIG_SERVICE_URL": {
      "*": "https://config-service.cdrentertainment.com/"
    },
    "GAME_GUKEY": {
      "*": "{redacted}"
    },
    "GRAPHITE": {
      "*": "graphite.rumblegames.com:2003"
    },
    "LOGGLY_BASE_URL": {
      "*": "https://logs-01.loggly.com/bulk/f91d5019-e31d-4955-812c-31891b64b8d9/tag/{0}/"
    },
    "RUMBLE_KEY": {
      "*": "{redacted}"
    },
    "RUMBLE_TOKEN_VALIDATION": {
      "*": "https://dev.nonprod.tower.cdrentertainment.com/token/validate"
    },
    "SLACK_ENDPOINT_POST_MESSAGE": {
      "*": "https://slack.com/api/chat.postMessage"
    },
    "SLACK_ENDPOINT_UPLOAD": {
      "*": "https://slack.com/api/files.upload"
    },
    "SLACK_ENDPOINT_USER_LIST": {
      "*": "https://slack.com/api/users.list"
    },
    "SLACK_LOG_BOT_TOKEN": {
      "*": "xoxb-4937491542-3072841079041-s1VFRHXYg7BFFGLqtH5ks5pp"
    },
    "SLACK_LOG_CHANNEL": {
      "*": "C031TKSGJ4T"
    },
    "SWARM_MODE": {
      "*": false
    },
    "VERBOSE_LOGGING": {
      "*": false
    },
    "RUMBLE_TOKEN_VERIFICATION": {
      "*": ""
    }
  }
}
```

# Class Overview

## Controllers
| Name            | Description                                                                                        |
|:----------------|:---------------------------------------------------------------------------------------------------|
| AdminController | Handles the operations for administrative tools, such as the CS portal, and updating leaderboards. |
| TopController   | Handles requests to `score`, `rankings`.                                                           |

## Exceptions
| Name                             | Description                                                                                                                                                                                         |
|:---------------------------------|:----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| AccountDisqualifiedException     | Thrown when a user is not allowed to participate in leaderboards.                                                                                                                                   |
| UnknownLeaderboardException      | Thrown when a leaderboard_id is unrecognized.  The game server should hit `/update` to create the leaderboard if the leaderboard_id should exist.                                                   |

## Models
| Name            | Description                                                                                                                            |
|:----------------|:---------------------------------------------------------------------------------------------------------------------------------------|
| Account         | Unused, to be removed                                                                                                                  |
| Enrollment      | A service record of a player's exploits within the leaderboard system, containing information about their tier and times participated. |
| Entry           | A minimal component tracked in the leaderboard data.  One per player per leaderboard.                                                  |
| Item            | Part of a reward to give to players.  Consists of a resource ID and quantity.                                                          |
| Leaderboard     | The meat of the service; contains all information necessary for tracking player scores, reward rules, and rollover rules.              |
| Ranking         | Generated when leaderboards are evaluated.  Indicates player positions in the leaderboard; each rank can have multiple players.        |
| Reward          | Data required to send winners their respective messages in mailbox-service.                                                            |
| ServiceRecord   | Unused, to be removed                                                                                                                  |
| ServiceSettings | Unused, to be removed                                                                                                                  |
| TierRules       | Dictates requirements for promotions, demotions, and rewards.                                                                          |

## Services
| Name               | Description                                                                                                                                                                                                                                |
|:-------------------|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| ArchiveService     | Stores copies of completed leaderboards in their entirety for a period of time.                                                                                                                                                            |
| EnrollmentService  | Handles player Enrollment documents.                                                                                                                                                                                                       |
| LeaderboardService | Handles scoring, data retrieval, and rollover mechanics.                                                                                                                                                                                   |
| MasterService      | Experimental PlatformTimerService.  The goal is to allow exactly one instance of the service to perform certain work regardless of how many have been created in kubernetes for scaling.  May be moved to platfrom-common at a later date. |
| ResetService       | Implementation of MasterService; locks down leaderboards and triggers rollovers.                                                                                                                                                           |
| RewardsService     | Registers winners and their rewards from completed leaderboards to create an auditable system.  Sends messages to mailbox-service at the end of the rollover.                                                                              |
| SettingsService    | Unused, to be removed                                                                                                                                                                                                                      |

## Endpoints

All endpoints live off of the base `{platform url}/leaderboard`.

### Top level

Player authorization tokens are required.

| Method | Endpoint    | Description                                                                                                            | Required Fields            | Optional Fields |
|-------:|:------------|:-----------------------------------------------------------------------------------------------------------------------|:---------------------------|:----------------|
|    GET | `/rankings` | Returns ranked and ordered information for a specific leaderboard ID.  Response includes top scores and nearby scores. | `leaderboardId`<br>`score` ||
|  PATCH | `/score`    | Adds or subtracts a value to the player's score.  While this value can be negative, scores are floored at 0.           | `leaderboardId`            ||

<hr />

`GET /rankings?leaderboardId=pvp_daily`

Response:

```
{
    "success": true,
    "leaderboardId": "pvp_daily",
    "tier": 1,
    "seasonalMaxTier": 0,
    "response": {
        "topScores": [
            {
                "accounts": [
                    "621d7b50ed456b3870d05a4c"
                ],
                "rank": 1,
                "score": 0,
                "isRequestingPlayer": true
            }
        ],
        "nearbyScores": [
            {
                "accounts": [
                    "621d7b50ed456b3870d05a4c"
                ],
                "rank": 1,
                "score": 0,
                "isRequestingPlayer": true
            }
        ]
    }
}
```

<hr />

`PATCH /score`

Body:

```
{
    "score": 55,
    "leaderboardId": "pvp_daily"
}
```

Response:

**No Content**

<hr />

### Admin

Admin authorization tokens are required.  Use the `leaderboard_AdminToken` from dynamic config.

| Method | Endpoint   | Description                                                                                                                                                                                                                                           | Required Fields | Optional Fields |
|-------:|:-----------|:------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|:----------------|:----------------|
|    GET | `/archive` | Returns a list of past leaderboards the player has placed in.  Archives are not kept indefinitely.  By default, only one leaderboard is returned, but any positive number can be specified.  Leaderboards are sorted by end time in descending order. | `leaderboardId` | `count`         |

<hr />

`GET /archive?leaderboardId=ldr_pvp_weekly_v1&count=2`

Response:

```
{
    "success": true,
    "leaderboards": [
        { ... },
        { ... }
    ]
}
```

<hr />

### Admin

Admin authorization tokens are required.  Use the `leaderboard_AdminToken` from dynamic config.

| Method | Endpoint        | Description                                                                                                                                                              | Required Fields | Optional Fields |
|-------:|:----------------|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------|:----------------|:----------------|
|    GET | `/admin/list`   | Returns a list of leaderboard IDs.  This list should be checked against the CSV managed by the design team.  If an ID does not exist, create it through `/admin/update`. |                 ||
|   POST | `/admin/update` | Creates or modifies leaderboards per the request body.                                                                                                                   | `leaderboard`   ||

<hr />

`GET /admin/list`

Response:

```
{
    "success": true,
    "leaderboardIds": [
        "pvp_daily"
    ]
}
```

<hr />

Due to its size, please see the dedicated [LEADERBOARD MANAGEMENT](LEADERBOARD_MANAGEMENT.md) README for a full example of how to format the message for `POST /admin/update`.

<hr />

## Example Flow

![Leaderboard flowchart](leaderboard-service.png?)

## Future Updates, Optimizations, and Nice-to-Haves


## Troubleshooting
