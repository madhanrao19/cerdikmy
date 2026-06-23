// Registers the cerdikMY service worker for installability + offline fallback.
// Kept as a static file so it satisfies the strict CSP (script-src 'self'; no inline).
if ('serviceWorker' in navigator) {
    window.addEventListener('load', function () {
        navigator.serviceWorker.register('/service-worker.js').catch(function (err) {
            console.warn('Service worker registration failed:', err);
        });
    });
}
