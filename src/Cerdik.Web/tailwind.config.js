/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Components/**/*.{razor,html,cs}",
    "./wwwroot/index.html"
  ],
  theme: {
    extend: {
      colors: {
        // cerdikMY brand palette (KPM-inspired: deep blue + warm accent)
        brand: {
          50: "#eef5ff",
          100: "#d9e8ff",
          200: "#bcd6ff",
          300: "#8ebcff",
          400: "#5996ff",
          500: "#3470f4",
          600: "#2152d9",
          700: "#1c41af",
          800: "#1d3a8a",
          900: "#1d356f",
          950: "#152149"
        }
      }
    }
  },
  plugins: []
};
