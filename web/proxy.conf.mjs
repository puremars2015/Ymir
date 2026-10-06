// 開發 proxy：/api 與 OIDC callback（/signin-oidc）轉給 Ymir.Api。Aspire（WithReference(api)）會設定 services__api__http__0。
const target = process.env['services__api__http__0'] ?? 'http://localhost:5080';

export default {
  '/api': { target, secure: false, changeOrigin: false },
  '/signin-oidc': { target, secure: false, changeOrigin: false },
};
