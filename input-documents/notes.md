## update research publisher

From each consuming project’s repository root, update/reinstall the package from the latest main branch:
npm install -D git+https://github.com/kemiller2002/research-publisher.git#main
Then ensure that project’s research-publisher.config.mjs uses its own domain:
site: {
title: "Your Project Research",
description: "Your project description",
baseUrl: "/",
siteUrl: "https://your-project.echelonfoundry.com/"
}
Rebuild and validate:
npm run research:validate
npm run research:build
If the project lacks the scripts, add these to package.json:
{
"scripts": {
"research:inventory": "research-publisher inventory --config ./research-publisher.config.mjs",
"research:validate": "research-publisher validate --config ./research-publisher.config.mjs",
"research:build": "research-publisher build --config ./research-publisher.config.mjs"
}
}
Optionally install the document-classification prompt:
npx research-publisher install-prompt --config ./research-publisher.config.mjs

## research operating system

Yes—the project name should change for each installation because it personalizes the charter, manifest, context, and pilot records.
Current usage:
npx --yes --prefer-online \
 --package=github:kemiller2002/repository-operating-system#main \
 ros-bootstrap init \
 --project "Communication Engineering" \
 --target .
For another project:

# Run inside the target repository

npx --yes --prefer-online \
 --package=github:kemiller2002/repository-operating-system#main \
 ros-bootstrap init \
 --project "Visual Engineering" \
 --target .
The CLI currently requires --project. A useful improvement would be to make it optional and derive the default from the target folder—for example, visual-engineering becomes Visual Engineering—while retaining --project as an override.

## reasearch publisher diagonstocis help prompt

Improve this project’s GitHub Actions integration with `research-publisher` so validation failures expose actionable diagnostics.

Inspect the existing workflow that runs the research-publisher build, then update it to:

1. Keep the existing build command and behavior.
2. When the build fails, read:
   `build-reports/build-diagnostics.json`
3. Print every diagnostic whose severity is `"error"` to the GitHub Actions log.
4. If `jq` is available, use:
   `jq '.diagnostics[] | select(.severity == "error")'`
   Otherwise provide a Node.js fallback.
5. Gracefully handle the diagnostics file not existing. Do not let the reporting step obscure or replace the original build failure.
6. Upload the entire `build-reports` directory as a GitHub Actions artifact when the build fails, if the workflow is permitted to use `actions/upload-artifact`.
7. Preserve the build’s nonzero exit status so the job still fails.
8. Avoid changing unrelated workflow behavior.

A suitable reporting step will resemble:

- name: Show research-publisher diagnostics
  if: failure()
  shell: bash
  run: |
  diagnostics_file="build-reports/build-diagnostics.json"
  if [ -f "$diagnostics_file" ]; then
  if command -v jq >/dev/null 2>&1; then
  jq '.diagnostics[] | select(.severity == "error")' "$diagnostics_file"
      else
        node -e '
          const fs = require("node:fs");
          const report = JSON.parse(fs.readFileSync(process.argv[1], "utf8"));
          const errors = (report.diagnostics || []).filter(
            diagnostic => diagnostic.severity === "error"
          );
          console.log(JSON.stringify(errors, null, 2));
        ' "$diagnostics_file"
  fi
  else
  echo "research-publisher did not produce $diagnostics_file"
  fi

Also add, when appropriate:

- name: Upload research-publisher diagnostics
  if: failure()
  uses: actions/upload-artifact@v4
  with:
  name: research-publisher-diagnostics
  path: build-reports/
  if-no-files-found: warn

After editing, validate the workflow syntax and summarize exactly which workflow file and steps were changed.
