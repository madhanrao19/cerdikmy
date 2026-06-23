// Text-to-speech (read aloud) via the browser Web Speech API. Kept as a static file so it
// satisfies the strict CSP (script-src 'self'; no inline). No-op where the API is unavailable.
window.cerdikSpeak = function (text, lang) {
    if (!('speechSynthesis' in window) || !text) return false;
    window.speechSynthesis.cancel();
    var u = new SpeechSynthesisUtterance(text);
    if (lang) u.lang = lang;
    u.rate = 0.95;
    window.speechSynthesis.speak(u);
    return true;
};

window.cerdikStopSpeak = function () {
    if ('speechSynthesis' in window) window.speechSynthesis.cancel();
};
