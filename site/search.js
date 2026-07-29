(() => {
  const input = document.querySelector("[data-search]");
  const cards = [...document.querySelectorAll("[data-search-text]")];
  if (!input || !cards.length) return;

  const apply = () => {
    const terms = input.value.toLowerCase().trim().split(/\s+/).filter(Boolean);
    let visible = 0;
    cards.forEach(card => {
      const haystack = card.dataset.searchText.toLowerCase();
      const show = terms.every(term => haystack.includes(term));
      card.hidden = !show;
      if (show) visible += 1;
    });
    const empty = document.querySelector("[data-empty]");
    if (empty) empty.hidden = visible !== 0;
  };

  input.addEventListener("input", apply);
  const params = new URLSearchParams(location.search);
  if (params.get("q")) {
    input.value = params.get("q");
    apply();
  }
})();
