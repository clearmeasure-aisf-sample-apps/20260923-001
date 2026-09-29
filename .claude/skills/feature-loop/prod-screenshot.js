#!/usr/bin/env node
// Prod screenshot helper (Node + Playwright) for feature-loop evidence.
//
// Usage: node prod-screenshot.js <baseUrl> <out.png> [--testid <data-testid>] [--wait-testid <data-testid>]
//        [--login-path /login] [--viewport 1400x1000]
//   --testid       element captured (scrolled into view; the page is captured with it visible). Default: whole page.
//   --wait-testid  element that must exist after sign-in (default: same as --testid).
//
// Sign-in: opens <baseUrl>/login, picks the first real user in the dropdown, submits the form.
//
// TLS: verification is NEVER disabled. Chromium is started with
// --ignore-certificate-errors-spki-list holding only the public-key hashes of the agent-proxy
// CA certificates, computed at run time from the CA bundle named by SSL_CERT_FILE /
// NODE_EXTRA_CA_CERTS / REQUESTS_CA_BUNDLE / CURL_CA_BUNDLE (first that exists). Nothing is hard-coded.
//
// Playwright is resolved from the local install, PLAYWRIGHT_MODULE_PATH, the global npm root or /opt/node*.
// Chromium is taken from PLAYWRIGHT_CHROMIUM_EXECUTABLE, else <PLAYWRIGHT_BROWSERS_PATH>/chromium, else Playwright's own.
'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { execSync } = require('child_process');

const CA_SUBJECTS = [/CCR agent-proxy interception CA/i, /CCR Upstream Proxy CA/i, /sandbox-egress.*Egress Gateway CA/i, /TLS Inspection CA/i];

function loadPlaywright() {
  const candidates = [process.env.PLAYWRIGHT_MODULE_PATH, 'playwright'];
  try { candidates.push(path.join(execSync('npm root -g', { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim(), 'playwright')); } catch { /* npm absent */ }
  for (const dir of ['/opt/node22/lib/node_modules', '/usr/lib/node_modules', '/usr/local/lib/node_modules']) candidates.push(path.join(dir, 'playwright'));
  for (const candidate of candidates.filter(Boolean)) {
    try { return require(candidate); } catch { /* try next */ }
  }
  throw new Error('playwright module not found (set PLAYWRIGHT_MODULE_PATH)');
}

/** SPKI sha256 (base64) of every bundle certificate whose subject matches a proxy CA name. */
function proxyCaSpkiHashes() {
  const bundle = ['SSL_CERT_FILE', 'NODE_EXTRA_CA_CERTS', 'REQUESTS_CA_BUNDLE', 'CURL_CA_BUNDLE']
    .map(name => process.env[name]).find(file => file && fs.existsSync(file));
  if (!bundle) throw new Error('no CA bundle: set SSL_CERT_FILE, NODE_EXTRA_CA_CERTS, REQUESTS_CA_BUNDLE or CURL_CA_BUNDLE');
  const pems = fs.readFileSync(bundle, 'utf8').match(/-----BEGIN CERTIFICATE-----[\s\S]*?-----END CERTIFICATE-----/g) || [];
  const hashes = new Set();
  for (const pem of pems) {
    let cert;
    try { cert = new crypto.X509Certificate(pem); } catch { continue; }
    if (!CA_SUBJECTS.some(pattern => pattern.test(cert.subject))) continue;
    const spki = cert.publicKey.export({ type: 'spki', format: 'der' });
    hashes.add(crypto.createHash('sha256').update(spki).digest('base64'));
  }
  if (hashes.size === 0) throw new Error(`no agent-proxy CA certificate found in ${bundle}`);
  return [...hashes];
}

function parseArgs(argv) {
  const positional = [];
  const options = { loginPath: '/login', viewport: '1400x1000' };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--testid') options.testid = argv[++i];
    else if (arg === '--wait-testid') options.waitTestid = argv[++i];
    else if (arg === '--login-path') options.loginPath = argv[++i];
    else if (arg === '--viewport') options.viewport = argv[++i];
    else positional.push(arg);
  }
  if (positional.length < 2) throw new Error('usage: prod-screenshot.js <baseUrl> <out.png> [--testid id] [--wait-testid id]');
  const [width, height] = options.viewport.split('x').map(Number);
  return { base: positional[0].replace(/\/+$/, ''), out: positional[1], ...options, width, height };
}

function chromiumExecutable() {
  if (process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE) return process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE;
  const link = process.env.PLAYWRIGHT_BROWSERS_PATH && path.join(process.env.PLAYWRIGHT_BROWSERS_PATH, 'chromium');
  return link && fs.existsSync(link) ? link : undefined;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const { chromium } = loadPlaywright();
  const spki = proxyCaSpkiHashes();
  const browser = await chromium.launch({
    executablePath: chromiumExecutable(),
    args: [`--ignore-certificate-errors-spki-list=${spki.join(',')}`],
  });
  try {
    const page = await browser.newPage({ viewport: { width: args.width, height: args.height } });
    await page.goto(args.base + args.loginPath, { waitUntil: 'networkidle', timeout: 120000 });
    const select = page.locator('select').first();
    await select.waitFor({ timeout: 60000 });
    await page.waitForFunction(() => document.querySelector('select').options.length > 1, null, { timeout: 60000 });
    await select.selectOption({ index: 1 });
    await page.locator('button[type=submit]').first().click();
    const waitId = args.waitTestid || args.testid;
    if (waitId) await page.waitForSelector(`[data-testid=${waitId}]`, { timeout: 60000 });
    await page.waitForTimeout(1500);
    if (args.testid) {
      const element = page.locator(`[data-testid=${args.testid}]`).first();
      console.log(`${args.testid}: ${(await element.innerText()).trim().slice(0, 200)}`);
      await element.scrollIntoViewIfNeeded();
      await page.waitForTimeout(500);
    }
    await page.screenshot({ path: args.out });
    console.log(`screenshot: ${args.out} (${spki.length} proxy CA key(s) trusted)`);
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error.message); process.exit(1); });
