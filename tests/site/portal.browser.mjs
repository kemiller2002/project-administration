// Drives the built portal (dist/) in Chromium: Limen search, Forma patterns,
// Folio print layout. Run after `python3 scripts/knowledge.py build`.
// Uses PLAYWRIGHT_CHROMIUM_EXECUTABLE when set, otherwise Playwright's own.
import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
import assert from "node:assert/strict";
import { chromium } from "playwright";

const root = join(import.meta.dirname, "../../dist");
const types = { ".html": "text/html", ".css": "text/css", ".js": "text/javascript", ".json": "application/json" };

const server = createServer(async (request, response) => {
  const path = normalize(decodeURIComponent(new URL(request.url, "http://x").pathname)).replace(/^\/+/, "");
  try {
    const body = await readFile(join(root, path || "index.html"));
    response.writeHead(200, { "content-type": types[extname(path)] ?? "application/octet-stream" }).end(body);
  } catch {
    response.writeHead(404).end();
  }
});
await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
const base = `http://127.0.0.1:${server.address().port}`;

const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE || undefined });
const failures = [];
const check = async (name, body) => {
  const page = await browser.newPage();
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (message) => message.type() === "error" && errors.push(message.text()));
  try {
    await body(page);
    assert.deepEqual(errors, [], "no page errors");
    console.log(`ok - ${name}`);
  } catch (error) {
    failures.push(name);
    console.log(`not ok - ${name}\n  ${error.message}`);
  } finally {
    await page.close();
  }
};
const visibleCards = (page) => page.locator("a.card:not([hidden])").count();

await check("Forma styles the page and Limen starts the search", async (page) => {
  await page.goto(`${base}/index.html`);
  await page.locator(".ef-search__status").filter({ hasText: /\d+ pages?/ }).waitFor();
  const layer = await page.evaluate(() =>
    [...document.styleSheets].some((sheet) => sheet.href?.endsWith("/assets/@echelon-foundry/design-system/all.css") && sheet.cssRules.length > 0));
  assert.ok(layer, "Forma all.css loaded");
  assert.equal(await page.locator("ef-search form.ef-search[role=search]").count(), 1);
  assert.equal(await page.locator(".ef-search__clear").isDisabled(), true);
});

await check("typing filters cards through the engine; clear restores them", async (page) => {
  await page.goto(`${base}/index.html`);
  await page.locator(".ef-search__status").filter({ hasText: /pages?$/ }).waitFor();
  const total = await visibleCards(page);
  assert.ok(total > 1, "several cards");
  await page.fill("#site-search", "zzzz-no-such-term");
  await page.locator(".ef-empty-state:not([hidden])").waitFor();
  assert.equal(await visibleCards(page), 0);
  await page.click(".ef-search__clear");
  await page.locator(".ef-empty-state[hidden]").waitFor({ state: "attached" });
  assert.equal(await visibleCards(page), total);
  assert.equal(await page.inputValue("#site-search"), "");
});

await check("?q= on the home page seeds the query", async (page) => {
  await page.goto(`${base}/index.html?q=zzzz-no-such-term`);
  await page.locator(".ef-empty-state:not([hidden])").waitFor();
  assert.equal(await page.inputValue("#site-search"), "zzzz-no-such-term");
});

await check("article pages submit search to the home page natively", async (page) => {
  await page.goto(`${base}/index.html`);
  const href = await page.locator("a.card").first().getAttribute("href");
  await page.goto(`${base}/${href}`);
  assert.equal(await page.locator("script[type=module]").count(), 0, "no kernel on article pages");
  await page.fill("#site-search", "zzzz-no-such-term");
  await page.press("#site-search", "Enter");
  await page.waitForURL(/index\.html\?q=zzzz-no-such-term/);
  await page.locator(".ef-empty-state:not([hidden])").waitFor();
});

await check("Folio lays out the printed article", async (page) => {
  await page.goto(`${base}/index.html`);
  const href = await page.locator("a.card").first().getAttribute("href");
  await page.goto(`${base}/${href}`);
  assert.equal(await page.locator("ef-print-document ef-print-section article.content").count(), 1);
  assert.equal(await page.locator("ef-print-header").isVisible(), false, "print header hidden on screen");
  await page.emulateMedia({ media: "print" });
  assert.equal(await page.locator("ef-print-header").isVisible(), true, "print header shown on paper");
  assert.equal(await page.locator(".site-header").isVisible(), false, "site chrome hidden on paper");
  const display = await page.locator("ef-print-section").evaluate((el) => getComputedStyle(el).display);
  assert.equal(display, "block", "Folio print.css applied");
  const pdf = await page.pdf();
  assert.ok(pdf.length > 1000, "renders to PDF");
});

await browser.close();
server.close();
if (failures.length) {
  console.log(`\n${failures.length} failed`);
  process.exit(1);
}
console.log("\nall portal checks passed");
