/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Components/**/*.{razor,html,cs}",
    "./wwwroot/index.html"
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ['"Public Sans"', "ui-sans-serif", "system-ui", "sans-serif"]
      },
      borderRadius: {
        DEFAULT: "8px"
      },
      colors: {
        // cerdikMY brand palette: primary blue #2152D9 + Cerdik Teal accent #14B8A6
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
        },
        accent: {
          50: "#effcf9",
          100: "#cbfbef",
          400: "#2dd4bf",
          500: "#14b8a6",
          600: "#0d9488",
          700: "#0f766e"
        }
      }
    }
  },
  plugins: []
};
