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
        "playersPerShard": 50,
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
                "tier": 1,
                "promotionRank": 5,
                "demotionRank": -1,
                "promotionPercentage": null,
                "demotionPercentage": null,
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
        "message": "first_place_body",
        "bannerImage": "abc.png",
        "icon": "icon.png",
        "minimumRank": 1,
        "minimumPercentile": -1,
        "contents": [
            {
                "quantity": 1234,
                "resourceId": "hard_currency"
            },
            {
                "quantity": 10000,
                "resourceId": "soft_currency"
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
        "title": "Daily PvP Leaderboards",
        "description": "Use your PvP tickets and climb the ranks!",
        "rolloverType": 1,
        "playersPerShard": 50,
        "maxTier": 6,
        "tierRules": [
            {
                "tier": 1,
                "promotionRank": 5,
                "demotionRank": -1,
                "promotionPercentage": null,
                "demotionPercentage": null,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 1234,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 10000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 500,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 5000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 250,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 2500,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 50,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 500,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "participation_subject",
                        "message": "participation_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "contents": [
                            {
                                "quantity": 100,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 1000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_half_subject",
                        "message": "top_half_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "contents": [
                            {
                                "quantity": 100,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 1000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    }
                ]
            },
            {
                "tier": 2,
                "promotionRank": 5,
                "demotionRank": 26,
                "promotionPercentage": null,
                "demotionPercentage": null,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 2000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 20000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 1000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 10000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 500,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 5000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 100,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 1000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "participation_subject",
                        "message": "participation_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "contents": [
                            {
                                "quantity": 200,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 2000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_half_subject",
                        "message": "top_half_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "contents": [
                            {
                                "quantity": 200,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 2000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    }
                ]
            },
            {
                "tier": 3,
                "promotionRank": 5,
                "demotionRank": 26,
                "promotionPercentage": null,
                "demotionPercentage": null,
                "rewards": [
                    {
                       "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 3000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 30000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 1500,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 15000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 750,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 7500,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 150,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 1500,
                                "resourceId": "soft_currency"
                            }
                        ]
                    }
                ]
            },
            {
                "tier": 4,
                "promotionRank": 5,
                "demotionRank": 26,
                "promotionPercentage": null,
                "demotionPercentage": null,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 4000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 40000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 2000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 20000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 1000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 10000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 200,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 2000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "participation_subject",
                        "message": "participation_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "contents": [
                            {
                                "quantity": 300,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 3000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_half_subject",
                        "message": "top_half_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "contents": [
                            {
                                "quantity": 300,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 3000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    }
                ]
            },
            {
                "tier": 5,
                "promotionRank": 5,
                "demotionRank": 26,
                "promotionPercentage": null,
                "demotionPercentage": null,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 5000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 50000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 2500,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 25000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 1250,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 12500,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 250,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 2500,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "participation_subject",
                        "message": "participation_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "contents": [
                            {
                                "quantity": 400,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 4000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_half_subject",
                        "message": "top_half_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "contents": [
                            {
                                "quantity": 400,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 4000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    }
                ]
            },
            {
                "tier": 6,
                "promotionRank": -1,
                "demotionRank": 26,
                "promotionPercentage": null,
                "demotionPercentage": null,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 6000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 60000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 3000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 30000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 1500,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 15000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "contents": [
                            {
                                "quantity": 300,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 3000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "participation_subject",
                        "message": "participation_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "contents": [
                            {
                                "quantity": 600,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 6000,
                                "resourceId": "soft_currency"
                            }
                        ]
                    },
                    {
                        "subject": "top_half_subject",
                        "message": "top_half_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "contents": [
                            {
                                "quantity": 600,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 6000,
                                "resourceId": "soft_currency"
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
        "leaderboardId": "ldr_pvp_weekly_v2",
        "title": "Daily PvP Leaderboards",
        "description": "Use your PvP tickets and climb the ranks!",
        "rollover": 0,
        "rolloverType": 1,
        "lastReset": 0,
        "Tier": 6,
        "maxTier": 6,
        "tierRules": [
            {
                "tier": 1,
                "promotionRank": 5,
                "demotionRank": -1,
                "promotionPercentage": 0,
                "demotionPercentage": 0,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 1234,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 10000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 500,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 5000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 250,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 2500,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 50,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 500,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "participation_subject",
                        "message": "participation_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 100,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 1000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "top_half_subject",
                        "message": "top_half_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 100,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 1000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    }
                ]
            },
            {
                "tier": 2,
                "promotionRank": 5,
                "demotionRank": 26,
                "promotionPercentage": 0,
                "demotionPercentage": 0,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 2000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 20000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 1000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 10000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 500,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 5000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 100,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 1000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "participation_subject",
                        "message": "participation_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 200,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 2000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "top_half_subject",
                        "message": "top_half_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 200,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 2000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    }
                ]
            },
            {
                "tier": 3,
                "promotionRank": 5,
                "demotionRank": 26,
                "promotionPercentage": 0,
                "demotionPercentage": 0,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 3000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 30000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 1500,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 15000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 750,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 7500,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 150,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 1500,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    }
                ]
            },
            {
                "tier": 4,
                "promotionRank": 5,
                "demotionRank": 26,
                "promotionPercentage": 0,
                "demotionPercentage": 0,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 4000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 40000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 2000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 20000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 1000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 10000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 200,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 2000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "participation_subject",
                        "message": "participation_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 300,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 3000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "top_half_subject",
                        "message": "top_half_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 300,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 3000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    }
                ]
            },
            {
                "tier": 5,
                "promotionRank": 5,
                "demotionRank": 26,
                "promotionPercentage": 0,
                "demotionPercentage": 0,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 5000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 50000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 2500,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 25000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 1250,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 12500,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 250,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 2500,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "participation_subject",
                        "message": "participation_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 400,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 4000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "top_half_subject",
                        "message": "top_half_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 400,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 4000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    }
                ]
            },
            {
                "tier": 6,
                "promotionRank": -1,
                "demotionRank": 26,
                "promotionPercentage": 0,
                "demotionPercentage": 0,
                "rewards": [
                    {
                        "subject": "first_place_subject",
                        "message": "first_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 6000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 60000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 1,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "second_place_subject",
                        "message": "second_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 3000,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 30000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 2,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "third_place_subject",
                        "message": "third_place_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 1500,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 15000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 3,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "top_30_subject",
                        "message": "top_30_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 300,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 3000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": 30,
                        "minimumPercentile": -1,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "participation_subject",
                        "message": "participation_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 600,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 6000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": -1,
                        "minimumPercentile": 0,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    },
                    {
                        "subject": "top_half_subject",
                        "message": "top_half_body",
                        "bannerImage": "abc.png",
                        "icon": "icon.png",
                        "tier": 0,
                        "contents": [
                            {
                                "quantity": 600,
                                "resourceId": "hard_currency"
                            },
                            {
                                "quantity": 6000,
                                "resourceId": "soft_currency"
                            }
                        ],
                        "minimumRank": -1,
                        "minimumPercentile": 50,
                        "timeAwarded": 0,
                        "leaderboardId": null
                    }
                ]
            }
        ],
        "currentTierRules": {
            "tier": 6,
            "promotionRank": -1,
            "demotionRank": 26,
            "promotionPercentage": 0,
            "demotionPercentage": 0,
            "rewards": [
                {
                    "subject": "first_place_subject",
                    "message": "first_place_body",
                    "bannerImage": "abc.png",
                    "icon": "icon.png",
                    "tier": 0,
                    "contents": [
                        {
                            "quantity": 6000,
                            "resourceId": "hard_currency"
                        },
                        {
                            "quantity": 60000,
                            "resourceId": "soft_currency"
                        }
                    ],
                    "minimumRank": 1,
                    "minimumPercentile": -1,
                    "timeAwarded": 0,
                    "leaderboardId": null
                },
                {
                    "subject": "second_place_subject",
                    "message": "second_place_body",
                    "bannerImage": "abc.png",
                    "icon": "icon.png",
                    "tier": 0,
                    "contents": [
                        {
                            "quantity": 3000,
                            "resourceId": "hard_currency"
                        },
                        {
                            "quantity": 30000,
                            "resourceId": "soft_currency"
                        }
                    ],
                    "minimumRank": 2,
                    "minimumPercentile": -1,
                    "timeAwarded": 0,
                    "leaderboardId": null
                },
                {
                    "subject": "third_place_subject",
                    "message": "third_place_body",
                    "bannerImage": "abc.png",
                    "icon": "icon.png",
                    "tier": 0,
                    "contents": [
                        {
                            "quantity": 1500,
                            "resourceId": "hard_currency"
                        },
                        {
                            "quantity": 15000,
                            "resourceId": "soft_currency"
                        }
                    ],
                    "minimumRank": 3,
                    "minimumPercentile": -1,
                    "timeAwarded": 0,
                    "leaderboardId": null
                },
                {
                    "subject": "top_30_subject",
                    "message": "top_30_body",
                    "bannerImage": "abc.png",
                    "icon": "icon.png",
                    "tier": 0,
                    "contents": [
                        {
                            "quantity": 300,
                            "resourceId": "hard_currency"
                        },
                        {
                            "quantity": 3000,
                            "resourceId": "soft_currency"
                        }
                    ],
                    "minimumRank": 30,
                    "minimumPercentile": -1,
                    "timeAwarded": 0,
                    "leaderboardId": null
                },
                {
                    "subject": "participation_subject",
                    "message": "participation_body",
                    "bannerImage": "abc.png",
                    "icon": "icon.png",
                    "tier": 0,
                    "contents": [
                        {
                            "quantity": 600,
                            "resourceId": "hard_currency"
                        },
                        {
                            "quantity": 6000,
                            "resourceId": "soft_currency"
                        }
                    ],
                    "minimumRank": -1,
                    "minimumPercentile": 0,
                    "timeAwarded": 0,
                    "leaderboardId": null
                },
                {
                    "subject": "top_half_subject",
                    "message": "top_half_body",
                    "bannerImage": "abc.png",
                    "icon": "icon.png",
                    "tier": 0,
                    "contents": [
                        {
                            "quantity": 600,
                            "resourceId": "hard_currency"
                        },
                        {
                            "quantity": 6000,
                            "resourceId": "soft_currency"
                        }
                    ],
                    "minimumRank": -1,
                    "minimumPercentile": 50,
                    "timeAwarded": 0,
                    "leaderboardId": null
                }
            ]
        },
        "currentTierRewards": [
            {
                "subject": "first_place_subject",
                "message": "first_place_body",
                "bannerImage": "abc.png",
                "icon": "icon.png",
                "tier": 0,
                "contents": [
                    {
                        "quantity": 6000,
                        "resourceId": "hard_currency"
                    },
                    {
                        "quantity": 60000,
                        "resourceId": "soft_currency"
                    }
                ],
                "minimumRank": 1,
                "minimumPercentile": -1,
                "timeAwarded": 0,
                "leaderboardId": null
            },
            {
                "subject": "second_place_subject",
                "message": "second_place_body",
                "bannerImage": "abc.png",
                "icon": "icon.png",
                "tier": 0,
                "contents": [
                    {
                        "quantity": 3000,
                        "resourceId": "hard_currency"
                    },
                    {
                        "quantity": 30000,
                        "resourceId": "soft_currency"
                    }
                ],
                "minimumRank": 2,
                "minimumPercentile": -1,
                "timeAwarded": 0,
                "leaderboardId": null
            },
            {
                "subject": "third_place_subject",
                "message": "third_place_body",
                "bannerImage": "abc.png",
                "icon": "icon.png",
                "tier": 0,
                "contents": [
                    {
                        "quantity": 1500,
                        "resourceId": "hard_currency"
                    },
                    {
                        "quantity": 15000,
                        "resourceId": "soft_currency"
                    }
                ],
                "minimumRank": 3,
                "minimumPercentile": -1,
                "timeAwarded": 0,
                "leaderboardId": null
            },
            {
                "subject": "top_30_subject",
                "message": "top_30_body",
                "bannerImage": "abc.png",
                "icon": "icon.png",
                "tier": 0,
                "contents": [
                    {
                        "quantity": 300,
                        "resourceId": "hard_currency"
                    },
                    {
                        "quantity": 3000,
                        "resourceId": "soft_currency"
                    }
                ],
                "minimumRank": 30,
                "minimumPercentile": -1,
                "timeAwarded": 0,
                "leaderboardId": null
            },
            {
                "subject": "participation_subject",
                "message": "participation_body",
                "bannerImage": "abc.png",
                "icon": "icon.png",
                "tier": 0,
                "contents": [
                    {
                        "quantity": 600,
                        "resourceId": "hard_currency"
                    },
                    {
                        "quantity": 6000,
                        "resourceId": "soft_currency"
                    }
                ],
                "minimumRank": -1,
                "minimumPercentile": 0,
                "timeAwarded": 0,
                "leaderboardId": null
            },
            {
                "subject": "top_half_subject",
                "message": "top_half_body",
                "bannerImage": "abc.png",
                "icon": "icon.png",
                "tier": 0,
                "contents": [
                    {
                        "quantity": 600,
                        "resourceId": "hard_currency"
                    },
                    {
                        "quantity": 6000,
                        "resourceId": "soft_currency"
                    }
                ],
                "minimumRank": -1,
                "minimumPercentile": 50,
                "timeAwarded": 0,
                "leaderboardId": null
            }
        ],
        "playersPerShard": 50,
        "shardID": null,
        "scores": [],
        "isResetting": false,
        "id": null
    },
    "tierIDs": [
        "622bbb68b10b46362ff09e01",
        "622bbb69b10b46362ff09e02",
        "622bbb69b10b46362ff09e03",
        "622bbb69b10b46362ff09e04",
        "622bbb69b10b46362ff09e05",
        "622bbb69b10b46362ff09e06",
        "622bbb69b10b46362ff09e07"
    ]
}
```