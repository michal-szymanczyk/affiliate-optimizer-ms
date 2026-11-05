# Solution: Thompson sampling

The random choice is replaced by a **Thompson sampling** model, with random kept as the control.
The two are switchable mid-run from the header.

For every click the model:

1. Keeps only the networks that cover the click's merchant. Coverage is the one published fact.
2. Draws one plausible payout per network from its belief about that network.
3. Routes to the highest draw.

A network the model is sure about draws close to its estimate, so the best known network usually
wins: that is earning. A network it is unsure about draws from a wide range and sometimes wins:
that is learning.

## Why it fits this problem

| Property of the problem | How Thompson sampling handles it |
| --- | --- |
| The advertised rate is a claim | It is only the starting guess, held loosely. One real result outweighs it. |
| Results are a minute late | The draws are random, so the twenty or so decisions made before any result arrives do not all go to the same network. Clicks already on their way count as doubt about to be settled, so a network is not probed twice for one answer. |
| Payouts move and there is no counterfactual | Beliefs fade with time toward what the network usually pays, and doubt grows back toward how much it usually varies. A network that swings is looked at again within minutes; a steady one that is behind is left alone. |

## The belief, in one paragraph

One belief per network: an estimate of what it pays now, the doubt around it, and what it usually
pays across all its results. Time relaxes the
estimate toward the usual payout with a ten-minute time constant. The noise of each network is
learned from its own results, so a noisy network is not mistaken for one whose payout changed.
Code: [`thompson.ts`](frontend/src/models/thompson.ts) and [`belief.ts`](frontend/src/models/belief.ts).

## Results

| | Thompson | Random |
| --- | --- | --- |
| Simulation shaped like the real feed (share of the best possible payout) | about 0.81 | about 0.39 |
| Live run, about 11 minutes (commission per settled click) | $0.5766 | $0.2448 |

The simulation shows the mechanism works; it does not predict live earnings. The live figures are
one run each, back to back, under different network conditions, so they are an observation and not
a measurement. Screenshots are in [`example_run_results/`](example_run_results/).

## Around the model

- Every click records which model decided it, why, and whether it was exploring.
- A beliefs panel shows the model's whole state per network, so any decision can be checked.
- Every model learns from every disclosed commission, whichever model routed the click.
- If the deciding model fails, the click loop pauses instead of wasting clicks.
