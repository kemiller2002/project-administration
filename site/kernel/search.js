// Limen wiring for the knowledge-hub search: the only site script that
// touches the browser. It reads the card index the build embedded in the page
// and starts the kernel; every decision lives in the engine.
import { BrowserKernel } from "@echelon-foundry/limen";
import { createSearchTransport } from "../engine/search.js";

const index = document.getElementById("search-index");
const cards = index ? JSON.parse(index.textContent ?? "[]") : [];

await new BrowserKernel(createSearchTransport(cards), document).start();
