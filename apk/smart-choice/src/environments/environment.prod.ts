const apiBaseUrl = (window.__env?.API_BASE_URL ?? 'http://localhost:5148').replace(/\/+$/, '');

export const environment = {
  production: true,
  apiBaseUrl
};
