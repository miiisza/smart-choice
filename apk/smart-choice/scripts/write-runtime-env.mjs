import { writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const defaultApiBaseUrl = 'http://localhost:5148';
const rawApiBaseUrl = process.env.IONIC_API_BASE_URL ?? defaultApiBaseUrl;
const apiBaseUrl = rawApiBaseUrl.trim().replace(/\/+$/, '');

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const envFilePath = resolve(scriptDirectory, '../src/assets/env.js');

const runtimeConfig = {
  API_BASE_URL: apiBaseUrl
};

const fileContent = `window.__env = Object.freeze(${JSON.stringify(runtimeConfig, null, 2)});\n`;
writeFileSync(envFilePath, fileContent, 'utf8');

console.log(`Runtime env written to ${envFilePath} (API_BASE_URL=${apiBaseUrl}).`);
