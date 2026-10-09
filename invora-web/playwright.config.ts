import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'./e2e',fullyParallel:false,workers:1,use:{baseURL:process.env['INVORA_WEB_URL']??'http://127.0.0.1:4200',trace:'retain-on-failure'},reporter:'list',outputDir:'../artifacts/browser-tests'});
