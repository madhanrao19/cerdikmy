// Sets the ASP.NET Core culture cookie so RequestLocalization picks the chosen language
// on the next (forced) navigation. Kept in a static file so it satisfies the strict CSP
// (script-src 'self'; no inline/eval).
window.cerdikSetCulture = function (cookieValue) {
    document.cookie = ".AspNetCore.Culture=" + cookieValue + ";path=/;max-age=31536000;samesite=lax";
};
