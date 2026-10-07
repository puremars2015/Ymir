// Vibe Maker 的 service worker：只負責「可安裝」與靜態資源加速。
// 安全：登入狀態是 HttpOnly cookie（ADR-0002），所以 /api（含 SSE、登入、下載）一律不經過快取，
// 也不快取導覽請求（index.html），避免登入狀態或舊版畫面被留在裝置上。
const CACHE = 'ymir-static-v1';

// 檔名含雜湊的 build 產物（chunk-XXXX.js、main-XXXX.js、styles-XXXX.css）內容不變，可安全 cache-first。
const HASHED_ASSET = /^\/[\w.-]+-[\w-]{8}\.(?:js|css)$/;

self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') return;
  const url = new URL(request.url);
  if (url.origin !== self.location.origin || !HASHED_ASSET.test(url.pathname)) return;

  event.respondWith(
    caches.open(CACHE).then(async (cache) => {
      const hit = await cache.match(request);
      if (hit) return hit;
      const response = await fetch(request);
      if (response.ok) cache.put(request, response.clone());
      return response;
    }),
  );
});
