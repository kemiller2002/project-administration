// Limen engine for the knowledge-hub search.
//
// Owns the one piece of application state on the page — the search query —
// and projects which cards are visible from it. It imports nothing and never
// touches a browser API; the kernel moves plain data across the boundary.

/** @typedef {{ readonly key: string, readonly text: string }} Card */
/** @typedef {{ readonly query: string }} State */

/** @param {string} query */
export const termsOf = (query) =>
  query.toLowerCase().trim().split(/\s+/).filter(Boolean);

/** @param {readonly string[]} terms @param {Card} card */
export const matches = (terms, card) => {
  const haystack = card.text.toLowerCase();
  return terms.every((term) => haystack.includes(term));
};

/** The `q` parameter of a query string such as `?q=ros+work`. @param {string} search */
export const queryFromLocation = (search) =>
  search
    .replace(/^\?/, "")
    .split("&")
    .map((pair) => pair.split("="))
    .filter(([name]) => name === "q")
    .map(([, value = ""]) => decodeURIComponent(value.replace(/\+/g, " ")))
    .concat([""])[0];

/** View key that drives one card's `hidden` binding. @param {string} key */
export const cardViewKey = (key) => `card:${key}`;

/** @param {number} visible @param {number} total @param {string} query */
const statusText = (visible, total, query) =>
  query.trim() === ""
    ? `${total} ${total === 1 ? "page" : "pages"}`
    : `${visible} of ${total} ${total === 1 ? "page matches" : "pages match"}`;

/**
 * Pure: state and one browser message in, next state out.
 * @param {State} state
 * @param {any} message
 * @returns {State}
 */
export const transition = (state, message) => {
  switch (message.kind) {
    case "Initialize":
      return { query: queryFromLocation(message.location.query) };
    case "Event":
      switch (message.event.name) {
        case "search": return { query: message.event.value ?? "" };
        case "clear": return { query: "" };
        // A data-event nothing answers is a wiring bug; fail loudly.
        default: throw new Error(`Unrecognized event: ${message.event.name}`);
      }
    // The page requests no effects and does not route, so these leave the
    // query unchanged.
    case "LocationChanged":
    case "EffectResult":
      return state;
    default:
      throw new Error(`Unrecognized message: ${message.kind}`);
  }
};

/**
 * Pure: state in, view out. Visibility, the empty state and the status line
 * are all projected here; the DOM never re-derives them.
 * @param {readonly Card[]} cards
 * @param {State} state
 */
export const project = (cards, state) => {
  const terms = termsOf(state.query);
  const shown = cards.map((card) => [card.key, matches(terms, card)]);
  const visible = shown.filter(([, isShown]) => isShown).length;
  return Object.fromEntries([
    ["query", state.query],
    ["clearDisabled", state.query === ""],
    ["emptyHidden", visible !== 0],
    ["status", statusText(visible, cards.length, state.query)],
    ...shown.map(([key, isShown]) => [cardViewKey(key), !isShown]),
  ]);
};

/**
 * The engine↔kernel contract for the search page.
 * @param {readonly Card[]} cards
 */
export const createSearchTransport = (cards) => {
  let state = { query: "" };
  return {
    async start() {},
    async dispatch(message) {
      state = transition(state, message);
      return { view: project(cards, state), effects: [], cancellations: [] };
    },
  };
};
