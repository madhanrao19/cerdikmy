# Self-hosted Public Sans

cerdikMY uses **Public Sans** (the cerdikMY design-system typeface) **self-hosted** — there is no
external font CDN call at runtime (better for privacy/PDPA and offline/on-prem deployments).

`wwwroot/css/app.css` declares `@font-face` rules pointing at the files below. Until you add them,
the browser gracefully falls back to the system sans-serif stack — no network request either way.

## Add the font files

Download the Public Sans woff2 weights and place them here with these exact names:

| Weight | File name |
| ------ | --------- |
| 400 (Regular)  | `public-sans-400.woff2` |
| 500 (Medium)   | `public-sans-500.woff2` |
| 600 (SemiBold) | `public-sans-600.woff2` |
| 700 (Bold)     | `public-sans-700.woff2` |
| 800 (ExtraBold)| `public-sans-800.woff2` |

Easiest source is the `@fontsource/public-sans` package (MIT/OFL):

```bash
npm i @fontsource/public-sans
# copy the latin weights into this folder, renaming to the names above, e.g.:
cp node_modules/@fontsource/public-sans/files/public-sans-latin-400-normal.woff2 \
   src/Cerdik.Web/wwwroot/fonts/public-sans-400.woff2
# …repeat for 500/600/700/800
```

Or download from the Public Sans GitHub release / Google Fonts and convert to woff2.

Public Sans is licensed under the SIL Open Font License 1.1.
