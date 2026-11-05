/**
 * Randomness helpers.
 *
 * Everything random in the client goes through an injected `() => number` returning a value in
 * [0, 1), the same shape as Math.random, so a test can pin it and a simulation can seed it.
 */

/**
 * One draw from a standard normal distribution, by the Box–Muller transform.
 *
 * It always consumes exactly two uniforms, which keeps a scripted random source easy to reason
 * about: a first uniform of 0 gives a draw of exactly 0 whatever the second is, because the radius
 * is sqrt(-2 ln(1 - 0)) = 0.
 *
 * The first uniform is flipped to (0, 1] before the logarithm. Math.random can return exactly 0,
 * and ln(0) would make the draw infinite.
 */
export function standardNormal(random: () => number): number {
  const first = random()
  const second = random()
  if (!(first >= 0 && first < 1 && second >= 0 && second < 1)) {
    throw new Error(
      `standardNormal: the injected random function returned ${first} and ${second}. It must return ` +
        `values in [0, 1).`,
    )
  }
  return Math.sqrt(-2 * Math.log(1 - first)) * Math.cos(2 * Math.PI * second)
}

/**
 * A small seeded generator (mulberry32) with the same shape as Math.random.
 *
 * For tests and simulations: the same seed gives the same run, which Math.random cannot. Not for
 * anything that needs to be unpredictable.
 */
export function seededRandom(seed: number): () => number {
  let state = seed | 0
  return () => {
    state = (state + 0x6d2b79f5) | 0
    let mixed = Math.imul(state ^ (state >>> 15), 1 | state)
    mixed = (mixed + Math.imul(mixed ^ (mixed >>> 7), 61 | mixed)) ^ mixed
    return ((mixed ^ (mixed >>> 14)) >>> 0) / 4_294_967_296
  }
}
