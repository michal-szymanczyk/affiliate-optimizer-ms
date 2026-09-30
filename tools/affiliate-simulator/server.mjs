// A local stand-in for the Affiliate Service, implementing the five endpoints of ../../openapi.json with the
// behaviours API.md describes: credential headers, token-bucket rate limits, two-minute click expiry, final
// routing with replay, rejections that are visible twice, and a cursor feed that discloses settlements one
// minute after routing. The hidden world is invented: some networks honour their advertised CPC, some do not,
// some are noisy, and some change behaviour on a schedule anchored to the first authenticated call.
//
//   node tools/affiliate-simulator/server.mjs
//   PORT=8787 APP_KEY=ak_local APP_SECRET=as_local DISCLOSURE_DELAY_MS=60000 SEED=7
//
// GET /_simulator/truth (no auth) reveals the hidden means so a walkthrough can compare what a model learned.
// This is NOT the real service and its numbers mean nothing outside this repository.

import http from 'node:http';
import crypto from 'node:crypto';

// Settings come from the environment or from --NAME=value arguments (e.g. --DISCLOSURE_DELAY_MS=8000).
const args = Object.fromEntries(process.argv.slice(2).filter((a) => a.startsWith('--') && a.includes('=')).map((a) => a.slice(2).split(/=(.*)/s).slice(0, 2)));
const setting = (name, fallback) => args[name] ?? process.env[name] ?? fallback;

const PORT = Number(setting('PORT', 8787));
const APP_KEY = setting('APP_KEY', 'ak_local');
const APP_SECRET = setting('APP_SECRET', 'as_local');
const DISCLOSURE_DELAY_MS = Number(setting('DISCLOSURE_DELAY_MS', 60_000));
const CLICK_TTL_MS = 120_000;
const FEED_PAGE = 100;
const SEED = Number(setting('SEED', 7));
const CURSOR_SECRET = crypto.randomBytes(16);

// ---- deterministic world -------------------------------------------------------------------------------

function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const rng = mulberry32(SEED);
const pick = (list) => list[Math.floor(rng() * list.length)];
const round4 = (n) => Math.round(n * 10_000) / 10_000;

const merchantWords = ['Acme', 'Beacon', 'Cobalt', 'Delta', 'Ember', 'Fable', 'Granite', 'Harbor', 'Indigo', 'Juniper', 'Kestrel', 'Lumen', 'Meadow', 'Nimbus', 'Orchid', 'Pioneer', 'Quartz', 'Ripple', 'Summit', 'Tundra', 'Umber', 'Vertex', 'Willow', 'Xenon', 'Yonder', 'Zephyr'];
const merchantKinds = ['Outfitters', 'Books', 'Kitchen', 'Bikes', 'Garden', 'Audio', 'Toys', 'Pets', 'Tools', 'Travel', 'Shoes', 'Home', 'Games', 'Coffee', 'Optics'];
const merchants = Array.from({ length: 100 }, (_, i) => ({ merchantId: i + 1, name: `${merchantWords[i % merchantWords.length]} ${merchantKinds[Math.floor(i / merchantWords.length) % merchantKinds.length]} ${Math.floor(i / 26) + 1}`.replace(/ 1$/, '') }));

const networkNames = ['vanguard', 'corsair', 'meridian', 'pelican', 'summit', 'harbor', 'lantern', 'quill', 'atlas', 'delta', 'ember', 'nomad', 'orbit', 'ridge', 'tidal'];
const networks = networkNames.map((name, index) => {
  const advertisedRate = round4(0.08 + rng() * 0.6);
  const coverage = new Set();
  const size = 25 + Math.floor(rng() * 45);
  while (coverage.size < size) coverage.add(1 + Math.floor(rng() * 100));
  // hidden behaviour: honest, generous, stingy, noisy or drifting
  const profile = pick(['honest', 'honest', 'generous', 'stingy', 'stingy', 'noisy', 'drifting', 'drifting']);
  const honesty = { honest: 0.95 + rng() * 0.1, generous: 1.1 + rng() * 0.3, stingy: 0.35 + rng() * 0.3, noisy: 0.8 + rng() * 0.3, drifting: 0.9 + rng() * 0.2 }[profile];
  const noise = profile === 'noisy' ? 0.35 : 0.06;
  const schedule = profile === 'drifting'
    ? [{ atMinute: 0, multiplier: 1 }, { atMinute: 6 + Math.floor(rng() * 6), multiplier: 0.4 + rng() * 0.3 }, { atMinute: 18 + Math.floor(rng() * 8), multiplier: 1.2 + rng() * 0.4 }]
    : [{ atMinute: 0, multiplier: 1 }];
  return { id: `net-${name}`, name: `${name[0].toUpperCase()}${name.slice(1)} Network`, advertisedRate, supportedMerchantIds: [...coverage].sort((a, b) => a - b), hidden: { profile, honesty, noise, schedule }, index };
});
for (const merchant of merchants) {
  if (!networks.some((n) => n.supportedMerchantIds.includes(merchant.merchantId))) {
    const n = networks[merchant.merchantId % networks.length];
    n.supportedMerchantIds.push(merchant.merchantId);
    n.supportedMerchantIds.sort((a, b) => a - b);
  }
}

// ---- state -------------------------------------------------------------------------------------------------

let anchor = null;                       // time of the first authenticated call
const clicks = new Map();                // clickId → record
const disclosed = [];                    // feed rows in disclosure order
const pendingSettlements = [];           // routed clicks whose settlement is not yet disclosed
let clickSequence = 0;

const buckets = {
  next: { capacity: 40, tokens: 40, refillPerMs: 20 / 60_000, last: Date.now() },
  commissions: { capacity: 60, tokens: 60, refillPerMs: 60 / 60_000, last: Date.now() },
};

function take(bucket) {
  const now = Date.now();
  bucket.tokens = Math.min(bucket.capacity, bucket.tokens + (now - bucket.last) * bucket.refillPerMs);
  bucket.last = now;
  if (bucket.tokens >= 1) { bucket.tokens -= 1; return null; }
  return round4((1 - bucket.tokens) / bucket.refillPerMs / 1000);
}

function multiplierAt(network, at) {
  const minutes = (at - anchor) / 60_000;
  let current = 1;
  for (const step of network.hidden.schedule) if (minutes >= step.atMinute) current = step.multiplier;
  return current;
}

function realizedCpc(network, at) {
  const base = network.advertisedRate * network.hidden.honesty * multiplierAt(network, at);
  const noise = 1 + (Math.random() * 2 - 1) * network.hidden.noise;   // deliberately unseeded, like the real thing
  return round4(Math.max(0.0001, base * noise));
}

function discloseDue(now) {
  pendingSettlements.sort((a, b) => a.availableAt - b.availableAt);
  while (pendingSettlements.length > 0 && pendingSettlements[0].availableAt <= now) {
    const click = pendingSettlements.shift();
    click.disclosedAt = new Date(now).toISOString();
    disclosed.push({ clickId: click.clickId, merchantId: click.merchantId, routedNetworkId: click.networkId, routedAt: click.routedAt, disclosedAt: click.disclosedAt, kind: 'settlement', state: 'settled', commission: click.commission, errorCode: null });
  }
}

function encodeCursor(index) {
  const payload = Buffer.from(JSON.stringify({ i: index })).toString('base64url');
  const sig = crypto.createHmac('sha256', CURSOR_SECRET).update(payload).digest('base64url').slice(0, 16);
  return `${payload}.${sig}`;
}

function decodeCursor(cursor) {
  const [payload, sig] = String(cursor).split('.');
  if (!payload || !sig) return null;
  const expected = crypto.createHmac('sha256', CURSOR_SECRET).update(payload).digest('base64url').slice(0, 16);
  if (sig !== expected) return null;
  try { const { i } = JSON.parse(Buffer.from(payload, 'base64url').toString()); return Number.isInteger(i) && i >= 0 ? i : null; } catch { return null; }
}

// ---- http ----------------------------------------------------------------------------------------------------

function send(res, status, body) {
  const json = JSON.stringify(body);
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'content-length': Buffer.byteLength(json) });
  res.end(json);
}

function readBody(req) {
  return new Promise((resolve) => {
    let data = '';
    req.on('data', (chunk) => { data += chunk; });
    req.on('end', () => resolve(data));
  });
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, `http://${req.headers.host}`);
  const path = url.pathname.replace(/\/+$/, '') || '/';

  if (path === '/_simulator/truth') {
    const now = Date.now();
    return send(res, 200, { anchor, networks: networks.map((n) => ({ id: n.id, advertisedRate: n.advertisedRate, profile: n.hidden.profile, currentMean: anchor ? round4(n.advertisedRate * n.hidden.honesty * multiplierAt(n, now)) : null, noise: n.hidden.noise, schedule: n.hidden.schedule })) });
  }

  if (req.headers['x-app-key'] !== APP_KEY || req.headers['x-app-secret'] !== APP_SECRET)
    return send(res, 401, { code: 'UNAUTHORIZED', message: 'Missing, malformed, or revoked credentials.' });

  const now = Date.now();
  anchor ??= now;

  if (req.method === 'GET' && path === '/merchants') return send(res, 200, { merchants });
  if (req.method === 'GET' && path === '/networks') return send(res, 200, { networks: networks.map(({ id, name, advertisedRate, supportedMerchantIds }) => ({ id, name, advertisedRate, supportedMerchantIds })) });

  if (req.method === 'POST' && path === '/clicks/next') {
    const retryAfter = take(buckets.next);
    if (retryAfter !== null) return send(res, 429, { code: 'RATE_LIMITED', message: 'Click issue throttle exceeded.', retryAfterSeconds: retryAfter });
    const merchant = merchants[Math.floor(Math.random() * merchants.length)];
    const click = { clickId: `clk_${String(++clickSequence).padStart(6, '0')}${crypto.randomBytes(2).toString('hex')}`, merchantId: merchant.merchantId, issuedAt: new Date(now).toISOString(), expiresAt: new Date(now + CLICK_TTL_MS).toISOString(), expiresAtMs: now + CLICK_TTL_MS };
    clicks.set(click.clickId, click);
    return send(res, 200, { clickId: click.clickId, merchantId: click.merchantId, issuedAt: click.issuedAt, expiresAt: click.expiresAt });
  }

  const route = path.match(/^\/clicks\/([^/]+)\/route$/);
  if (req.method === 'POST' && route) {
    const clickId = decodeURIComponent(route[1]);
    let body;
    try { body = JSON.parse((await readBody(req)) || 'null'); } catch { body = null; }
    if (!body || typeof body.networkId !== 'string' || body.networkId.length === 0)
      return send(res, 400, { code: 'INVALID_REQUEST', message: 'Body must be { "networkId": string }.' });
    const click = clicks.get(clickId);
    if (!click) return send(res, 404, { code: 'CLICK_NOT_FOUND', message: `Unknown click ${clickId}.` });
    const network = networks.find((n) => n.id === body.networkId);
    if (!network) return send(res, 404, { code: 'NETWORK_NOT_FOUND', message: `Unknown network ${body.networkId}.` });

    if (click.routing) return send(res, click.routing.status, { ...click.routing.body, alreadyRouted: true });

    const routedAt = new Date(now).toISOString();
    let status; let responseBody;
    if (now > click.expiresAtMs) {
      status = 410;
      responseBody = { outcome: 'rejected', clickId, networkId: network.id, routedAt, alreadyRouted: false, errorCode: 'CLICK_EXPIRED', message: 'The click expired before it was routed.' };
    } else if (!network.supportedMerchantIds.includes(click.merchantId)) {
      status = 422;
      responseBody = { outcome: 'rejected', clickId, networkId: network.id, routedAt, alreadyRouted: false, errorCode: 'MERCHANT_NOT_SUPPORTED', message: `${network.id} does not cover merchant ${click.merchantId}.` };
    } else {
      status = 200;
      const availableAt = now + DISCLOSURE_DELAY_MS;
      responseBody = { outcome: 'accepted', clickId, networkId: network.id, routedAt, alreadyRouted: false, commissionAvailableAt: new Date(availableAt).toISOString() };
      Object.assign(click, { networkId: network.id, routedAt, commission: realizedCpc(network, now), availableAt });
      pendingSettlements.push(click);
    }
    click.routing = { status, body: responseBody };
    if (status !== 200) {
      disclosed.push({ clickId, merchantId: click.merchantId, routedNetworkId: network.id, routedAt, disclosedAt: routedAt, kind: 'rejection', state: 'rejected', commission: null, errorCode: responseBody.errorCode });
    }
    return send(res, status, responseBody);
  }

  if (req.method === 'GET' && path === '/commissions') {
    const retryAfter = take(buckets.commissions);
    if (retryAfter !== null) return send(res, 429, { code: 'RATE_LIMITED', message: 'Commission feed throttle exceeded.', retryAfterSeconds: retryAfter });
    const cursor = url.searchParams.get('cursor');
    let index = 0;
    if (cursor !== null) {
      const decoded = decodeCursor(cursor);
      if (decoded === null) return send(res, 400, { code: 'INVALID_CURSOR', message: 'Malformed or tampered cursor token.' });
      index = Math.min(decoded, disclosed.length);
    }
    discloseDue(now);
    const rows = disclosed.slice(index, index + FEED_PAGE);
    const nextIndex = index + rows.length;
    return send(res, 200, { commissions: rows, nextCursor: nextIndex === 0 ? null : encodeCursor(nextIndex) });
  }

  return send(res, 404, { code: 'CLICK_NOT_FOUND', message: `No such endpoint: ${req.method} ${path}` });
});

server.listen(PORT, () => {
  console.log(`Affiliate Service simulator on http://localhost:${PORT} (key ${APP_KEY}, disclosure delay ${DISCLOSURE_DELAY_MS} ms, seed ${SEED})`);
  console.log(`Networks: ${networks.map((n) => `${n.id}=${n.hidden.profile}`).join(', ')}`);
});
