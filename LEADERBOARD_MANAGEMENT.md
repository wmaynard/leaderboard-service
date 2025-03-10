# Creating & Updating Leaderboards

Leaderboards and their rules are managed through the `POST /admin/update` endpoint.  This is the meat of the leaderboards service and is **required** for proper functionality.  The request body must be built from the CSV the design team manages.  This readme will guide you through each step of the request body and build the request as you go, with a complete request at the end.

## Basic Information

The update endpoint is a bulk update; it requires an array of Leaderboards, but also handles deletion as well.  This is done to be able to rollback any database transactions that happen if any leaderboard fails validation.  If any part of the update fails, there are no changes to the database.

## Deleting Leaderboards

Deletion happens first.  If you pass in a leaderboardId of `ldr_pvp_weekly_v1` into the deletion array and also specify the same ID to create the leaderboard, this effectively resets the leaderboard **without rollover.**.  While useful for debugging, be _very_ careful when doing this, because you may cost players progress if this happens in prod!

We start off the request with:

```
{
    "idsToDelete": [
        "ldr_pvp_weekly_v1",
        "ldr_pvp_weekly_v2"
    ],
    ...
```

## Defining Leaderboards

Every leaderboard shares some simple descriptors.  Since leaderboards will be localized eventually, some fields are intended for internal use only, but could be used to hold the string key for localization instead should that make more sense.

* The **leaderboardId** is a unique identifier for a set of rules.  Examples could be `pvp_weekly` or `energy_spend_monthly`.
* The **title** is a short description for the leaderboard.
* The **description** is a longer-form description for the leaderboard.
* The **rolloverType** dictates how often the leaderboard resets and distributes rewards.
  * 0: Hourly (untested)
  * 1: Daily
  * 2: Weekly
  * 3: Monthly
  * 4: Annually (untested)
  * 5: None (untested)
* The **playersPerShard** controls how many players can be assigned to a particular leaderboard before a new shard of it is created.  This is currently a placeholder for future functionality.
* The **startTime** determines when a leaderboard is eligible to receive scores.  This is a Unix timestamp.
* The **tierCount** determines how many leaderboards will actually be created.  When leaderboards hit rollover, players can be promoted or demoted to different tiers.  A value greater than 0 is required.
* A **rolloversInSeason**, the count for the number of times a leaderboard can reset before issuing seasonal rewards and resetting tiers.  A value of -1 keeps seasons turned off.
* A **maxTierOnSeasonEnd**, an optional number that prevents players from being kicked down to the bottom tier when a season ends.

Leaderboards are passed as an array of objects.  Here's our sample request so far:

```
...
"idsToDelete": [
    "ldr_pvp_weekly_v1",
    "ldr_pvp_weekly_v2"
],
"leaderboards": [
    {
        "leaderboardId": "ldr_pvp_weekly_v1",
        "title": "Daily PvP Leaderboards",
        "description": "Use your PvP tickets and climb the ranks!",
        "rolloverType": 1,
        "startTime": 1650922715639,
        "tierCount": 2,
        "rolloversInSeason": 20,
        "maxTierOnSeasonEnd": 0,
        ...
    },
    ...
]
```

### Guild Shards

Guild shards require no special updates to work.  When a score request comes in for a guild, and the requesting player is verified as a member of said guild, Platform will either find the relevant guild shard and record the player's score, or if said shard does not exist, it will be created.

By default, guild shards have no rewards.  Guild Shard Rewards require separate reward definitions.  For further information, refer to the section **Guild Shard Rewards** further below.

Guild shards behave slightly differently than normal shards in the following ways:

* The guild roster is not maintained on the Leaderboards side for performance reasons.  If someone leaves the guild while a leaderboard is active, leaderboards still holds onto their records until rollover.
  * This means that if someone leaves a guild and later rejoins, their contributions to that leaderboard remain intact.  Changing this behavior requires further enhancements and interdependencies requires additional work on both Guilds and Leaderboards, but is possible.  Pros: if someone is wrongfully kicked and invited back, they don't lose progress.  Cons: it's a potential vector for players to game with guild-hopping in **other systems**.  No one will receive two guild rewards from a leaderboard shard.
* During rollover, leaderboards fetches roster information from Guilds.  Only members who are currently in the guild at rollover are eligible for the shard's rewards.
  * People may leave or be kicked out of the guild before rollover.  These scores are still tracked, but ignored for reward calculation.
  * If guild information isn't available at all, it's assumed that the guild has disbanded, and no rewards are granted.
* Like normal shards, guild shards are first archived, then destroyed when rollover completes.  A new shard will be generated when needed.

## Introduction to Tier Rules

Every tier has a set of rules associated with it.  These rules determine what happens at rollover.  Every tier requires its own rules, and these rules need to be defined with our update request.  The `tierRules` of our request body is an array of objects containing:

* A **tier**, a 0-index integer value.
* A **maxTierOnSeasonEnd**, a 0-index integer value indicating the maximum tier a player can be once a season ends.
* A **promotionRank** which dictates which ranks advance to the higher tier on rollover.  Every rank less than or equal to this value will be promoted.
* A **demotionRank** which dictates which ranks fall to the lower tier on rollover.  Every rank equal to or greater than this value will be demoted.
* A **promotionPercentage**, a placeholder for future functionality.
* A **demotionPercentage**, a placeholder for future functionality.
* The number of **playersPerShard**.  When this is set to a negative value, a Leaderboard's capacity is _unlimited_.  When set to a positive number, that number becomes the maximum capacity for a leaderboard type.
* **rewards**, an array of objects determining which players receive in-game items for their participation.

Continuing our request:

```
       ...
        "tierRules": [
            {
                "tier": 0,
                "maxTierOnSeasonEnd": 0,
                "promotionRank": 5,
                "demotionRank": -1,
                "promotionPercentage": null,
                "demotionPercentage": null,
                "playersPerShard": 50,
                "rewards": [...]                 // Coming up next
                "seasonalReward" : { ... }
            },
            {
                "tier": 2,
                ...
            }
        ]
        ...
```

## Defining Rewards

Each tier contains a set of rewards that are associated with it.  There is no limit on the rewards that can be attached to a tier, but only one set can be sent to an individual player on rollover.  Every reward has a definition for a minimum rank required and a minimum percentile that players must achieve to be eligible.  Using an example, let's say we have a leaderboard of 100 participants.

* Jane placed #3
* John placed #57
* Rank Rewards have been defined for a minimum rank of 1, 2, 3, and 10.
* Percentile Rewards have been defined for 50% and 100%.

Our reward eligibility looks like this, in descending order of priority:

| Reward          | Jane | John |
|:----------------|:-----|:-----|
| Minimum Rank 1  | No   | No   |
| Minimum Rank 2  | No   | No   |
| Minimum Rank 3  | Yes  | No   |
| Minimum Rank 10 | Yes  | No   |
| Percentile 50   | Yes  | No   |
| Percentile 100  | Yes  | Yes  |

The top reward is always chosen from this list.  Rank Rewards always take priority over Percentile Rewards.  This means that if we had a Rank Reward for 60 and our leaderboard only has 100 people, the Percentile 50 reward would _never be issued to anyone_.  However, it could still be useful if our leaderboard sees more players in a future cycle.

In addition to the rank and percentages, each reward must contain the following for integration with mailbox-service:

* A **subject** 
* A **message** body
* A **bannerImage**
* An **icon**
* An array of **contents**, attachments for the actual rewards.

```
...
"rewards": [
    {
        "subject": "first_place_subject",
        "body": "first_place_body",
        "banner": "abc.png",
        "icon": "icon.png",
        "internalNote": "ldr_pvp_weekly_v1 reward",
        "minimumRank": 1,
        "minimumPercentile": -1,
        "guildReward": false,
        "attachments": [
            {
                "Type": "currency",
                "Quantity": 1000,
                "RewardId": "hard_currency"
            },
            {
                "Type": "currency",
                "Quantity": 10000,
                "RewardId": "soft_currency"
            }
        ]
    },
    ...
]
```

### Guild Shard Rewards

Because Guild Shards are spawned off of the base definition shard, rewards for guild shards must also be defined in this array.  Per design, guild rewards need to be different from standard rewards; all other shard behavior remains the same.

Guild shards only pull rewards that are designed for guilds.  To define a guild shard reward, set `guildReward` to true.  This field is optional and will default to false.

If you have no rewards with `guildReward` set, the guild shard will still work; however, no rewards will be issued on rollover.

### Season Rewards

At the end of a leaderboard's season, a reward package is sent out to all players for their participation.  This reward is determined by the highest tier the player managed to hit during the season.  To define this reward, see the below example:

```
...
"tierRules: [
    {
        "tier": 0
        ...
        "rewards": [ ... ],
        "seasonalReward": {
            "subject": "placeholder",
            "body": "placeholder",
            "banner": "placeholder",
            "icon": "placeholder",
            "internalNote": "ldr_pvp_season_01 season reward",
            "attachments": [
                {
                    "type": "Currency",
                    "rewardId": "pvp_shop_currency",
                    "quantity": 10
                },
                {
                    "type": "LootTable",
                    "rewardId": "loot_equipment_04_legendary",
                    "quantity": 1
                },
                {
                    "type": "Currency",
                    "rewardId": "shard_limited_time_scroll",
                    "quantity": 1000
                },
                {
                    "type": "Currency",
                    "rewardId": "hard_currency",
                    "quantity": 1000
                },
                {
                    "type": "Currency",
                    "rewardId": "soft_currency",
                    "quantity": 100000
                },
                {
                    "type": "Currency",
                    "rewardId": "xp_currency",
                    "quantity": 75000
                }
            ]
        }
    }
]
```

The seasonal reward is the same exact model / data structure as the array of placement rewards.  However, the placement data is not used - this is because the season has no concept of where people were in the rankings.  The important difference is that there is a **single reward** per tier instead of an **array**.  When using seasons, a seasonal reward must be defined.

### Rewards Wrap-Up

After rollover completes, leaderboard-service will send all applicable rewards to the appropriate mailboxes.

## Full Request Example

This sample request contains all of the information necessary to create a leaderboard with:

* 6 tiers, each with promotion and demotion rules
* Rewards for:
  * The #1 player
  * The #2 player
  * The #3 player
  * Players above rank 30
  * The top half of the players
  * All players (participation reward)

`PATCH /leaderboard/admin/update`

Authorization: `Bearer {leaderboard_AdminToken}`

```
{
    "leaderboard": {
        "leaderboardId": "ldr_pvp_weekly_v1",
        "title": "Weekly PvP Leaderboards",
        "description": "Use your PvP tickets and climb the ranks!",
        "rolloverType": 2,
        "tierCount": 2,
        "tierRules": [
            {
                "tier": 0,
                "maxTierOnSeasonEnd": 0,
                "promotionRank": 5,
                "demotionRank": -1,
                "promotionPercentage": null,
                "demotionPercentage": null,
                "playersPerShard": 50, // This is just a placeholder for now
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "body": "first_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 1000,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 10000,
                                "RewardId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "second_place_subject",
                        "body": "second_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 500,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 5000,
                                "RewardId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "third_place_subject",
                        "body": "third_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 250,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 2500,
                                "RewardId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_30_subject",
                        "body": "top_30_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 100,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 1000,
                                "RewardId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_half_subject",
                        "body": "top_half_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 75,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 750,
                                "RewardId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "first_place_subject",
                        "body": "first_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 25,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 250,
                                "RewardId": "soft_currency"
                            }
                        ]
                    }
                ]
            },
            {
                "tier": 1,
                "maxTierOnSeasonEnd": 0,
                "promotionRank": 5,
                "demotionRank": 40,
                "promotionPercentage": null,
                "demotionPercentage": null,
                "playersPerShard": 50,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "body": "first_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 2000,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 20000,
                                "RewardId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "second_place_subject",
                        "body": "second_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 1000,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 10000,
                                "RewardId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "third_place_subject",
                        "body": "third_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 500,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 5000,
                                "RewardId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_30_subject",
                        "body": "top_30_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 200,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 2000,
                                "RewardId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_half_subject",
                        "body": "top_half_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 150,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 1500,
                                "RewardId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "first_place_subject",
                        "body": "first_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "attachments": [
                            {
                                "Type": "currency",
                                "Quantity": 50,
                                "RewardId": "hard_currency"
                            },
                            {
                                "Type": "currency",
                                "Quantity": 500,
                                "RewardId": "soft_currency"
                            }
                        ]
                    }
                ]
            }
        ]
    }
}
```

### Response

This will likely be removed or simplified in the future, but for now it's useful for debugging and validating creation on platform side.

```
{
    "success": true,
    "deleted": 4
}
```