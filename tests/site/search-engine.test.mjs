// Pure tests of the knowledge-hub Limen engine: no browser, no DOM.
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  cardViewKey,
  createSearchTransport,
  project,
  queryFromLocation,
  transition,
} from "../../site/engine/search.js";

const cards = [
  { key: "ros-work", text: "ROS work items Praxis" },
  { key: "inventory", text: "Installation inventory Project Administration" },
];

const initialize = (query) => ({
  kind: "Initialize",
  protocolVersion: 1,
  capabilities: [],
  location: { path: "/index.html", query, hash: "" },
});
const event = (name, value) => ({ kind: "Event", event: { kind: "Event", name, value } });

test("an empty query shows every card and hides the empty state", () => {
  const view = project(cards, { query: "" });
  assert.equal(view[cardViewKey("ros-work")], false);
  assert.equal(view[cardViewKey("inventory")], false);
  assert.equal(view.emptyHidden, true);
  assert.equal(view.clearDisabled, true);
  assert.equal(view.status, "2 pages");
});

test("every term must match, case-insensitively", () => {
  const view = project(cards, { query: "  PRAXIS work " });
  assert.equal(view[cardViewKey("ros-work")], false);
  assert.equal(view[cardViewKey("inventory")], true);
  assert.equal(view.status, "1 of 2 pages match");
  assert.equal(view.clearDisabled, false);
});

test("no match projects the empty state", () => {
  const view = project(cards, { query: "nothing-here" });
  assert.equal(view.emptyHidden, false);
  assert.equal(view.status, "0 of 2 pages match");
});

test("the initial query comes from ?q= in the location", () => {
  assert.equal(queryFromLocation("?q=ros+work"), "ros work");
  assert.equal(queryFromLocation("?x=1&q=caf%C3%A9"), "café");
  assert.equal(queryFromLocation(""), "");
  assert.deepEqual(transition({ query: "" }, initialize("?q=inventory")), { query: "inventory" });
});

test("search and clear events move the query; unknown events are wiring bugs", () => {
  assert.deepEqual(transition({ query: "" }, event("search", "ros")), { query: "ros" });
  assert.deepEqual(transition({ query: "ros" }, event("clear")), { query: "" });
  assert.throws(() => transition({ query: "" }, event("submit")), /Unrecognized event: submit/);
});

test("location changes leave the query alone", () => {
  const state = { query: "ros" };
  const moved = { kind: "LocationChanged", location: { path: "/", query: "", hash: "" } };
  assert.equal(transition(state, moved), state);
});

test("the transport answers every message with a full projection and no effects", async () => {
  const transport = createSearchTransport(cards);
  await transport.start();
  const first = await transport.dispatch(initialize("?q=inventory"));
  assert.equal(first.view.query, "inventory");
  assert.deepEqual(first.effects, []);
  assert.deepEqual(first.cancellations, []);
  const second = await transport.dispatch(event("clear"));
  assert.equal(second.view.status, "2 pages");
});
