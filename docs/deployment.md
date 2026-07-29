# Showing the knowledge hub

The build output is a static site in `dist/`. Publish that directory on the
organization's approved static hosting service.

## Recommended pipeline

1. Run `python3 scripts/knowledge.py validate` for every pull request.
2. Run `python3 scripts/knowledge.py build` after merge to the default branch.
3. Upload the complete `dist/` directory as one immutable release.
4. Point a stable internal URL, such as `knowledge.example.com`, at that release.
5. Keep prior releases available for fast rollback.
6. Apply authentication, access logs, retention, and security headers at the
   hosting layer.

The portal has no server-side runtime, cookies, or third-party scripts. Its
search runs locally in the reader's browser. If content is confidential, the
host still must require authentication because static does not mean public.

## Hosting choices

| Environment | Good fit | Deployment unit |
|---|---|---|
| GitHub Pages | Repository-visible documentation | `dist/` artifact |
| Object storage + CDN | Enterprise control and high availability | Versioned directory |
| Internal web server | Restricted networks | Copied `dist/` directory |
| Existing developer portal | One front door already exists | Embed or proxy `dist/` |

Prefer the platform the organization already operates. The content and
machine-readable index remain portable across all four choices.

## Local preview

```sh
python3 scripts/knowledge.py build
python3 -m http.server 8000 --directory dist
```

Open <http://localhost:8000>. Do not open generated HTML directly from the file
system; serving it locally matches hosted URL behavior.

## Publishing contract

- `/index.html` is the human entry point.
- `/knowledge-index.json` is the integration entry point.
- Content URLs are stable and must not be changed casually.
- A deployment is successful only when the home page, one content page, site
  assets, and the JSON index all return successfully.
