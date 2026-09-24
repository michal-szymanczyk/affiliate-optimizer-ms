# The Problem Space

This document defines the domain and the simulated world: what affiliate routing *is*, why the
decision is difficult, and what each moving part means.

## Where this comes from

A site that sends traffic to merchants can be paid through an **affiliate network** such as
Rakuten, CJ, or Impact. This exercise models one arrangement only: **cost per click (CPC)**.
A network pays for each eligible click routed to it. There is no purchase, sale, order, lead, or
conversion anywhere in the simulation.

Each network publishes a non-binding headline CPC. The amount it actually pays for a routed click
is the **realized CPC**, represented by the API as `commission`. That value is observable only after
the click has been routed.

The wrinkle that makes it interesting: a given retailer is often reachable through *several*
networks at once. Same shopper, same destination, different intermediary, and materially different
money, because networks differ in what they pay and in how honestly they describe it.

So every outbound click is a small revenue decision. Multiply by a lot of clicks and the decision
stops being small.

## The cast

A simulated version of exactly that decision, compressed so a whole cycle of learning-and-acting fits
in an afternoon instead of a quarter. Seven concepts carry all of it.

| | In the real thing | Here |
| --- | --- | --- |
| **Merchant** | The retailer a shopper is heading to | One of a hundred fixed brands with a stable `merchantId`. Every click names exactly one. Carries no hidden information: it only decides which networks are eligible. |
| **Network** | The affiliate network you route them through | Somewhere you can route a click. There are fifteen, the same fifteen for everyone, and they do not change under you. |
| **Click** | A shopper clicking through to a retailer | One unit of traffic. Names a merchant, expires two minutes after issue, and is the thing you decide about. |
| **Advertised CPC** | The per-click rate a network publishes in its catalog | What a network *claims* it pays per click. Public, fixed, non-binding. Named `advertisedRate` in the API. |
| **Realized CPC** | What actually lands after a reporting delay | What a network actually paid for one click. Named `commission` in the API and revealed one minute later. |
| **Coverage** | Which retailers a network has a program with | `supportedMerchantIds`. The one thing here you can treat as fact. |
| **Route** | Sending the shopper somewhere, and you only get one shot | Committing one click to one network. Final and irreversible. |

Two of those are worth reading twice next to each other: **advertised CPC is a claim; realized CPC
is the observation.** The whole exercise lives in the gap between them.

Both are **per click**, which is why they are small: cents, or fractions of a cent.

**Four decimal places are significant.** Some networks here differ from each other by less than a
cent, so a client that rounds to two decimals genuinely cannot tell them apart; the difference
disappears into the rounding.

**The numbers are invented.** Not market data, not scraped from any network's catalog, not calibrated
against anyone's real earnings. Do not read anything into their absolute size. What is real is the
*structure*: that claims are unreliable, that networks differ from each other, and that some of them
change over time. That is what there is to learn.

## Three properties that make the decision hard

Three properties shape the routing decision.

### 1. Coverage is fact; the advertised rate is not

`supportedMerchantIds` is the one thing in the system you can simply trust. If a network
lists a merchant, routing there is eligible; if it does not, the routing is rejected *and
the click is spent*. Because coverage is published, that is an avoidable mistake, and
spending a click on it is the intended consequence.

The advertised rate gets no such guarantee. Some networks honor it. Some do not. Some pay
well under some conditions and poorly under others. Some are genuinely noisy. If a
network's commissions look erratic, that is the data being erratic, not a bug in the
server. Nothing in the API labels these cases.

### 2. You act on stale information, always

Routing never returns a commission. It appears in the disclosure feed a minute later.

So the loop is not *decide → learn → decide*. It is *decide, decide, decide, learn a
little about something you decided a while ago, keep deciding*. Any belief you hold about
a network is necessarily out of date, and the amount by which it is out of date is fixed
and known.

### 3. There is no ground truth, and no counterfactual

There is no endpoint that reveals what a network "really" pays. There is no way to learn
what a click *would* have earned somewhere else. You see only what happened on the clicks
you actually routed, which means:

- You can measure **realized commission**. You cannot measure regret.
- Every observation you have is one you paid a click for.
- A network you never try stays unknown forever, and looking costs something.

That last point is the oldest trade in the book: spend traffic learning, or spend it
earning. It is unavoidable here rather than bolted on.

## What the simulation compresses

Two things about this world are deliberately unlike the real one.

**Realized CPC moves much faster than it would.** A real per-click arrangement changes
occasionally. Here the hidden payment behavior can change several times in half an hour, which is
what makes learning it a live problem rather than a lookup you do once. The advertised CPC remains
fixed.

**Every eligible routing pays.** There is no purchase or conversion step, and no zero-valued
commission. The only no-earning outcomes are clicks you let expire or route outside a network's
coverage. That follows from paying per click, but it is worth saying explicitly because many
affiliate arrangements work differently.

Each commission is therefore a noisy observation of what a network paid at that moment. The noise is
small; the underlying behavior is what moves.

## Simulation boundaries

- Networks do not react to you. Nothing you route changes how any network behaves, and
  there is no competition for capacity.
- The fifteen networks, their advertised rates, and their coverage are stable for your
  whole run.
- Every candidate receives the same network configuration and time schedule, anchored to their own
  first API call.
- Commission draws are independent and unseeded, so individual outcomes differ.
