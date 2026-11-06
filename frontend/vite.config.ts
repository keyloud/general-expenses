import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    // Браузер обращается к тому же адресу /api, Vite пересылает запрос в ASP.NET Core.
    // Это настройка разработки. При публикации понадобится аналогичный reverse proxy.
    proxy: { '/api': 'http://127.0.0.1:5080' },
  },
});
