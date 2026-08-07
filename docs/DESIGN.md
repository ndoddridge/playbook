# Fantasy Decision Engine (Working Design Doc v1)

## Vision

Build a fast, intelligent fantasy football decision engine whose sole purpose is maximizing the user's chances of winning. It should prioritize concise, actionable recommendations over raw data while remaining fully explainable. Every recommendation should adapt in real time as new information arrives or the user makes roster changes.

This is **not** a fantasy football website. It is a personal football intelligence platform.

---

# Core Principles

1. Recommendations before data.
2. Every recommendation must be explainable.
3. Every recommendation has a confidence score.
4. Everything updates continuously.
5. Every page is personalized to the selected league.
6. The system always understands context.
7. The UI should be clean, modern, and extremely fast.

---

# High Level Architecture

Data Engine

↓

News Engine

↓

Intelligence Engine

↓

Projection Engine

↓

Decision Engine

↓

UI

Every engine should have a single responsibility and be independently testable.

---

# Pages

## 1. Dashboard

The homepage.

Displays only the highest priority football news that affects fantasy.

Examples:

- Major injuries
- Suspensions
- Trades
- Depth chart changes
- Practice updates
- Coaching announcements
- Waiver trends

This page is league agnostic.

Its purpose is to answer:

> "What happened in the NFL today?"

---

## 2. League Selector

Quick switching between leagues.

Example:

League A

League B

Dynasty

Work League

Family League

Switching leagues immediately updates every recommendation throughout the application.

The currently selected league becomes global application state.

---

## 3. Team Dashboard

This becomes the primary working page.

Displays:

Today's Decisions

Start

Bench

Waivers

Trades

Drops

Hold

Confidence

Each recommendation expands into a concise explanation.

If the user performs one recommendation, every remaining recommendation is automatically recalculated.

The recommendation engine should understand how every suggestion affects every other suggestion.

---

## 4. Draft Assistant

Designed to remain open beside a Sleeper draft.

Live recalculation after every draft pick.

Displays:

Best Available

Best Value

Roster Construction

Position Scarcity

Tier Breaks

Recommended Pick

Confidence

Reasoning

Eventually:

Automatic draft tracking from Sleeper if technically possible.

Otherwise:

Manual pick entry.

---

## 5. Player Explorer

Search any player.

Displays:

Projection

Floor

Ceiling

Confidence

Recent News

Historical Performance

College Statistics

Usage Trends

Injury Timeline

Strength of Schedule

Fantasy History

Detailed statistics remain available without cluttering the rest of the application.

---

## 6. Quick Picks

A separate page focused on likely outcomes.

Examples:

Most likely touchdown scorers

Most likely 100-yard receiver

QB over 250 yards

RB over 75 rushing yards

Player anytime TD probability

Projected passing yards

Projected receptions

Projected interceptions

Projected sacks

Each prediction includes:

Confidence

Reasoning

Historical comparison

Recent trend

This page is intended to evolve into a general football prediction engine beyond fantasy.

---

## 7. Replay & Verification

One of the most important long-term pages.

Replay historical NFL seasons week by week.

Run the engine using only information that would have been available at that point in time.

Measure:

Lineup accuracy

Waiver success

Trade success

Draft success

Projection accuracy

Confidence calibration

Every recommendation is logged.

This page becomes our benchmark for improving the system.

---

# Core Engines

## Data Engine

Continuously updates:

NFL statistics

College statistics

Fantasy history

Depth charts

Snap counts

Usage

Schedules

Vegas odds

Weather

Fantasy scoring

ADP

Injuries

Transactions

Everything is normalized into one database.

---

## News Engine

Continuously monitors:

Official NFL news

Beat reporters

Practice reports

Transactions

Roster moves

Coach interviews

Fantasy-relevant updates

The output is summarized, deduplicated, and prioritized by fantasy impact.

---

## Intelligence Engine

Maintains live scores for every player.

Examples:

Draft Value

Trade Value

Waiver Value

Rest-of-Season Value

Weekly Start Value

Risk

Consistency

Breakout Potential

Confidence

Every other engine consumes these scores.

---

## Projection Engine

Produces:

Expected Points

Floor

Ceiling

Confidence

Primary Factors

The explanation should always be concise and expandable.

---

## Decision Engine

The core of the application.

Responsible for:

Lineups

Trades

Waivers

Drops

Starts

Benches

It should understand interactions between recommendations.

Example:

If a waiver recommendation is accepted, lineup recommendations immediately recalculate.

Nothing exists in isolation.

---

# Context Awareness

Every recommendation must understand:

League settings

Scoring rules

Roster requirements

Bench

Waiver rules

Current matchup

Opponent roster

Free agents

Playoff schedule

Bye weeks

Remaining schedule

Recommendations should never be generic.

---

# Recommendation Philosophy

Every recommendation contains:

Action

Confidence

Impact

Reason

Supporting evidence

Example:

Start Jordan Love

Confidence: 93%

Impact: +6.1% weekly win probability

Reason:

Opponent struggles versus QBs.

Weather improved.

Receiving corps healthy.

Vegas projects a high-scoring game.

---

# UI Philosophy

Dark theme.

Modern.

Minimal.

Large cards.

Very little scrolling.

Expandable details.

Fast navigation.

Eventually include:

Player headshots

Team logos

Injury badges

Trend indicators

Confidence visualization

The interface should feel closer to Linear than ESPN.

---

# Version 1 Scope

Dashboard

League Management

Team Dashboard

Player Explorer

Draft Assistant

Quick Picks

Replay Framework

Everything else waits until these are polished.

---

# Future Features (Not Version 1)

AI-generated projections

Dynasty mode

Keeper support

DFS

Multi-sport support

Advanced betting models

Automated trade negotiation

Mobile application

Browser extension

---

# Guiding Goal

When the user opens the application, they should immediately understand the highest-impact decisions they can make to improve their chances of winning. The application should continuously ingest football information, convert it into actionable recommendations, explain every recommendation clearly, and automatically adapt whenever news breaks or the user changes their roster. Over time, the same intelligence engine should expand beyond fantasy football into a general football prediction platform while remaining grounded in transparent, testable, and verifiable decision making.