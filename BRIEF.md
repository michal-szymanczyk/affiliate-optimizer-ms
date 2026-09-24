# Project Brief

If you have not yet read [`CONCEPTS`](CONCEPTS.md), start there. This document assumes you
understand the simulated affiliate world and its delayed feedback.

> You have been given a client that already works and is naive in exactly one place.
> Turn it into the beginnings of an optimization platform you would be happy to hand to a
> colleague.

The random choice is deliberately unsatisfying, but replacing it with a cleverer function is only
part of the challenge. A useful system also needs a credible way to compare approaches, an operator
experience that makes its behavior understandable, enough insight to earn trust, and a shape that
can evolve after this exercise.

Improving routing decisions is the product objective. Raw commission is not the assessment metric:
the world moves, outcomes are noisy, and nobody sees what an unchosen network would have paid. We
care much more about how you reason about performance than the total your particular run earns.

Plan around roughly four hours, but treat that as a guideline rather than a cutoff. Finishing
earlier is fine. If a direction interests you and you want to take it further, that is welcome too.
Thoughtfulness and clear choices matter more than feature count.

## The shape of the challenge

These are the areas we are most interested in exploring with you. They are lenses, not a feature
checklist.

| Area | The question behind it |
| --- | --- |
| [Optimization models and evaluation](#optimization-models-and-evaluation) | How can different routing approaches be compared honestly? |
| [The operator experience](#the-operator-experience) | What does a person responsible for this system need to see and do? |
| [Observability and insight](#observability-and-insight) | How does the platform explain its health, decisions, and results? |
| [Evolution](#evolution) | What makes the next optimization model or product capability easier to add than the first? |

## The starting point

The supplied random optimization model already sits behind a small `OptimizationModel` interface
with a `name` and `choose` method. In this project, an **optimization model** simply means a named
routing strategy. It may be a rule, a statistical approach, a learning algorithm, or something else
you can explain; it does not need to involve machine learning.

The client already completes a live run from issuing clicks through displaying delayed results. You
may build on that shape, simplify it, or move responsibilities if your direction calls for it.

## Optimization models and evaluation

Try more than one optimization model for choosing a network. The shipped random optimization model
is a useful control. We are interested in the optimization models, but equally interested in the
system around them: how they receive observations, retain knowledge, identify their decisions, and
produce evidence that can be interpreted later.

Comparison is deliberately awkward here. Two optimization models run back to back may experience
different network behavior. Two optimization models sharing traffic at the same time do not receive
the same merchants or random outcomes. Replaying recorded decisions can test consistency and data
processing, but it cannot reveal what another network would have paid.

There is no single required evaluation design. We would like to see how you think about questions
such as:

- What is a meaningful unit of comparison: a decision, a period of time, a run, or something else?
- What context and provenance must be recorded for two results to be compared?
- How should sample size, changing conditions, and uncertainty affect the conclusions an operator
  draws?
- What can the available evidence support, and what would it be dishonest to claim?

A narrow comparison you understand is more valuable than a sophisticated score you cannot defend.
An inconclusive result can be a good result if the system makes clear why it is inconclusive.

## The operator experience

Assume the primary user is the person responsible for the routing system: someone trying to
understand what it is doing, whether it is working, and what should happen next. The supplied UI is
a functioning example, not a specification. You may extend it, simplify it, replace it, or support
the operator somewhere other than the browser.

Think about the questions that person would bring to the product. Can they identify the active
optimization model and understand why it made a decision? Can they distinguish current performance
from historical performance? Can they recognize that the evidence is thin, stale, or changing? If
something is wrong, can they tell whether the problem is traffic, an optimization model, the data
feed, or the application?

We do not grade visual polish. We do care about information hierarchy, useful controls, and whether
the experience communicates the truth of the system rather than projecting false certainty. A
small, coherent operator workflow is stronger than a wide collection of disconnected screens.

## Observability and insight

There are two related kinds of visibility here:

- **Operational observability** answers whether the system is healthy: whether clicks are flowing,
  decisions are being made, commissions are arriving, retries are succeeding, and data is fresh.
- **Decision insight** helps explain the optimization problem: how advertised and realized CPC
  compare, where coverage constrains choices, how behavior changes, what evidence an optimization model
  has seen, and why one optimization model appears different from another.

You do not need to build a complete analytics platform. Choose the signals that matter for your
direction and decide how they should be represented. We are interested in whether the platform can
help an operator move from “the total went up” to a more useful understanding of what happened and
why.

## Evolution

Imagine another engineer receives this repository after you. They may want to add or configure an
optimization model, preserve run history, compare a new optimization model with an old one, or run
the loop somewhere other than an open browser tab.

What boundaries make those changes straightforward? Which concepts deserve stable interfaces or
persistent identities? What belongs to the routing loop, to an optimization model, to an observation
store, or to the operator experience? Which decisions are appropriate for this prototype, and which
should be left open until the system has more evidence and real operating requirements?

We are not asking you to build the future architecture. We are looking for a system whose present
shape reflects a thoughtful view of where change is likely.

## How you worked with AI

Use whatever tools you like. We use them daily and would rather see your real toolkit than a
performance of working without it. Be prepared to explain briefly where AI helped, where it got in
the way, what you checked, and what you would not trust it with here.

## What this is not

This is not a commission leaderboard, a request for production-scale infrastructure, or an
expectation that every question above be answered in code. Pick a coherent direction and make the
trade-offs visible. What you deliberately leave out can be as revealing as what you build.

## What to hand back

We are looking for two things:

1. **The committed repository**, in the state you want us to review.
2. **A walkthrough of your thinking**, in whatever form works for you. That could be a live demo, a
   code walkthrough, notes in the repository, diagrams, a Mural board, slides, or a combination.

There is no required presentation format. Give us enough to understand the direction you chose,
what you learned, the trade-offs you made, and how you thought about the parts you did not build.

## The walkthrough

We will spend about an hour running the project and talking through the work. Expect questions about
your optimization models and evidence, the operator experience, the insights you chose to expose,
how the design could evolve, and how you used and checked your tools. We are interested in candid
reasoning, including failed approaches, uncertainty, and incomplete work.

## If something looks broken

Say so. The server is ours and it is new. If something looks broken, contradictory, or strange,
telling us is more useful than working around it silently.
