# Creating & Updating Leaderboards

Leaderboards and their rules are managed through the `POST /admin/update` endpoint.  This is the meat of the leaderboards service and is **required** for proper functionality.  The request body must be built from the CSV the design team manages.  This readme will guide you through each step of the request body and build the request as you go, with a complete request at the end.

## Basic Information

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
* The **maxTier** determines how many leaderboards will actually be created.  When leaderboards hit rollover, players can be promoted or demoted to different tiers.

Sample request so far:

```
{
    "leaderboard": {
        "leaderboardId": "ldr_pvp_weekly_v1",
        "title": "Daily PvP Leaderboards",
        "description": "Use your PvP tickets and climb the ranks!",
        "rolloverType": 1,
        "maxTier": 6,
        ...
      }
}
```

## Introduction to Tier Rules

Every tier has a set of rules associated with it.  These rules determine what happens at rollover.  Every tier requires its own rules, and these rules need to be defined with our update request.  The `tierRules` of our request body is an array of objects containing:

* A **tier**, a 0-index integer value.
* A **promotionRank** which dictates which ranks advance to the higher tier on rollover.  Every rank less than or equal to this value will be promoted.
* A **demotionRank** which dictates which ranks fall to the lower tier on rollover.  Every rank equal to or greater than this value will be demoted.
* A **promotionPercentage**, a placeholder for future functionality.
* A **demotionPercentage**, a placeholder for future functionality.
* **rewards**, an array of objects determining which players receive in-game items for their participation.

Continuing our request:

```
       ...
        "tierRules": [
            {
                "tier": 0,
                "promotionRank": 5,
                "demotionRank": -1,
                "promotionPercentage": null,
                "demotionPercentage": null,
                "playersPerShard": 50, // This is just a placeholder for now
                "rewards": [...]                 // Coming up next
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

After rollover completes, leaderboard-service will send these rewards to the appropriate mailboxes.

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
        "maxTier": 1,
        "tierRules": [
            {
                "tier": 0,
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
    "leaderboard": {
        "leaderboardId": "ldr_pvp_weekly_v1",
        "title": "Weekly PvP Leaderboards",
        "description": "Use your PvP tickets and climb the ranks!",
        "rolloverType": 2,
        "rolloverTypeVerbose": "Weekly",
        "tier": 1,
        "maxTier": 1,
        "tierRules": [
            {
                "tier": 0,
                "promotionRank": 5,
                "demotionRank": -1,
                "playersPerShard": 50,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "body": "first_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 1000,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 10000,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "timeAwarded": 0
                    },
                    {
                        "subject": "second_place_subject",
                        "body": "second_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 500,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 5000,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "timeAwarded": 0
                    },
                    {
                        "subject": "third_place_subject",
                        "body": "third_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 250,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 2500,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "timeAwarded": 0
                    },
                    {
                        "subject": "top_30_subject",
                        "body": "top_30_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 100,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 1000,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "timeAwarded": 0
                    },
                    {
                        "subject": "top_half_subject",
                        "body": "top_half_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 75,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 750,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "timeAwarded": 0
                    },
                    {
                        "subject": "first_place_subject",
                        "body": "first_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 25,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 250,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "timeAwarded": 0
                    }
                ]
            },
            {
                "tier": 1,
                "promotionRank": 5,
                "demotionRank": 40,
                "playersPerShard": 50,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "body": "first_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 2000,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 20000,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "timeAwarded": 0
                    },
                    {
                        "subject": "second_place_subject",
                        "body": "second_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 1000,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 10000,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "timeAwarded": 0
                    },
                    {
                        "subject": "third_place_subject",
                        "body": "third_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 500,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 5000,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "timeAwarded": 0
                    },
                    {
                        "subject": "top_30_subject",
                        "body": "top_30_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 200,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 2000,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "timeAwarded": 0
                    },
                    {
                        "subject": "top_half_subject",
                        "body": "top_half_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 150,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 1500,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "timeAwarded": 0
                    },
                    {
                        "subject": "first_place_subject",
                        "body": "first_place_body",
                        "banner": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "attachments": [
                            {
                                "Quantity": 50,
                                "RewardId": "hard_currency",
                                "Type": "currency"
                            },
                            {
                                "Quantity": 500,
                                "RewardId": "soft_currency",
                                "Type": "currency"
                            }
                        ],
                        "internalNote": "ldr_pvp_weekly_v1 reward",
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "timeAwarded": 0
                    }
                ]
            }
        ],
        "scores": []
    },
    "tierIDs": [
        "623517d10f0c01c01b14075d",
        "623517d10f0c01c01b14075e"
    ]
}
```