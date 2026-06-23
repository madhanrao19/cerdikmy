// cerdikMY service worker — installability + offline fallback for the Blazor Server app.
// The app needs its SignalR circuit for interactivity, so we don't attempt full offline use:
// instead we precache the app shell + static assets, serve a friendly offline page when a
// navigation fails, and cache-first the static files so repeat loads are fast.

const CACHE = 'cerdikmy-v1';
const PRECACHE = [
  '/offline.html',
  '/css/app.css',
  '/icon.svg',
  '/manifest.webmanifest',
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE)
      // Cache each asset independently so one missing file can't abort the whole install.
      .then((cache) => Promise.allSettled(PRECACHE.map((url) => cache.add(url))))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;

  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return; // let cross-origin (media, APIs) pass through

  // Never cache the Blazor framework negotiate/SignalR endpoints.
  if (url.pathname.startsWith('/_blazor')) return;

  // Navigations: network-first, fall back to the offline page when disconnected.
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req).catch(() => caches.match('/offline.html'))
    );
    return;
  }

  // Static assets: cache-first, then network (and populate the cache).
  if (/\.(?:css|js|svg|png|jpg|jpeg|webp|woff2?|ico|json|webmanifest)$/.test(url.pathname)) {
    event.respondWith(
      caches.match(req).then((cached) =>
        cached || fetch(req).then((resp) => {
          if (resp.ok) {
            const copy = resp.clone();
            caches.open(CACHE).then((cache) => cache.put(req, copy));
          }
          return resp;
        }).catch(() => cached)
      )
    );
  }
});
