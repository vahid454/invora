import type {APIRequestContext} from '@playwright/test';
import {readFileSync,writeFileSync,chmodSync} from 'node:fs';
import {createHash} from 'node:crypto';
interface OwnerSession {accessToken:string;expiresAtUtc:string;user:{id:string;branchIds:string[]}}
// Reuse the fixture API session; each browser still exercises the real login flow.
// This keeps a fast suite within the application's normal authentication rate limit.
export async function ownerSession(request:APIRequestContext,setup:Record<string,string>):Promise<OwnerSession>{
 const environmentPath=process.env['INVORA_E2E_ENV'];if(!environmentPath)throw new Error('An isolated test environment is required.');
 const config:Record<string,string>=JSON.parse(readFileSync(environmentPath,'utf8'));const fingerprint=createHash('sha256').update(config['Auth__BootstrapKey']+String(process.env['INVORA_WEB_URL']??'http://127.0.0.1:4200')+setup['ownerLogin']).digest('hex');const path=environmentPath+'.owner-session.json';
 try{const cached=JSON.parse(readFileSync(path,'utf8')) as {fingerprint:string;session:OwnerSession};if(cached.fingerprint===fingerprint&&Date.parse(cached.session.expiresAtUtc)>Date.now()+60000)return cached.session;}catch{/* A new disposable environment needs its own fixture session. */}
 const registered=await request.post('/api/v1/auth/register',{headers:{'X-Invora-Bootstrap':config['Auth__BootstrapKey']},data:setup});const response=registered.ok()?registered:await request.post('/api/v1/auth/login',{data:{login:setup['ownerLogin'],password:setup['password']}});if(!response.ok())throw new Error('Fixture owner authentication failed with status '+response.status());const session=await response.json() as OwnerSession;writeFileSync(path,JSON.stringify({fingerprint,session}),{mode:0o600});chmodSync(path,0o600);return session;
}
